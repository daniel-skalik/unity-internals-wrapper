namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using DandaUtils.Editor.UnityInternalsGenerator.Browser;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;
    using DandaUtils.Editor.UnityInternalsGenerator.Selection;
    using NUnit.Framework;
    using UnityInternalsFixture;

    public sealed class BrowserModelTests
    {
        private const string c_Host = "Wrapped.Fixture.Host";
        private const BindingFlags c_AllStatic = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly Type s_Marker = typeof(FixtureMarker);
        private static readonly ISet<string> s_NoneExpanded = new HashSet<string>();

        private static Catalog s_Catalog;

        private static Catalog Catalog => s_Catalog ??= CatalogBuilder.Build(new GenerationContext(new GenerationInput(new[] { s_Marker.Assembly }, c_Host, NamingStyle.Exact, null, "test")));

        private static string Ns => SelectionIds.Namespace("UnityInternalsFixture");

        private static Type Fixture(string name) => s_Marker.Assembly.GetType("UnityInternalsFixture." + name, true);

        private static string TypeId(string name) => MemberId.Type(Fixture(name));

        private static string MethodId(string type, string name) => MemberId.Member(Fixture(type).GetMethods(c_AllStatic).First(m => m.Name == name));

        private static List<string> Rows(Catalog catalog, SelectionRules rules, string search, ShowFilter show = ShowFilter.All, WrapStatus? status = null, ISet<string> expanded = null)
        {
            var selection = SelectionResolver.Resolve(catalog, rules);
            var states = SelectionEditing.States(catalog, rules);
            return new BrowserModel(catalog).Rows(search, show, status, expanded ?? s_NoneExpanded, selection, states).Select(r => r.Id).ToList();
        }

        [Test]
        public void Search_ShowsMatchAndAncestors_NotUnrelated()
        {
            var rows = Rows(Catalog, SelectionRules.Empty, "next");
            CollectionAssert.Contains(rows, Ns);
            CollectionAssert.Contains(rows, TypeId("StaticApi"));
            CollectionAssert.Contains(rows, MethodId("StaticApi", "Next"));
            CollectionAssert.DoesNotContain(rows, TypeId("Holder"));
            CollectionAssert.DoesNotContain(rows, MethodId("StaticApi", "Add"));
        }

        [Test]
        public void Search_IsCaseInsensitive()
        {
            CollectionAssert.Contains(Rows(Catalog, SelectionRules.Empty, "NEXT"), MethodId("StaticApi", "Next"));
        }

        [Test]
        public void Search_DoesNotMatchNamespaceNames()
        {
            Assert.IsEmpty(Rows(Catalog, SelectionRules.Empty, "unityinternalsfixture"));
        }

        [Test]
        public void EmptySearch_FollowsExpanded()
        {
            var collapsed = Rows(Catalog, SelectionRules.Empty, "");
            CollectionAssert.Contains(collapsed, Ns);
            CollectionAssert.DoesNotContain(collapsed, TypeId("StaticApi"));

            var nsOpen = Rows(Catalog, SelectionRules.Empty, "", expanded: new HashSet<string> { Ns });
            CollectionAssert.Contains(nsOpen, TypeId("StaticApi"));
            CollectionAssert.DoesNotContain(nsOpen, MethodId("StaticApi", "Add"));

            var typeOpen = Rows(Catalog, SelectionRules.Empty, "", expanded: new HashSet<string> { Ns, TypeId("StaticApi") });
            CollectionAssert.Contains(typeOpen, MethodId("StaticApi", "Add"));
        }

        [Test]
        public void Rows_AreInTreeOrder_WithDepthAndHasChildren()
        {
            var selection = SelectionResolver.Resolve(Catalog, SelectionRules.Empty);
            var states = SelectionEditing.States(Catalog, SelectionRules.Empty);
            var rows = new BrowserModel(Catalog).Rows("", ShowFilter.All, null, new HashSet<string> { Ns, TypeId("StaticApi") }, selection, states);

            var nsRow = rows.First(r => r.Id == Ns);
            var typeRow = rows.First(r => r.Id == TypeId("StaticApi"));
            var addRow = rows.First(r => r.Id == MethodId("StaticApi", "Add"));
            Assert.AreEqual(0, nsRow.Depth);
            Assert.AreEqual(1, typeRow.Depth);
            Assert.AreEqual(2, addRow.Depth);
            Assert.IsTrue(nsRow.HasChildren);
            Assert.IsTrue(typeRow.HasChildren);
            Assert.IsFalse(addRow.HasChildren);
            Assert.Less(rows.ToList().IndexOf(typeRow), rows.ToList().IndexOf(addRow));
        }

        [Test]
        public void ShowSelected_ShowsOnlySelectedType_AndNamespace()
        {
            var holder = TypeId("Holder");
            var rows = Rows(Catalog, new SelectionRules(new[] { holder }, Array.Empty<string>()), "", ShowFilter.Selected, expanded: new HashSet<string> { Ns, holder });
            var expected = new[] { Ns, holder }.Concat(Catalog.ChildrenOf(holder).Select(n => n.Id)).ToList();
            CollectionAssert.AreEquivalent(expected, rows);
        }

        [Test]
        public void ShowUnselected_WholeTypeWithNoUnselectedDescendants_IsHiddenWithChildren()
        {
            var holder = TypeId("Holder");
            var rows = Rows(Catalog, new SelectionRules(new[] { holder }, Array.Empty<string>()), "", ShowFilter.Unselected, expanded: new HashSet<string> { Ns, holder });
            CollectionAssert.DoesNotContain(rows, holder);
            Assert.IsFalse(Catalog.ChildrenOf(holder).Any(n => rows.Contains(n.Id)));
        }

        [Test]
        public void ShowSelected_IncludesAutoIncludedDependencies()
        {
            var rows = Rows(Catalog, new SelectionRules(new[] { MethodId("StaticApi", "Next") }, Array.Empty<string>()), "", ShowFilter.Selected, expanded: new HashSet<string> { Ns });
            CollectionAssert.Contains(rows, TypeId("Color8"));
            CollectionAssert.Contains(rows, TypeId("StaticApi"));
        }

        [Test]
        public void ShowUnselected_HidesSelected()
        {
            var rows = Rows(Catalog, new SelectionRules(new[] { TypeId("Holder") }, Array.Empty<string>()), "", ShowFilter.Unselected, expanded: new HashSet<string> { Ns });
            CollectionAssert.DoesNotContain(rows, TypeId("Holder"));
            CollectionAssert.Contains(rows, TypeId("StaticApi"));
        }

        [Test]
        public void StatusFilter_Deferred_ShowsWithRef_HidesAdd()
        {
            var rows = Rows(Catalog, SelectionRules.Empty, "", status: WrapStatus.Deferred, expanded: new HashSet<string> { Ns, TypeId("StaticApi") });
            CollectionAssert.Contains(rows, MethodId("StaticApi", "WithRef"));
            CollectionAssert.DoesNotContain(rows, MethodId("StaticApi", "Add"));
            CollectionAssert.Contains(rows, TypeId("StaticApi"));
        }

        [Test]
        public void StatusFilter_CombinesWithSearch()
        {
            var rows = Rows(Catalog, SelectionRules.Empty, "ref", status: WrapStatus.Deferred);
            CollectionAssert.Contains(rows, MethodId("StaticApi", "WithRef"));
            Assert.IsFalse(rows.Contains(MethodId("StaticApi", "Add")));
        }

        [Test]
        public void DottedSearch_NamespaceAndType_MatchesTypeNotMembers()
        {
            var rows = Rows(Catalog, SelectionRules.Empty, "UnityInternalsFixture.StaticApi");
            CollectionAssert.Contains(rows, TypeId("StaticApi"));
            CollectionAssert.Contains(rows, Ns);
            Assert.IsFalse(Catalog.ChildrenOf(TypeId("StaticApi")).Any(n => rows.Contains(n.Id)));
        }

        [TestCase("StaticApi.Add")]
        [TestCase("UnityInternalsFixture.StaticApi.Add")]
        [TestCase("staticapi.ADD")]
        public void DottedSearch_TypeAndMember_MatchesMember(string search)
        {
            var rows = Rows(Catalog, SelectionRules.Empty, search);
            CollectionAssert.Contains(rows, MethodId("StaticApi", "Add"));
            CollectionAssert.DoesNotContain(rows, MethodId("StaticApi", "Next"));
        }

        [Test]
        public void DottedSearch_NestedType()
        {
            CollectionAssert.Contains(Rows(Catalog, SelectionRules.Empty, "StaticApi.Nested"), TypeId("StaticApi+Nested"));
        }

        [Test]
        public void DottedSearch_PartialLastSegment()
        {
            var rows = Rows(Catalog, SelectionRules.Empty, "UnityInternalsFixture.Stat");
            CollectionAssert.Contains(rows, TypeId("StaticApi"));
            CollectionAssert.DoesNotContain(rows, MethodId("StaticApi", "Add"));
        }

        [Test]
        public void DottedSearch_PrefixMustEndOnSegmentBoundary()
        {
            Assert.IsEmpty(Rows(Catalog, SelectionRules.Empty, "Fixture.StaticApi"));
        }

        [Test]
        public void Search_MatchedContainer_Collapsed_HasFoldout_NoChildren()
        {
            var rows = RowObjects(Catalog, "StaticApi", null, s_NoneExpanded);
            var api = rows.Single(r => r.Id == TypeId("StaticApi"));
            Assert.IsTrue(api.HasChildren);
            Assert.IsTrue(api.CanExpand);
            Assert.IsFalse(rows.Any(r => r.Id == MethodId("StaticApi", "Add")));
        }

        [Test]
        public void Search_MatchedContainer_Expanded_ShowsAllChildren_FilteredByStatus()
        {
            var expanded = new HashSet<string> { TypeId("StaticApi") };
            var rows = RowObjects(Catalog, "StaticApi", null, expanded);
            var add = rows.Single(r => r.Id == MethodId("StaticApi", "Add"));
            Assert.AreEqual(2, add.Depth);
            Assert.IsTrue(rows.Any(r => r.Id == MethodId("StaticApi", "WithRef")));
            Assert.IsTrue(rows.Any(r => r.Id == TypeId("StaticApi+Nested")));

            var wrappedOnly = RowObjects(Catalog, "StaticApi", WrapStatus.Wrapped, expanded).Select(r => r.Id).ToList();
            CollectionAssert.Contains(wrappedOnly, MethodId("StaticApi", "Add"));
            CollectionAssert.DoesNotContain(wrappedOnly, MethodId("StaticApi", "WithRef"));
        }

        [Test]
        public void Search_AncestorOnly_CannotExpand_AndExpandingItShowsNothingMore()
        {
            var rows = RowObjects(Catalog, "next", null, new HashSet<string> { Ns, TypeId("StaticApi") });
            var ns = rows.Single(r => r.Id == Ns);
            var api = rows.Single(r => r.Id == TypeId("StaticApi"));
            Assert.IsTrue(ns.HasChildren);
            Assert.IsFalse(ns.CanExpand);
            Assert.IsFalse(api.CanExpand);
            Assert.IsFalse(rows.Any(r => r.Id == TypeId("Holder")));
            Assert.IsFalse(rows.Any(r => r.Id == MethodId("StaticApi", "Add")));
        }

        [Test]
        public void Search_ExpandedRevealedChildren_FollowExpandedRecursively()
        {
            var catalog = new Catalog(new[]
            {
                new CatalogNode("ns:A", null, "A", NodeKind.Namespace, WrapStatus.Wrapped, null, null, false, false),
                new CatalogNode("T1", "ns:A", "Outer", NodeKind.Class, WrapStatus.Wrapped, null, null, true, true),
                new CatalogNode("T2", "T1", "Inner", NodeKind.Class, WrapStatus.Wrapped, null, null, true, false),
                new CatalogNode("M", "T2", "Run", NodeKind.Method, WrapStatus.Wrapped, null, null, false, false),
            });

            var openOuter = RowObjects(catalog, "outer", null, new HashSet<string> { "T1" }).Select(r => r.Id).ToList();
            CollectionAssert.AreEqual(new[] { "ns:A", "T1", "T2" }, openOuter);
            var inner = RowObjects(catalog, "outer", null, new HashSet<string> { "T1" }).Single(r => r.Id == "T2");
            Assert.IsTrue(inner.HasChildren);
            Assert.IsTrue(inner.CanExpand);

            var openBoth = RowObjects(catalog, "outer", null, new HashSet<string> { "T1", "T2" }).Select(r => r.Id).ToList();
            CollectionAssert.AreEqual(new[] { "ns:A", "T1", "T2", "M" }, openBoth);
        }

        [Test]
        public void NoSearch_RowsReportCanExpandForContainers()
        {
            var rows = RowObjects(Catalog, "", null, s_NoneExpanded);
            Assert.IsTrue(rows.Single(r => r.Id == Ns).CanExpand);
        }

        private static IReadOnlyList<BrowserRow> RowObjects(Catalog catalog, string search, WrapStatus? status, ISet<string> expanded)
        {
            var selection = SelectionResolver.Resolve(catalog, SelectionRules.Empty);
            var states = SelectionEditing.States(catalog, SelectionRules.Empty);
            return new BrowserModel(catalog).Rows(search, ShowFilter.All, status, expanded, selection, states);
        }

        [Test, Category("Slow")]
        public void RealCatalog_BuildAndSearch_MeetBudget()
        {
            var assemblies = new[] { typeof(UnityEditor.Editor).Assembly, typeof(UnityEngine.Object).Assembly };
            var ctx = new GenerationContext(new GenerationInput(assemblies, "Unity.InternalAPIEngineBridge.002", NamingStyle.Exact, null, "perf"));

            var buildWatch = Stopwatch.StartNew();
            var catalog = CatalogBuilder.Build(ctx);
            buildWatch.Stop();

            var rules = SelectionRules.All;
            var selection = SelectionResolver.Resolve(catalog, rules);
            var states = SelectionEditing.States(catalog, rules);
            var model = new BrowserModel(catalog);

            var searchWatch = Stopwatch.StartNew();
            var rows = model.Rows("gameview", ShowFilter.All, null, s_NoneExpanded, selection, states);
            searchWatch.Stop();

            var dottedWatch = Stopwatch.StartNew();
            var dotted = model.Rows("Menu.AddMenuItem", ShowFilter.All, null, s_NoneExpanded, selection, states);
            dottedWatch.Stop();

            TestContext.WriteLine($"[Perf] nodes={catalog.Nodes.Count} build={buildWatch.ElapsedMilliseconds}ms rows(gameview)={rows.Count} in {searchWatch.ElapsedMilliseconds}ms rows(Menu.AddMenuItem)={dotted.Count} in {dottedWatch.ElapsedMilliseconds}ms");

            // Public type with internal members (step 4): UnityEditor.Menu wraps its internal AddMenuItem.
            var menu = catalog.Get(MemberId.Type(typeof(UnityEditor.Menu)));
            Assert.IsNotNull(menu, "UnityEditor.Menu node");
            Assert.AreEqual(WrapStatus.Wrapped, menu.Status);
            var addMenuItem = catalog.ChildrenOf(menu.Id).FirstOrDefault(n => n.Name == "AddMenuItem");
            Assert.IsNotNull(addMenuItem, "AddMenuItem under UnityEditor.Menu");
            Assert.AreEqual(WrapStatus.Wrapped, addMenuItem.Status);
            Assert.IsTrue(dotted.Any(r => r.Id == addMenuItem.Id), "dotted search finds AddMenuItem");
            Assert.Less(dottedWatch.ElapsedMilliseconds, 200, "dotted search rows");
            Assert.Greater(catalog.Nodes.Count, 1000);
            Assert.Greater(rows.Count, 0);
            Assert.Less(buildWatch.Elapsed.TotalSeconds, 15.0, "catalog build");
            Assert.Less(searchWatch.ElapsedMilliseconds, 200, "search rows");
        }
    }
}
