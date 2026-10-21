namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using DandaUtils.Editor.UnityInternalsGenerator.Discovery;
    using NUnit.Framework;
    using UnityEngine;
    using UnityInternalsFixture;

    public sealed class BridgeAccessTests
    {
        private static IReadOnlyCollection<string> Range(string kind, int from, int to, string suffix = "")
        {
            var list = new List<string>();
            for (var i = from; i <= to; i++) list.Add($"Unity.InternalAPI{kind}Bridge{suffix}.{i:000}");
            return list;
        }

        private static IReadOnlyList<IReadOnlyCollection<string>> Grants()
        {
            var editor = Range("Editor", 1, 24).Concat(Range("Engine", 1, 24)).ToList();
            var engine = Range("Engine", 1, 24);
            return new List<IReadOnlyCollection<string>> { editor, engine };
        }

        private static readonly HashSet<string> s_Used = new HashSet<string>
        {
            "Unity.InternalAPIEditorBridge.001", "Unity.InternalAPIEngineBridge.001", "Unity.InternalAPIEngineBridge.020"
        };

        [Test]
        public void ChooseHostName_PicksMostGrantedFreeName()
        {
            var name = BridgeAccess.ChooseHostName(Grants(), s_Used, null, out var warning);
            Assert.AreEqual("Unity.InternalAPIEngineBridge.002", name);
            Assert.IsNull(warning);
        }

        [Test]
        public void ChooseHostName_KeepsFreePersisted()
        {
            var name = BridgeAccess.ChooseHostName(Grants(), s_Used, "Unity.InternalAPIEngineBridge.005", out var warning);
            Assert.AreEqual("Unity.InternalAPIEngineBridge.005", name);
            Assert.IsNull(warning);
        }

        [Test]
        public void ChooseHostName_PersistedNowUsed_PicksNewAndWarns()
        {
            var used = new HashSet<string>(s_Used) { "Unity.InternalAPIEngineBridge.005" };
            var name = BridgeAccess.ChooseHostName(Grants(), used, "Unity.InternalAPIEngineBridge.005", out var warning);
            Assert.AreEqual("Unity.InternalAPIEngineBridge.002", name);
            Assert.IsNotNull(warning);
        }

        [Test]
        public void ChooseHostName_PersistedGrantedByFewerSources_Dropped()
        {
            var name = BridgeAccess.ChooseHostName(Grants(), s_Used, "Unity.InternalAPIEditorBridge.005", out var warning);
            Assert.AreEqual("Unity.InternalAPIEngineBridge.002", name);
            Assert.IsNotNull(warning);
        }

        [Test]
        public void ChooseHostName_OnlyDev_PicksDev()
        {
            var grants = new List<IReadOnlyCollection<string>> { Range("Engine", 1, 3, "Dev") };
            var name = BridgeAccess.ChooseHostName(grants, new HashSet<string>(), null, out _);
            Assert.AreEqual("Unity.InternalAPIEngineBridgeDev.001", name);
        }

        [Test]
        public void ChooseHostName_NoCandidate_Null()
        {
            var grants = new List<IReadOnlyCollection<string>> { new[] { "Other.Friend" } };
            Assert.IsNull(BridgeAccess.ChooseHostName(grants, new HashSet<string>(), null, out _));
        }

        [Test]
        public void GetGrantedFriends_Unity()
        {
            var friends = BridgeAccess.GetGrantedFriends(typeof(UnityEditor.Editor).Assembly);
            CollectionAssert.Contains(friends, "Unity.InternalAPIEngineBridge.001");
        }

        [Test]
        public void GetGrantedFriends_Fixture()
        {
            var friends = BridgeAccess.GetGrantedFriends(typeof(FixtureMarker).Assembly);
            CollectionAssert.Contains(friends, "Wrapped.Fixture.Host");
            Assert.IsTrue(BridgeAccess.Grants(typeof(FixtureMarker).Assembly, "Wrapped.Fixture.Host"));
            Assert.IsFalse(BridgeAccess.Grants(typeof(FixtureMarker).Assembly, "Nope"));
        }

        [Test]
        public void UsedAssemblyNames_ContainsExistingBridge()
        {
            var root = Directory.GetParent(Application.dataPath).FullName;
            var names = UsedAssemblyNames.Collect(root, null);
            Assert.IsTrue(names.Contains("Unity.InternalAPIEngineBridge.020"));
        }

        [Test]
        public void UsedAssemblyNames_ExcludesOwnOutput_AndSkipsBadFiles()
        {
            var root = Path.Combine(Path.GetTempPath(), "uig_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Assets", "Own"));
                Directory.CreateDirectory(Path.Combine(root, "Assets", "Other"));
                File.WriteAllText(Path.Combine(root, "Assets", "Own", "Own.asmdef"), "{ \"name\": \"Zz.Own.Name\" }");
                File.WriteAllText(Path.Combine(root, "Assets", "Other", "Other.asmdef"), "{ \"name\": \"Zz.Other.Name\" }");
                File.WriteAllText(Path.Combine(root, "Assets", "Other", "Bad.asmdef"), "not json {{{");
                var own = Path.Combine(root, "Assets", "Own", "Own.asmdef").Replace(Path.DirectorySeparatorChar, '/');
                var names = UsedAssemblyNames.Collect(root, own);
                Assert.IsTrue(names.Contains("Zz.Other.Name"));
                Assert.IsFalse(names.Contains("Zz.Own.Name"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
