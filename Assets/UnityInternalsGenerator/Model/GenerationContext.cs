namespace DandaUtils.Editor.UnityInternalsGenerator.Model
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using DandaUtils.Editor.UnityInternalsGenerator.Discovery;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;
    using DandaUtils.Editor.UnityInternalsGenerator.Rules;
    using DandaUtils.Editor.UnityInternalsGenerator.Selection;

    /// <summary>Shared state of one generation run: accessibility, memoized type classification, conversions and the report sink.</summary>
    public sealed class GenerationContext
    {
        private const string c_CompilerGeneratedAttribute = "System.Runtime.CompilerServices.CompilerGeneratedAttribute";
        private const string c_IsByRefLikeAttribute = "System.Runtime.CompilerServices.IsByRefLikeAttribute";

        private readonly GenerationInput m_Input;
        private readonly HashSet<Assembly> m_Sources;
        private readonly HashSet<string> m_AutoExcludedIds;
        private readonly IReadOnlyList<ITypeRule> m_TypeRules = TypeRules.CreateOrdered();
        private readonly IReadOnlyList<IConversionRule> m_ConversionRules = ConversionRules.CreateOrdered();
        private readonly Dictionary<Type, TypeClassification> m_Classifications = new Dictionary<Type, TypeClassification>();
        private readonly IReadOnlyList<IMemberRule> m_MemberRules = MemberRules.CreateOrdered();
        private readonly Dictionary<Type, MemberPlan> m_Plans = new Dictionary<Type, MemberPlan>();
        private readonly Dictionary<Type, Conversion> m_Conversions = new Dictionary<Type, Conversion>();
        private readonly Dictionary<Assembly, bool> m_Grants = new Dictionary<Assembly, bool>();
        private readonly Dictionary<Type, HashSet<string>> m_EmittedShapes = new Dictionary<Type, HashSet<string>>();
        private readonly List<ReportEntry> m_Entries = new List<ReportEntry>();
        private readonly Func<Type, string> m_WrappedTypeMap;
        private HashSet<string> m_EmittedTypeIds;
        private HashSet<string> m_EmittedMemberIds;

        public GenerationContext(GenerationInput input)
        {
            m_Input = input;
            m_Sources = new HashSet<Assembly>(input.Sources);
            m_AutoExcludedIds = new HashSet<string>(input.AutoExcludedIds, StringComparer.Ordinal);
            Naming = NamingStrategies.For(input.Naming);
            m_WrappedTypeMap = MapWrappedType;
        }

        public GenerationInput Input => m_Input;

        public INamingStrategy Naming { get; }

        public IReadOnlyList<ReportEntry> Entries => m_Entries.OrderBy(e => e.Id, StringComparer.Ordinal).ThenBy(e => e.Rule, StringComparer.Ordinal).ToList();

        public void Report(ReportEntry e) => m_Entries.Add(e);

        public bool IsAccessible(MethodBase m) => m.IsPublic || m.IsAssembly || m.IsFamilyOrAssembly;

        /// <summary>Not fully public, whole declaring chain public-or-internal, and the source assembly grants the host.</summary>
        public bool IsInternalCandidate(Type t) => t.IsVisible == false && IsChainAccessible(t) && SourceGrantsHost(t.Assembly);

        /// <summary>True when the type is declared in one of the source assemblies.</summary>
        public bool IsSourceType(Type t) => m_Sources.Contains(t.Assembly);

        public bool IsWrappedType(Type t) => IsClassifiable(t) && Classify(t).Status == WrapStatus.Wrapped;

        /// <summary>True when the type's wrapper holds a Unity value: class, struct and interface wrappers, not enums and not static classes.</summary>
        public bool IsInstanceWrapper(Type t) => IsWrappedType(t) && t.IsEnum == false && IsStaticClass(t) == false;

        /// <summary>
        /// The wrapper the wrapper of <paramref name="t"/> derives from: the nearest internal base emitted as an instance wrapper.
        /// Null when a public base comes first (then the wrapper is a root, see <see cref="PublicBase"/>) or when there is no base at all.
        /// </summary>
        public Type WrapperBase(Type t)
        {
            if (t == null || t.IsClass == false) return null;

            for (var b = t.BaseType; b != null; b = b.BaseType)
            {
                if (b.IsVisible) return null;
                if (IsEmitted(b) && IsInstanceWrapper(b)) return b;
            }
            return null;
        }

        /// <summary>First public base of the type, or null when that is <see cref="object"/> (a user-defined conversion to a base class is illegal) or there is none.</summary>
        public static Type PublicBase(Type t)
        {
            for (var b = t.BaseType; b != null; b = b.BaseType)
            {
                if (b.IsVisible) return b == typeof(object) ? null : b;
            }
            return null;
        }

        /// <summary>True when the member's emitted shape repeats a name (and, where overloadable, a signature) an emitted wrapper base already declares, so it needs <c>new</c>.</summary>
        public bool HidesWrapperBaseMember(MemberInfo m)
        {
            var declaring = m.DeclaringType;
            if (declaring == null) return false;

            var shape = EmittedShape(m);
            for (var b = WrapperBase(declaring); b != null; b = WrapperBase(b))
            {
                if (EmittedShapes(b).Contains(shape)) return true;
            }
            return false;
        }

        /// <summary>Memoized emitted shapes of one wrapper's own members; building them walks the whole parameter pipeline, so it happens once per type.</summary>
        private HashSet<string> EmittedShapes(Type t)
        {
            if (m_EmittedShapes.TryGetValue(t, out var cached)) return cached;

            var shapes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var planned in PlanFor(t))
            {
                if (IsEmitted(planned.Member)) shapes.Add(EmittedShape(planned.Member));
            }
            m_EmittedShapes[t] = shapes;
            return shapes;
        }

        /// <summary>Emitted name plus, where overloadable, the emitted parameter-type signature. Two members with the same shape cannot coexist on one type.</summary>
        private string EmittedShape(MemberInfo m) => MemberSyntax.WrappedName(m, this) + MemberSyntax.Signature(m, this);

        private static bool IsStaticClass(Type t) => t.IsClass && t.IsAbstract && t.IsSealed;

        /// <summary>True when the compile loop auto-excluded this exact type or member id after a compile failure.</summary>
        public bool IsAutoExcludedId(string id) => m_AutoExcludedIds.Contains(id);

        /// <summary>Restricts emission to the resolved set. Classification, conversions and plans are unaffected.</summary>
        public void ApplySelection(SelectionResult r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));

            m_EmittedTypeIds = new HashSet<string>(r.EmittedTypeIds, StringComparer.Ordinal);
            m_EmittedMemberIds = new HashSet<string>(r.EmittedMemberIds, StringComparer.Ordinal);
        }

        /// <summary>Wrapped and, once <see cref="ApplySelection"/> ran, in the emitted type set.</summary>
        public bool IsEmitted(Type t) => IsWrappedType(t) && (m_EmittedTypeIds == null || m_EmittedTypeIds.Contains(MemberId.Type(t)));

        /// <summary>For planned members: true until <see cref="ApplySelection"/> ran, then membership in the emitted member set.</summary>
        public bool IsEmitted(MemberInfo m) => m_EmittedMemberIds == null || m_EmittedMemberIds.Contains(MemberId.Member(m));

        public Conversion Convert(Type t)
        {
            if (m_Conversions.TryGetValue(t, out var cached)) return cached;
            var rule = m_ConversionRules.First(r => r.Match(t, this));
            var conversion = rule.Create(t, this);
            m_Conversions[t] = conversion;
            return conversion;
        }

        /// <summary>Wrapper-side C# reference: wrapped internal types are mapped to their generated types, everything else keeps its real name.</summary>
        public string WrappedTypeRef(Type t) => CSharpNames.TypeRef(t, m_WrappedTypeMap);

        /// <summary>Memoized. Reports the status to <see cref="Entries"/> exactly once per classifiable type.</summary>
        public TypeClassification Classify(Type t)
        {
            if (IsClassifiable(t) == false) return new TypeClassification(WrapStatus.Unsupported, "NotClassifiable", "Not a non-public type of a source assembly");
            if (m_Classifications.TryGetValue(t, out var cached)) return cached;

            var result = ClassifyCore(t);
            m_Classifications[t] = result;
            Report(new ReportEntry(MemberId.Type(t), result.RuleName, result.Status, result.Reason));
            return result;
        }

        /// <summary>Every type classified so far (after <see cref="ClassifyAll"/>: every non-public type of the sources), unordered.</summary>
        public IReadOnlyCollection<Type> ClassifiedTypes => m_Classifications.Keys;

        /// <summary>Memoized member plan of a Wrapped class; members are reported once, on first use. Empty for enums and types that are not Wrapped.</summary>
        public IReadOnlyList<PlannedMember> PlanFor(Type t) => PlanOf(t)?.Planned ?? Array.Empty<PlannedMember>();

        /// <summary>Every member considered for a Wrapped class with its verdict, same memo as <see cref="PlanFor"/>. Empty for enums and types that are not Wrapped.</summary>
        public IReadOnlyList<MemberVerdict> MemberVerdicts(Type t) => PlanOf(t)?.Verdicts ?? Array.Empty<MemberVerdict>();

        private MemberPlan PlanOf(Type t)
        {
            if (t.IsEnum || IsWrappedType(t) == false) return null;
            if (m_Plans.TryGetValue(t, out var cached)) return cached;

            var plan = MemberPlanner.Plan(t, m_MemberRules, this);
            m_Plans[t] = plan;
            return plan;
        }

        /// <summary>All non-public types of the source assemblies are classified (and reported); returns those Wrapped, sorted by FullName ordinal.</summary>
        public IReadOnlyList<Type> ClassifyAll()
        {
            var wrapped = new List<Type>();
            foreach (var assembly in m_Input.Sources)
            {
                foreach (var type in SafeGetTypes(assembly))
                {
                    if (IsClassifiable(type) && Classify(type).Status == WrapStatus.Wrapped) wrapped.Add(type);
                }
            }
            wrapped.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            return wrapped;
        }

        /// <summary>Turns a Wrapped type (and its Wrapped nested types) into NameCollision because its generated file path collides with another type's.</summary>
        public void RejectPathCollision(Type t, string path)
        {
            var reason = "file path collision: " + path;
            foreach (var type in m_Classifications.Keys.ToList())
            {
                if (IsSameOrNestedIn(type, t) == false || m_Classifications[type].Status != WrapStatus.Wrapped) continue;
                var id = MemberId.Type(type);
                var rejected = new TypeClassification(WrapStatus.NameCollision, "PathCollision", reason);
                m_Classifications[type] = rejected;
                var index = m_Entries.FindIndex(e => e.Id == id);
                if (index >= 0) m_Entries[index] = new ReportEntry(id, rejected.RuleName, rejected.Status, reason);
            }
        }

        private static bool IsSameOrNestedIn(Type type, Type outer)
        {
            for (var current = type; current != null; current = current.DeclaringType)
            {
                if (current == outer) return true;
            }
            return false;
        }

        private TypeClassification ClassifyCore(Type t)
        {
            if (IsCompilerGenerated(t)) return new TypeClassification(WrapStatus.Excluded, "CompilerGenerated", "Compiler-generated type");
            if (m_AutoExcludedIds.Contains(MemberId.Type(t))) return new TypeClassification(WrapStatus.Excluded, "AutoExclusion", "AutoExclusion: failed to compile");
            if (IsChainAccessible(t) == false) return new TypeClassification(WrapStatus.NotAccessible, "Accessibility", "Private or protected type");
            if (SourceGrantsHost(t.Assembly) == false) return new TypeClassification(WrapStatus.NoBridgeAccess, "BridgeAccess", "Assembly does not grant InternalsVisibleTo to " + m_Input.HostAssemblyName);
            if (t.IsGenericTypeDefinition) return new TypeClassification(WrapStatus.Unsupported, "GenericType", "GenericType");

            var declaring = t.DeclaringType;
            if (declaring != null)
            {
                if (declaring.IsVisible) return new TypeClassification(WrapStatus.Deferred, "NestedInPublicType", "PublicTypeInternalMembers (step 4)");
                var declaringStatus = Classify(declaring).Status;
                if (declaringStatus == WrapStatus.Excluded || declaringStatus == WrapStatus.NotAccessible) return new TypeClassification(declaringStatus, "NestedInUnwrappedType", Classify(declaring).RuleName == "AutoExclusion" ? "Declaring type auto-excluded: failed to compile" : "Declaring type is " + declaringStatus);
                if (declaringStatus != WrapStatus.Wrapped) return new TypeClassification(WrapStatus.Deferred, "NestedInUnwrappedType", "Declaring type is " + declaringStatus);

                var nestedName = CSharpNames.TypeDeclName(t);
                if (IsInstanceWrapper(declaring) && WrapperSyntax.IsReserved(nestedName)) return new TypeClassification(WrapStatus.NameCollision, "ReservedName", "Name reserved by the wrapper '" + nestedName + "'");
            }

            if (t.IsEnum == false && typeof(Delegate).IsAssignableFrom(t)) return new TypeClassification(WrapStatus.Deferred, "Delegate", "Delegate wrapper (step 3)");

            // A ref struct cannot be a wrapper field and cannot be boxed, so no wrapper shape fits it. The conversion rules reject it in signature position too.
            if (IsByRefLike(t)) return new TypeClassification(WrapStatus.Unsupported, "RefStruct", "ref struct");

            foreach (var rule in m_TypeRules)
            {
                if (rule.Match(t, this)) return new TypeClassification(WrapStatus.Wrapped, rule.Name, null, rule.Generator);
            }
            return new TypeClassification(WrapStatus.Unsupported, "NoRule", "No type rule matched");
        }

        // Plain non-public, non-constructed types declared in a source assembly; everything else never gets a classification.
        private bool IsClassifiable(Type t)
        {
            if (t.HasElementType || t.IsGenericParameter || t.IsVisible) return false;
            if (t.IsGenericType && t.IsGenericTypeDefinition == false) return false;
            return IsSourceType(t);
        }

        private bool SourceGrantsHost(Assembly a)
        {
            if (m_Grants.TryGetValue(a, out var granted)) return granted;
            granted = m_Sources.Contains(a) && BridgeAccess.Grants(a, m_Input.HostAssemblyName);
            m_Grants[a] = granted;
            return granted;
        }

        private static bool IsChainAccessible(Type t)
        {
            for (var current = t; current != null; current = current.DeclaringType)
            {
                var ok = current.DeclaringType == null
                    ? current.IsPublic || current.IsNotPublic
                    : current.IsNestedPublic || current.IsNestedAssembly || current.IsNestedFamORAssem;
                if (ok == false) return false;
            }
            return true;
        }

        /// <summary>True when the type or any declaring type is compiler-generated (name contains '&lt;' or carries [CompilerGenerated]).</summary>
        internal static bool IsCompilerGenerated(Type t)
        {
            for (var current = t; current != null; current = current.DeclaringType)
            {
                if (IsCompilerGeneratedMember(current)) return true;
            }
            return false;
        }

        /// <summary>True when the type is a <c>ref struct</c> (ByRefLike): it can only live on the stack, so it fits neither a wrapper field nor a boxed comparison.</summary>
        public static bool IsByRefLike(Type t) => t.IsValueType && t.GetCustomAttributesData().Any(a => a.AttributeType.FullName == c_IsByRefLikeAttribute);

        /// <summary>True when the member itself (not its declaring chain) is compiler-generated.</summary>
        internal static bool IsCompilerGeneratedMember(MemberInfo m) => m.Name.Contains('<') || m.GetCustomAttributesData().Any(a => a.AttributeType.FullName == c_CompilerGeneratedAttribute);

        private string MapWrappedType(Type t) => IsWrappedType(t) ? "global::" + NamespaceMapper.Map(t.Namespace) + "." + NestedChain(t) : null;

        private static string NestedChain(Type t) => t.DeclaringType == null ? CSharpNames.TypeDeclName(t) : NestedChain(t.DeclaringType) + "." + CSharpNames.TypeDeclName(t);

        /// <summary>All loadable types of the assembly; types that fail to load are skipped.</summary>
        internal static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(x => x != null);
            }
        }
    }
}
