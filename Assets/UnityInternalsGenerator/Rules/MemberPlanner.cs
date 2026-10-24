namespace DandaUtils.Editor.UnityInternalsGenerator.Rules
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;

    /// <summary>A declared member that passed every check and will be emitted by <see cref="Rule"/>.</summary>
    public sealed class PlannedMember
    {
        public PlannedMember(MemberInfo member, IMemberRule rule)
        {
            Member = member;
            Rule = rule;
        }

        public MemberInfo Member { get; }
        public IMemberRule Rule { get; }
    }

    /// <summary>One considered member with the planner's verdict. Accessor methods covered by a property or event entry are not listed.</summary>
    public sealed class MemberVerdict
    {
        public MemberVerdict(MemberInfo member, WrapStatus status, string reason)
        {
            Member = member;
            Status = status;
            Reason = reason;
        }

        public MemberInfo Member { get; }
        public WrapStatus Status { get; }
        public string Reason { get; }
    }

    /// <summary>Result of planning one type: members to emit, and a verdict for every member considered.</summary>
    public sealed class MemberPlan
    {
        public MemberPlan(IReadOnlyList<PlannedMember> planned, IReadOnlyList<MemberVerdict> verdicts)
        {
            Planned = planned;
            Verdicts = verdicts;
        }

        public IReadOnlyList<PlannedMember> Planned { get; }
        public IReadOnlyList<MemberVerdict> Verdicts { get; }
    }

    /// <summary>
    /// Decides the status of every declared member of a wrapped class and reports exactly one entry per member,
    /// except accessor methods, which are covered by their property's or event's entry.
    /// </summary>
    public static class MemberPlanner
    {
        private const BindingFlags c_Declared = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private const string c_CompilerGeneratedAttribute = "System.Runtime.CompilerServices.CompilerGeneratedAttribute";

        /// <summary>Wrapped members in emission order (fields, properties, methods, each by metadata token) plus every verdict. Reports each member; call once per type (see <see cref="GenerationContext.PlanFor"/>).</summary>
        public static MemberPlan Plan(Type t, IReadOnlyList<IMemberRule> rules, GenerationContext ctx)
        {
            var candidates = new List<Candidate>();
            foreach (var m in DeclaredMembers(t))
            {
                var rule = rules.FirstOrDefault(r => r.Match(m, ctx));
                var verdict = Check(m, rule, ctx);
                candidates.Add(new Candidate(m, rule, verdict));
            }

            MarkCollisions(t, candidates, ctx);

            var planned = new List<PlannedMember>();
            var verdicts = new List<MemberVerdict>();
            foreach (var c in candidates)
            {
                verdicts.Add(new MemberVerdict(c.Member, c.Verdict.Status, c.Verdict.Reason));
                ctx.Report(new ReportEntry(MemberId.Member(c.Member), c.Rule?.Name ?? KindName(c.Member), c.Verdict.Status, c.Verdict.Reason));
                if (c.Verdict.Status == WrapStatus.Wrapped) planned.Add(new PlannedMember(c.Member, c.Rule));
            }
            return new MemberPlan(planned, verdicts);
        }

        /// <summary>
        /// Every member the wrapper of <paramref name="t"/> considers, in emission order. A public type contributes only its
        /// accessible-internal members: its public ones are reachable without the wrapper, so they are not mirrored (step 4).
        /// </summary>
        private static IEnumerable<MemberInfo> DeclaredMembers(Type t)
        {
            var properties = ByToken(t.GetProperties(c_Declared));
            var events = ByToken(t.GetEvents(c_Declared));
            var accessors = new HashSet<MethodInfo>(properties.SelectMany(p => p.GetAccessors(true))
                .Concat(events.SelectMany(e => new[] { e.GetAddMethod(true), e.GetRemoveMethod(true), e.GetRaiseMethod(true) }))
                .Where(a => a != null));

            var ctors = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Cast<MemberInfo>().ToList();
            if (t.TypeInitializer != null) ctors.Add(t.TypeInitializer);

            var all = ByToken(t.GetFields(c_Declared)).Cast<MemberInfo>()
                .Concat(properties)
                .Concat(events)
                .Concat(ByToken(ctors))
                .Concat(ByToken(t.GetMethods(c_Declared)).Where(m => accessors.Contains(m) == false));

            return t.IsVisible ? all.Where(IsInternalMember) : all;
        }

        /// <summary>
        /// True when the host assembly can only reach the member because it is <c>internal</c> or <c>protected internal</c>.
        /// These are the members a public type's wrapper exposes; <c>protected</c> and <c>private</c> wait for step 7.
        /// </summary>
        public static bool IsInternalMember(MemberInfo m)
        {
            switch (m)
            {
                case FieldInfo f: return f.IsAssembly || f.IsFamilyOrAssembly;
                case PropertyInfo p: return p.GetAccessors(true).Any(IsInternalMethod);
                case EventInfo e: return new[] { e.GetAddMethod(true), e.GetRemoveMethod(true), e.GetRaiseMethod(true) }.Any(a => a != null && IsInternalMethod(a));
                case MethodBase method: return IsInternalMethod(method);
                default: return false;
            }
        }

        private static bool IsInternalMethod(MethodBase m) => m.IsAssembly || m.IsFamilyOrAssembly;

        private static List<T> ByToken<T>(IEnumerable<T> members) where T : MemberInfo => members.OrderBy(m => m.MetadataToken).ToList();

        private static Verdict Check(MemberInfo m, IMemberRule rule, GenerationContext ctx)
        {
            if (ctx.IsAutoExcludedId(MemberId.Member(m))) return Verdict.Of(WrapStatus.Excluded, "AutoExclusion: failed to compile");
            if (IsCompilerGenerated(m)) return Verdict.Of(WrapStatus.Excluded, "CompilerGenerated");
            if (m is ConstructorInfo c && c.IsStatic) return Verdict.Of(WrapStatus.Excluded, "StaticConstructor");
            if (IsAccessible(m, ctx) == false) return Verdict.Of(WrapStatus.NotAccessible, "Private or protected member");
            if (m.GetCustomAttribute<ObsoleteAttribute>(false)?.IsError == true) return Verdict.Of(WrapStatus.Excluded, "ObsoleteError");
            if (m is EventInfo) return Verdict.Of(WrapStatus.Deferred, "Event (step 3)");
            if (rule == null) return Verdict.Of(WrapStatus.Unsupported, "No member rule matched");

            switch (m)
            {
                case MethodInfo method: return CheckMethod(method, ctx);
                case ConstructorInfo ctor: return CheckConstructor(ctor, ctx);
                case PropertyInfo property: return property.GetIndexParameters().Length > 0 ? CheckIndexer(property, ctx) : FromConversion(ctx.Convert(property.PropertyType));
                case FieldInfo field: return CheckField(field, ctx);
                default: return Verdict.Of(WrapStatus.Unsupported, "Unsupported member kind");
            }
        }

        private static Verdict CheckMethod(MethodInfo m, GenerationContext ctx)
        {
            if (m.IsSpecialName) return m.Name.StartsWith("op_", StringComparison.Ordinal) ? Verdict.Of(WrapStatus.Deferred, "Operator (step 3)") : Verdict.Of(WrapStatus.Unsupported, "SpecialName");
            if (m.IsGenericMethodDefinition) return Verdict.Of(WrapStatus.Deferred, "GenericMethod (step 3)");
            if (IsVarArgs(m)) return Verdict.Of(WrapStatus.Unsupported, "VarArgs");

            var returns = ctx.Convert(m.ReturnType);
            if (returns.Status != WrapStatus.Wrapped) return FromConversion(returns);
            return CheckParameters(m.GetParameters(), ctx);
        }

        /// <summary>The wrapper calls the real constructor, so an abstract type cannot offer one until the inheritable proxies of step 6.</summary>
        private static Verdict CheckConstructor(ConstructorInfo c, GenerationContext ctx)
        {
            var t = c.DeclaringType;
            if (t.IsAbstract) return Verdict.Of(WrapStatus.Deferred, "Inheritable (step 6)");
            if (t.IsValueType && c.GetParameters().Length == 0) return Verdict.Of(WrapStatus.Unsupported, "Parameterless struct constructor");
            if (IsVarArgs(c)) return Verdict.Of(WrapStatus.Unsupported, "VarArgs");
            return CheckParameters(c.GetParameters(), ctx);
        }

        private static Verdict CheckIndexer(PropertyInfo p, GenerationContext ctx)
        {
            var value = ctx.Convert(p.PropertyType);
            if (value.Status != WrapStatus.Wrapped) return FromConversion(value);
            return CheckParameters(p.GetIndexParameters(), ctx);
        }

        private static bool IsVarArgs(MethodBase m) => (m.CallingConvention & CallingConventions.VarArgs) != 0;

        private static Verdict CheckParameters(ParameterInfo[] ps, GenerationContext ctx)
        {
            foreach (var p in ps)
            {
                var conversion = ctx.Convert(p.ParameterType);
                if (conversion.Status != WrapStatus.Wrapped) return FromConversion(conversion);
            }

            MemberSyntax.Parameters(ps, ctx, out var droppedDefault);
            return Verdict.Of(WrapStatus.Wrapped, droppedDefault ? "default dropped" : null);
        }

        private static Verdict CheckField(FieldInfo f, GenerationContext ctx)
        {
            var conversion = ctx.Convert(f.FieldType);
            if (conversion.Status != WrapStatus.Wrapped) return FromConversion(conversion);
            if (f.IsLiteral && MemberSyntax.TryFormatConst(f, conversion, out _) == false) return Verdict.Of(WrapStatus.Unsupported, "Const value not formattable");
            return Verdict.Of(WrapStatus.Wrapped, null);
        }

        private static Verdict FromConversion(Conversion c) => Verdict.Of(c.Status, c.Reason);

        // Same final name: non-methods (and nested wrapped types, and the enclosing type name) collide with everything; methods only with identical parameter types.
        // Constructors and indexers carry no member name, so only an identical emitted signature collides. On an instance wrapper the names
        // the wrapper declares itself are reserved (WrapperSyntax.ReservedNames).
        private static void MarkCollisions(Type t, List<Candidate> candidates, GenerationContext ctx)
        {
            var typeName = CSharpNames.TypeDeclName(t);
            var nestedNames = new HashSet<string>(t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Where(ctx.IsWrappedType).Select(CSharpNames.TypeDeclName), StringComparer.Ordinal);
            var isInstanceWrapper = ctx.IsInstanceWrapper(t);
            var declaresTarget = ctx.DeclaresTarget(t);
            var wrapped = candidates.Where(c => c.Verdict.Status == WrapStatus.Wrapped).ToList();

            foreach (var group in wrapped.Where(c => HasOwnName(c.Member) == false)
                         .GroupBy(c => MemberSyntax.WrappedName(c.Member, ctx) + MemberSyntax.Signature(c.Member, ctx), StringComparer.Ordinal)
                         .Where(g => g.Count() > 1))
            {
                Collide(group.ToList(), "Duplicate signature '" + group.Key + "'");
            }

            foreach (var group in wrapped.Where(c => HasOwnName(c.Member)).GroupBy(c => MemberSyntax.WrappedName(c.Member, ctx), StringComparer.Ordinal))
            {
                var name = group.Key;
                var members = group.ToList();
                if (isInstanceWrapper && WrapperSyntax.IsReserved(name, declaresTarget))
                {
                    Collide(members, "Name reserved by the wrapper '" + name + "'");
                    continue;
                }
                if (name == typeName)
                {
                    Collide(members, "Name equals enclosing type '" + name + "'");
                    continue;
                }
                if (nestedNames.Contains(name))
                {
                    Collide(members, "Name equals nested type '" + name + "'");
                    continue;
                }
                if (members.Count < 2) continue;
                if (members.Any(c => c.Member is MethodInfo == false))
                {
                    Collide(members, "Duplicate name '" + name + "'");
                    continue;
                }
                foreach (var overload in members.GroupBy(c => MemberSyntax.Signature(c.Member, ctx), StringComparer.Ordinal).Where(g => g.Count() > 1))
                {
                    Collide(overload.ToList(), "Duplicate signature '" + name + overload.Key + "'");
                }
            }
        }

        /// <summary>False for constructors and indexers: their declaration carries no member name.</summary>
        private static bool HasOwnName(MemberInfo m)
        {
            if (m is ConstructorInfo) return false;
            return (m is PropertyInfo p && p.GetIndexParameters().Length > 0) == false;
        }

        private static void Collide(List<Candidate> members, string reason)
        {
            foreach (var c in members) c.Verdict = Verdict.Of(WrapStatus.NameCollision, reason);
        }

        private static bool IsAccessible(MemberInfo m, GenerationContext ctx)
        {
            switch (m)
            {
                case FieldInfo f: return f.IsPublic || f.IsAssembly || f.IsFamilyOrAssembly;
                case PropertyInfo p: return p.GetAccessors(true).Any(ctx.IsAccessible);
                case EventInfo e: return e.GetAddMethod(true) is MethodInfo add && ctx.IsAccessible(add);
                case MethodBase method: return ctx.IsAccessible(method);
                default: return false;
            }
        }

        private static bool IsCompilerGenerated(MemberInfo m) => m.Name.IndexOf('<') >= 0 || m.GetCustomAttributesData().Any(a => a.AttributeType.FullName == c_CompilerGeneratedAttribute);

        private static string KindName(MemberInfo m)
        {
            switch (m)
            {
                case ConstructorInfo _: return "Constructor";
                case EventInfo _: return "Event";
                case PropertyInfo p when p.GetIndexParameters().Length > 0: return "Indexer";
                default: return m.MemberType.ToString();
            }
        }

        private sealed class Candidate
        {
            public Candidate(MemberInfo member, IMemberRule rule, Verdict verdict)
            {
                Member = member;
                Rule = rule;
                Verdict = verdict;
            }

            public MemberInfo Member { get; }
            public IMemberRule Rule { get; }
            public Verdict Verdict { get; set; }
        }

        private readonly struct Verdict
        {
            private Verdict(WrapStatus status, string reason)
            {
                Status = status;
                Reason = reason;
            }

            public WrapStatus Status { get; }
            public string Reason { get; }

            public static Verdict Of(WrapStatus status, string reason) => new Verdict(status, reason);
        }
    }
}
