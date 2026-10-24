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

    public sealed class SelectionResolverTests
    {
        private const string c_Host = "Wrapped.Fixture.Host";
        private const BindingFlags c_AllStatic = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly Type s_Marker = typeof(FixtureMarker);

        private static Catalog s_Catalog;

        private static Catalog Catalog => s_Catalog ??= CatalogBuilder.Build(new GenerationContext(new GenerationInput(new[] { s_Marker.Assembly }, c_Host, NamingStyle.Exact, null, "test")));

        private static Type Fixture(string name) => s_Marker.Assembly.GetType("UnityInternalsFixture." + name, true);

        private static string TypeId(string name) => MemberId.Type(Fixture(name));

        private static string MethodId(string type, string name) => MemberId.Member(Fixture(type).GetMethods(c_AllStatic).First(m => m.Name == name));

        private static SelectionResult Resolve(string[] included, string[] excluded = null) => SelectionResolver.Resolve(Catalog, new SelectionRules(included, excluded ?? Array.Empty<string>()));

        [Test]
        public void Empty_EmitsNothing()
        {
            var result = SelectionResolver.Resolve(Catalog, SelectionRules.Empty);
            Assert.IsEmpty(result.SelectedIds);
            Assert.IsEmpty(result.EmittedMemberIds);
            Assert.IsEmpty(result.EmittedTypeIds);
            Assert.IsEmpty(result.AutoIncludedBy);
            Assert.IsEmpty(result.MissingIds);
            Assert.AreEqual(0, result.EmittedFileCount);
        }

        [Test]
        public void WholeClass_EmitsWrappedMembers_NestedAndDependencies()
        {
            var result = Resolve(new[] { TypeId("StaticApi") });
            CollectionAssert.Contains(result.EmittedMemberIds, MethodId("StaticApi", "Add"));
            CollectionAssert.Contains(result.EmittedMemberIds, MethodId("StaticApi", "Reset"));
            CollectionAssert.DoesNotContain(result.EmittedMemberIds, MethodId("StaticApi", "WithRef"));
            Assert.IsTrue(result.IsSelected(MethodId("StaticApi", "WithRef")));
            CollectionAssert.Contains(result.EmittedTypeIds, TypeId("StaticApi+Nested"));
            CollectionAssert.Contains(result.EmittedTypeIds, TypeId("Color8"));
            CollectionAssert.Contains(result.EmittedTypeIds, TypeId("Mask"));
            Assert.IsTrue(result.AutoIncludedBy.ContainsKey(TypeId("Color8")));
            Assert.IsTrue(result.AutoIncludedBy.ContainsKey(TypeId("Mask")));
            Assert.IsFalse(result.AutoIncludedBy.ContainsKey(TypeId("StaticApi+Nested")));
            Assert.IsFalse(result.AutoIncludedBy.ContainsKey(TypeId("StaticApi")));
            CollectionAssert.Contains(result.EmittedTypeIds, TypeId("Holder")); // MakeHolder returns it
            Assert.IsTrue(result.AutoIncludedBy.ContainsKey(TypeId("Holder")));
        }

        [Test]
        public void Exception_RemovesMember()
        {
            var result = Resolve(new[] { TypeId("StaticApi") }, new[] { MethodId("StaticApi", "Add") });
            CollectionAssert.DoesNotContain(result.EmittedMemberIds, MethodId("StaticApi", "Add"));
            CollectionAssert.Contains(result.EmittedMemberIds, MethodId("StaticApi", "Reset"));
        }

        [Test]
        public void SingleMember_EmitsContainerAndDependencies()
        {
            var next = MethodId("StaticApi", "Next");
            var result = Resolve(new[] { next });
            CollectionAssert.AreEqual(new[] { next }, result.EmittedMemberIds);
            CollectionAssert.IsSupersetOf(result.EmittedTypeIds, new[] { TypeId("StaticApi"), TypeId("Color8"), TypeId("Mask") });
            CollectionAssert.DoesNotContain(result.EmittedTypeIds, TypeId("Holder"));
            Assert.AreEqual(next, result.AutoIncludedBy[TypeId("StaticApi")]);
            Assert.AreEqual(next, result.AutoIncludedBy[TypeId("Color8")]);
        }

        [Test]
        public void DependencyBeatsException()
        {
            var next = MethodId("StaticApi", "Next");
            var result = Resolve(new[] { next }, new[] { TypeId("Color8") });
            CollectionAssert.Contains(result.EmittedTypeIds, TypeId("Color8"));
            Assert.AreEqual(next, result.AutoIncludedBy[TypeId("Color8")]);
        }

        [Test]
        public void ExcludedType_NotEmitted_WhenNothingNeedsIt()
        {
            var result = Resolve(new[] { TypeId("Holder") }, new[] { TypeId("Color8") });
            CollectionAssert.DoesNotContain(result.EmittedTypeIds, TypeId("Color8"));
            CollectionAssert.Contains(result.EmittedTypeIds, TypeId("Holder"));
        }

        [Test]
        public void NeededNestedEnum_InUnselectedClass_EmitsOuterAsContainerOnly()
        {
            var pick = MethodId("UsesNested", "Pick");
            var result = Resolve(new[] { TypeId("UsesNested") });
            CollectionAssert.IsSupersetOf(result.EmittedTypeIds, new[] { TypeId("StaticApi"), TypeId("StaticApi+Nested"), TypeId("UsesNested") });
            var staticApiMemberIds = Fixture("StaticApi").GetMembers(c_AllStatic).Select(MemberId.Member).ToHashSet();
            Assert.IsFalse(result.EmittedMemberIds.Any(staticApiMemberIds.Contains));
            CollectionAssert.Contains(result.EmittedMemberIds, pick);
            Assert.AreEqual(pick, result.AutoIncludedBy[TypeId("StaticApi+Nested")]);
            Assert.AreEqual(TypeId("StaticApi+Nested"), result.AutoIncludedBy[TypeId("StaticApi")]);
        }

        [Test]
        public void SelectedClass_WithNothingEmittable_ProducesNoFile()
        {
            var result = Resolve(new[] { TypeId("Generic`1") });
            CollectionAssert.DoesNotContain(result.EmittedTypeIds, TypeId("Generic`1"));
            Assert.AreEqual(0, result.EmittedFileCount);
        }

        [Test]
        public void NamespaceWhole_EqualsAll()
        {
            var ns = Resolve(new[] { SelectionIds.Namespace("UnityInternalsFixture") });
            var all = SelectionResolver.Resolve(Catalog, SelectionRules.All);
            CollectionAssert.AreEqual(all.EmittedMemberIds, ns.EmittedMemberIds);
            CollectionAssert.AreEqual(all.EmittedTypeIds, ns.EmittedTypeIds);
            Assert.AreEqual(all.EmittedFileCount, ns.EmittedFileCount);
            Assert.AreEqual(Catalog.Nodes.Count, all.SelectedIds.Count);
            Assert.IsEmpty(all.MissingIds);
            Assert.IsEmpty(all.AutoIncludedBy);
        }

        [Test]
        public void EmittedFileCount_CountsTopLevelEmittedTypes()
        {
            var result = Resolve(new[] { TypeId("UsesNested") });
            Assert.AreEqual(2, result.EmittedFileCount);
        }

        [Test]
        public void MissingIds_ListsUnknownRuleIds_Sorted()
        {
            var result = Resolve(new[] { "Gone|X|Type|X" }, new[] { "Absent|Y" });
            CollectionAssert.AreEqual(new[] { "Absent|Y", "Gone|X|Type|X" }, result.MissingIds);
            Assert.AreEqual(0, result.EmittedFileCount);
        }
    }
}
