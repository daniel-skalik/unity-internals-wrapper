namespace DandaUtils.Editor.UnityInternalsGenerator
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using DandaUtils.Editor.UnityInternalsGenerator.Discovery;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Output;
    using DandaUtils.Editor.UnityInternalsGenerator.Pipeline;
    using DandaUtils.Editor.UnityInternalsGenerator.Selection;
    using DandaUtils.Editor.UnityInternalsGenerator.Settings;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Orchestrates one generation: resolve sources, choose host, run, write, refresh, log.</summary>
    public static class GenerationCommand
    {
        private const string c_ProgressTitle = "Unity Internals Generator";
        private const int c_MaxLoggedErrors = 20;

        /// <summary>SessionState key holding the last summary until the next domain reload logs it.</summary>
        public const string c_PendingSummaryKey = "DandaUtils.UnityInternalsGenerator.PendingSummary";

        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        public static string OutputReportPath => ProjectRoot + "/" + UnityInternalsGeneratorSettings.instance.OutputFolder.Trim('/') + "/_GenerationReport.md";
        public static string FailedReportPath => ProjectRoot + "/Temp/UnityInternalsGenerator/_LastFailedReport.md";

        public static void Execute()
        {
            try
            {
                EditorUtility.DisplayProgressBar(c_ProgressTitle, "Preparing", 0.05f);
                ExecuteCore();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void ExecuteCore()
        {
            var settings = UnityInternalsGeneratorSettings.instance;
            var projectRoot = ProjectRoot;
            var outputFolder = settings.OutputFolder.Trim('/');

            var input = CreateInput(out var error, out var noAccess, true);
            if (input == null)
            {
                Debug.LogError("[UnityInternalsGenerator] " + error);
                return;
            }
            if (settings.Selection.IsEmpty) Debug.LogWarning("[UnityInternalsGenerator] Nothing selected — open Tools/DandaUtils/Unity Internals/Browser");

            var host = input.HostAssemblyName;
            var granted = input.Sources;
            var unityVersion = input.UnityVersion;

            // Sources that do not grant the chosen host cannot be wrapped: one NoBridgeAccess entry per such assembly, id "<AssemblyName>|*".
            var extraEntries = noAccess
                .Select(s => new ReportEntry(s.GetName().Name + "|*", "BridgeAccess", WrapStatus.NoBridgeAccess, "Assembly does not grant InternalsVisibleTo '" + host + "'."))
                .ToList();

            EditorUtility.DisplayProgressBar(c_ProgressTitle, "Generating and pre-compiling", 0.3f);
            var references = CompilerReferences.Collect(granted);
            var result = GenerationRunner.Run(input, new RoslynProcessCompiler(), references, settings.MaxCompilePasses);

            var entries = result.Entries.Concat(extraEntries).ToList();
            var (md, json) = ReportWriter.Build(entries, unityVersion, host);

            if (result.Success == false)
            {
                ReportFailure(result, md);
                return;
            }

            EditorUtility.DisplayProgressBar(c_ProgressTitle, "Writing files", 0.8f);
            var info = "{\n    \"generatorVersion\": " + GeneratorInfo.Version + ",\n    \"unityVersion\": " + ReportWriter.Quote(unityVersion) + ",\n    \"host\": " + ReportWriter.Quote(host) + "\n}\n";
            OutputWriter.Write(projectRoot, outputFolder, host, result.Files, info, md, json);

            // The refresh recompiles the output assembly and reloads the domain, so also hand the summary over via SessionState.
            var summary = BuildSummary(entries, result, host, outputFolder);
            Debug.Log(summary);
            SessionState.SetString(c_PendingSummaryKey, summary);
            AssetDatabase.Refresh();
        }

        /// <summary>Resolves sources, chooses the host (persisting a changed choice when <paramref name="persistHost"/>), and builds the input from the stored selection. Returns null and sets <paramref name="error"/> on failure.</summary>
        public static GenerationInput CreateInput(out string error, bool persistHost = true) => CreateInput(out error, out _, persistHost);

        private static GenerationInput CreateInput(out string error, out List<Assembly> noAccess, bool persistHost)
        {
            error = null;
            noAccess = new List<Assembly>();
            var settings = UnityInternalsGeneratorSettings.instance;
            var projectRoot = ProjectRoot;
            var outputFolder = settings.OutputFolder.Trim('/');

            var sources = ResolveSources(settings.IncludedAssemblies, out error);
            if (sources == null) return null;

            var used = UsedAssemblyNames.Collect(projectRoot, projectRoot + "/" + outputFolder + "/WrappedUnityInternals.asmdef");
            var grants = sources.Select(BridgeAccess.GetGrantedFriends).ToList();
            var host = BridgeAccess.ChooseHostName(grants, used, settings.HostAssemblyName, out var warning);
            if (warning != null) Debug.LogWarning("[UnityInternalsGenerator] " + warning);
            if (host == null)
            {
                error = "No usable bridge assembly name granted by all included assemblies.";
                return null;
            }
            if (persistHost && host != settings.HostAssemblyName)
            {
                settings.HostAssemblyName = host;
                settings.Save();
            }

            noAccess = sources.Where(s => BridgeAccess.Grants(s, host) == false).ToList();
            var granted = sources.Except(noAccess).ToList();
            return new GenerationInput(granted, host, settings.NamingStyle, settings.Selection, Application.unityVersion);
        }

        private static List<Assembly> ResolveSources(IReadOnlyList<string> names, out string error)
        {
            error = null;
            var loaded = AppDomain.CurrentDomain.GetAssemblies();
            var sources = new List<Assembly>();
            var ok = true;
            foreach (var name in names)
            {
                var assembly = loaded.FirstOrDefault(a => a.IsDynamic == false && a.GetName().Name == name);
                if (assembly == null)
                {
                    error = "Included assembly not loaded: " + name;
                    ok = false;
                    continue;
                }
                sources.Add(assembly);
            }
            return ok ? sources : null;
        }

        private static void ReportFailure(RunResult result, string md)
        {
            var path = FailedReportPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, md);
            var shown = string.Join("\n", result.RemainingErrors.Take(c_MaxLoggedErrors).Select(e => e.ToString()));
            Debug.LogError("[UnityInternalsGenerator] Generation failed after " + result.Passes + " pass(es); nothing was written. " + result.RemainingErrors.Count + " compile error(s), first " + Math.Min(c_MaxLoggedErrors, result.RemainingErrors.Count) + ":\n" + shown + "\nReport: " + path);
        }

        private static string BuildSummary(IReadOnlyList<ReportEntry> entries, RunResult result, string host, string outputFolder)
        {
            // Same counts as the md report table: selected entries per status, then NotSelected.
            var counts = string.Join(", ", ReportWriter.StatusCounts(entries).Select(p => p.Key + "=" + p.Value));
            return "[UnityInternalsGenerator] Done. Host '" + host + "', " + result.Files.Count + " file(s), " + result.Passes + " compile pass(es) -> " + outputFolder + "\n" + counts;
        }
    }
}
