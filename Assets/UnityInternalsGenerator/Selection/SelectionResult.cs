namespace DandaUtils.Editor.UnityInternalsGenerator.Selection
{
    using System;
    using System.Collections.Generic;

    /// <summary>What a <see cref="SelectionRules"/> resolves to against a <see cref="Catalog"/>. All collections are ordinal-sorted.</summary>
    public sealed class SelectionResult
    {
        private readonly HashSet<string> m_SelectedSet;

        public SelectionResult(IReadOnlyList<string> selectedIds, IReadOnlyList<string> emittedMemberIds, IReadOnlyList<string> emittedTypeIds, IReadOnlyDictionary<string, string> autoIncludedBy, IReadOnlyList<string> missingIds, int emittedFileCount)
        {
            SelectedIds = selectedIds;
            EmittedMemberIds = emittedMemberIds;
            EmittedTypeIds = emittedTypeIds;
            AutoIncludedBy = autoIncludedBy;
            MissingIds = missingIds;
            EmittedFileCount = emittedFileCount;
            m_SelectedSet = new HashSet<string>(selectedIds, StringComparer.Ordinal);
        }

        /// <summary>Every catalog node whose selection state is true.</summary>
        public IReadOnlyCollection<string> SelectedIds { get; }

        /// <summary>Wrapped selected members.</summary>
        public IReadOnlyCollection<string> EmittedMemberIds { get; }

        /// <summary>Types that get emitted: selected enums, dependencies, and declaring-type containers.</summary>
        public IReadOnlyCollection<string> EmittedTypeIds { get; }

        /// <summary>Type id → id that required it (member or type; ordinal-smallest requirer). Only for emitted types that are not themselves selected.</summary>
        public IReadOnlyDictionary<string, string> AutoIncludedBy { get; }

        /// <summary>Rule ids absent from the catalog, sorted.</summary>
        public IReadOnlyList<string> MissingIds { get; }

        /// <summary>Number of emitted top-level types (one file each).</summary>
        public int EmittedFileCount { get; }

        public bool IsSelected(string id) => id != null && m_SelectedSet.Contains(id);
    }
}
