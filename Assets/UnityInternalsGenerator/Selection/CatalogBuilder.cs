namespace DandaUtils.Editor.UnityInternalsGenerator.Selection
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;
    using DandaUtils.Editor.UnityInternalsGenerator.Rules;

    /// <summary>Builds the selection <see cref="Catalog"/> from a classified context. Classification is selection-agnostic.</summary>
    public static class CatalogBuilder
    {
        private const string c_GlobalNamespaceName = "<global>";
        private const string c_CompilerGeneratedRule = "CompilerGenerated";

        public static Catalog Build(GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            ctx.ClassifyAll();
            var nodes = new List<CatalogNode>();
            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            var types = ctx.ClassifiedTypes.Where(t => ctx.Classify(t).RuleName != c_CompilerGeneratedRule).ToList();
            var typeNodes = new HashSet<Type>(types);

            foreach (var t in types)
            {
                var status = ctx.Classify(t);
                nodes.Add(new CatalogNode(MemberId.Type(t), ParentId(t, typeNodes, namespaces, nodes), CSharpNames.TypeDeclName(t), TypeKind(t), status.Status, status.Reason, null, true, t.DeclaringType == null));
                if (status.Status != WrapStatus.Wrapped || t.IsEnum) continue;

                foreach (var verdict in ctx.MemberVerdicts(t)) nodes.Add(MemberNode(verdict, t, ctx));
            }

            return new Catalog(nodes);
        }

        private static string ParentId(Type t, HashSet<Type> typeNodes, HashSet<string> namespaces, List<CatalogNode> nodes) => t.DeclaringType != null && typeNodes.Contains(t.DeclaringType) ? MemberId.Type(t.DeclaringType) : NamespaceNodeId(t.Namespace, namespaces, nodes);

        private static string NamespaceNodeId(string ns, HashSet<string> seen, List<CatalogNode> nodes)
        {
            var id = SelectionIds.Namespace(ns);
            for (var current = id; current != null && seen.Add(current); current = SelectionIds.NamespaceParent(current))
            {
                var name = SelectionIds.NamespaceName(current);
                var dot = name.LastIndexOf('.');
                var display = name.Length == 0 ? c_GlobalNamespaceName : name.Substring(dot + 1);
                nodes.Add(new CatalogNode(current, SelectionIds.NamespaceParent(current), display, NodeKind.Namespace, WrapStatus.Wrapped, null, null, false, false));
            }
            return id;
        }

        private static NodeKind TypeKind(Type t)
        {
            if (t.IsEnum) return NodeKind.Enum;
            if (t.IsValueType) return NodeKind.Struct;
            if (t.IsInterface) return NodeKind.Interface;
            if (t.IsClass == false || typeof(Delegate).IsAssignableFrom(t)) return NodeKind.Other;
            return t.IsAbstract && t.IsSealed ? NodeKind.StaticClass : NodeKind.Class;
        }

        private static CatalogNode MemberNode(MemberVerdict verdict, Type owner, GenerationContext ctx)
        {
            var dependencies = verdict.Status == WrapStatus.Wrapped ? Dependencies(verdict.Member, ctx) : null;
            return new CatalogNode(MemberId.Member(verdict.Member), MemberId.Type(owner), verdict.Member.Name, MemberKind(verdict.Member), verdict.Status, verdict.Reason, dependencies, false, false);
        }

        private static NodeKind MemberKind(MemberInfo m)
        {
            switch (m)
            {
                case MethodInfo _: return NodeKind.Method;
                case ConstructorInfo _: return NodeKind.Constructor;
                case PropertyInfo p: return p.GetIndexParameters().Length > 0 ? NodeKind.Indexer : NodeKind.Property;
                case FieldInfo f: return f.IsLiteral ? NodeKind.Const : NodeKind.Field;
                default: return NodeKind.Other;
            }
        }

        private static IReadOnlyList<string> Dependencies(MemberInfo m, GenerationContext ctx)
        {
            var signature = new List<Type>();
            switch (m)
            {
                case MethodInfo method:
                    signature.Add(method.ReturnType);
                    signature.AddRange(method.GetParameters().Select(p => p.ParameterType));
                    break;
                case ConstructorInfo ctor:
                    signature.AddRange(ctor.GetParameters().Select(p => p.ParameterType));
                    break;
                case PropertyInfo property:
                    signature.Add(property.PropertyType);
                    signature.AddRange(property.GetIndexParameters().Select(p => p.ParameterType));
                    break;
                case FieldInfo field:
                    signature.Add(field.FieldType);
                    break;
            }
            return signature.Where(ctx.NeedsWrapper).Select(MemberId.Type).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
        }
    }
}
