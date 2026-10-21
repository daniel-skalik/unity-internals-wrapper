namespace DandaUtils.Editor.UnityInternalsGenerator.Output
{
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;

    public static class MetaFiles
    {
        private const string c_GuidSeed = "DandaUtils.UnityInternalsGenerator:";

        public static string Guid(string projectRelativePath)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(c_GuidSeed + projectRelativePath));
                var sb = new StringBuilder(32);
                foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static string ForScript(string guid) =>
            "fileFormatVersion: 2\nguid: " + guid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";

        public static string ForFolder(string guid) =>
            "fileFormatVersion: 2\nguid: " + guid + "\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";

        public static string ForAsmdef(string guid) =>
            "fileFormatVersion: 2\nguid: " + guid + "\nAssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";

        public static string ForText(string guid) =>
            "fileFormatVersion: 2\nguid: " + guid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
    }
}
