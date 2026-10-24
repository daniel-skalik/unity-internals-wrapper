namespace DandaUtils.Editor.UnityInternalsGenerator.Pipeline
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using DandaUtils.Editor.UnityInternalsGenerator.Emit;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Compiles generated sources off-project with the Roslyn csc bundled in the Unity editor. Synchronous.</summary>
    public sealed class RoslynProcessCompiler : ICSharpCompiler
    {
        private const int c_TimeoutMilliseconds = 10 * 60 * 1000;

        private readonly string m_OutputDirectory;

        /// <summary>By default sources and the assembly go to a temp folder that is deleted again. With an output directory that folder is used and kept, so the produced DLL can be loaded.</summary>
        public RoslynProcessCompiler(string outputDirectory = null)
        {
            m_OutputDirectory = outputDirectory;
        }

        private static readonly Regex s_Located = new Regex(@"^(?<file>.+)\((?<line>\d+),\d+\): (?<sev>error|warning) (?<code>CS\d+): (?<msg>.*)$", RegexOptions.Compiled);
        private static readonly Regex s_Unlocated = new Regex(@"^(?:.*?: )?(?<sev>error|warning) (?<code>CS\d+): (?<msg>.*)$", RegexOptions.Compiled);

        public IReadOnlyList<CompilerDiagnostic> Compile(IReadOnlyList<GeneratedFile> files, string assemblyName, IReadOnlyList<string> references)
        {
            var contents = EditorApplication.applicationContentsPath;
            var dotnet = Path.Combine(contents, "NetCoreRuntime", Application.platform == RuntimePlatform.WindowsEditor ? "dotnet.exe" : "dotnet");
            var csc = Path.Combine(contents, "DotNetSdkRoslyn", "csc.dll");
            if (File.Exists(dotnet) == false) throw new FileNotFoundException("Unity bundled dotnet not found: " + dotnet);
            if (File.Exists(csc) == false) throw new FileNotFoundException("Unity bundled Roslyn csc.dll not found: " + csc);

            var keep = string.IsNullOrEmpty(m_OutputDirectory) == false;
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var dir = (keep ? m_OutputDirectory : Path.Combine(projectRoot, "Temp", "UnityInternalsGenerator", Guid.NewGuid().ToString("N"))).Replace('\\', '/');
            try
            {
                Directory.CreateDirectory(dir);
                var rsp = new StringBuilder();
                rsp.Append("-target:library -nologo -noconfig -nostdlib+ -deterministic -langversion:9.0 -nowarn:0612,0618 -preferreduilang:en-US\n");
                rsp.Append("-out:\"").Append(dir).Append('/').Append(assemblyName).Append(".dll\"\n");
                foreach (var reference in references) rsp.Append("-r:\"").Append(reference.Replace('\\', '/')).Append("\"\n");

                var utf8 = new UTF8Encoding(false);
                foreach (var file in files)
                {
                    var path = dir + "/" + file.RelativePath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, file.Content, utf8);
                    rsp.Append('"').Append(path).Append("\"\n");
                }
                var rspPath = dir + "/args.rsp";
                File.WriteAllText(rspPath, rsp.ToString(), utf8);

                var output = Run(dotnet, "\"" + csc + "\" @\"" + rspPath + "\"", dir, out var exitCode, out var timedOut);
                if (timedOut) return new[] { new CompilerDiagnostic(null, 0, "CSC", "csc timed out", true) };
                return Parse(output, dir, exitCode);
            }
            finally
            {
                try
                {
                    if (keep == false && Directory.Exists(dir)) Directory.Delete(dir, true);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    // Temp folder cleanup is best effort.
                }
            }
        }

        private static IReadOnlyList<CompilerDiagnostic> Parse(string output, string rootDir, int exitCode)
        {
            var root = rootDir.Replace('\\', '/').TrimEnd('/') + "/";
            var result = new List<CompilerDiagnostic>();
            foreach (var raw in output.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var m = s_Located.Match(line);
                if (m.Success)
                {
                    var file = m.Groups["file"].Value.Replace('\\', '/');
                    if (file.StartsWith(root, StringComparison.OrdinalIgnoreCase)) file = file.Substring(root.Length);
                    result.Add(new CompilerDiagnostic(file, int.Parse(m.Groups["line"].Value, CultureInfo.InvariantCulture), m.Groups["code"].Value, m.Groups["msg"].Value, m.Groups["sev"].Value == "error"));
                    continue;
                }
                m = s_Unlocated.Match(line);
                if (m.Success) result.Add(new CompilerDiagnostic(null, 0, m.Groups["code"].Value, m.Groups["msg"].Value, m.Groups["sev"].Value == "error"));
            }

            if (exitCode != 0 && result.Exists(d => d.IsError) == false) result.Add(new CompilerDiagnostic(null, 0, "CSC", "csc exited with code " + exitCode.ToString(CultureInfo.InvariantCulture) + ": " + output.Trim(), true));
            return result;
        }

        private static void KillTree(Process process)
        {
            try
            {
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // netstandard 2.1 has no Kill(entireProcessTree): taskkill /T takes the child processes down too.
                    var killer = Process.Start(new ProcessStartInfo("taskkill", "/PID " + process.Id.ToString(CultureInfo.InvariantCulture) + " /T /F") { UseShellExecute = false, CreateNoWindow = true });
                    killer?.WaitForExit(10000);
                }
                process.Kill();
            }
            catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception || e is NotSupportedException)
            {
                // Process already exited or cannot be killed: nothing more to do.
            }
        }

        private static string Run(string fileName, string arguments, string workingDirectory, out int exitCode, out bool timedOut)
        {
            var info = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var text = new StringBuilder();
            var sync = new object();
            DataReceivedEventHandler append = (_, e) =>
            {
                if (e.Data == null) return;
                lock (sync) text.Append(e.Data).Append('\n');
            };

            using (var process = new Process { StartInfo = info })
            {
                process.OutputDataReceived += append;
                process.ErrorDataReceived += append;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (process.WaitForExit(c_TimeoutMilliseconds) == false)
                {
                    KillTree(process);
                    exitCode = -1;
                    timedOut = true;
                    return string.Empty;
                }
                process.WaitForExit(); // flush the async output readers
                exitCode = process.ExitCode;
            }
            timedOut = false;
            lock (sync) return text.ToString();
        }
    }
}
