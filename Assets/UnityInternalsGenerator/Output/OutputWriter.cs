namespace DandaUtils.Editor.UnityInternalsGenerator.Output
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using DandaUtils.Editor.UnityInternalsGenerator.Emit;

    /// <summary>Writes generator-owned files deterministically. Plain System.IO; the caller refreshes the AssetDatabase.</summary>
    public static class OutputWriter
    {
        private const string c_AsmdefName = "WrappedUnityInternals.asmdef";
        private const string c_InfoName = "GenerationInfo.json";
        private const string c_ReportMdName = "_GenerationReport.md";
        private const string c_ReportJsonName = "_GenerationReport.json";
        private const string c_GeneratedSuffix = ".g.cs";

        private static readonly UTF8Encoding s_Utf8 = new UTF8Encoding(false);

        public static void Write(string projectRoot, string outputFolder, string hostAssemblyName, IReadOnlyList<GeneratedFile> files, string generationInfoJson, string reportMd, string reportJson)
        {
            foreach (var f in files) ValidatePath(f.RelativePath);
            var folder = outputFolder.Replace('\\', '/').Trim('/');
            var contents = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in files) contents[f.RelativePath.Replace('\\', '/')] = f.Content;
            contents[c_AsmdefName] = BuildAsmdef(hostAssemblyName);
            contents[c_InfoName] = generationInfoJson;
            contents[c_ReportMdName] = reportMd;
            contents[c_ReportJsonName] = reportJson;

            EnsureFolder(projectRoot, folder);
            foreach (var pair in contents)
            {
                var assetPath = folder + "/" + pair.Key;
                EnsureParentFolders(projectRoot, folder, pair.Key);
                var full = ToFull(projectRoot, assetPath);
                if (pair.Key != c_AsmdefName || KeepsExistingAsmdef(full, hostAssemblyName) == false) WriteIfChanged(full, pair.Value);
                EnsureMeta(full + ".meta", MetaFor(assetPath));
            }

            DeleteStale(projectRoot, folder, contents.Keys);
        }

        // Unity rewrites the asmdef after import (it adds "references" GUIDs); an existing asmdef of the same name is left alone so repeated runs stay stable.
        private static bool KeepsExistingAsmdef(string fullPath, string host) => File.Exists(fullPath) && File.ReadAllText(fullPath).Contains("\"name\": " + ReportWriter.Quote(host) + ",");

        private static string BuildAsmdef(string host) =>
            "{\n" +
            "    \"name\": " + ReportWriter.Quote(host) + ",\n" +
            "    \"rootNamespace\": \"\",\n" +
            "    \"references\": [],\n" +
            "    \"includePlatforms\": [\n        \"Editor\"\n    ],\n" +
            "    \"excludePlatforms\": [],\n" +
            "    \"allowUnsafeCode\": false,\n" +
            "    \"overrideReferences\": false,\n" +
            "    \"precompiledReferences\": [],\n" +
            "    \"autoReferenced\": true,\n" +
            "    \"defineConstraints\": [],\n" +
            "    \"versionDefines\": [],\n" +
            "    \"noEngineReferences\": false\n" +
            "}\n";

        private static string MetaFor(string assetPath)
        {
            var guid = MetaFiles.Guid(assetPath);
            if (assetPath.EndsWith(".cs", StringComparison.Ordinal)) return MetaFiles.ForScript(guid);
            if (assetPath.EndsWith(".asmdef", StringComparison.Ordinal)) return MetaFiles.ForAsmdef(guid);
            return MetaFiles.ForText(guid);
        }

        private static string ToFull(string projectRoot, string assetPath) => Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));

        private static void EnsureFolder(string projectRoot, string assetPath)
        {
            var full = ToFull(projectRoot, assetPath);
            Directory.CreateDirectory(full);
            EnsureMeta(full + ".meta", MetaFiles.ForFolder(MetaFiles.Guid(assetPath)));
        }

        private static void EnsureParentFolders(string projectRoot, string root, string relativeFile)
        {
            var parts = relativeFile.Split('/');
            var current = root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                current += "/" + parts[i];
                EnsureFolder(projectRoot, current);
            }
        }

        private static void WriteIfChanged(string fullPath, string content)
        {
            var bytes = s_Utf8.GetBytes(content);
            if (File.Exists(fullPath) && File.ReadAllBytes(fullPath).AsSpan().SequenceEqual(bytes)) return;
            File.WriteAllBytes(fullPath, bytes);
        }

        private static void EnsureMeta(string metaFullPath, string content)
        {
            if (File.Exists(metaFullPath)) return;
            File.WriteAllBytes(metaFullPath, s_Utf8.GetBytes(content));
        }

        private static void ValidatePath(string relativePath)
        {
            var normalized = relativePath.Replace('\\', '/');
            if (string.IsNullOrEmpty(normalized) || normalized.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(relativePath) || normalized.Contains(":"))
                throw new ArgumentException("Generated file path must be relative: " + relativePath, nameof(relativePath));
            foreach (var segment in normalized.Split('/'))
            {
                if (segment == ".." || segment == ".")
                    throw new ArgumentException("Generated file path must not contain '.' or '..' segments: " + relativePath, nameof(relativePath));
            }
        }

        private static void DeleteStale(string projectRoot, string folder, IEnumerable<string> keptRelative)
        {
            var rootFull = ToFull(projectRoot, folder);
            var kept = new HashSet<string>(keptRelative, StringComparer.Ordinal);
            var touchedDirs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(rootFull, "*" + c_GeneratedSuffix, SearchOption.AllDirectories).Concat(Directory.GetFiles(rootFull, "*" + c_GeneratedSuffix + ".meta", SearchOption.AllDirectories)))
            {
                var rel = file.Substring(rootFull.Length).TrimStart('\\', '/').Replace('\\', '/');
                if (rel.EndsWith(".meta", StringComparison.Ordinal)) rel = rel.Substring(0, rel.Length - 5);
                if (kept.Contains(rel)) continue;
                File.Delete(file);
                touchedDirs.Add(Path.GetDirectoryName(file));
            }

            var rootNormalized = Path.GetFullPath(rootFull).TrimEnd('\\', '/');
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dir in touchedDirs)
            {
                var current = Path.GetFullPath(dir).TrimEnd('\\', '/');
                while (string.Equals(current, rootNormalized, StringComparison.OrdinalIgnoreCase) == false && current.Length > rootNormalized.Length)
                {
                    candidates.Add(current);
                    current = Path.GetDirectoryName(current);
                }
            }

            foreach (var dir in candidates.OrderByDescending(d => d.Length))
            {
                if (Directory.Exists(dir) == false || Directory.GetFileSystemEntries(dir).Length > 0) continue;
                Directory.Delete(dir);
                if (File.Exists(dir + ".meta")) File.Delete(dir + ".meta");
            }
        }
    }
}
