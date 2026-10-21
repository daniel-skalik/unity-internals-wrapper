namespace DandaUtils.Editor.UnityInternalsGenerator.Selection
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>Immutable node tree: namespaces, types and planned members, indexed by id.</summary>
    public sealed class Catalog
    {
        private static readonly IReadOnlyList<CatalogNode> s_NoChildren = Array.Empty<CatalogNode>();

        private readonly Dictionary<string, CatalogNode> m_ById;
        private readonly Dictionary<string, List<CatalogNode>> m_Children = new Dictionary<string, List<CatalogNode>>(StringComparer.Ordinal);
        private readonly List<CatalogNode> m_Roots = new List<CatalogNode>();

        public Catalog(IEnumerable<CatalogNode> nodes)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));

            m_ById = new Dictionary<string, CatalogNode>(StringComparer.Ordinal);
            foreach (var node in nodes) m_ById.TryAdd(node.Id, node);

            Nodes = m_ById.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
            foreach (var node in Nodes)
            {
                if (node.ParentId == null || m_ById.ContainsKey(node.ParentId) == false)
                {
                    m_Roots.Add(node);
                    continue;
                }
                if (m_Children.TryGetValue(node.ParentId, out var list) == false) m_Children[node.ParentId] = list = new List<CatalogNode>();
                list.Add(node);
            }

            m_Roots.Sort(CompareSiblings);
            foreach (var list in m_Children.Values) list.Sort(CompareSiblings);
        }

        /// <summary>All nodes sorted by id ordinal.</summary>
        public IReadOnlyList<CatalogNode> Nodes { get; }

        /// <summary>The node, or null when absent.</summary>
        public CatalogNode Get(string id) => id != null && m_ById.TryGetValue(id, out var node) ? node : null;

        public bool Contains(string id) => id != null && m_ById.ContainsKey(id);

        /// <summary>Null id returns the roots. One list sorted by name (OrdinalIgnoreCase), ties by kind (namespace, type, member) then id ordinal, so namespaces and types interleave.</summary>
        public IReadOnlyList<CatalogNode> ChildrenOf(string id)
        {
            if (id == null) return m_Roots;
            return m_Children.TryGetValue(id, out var list) ? list : s_NoChildren;
        }

        public string ParentOf(string id) => Get(id)?.ParentId;

        private static int CompareSiblings(CatalogNode a, CatalogNode b)
        {
            var byName = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            if (byName != 0) return byName;
            var byGroup = Group(a).CompareTo(Group(b));
            return byGroup != 0 ? byGroup : string.CompareOrdinal(a.Id, b.Id);
        }

        private static int Group(CatalogNode n) => n.Kind == NodeKind.Namespace ? 0 : n.IsType ? 1 : 2;
    }
}
