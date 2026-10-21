namespace DandaUtils.Editor.UnityInternalsGenerator.Naming
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public static class CSharpNames
    {
        private static readonly Dictionary<Type, string> s_Keywords = new Dictionary<Type, string>
        {
            { typeof(void), "void" },
            { typeof(bool), "bool" },
            { typeof(byte), "byte" },
            { typeof(sbyte), "sbyte" },
            { typeof(char), "char" },
            { typeof(decimal), "decimal" },
            { typeof(double), "double" },
            { typeof(float), "float" },
            { typeof(int), "int" },
            { typeof(uint), "uint" },
            { typeof(long), "long" },
            { typeof(ulong), "ulong" },
            { typeof(short), "short" },
            { typeof(ushort), "ushort" },
            { typeof(object), "object" },
            { typeof(string), "string" },
        };

        private static readonly HashSet<string> s_ReservedWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
            "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
            "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
            "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
            "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
            "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        };

        public static string TypeRef(Type t, Func<Type, string> overrideMap = null)
        {
            var mapped = overrideMap?.Invoke(t);
            if (mapped != null) return mapped;

            if (t.IsByRef) return TypeRef(t.GetElementType(), overrideMap);
            if (t.IsPointer) return TypeRef(t.GetElementType(), overrideMap) + "*";
            if (t.IsArray) return ArrayRef(t, overrideMap);
            if (t.IsGenericParameter) return t.Name;
            if (s_Keywords.TryGetValue(t, out var keyword)) return keyword;

            return "global::" + QualifiedName(t, overrideMap);
        }

        public static string Identifier(string name) => s_ReservedWords.Contains(name) ? "@" + name : name;

        public static string TypeDeclName(Type t)
        {
            var name = t.Name;
            var tick = name.IndexOf('`');
            return tick < 0 ? name : name.Substring(0, tick);
        }

        private static string ArrayRef(Type t, Func<Type, string> overrideMap)
        {
            var rank = t.GetArrayRank();
            var suffix = rank == 1 ? (t.IsSZArray ? "[]" : "[*]") : "[" + new string(',', rank - 1) + "]";
            return TypeRef(t.GetElementType(), overrideMap) + suffix;
        }

        // Namespace + nested chain; generic arguments are attached to the segment that declares them.
        private static string QualifiedName(Type t, Func<Type, string> overrideMap)
        {
            var args = t.IsGenericType ? t.GetGenericArguments() : Type.EmptyTypes;
            var declaring = t.DeclaringType;
            var prefix = declaring != null ? QualifiedName(declaring, overrideMap) + "." : (string.IsNullOrEmpty(t.Namespace) ? "" : t.Namespace + ".");

            var ownCount = args.Length - (declaring != null && declaring.IsGenericType ? declaring.GetGenericArguments().Length : 0);
            var sb = new StringBuilder(prefix).Append(TypeDeclName(t));
            if (ownCount <= 0) return sb.ToString();

            sb.Append('<');
            for (var i = args.Length - ownCount; i < args.Length; i++)
            {
                if (i > args.Length - ownCount) sb.Append(", ");
                sb.Append(TypeRef(args[i], overrideMap));
            }
            return sb.Append('>').ToString();
        }
    }
}
