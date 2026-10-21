namespace DandaUtils.Editor.UnityInternalsGenerator
{
    using System;
    using System.IO;
    using DandaUtils.Editor.UnityInternalsGenerator.Browser;
    using DandaUtils.Editor.UnityInternalsGenerator.Settings;
    using UnityEditor;
    using UnityEngine;

    [InitializeOnLoad]
    public static class GeneratorMenu
    {
        private const string c_MenuRoot = "Tools/DandaUtils/Unity Internals/";

        [Serializable]
        private sealed class GenerationInfoDto
        {
            public int generatorVersion = 0;
            public string unityVersion = null;
        }

        static GeneratorMenu() => EditorApplication.delayCall += OnLoaded;

        [MenuItem(c_MenuRoot + "Generate")]
        public static void Generate() => GenerationCommand.Execute();

        [MenuItem(c_MenuRoot + "Browser")]
        public static void OpenBrowser() => UnityInternalsBrowserWindow.Open();

        [MenuItem(c_MenuRoot + "Open Report")]
        public static void OpenReport()
        {
            var path = GenerationCommand.OutputReportPath;
            if (File.Exists(path) == false)
            {
                Debug.LogWarning("[UnityInternalsGenerator] No report yet: " + path);
                return;
            }
            EditorUtility.OpenWithDefaultApp(path);
        }

        [MenuItem(c_MenuRoot + "Settings")]
        public static void OpenSettings() => SettingsService.OpenProjectSettings(UnityInternalsGeneratorSettingsProvider.Path);

        private static void OnLoaded()
        {
            LogPendingSummary();
            CheckVersion();
        }

        private static void LogPendingSummary()
        {
            var summary = SessionState.GetString(GenerationCommand.c_PendingSummaryKey, string.Empty);
            if (string.IsNullOrEmpty(summary)) return;
            SessionState.EraseString(GenerationCommand.c_PendingSummaryKey);
            Debug.Log(summary);
        }

        private static void CheckVersion()
        {
            try
            {
                var path = GenerationCommand.ProjectRoot + "/" + UnityInternalsGeneratorSettings.instance.OutputFolder.Trim('/') + "/GenerationInfo.json";
                if (File.Exists(path) == false) return;
                var info = JsonUtility.FromJson<GenerationInfoDto>(File.ReadAllText(path));
                if (info == null || string.IsNullOrEmpty(info.unityVersion) || info.unityVersion == Application.unityVersion) return;
                Debug.LogWarning("[UnityInternalsGenerator] Wrapped internals were generated for Unity " + info.unityVersion + " but this is " + Application.unityVersion + ". Consider regenerating: " + c_MenuRoot + "Generate");
            }
            catch (Exception)
            {
                // unreadable info file: ignore
            }
        }
    }
}
