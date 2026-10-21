namespace DandaUtils.Editor.UnityInternalsGenerator.Model
{
    using System;
    using System.Globalization;
    using System.Reflection;

    /// <summary>Stable ids: Assembly|Namespace.Outer+Inner|Kind|Name(ParamType,...)</summary>
    public static class MemberId
    {
        public static string Type(Type t) => Compose(t, "Type", t.Name);

        public static string Member(MemberInfo m)
        {
            var declaring = m.DeclaringType;
            switch (m)
            {
                case ConstructorInfo c: return Compose(declaring, "Ctor", c.Name + Params(c));
                case MethodInfo method: return Compose(declaring, "Method", GenericName(method) + Params(method));
                case PropertyInfo _: return Compose(declaring, "Property", m.Name);
                case FieldInfo f: return Compose(declaring, f.IsLiteral ? "Const" : "Field", m.Name);
                case EventInfo _: return Compose(declaring, "Event", m.Name);
                case System.Type nested: return Type(nested);
                default: throw new ArgumentException("Unsupported member kind: " + m.MemberType, nameof(m));
            }
        }

        /// <summary>Type id of the declaring type of a member id ("Asm|Ns.Outer+Inner|Method|X()" gives "Asm|Ns.Outer+Inner|Type|Inner"); null for type ids and unparsable ids.</summary>
        public static string DeclaringTypeId(string memberId)
        {
            if (memberId == null) return null;

            var parts = memberId.Split(new[] { '|' }, 4);
            if (parts.Length < 4 || parts[2] == "Type") return null;

            var fullName = parts[1];
            var simple = fullName.Substring(Math.Max(fullName.LastIndexOf('.'), fullName.LastIndexOf('+')) + 1);
            return parts[0] + "|" + fullName + "|Type|" + simple;
        }

        private static string Compose(Type owner, string kind, string name) => owner.Assembly.GetName().Name + "|" + (owner.FullName ?? owner.Name) + "|" + kind + "|" + name;

        private static string GenericName(MethodInfo m) => m.IsGenericMethodDefinition ? m.Name + "``" + m.GetGenericArguments().Length.ToString(CultureInfo.InvariantCulture) : m.Name;

        private static string Params(MethodBase m)
        {
            var ps = m.GetParameters();
            var names = new string[ps.Length];
            for (var i = 0; i < ps.Length; i++) names[i] = Render(ps[i].ParameterType);
            return "(" + string.Join(",", names) + ")";
        }

        // Runtime-independent type rendering: no assembly qualification, no Type.FullName for constructed generics.
        private static string Render(Type t)
        {
            if (t.IsByRef) return Render(t.GetElementType()) + "&";
            if (t.IsPointer) return Render(t.GetElementType()) + "*";
            if (t.IsArray)
            {
                var rank = t.GetArrayRank();
                return Render(t.GetElementType()) + (rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]");
            }
            if (t.IsGenericParameter) return (t.DeclaringMethod != null ? "!!" : "!") + t.GenericParameterPosition.ToString(CultureInfo.InvariantCulture);
            if (t.IsGenericType == false) return t.FullName ?? t.Name;

            var definition = t.GetGenericTypeDefinition().FullName;
            var args = t.GetGenericArguments();
            var rendered = new string[args.Length];
            for (var i = 0; i < args.Length; i++) rendered[i] = Render(args[i]);
            return definition + "[" + string.Join(";", rendered) + "]";
        }
    }
}
