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
            new IndexerMemberRule(),
            new PropertyMemberRule(),
            new ConstructorMemberRule(),
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
            w.Line(MemberSyntax.PublicNew(f, ctx) + "const " + conversion.WrappedTypeRef + " " + MemberSyntax.WrappedName(f, ctx) + " = " + literal + ";");
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
            var head = MemberSyntax.Modifiers(f, ctx) + conversion.WrappedTypeRef + " " + MemberSyntax.WrappedName(f, ctx);

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
            var head = MemberSyntax.Modifiers(p, ctx) + conversion.WrappedTypeRef + " " + MemberSyntax.WrappedName(p, ctx);

            MemberSyntax.Attributes(p, w);
            MemberSyntax.Accessors(p, conversion, source, head, w, ctx);
        }
    }

    /// <summary>Indexer: the wrapper indexes the value field (or the Unity type) with the converted arguments.</summary>
    public sealed class IndexerMemberRule : IMemberRule
    {
        public string Name => "Indexer";

        public bool Match(MemberInfo m, GenerationContext ctx) => m is PropertyInfo p && p.GetIndexParameters().Length > 0;

        public void Emit(MemberInfo m, CodeWriter w, GenerationContext ctx)
        {
            var p = (PropertyInfo)m;
            var conversion = ctx.Convert(p.PropertyType);
            var parameters = MemberSyntax.Parameters(p.GetIndexParameters(), ctx, out _);
            var source = MemberSyntax.Owner(p) + "[" + string.Join(", ", parameters.Select(x => x.Argument)) + "]";
            var head = MemberSyntax.Modifiers(p, ctx) + conversion.WrappedTypeRef + " this[" + string.Join(", ", parameters.Select(x => x.Declaration)) + "]";

            MemberSyntax.Attributes(p, w);
            MemberSyntax.Accessors(p, conversion, source, head, w, ctx);
        }
    }

    /// <summary>Constructor: builds the real Unity value and hands it to the wrapper's internal constructor.</summary>
    public sealed class ConstructorMemberRule : IMemberRule
    {
        public string Name => "Constructor";

        public bool Match(MemberInfo m, GenerationContext ctx) => m is ConstructorInfo c && c.IsStatic == false;

        public void Emit(MemberInfo m, CodeWriter w, GenerationContext ctx)
        {
            var c = (ConstructorInfo)m;
            var parameters = MemberSyntax.Parameters(c.GetParameters(), ctx, out _);
            var declaration = string.Join(", ", parameters.Select(p => p.Declaration));
            var arguments = string.Join(", ", parameters.Select(p => p.Argument));

            MemberSyntax.Attributes(c, w);
            w.Line("public " + CSharpNames.TypeDeclName(c.DeclaringType) + "(" + declaration + ") : this(new " + CSharpNames.TypeRef(c.DeclaringType) + "(" + arguments + ")) { }");
        }
    }

    public sealed class MethodMemberRule : IMemberRule
    {
        public string Name => "Method";

        public bool Match(MemberInfo m, GenerationContext ctx) => m is MethodInfo;

        public void Emit(MemberInfo m, CodeWriter w, GenerationContext ctx)
        {
            var method = (MethodInfo)m;
            var returns = ctx.Convert(method.ReturnType);
            var parameters = MemberSyntax.Parameters(method.GetParameters(), ctx, out _);
            var declaration = string.Join(", ", parameters.Select(p => p.Declaration));
            var call = MemberSyntax.SourceRef(method) + "(" + string.Join(", ", parameters.Select(p => p.Argument)) + ")";

            MemberSyntax.Attributes(method, w);
            w.Line(MemberSyntax.Modifiers(method, ctx) + returns.WrappedTypeRef + " " + MemberSyntax.WrappedName(method, ctx) + "(" + declaration + ") => " + returns.ToWrapped(call) + ";");
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
        /// <summary>What the member is read through: the Unity type for static members, the wrapper's value field for instance members.</summary>
        public static string Owner(MemberInfo m) => IsStatic(m) ? CSharpNames.TypeRef(m.DeclaringType) : WrapperSyntax.ValueField;

        public static string SourceRef(MemberInfo m) => Owner(m) + "." + CSharpNames.Identifier(m.Name);

        /// <summary>Emitted name: the naming strategy's, except constructors (the wrapper type name) and indexers ("this").</summary>
        public static string WrappedName(MemberInfo m, GenerationContext ctx)
        {
            if (m is ConstructorInfo) return CSharpNames.TypeDeclName(m.DeclaringType);
            if (m is PropertyInfo p && p.GetIndexParameters().Length > 0) return "this";
            return CSharpNames.Identifier(ctx.Naming.MemberName(m.Name));
        }

        /// <summary>"public ", plus "new " when the member hides one an emitted wrapper base declares.</summary>
        public static string PublicNew(MemberInfo m, GenerationContext ctx) => "public " + (ctx.HidesWrapperBaseMember(m) ? "new " : string.Empty);

        public static string Modifiers(MemberInfo m, GenerationContext ctx) => PublicNew(m, ctx) + (IsStatic(m) ? "static " : string.Empty);

        public static bool IsStatic(MemberInfo m)
        {
            switch (m)
            {
                case FieldInfo f: return f.IsStatic;
                case PropertyInfo p: return p.GetAccessors(true).Any(a => a.IsStatic);
                case EventInfo e: return e.GetAddMethod(true)?.IsStatic == true;
                case MethodBase method: return method.IsStatic;
                default: return false;
            }
        }

        /// <summary>Emitted parameter-type signature, used for hiding and collision checks; null for members that cannot be overloaded.</summary>
        public static string Signature(MemberInfo m, GenerationContext ctx)
        {
            if (m is MethodBase method) return "(" + string.Join(", ", Parameters(method.GetParameters(), ctx, out _).Select(p => p.SignatureType)) + ")";
            if (m is PropertyInfo p && p.GetIndexParameters().Length > 0) return "[" + string.Join(", ", Parameters(p.GetIndexParameters(), ctx, out _).Select(x => x.SignatureType)) + "]";
            return null;
        }

        /// <summary>Writes a get/set pair, or an expression body when there is only a getter.</summary>
        public static void Accessors(PropertyInfo p, Conversion conversion, string source, string head, CodeWriter w, GenerationContext ctx)
        {
            var getter = IsUsable(p.GetGetMethod(true), ctx) ? "get => " + conversion.ToWrapped(source) + ";" : null;
            var setter = IsUsable(p.GetSetMethod(true), ctx) ? "set => " + source + " = " + conversion.ToUnity("value") + ";" : null;

            if (getter != null && setter == null)
            {
                w.Line(head + " => " + conversion.ToWrapped(source) + ";");
                return;
            }
            w.Line(head + " { " + string.Join(" ", new[] { getter, setter }.Where(s => s != null)) + " }");
        }

        private static bool IsUsable(MethodInfo accessor, GenerationContext ctx) => accessor != null && ctx.IsAccessible(accessor);

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
        public static IReadOnlyList<WrappedParameter> Parameters(ParameterInfo[] ps, GenerationContext ctx, out bool droppedDefault)
        {
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
