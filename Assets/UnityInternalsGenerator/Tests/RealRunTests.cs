namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
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
    /// Runs the generator over the real editor assemblies. The whole-selection run is Explicit: it classifies both CoreModules and
    /// pre-compiles the full output, which takes minutes. Run it by name after changing a rule, and read the logged counts.
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

            var publicTypes = new HashSet<string>(result.Entries.Where(e => e.Rule == "PublicTypeInternalMembers").Select(e => e.Id), StringComparer.Ordinal);
            var publicMembers = result.Entries.Where(e => publicTypes.Contains(MemberId.DeclaringTypeId(e.Id) ?? string.Empty)).ToList();

            Debug.Log("Real run: host " + host + ", " + result.Files.Count + " files, " + result.Passes + " pass(es)\n" +
                "public types wrapped (step 4): " + publicTypes.Count + "\n" +
                "their members: " + publicMembers.Count + ", of them Wrapped: " + publicMembers.Count(e => e.Status == WrapStatus.Wrapped) + "\n" +
                string.Join("\n", ReportWriter.StatusCounts(result.Entries).Select(p => p.Key + ": " + p.Value)));

            Assert.IsTrue(result.Success, string.Join("\n", result.RemainingErrors.Take(25).Select(d => d.ToString())));
        }

        /// <summary>
        /// Step 4 end to end on the example of `docs/ENGINEERING.md`: the internal <c>UnityEditor.Menu.AddMenuItem</c> is wrapped,
        /// the wrapper compiles against the real reference set and the call reaches Unity.
        /// </summary>
        [Test, Category("Slow")]
        public void Menu_AddMenuItem_IsWrapped_CompilesAndIsCallable()
        {
            var editor = typeof(UnityEditor.Editor).Assembly;
            var host = BridgeAccess.ChooseHostName(new[] { BridgeAccess.GetGrantedFriends(editor) }, new HashSet<string>(StringComparer.Ordinal), null, out _);
            var add = typeof(UnityEditor.Menu).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(m => m.Name == "AddMenuItem")
                .OrderBy(m => m.GetParameters().Length)
                .Last();
            var id = MemberId.Member(add);

            var input = new GenerationInput(new[] { editor }, host, NamingStyle.Exact, new SelectionRules(new[] { id }, Array.Empty<string>()), Application.unityVersion);
            var result = WrapperGenerator.Generate(input);

            Assert.AreEqual(WrapStatus.Wrapped, result.Entries.Single(e => e.Id == id).Status, "AddMenuItem status");
            var file = result.Files.Single(f => f.RelativePath == "WrappedUnityEditor/Menu.g.cs");
            StringAssert.Contains("public static void AddMenuItem(", file.Content);

            var outputDir = Path.Combine(Path.GetTempPath(), "UnityInternalsGeneratorMenu", Guid.NewGuid().ToString("N")).Replace('\\', '/');
            try
            {
                var diagnostics = new RoslynProcessCompiler(outputDir).Compile(result.Files, host, CompilerReferences.Collect(input.Sources));
                Assert.AreEqual(0, diagnostics.Count(d => d.IsError), string.Join("\n", diagnostics.Where(d => d.IsError).Select(d => d.ToString())));

                var wrapper = Assembly.Load(File.ReadAllBytes(outputDir + "/" + host + ".dll")).GetType("WrappedUnityEditor.Menu", true);
                var wrapped = wrapper.GetMethod("AddMenuItem", BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(wrapped, "wrapper AddMenuItem");
                CollectionAssert.AreEqual(add.GetParameters().Select(p => p.ParameterType).ToList(), wrapped.GetParameters().Select(p => p.ParameterType).ToList());

                Assert.DoesNotThrow(() => wrapped.Invoke(null, Arguments(wrapped)));
            }
            finally
            {
                if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
            }
        }

        /// <summary>Arguments for a menu-registration call, built from the parameter types so the test does not hardcode Unity's signature.</summary>
        private static object[] Arguments(MethodInfo m)
        {
            var parameters = m.GetParameters();
            var args = new object[parameters.Length];
            var firstString = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                var t = parameters[i].ParameterType;
                if (t == typeof(string))
                {
                    args[i] = firstString ? "DandaUtilsGeneratorTest/Added" : string.Empty;
                    firstString = false;
                }
                else if (t == typeof(Action)) args[i] = new Action(() => { });
                else if (t == typeof(Func<bool>)) args[i] = new Func<bool>(() => false);
                else if (t.IsValueType) args[i] = Activator.CreateInstance(t);
                else args[i] = null;
            }
            return args;
        }
    }
}
