namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using DandaUtils.Editor.UnityInternalsGenerator.Discovery;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;
    using DandaUtils.Editor.UnityInternalsGenerator.Output;
    using DandaUtils.Editor.UnityInternalsGenerator.Pipeline;
    using DandaUtils.Editor.UnityInternalsGenerator.Selection;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Real run over the default included assemblies. Explicit: it classifies both CoreModules and pre-compiles the whole output,
    /// which takes minutes. Run it by name after changing a rule, and read the logged counts.
    /// </summary>
    public sealed class RealRunTests
    {
        private static readonly string[] s_DefaultAssemblies = { "UnityEditor.CoreModule", "UnityEngine.CoreModule" };

        [Test, Explicit("Minutes: classifies both CoreModules and pre-compiles the full output.")]
        public void CoreModules_WholeSelection_PreCompilesClean()
        {
            var sources = s_DefaultAssemblies
                .Select(name => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name))
                .Where(a => a != null)
                .ToList();
            Assert.AreEqual(s_DefaultAssemblies.Length, sources.Count, "default assemblies not loaded");

            var grants = sources.Select(BridgeAccess.GetGrantedFriends).ToList();
            var host = BridgeAccess.ChooseHostName(grants, new HashSet<string>(StringComparer.Ordinal), null, out _);
            Assert.IsNotEmpty(host);

            var input = new GenerationInput(sources, host, NamingStyle.Exact, SelectionRules.All, Application.unityVersion);
            var result = GenerationRunner.Run(input, new RoslynProcessCompiler(), CompilerReferences.Collect(sources), 5);

            Debug.Log("Real run: host " + host + ", " + result.Files.Count + " files, " + result.Passes + " pass(es)\n" +
                string.Join("\n", ReportWriter.StatusCounts(result.Entries).Select(p => p.Key + ": " + p.Value)));

            Assert.IsTrue(result.Success, string.Join("\n", result.RemainingErrors.Take(25).Select(d => d.ToString())));
        }
    }
}
