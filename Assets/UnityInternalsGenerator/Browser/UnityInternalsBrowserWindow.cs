namespace DandaUtils.Editor.UnityInternalsGenerator.Browser
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Pipeline;
    using DandaUtils.Editor.UnityInternalsGenerator.Selection;
    using DandaUtils.Editor.UnityInternalsGenerator.Settings;
    using UnityEditor;
    using UnityEditor.IMGUI.Controls;
    using UnityEngine;

    /// <summary>Searchable tri-state tree over the selection <see cref="Catalog"/>; every edit is stored in the settings immediately. The catalog is rebuilt on open and on Refresh, never persisted.</summary>
    public sealed class UnityInternalsBrowserWindow : EditorWindow
    {
        private const string c_Title = "Unity Internals";
        private const string c_ProgressTitle = "Unity Internals Browser";
        private const string c_EmptySelectionMessage = "Nothing selected — tick items to generate";

        private const double c_SearchDebounceSeconds = 0.15;

        // Statuses a catalog type/member node can carry; report-only statuses are not offered.
        private static readonly WrapStatus[] s_Statuses = { WrapStatus.Wrapped, WrapStatus.Deferred, WrapStatus.Unsupported, WrapStatus.Excluded, WrapStatus.NotAccessible, WrapStatus.NameCollision };
        private static readonly string[] s_StatusOptions = new[] { "All" }.Concat(s_Statuses.Select(s => s.ToString())).ToArray();

        [SerializeField] private TreeViewState m_TreeState;
        [SerializeField] private string m_Search = "";
        [SerializeField] private ShowFilter m_Show = ShowFilter.All;
        [SerializeField] private int m_StatusIndex;

        // Everything below is session-only: [NonSerialized] keeps the domain-reload backup (which also serializes private fields) from restoring stale values.
        [NonSerialized] private SearchField m_SearchField;
        [NonSerialized] private BrowserTreeView m_Tree;
        [NonSerialized] private Catalog m_Catalog;
        [NonSerialized] private BrowserModel m_Model;
        [NonSerialized] private SelectionRules m_Rules;
        [NonSerialized] private SelectionResult m_Selection;
        [NonSerialized] private IReadOnlyDictionary<string, CheckState> m_States;
        [NonSerialized] private string m_Summary = "";
        [NonSerialized] private string m_Error;
        [NonSerialized] private string m_PendingToggle;
        [NonSerialized] private bool m_BuildScheduled;
        [NonSerialized] private double m_SearchReloadAt = -1;

        public static void Open()
        {
            var window = GetWindow<UnityInternalsBrowserWindow>();
            window.titleContent = new GUIContent(c_Title);
            window.Show();
        }

        private WrapStatus? StatusFilter => m_StatusIndex <= 0 || m_StatusIndex > s_Statuses.Length ? (WrapStatus?)null : s_Statuses[m_StatusIndex - 1];

        private void OnEnable()
        {
            titleContent = new GUIContent(c_Title);
            m_TreeState ??= new TreeViewState();
            m_SearchField = new SearchField();
            m_Tree = new BrowserTreeView(m_TreeState, this);
            EditorApplication.update -= ReloadAfterSearchDebounce;
            EditorApplication.update += ReloadAfterSearchDebounce;
            m_Rules = UnityInternalsGeneratorSettings.instance.Selection;
            if (m_Catalog == null) return;

            // Re-enabled without a domain reload: the catalog is still valid, only the tree is new.
            m_Tree.SetCatalog(m_Catalog);
            Resolve();
        }

        private void OnDisable() => EditorApplication.update -= ReloadAfterSearchDebounce;

        private void ReloadAfterSearchDebounce()
        {
            if (m_SearchReloadAt < 0 || EditorApplication.timeSinceStartup < m_SearchReloadAt) return;

            m_SearchReloadAt = -1;
            if (m_Catalog == null) return;
            m_Tree.Reload();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (m_Error != null) EditorGUILayout.HelpBox(m_Error, MessageType.Error);
            if (m_Catalog == null)
            {
                if (m_Error == null) ScheduleBuild();
                EditorGUILayout.LabelField(m_Error == null ? "Building catalog…" : "Press Refresh to retry.");
                return;
            }

            if (m_Rules.IsEmpty) EditorGUILayout.HelpBox(c_EmptySelectionMessage, MessageType.Info);
            EditorGUILayout.LabelField(m_Summary, EditorStyles.miniLabel);

            var rect = GUILayoutUtility.GetRect(0f, 100000f, 0f, 100000f);
            m_Tree.OnGUI(rect);
            ApplyPendingToggle();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var search = m_SearchField.OnToolbarGUI(m_Search ?? "");
                if (string.Equals(search, m_Search, StringComparison.Ordinal) == false)
                {
                    // Rows are rebuilt once typing pauses, not per keystroke.
                    m_Search = search;
                    m_SearchReloadAt = EditorApplication.timeSinceStartup + c_SearchDebounceSeconds;
                }

                EditorGUI.BeginChangeCheck();
                var show = (ShowFilter)EditorGUILayout.EnumPopup(m_Show, EditorStyles.toolbarPopup, GUILayout.Width(90f));
                var statusIndex = EditorGUILayout.Popup(m_StatusIndex, s_StatusOptions, EditorStyles.toolbarPopup, GUILayout.Width(120f));
                if (EditorGUI.EndChangeCheck())
                {
                    m_Show = show;
                    m_StatusIndex = statusIndex;
                    if (m_Catalog != null) m_Tree.Reload();
                }

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f))) EditorApplication.delayCall += Refresh;
                if (GUILayout.Button("Generate", EditorStyles.toolbarButton, GUILayout.Width(65f))) Generate();
            }
        }

        private void ScheduleBuild()
        {
            if (m_BuildScheduled) return;
            m_BuildScheduled = true;
            EditorApplication.delayCall += BuildCatalog;
        }

        private void BuildCatalog()
        {
            m_BuildScheduled = false;
            if (this == null) return;

            try
            {
                EditorUtility.DisplayProgressBar(c_ProgressTitle, "Classifying internal types", 0.3f);
                var input = GenerationCommand.CreateInput(out var error, persistHost: false);
                if (input == null)
                {
                    m_Error = error;
                    return;
                }

                // Classification and the catalog are selection-agnostic; the rules are applied by the resolver below.
                var all = new GenerationInput(input.Sources, input.HostAssemblyName, input.Naming, SelectionRules.All, input.UnityVersion);
                var catalog = WrapperGenerator.BuildCatalog(all);
                EditorUtility.DisplayProgressBar(c_ProgressTitle, "Building browser", 0.9f);
                m_Model = new BrowserModel(catalog);
                m_Catalog = catalog;
                m_Tree.SetCatalog(catalog);
                m_Error = null;
                Resolve();
            }
            catch (Exception e)
            {
                m_Catalog = null;
                m_Error = "Catalog build failed: " + e.Message;
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        private void Refresh()
        {
            if (this == null) return;
            m_Rules = UnityInternalsGeneratorSettings.instance.Selection;
            m_Catalog = null;
            m_Model = null;
            m_Error = null;
            ScheduleBuild();
            Repaint();
        }

        /// <summary>Recomputes check states, the resolved selection and the summary once, then rebuilds the rows.</summary>
        private void Resolve()
        {
            m_States = SelectionEditing.States(m_Catalog, m_Rules);
            m_Selection = SelectionResolver.Resolve(m_Catalog, m_Rules);
            m_Summary = BuildSummary();
            m_Tree.Reload();
        }

        private string BuildSummary()
        {
            var types = 0;
            var members = 0;
            foreach (var id in m_Selection.SelectedIds)
            {
                var node = m_Catalog.Get(id);
                if (node == null || node.Kind == NodeKind.Namespace) continue;
                if (node.IsType) types++;
                else members++;
            }

            return "Selected: " + types + " types, " + members + " members · auto-included: " + m_Selection.AutoIncludedBy.Count + " · missing: " + m_Selection.MissingIds.Count + " · would generate: " + m_Selection.EmittedFileCount + " files";
        }

        private void RequestToggle(string id) => m_PendingToggle = id;

        private void ApplyPendingToggle()
        {
            if (m_PendingToggle == null) return;

            var id = m_PendingToggle;
            m_PendingToggle = null;

            // Rows never keep keyboard focus: hand it back to the tree so Space / arrows act on the tree, not on a stale toggle.
            GUIUtility.keyboardControl = 0;
            m_Tree.SetFocus();

            // Same rule as SelectionEditing.Toggle, but reuses the states already computed for this change.
            var rules = m_Catalog.Contains(id) ? SelectionEditing.Set(m_Rules, m_Catalog, id, m_States.TryGetValue(id, out var state) == false || state != CheckState.Whole) : SelectionEditing.RemoveRule(m_Rules, id);
            SaveSelection(rules);
            Resolve();
            Repaint();
        }

        /// <summary>Every edit goes to the settings asset at once, so a Generate from the menu or settings page always sees it.</summary>
        private void SaveSelection(SelectionRules rules)
        {
            var settings = UnityInternalsGeneratorSettings.instance;
            settings.SetSelection(rules);
            settings.Save();
            m_Rules = settings.Selection;
        }

        private static void Generate() => EditorApplication.delayCall += GenerationCommand.Execute;

        private string DisplayName(string id)
        {
            var node = m_Catalog.Get(id);
            if (node == null) return id;

            var parent = m_Catalog.Get(node.ParentId);
            return node.IsType == false && parent != null && parent.IsType ? parent.Name + "." + node.Name : node.Name;
        }

        private sealed class BrowserTreeView : TreeView
        {
            private const int c_RootId = 0;
            private const int c_MissingHeaderId = int.MaxValue;
            private const float c_ToggleWidth = 16f;
            private const float c_KindWidth = 80f;
            private const float c_StatusWidth = 110f;
            private const string c_AutoPrefix = "auto · needed by ";

            private readonly UnityInternalsBrowserWindow m_Window;
            private readonly Dictionary<string, int> m_IntIds = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly Dictionary<int, string> m_StringIds = new Dictionary<int, string>();
            private readonly Dictionary<int, string> m_MissingRows = new Dictionary<int, string>();
            private readonly HashSet<int> m_FixedExpansion = new HashSet<int>();
            private Catalog m_Catalog;

            public BrowserTreeView(TreeViewState state, UnityInternalsBrowserWindow window) : base(state)
            {
                m_Window = window;
                showAlternatingRowBackgrounds = true;
            }

            /// <summary>Catalog node index + 1 becomes the stable int id, so expansion state survives rebuilds of the same catalog.</summary>
            public void SetCatalog(Catalog catalog)
            {
                m_Catalog = catalog;
                m_IntIds.Clear();
                m_StringIds.Clear();
                for (var i = 0; i < catalog.Nodes.Count; i++)
                {
                    m_IntIds[catalog.Nodes[i].Id] = i + 1;
                    m_StringIds[i + 1] = catalog.Nodes[i].Id;
                }
            }

            protected override bool CanMultiSelect(TreeViewItem item) => false;

            protected override TreeViewItem BuildRoot() => new TreeViewItem(c_RootId, -1, "root") { children = new List<TreeViewItem>() };

            protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
            {
                var rows = new List<TreeViewItem>();
                root.children = new List<TreeViewItem>();
                m_MissingRows.Clear();
                m_FixedExpansion.Clear();
                if (m_Catalog == null || m_Window.m_Selection == null) return rows;

                AddMissingRows(root, rows);
                AddCatalogRows(root, rows);
                return rows;
            }

            private void AddMissingRows(TreeViewItem root, List<TreeViewItem> rows)
            {
                var missing = m_Window.m_Selection.MissingIds;
                if (missing.Count == 0) return;

                var header = new TreeViewItem(c_MissingHeaderId, 0, "Missing selections (" + missing.Count + ")");
                root.AddChild(header);
                rows.Add(header);
                for (var i = 0; i < missing.Count; i++)
                {
                    var id = c_MissingHeaderId - 1 - i;
                    m_MissingRows[id] = missing[i];
                    rows.Add(new TreeViewItem(id, 1, missing[i]) { parent = header });
                }
            }

            private void AddCatalogRows(TreeViewItem root, List<TreeViewItem> rows)
            {
                var w = m_Window;
                var expanded = new HashSet<string>(StringComparer.Ordinal);
                foreach (var intId in state.expandedIDs)
                {
                    if (m_StringIds.TryGetValue(intId, out var id)) expanded.Add(id);
                }

                // The model follows the real expanded state in both modes; during a search, ancestors on a match path are always open (no foldout).
                var modelRows = w.m_Model.Rows(w.m_Search, w.m_Show, w.StatusFilter, expanded, w.m_Selection, w.m_States);
                var byDepth = new List<TreeViewItem>();
                for (var i = 0; i < modelRows.Count; i++)
                {
                    var row = modelRows[i];
                    var item = new TreeViewItem(m_IntIds[row.Id], row.Depth, m_Catalog.Get(row.Id).Name);
                    var parent = row.Depth == 0 || row.Depth > byDepth.Count ? root : byDepth[row.Depth - 1];
                    parent.AddChild(item);

                    var childrenFollow = i + 1 < modelRows.Count && modelRows[i + 1].Depth > row.Depth;
                    if (row.HasChildren && childrenFollow == false) item.children = CreateChildListForCollapsedParent();
                    if (row.HasChildren && row.CanExpand == false) m_FixedExpansion.Add(item.id);

                    if (byDepth.Count > row.Depth) byDepth[row.Depth] = item;
                    else byDepth.Add(item);
                    rows.Add(item);
                }
            }

            protected override bool CanChangeExpandedState(TreeViewItem item) => m_FixedExpansion.Contains(item.id) == false && base.CanChangeExpandedState(item);

            protected override IList<int> GetAncestors(int id)
            {
                var result = new List<int>();
                if (m_Catalog == null || m_StringIds.TryGetValue(id, out var stringId) == false) return result;

                var parent = m_Catalog.ParentOf(stringId);
                while (parent != null && m_IntIds.TryGetValue(parent, out var parentId))
                {
                    result.Add(parentId);
                    parent = m_Catalog.ParentOf(parent);
                }
                return result;
            }

            protected override IList<int> GetDescendantsThatHaveChildren(int id)
            {
                var result = new List<int>();
                if (m_Catalog == null || m_StringIds.TryGetValue(id, out var stringId) == false) return result;

                var pending = new Stack<string>();
                pending.Push(stringId);
                while (pending.Count > 0)
                {
                    var current = pending.Pop();
                    var children = m_Catalog.ChildrenOf(current);
                    if (children.Count == 0) continue;

                    result.Add(m_IntIds[current]);
                    foreach (var child in children) pending.Push(child.Id);
                }
                return result;
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                var item = args.item;
                var rect = args.rowRect;
                var toggleRect = new Rect(rect.x + GetContentIndent(item), rect.y, c_ToggleWidth, rect.height);
                var statusRect = new Rect(rect.xMax - c_StatusWidth, rect.y, c_StatusWidth, rect.height);
                var kindRect = new Rect(statusRect.x - c_KindWidth, rect.y, c_KindWidth, rect.height);
                var labelRect = new Rect(toggleRect.xMax + 2f, rect.y, Mathf.Max(0f, kindRect.x - toggleRect.xMax - 4f), rect.height);

                if (item.id == c_MissingHeaderId)
                {
                    GUI.Label(new Rect(toggleRect.x, rect.y, rect.xMax - toggleRect.x, rect.height), item.displayName, EditorStyles.boldLabel);
                    return;
                }

                if (m_MissingRows.TryGetValue(item.id, out var missingId))
                {
                    DrawMissingRow(missingId, toggleRect, labelRect, kindRect, statusRect);
                    return;
                }

                if (m_StringIds.TryGetValue(item.id, out var id) == false) return;
                var node = m_Catalog.Get(id);
                var state = m_Window.m_States != null && m_Window.m_States.TryGetValue(id, out var s) ? s : CheckState.None;

                EditorGUI.showMixedValue = state == CheckState.Partial;
                EditorGUI.BeginChangeCheck();
                EditorGUI.Toggle(toggleRect, state == CheckState.Whole);
                var changed = EditorGUI.EndChangeCheck();
                EditorGUI.showMixedValue = false;
                if (changed) m_Window.RequestToggle(id);

                DrawName(node.Name, m_Window.m_Selection.AutoIncludedBy.TryGetValue(id, out var by) ? c_AutoPrefix + m_Window.DisplayName(by) : null, labelRect);
                if (node.Kind == NodeKind.Namespace) return;

                GUI.Label(kindRect, node.Kind.ToString(), EditorStyles.miniLabel);
                GUI.Label(statusRect, new GUIContent(node.Status.ToString(), node.Reason), EditorStyles.miniLabel);
            }

            private void DrawMissingRow(string missingId, Rect toggleRect, Rect labelRect, Rect kindRect, Rect statusRect)
            {
                // Every missing rule is drawn ticked; unticking removes the rule (included or exception alike).
                var isInclude = m_Window.m_Rules.RuleAt(missingId) == true;
                EditorGUI.BeginChangeCheck();
                EditorGUI.Toggle(toggleRect, true);
                if (EditorGUI.EndChangeCheck()) m_Window.RequestToggle(missingId);

                GUI.Label(labelRect, new GUIContent(missingId, missingId));
                GUI.Label(kindRect, "missing", EditorStyles.miniLabel);
                GUI.Label(statusRect, new GUIContent(isInclude ? "missing selection" : "missing exception", "Id not found in current Unity; untick to remove the rule"), EditorStyles.miniLabel);
            }

            private static void DrawName(string name, string suffix, Rect rect)
            {
                if (suffix == null)
                {
                    GUI.Label(rect, name);
                    return;
                }

                var nameWidth = Mathf.Min(rect.width, EditorStyles.label.CalcSize(new GUIContent(name)).x);
                GUI.Label(new Rect(rect.x, rect.y, nameWidth, rect.height), name);
                GUI.Label(new Rect(rect.x + nameWidth + 6f, rect.y, Mathf.Max(0f, rect.width - nameWidth - 6f), rect.height), suffix, EditorStyles.miniLabel);
            }
        }
    }
}
