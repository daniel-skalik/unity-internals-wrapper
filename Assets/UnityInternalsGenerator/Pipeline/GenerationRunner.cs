namespace DandaUtils.Editor.UnityInternalsGenerator.Pipeline
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DandaUtils.Editor.UnityInternalsGenerator.Emit;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;

    public sealed class RunResult
    {
        public RunResult(bool success, IReadOnlyList<GeneratedFile> files, IReadOnlyList<ReportEntry> entries, IReadOnlyList<CompilerDiagnostic> remainingErrors, int passes)
        {
            Success = success;
            Files = files;
            Entries = entries;
            RemainingErrors = remainingErrors;
            Passes = passes;
        }

        public bool Success { get; }
        public IReadOnlyList<GeneratedFile> Files { get; }
        public IReadOnlyList<ReportEntry> Entries { get; }
        public IReadOnlyList<CompilerDiagnostic> RemainingErrors { get; }
        public int Passes { get; }
    }

    /// <summary>Generate, pre-compile, auto-exclude whatever fails to compile, repeat. Auto-excludes are never persisted.</summary>
    public static class GenerationRunner
    {
        public static RunResult Run(GenerationInput input, ICSharpCompiler compiler, IReadOnlyList<string> references, int maxPasses)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (compiler == null) throw new ArgumentNullException(nameof(compiler));

            var autoExcluded = new Dictionary<string, string>(StringComparer.Ordinal);
            var passes = 0;
            GenerationResult generation = null;
            IReadOnlyList<CompilerDiagnostic> errors = Array.Empty<CompilerDiagnostic>();

            while (passes < maxPasses)
            {
                passes++;
                var passInput = new GenerationInput(input.Sources, input.HostAssemblyName, input.Naming, input.Selection, input.UnityVersion, autoExcluded.Keys.ToList());
                generation = WrapperGenerator.Generate(passInput);

                errors = compiler.Compile(generation.Files, input.HostAssemblyName, references).Where(d => d.IsError).ToList();
                if (errors.Count == 0) return Finish(true, generation, autoExcluded, errors, passes);

                // Unattributable errors end the loop without excluding anything: guessing would drop unrelated types.
                var attributed = Attribute(generation.Files, errors);
                if (attributed == null) break;

                var added = false;
                foreach (var pair in attributed)
                {
                    if (autoExcluded.ContainsKey(pair.Key)) continue;
                    autoExcluded[pair.Key] = pair.Value;
                    added = true;
                }
                if (added == false) break;
            }

            return Finish(false, generation, autoExcluded, errors, passes);
        }

        // id -> "<Code>: <Message>" of the first error per id, or null when any error cannot be mapped to a generated file.
        private static Dictionary<string, string> Attribute(IReadOnlyList<GeneratedFile> files, IReadOnlyList<CompilerDiagnostic> errors)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var error in errors)
            {
                var id = OwnerOf(files, error);
                if (id == null) return null;
                if (result.ContainsKey(id) == false) result[id] = error.Code + ": " + error.Message;
            }
            return result;
        }

        private static string OwnerOf(IReadOnlyList<GeneratedFile> files, CompilerDiagnostic error)
        {
            if (error.File == null) return null;
            var path = error.File.Replace('\\', '/');
            var file = files.FirstOrDefault(f => path.Equals(f.RelativePath, StringComparison.OrdinalIgnoreCase) || path.EndsWith("/" + f.RelativePath, StringComparison.OrdinalIgnoreCase));
            if (file == null) return null;

            var owner = file.MemberAt(error.Line);
            if (owner != null) return owner;

            // Outside any member scope (header, braces): the first owned line of the file belongs to the top-level type.
            var lineCount = file.Content.Count(c => c == '\n') + 1;
            for (var line = 1; line <= lineCount; line++)
            {
                var candidate = file.MemberAt(line);
                if (candidate != null) return candidate;
            }
            return null;
        }

        private static RunResult Finish(bool success, GenerationResult generation, Dictionary<string, string> autoExcluded, IReadOnlyList<CompilerDiagnostic> errors, int passes)
        {
            var entries = generation.Entries.Where(e => autoExcluded.ContainsKey(e.Id) == false).ToList();
            foreach (var pair in autoExcluded)
            {
                var previous = generation.Entries.FirstOrDefault(e => e.Id == pair.Key);
                // Only emitted ids reach the compiler, so a compile failure is always shown as selected even if the final pass no longer needs it.
                entries.Add(new ReportEntry(pair.Key, previous?.Rule ?? "Compile", WrapStatus.CompileFailed, pair.Value, true));
            }
            entries.Sort((a, b) =>
            {
                var byId = string.CompareOrdinal(a.Id, b.Id);
                return byId != 0 ? byId : string.CompareOrdinal(a.Rule, b.Rule);
            });
            return new RunResult(success, generation.Files, entries, success ? Array.Empty<CompilerDiagnostic>() : errors, passes);
        }
    }
}
