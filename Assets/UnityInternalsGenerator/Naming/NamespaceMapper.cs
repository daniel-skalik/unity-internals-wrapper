namespace DandaUtils.Editor.UnityInternalsGenerator.Naming
{
    public static class NamespaceMapper
    {
        private const string c_GlobalNamespace = "WrappedGlobal";

        public static string Map(string unityNamespace)
        {
            if (string.IsNullOrEmpty(unityNamespace)) return c_GlobalNamespace;
            return "Wrapped" + unityNamespace;
        }
    }
}
