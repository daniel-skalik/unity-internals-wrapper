namespace DandaUtils.Editor.UnityInternalsGenerator.Pipeline
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Assembly = System.Reflection.Assembly;
    using UnityEditor.Compilation;

    public static class CompilerReferences
    {
        private const string c_GeneratorAssemblyName = "DandaUtils.UnityInternalsGenerator.Editor";

        /// <summary>
        /// Generator assembly references (same set Unity gives the generated editor asmdef), plus each source assembly.
        /// </summary>
        public static IReadOnlyList<string> Collect(IReadOnlyList<Assembly> sources)
        {
            var paths = new List<string>();

            var generator = CompilationPipeline.GetAssemblies(AssembliesType.Editor).FirstOrDefault(a => a.name == c_GeneratorAssemblyName);
            if (generator != null) paths.AddRange(generator.compiledAssemblyReferences);

            // Only what Unity itself would hand an editor asmdef: pulling in further loaded assemblies (e.g. Bee.*) would let the
            // pre-compile pass on code that the real asmdef compile rejects. Types needing other assemblies get auto-excluded instead.
            foreach (var source in sources) paths.Add(source.Location);

            return paths
                .Where(p => string.IsNullOrEmpty(p) == false)
                .Select(p => Path.GetFullPath(p).Replace('\\', '/'))
                .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }
    }
}
