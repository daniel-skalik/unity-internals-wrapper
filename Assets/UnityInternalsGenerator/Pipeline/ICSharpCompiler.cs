namespace DandaUtils.Editor.UnityInternalsGenerator.Pipeline
{
    using System.Collections.Generic;
    using DandaUtils.Editor.UnityInternalsGenerator.Emit;

    public interface ICSharpCompiler
    {
        IReadOnlyList<CompilerDiagnostic> Compile(IReadOnlyList<GeneratedFile> files, string assemblyName, IReadOnlyList<string> references);
    }
}
