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

        /// <summary>Reason of public type nodes that only list their internal members, and of those member nodes.</summary>
        public const string PublicTypeInternalMembersReason = "PublicTypeInternalMembers (step 4)";

        private const BindingFlags c_DeclaredOnly = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        public static Catalog Build(GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            ctx.ClassifyAll();
            var nodes = new List<CatalogNode>();
            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            var types = ctx.ClassifiedTypes.Where(t => ctx.Classify(t).RuleName != c_CompilerGeneratedRule).ToList();
            var publicTypes = PublicTypesWithInternals(ctx, types);
            var typeNodes = new HashSet<Type>(types);
            typeNodes.UnionWith(publicTypes.Keys);

            foreach (var t in types)
            {
                var status = ctx.Classify(t);
                nodes.Add(new CatalogNode(MemberId.Type(t), ParentId(t, typeNodes, namespaces, nodes), CSharpNames.TypeDeclName(t), TypeKind(t), status.Status, status.Reason, null, true, t.DeclaringType == null));
                if (status.Status != WrapStatus.Wrapped || t.IsEnum) continue;

                foreach (var verdict in ctx.MemberVerdicts(t)) nodes.Add(MemberNode(verdict, t, ctx));
            }

            // Public types only list their internal members, all Deferred (never emitted) until public-type support exists.
            foreach (var pair in publicTypes)
            {
                var t = pair.Key;
                var typeId = MemberId.Type(t);
                nodes.Add(new CatalogNode(typeId, ParentId(t, typeNodes, namespaces, nodes), CSharpNames.TypeDeclName(t), TypeKind(t), WrapStatus.Deferred, PublicTypeInternalMembersReason, null, true, t.DeclaringType == null));
                foreach (var m in pair.Value) nodes.Add(new CatalogNode(MemberId.Member(m), typeId, m.Name, MemberKind(m), WrapStatus.Deferred, PublicTypeInternalMembersReason, null, false, false));
            }

            return new Catalog(nodes);
        }

        private static string ParentId(Type t, HashSet<Type> typeNodes, HashSet<string> namespaces, List<CatalogNode> nodes) => t.DeclaringType != null && typeNodes.Contains(t.DeclaringType) ? MemberId.Type(t.DeclaringType) : NamespaceNodeId(t.Namespace, namespaces, nodes);

        /// <summary>
        /// Public, non-compiler-generated source types that declare an accessible internal member, plus the public declaring types
        /// of every catalog type nested in them (so nested nodes keep their declaring chain). Value: the internal members (maybe empty).
        /// </summary>
        private static Dictionary<Type, List<MemberInfo>> PublicTypesWithInternals(GenerationContext ctx, IReadOnlyList<Type> internalTypes)
        {
            var result = new Dictionary<Type, List<MemberInfo>>();
            foreach (var assembly in ctx.Input.Sources)
            {
                foreach (var t in GenerationContext.SafeGetTypes(assembly))
                {
                    if (t.IsVisible == false || GenerationContext.IsCompilerGenerated(t)) continue;

                    var members = InternalMembers(t);
                    if (members.Count > 0) result[t] = members;
                }
            }

            foreach (var nested in internalTypes.Where(IsInternalNested).Concat(result.Keys.ToList()))
            {
                for (var declaring = nested.DeclaringType; declaring != null && declaring.IsVisible; declaring = declaring.DeclaringType)
                {
                    if (result.ContainsKey(declaring)) break;
                    result[declaring] = new List<MemberInfo>();
                }
            }

            return result;
        }

        private static bool IsInternalNested(Type t) => t.IsNestedAssembly || t.IsNestedFamORAssem;

        /// <summary>Declared-only accessible-internal members; accessor methods, static ctors and compiler-generated members are skipped.</summary>
        private static List<MemberInfo> InternalMembers(Type t)
        {
            var result = new List<MemberInfo>();
            var accessors = new HashSet<MethodInfo>();

            foreach (var property in t.GetProperties(c_DeclaredOnly))
            {
                var methods = property.GetAccessors(true);
                accessors.UnionWith(methods);
                if (methods.Any(IsInternal)) result.Add(property);
            }

            foreach (var e in t.GetEvents(c_DeclaredOnly))
            {
                var methods = new[] { e.GetAddMethod(true), e.GetRemoveMethod(true), e.GetRaiseMethod(true) }.Where(m => m != null).ToList();
                accessors.UnionWith(methods);
                if (methods.Any(IsInternal)) result.Add(e);
            }

            result.AddRange(t.GetMethods(c_DeclaredOnly).Where(m => accessors.Contains(m) == false && IsInternal(m)));
            result.AddRange(t.GetConstructors(c_DeclaredOnly).Where(c => c.IsStatic == false && IsInternal(c)));
            result.AddRange(t.GetFields(c_DeclaredOnly).Where(f => f.IsAssembly || f.IsFamilyOrAssembly));

            result.RemoveAll(GenerationContext.IsCompilerGeneratedMember);
            return result;
        }

        private static bool IsInternal(MethodBase m) => m.IsAssembly || m.IsFamilyOrAssembly;

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
            return signature.Where(ctx.IsWrappedType).Select(MemberId.Type).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
        }
    }
}
