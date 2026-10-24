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

        /// <summary>One file per top-level wrapped type plus, when any class wrapper is emitted, the wrapper registry; sorted by RelativePath ordinal.</summary>
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

        private const string c_FactoryMapType = "global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Func<object, object>>";
        private const int c_RegistrationsPerMethod = 200;

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

            var registry = EmitRegistry(ctx);
            if (registry != null) files.Add(registry);

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
            Header(w, ctx);
            using (w.Block("namespace " + ns))
            {
                TypeEmission.Emit(t, w, ctx);
            }
            return w.ToFile(FilePath(t));
        }

        /// <summary>
        /// The single public Wrap entry point: one factory per emitted class wrapper, keyed by the exact Unity type, so walking
        /// the runtime type's base chain yields the most-derived wrapper available. Null when no class wrapper is emitted.
        /// </summary>
        private static GeneratedFile EmitRegistry(GenerationContext ctx)
        {
            var classes = ctx.ClassifiedTypes
                .Where(t => t.IsClass && ctx.IsEmitted(t) && ctx.IsInstanceWrapper(t))
                .OrderBy(MemberId.Type, StringComparer.Ordinal)
                .ToList();
            if (classes.Count == 0) return null;

            var chunks = (classes.Count + c_RegistrationsPerMethod - 1) / c_RegistrationsPerMethod;
            var w = new CodeWriter();
            Header(w, ctx);
            using (w.Block("namespace " + WrapperSyntax.RegistryNamespace))
            {
                w.Line("/// <summary>Maps a Unity internal instance to its most-derived generated wrapper.</summary>");
                using (w.Block("public static class " + WrapperSyntax.RegistryType))
                {
                    w.Line("private static readonly " + c_FactoryMapType + " s_Factories = Build();");
                    w.Line();
                    using (w.Block("private static " + c_FactoryMapType + " Build()"))
                    {
                        w.Line("var map = new " + c_FactoryMapType + "(" + Count(classes.Count) + ");");
                        for (var chunk = 0; chunk < chunks; chunk++) w.Line("Add" + Count(chunk) + "(map);");
                        w.Line("return map;");
                    }

                    for (var chunk = 0; chunk < chunks; chunk++)
                    {
                        w.Line();
                        using (w.Block("private static void Add" + Count(chunk) + "(" + c_FactoryMapType + " map)"))
                        {
                            foreach (var t in classes.Skip(chunk * c_RegistrationsPerMethod).Take(c_RegistrationsPerMethod))
                            {
                                var unity = CSharpNames.TypeRef(t);
                                using (w.Member(MemberId.Type(t)))
                                {
                                    w.Line("map[typeof(" + unity + ")] = value => new " + ctx.WrappedTypeRef(t) + "((" + unity + ")value);");
                                }
                            }
                        }
                    }

                    w.Line();
                    w.Line("/// <summary>The most-derived registered wrapper for the runtime type of <paramref name=\"value\"/>; null for null and for unregistered types.</summary>");
                    using (w.Block("public static object " + WrapperSyntax.WrapMethod + "(object value)"))
                    {
                        w.Line("if (value == null) return null;");
                        w.Line();
                        using (w.Block("for (var type = value.GetType(); type != null; type = type.BaseType)"))
                        {
                            w.Line("if (s_Factories.TryGetValue(type, out var factory)) return factory(value);");
                        }
                        w.Line("return null;");
                    }
                }
            }
            return w.ToFile(WrapperSyntax.RegistryPath);
        }

        private static void Header(CodeWriter w, GenerationContext ctx)
        {
            w.Line("// <auto-generated>");
            w.Line("// Generated by DandaUtils.UnityInternalsGenerator v" + GeneratorInfo.Version.ToString(CultureInfo.InvariantCulture) + " for Unity " + ctx.Input.UnityVersion + ". Do not edit.");
            w.Line("// </auto-generated>");
            w.Line("#pragma warning disable 0612, 0618");
        }

        private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
