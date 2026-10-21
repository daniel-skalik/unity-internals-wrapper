namespace DandaUtils.Editor.UnityInternalsGenerator.Naming
{
    using System.Globalization;

    public sealed class PascalCaseNaming : INamingStrategy
    {
        public string MemberName(string original)
        {
            if (string.IsNullOrEmpty(original)) return original;
            return char.ToUpper(original[0], CultureInfo.InvariantCulture) + original.Substring(1);
        }
    }
}
