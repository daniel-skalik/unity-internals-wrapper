namespace DandaUtils.Editor.UnityInternalsGenerator.Pipeline
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using DandaUtils.Editor.UnityInternalsGenerator.Emit;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;
    using DandaUtils.Editor.UnityInternalsGenerator.Rules;
    using DandaUtils.Editor.UnityInternalsGenerator.Selection;

    public sealed class GenerationResult
    {
        public GenerationResult(IReadOnlyList<GeneratedFile> files, IReadOnlyList<ReportEntry> entries, SelectionResult selection, Catalog catalog)
        {
            Files = files;
            Entries = entries;
            Selection = selection;
            Catalog = catalog;
        }

        /// <summary>One file per top-level wrapped type, sorted by RelativePath ordinal.</summary>
        public IReadOnlyList<GeneratedFile> Files { get; }

        /// <summary>Every classified type and considered member (with its <see cref="ReportEntry.Selected"/> flag) plus one entry per rule id absent from the catalog (SelectedMissing, or Excluded when its declaring type exists but is not Wrapped), sorted by id then rule.</summary>
        public IReadOnlyList<ReportEntry> Entries { get; }

        /// <summary>The input selection resolved against <see cref="Catalog"/>.</summary>
        public SelectionResult Selection { get; }

        /// <summary>Every selectable node of this run (after path-collision rejection).</summary>
        public Catalog Catalog { get; }
    }

    /// <summary>Pure generation step: classifies the input assemblies, resolves the selection and emits wrapper sources in memory. No I/O.</summary>
    public static class WrapperGenerator
    {
        private const string c_SelectionRule = "Selection";
        private const string c_MissingReason = "selected id not found in current Unity";
        private const string c_MissingExceptionReason = "exception id not found in current Unity";
        private const string c_AutoIncludedPrefix = "auto-included: needed by ";
        private const string c_DeclaringAutoExcludedReason = "Declaring type auto-excluded: failed to compile";
        private const string c_DeclaringStatusPrefix = "Declaring type is ";

        public static GenerationResult Generate(GenerationInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var ctx = new GenerationContext(input);
            var catalog = PrepareCatalog(ctx, out var topLevel);
            var selection = SelectionResolver.Resolve(catalog, input.Selection);
            ctx.ApplySelection(selection);
            var entries = SelectionEntries(ctx, catalog, selection, input.Selection);

            var files = new List<GeneratedFile>();
            foreach (var t in topLevel)
            {
                if (ctx.IsEmitted(t)) files.Add(EmitFile(t, ctx));
            }
            files.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));
            return new GenerationResult(files, entries, selection, catalog);
        }

        /// <summary>Classifies the input and builds its selection catalog exactly as <see cref="Generate"/> sees it, without resolving or emitting (the input selection is ignored).</summary>
        public static Catalog BuildCatalog(GenerationInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            return PrepareCatalog(new GenerationContext(input), out _);
        }

        private static Catalog PrepareCatalog(GenerationContext ctx, out List<Type> topLevel)
        {
            topLevel = ctx.ClassifyAll().Where(t => t.DeclaringType == null).ToList();

            // Before the catalog: plans are memoized lazily and must see the final classifications.
            foreach (var group in CollidingGroups(topLevel, FilePath))
            {
                foreach (var t in group) ctx.RejectPathCollision(t, FilePath(t));
            }

            return CatalogBuilder.Build(ctx);
        }

        /// <summary>Groups of more than one item sharing a path (OrdinalIgnoreCase, as on case-insensitive file systems), in first-seen order.</summary>
        public static IReadOnlyList<IReadOnlyList<T>> CollidingGroups<T>(IEnumerable<T> items, Func<T, string> path)
        {
            return items.GroupBy(path, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => (IReadOnlyList<T>)g.ToList()).ToList();
        }

        private static IReadOnlyList<ReportEntry> SelectionEntries(GenerationContext ctx, Catalog catalog, SelectionResult selection, SelectionRules rules)
        {
            var result = ctx.Entries.Select(e => WithSelection(e, selection)).ToList();
            result.AddRange(selection.MissingIds.Select(id => MissingEntry(id, rules, catalog, ctx)));
            result.Sort((a, b) =>
            {
                var byId = string.CompareOrdinal(a.Id, b.Id);
                return byId != 0 ? byId : string.CompareOrdinal(a.Rule, b.Rule);
            });
            return result;
        }

        /// <summary>A rule id absent from the catalog: Excluded when its declaring type exists but is not Wrapped (e.g. auto-excluded, so its members vanished), otherwise SelectedMissing.</summary>
        private static ReportEntry MissingEntry(string id, SelectionRules rules, Catalog catalog, GenerationContext ctx)
        {
            var declaring = catalog.Get(MemberId.DeclaringTypeId(id));
            if (declaring == null || declaring.Status == WrapStatus.Wrapped) return new ReportEntry(id, c_SelectionRule, WrapStatus.SelectedMissing, MissingReason(id, rules), true);

            var reason = IsAutoExcludedChain(declaring, catalog, ctx) ? c_DeclaringAutoExcludedReason : c_DeclaringStatusPrefix + declaring.Status;
            return new ReportEntry(id, c_SelectionRule, WrapStatus.Excluded, reason, true);
        }

        private static bool IsAutoExcludedChain(CatalogNode type, Catalog catalog, GenerationContext ctx)
        {
            for (var current = type; current != null && current.IsType; current = catalog.Get(current.ParentId))
            {
                if (ctx.IsAutoExcludedId(current.Id)) return true;
            }
            return false;
        }

        private static string MissingReason(string id, SelectionRules rules) => rules.Included.Contains(id) ? c_MissingReason : c_MissingExceptionReason;

        private static ReportEntry WithSelection(ReportEntry e, SelectionResult selection)
        {
            if (selection.AutoIncludedBy.TryGetValue(e.Id, out var by)) return new ReportEntry(e.Id, e.Rule, e.Status, e.Reason ?? c_AutoIncludedPrefix + by, true);
            return new ReportEntry(e.Id, e.Rule, e.Status, e.Reason, selection.IsSelected(e.Id));
        }

        private static string FilePath(Type t) => NamespaceMapper.Map(t.Namespace).Replace('.', '/') + "/" + CSharpNames.TypeDeclName(t) + ".g.cs";

        private static GeneratedFile EmitFile(Type t, GenerationContext ctx)
        {
            var ns = NamespaceMapper.Map(t.Namespace);
            var w = new CodeWriter();
            w.Line("// <auto-generated>");
            w.Line("// Generated by DandaUtils.UnityInternalsGenerator v" + GeneratorInfo.Version.ToString(CultureInfo.InvariantCulture) + " for Unity " + ctx.Input.UnityVersion + ". Do not edit.");
            w.Line("// </auto-generated>");
            w.Line("#pragma warning disable 0612, 0618");
            using (w.Block("namespace " + ns))
            {
                TypeEmission.Emit(t, w, ctx);
            }
            return w.ToFile(FilePath(t));
        }
    }
}
