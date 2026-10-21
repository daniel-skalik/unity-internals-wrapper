namespace DandaUtils.Editor.UnityInternalsGenerator.Settings
{
    using System.Collections.Generic;
    using System.Linq;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;
    using DandaUtils.Editor.UnityInternalsGenerator.Selection;
    using UnityEditor;
    using UnityEngine;

    [FilePath("ProjectSettings/UnityInternalsGeneratorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class UnityInternalsGeneratorSettings : ScriptableSingleton<UnityInternalsGeneratorSettings>
    {
        [SerializeField] private string m_OutputFolder = "Assets/WrappedUnityInternals";
        [SerializeField] private string m_HostAssemblyName = "";
        [SerializeField] private NamingStyle m_NamingStyle = NamingStyle.Exact;
        [SerializeField] private List<string> m_IncludedAssemblies = new List<string> { "UnityEditor.CoreModule", "UnityEngine.CoreModule" };
        [SerializeField] private List<string> m_IncludedIds = new List<string>();
        [SerializeField] private List<string> m_ExcludedIds = new List<string>();
        [SerializeField, HideInInspector] private List<string> m_ExcludedNamespaces = new List<string>();
        [SerializeField] private int m_MaxCompilePasses = 5;

        public string OutputFolder => m_OutputFolder;

        public string HostAssemblyName
        {
            get => m_HostAssemblyName;
            set => m_HostAssemblyName = value;
        }

        public NamingStyle NamingStyle => m_NamingStyle;
        public IReadOnlyList<string> IncludedAssemblies => m_IncludedAssemblies;
        public int MaxCompilePasses => Mathf.Max(1, m_MaxCompilePasses);

        private SelectionRules m_Selection;

        /// <summary>Allowlist built from the stored include/exception id lists; cached until <see cref="SetSelection"/> or a reload.</summary>
        public SelectionRules Selection => m_Selection ??= new SelectionRules(m_IncludedIds, m_ExcludedIds);

        /// <summary>Stores the rules as normalized id lists and marks the asset dirty. Call <see cref="Save()"/> to persist.</summary>
        public void SetSelection(SelectionRules rules)
        {
            m_IncludedIds = SettingsMigration.Normalize(rules.Included);
            m_ExcludedIds = SettingsMigration.Normalize(rules.Excluded);
            m_Selection = null;
            EditorUtility.SetDirty(this);
        }

        public void Save() => Save(true);

        private void OnEnable()
        {
            m_Selection = null;
            var changed = SettingsMigration.MigrateNamespaces(m_ExcludedNamespaces, m_ExcludedIds);
            var includedNormalized = SettingsMigration.Normalize(m_IncludedIds);
            if (includedNormalized.SequenceEqual(m_IncludedIds, System.StringComparer.Ordinal) == false)
            {
                m_IncludedIds = includedNormalized;
                changed = true;
            }
            if (changed == false) return;

            EditorUtility.SetDirty(this);
            Save(true);
        }
    }
}
