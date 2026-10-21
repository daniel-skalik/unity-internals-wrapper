namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DandaUtils.Editor.UnityInternalsGenerator.Emit;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;
    using DandaUtils.Editor.UnityInternalsGenerator.Pipeline;
    using NUnit.Framework;
    using UnityInternalsFixture;

    public sealed class CompileTests
    {
        private const string c_Host = "Wrapped.Fixture.Host";
        private const string c_ApiFile = "WrappedUnityInternalsFixture/StaticApi.g.cs";

        private sealed class FakeCompiler : ICSharpCompiler
        {
            private readonly Func<int, IReadOnlyList<GeneratedFile>, IReadOnlyList<CompilerDiagnostic>> m_Behavior;

            public FakeCompiler(Func<int, IReadOnlyList<GeneratedFile>, IReadOnlyList<CompilerDiagnostic>> behavior) => m_Behavior = behavior;

            public int Calls { get; private set; }

            public IReadOnlyList<CompilerDiagnostic> Compile(IReadOnlyList<GeneratedFile> files, string assemblyName, IReadOnlyList<string> references)
            {
                Calls++;
                return m_Behavior(Calls, files);
            }
        }

        private static GenerationInput Input() => new GenerationInput(new[] { typeof(FixtureMarker).Assembly }, c_Host, NamingStyle.Exact, null, "test");

        private static int LineOf(GeneratedFile file, string text)
        {
            var lines = file.Content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(text)) return i + 1;
            }
            throw new InvalidOperationException("Line not found: " + text);
        }

        private static GeneratedFile Api(IReadOnlyList<GeneratedFile> files) => files.First(f => f.RelativePath == c_ApiFile);

        [Test]
        public void RealCompile_FixtureOutput_HasNoErrors()
        {
            var input = Input();
            var files = WrapperGenerator.Generate(input).Files;
            var references = CompilerReferences.Collect(input.Sources);

            var diagnostics = new RoslynProcessCompiler().Compile(files, c_Host, references);

            Assert.AreEqual(0, diagnostics.Count(d => d.IsError), string.Join("\n", diagnostics.Where(d => d.IsError)));
        }

        [Test]
        public void RealCompile_BrokenFile_ReportsErrorWithRelativePath()
        {
            var broken = new GeneratedFile("Broken/X.g.cs", "class X { int }\n", new string[1]);

            var diagnostics = new RoslynProcessCompiler().Compile(new[] { broken }, "Broken.Host", CompilerReferences.Collect(Input().Sources));

            var errors = diagnostics.Where(d => d.IsError).ToList();
            Assert.AreEqual(1, errors.Count, string.Join("\n", diagnostics));
            Assert.AreEqual(1, errors[0].Line);
            StringAssert.StartsWith("CS", errors[0].Code);
            Assert.AreEqual("Broken/X.g.cs", errors[0].File);
        }

        [Test]
        public void Runner_ErrorOnAdd_ExcludedOnSecondPass_ReportedCompileFailed()
        {
            var compiler = new FakeCompiler((pass, files) =>
            {
                if (pass > 1) return Array.Empty<CompilerDiagnostic>();
                var api = Api(files);
                return new[] { new CompilerDiagnostic(c_ApiFile, LineOf(api, " Add("), "CS0103", "boom", true) };
            });

            var result = GenerationRunner.Run(Input(), compiler, Array.Empty<string>(), 5);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.Passes);
            Assert.IsFalse(Api(result.Files).Content.Contains("Add("));
            var add = result.Entries.Single(e => e.Id.Contains("|Method|Add("));
            Assert.AreEqual(WrapStatus.CompileFailed, add.Status);
            Assert.AreEqual("CS0103: boom", add.Reason);
            Assert.AreEqual(0, result.RemainingErrors.Count);
        }

        [Test]
        public void Runner_AlwaysErroring_StopsAtMaxPasses()
        {
            var compiler = new FakeCompiler((pass, files) => files.Count == 0
                ? Array.Empty<CompilerDiagnostic>()
                : new[] { new CompilerDiagnostic(files[0].RelativePath, 1, "CS1002", "always", true) });

            var result = GenerationRunner.Run(Input(), compiler, Array.Empty<string>(), 3);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(3, result.Passes);
            Assert.AreEqual(3, compiler.Calls);
            Assert.IsNotEmpty(result.RemainingErrors);
        }

        [Test]
        public void Runner_AbsoluteTempPathInDiagnostic_StillMapsBySuffix()
        {
            var compiler = new FakeCompiler((pass, files) => pass > 1
                ? Array.Empty<CompilerDiagnostic>()
                : new[] { new CompilerDiagnostic("C:/x/Temp/UnityInternalsGenerator/abc/" + c_ApiFile, LineOf(Api(files), " Add("), "CS0103", "boom", true) });

            var result = GenerationRunner.Run(Input(), compiler, Array.Empty<string>(), 5);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(WrapStatus.CompileFailed, result.Entries.Single(e => e.Id.Contains("|Method|Add(")).Status);
        }

        [Test]
        public void Runner_ErrorWithoutFile_IsFatalAndExcludesNothing()
        {
            var compiler = new FakeCompiler((pass, files) => new[] { new CompilerDiagnostic(null, 0, "CS0006", "missing reference", true) });

            var result = GenerationRunner.Run(Input(), compiler, Array.Empty<string>(), 5);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, result.Passes);
            Assert.AreEqual(1, result.RemainingErrors.Count);
            Assert.IsFalse(result.Entries.Any(e => e.Status == WrapStatus.CompileFailed));
        }

        [Test]
        public void Runner_Warnings_AreIgnored()
        {
            var compiler = new FakeCompiler((pass, files) => new[] { new CompilerDiagnostic(c_ApiFile, 1, "CS0108", "hides", false) });

            var result = GenerationRunner.Run(Input(), compiler, Array.Empty<string>(), 5);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.Passes);
        }

        [Test]
        public void Runner_AutoExcludedType_CascadedEntriesSayAutoExcluded()
        {
            var compiler = new FakeCompiler((pass, files) => pass > 1
                ? Array.Empty<CompilerDiagnostic>()
                : new[] { new CompilerDiagnostic(c_ApiFile, 1, "CS0103", "boom", true) });

            var result = GenerationRunner.Run(Input(), compiler, Array.Empty<string>(), 5);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(WrapStatus.CompileFailed, result.Entries.Single(e => e.Id.EndsWith("|UnityInternalsFixture.StaticApi|Type|StaticApi")).Status);
            var nested = result.Entries.Single(e => e.Id.EndsWith("|UnityInternalsFixture.StaticApi+Nested|Type|Nested"));
            Assert.AreEqual(WrapStatus.Excluded, nested.Status);
            Assert.AreEqual("Declaring type auto-excluded: failed to compile", nested.Reason);
        }

        [Test]
        public void Runner_DoesNotMutateCallerInput()
        {
            var input = Input();
            var compiler = new FakeCompiler((pass, files) => pass > 1
                ? Array.Empty<CompilerDiagnostic>()
                : new[] { new CompilerDiagnostic(c_ApiFile, LineOf(Api(files), " Add("), "CS0103", "boom", true) });

            GenerationRunner.Run(input, compiler, Array.Empty<string>(), 5);

            Assert.AreEqual(0, input.AutoExcludedIds.Count);
        }
    }
}
