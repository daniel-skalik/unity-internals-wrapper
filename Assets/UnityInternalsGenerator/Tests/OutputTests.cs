namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using DandaUtils.Editor.UnityInternalsGenerator.Emit;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Output;
    using NUnit.Framework;

    public sealed class OutputTests
    {
        private const string c_Out = "Assets/Out";

        private string m_Root;

        [SetUp]
        public void SetUp()
        {
            var projectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath);
            m_Root = Path.Combine(projectRoot, "Temp", "UnityInternalsGeneratorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(m_Root, "Assets"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true);
        }

        private static GeneratedFile File(string rel, string content) => new GeneratedFile(rel, content, Array.Empty<string>());

        private void Write(params GeneratedFile[] files) => OutputWriter.Write(m_Root, c_Out, "Host.A", files, "{\"generatorVersion\":1}\n", "# md\n", "{}\n");

        private string P(string rel) => Path.Combine(m_Root, c_Out, rel);

        [Test]
        public void Write_Twice_SecondCallModifiesNoFile()
        {
            Write(File("WrappedUnityEditor/Toolbar.g.cs", "class A {}\n"));
            var before = new Dictionary<string, DateTime>();
            foreach (var f in Directory.GetFiles(Path.Combine(m_Root, "Assets"), "*", SearchOption.AllDirectories)) before[f] = new FileInfo(f).LastWriteTimeUtc;
            System.Threading.Thread.Sleep(50);

            Write(File("WrappedUnityEditor/Toolbar.g.cs", "class A {}\n"));

            var after = Directory.GetFiles(Path.Combine(m_Root, "Assets"), "*", SearchOption.AllDirectories);
            Assert.AreEqual(before.Count, after.Length);
            foreach (var f in after) Assert.AreEqual(before[f], new FileInfo(f).LastWriteTimeUtc, f);
        }

        [Test]
        public void Write_CreatesDeterministicMetasForFilesAndFolders()
        {
            Write(File("WrappedUnityEditor/Toolbar.g.cs", "class A {}\n"));

            var guid = MetaFiles.Guid("Assets/Out/WrappedUnityEditor/Toolbar.g.cs");
            StringAssert.Contains("guid: " + guid + "\n", System.IO.File.ReadAllText(P("WrappedUnityEditor/Toolbar.g.cs.meta")));
            Assert.AreEqual(MetaFiles.ForFolder(MetaFiles.Guid("Assets/Out/WrappedUnityEditor")), System.IO.File.ReadAllText(P("WrappedUnityEditor.meta")));
            Assert.AreEqual(MetaFiles.ForFolder(MetaFiles.Guid("Assets/Out")), System.IO.File.ReadAllText(Path.Combine(m_Root, "Assets/Out.meta")));
            Assert.AreEqual(MetaFiles.ForAsmdef(MetaFiles.Guid("Assets/Out/WrappedUnityInternals.asmdef")), System.IO.File.ReadAllText(P("WrappedUnityInternals.asmdef.meta")));
            Assert.AreEqual(MetaFiles.ForText(MetaFiles.Guid("Assets/Out/GenerationInfo.json")), System.IO.File.ReadAllText(P("GenerationInfo.json.meta")));
            Assert.AreEqual(MetaFiles.ForText(MetaFiles.Guid("Assets/Out/_GenerationReport.md")), System.IO.File.ReadAllText(P("_GenerationReport.md.meta")));
            Assert.AreEqual(32, guid.Length);
        }

        [Test]
        public void Write_ExistingMetaWithOtherGuid_IsKept()
        {
            Directory.CreateDirectory(P("N"));
            const string custom = "fileFormatVersion: 2\nguid: 00000000000000000000000000000001\n";
            System.IO.File.WriteAllText(P("N/A.g.cs.meta"), custom);

            Write(File("N/A.g.cs", "class A {}\n"));

            Assert.AreEqual(custom, System.IO.File.ReadAllText(P("N/A.g.cs.meta")));
        }

        [Test]
        public void Write_StaleGeneratedFilesRemoved_UserFilesKept()
        {
            Write(File("Old.g.cs", "class Old {}\n"), File("Gone/Deep.g.cs", "class D {}\n"), File("Mixed/Stale.g.cs", "class S {}\n"));
            System.IO.File.WriteAllText(P("Notes.txt"), "mine");
            System.IO.File.WriteAllText(P("Mixed/Keep.txt"), "mine");

            Write(File("New.g.cs", "class N {}\n"));

            Assert.IsFalse(System.IO.File.Exists(P("Old.g.cs")));
            Assert.IsFalse(System.IO.File.Exists(P("Old.g.cs.meta")));
            Assert.IsFalse(Directory.Exists(P("Gone")));
            Assert.IsFalse(System.IO.File.Exists(P("Gone.meta")));
            Assert.IsFalse(System.IO.File.Exists(P("Mixed/Stale.g.cs")));
            Assert.IsFalse(System.IO.File.Exists(P("Mixed/Stale.g.cs.meta")));
            Assert.IsTrue(System.IO.File.Exists(P("Mixed/Keep.txt")));
            Assert.IsTrue(System.IO.File.Exists(P("Mixed.meta")));
            Assert.AreEqual("mine", System.IO.File.ReadAllText(P("Notes.txt")));
            Assert.IsTrue(System.IO.File.Exists(P("New.g.cs")));
        }

        [Test]
        public void Write_UserCreatedEmptyFolder_SurvivesWhileStaleFileElsewhereIsRemoved()
        {
            Write(File("Sub/Old.g.cs", "class O {}\n"));
            Directory.CreateDirectory(P("UserDir"));
            System.IO.File.WriteAllText(P("UserDir.meta"), "fileFormatVersion: 2\nguid: 00000000000000000000000000000002\n");

            Write(File("New.g.cs", "class N {}\n"));

            Assert.IsFalse(Directory.Exists(P("Sub")));
            Assert.IsFalse(System.IO.File.Exists(P("Sub.meta")));
            Assert.IsTrue(Directory.Exists(P("UserDir")));
            Assert.IsTrue(System.IO.File.Exists(P("UserDir.meta")));
        }

        [Test]
        public void Write_OrphanGeneratedMeta_IsRemovedAndFolderPruned()
        {
            Write(File("Sub/A.g.cs", "class A {}\n"));
            System.IO.File.Delete(P("Sub/A.g.cs"));

            Write(File("New.g.cs", "class N {}\n"));

            Assert.IsFalse(System.IO.File.Exists(P("Sub/A.g.cs.meta")));
            Assert.IsFalse(Directory.Exists(P("Sub")));
            Assert.IsFalse(System.IO.File.Exists(P("Sub.meta")));
        }

        [Test]
        public void Write_InvalidRelativePath_ThrowsAndWritesNothing()
        {
            Assert.Throws<ArgumentException>(() => Write(File("../Evil.g.cs", "x")));
            Assert.Throws<ArgumentException>(() => Write(File("A/../../Evil.g.cs", "x")));
            Assert.Throws<ArgumentException>(() => Write(File("/abs/Evil.g.cs", "x")));
            Assert.Throws<ArgumentException>(() => Write(File("C:/Evil.g.cs", "x")));
            Assert.IsFalse(Directory.Exists(P("")));
        }

        [Test]
        public void Write_ChangedContent_IsRewritten()
        {
            Write(File("A.g.cs", "class A {}\n"));
            Write(File("A.g.cs", "class B {}\n"));
            Assert.AreEqual("class B {}\n", System.IO.File.ReadAllText(P("A.g.cs")));
        }

        [Test]
        public void Write_Asmdef_HasExpectedContentAndNoBom()
        {
            Write();
            var bytes = System.IO.File.ReadAllBytes(P("WrappedUnityInternals.asmdef"));
            Assert.AreNotEqual(0xEF, bytes[0]);
            var text = System.IO.File.ReadAllText(P("WrappedUnityInternals.asmdef"));
            StringAssert.Contains("\"name\": \"Host.A\"", text);
            StringAssert.Contains("\"includePlatforms\": [\n        \"Editor\"\n    ]", text);
            StringAssert.Contains("\"autoReferenced\": true", text);
            Assert.IsFalse(text.Contains("\r"));
        }

        [Test]
        public void Write_ExistingAsmdefWithSameName_IsKept_DifferentNameIsRewritten()
        {
            Write();
            var path = P("WrappedUnityInternals.asmdef");
            var unityEdited = System.IO.File.ReadAllText(path).Replace("\"references\": []", "\"references\": [\"GUID:abc\"]");
            System.IO.File.WriteAllText(path, unityEdited);

            Write();
            Assert.AreEqual(unityEdited, System.IO.File.ReadAllText(path));

            OutputWriter.Write(m_Root, c_Out, "Host.B", Array.Empty<GeneratedFile>(), "{}\n", "# md\n", "{}\n");
            StringAssert.Contains("\"name\": \"Host.B\"", System.IO.File.ReadAllText(path));
            StringAssert.DoesNotContain("GUID:abc", System.IO.File.ReadAllText(path));
        }

        [Test]
        public void ReportBuild_ContainsStatusTableAndSortedSections()
        {
            var entries = new List<ReportEntry>
            {
                new ReportEntry("T:B", "r1", WrapStatus.Deferred, "later"),
                new ReportEntry("T:A", "r2", WrapStatus.Deferred, "say \"hi\"\n"),
                new ReportEntry("T:C", "r3", WrapStatus.Wrapped, ""),
            };

            var (md, json) = ReportWriter.Build(entries, "6000.0.59f2", "Host.A");

            StringAssert.Contains("| Deferred | 2 |", md);
            StringAssert.Contains("| Wrapped | 1 |", md);
            StringAssert.Contains("| CompileFailed | 0 |", md);
            Assert.Less(md.IndexOf("`T:A`", StringComparison.Ordinal), md.IndexOf("`T:B`", StringComparison.Ordinal));
            Assert.IsFalse(md.Contains("`T:C`"));
            Assert.Less(json.IndexOf("\"T:A\"", StringComparison.Ordinal), json.IndexOf("\"T:B\"", StringComparison.Ordinal));
            StringAssert.Contains("say \\\"hi\\\"\\n", json);
            StringAssert.Contains("\"unityVersion\": \"6000.0.59f2\"", json);
            Assert.IsFalse(json.Contains("\r"));
        }

        [Test]
        public void ReportBuild_SelectionAware_CountsSelectedAndListsOnlySelectedSections()
        {
            var entries = new List<ReportEntry>
            {
                new ReportEntry("T:A", "r", WrapStatus.Wrapped, ""),
                new ReportEntry("T:B", "r", WrapStatus.Wrapped, ""),
                new ReportEntry("T:D", "r", WrapStatus.Deferred, "later", false),
                new ReportEntry("T:Gone", "Selection", WrapStatus.SelectedMissing, "gone", true),
            };

            var (md, json) = ReportWriter.Build(entries, "6000.0.59f2", "Host.A");

            StringAssert.Contains("| Wrapped | 2 |", md);
            StringAssert.Contains("| Deferred | 0 |", md);
            StringAssert.Contains("| SelectedMissing | 1 |", md);
            StringAssert.Contains("| NotSelected | 1 |", md);
            StringAssert.Contains("## SelectedMissing", md);
            StringAssert.Contains("`T:Gone`", md);
            Assert.IsFalse(md.Contains("## Deferred"));
            Assert.IsFalse(md.Contains("`T:D`"));
            Assert.Greater(md.IndexOf("| NotSelected |", StringComparison.Ordinal), md.IndexOf("| SelectedMissing |", StringComparison.Ordinal));
            StringAssert.DoesNotContain("\"T:D\"", json);
            StringAssert.DoesNotContain("\"selected\": false", json);
            StringAssert.Contains("\"notSelectedCount\": 1,", json);
            StringAssert.Contains("\"T:Gone\"", json);
            StringAssert.Contains("\"selected\": true", json);
        }

        [Test]
        public void ReportBuild_JsonWithOnlyUnselected_HasEmptyEntriesAndCount()
        {
            var (_, json) = ReportWriter.Build(new List<ReportEntry> { new ReportEntry("T:D", "r", WrapStatus.Deferred, "later", false) }, "v", "Host.A");
            StringAssert.Contains("\"notSelectedCount\": 1,\n    \"entries\": []\n}", json);
        }

        [Test]
        public void StatusCounts_SelectedPerStatus_ThenNotSelected()
        {
            var counts = ReportWriter.StatusCounts(new[]
            {
                new ReportEntry("a", "r", WrapStatus.Wrapped, null),
                new ReportEntry("b", "r", WrapStatus.Wrapped, null, false),
                new ReportEntry("c", "r", WrapStatus.Deferred, null, false),
                new ReportEntry("d", "r", WrapStatus.CompileFailed, null),
            });

            var statuses = (WrapStatus[])Enum.GetValues(typeof(WrapStatus));
            Assert.AreEqual(statuses.Length + 1, counts.Count);
            CollectionAssert.AreEqual(statuses.Select(s => s.ToString()).Concat(new[] { "NotSelected" }).ToList(), counts.Select(p => p.Key).ToList());
            Assert.AreEqual(1, counts.Single(p => p.Key == "Wrapped").Value);
            Assert.AreEqual(0, counts.Single(p => p.Key == "Deferred").Value);
            Assert.AreEqual(1, counts.Single(p => p.Key == "CompileFailed").Value);
            Assert.AreEqual(2, counts.Last().Value);
        }
    }
}
