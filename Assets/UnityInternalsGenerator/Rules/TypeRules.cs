namespace DandaUtils.Editor.UnityInternalsGenerator.Rules
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using DandaUtils.Editor.UnityInternalsGenerator.Emit;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;

    public static class TypeRules
    {
        /// <summary>Evaluated in order after pre-checks (access, generics, nesting, delegates) pass. A match means the type is Wrapped.</summary>
        public static IReadOnlyList<ITypeRule> CreateOrdered() => new ITypeRule[]
        {
            new EnumTypeRule(),
            new StaticClassTypeRule(),
            new StructTypeRule(),
            new InterfaceTypeRule(),
            new InstanceClassTypeRule(),
        };
    }

    /// <summary>Emits a Wrapped type through its classification's generator, attributing every line to the type id. Wrapped types outside the emitted set are skipped.</summary>
    public static class TypeEmission
    {
        public static void Emit(Type t, CodeWriter w, GenerationContext ctx)
        {
            var classification = ctx.Classify(t);
            if (classification.Status == WrapStatus.Wrapped && ctx.IsEmitted(t) == false) return;
            if (classification.Status != WrapStatus.Wrapped) throw new InvalidOperationException("Type is not wrapped: " + MemberId.Type(t) + " (" + classification.Status + ")");

            using (w.Member(MemberId.Type(t)))
            {
                classification.Generator.Emit(t, w, ctx);
            }
        }
    }

    public sealed class EnumTypeRule : ITypeRule
    {
        public string Name => "EnumTypeRule";

        public ITypeGenerator Generator { get; } = new EnumTypeGenerator();

        public bool Match(Type t, GenerationContext ctx) => t.IsEnum;
    }

    public sealed class StaticClassTypeRule : ITypeRule
    {
        public string Name => "StaticClassTypeRule";

        public ITypeGenerator Generator { get; } = new ClassTypeGenerator();

        public bool Match(Type t, GenerationContext ctx) => t.IsClass && t.IsAbstract && t.IsSealed;
    }

    public sealed class StructTypeRule : ITypeRule
    {
        public string Name => "StructTypeRule";

        public ITypeGenerator Generator { get; } = new WrapperTypeGenerator(WrapperKind.Struct);

        public bool Match(Type t, GenerationContext ctx) => t.IsValueType;
    }

    public sealed class InterfaceTypeRule : ITypeRule
    {
        public string Name => "InterfaceTypeRule";

        public ITypeGenerator Generator { get; } = new WrapperTypeGenerator(WrapperKind.Interface);

        public bool Match(Type t, GenerationContext ctx) => t.IsInterface;
    }

    public sealed class InstanceClassTypeRule : ITypeRule
    {
        public string Name => "InstanceClassTypeRule";

        public ITypeGenerator Generator { get; } = new WrapperTypeGenerator(WrapperKind.Class);

        public bool Match(Type t, GenerationContext ctx) => t.IsClass;
    }

    /// <summary>Mirror enum: same underlying type, values and [Flags]. Values are always emitted whole and never reported.</summary>
    public sealed class EnumTypeGenerator : ITypeGenerator
    {
        public void Emit(Type t, CodeWriter w, GenerationContext ctx)
        {
            if (t.IsDefined(typeof(FlagsAttribute), false)) w.Line("[global::System.Flags]");

            using (w.Block("public enum " + CSharpNames.TypeDeclName(t) + " : " + CSharpNames.TypeRef(Enum.GetUnderlyingType(t))))
            {
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static).OrderBy(f => f.MetadataToken))
                {
                    using (w.Member(MemberId.Member(f)))
                    {
                        w.Line(CSharpNames.Identifier(f.Name) + " = " + Convert.ToString(f.GetRawConstantValue(), CultureInfo.InvariantCulture) + ",");
                    }
                }
            }
        }
    }

    /// <summary>Static class → public static class with forwarding static members, then emitted nested types.</summary>
    public sealed class ClassTypeGenerator : ITypeGenerator
    {
        public void Emit(Type t, CodeWriter w, GenerationContext ctx)
        {
            var members = TypeBodies.EmittedMembers(t, ctx);

            using (w.Block("public static class " + CSharpNames.TypeDeclName(t)))
            {
                foreach (var planned in members) TypeBodies.EmitMember(planned, w, ctx);
                TypeBodies.EmitNested(t, w, ctx, members.Count > 0);
            }
        }
    }

    public enum WrapperKind
    {
        Class,
        Struct,
        Interface,
    }

    /// <summary>
    /// Internal class, struct or interface → public wrapper holding the real value in the internal field <see cref="WrapperSyntax.ValueField"/>.
    /// Class wrappers mirror the internal base chain up to the first public base (exposed as Target plus an implicit conversion);
    /// Wrap turns null into null and, for classes, returns the most-derived registered wrapper.
    /// </summary>
    public sealed class WrapperTypeGenerator : ITypeGenerator
    {
        private readonly WrapperKind m_Kind;

        public WrapperTypeGenerator(WrapperKind kind)
        {
            m_Kind = kind;
        }

        public void Emit(Type t, CodeWriter w, GenerationContext ctx)
        {
            var name = CSharpNames.TypeDeclName(t);
            var unity = CSharpNames.TypeRef(t);
            var baseWrapper = m_Kind == WrapperKind.Class ? ctx.WrapperBase(t) : null;
            var keyword = m_Kind == WrapperKind.Struct ? "struct " : "class ";
            var header = "public " + keyword + name + (baseWrapper == null ? string.Empty : " : " + ctx.WrappedTypeRef(baseWrapper));

            using (w.Block(header))
            {
                EmitShape(t, name, unity, baseWrapper, w);

                foreach (var planned in TypeBodies.EmittedMembers(t, ctx))
                {
                    w.Line();
                    TypeBodies.EmitMember(planned, w, ctx);
                }
                TypeBodies.EmitNested(t, w, ctx, true);
            }
        }

        private void EmitShape(Type t, string name, string unity, Type baseWrapper, CodeWriter w)
        {
            var isStruct = m_Kind == WrapperKind.Struct;
            var isRoot = baseWrapper == null;

            w.Line("internal " + (isRoot ? string.Empty : "new ") + (isStruct ? string.Empty : "readonly ") + unity + " " + WrapperSyntax.ValueField + ";");
            w.Line();
            w.Line("internal " + name + "(" + unity + " value)" + (isRoot ? string.Empty : " : base(value)") + " { " + WrapperSyntax.ValueField + " = value; }");
            w.Line();
            w.Line("internal static " + name + " " + WrapperSyntax.WrapMethod + "(" + unity + " value) => " + WrapExpression(name) + ";");

            if (isRoot == false) return;

            var publicBase = m_Kind == WrapperKind.Class ? GenerationContext.PublicBase(t) : null;
            if (publicBase != null)
            {
                var publicRef = CSharpNames.TypeRef(publicBase);
                w.Line();
                w.Line("public " + publicRef + " " + WrapperSyntax.TargetProperty + " => " + WrapperSyntax.ValueField + ";");
                w.Line();
                w.Line("public static implicit operator " + publicRef + "(" + name + " wrapper) => wrapper?." + WrapperSyntax.ValueField + ";");
            }

            w.Line();
            w.Line("public override bool Equals(object obj) => obj is " + name + " other && " + SameValue("other") + ";");
            w.Line();
            w.Line("public override int GetHashCode() => " + WrapperSyntax.ValueField + ".GetHashCode();");
            w.Line();
            w.Line("public static bool operator ==(" + name + " left, " + name + " right) => " + EqualityExpression() + ";");
            w.Line();
            w.Line("public static bool operator !=(" + name + " left, " + name + " right) => (left == right) == false;");
        }

        private string WrapExpression(string name)
        {
            switch (m_Kind)
            {
                case WrapperKind.Struct: return "new " + name + "(value)";

                // A public class between two internal ones makes the lower wrapper a root, so the registry can hand back a wrapper that is not a T.
                case WrapperKind.Class: return "value == null ? null : " + WrapperSyntax.RegistryRef + "." + WrapperSyntax.WrapMethod + "(value) as " + name + " ?? new " + name + "(value)";
                default: return "value == null ? null : new " + name + "(value)";
            }
        }

        private string SameValue(string other) =>
            m_Kind == WrapperKind.Struct
                ? WrapperSyntax.ValueField + ".Equals(" + other + "." + WrapperSyntax.ValueField + ")"
                : "global::System.Object.ReferenceEquals(" + WrapperSyntax.ValueField + ", " + other + "." + WrapperSyntax.ValueField + ")";

        private string EqualityExpression() =>
            m_Kind == WrapperKind.Struct
                ? "left." + WrapperSyntax.ValueField + ".Equals(right." + WrapperSyntax.ValueField + ")"
                : "global::System.Object.ReferenceEquals(left, null) ? global::System.Object.ReferenceEquals(right, null) : left.Equals(right)";
    }

    /// <summary>Emission shared by the type generators: emitted members in plan order, then emitted nested types.</summary>
    internal static class TypeBodies
    {
        public static IReadOnlyList<PlannedMember> EmittedMembers(Type t, GenerationContext ctx) => ctx.PlanFor(t).Where(p => ctx.IsEmitted(p.Member)).ToList();

        public static void EmitMember(PlannedMember planned, CodeWriter w, GenerationContext ctx)
        {
            using (w.Member(MemberId.Member(planned.Member)))
            {
                planned.Rule.Emit(planned.Member, w, ctx);
            }
        }

        public static void EmitNested(Type t, CodeWriter w, GenerationContext ctx, bool wroteAny)
        {
            var nested = t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(ctx.IsEmitted)
                .OrderBy(n => n.FullName, StringComparer.Ordinal)
                .ToList();

            foreach (var n in nested)
            {
                if (wroteAny) w.Line();
                TypeEmission.Emit(n, w, ctx);
                wroteAny = true;
            }
        }
    }
}
