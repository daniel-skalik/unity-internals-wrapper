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

    public static class MemberRules
    {
        /// <summary>Evaluated in order; the first matching rule emits the member.</summary>
        public static IReadOnlyList<IMemberRule> CreateOrdered() => new IMemberRule[]
        {
            new ConstMemberRule(),
            new FieldMemberRule(),
            new PropertyMemberRule(),
            new MethodMemberRule(),
        };
    }

    public sealed class ConstMemberRule : IMemberRule
    {
        public string Name => "Const";

        public bool Match(MemberInfo m, GenerationContext ctx) => m is FieldInfo f && f.IsLiteral;

        public void Emit(MemberInfo m, CodeWriter w, GenerationContext ctx)
        {
            var f = (FieldInfo)m;
            var conversion = ctx.Convert(f.FieldType);
            if (MemberSyntax.TryFormatConst(f, conversion, out var literal) == false) throw new InvalidOperationException("Const value not formattable: " + MemberId.Member(f));

            MemberSyntax.Attributes(f, w);
            w.Line("public const " + conversion.WrappedTypeRef + " " + MemberSyntax.WrappedName(f, ctx) + " = " + literal + ";");
        }
    }

    public sealed class FieldMemberRule : IMemberRule
    {
        public string Name => "Field";

        public bool Match(MemberInfo m, GenerationContext ctx) => m is FieldInfo f && f.IsLiteral == false;

        public void Emit(MemberInfo m, CodeWriter w, GenerationContext ctx)
        {
            var f = (FieldInfo)m;
            var conversion = ctx.Convert(f.FieldType);
            var source = MemberSyntax.SourceRef(f);
            var head = "public static " + conversion.WrappedTypeRef + " " + MemberSyntax.WrappedName(f, ctx);

            MemberSyntax.Attributes(f, w);
            w.Line(f.IsInitOnly
                ? head + " => " + conversion.ToWrapped(source) + ";"
                : head + " { get => " + conversion.ToWrapped(source) + "; set => " + source + " = " + conversion.ToUnity("value") + "; }");
        }
    }

    public sealed class PropertyMemberRule : IMemberRule
    {
        public string Name => "Property";

        public bool Match(MemberInfo m, GenerationContext ctx) => m is PropertyInfo;

        public void Emit(MemberInfo m, CodeWriter w, GenerationContext ctx)
        {
            var p = (PropertyInfo)m;
            var conversion = ctx.Convert(p.PropertyType);
            var source = MemberSyntax.SourceRef(p);
            var head = "public static " + conversion.WrappedTypeRef + " " + MemberSyntax.WrappedName(p, ctx);
            var getter = IsUsable(p.GetGetMethod(true), ctx) ? "get => " + conversion.ToWrapped(source) + ";" : null;
            var setter = IsUsable(p.GetSetMethod(true), ctx) ? "set => " + source + " = " + conversion.ToUnity("value") + ";" : null;

            MemberSyntax.Attributes(p, w);
            if (getter != null && setter == null)
            {
                w.Line(head + " => " + conversion.ToWrapped(source) + ";");
                return;
            }
            w.Line(head + " { " + string.Join(" ", new[] { getter, setter }.Where(s => s != null)) + " }");
        }

        private static bool IsUsable(MethodInfo accessor, GenerationContext ctx) => accessor != null && ctx.IsAccessible(accessor);
    }

    public sealed class MethodMemberRule : IMemberRule
    {
        public string Name => "Method";

        public bool Match(MemberInfo m, GenerationContext ctx) => m is MethodInfo;

        public void Emit(MemberInfo m, CodeWriter w, GenerationContext ctx)
        {
            var method = (MethodInfo)m;
            var returns = ctx.Convert(method.ReturnType);
            var parameters = MemberSyntax.Parameters(method, ctx, out _);
            var declaration = string.Join(", ", parameters.Select(p => p.Declaration));
            var call = MemberSyntax.SourceRef(method) + "(" + string.Join(", ", parameters.Select(p => p.Argument)) + ")";

            MemberSyntax.Attributes(method, w);
            w.Line("public static " + returns.WrappedTypeRef + " " + MemberSyntax.WrappedName(method, ctx) + "(" + declaration + ") => " + returns.ToWrapped(call) + ";");
        }
    }

    /// <summary>One wrapper parameter: declaration text and the converted argument passed to the Unity member.</summary>
    public sealed class WrappedParameter
    {
        public WrappedParameter(string declaration, string argument, string signatureType)
        {
            Declaration = declaration;
            Argument = argument;
            SignatureType = signatureType;
        }

        public string Declaration { get; }
        public string Argument { get; }
        public string SignatureType { get; }
    }

    /// <summary>C# fragments shared by member rules and <see cref="MemberPlanner"/>. Parameter helpers assume every conversion is Wrapped.</summary>
    public static class MemberSyntax
    {
        public static string SourceRef(MemberInfo m) => CSharpNames.TypeRef(m.DeclaringType) + "." + CSharpNames.Identifier(m.Name);

        public static string WrappedName(MemberInfo m, GenerationContext ctx) => CSharpNames.Identifier(ctx.Naming.MemberName(m.Name));

        /// <summary>Copies a non-error [Obsolete]; error-obsolete members never reach emission.</summary>
        public static void Attributes(MemberInfo m, CodeWriter w)
        {
            var obsolete = m.GetCustomAttribute<ObsoleteAttribute>(false);
            if (obsolete == null) return;
            w.Line(obsolete.Message == null ? "[global::System.Obsolete]" : "[global::System.Obsolete(" + DefaultValueFormatter.Quote(obsolete.Message) + ")]");
        }

        public static bool TryFormatConst(FieldInfo f, Conversion conversion, out string literal) => DefaultValueFormatter.TryFormatValue(f.GetRawConstantValue(), f.FieldType, conversion.WrappedTypeRef, out literal);

        private static string CallerAttributes(ParameterInfo p)
        {
            var result = string.Empty;
            if (p.IsDefined(typeof(System.Runtime.CompilerServices.CallerFilePathAttribute), false)) result += "[global::System.Runtime.CompilerServices.CallerFilePath] ";
            if (p.IsDefined(typeof(System.Runtime.CompilerServices.CallerMemberNameAttribute), false)) result += "[global::System.Runtime.CompilerServices.CallerMemberName] ";
            if (p.IsDefined(typeof(System.Runtime.CompilerServices.CallerLineNumberAttribute), false)) result += "[global::System.Runtime.CompilerServices.CallerLineNumber] ";
            return result;
        }

        /// <summary>
        /// Defaults that cannot be formatted are dropped, together with every default before them
        /// (C# requires optional parameters to trail); <paramref name="droppedDefault"/> reports it.
        /// </summary>
        public static IReadOnlyList<WrappedParameter> Parameters(MethodInfo m, GenerationContext ctx, out bool droppedDefault)
        {
            var ps = m.GetParameters();
            var literals = new string[ps.Length];
            var isParams = new bool[ps.Length];
            var conversions = new Conversion[ps.Length];
            for (var i = 0; i < ps.Length; i++)
            {
                conversions[i] = ctx.Convert(ps[i].ParameterType);
                isParams[i] = i == ps.Length - 1 && ps[i].IsDefined(typeof(ParamArrayAttribute), false);
                if (isParams[i] == false && DefaultValueFormatter.TryFormat(ps[i], conversions[i].WrappedTypeRef, out var literal)) literals[i] = literal;
            }

            var required = false;
            for (var i = ps.Length - 1; i >= 0; i--)
            {
                if (isParams[i]) continue;
                if (required) literals[i] = null;
                else if (literals[i] == null) required = true;
            }

            droppedDefault = false;
            var result = new WrappedParameter[ps.Length];
            for (var i = 0; i < ps.Length; i++)
            {
                if (ps[i].HasDefaultValue && isParams[i] == false && literals[i] == null) droppedDefault = true;

                var name = CSharpNames.Identifier(string.IsNullOrEmpty(ps[i].Name) ? "arg" + i.ToString(CultureInfo.InvariantCulture) : ps[i].Name);
                var typeRef = conversions[i].WrappedTypeRef;
                var declaration = CallerAttributes(ps[i]) + (isParams[i] ? "params " : string.Empty) + typeRef + " " + name + (literals[i] == null ? string.Empty : " = " + literals[i]);
                result[i] = new WrappedParameter(declaration, conversions[i].ToUnity(name), typeRef);
            }
            return result;
        }
    }
}
