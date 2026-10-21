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

        private static IEnumerable<MemberInfo> DeclaredMembers(Type t)
        {
            var properties = ByToken(t.GetProperties(c_Declared));
            var events = ByToken(t.GetEvents(c_Declared));
            var accessors = new HashSet<MethodInfo>(properties.SelectMany(p => p.GetAccessors(true))
                .Concat(events.SelectMany(e => new[] { e.GetAddMethod(true), e.GetRemoveMethod(true), e.GetRaiseMethod(true) }))
                .Where(a => a != null));

            var ctors = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Cast<MemberInfo>().ToList();
            if (t.TypeInitializer != null) ctors.Add(t.TypeInitializer);

            return ByToken(t.GetFields(c_Declared)).Cast<MemberInfo>()
                .Concat(properties)
                .Concat(events)
                .Concat(ByToken(ctors))
                .Concat(ByToken(t.GetMethods(c_Declared)).Where(m => accessors.Contains(m) == false));
        }

        private static List<T> ByToken<T>(IEnumerable<T> members) where T : MemberInfo => members.OrderBy(m => m.MetadataToken).ToList();

        private static Verdict Check(MemberInfo m, IMemberRule rule, GenerationContext ctx)
        {
            if (ctx.IsAutoExcludedId(MemberId.Member(m))) return Verdict.Of(WrapStatus.Excluded, "AutoExclusion: failed to compile");
            if (IsCompilerGenerated(m)) return Verdict.Of(WrapStatus.Excluded, "CompilerGenerated");
            if (m is ConstructorInfo c && c.IsStatic) return Verdict.Of(WrapStatus.Excluded, "StaticConstructor");
            if (IsAccessible(m, ctx) == false) return Verdict.Of(WrapStatus.NotAccessible, "Private or protected member");
            if (IsStatic(m) == false) return Verdict.Of(WrapStatus.Deferred, "InstanceMembers (step 2)");
            if (m.GetCustomAttribute<ObsoleteAttribute>(false)?.IsError == true) return Verdict.Of(WrapStatus.Excluded, "ObsoleteError");
            if (m is EventInfo) return Verdict.Of(WrapStatus.Deferred, "Event (step 3)");
            if (rule == null) return Verdict.Of(WrapStatus.Unsupported, "No member rule matched");

            switch (m)
            {
                case MethodInfo method: return CheckMethod(method, ctx);
                case PropertyInfo property: return property.GetIndexParameters().Length > 0 ? Verdict.Of(WrapStatus.Deferred, "Indexer (step 2)") : FromConversion(ctx.Convert(property.PropertyType));
                case FieldInfo field: return CheckField(field, ctx);
                default: return Verdict.Of(WrapStatus.Unsupported, "Unsupported member kind");
            }
        }

        private static Verdict CheckMethod(MethodInfo m, GenerationContext ctx)
        {
            if (m.IsSpecialName) return m.Name.StartsWith("op_", StringComparison.Ordinal) ? Verdict.Of(WrapStatus.Deferred, "Operator (step 3)") : Verdict.Of(WrapStatus.Unsupported, "SpecialName");
            if (m.IsGenericMethodDefinition) return Verdict.Of(WrapStatus.Deferred, "GenericMethod (step 3)");
            if ((m.CallingConvention & CallingConventions.VarArgs) != 0) return Verdict.Of(WrapStatus.Unsupported, "VarArgs");

            var returns = ctx.Convert(m.ReturnType);
            if (returns.Status != WrapStatus.Wrapped) return FromConversion(returns);
            foreach (var p in m.GetParameters())
            {
                var conversion = ctx.Convert(p.ParameterType);
                if (conversion.Status != WrapStatus.Wrapped) return FromConversion(conversion);
            }

            MemberSyntax.Parameters(m, ctx, out var droppedDefault);
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
        private static void MarkCollisions(Type t, List<Candidate> candidates, GenerationContext ctx)
        {
            var typeName = CSharpNames.TypeDeclName(t);
            var nestedNames = new HashSet<string>(t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Where(ctx.IsWrappedType).Select(CSharpNames.TypeDeclName), StringComparer.Ordinal);
            var groups = candidates.Where(c => c.Verdict.Status == WrapStatus.Wrapped).GroupBy(c => MemberSyntax.WrappedName(c.Member, ctx), StringComparer.Ordinal);

            foreach (var group in groups)
            {
                var name = group.Key;
                var members = group.ToList();
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
                foreach (var overload in members.GroupBy(c => MethodSignature((MethodInfo)c.Member, ctx), StringComparer.Ordinal).Where(g => g.Count() > 1))
                {
                    Collide(overload.ToList(), "Duplicate signature '" + name + overload.Key + "'");
                }
            }
        }

        private static void Collide(List<Candidate> members, string reason)
        {
            foreach (var c in members) c.Verdict = Verdict.Of(WrapStatus.NameCollision, reason);
        }

        private static string MethodSignature(MethodInfo m, GenerationContext ctx) => "(" + string.Join(", ", MemberSyntax.Parameters(m, ctx, out _).Select(p => p.SignatureType)) + ")";

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

        private static bool IsStatic(MemberInfo m)
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

        private static bool IsCompilerGenerated(MemberInfo m) => m.Name.IndexOf('<') >= 0 || m.GetCustomAttributesData().Any(a => a.AttributeType.FullName == c_CompilerGeneratedAttribute);

        private static string KindName(MemberInfo m)
        {
            switch (m)
            {
                case ConstructorInfo _: return "Constructor";
                case EventInfo _: return "Event";
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
