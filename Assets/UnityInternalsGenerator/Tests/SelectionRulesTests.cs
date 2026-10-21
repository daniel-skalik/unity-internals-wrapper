namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using System;
    using System.Collections.Generic;
    using DandaUtils.Editor.UnityInternalsGenerator.Selection;
    using NUnit.Framework;

    public sealed class SelectionRulesTests
    {
        // Tree a -> b -> c (c is the deepest node, a is the root).
        private static readonly Dictionary<string, string> s_Parents = new Dictionary<string, string>
        {
            { "a", null },
            { "b", "a" },
            { "c", "b" },
        };

        private static string ParentOf(string id) => s_Parents.TryGetValue(id, out var parent) ? parent : null;

        [Test]
        public void IncludedAncestor_SelectsDescendant()
        {
            var rules = new SelectionRules(new[] { "a" }, Array.Empty<string>());

            Assert.IsTrue(rules.IsSelected("c", ParentOf));
        }

        [Test]
        public void ExcludedMiddle_DeselectsOnlyBelowIt()
        {
            var rules = new SelectionRules(new[] { "a" }, new[] { "b" });

            Assert.IsFalse(rules.IsSelected("c", ParentOf));
            Assert.IsTrue(rules.IsSelected("a", ParentOf));
        }

        [Test]
        public void DeepestRuleWins_IncludedLeafInsideExcludedMiddle()
        {
            var rules = new SelectionRules(new[] { "a", "c" }, new[] { "b" });

            Assert.IsTrue(rules.IsSelected("c", ParentOf));
            Assert.IsFalse(rules.IsSelected("b", ParentOf));
        }

        [Test]
        public void EmptyRules_SelectNothing()
        {
            var rules = SelectionRules.Empty;

            Assert.IsTrue(rules.IsEmpty);
            Assert.IsFalse(rules.IsSelected("a", ParentOf));
            Assert.IsFalse(rules.IsSelected("c", ParentOf));
        }

        [Test]
        public void All_SelectsEverything()
        {
            Assert.IsTrue(SelectionRules.All.IsSelected("x", _ => null));
            Assert.IsFalse(SelectionRules.All.IsEmpty);
            Assert.IsEmpty(SelectionRules.All.Included);
            Assert.IsEmpty(SelectionRules.All.Excluded);
            Assert.IsNull(SelectionRules.All.RuleAt("x"));
        }

        [Test]
        public void Constructor_DedupesAndSortsOrdinal()
        {
            var rules = new SelectionRules(new[] { "b", "a", "a" }, new[] { "d", "c", "c" });

            CollectionAssert.AreEqual(new[] { "a", "b" }, rules.Included);
            CollectionAssert.AreEqual(new[] { "c", "d" }, rules.Excluded);
        }

        [Test]
        public void RuleAt_IdInBothLists_ExcludedWins()
        {
            var rules = new SelectionRules(new[] { "a" }, new[] { "a" });

            Assert.AreEqual(false, rules.RuleAt("a"));
        }

        [Test]
        public void RuleAt_ReturnsNullWhenNoRule()
        {
            var rules = new SelectionRules(new[] { "a" }, Array.Empty<string>());

            Assert.IsNull(rules.RuleAt("zzz"));
            Assert.AreEqual(true, rules.RuleAt("a"));
        }

        [Test]
        public void IsSelected_CycleInParents_TerminatesAndReturnsFalse()
        {
            var cyclic = new Dictionary<string, string> { { "x", "y" }, { "y", "x" } };
            var rules = new SelectionRules(new[] { "q" }, Array.Empty<string>());

            Assert.IsFalse(rules.IsSelected("x", id => cyclic.TryGetValue(id, out var p) ? p : null));
        }

        [Test]
        public void IsSelected_RuleOnNodeInsideCycle_TakesEffect()
        {
            var cyclic = new Dictionary<string, string> { { "x", "y" }, { "y", "x" } };
            var rules = new SelectionRules(new[] { "x" }, Array.Empty<string>());

            Assert.IsTrue(rules.IsSelected("x", id => cyclic.TryGetValue(id, out var p) ? p : null));
            Assert.IsTrue(rules.IsSelected("y", id => cyclic.TryGetValue(id, out var p) ? p : null));
        }

        [Test]
        public void IsSelected_ExcludedNodeInsideCycle_DeselectsAroundCycle()
        {
            var cyclic = new Dictionary<string, string> { { "x", "y" }, { "y", "x" } };
            var rules = new SelectionRules(Array.Empty<string>(), new[] { "y" });

            Assert.IsFalse(rules.IsSelected("x", id => cyclic.TryGetValue(id, out var p) ? p : null));
        }

        [Test]
        public void IsEmpty_ExclusionsOnly_IsTrue()
        {
            var rules = new SelectionRules(Array.Empty<string>(), new[] { "a" });

            Assert.IsTrue(rules.IsEmpty);
            Assert.IsTrue(SelectionRules.Empty.IsEmpty);
        }

        [Test]
        public void IsEmpty_IncludeOnly_IsFalse()
        {
            var rules = new SelectionRules(new[] { "a" }, Array.Empty<string>());

            Assert.IsFalse(rules.IsEmpty);
        }

        [Test]
        public void Namespace_NullIsGlobal()
        {
            Assert.AreEqual("ns:", SelectionIds.Namespace(null));
            Assert.AreEqual("ns:UnityEditor.Experimental", SelectionIds.Namespace("UnityEditor.Experimental"));
        }

        [Test]
        public void NamespaceParent_Rules()
        {
            Assert.AreEqual("ns:A", SelectionIds.NamespaceParent("ns:A.B"));
            Assert.IsNull(SelectionIds.NamespaceParent("ns:A"));
            Assert.IsNull(SelectionIds.NamespaceParent("ns:"));
        }
    }
}
