namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using System;
    using System.Linq;
    using System.Reflection;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;
    using DandaUtils.Editor.UnityInternalsGenerator.Selection;
    using NUnit.Framework;
    using UnityInternalsFixture;

    public sealed class CatalogTests
    {
        private const string c_Host = "Wrapped.Fixture.Host";
        private const BindingFlags c_Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Type s_Marker = typeof(FixtureMarker);

        private static GenerationContext NewContext()
        {
            var input = new GenerationInput(new[] { s_Marker.Assembly }, c_Host, NamingStyle.Exact, null, "test");
            return new GenerationContext(input);
        }

        private static Type Fixture(string name) => s_Marker.Assembly.GetType("UnityInternalsFixture." + name, true);

        private static string MethodId(string type, string name) => MemberId.Member(Fixture(type).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).First(m => m.Name == name));

        /// <summary>The Branch constructor and the Grid indexer that take a wrapped <c>Point</c>.</summary>
        private static string PointCtorId() => MemberId.Member(Fixture("Branch").GetConstructor(c_Declared, null, new[] { Fixture("Point") }, null));

        private static string PointIndexerId() => MemberId.Member(Fixture("Grid").GetProperties(c_Declared).Single(p => p.GetIndexParameters().Any(i => i.ParameterType == Fixture("Point"))));

        private static Catalog Build() => CatalogBuilder.Build(NewContext());

        [Test]
        public void Namespace_Node_IsRoot()
        {
            var node = Build().Get(SelectionIds.Namespace("UnityInternalsFixture"));
            Assert.IsNotNull(node);
            Assert.IsNull(node.ParentId);
            Assert.AreEqual(NodeKind.Namespace, node.Kind);
            Assert.AreEqual("UnityInternalsFixture", node.Name);
        }

        [Test]
        public void ChildrenOfNull_ContainsFixtureNamespace()
        {
            Assert.IsTrue(Build().ChildrenOf(null).Any(n => n.Id == SelectionIds.Namespace("UnityInternalsFixture")));
        }

        [Test]
        public void TopLevelType_Node()
        {
            var node = Build().Get(MemberId.Type(Fixture("StaticApi")));
            Assert.IsNotNull(node);
            Assert.AreEqual(SelectionIds.Namespace("UnityInternalsFixture"), node.ParentId);
            Assert.AreEqual("StaticApi", node.Name);
            Assert.AreEqual(NodeKind.StaticClass, node.Kind);
            Assert.AreEqual(WrapStatus.Wrapped, node.Status);
            Assert.IsTrue(node.IsTopLevelType);
        }

        [Test]
        public void NestedType_ParentIsDeclaringType()
        {
            var node = Build().Get(MemberId.Type(Fixture("StaticApi+Nested")));
            Assert.IsNotNull(node);
            Assert.AreEqual(MemberId.Type(Fixture("StaticApi")), node.ParentId);
            Assert.AreEqual(NodeKind.Enum, node.Kind);
            Assert.IsFalse(node.IsTopLevelType);
        }

        [Test]
        public void Methods_HaveKindAndStatus()
        {
            var catalog = Build();
            var add = catalog.Get(MethodId("StaticApi", "Add"));
            Assert.IsNotNull(add);
            Assert.AreEqual(NodeKind.Method, add.Kind);
            Assert.AreEqual(WrapStatus.Wrapped, add.Status);
            Assert.AreEqual("Add", add.Name);
            Assert.AreEqual(MemberId.Type(Fixture("StaticApi")), add.ParentId);
            Assert.AreEqual(WrapStatus.Deferred, catalog.Get(MethodId("StaticApi", "WithRef")).Status);
        }

        [Test]
        public void MemberKinds_PropertyFieldConstOther()
        {
            var catalog = Build();
            var children = catalog.ChildrenOf(MemberId.Type(Fixture("StaticApi")));
            Assert.AreEqual(NodeKind.Property, children.First(n => n.Name == "Current").Kind);
            Assert.AreEqual(NodeKind.Field, children.First(n => n.Name == "Counter").Kind);
            Assert.AreEqual(NodeKind.Const, children.First(n => n.Name == "Answer").Kind);
            Assert.AreEqual(NodeKind.Other, children.First(n => n.Name == "Changed").Kind);
        }

        [Test]
        public void TypeKinds_StructAndInterface()
        {
            var catalog = Build();
            Assert.AreEqual(NodeKind.Struct, catalog.Get(MemberId.Type(Fixture("Point"))).Kind);
            Assert.AreEqual(NodeKind.Interface, catalog.Get(MemberId.Type(Fixture("IThing"))).Kind);
            Assert.AreEqual(NodeKind.Class, catalog.Get(MemberId.Type(Fixture("Branch"))).Kind);
        }

        [Test]
        public void MemberKinds_ConstructorAndIndexer()
        {
            var children = Build().ChildrenOf(MemberId.Type(Fixture("Branch")));
            Assert.AreEqual(NodeKind.Indexer, children.First(n => n.Name == "Item").Kind);
            Assert.IsTrue(children.Where(n => n.Name == ".ctor").All(n => n.Kind == NodeKind.Constructor));
            Assert.AreEqual(3, children.Count(n => n.Name == ".ctor"));
        }

        [Test]
        public void Member_Dependencies_ConstructorAndIndexerSignatures()
        {
            var catalog = Build();
            var point = MemberId.Type(Fixture("Point"));

            CollectionAssert.Contains(catalog.Get(PointCtorId()).DependencyTypeIds, point);    // constructor parameter
            CollectionAssert.Contains(catalog.Get(PointIndexerId()).DependencyTypeIds, point); // index parameter
        }

        [Test]
        public void Member_TwoIndexersOnOneType_AreSeparateNodesWithTheirOwnDependencies()
        {
            var catalog = Build();
            var grid = Fixture("Grid");
            var byPoint = catalog.Get(PointIndexerId());
            var byInt = catalog.Get(MemberId.Member(grid.GetProperties(c_Declared).Single(p => p.GetIndexParameters()[0].ParameterType == typeof(int))));

            Assert.IsNotNull(byPoint, "indexer taking a Point");
            Assert.IsNotNull(byInt, "indexer taking an int");
            Assert.AreNotEqual(byPoint.Id, byInt.Id);
            CollectionAssert.AreEquivalent(new[] { MemberId.Type(Fixture("Point")) }, byPoint.DependencyTypeIds);
            Assert.AreEqual(0, byInt.DependencyTypeIds.Count);
            Assert.AreEqual(2, catalog.ChildrenOf(MemberId.Type(grid)).Count(n => n.Kind == NodeKind.Indexer));
        }

        [Test]
        public void Member_Dependencies_FieldAndMethodSignatures()
        {
            var catalog = Build();
            var corner = catalog.ChildrenOf(MemberId.Type(Fixture("Branch"))).First(n => n.Name == "Corner");
            CollectionAssert.Contains(corner.DependencyTypeIds, MemberId.Type(Fixture("Point"))); // field of a wrapped struct type

            var shift = catalog.Get(MethodId("StaticApi", "Shift"));
            CollectionAssert.AreEquivalent(new[] { MemberId.Type(Fixture("Point")) }, shift.DependencyTypeIds);
        }

        [Test]
        public void Member_Dependencies_ListWrappedTypes()
        {
            var next = Build().Get(MethodId("StaticApi", "Next"));
            CollectionAssert.AreEquivalent(new[] { MemberId.Type(Fixture("Color8")), MemberId.Type(Fixture("Mask")) }, next.DependencyTypeIds);
            Assert.AreEqual(0, Build().Get(MethodId("StaticApi", "Add")).DependencyTypeIds.Count);
        }

        [Test]
        public void NonWrappedTypes_HaveNoMembers()
        {
            var catalog = Build();
            var callback = catalog.Get(MemberId.Type(Fixture("Callback")));
            Assert.AreEqual(WrapStatus.Deferred, callback.Status);
            Assert.AreEqual(NodeKind.Other, callback.Kind);
            Assert.AreEqual(0, catalog.ChildrenOf(callback.Id).Count);
            Assert.AreEqual(0, catalog.ChildrenOf(MemberId.Type(Fixture("Color8"))).Count);
        }

        [Test]
        public void CompilerGeneratedTypes_Absent()
        {
            var catalog = Build();
            Assert.IsFalse(catalog.Nodes.Any(n => n.Id.Contains("<>")));
            Assert.IsTrue(catalog.Contains(MemberId.Type(Fixture("Lambdas"))));
        }

        [Test]
        public void Nodes_SortedById_AndParentsExist()
        {
            var catalog = Build();
            var ids = catalog.Nodes.Select(n => n.Id).ToList();
            CollectionAssert.AreEqual(ids.OrderBy(i => i, StringComparer.Ordinal).ToList(), ids);
            foreach (var node in catalog.Nodes.Where(n => n.ParentId != null)) Assert.IsTrue(catalog.Contains(node.ParentId), node.Id);
        }

        [Test]
        public void ChildrenOf_Ordering_ByNameIgnoreCase()
        {
            var names = Build().ChildrenOf(MemberId.Type(Fixture("StaticApi"))).Select(n => n.Name).ToList();
            CollectionAssert.AreEqual(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);
            var types = Build().ChildrenOf(SelectionIds.Namespace("UnityInternalsFixture")).Select(n => n.Name).ToList();
            CollectionAssert.AreEqual(types.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), types);
        }

        [Test]
        public void ChildrenOf_InterleavesNamespacesAndTypes_TiesByKind()
        {
            var catalog = new Catalog(new[]
            {
                new CatalogNode("ns:A", null, "A", NodeKind.Namespace, WrapStatus.Wrapped, null, null, false, false),
                new CatalogNode("ns:A.beta", "ns:A", "beta", NodeKind.Namespace, WrapStatus.Wrapped, null, null, false, false),
                new CatalogNode("ns:A.Same", "ns:A", "Same", NodeKind.Namespace, WrapStatus.Wrapped, null, null, false, false),
                new CatalogNode("T|A.Alpha", "ns:A", "Alpha", NodeKind.Class, WrapStatus.Wrapped, null, null, true, true),
                new CatalogNode("T|A.Gamma", "ns:A", "Gamma", NodeKind.Class, WrapStatus.Wrapped, null, null, true, true),
                new CatalogNode("T|A.same", "ns:A", "same", NodeKind.Class, WrapStatus.Wrapped, null, null, true, true),
            });

            var ids = catalog.ChildrenOf("ns:A").Select(n => n.Id).ToList();
            CollectionAssert.AreEqual(new[] { "T|A.Alpha", "ns:A.beta", "T|A.Gamma", "ns:A.Same", "T|A.same" }, ids);
        }

        [Test]
        public void PublicTypeWithInternalMember_DeferredNode_ListsOnlyInternalMembers()
        {
            var catalog = Build();
            var api = catalog.Get(MemberId.Type(typeof(PublicApi)));
            Assert.IsNotNull(api);
            Assert.AreEqual(WrapStatus.Deferred, api.Status);
            Assert.AreEqual(CatalogBuilder.PublicTypeInternalMembersReason, api.Reason);
            Assert.AreEqual(NodeKind.StaticClass, api.Kind);
            Assert.AreEqual(SelectionIds.Namespace("UnityInternalsFixture"), api.ParentId);
            Assert.IsTrue(api.IsType);
            Assert.IsTrue(api.IsTopLevelType);

            var children = catalog.ChildrenOf(api.Id);
            CollectionAssert.AreEqual(new[] { "Hidden" }, children.Select(n => n.Name).ToList());
            var hidden = children.Single();
            Assert.AreEqual(MemberId.Member(typeof(PublicApi).GetMethod("Hidden", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)), hidden.Id);
            Assert.AreEqual(NodeKind.Method, hidden.Kind);
            Assert.AreEqual(WrapStatus.Deferred, hidden.Status);
            Assert.AreEqual(CatalogBuilder.PublicTypeInternalMembersReason, hidden.Reason);
            Assert.AreEqual(0, hidden.DependencyTypeIds.Count);
        }

        [Test]
        public void InternalTypeNestedInPublicType_ParentIsPublicTypeNode()
        {
            var catalog = Build();
            var host = catalog.Get(MemberId.Type(typeof(PublicHost)));
            Assert.IsNotNull(host);
            Assert.AreEqual(WrapStatus.Deferred, host.Status);
            var inner = catalog.Get(MemberId.Type(typeof(PublicHost).GetNestedType("InnerMode", System.Reflection.BindingFlags.NonPublic)));
            Assert.AreEqual(host.Id, inner.ParentId);
            CollectionAssert.AreEqual(new[] { inner.Id }, catalog.ChildrenOf(host.Id).Select(n => n.Id).ToList());
        }

        [Test]
        public void PublicTypeWithoutInternals_Absent()
        {
            Assert.IsFalse(Build().Contains(MemberId.Type(typeof(FixtureMarker))));
        }

        [Test]
        public void PublicTypeNodes_AreNeverEmitted()
        {
            var catalog = Build();
            var result = SelectionResolver.Resolve(catalog, SelectionRules.All);
            CollectionAssert.DoesNotContain(result.EmittedTypeIds.ToList(), MemberId.Type(typeof(PublicApi)));
            CollectionAssert.DoesNotContain(result.EmittedTypeIds.ToList(), MemberId.Type(typeof(PublicHost)));
            Assert.IsFalse(result.EmittedMemberIds.Any(id => id.Contains("|UnityInternalsFixture.PublicApi|")));
        }

        [Test]
        public void ParentOf_Walks()
        {
            var catalog = Build();
            var add = MethodId("StaticApi", "Add");
            Assert.AreEqual(MemberId.Type(Fixture("StaticApi")), catalog.ParentOf(add));
            Assert.IsNull(catalog.ParentOf(SelectionIds.Namespace("UnityInternalsFixture")));
            Assert.IsNull(catalog.ParentOf("nope"));
            Assert.IsNull(catalog.Get("nope"));
        }

        [Test]
        public void Memoized_Plan_ReportsEachMemberOnce()
        {
            var ctx = NewContext();
            CatalogBuilder.Build(ctx);
            CatalogBuilder.Build(ctx);
            ctx.PlanFor(Fixture("StaticApi"));
            var ids = ctx.Entries.Select(e => e.Id).ToList();
            CollectionAssert.AreEqual(ids.Distinct().ToList(), ids);
            Assert.AreSame(ctx.PlanFor(Fixture("StaticApi")), ctx.PlanFor(Fixture("StaticApi")));
        }

        [Test]
        public void MemberVerdicts_IncludeNonWrapped_ButNotAccessors()
        {
            var verdicts = NewContext().MemberVerdicts(Fixture("StaticApi"));
            Assert.IsTrue(verdicts.Any(v => v.Member.Name == "WithRef" && v.Status == WrapStatus.Deferred));
            Assert.IsTrue(verdicts.Any(v => v.Member.Name == "Current"));
            Assert.IsFalse(verdicts.Any(v => v.Member.Name == "get_Current" || v.Member.Name == "add_Changed"));
        }
    }
}
