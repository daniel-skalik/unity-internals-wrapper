namespace DandaUtils.Editor.UnityInternalsGenerator.Selection
{
    using System.Collections.Generic;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;

    public enum NodeKind
    {
        Namespace,
        Class,
        StaticClass,
        Enum,
        Method,
        Property,
        Field,
        Const,
        Other,
    }

    /// <summary>One selectable node: namespace, type or member. Namespace nodes carry <see cref="WrapStatus.Wrapped"/> and no reason (they are pure containers).</summary>
    public sealed class CatalogNode
    {
        public CatalogNode(string id, string parentId, string name, NodeKind kind, WrapStatus status, string reason, IReadOnlyList<string> dependencyTypeIds, bool isType, bool isTopLevelType)
        {
            Id = id;
            ParentId = parentId;
            Name = name;
            Kind = kind;
            Status = status;
            Reason = reason;
            DependencyTypeIds = dependencyTypeIds ?? System.Array.Empty<string>();
            IsType = isType;
            IsTopLevelType = isTopLevelType;
        }

        public string Id { get; }
        public string ParentId { get; }
        public string Name { get; }
        public NodeKind Kind { get; }
        public WrapStatus Status { get; }
        public string Reason { get; }

        /// <summary>Type ids of wrapped internal types this member's signature needs; empty for non-members.</summary>
        public IReadOnlyList<string> DependencyTypeIds { get; }

        /// <summary>True for type nodes (any type kind, including ones classified Other); false for namespaces and members.</summary>
        public bool IsType { get; }

        /// <summary>True for a type node that is not nested.</summary>
        public bool IsTopLevelType { get; }
    }
}
