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

    public sealed class SelectionEditingTests
    {
        private const string c_Host = "Wrapped.Fixture.Host";
        private const BindingFlags c_AllStatic = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly Type s_Marker = typeof(FixtureMarker);

        private static Catalog s_Catalog;

        private static Catalog Catalog => s_Catalog ??= CatalogBuilder.Build(new GenerationContext(new GenerationInput(new[] { s_Marker.Assembly }, c_Host, NamingStyle.Exact, null, "test")));

        private static string Ns => SelectionIds.Namespace("UnityInternalsFixture");

        private static Type Fixture(string name) => s_Marker.Assembly.GetType("UnityInternalsFixture." + name, true);

        private static string TypeId(string name) => MemberId.Type(Fixture(name));

        private static string MethodId(string type, string name) => MemberId.Member(Fixture(type).GetMethods(c_AllStatic).First(m => m.Name == name));

        private static SelectionRules Rules(string[] included, string[] excluded = null) => new SelectionRules(included, excluded ?? Array.Empty<string>());

        [Test]
        public void Toggle_OnType_FromEmpty_Includes_ThenBackToEmpty()
        {
            var on = SelectionEditing.Toggle(SelectionRules.Empty, Catalog, TypeId("StaticApi"));
            CollectionAssert.AreEqual(new[] { TypeId("StaticApi") }, on.Included);
            Assert.IsEmpty(on.Excluded);

            var off = SelectionEditing.Toggle(on, Catalog, TypeId("StaticApi"));
            Assert.IsEmpty(off.Included);
            Assert.IsEmpty(off.Excluded);
        }

        [Test]
        public void Set_MemberFalse_UnderWholeType_AddsException_AndPartial()
        {
            var rules = SelectionEditing.Set(Rules(new[] { TypeId("StaticApi") }), Catalog, MethodId("StaticApi", "Add"), false);
            CollectionAssert.AreEqual(new[] { TypeId("StaticApi") }, rules.Included);
            CollectionAssert.AreEqual(new[] { MethodId("StaticApi", "Add") }, rules.Excluded);
            Assert.AreEqual(CheckState.Partial, SelectionEditing.StateOf(Catalog, rules, TypeId("StaticApi")));
        }

        [Test]
        public void Set_MemberTrue_RemovesRedundantException()
        {
            var partial = Rules(new[] { TypeId("StaticApi") }, new[] { MethodId("StaticApi", "Add") });
            var rules = SelectionEditing.Set(partial, Catalog, MethodId("StaticApi", "Add"), true);
            CollectionAssert.AreEqual(new[] { TypeId("StaticApi") }, rules.Included);
            Assert.IsEmpty(rules.Excluded);
            Assert.AreEqual(CheckState.Whole, SelectionEditing.StateOf(Catalog, rules, TypeId("StaticApi")));
        }

        [Test]
        public void Set_TypeTrue_ClearsDescendantRules()
        {
            var rules = SelectionEditing.Set(Rules(new[] { MethodId("StaticApi", "Add"), MethodId("StaticApi", "Reset") }), Catalog, TypeId("StaticApi"), true);
            CollectionAssert.AreEqual(new[] { TypeId("StaticApi") }, rules.Included);
            Assert.IsEmpty(rules.Excluded);
        }

        [Test]
        public void Set_TypeFalse_UnderWholeNamespace_AddsException()
        {
            var rules = SelectionEditing.Set(Rules(new[] { Ns }), Catalog, TypeId("StaticApi"), false);
            CollectionAssert.AreEqual(new[] { Ns }, rules.Included);
            CollectionAssert.AreEqual(new[] { TypeId("StaticApi") }, rules.Excluded);
        }

        [Test]
        public void Set_TypeFalse_ClearsDeeperRules_AndLeavesNoRedundantRule()
        {
            var rules = SelectionEditing.Set(Rules(new[] { MethodId("StaticApi", "Add") }, new[] { MethodId("StaticApi", "Reset") }), Catalog, TypeId("StaticApi"), false);
            Assert.IsEmpty(rules.Included);
            Assert.IsEmpty(rules.Excluded);
        }

        [Test]
        public void Toggle_Partial_SelectsWhole_ClearingExceptions()
        {
            var partial = Rules(new[] { TypeId("StaticApi") }, new[] { MethodId("StaticApi", "Add") });
            var rules = SelectionEditing.Toggle(partial, Catalog, TypeId("StaticApi"));
            CollectionAssert.AreEqual(new[] { TypeId("StaticApi") }, rules.Included);
            Assert.IsEmpty(rules.Excluded);
        }

        [Test]
        public void Set_OnAll_Throws()
        {
            Assert.Throws<ArgumentException>(() => SelectionEditing.Set(SelectionRules.All, Catalog, TypeId("StaticApi"), false));
        }

        [Test]
        public void Toggle_OnAll_Throws()
        {
            Assert.Throws<ArgumentException>(() => SelectionEditing.Toggle(SelectionRules.All, Catalog, TypeId("StaticApi")));
        }

        [Test]
        public void RemoveRule_DropsMissingIdFromBothLists_KeepsOthers()
        {
            var rules = SelectionEditing.RemoveRule(Rules(new[] { Ns, "gone" }, new[] { "gone", TypeId("StaticApi") }), "gone");
            CollectionAssert.AreEqual(new[] { Ns }, rules.Included);
            CollectionAssert.AreEqual(new[] { TypeId("StaticApi") }, rules.Excluded);
        }

        [Test]
        public void RemoveRule_OnAll_Throws()
        {
            Assert.Throws<ArgumentException>(() => SelectionEditing.RemoveRule(SelectionRules.All, "x"));
        }

        [Test]
        public void Set_UnknownId_ReturnsRulesUnchanged()
        {
            var rules = Rules(new[] { Ns });
            Assert.AreSame(rules, SelectionEditing.Set(rules, Catalog, "nope", true));
        }

        [Test]
        public void States_Leaf_ContainerWholeNoneAndPartial()
        {
            var none = SelectionEditing.States(Catalog, SelectionRules.Empty);
            Assert.AreEqual(CheckState.None, none[Ns]);
            Assert.AreEqual(CheckState.None, none[MethodId("StaticApi", "Add")]);

            var whole = SelectionEditing.States(Catalog, Rules(new[] { Ns }));
            Assert.AreEqual(CheckState.Whole, whole[Ns]);
            Assert.AreEqual(CheckState.Whole, whole[MethodId("StaticApi", "Add")]);

            var one = SelectionEditing.States(Catalog, Rules(new[] { MethodId("StaticApi", "Add") }));
            Assert.AreEqual(CheckState.Whole, one[MethodId("StaticApi", "Add")]);
            Assert.AreEqual(CheckState.None, one[MethodId("StaticApi", "Reset")]);
            Assert.AreEqual(CheckState.Partial, one[TypeId("StaticApi")]);
            Assert.AreEqual(CheckState.Partial, one[Ns]);
        }

        [Test]
        public void States_ChildlessType_IsLeaf()
        {
            var states = SelectionEditing.States(Catalog, Rules(new[] { TypeId("Point") }));
            Assert.AreEqual(CheckState.Whole, states[TypeId("Point")]);
            Assert.AreEqual(CheckState.None, SelectionEditing.States(Catalog, SelectionRules.Empty)[TypeId("Point")]);
        }

        [Test]
        public void States_All_AreWhole()
        {
            Assert.IsTrue(SelectionEditing.States(Catalog, SelectionRules.All).Values.All(s => s == CheckState.Whole));
        }

        [Test]
        public void States_CoversEveryCatalogNode()
        {
            Assert.AreEqual(Catalog.Nodes.Count, SelectionEditing.States(Catalog, SelectionRules.Empty).Count);
        }
    }
}
