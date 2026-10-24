namespace DandaUtils.Editor.UnityInternalsGenerator.Rules
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;

    public static class ConversionRules
    {
        /// <summary>Evaluated in order; the first matching rule wins. The last rule matches everything.</summary>
        public static IReadOnlyList<IConversionRule> CreateOrdered() => new IConversionRule[]
        {
            new UnsupportedConversionRule(),
            new ByRefConversionRule(),
            new GenericParameterConversionRule(),
            new PassthroughConversionRule(),
            new EnumCastConversionRule(),
            new WrapperConversionRule(),
            new FallbackConversionRule(),
        };
    }

    public sealed class UnsupportedConversionRule : IConversionRule
    {
        private const string c_IsByRefLikeAttribute = "System.Runtime.CompilerServices.IsByRefLikeAttribute";

        public string Name => "Unsupported";

        public bool Match(Type t, GenerationContext ctx) => t.IsPointer || t == typeof(TypedReference) || IsByRefLike(t);

        public Conversion Create(Type t, GenerationContext ctx) => Conversion.Unsupported(t.IsPointer ? "pointer type" : "ref struct / TypedReference");

        private static bool IsByRefLike(Type t) => t.IsValueType && t.GetCustomAttributesData().Any(a => a.AttributeType.FullName == c_IsByRefLikeAttribute);
    }

    public sealed class ByRefConversionRule : IConversionRule
    {
        public string Name => "ByRef";

        public bool Match(Type t, GenerationContext ctx) => t.IsByRef;

        public Conversion Create(Type t, GenerationContext ctx) => Conversion.Deferred("ref/out (step 3)");
    }

    public sealed class GenericParameterConversionRule : IConversionRule
    {
        public string Name => "GenericParameter";

        public bool Match(Type t, GenerationContext ctx) => t.IsGenericParameter;

        public Conversion Create(Type t, GenerationContext ctx) => Conversion.Deferred("GenericParameter (step 3)");
    }

    public sealed class PassthroughConversionRule : IConversionRule
    {
        public string Name => "Passthrough";

        public bool Match(Type t, GenerationContext ctx) => t == typeof(void) || (t.IsVisible && t.IsGenericParameter == false);

        public Conversion Create(Type t, GenerationContext ctx) => Conversion.Passthrough(CSharpNames.TypeRef(t));
    }

    public sealed class EnumCastConversionRule : IConversionRule
    {
        public string Name => "EnumCast";

        public bool Match(Type t, GenerationContext ctx) => t.IsEnum && ctx.IsWrappedType(t);

        public Conversion Create(Type t, GenerationContext ctx) => Conversion.Cast(ctx.WrappedTypeRef(t), CSharpNames.TypeRef(t));
    }

    /// <summary>Internal class, struct or interface with a wrapper: Wrap() one way, the wrapper's value field the other.</summary>
    public sealed class WrapperConversionRule : IConversionRule
    {
        public string Name => "WrapperConversion";

        public bool Match(Type t, GenerationContext ctx) => ctx.IsInstanceWrapper(t);

        public Conversion Create(Type t, GenerationContext ctx) => Conversion.Wrapper(ctx.WrappedTypeRef(t), t.IsValueType);
    }

    public sealed class FallbackConversionRule : IConversionRule
    {
        public string Name => "Fallback";

        public bool Match(Type t, GenerationContext ctx) => true;

        public Conversion Create(Type t, GenerationContext ctx)
        {
            if (t.IsArray) return Conversion.Deferred("Array (step 3)");
            if (t.IsGenericType && t.IsGenericTypeDefinition == false)
            {
                return t.GetGenericTypeDefinition() == typeof(Nullable<>) ? Conversion.Deferred("Nullable (step 3)") : Conversion.Deferred("GenericCollection (step 3)");
            }
            if (ctx.IsSourceType(t) == false) return Conversion.Unsupported("type outside source assemblies");

            var classification = ctx.Classify(t);
            if (classification.Status == WrapStatus.Wrapped) return Conversion.Unsupported("Wrapped type without a conversion rule");
            return Conversion.NotWrapped(classification.Status, classification.Reason ?? classification.RuleName);
        }
    }
}
