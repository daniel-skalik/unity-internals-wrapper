namespace DandaUtils.Editor.UnityInternalsGenerator.Settings
{
    using DandaUtils.Editor.UnityInternalsGenerator;
    using UnityEditor;
    using UnityEngine;

    public static class UnityInternalsGeneratorSettingsProvider
    {
        public const string Path = "Project/DandaUtils/Unity Internals Generator";

        [SettingsProvider]
        public static SettingsProvider Create() => new SettingsProvider(Path, SettingsScope.Project)
        {
            label = "Unity Internals Generator",
            guiHandler = DrawGui,
            keywords = new[] { "unity", "internals", "wrapper", "generator" },
        };

        private static void DrawGui(string searchContext)
        {
            var settings = UnityInternalsGeneratorSettings.instance;
            using (var serialized = new SerializedObject(settings))
            {
                serialized.Update();
                EditorGUI.BeginChangeCheck();
                var property = serialized.GetIterator();
                for (var enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
                {
                    if (property.propertyPath == "m_Script" || property.propertyPath == "m_IncludedIds" || property.propertyPath == "m_ExcludedIds") continue;
                    EditorGUILayout.PropertyField(property, true);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    serialized.ApplyModifiedProperties();
                    settings.Save();
                }
            }

            EditorGUILayout.Space();
            var selection = settings.Selection;
            EditorGUILayout.LabelField("Selection", "Included: " + selection.Included.Count + ", Exceptions: " + selection.Excluded.Count);
            if (GUILayout.Button("Open Browser", GUILayout.Height(22))) GeneratorMenu.OpenBrowser();

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate", GUILayout.Height(28))) GenerationCommand.Execute();
        }
    }
}
