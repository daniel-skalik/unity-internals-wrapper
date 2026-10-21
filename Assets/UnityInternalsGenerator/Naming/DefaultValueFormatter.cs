namespace DandaUtils.Editor.UnityInternalsGenerator.Naming
{
    using System;
    using System.Globalization;
    using System.Reflection;
    using System.Text;

    public static class DefaultValueFormatter
    {
        public static bool TryFormat(ParameterInfo p, string typeRef, out string literal)
        {
            literal = null;
            if (p.HasDefaultValue == false) return false;

            return TryFormatValue(p.RawDefaultValue, p.ParameterType, typeRef, out literal);
        }

        /// <summary>Formats a raw constant (enum constants as their underlying number) of <paramref name="type"/> as a C# literal.</summary>
        public static bool TryFormatValue(object raw, Type type, string typeRef, out string literal)
        {
            if (raw == null || raw is DBNull || raw is Missing)
            {
                literal = type.IsValueType ? "default" : "null";
                return true;
            }

            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (underlying.IsEnum)
            {
                var number = Convert.ToString(raw, CultureInfo.InvariantCulture);
                literal = "(" + typeRef + ")" + (number.StartsWith("-", StringComparison.Ordinal) ? "(" + number + ")" : number);
                return true;
            }

            return TryFormatPrimitive(raw, out literal);
        }

        private static bool TryFormatPrimitive(object raw, out string literal)
        {
            var inv = CultureInfo.InvariantCulture;
            switch (raw)
            {
                case bool b: literal = b ? "true" : "false"; return true;
                case string s: literal = Quote(s); return true;
                case char c: literal = "'" + EscapeChar(c, '\'') + "'"; return true;
                case float f: literal = FormatFloat(f, "float", f.ToString("R", inv), "f"); return true;
                case double d: literal = FormatFloat(d, "double", d.ToString("R", inv), "d"); return true;
                case decimal m: literal = m.ToString(inv) + "m"; return true;
                case int i: literal = i == int.MinValue ? "int.MinValue" : i.ToString(inv); return true;
                case long l: literal = l == long.MinValue ? "long.MinValue" : l.ToString(inv) + "L"; return true;
                case uint u: literal = u.ToString(inv) + "u"; return true;
                case ulong ul: literal = ul.ToString(inv) + "UL"; return true;
                case byte or sbyte or short or ushort: literal = Convert.ToString(raw, inv); return true;
                default: literal = null; return false;
            }
        }

        private static string FormatFloat(double value, string keyword, string text, string suffix)
        {
            if (double.IsNaN(value)) return keyword + ".NaN";
            if (double.IsPositiveInfinity(value)) return keyword + ".PositiveInfinity";
            if (double.IsNegativeInfinity(value)) return keyword + ".NegativeInfinity";
            return text + suffix;
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s) sb.Append(EscapeChar(c, '"'));
            return sb.Append('"').ToString();
        }

        private static string EscapeChar(char c, char quote)
        {
            if (c == quote) return "\\" + c;
            switch (c)
            {
                case '\\': return "\\\\";
                case '\n': return "\\n";
                case '\r': return "\\r";
                case '\t': return "\\t";
                case '\0': return "\\0";
            }
            if (char.IsControl(c) || char.IsSurrogate(c) || c == '\u2028' || c == '\u2029') return "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture);
            return c.ToString();
        }
    }
}
