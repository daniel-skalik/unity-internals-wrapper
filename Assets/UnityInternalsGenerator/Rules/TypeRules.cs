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
        /// <summary>Evaluated in order after pre-checks (access, generics, nesting) pass. A match means the type is Wrapped.</summary>
        public static IReadOnlyList<ITypeRule> CreateOrdered() => new ITypeRule[]
        {
            new EnumTypeRule(),
            new StaticClassTypeRule(),
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

        public bool Match(Type t, GenerationContext ctx) => t.IsClass && typeof(Delegate).IsAssignableFrom(t) == false;
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

    /// <summary>
    /// Static class → public static class; non-static class → public class with a private ctor and static members only.
    /// Emitted members, then emitted nested types.
    /// </summary>
    public sealed class ClassTypeGenerator : ITypeGenerator
    {
        public void Emit(Type t, CodeWriter w, GenerationContext ctx)
        {
            var name = CSharpNames.TypeDeclName(t);
            var isStatic = t.IsAbstract && t.IsSealed;
            var members = ctx.PlanFor(t).Where(p => ctx.IsEmitted(p.Member)).ToList();
            var nested = t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(n => ctx.IsEmitted(n))
                .OrderBy(n => n.FullName, StringComparer.Ordinal)
                .ToList();

            using (w.Block("public " + (isStatic ? "static " : string.Empty) + "class " + name))
            {
                if (isStatic == false) w.Line("private " + name + "() { }");

                foreach (var planned in members)
                {
                    using (w.Member(MemberId.Member(planned.Member)))
                    {
                        planned.Rule.Emit(planned.Member, w, ctx);
                    }
                }

                var wroteAny = isStatic == false || members.Count > 0;
                foreach (var n in nested)
                {
                    if (wroteAny) w.Line();
                    TypeEmission.Emit(n, w, ctx);
                    wroteAny = true;
                }
            }
        }
    }
}
