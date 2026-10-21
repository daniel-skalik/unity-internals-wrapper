namespace DandaUtils.Editor.UnityInternalsGenerator.Naming
{
    public sealed class ExactNaming : INamingStrategy
    {
        public string MemberName(string original) => original;
    }
}
