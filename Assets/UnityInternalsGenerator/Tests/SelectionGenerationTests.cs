namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using DandaUtils.Editor.UnityInternalsGenerator.Emit;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;
    using DandaUtils.Editor.UnityInternalsGenerator.Pipeline;
    using DandaUtils.Editor.UnityInternalsGenerator.Selection;
    using NUnit.Framework;
    using UnityInternalsFixture;

    public sealed class SelectionGenerationTests
    {
        private const string c_Host = "Wrapped.Fixture.Host";
        private const string c_ApiFile = "WrappedUnityInternalsFixture/StaticApi.g.cs";
        private const string c_Color8File = "WrappedUnityInternalsFixture/Color8.g.cs";
        private const BindingFlags c_AllStatic = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly Type s_Marker = typeof(FixtureMarker);

        private sealed class FakeCompiler : ICSharpCompiler
        {
            private readonly Func<int, IReadOnlyList<GeneratedFile>, IReadOnlyList<CompilerDiagnostic>> m_Behavior;
            private int m_Calls;

            public FakeCompiler(Func<int, IReadOnlyList<GeneratedFile>, IReadOnlyList<CompilerDiagnostic>> behavior) => m_Behavior = behavior;

            public IReadOnlyList<CompilerDiagnostic> Compile(IReadOnlyList<GeneratedFile> files, string assemblyName, IReadOnlyList<string> references) => m_Behavior(++m_Calls, files);
        }

        private static GenerationInput Input(SelectionRules selection) => new GenerationInput(new[] { s_Marker.Assembly }, c_Host, NamingStyle.Exact, selection, "test");

        private static GenerationResult Generate(SelectionRules selection) => WrapperGenerator.Generate(Input(selection));

        private static Type Fixture(string name) => s_Marker.Assembly.GetType("UnityInternalsFixture." + name, true);

        private static string TypeId(string name) => MemberId.Type(Fixture(name));

        private static string MethodId(string type, string name) => MemberId.Member(Fixture(type).GetMethods(c_AllStatic).First(m => m.Name == name));

        private static SelectionRules Only(params string[] ids) => new SelectionRules(ids, Array.Empty<string>());

        private static ReportEntry Entry(IReadOnlyList<ReportEntry> entries, string id) => entries.Single(e => e.Id == id);

        private static int LineOf(GeneratedFile file, string text) => Array.FindIndex(file.Content.Split('\n'), l => l.Contains(text)) + 1;

        [Test]
        public void SingleMember_EmitsOnlyItsFileAndDependencies()
        {
            var result = Generate(Only(MethodId("StaticApi", "Next")));

            CollectionAssert.AreEqual(new[] { c_Color8File, "WrappedUnityInternalsFixture/Mask.g.cs", c_ApiFile }, result.Files.Select(f => f.RelativePath).ToList());
            var api = result.Files.Single(f => f.RelativePath == c_ApiFile).Content;
            StringAssert.Contains(" Next(", api);
            StringAssert.DoesNotContain(" Add(", api);
            StringAssert.DoesNotContain("enum Nested", api);

            var add = Entry(result.Entries, MethodId("StaticApi", "Add"));
            Assert.AreEqual(WrapStatus.Wrapped, add.Status);
            Assert.IsFalse(add.Selected);
            var next = Entry(result.Entries, MethodId("StaticApi", "Next"));
            Assert.IsTrue(next.Selected);

            var color = Entry(result.Entries, TypeId("Color8"));
            Assert.IsTrue(color.Selected);
            StringAssert.StartsWith("auto-included: needed by", color.Reason);
            var container = Entry(result.Entries, TypeId("StaticApi"));
            Assert.IsTrue(container.Selected);
            Assert.AreEqual("auto-included: needed by " + MethodId("StaticApi", "Next"), container.Reason);
        }

        [Test]
        public void Unselected_KeepsRealStatus_NotExcluded()
        {
            var result = Generate(Only(MethodId("StaticApi", "Next")));

            var holder = Entry(result.Entries, TypeId("Holder"));
            Assert.AreEqual(WrapStatus.Wrapped, holder.Status);
            Assert.IsFalse(holder.Selected);
            var nested = Entry(result.Entries, TypeId("StaticApi+Nested"));
            Assert.AreEqual(WrapStatus.Wrapped, nested.Status);
            Assert.IsFalse(nested.Selected);
        }

        [Test]
        public void ExceptedMember_NotEmitted_KeepsStatus()
        {
            var reset = MethodId("StaticApi", "Reset");
            var result = Generate(new SelectionRules(new[] { SelectionIds.Namespace("UnityInternalsFixture") }, new[] { reset }));

            var entry = Entry(result.Entries, reset);
            Assert.AreEqual(WrapStatus.Wrapped, entry.Status);
            Assert.IsFalse(entry.Selected);
            var api = result.Files.Single(f => f.RelativePath == c_ApiFile).Content;
            StringAssert.DoesNotContain(" Reset(", api);
            StringAssert.Contains(" Add(", api);
        }

        [Test]
        public void NullSelection_MeansAll()
        {
            var input = Input(null);
            Assert.AreSame(SelectionRules.All, input.Selection);

            var none = WrapperGenerator.Generate(input);
            var all = Generate(SelectionRules.All);
            CollectionAssert.AreEqual(all.Files.Select(f => f.RelativePath + "\n" + f.Content).ToList(), none.Files.Select(f => f.RelativePath + "\n" + f.Content).ToList());
            Assert.IsTrue(none.Entries.Where(e => none.Catalog.Contains(e.Id)).All(e => e.Selected));
        }

        [Test]
        public void Result_ExposesCatalogAndSelection()
        {
            var result = Generate(Only(MethodId("StaticApi", "Next")));
            Assert.IsNotNull(result.Catalog);
            Assert.IsNotNull(result.Selection);
            Assert.IsTrue(result.Catalog.Contains(TypeId("StaticApi")));
            Assert.IsTrue(result.Selection.IsSelected(MethodId("StaticApi", "Next")));
            Assert.AreEqual(result.Files.Count, result.Selection.EmittedFileCount);
        }

        [Test]
        public void EmptySelection_GeneratesNoFiles()
        {
            var result = Generate(SelectionRules.Empty);
            Assert.IsEmpty(result.Files);
            Assert.IsTrue(result.Entries.All(e => e.Selected == false));
        }

        [Test]
        public void MissingId_ReportedSelectedMissing()
        {
            const string missing = "Gone.Assembly|Gone.Type|Type|Type";
            var result = Generate(Only(missing, MethodId("StaticApi", "Next")));

            var entry = Entry(result.Entries, missing);
            Assert.AreEqual(WrapStatus.SelectedMissing, entry.Status);
            Assert.AreEqual("Selection", entry.Rule);
            Assert.AreEqual("selected id not found in current Unity", entry.Reason);
            Assert.IsTrue(entry.Selected);
            var ids = result.Entries.Select(e => e.Id).ToList();
            CollectionAssert.AreEqual(ids.OrderBy(i => i, StringComparer.Ordinal).ToList(), ids);
        }

        [Test]
        public void MissingExceptionId_UsesExceptionReason()
        {
            const string missing = "Gone.Assembly|Gone.Type|Type|Type";
            var result = Generate(new SelectionRules(new[] { MethodId("StaticApi", "Next") }, new[] { missing }));

            var entry = Entry(result.Entries, missing);
            Assert.AreEqual(WrapStatus.SelectedMissing, entry.Status);
            Assert.AreEqual("exception id not found in current Unity", entry.Reason);
        }

        [Test]
        public void SelectedMember_OfAutoExcludedContainer_ReportedExcluded_NotMissing()
        {
            var add = MethodId("StaticApi", "Add");
            var compiler = new FakeCompiler((pass, files) => pass > 1
                ? Array.Empty<CompilerDiagnostic>()
                : new[] { new CompilerDiagnostic(c_ApiFile, 1, "CS0103", "boom", true) });

            var result = GenerationRunner.Run(Input(Only(add)), compiler, Array.Empty<string>(), 5);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(WrapStatus.CompileFailed, Entry(result.Entries, TypeId("StaticApi")).Status);
            var entry = Entry(result.Entries, add);
            Assert.AreEqual(WrapStatus.Excluded, entry.Status);
            Assert.AreEqual("Declaring type auto-excluded: failed to compile", entry.Reason);
            Assert.IsTrue(entry.Selected);
            Assert.IsFalse(result.Entries.Any(e => e.Status == WrapStatus.SelectedMissing));
        }

        [Test]
        public void SelectedMember_OfNonWrappedContainer_ReportedWithDeclaringStatus()
        {
            // Member id under the Deferred struct Point, which has no member nodes.
            var member = MemberId.Type(Fixture("Point")).Replace("|Type|Point", "|Method|Gone()");
            var entry = Entry(Generate(Only(member)).Entries, member);
            Assert.AreEqual(WrapStatus.Excluded, entry.Status);
            Assert.AreEqual("Declaring type is Deferred", entry.Reason);
            Assert.IsTrue(entry.Selected);
        }

        [Test]
        public void CompileFailedDependency_IsNotResurrected_CascadeReported()
        {
            var compiler = new FakeCompiler((pass, files) =>
            {
                if (pass > 1) return Array.Empty<CompilerDiagnostic>();
                var color = files.Single(f => f.RelativePath == c_Color8File);
                return new[] { new CompilerDiagnostic(c_Color8File, LineOf(color, "public enum Color8"), "CS0103", "boom", true) };
            });

            var result = GenerationRunner.Run(Input(Only(MethodId("StaticApi", "Next"))), compiler, Array.Empty<string>(), 5);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.Passes);
            CollectionAssert.DoesNotContain(result.Files.Select(f => f.RelativePath).ToList(), c_Color8File);
            var color8 = Entry(result.Entries, TypeId("Color8"));
            Assert.AreEqual(WrapStatus.CompileFailed, color8.Status);
            Assert.IsTrue(color8.Selected);
            var next = Entry(result.Entries, MethodId("StaticApi", "Next"));
            Assert.AreEqual(WrapStatus.Excluded, next.Status);
            StringAssert.Contains("AutoExclusion", next.Reason);
            Assert.IsTrue(next.Selected);
        }
    }
}
