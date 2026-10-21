namespace DandaUtils.Editor.UnityInternalsGenerator.Discovery
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor.Compilation;
    using UnityEngine;

    public static class UsedAssemblyNames
    {
        [Serializable]
        private sealed class AsmdefName
        {
            public string name = null;
        }

        public static ISet<string> Collect(string projectRoot, string ownOutputAsmdefPath)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var own = string.IsNullOrEmpty(ownOutputAsmdefPath) ? null : Normalize(ownOutputAsmdefPath);

            foreach (var folder in new[] { "Assets", "Packages", "Library/PackageCache" })
            {
                var path = Path.Combine(projectRoot, folder);
                if (Directory.Exists(path) == false) continue;
                foreach (var file in EnumerateAsmdefs(path))
                {
                    if (own != null && string.Equals(Normalize(file), own, StringComparison.OrdinalIgnoreCase)) continue;
                    var name = ReadName(file);
                    if (string.IsNullOrEmpty(name) == false) result.Add(name);
                }
            }

            foreach (var assembly in CompilationPipeline.GetAssemblies())
            {
                var asmdef = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly.name);
                if (own != null && asmdef != null && string.Equals(Normalize(Path.Combine(projectRoot, asmdef)), own, StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(assembly.name);
            }
            return result;
        }

        private static List<string> EnumerateAsmdefs(string path)
        {
            var files = new List<string>();
            try
            {
                files.AddRange(Directory.EnumerateFiles(path, "*.asmdef", SearchOption.AllDirectories));
            }
            catch (Exception)
            {
                // partial results are fine; unreadable subtree is skipped
            }
            return files;
        }

        private static string ReadName(string file)
        {
            try
            {
                return JsonUtility.FromJson<AsmdefName>(File.ReadAllText(file))?.name;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Normalize(string path) => Path.GetFullPath(path).Replace(Path.DirectorySeparatorChar, '/');
    }
}
