# Unity Internals Generator — Step 1 (Skeleton) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deterministic reflection-based generator that emits forwarding wrappers for internal enums and static members of internal classes from `UnityEditor.CoreModule` + `UnityEngine.CoreModule` into a committed bridge assembly, pre-compiled off-project, with a full report of everything not wrapped.

**Architecture:** Pure pipeline `Discover → Classify (rules) → Generate (CodeWriter) → Pre-compile (Unity's bundled Roslyn csc, synchronous) → auto-exclude loop → Write (deterministic files + metas) → Report`. Generator is a normal editor asmdef using only reflection; output is a separate generated asmdef named with a free `Unity.InternalAPIEngineBridge.0NN` friend name.

**Tech Stack:** Unity 6000.0.59f2, C# 9, System.Reflection, Unity Test Framework (NUnit, EditMode), `<Editor>/Data/NetCoreRuntime/dotnet(.exe)` + `<Editor>/Data/DotNetSdkRoslyn/csc.dll`.

**Spec:** `C:/Users/Danko/AppData/Roaming/JetBrains/Rider2026.1/scratches/2026-10-20-unity-internals-generator-design.md` (read §3, §4, §8 step 1, §9).

## Global Constraints

- Generator folder: `Assets/DandaUtils/Editor/UnityInternalsGenerator/` (symlink — use `ls -R`/`grep -R`/absolute Read; Grep/Glob tools don't follow it). Namespace `DandaUtils.Editor.UnityInternalsGenerator` (+ sub-namespaces mirroring folders).
- Generator asmdef `DandaUtils.UnityInternalsGenerator.Editor`, editor-only, no references to output asmdef. No new packages.
- Output default folder `Assets/WrappedUnityInternals/`, **committed to git**; asmdef file `WrappedUnityInternals.asmdef`, editor-only, `autoReferenced: true`, asmdef `name` = chosen host.
- Wrapped namespace = `"Wrapped" + first segment` + rest (`UnityEditor.UIElements` → `WrappedUnityEditor.UIElements`); global namespace → `WrappedGlobal`.
- Default naming `Exact`; `PascalCase` available.
- Default included assemblies: `UnityEditor.CoreModule`, `UnityEngine.CoreModule`.
- Determinism: sort types by `FullName` ordinal, members by `MetadataToken`; `\n` line endings; `/` separators; `CultureInfo.InvariantCulture` everywhere; no timestamps; header only generator version + Unity version.
- Never rewrite an existing `.meta`; new metas get GUID = lowercase hex MD5 of `"DandaUtils.UnityInternalsGenerator:" + <path relative to project root, '/'-separated>`.
- Broken generated code never reaches `Assets/` — write only after clean pre-compile.
- Conventions: `m_` private fields, `== false` not `!`, new classes `sealed` where possible, PascalCase, using-directives inside namespace.
- Git: **no commits** — user commits. "Commit" steps below = stop and leave working tree.
- Verification order per memory: Rider `get_file_problems` first, then `get_unity_compilation_result`; tests via `mcp__unityMCP__run_tests` (projectPath `D:\Projects\s1-new`, EditMode, assembly `DandaUtils.UnityInternalsGenerator.Tests`) + poll job.

## Review Focus

1. **Non-invariant culture** (user machine is sk-SK: decimal comma) — float/double/decimal defaults and enum values must format invariantly. Test in Task 1 under `sk-SK`.
2. **Persisted host name later collides** (package added an asmdef with same name) → generator must pick a new free name and log warning, not emit duplicate asmdef. Test in Task 2.
3. **User files in output folder** — only generator-owned files (`*.g.cs`, its metas, `WrappedUnityInternals.asmdef`, `GenerationInfo.json`, `_GenerationReport.md/.json`) may be deleted/overwritten; anything else untouched. Test in Task 7.
4. **Pre-compile never clean within max passes** → nothing written, previous output intact, report lists compile errors. Test in Task 6.
5. **csc reports absolute/temp paths** in diagnostics → mapping must resolve via relative path suffix. Test in Task 6.

---

## File Structure

```
Assets/DandaUtils/Editor/UnityInternalsGenerator/
  DandaUtils.UnityInternalsGenerator.Editor.asmdef
  GeneratorInfo.cs                    const Version = 1
  Naming/NamespaceMapper.cs           Unity ns → Wrapped ns
  Naming/INamingStrategy.cs           + ExactNaming.cs, PascalCaseNaming.cs, NamingStyle.cs (enum)
  Naming/CSharpNames.cs               type → C# source text, identifier escaping
  Naming/DefaultValueFormatter.cs     ParameterInfo default → literal
  Model/MemberId.cs                   stable ids
  Model/WrapStatus.cs                 enum
  Model/ReportEntry.cs                record-like sealed class
  Model/GenerationInput.cs            inputs
  Model/GenerationContext.cs          accessibility, type map, report sink
  Discovery/BridgeAccess.cs           IVT read + host choice
  Discovery/UsedAssemblyNames.cs      scan project asmdef names
  Rules/IConversionRule.cs  Rules/Conversion.cs  Rules/ConversionRules.cs  (Passthrough, EnumCast, Unsupported)
  Rules/ITypeRule.cs        Rules/TypeRules.cs   (EnumTypeRule, StaticClassTypeRule)
  Rules/IMemberRule.cs      Rules/MemberRules.cs (Method, Property, Field, Const)
  Emit/CodeWriter.cs   Emit/GeneratedFile.cs
  Pipeline/WrapperGenerator.cs        pure: input → files + report
  Pipeline/ICSharpCompiler.cs  Pipeline/RoslynProcessCompiler.cs  Pipeline/CompilerDiagnostic.cs  Pipeline/CompilerReferences.cs
  Pipeline/GenerationRunner.cs        generate/compile/auto-exclude loop
  Output/OutputWriter.cs  Output/MetaFiles.cs  Output/ReportWriter.cs
  Settings/UnityInternalsGeneratorSettings.cs  Settings/UnityInternalsGeneratorSettingsProvider.cs
  GeneratorMenu.cs                    menu + version check
Assets/DandaUtils/Editor/UnityInternalsGenerator/Tests/
  DandaUtils.UnityInternalsGenerator.Tests.asmdef     (refs generator + TestRunner, nunit, UNITY_INCLUDE_TESTS)
  *Tests.cs
  Fixture/DandaUtils.UnityInternalsGenerator.Tests.Fixture.asmdef  (editor, UNITY_INCLUDE_TESTS, autoReferenced false)
  Fixture/FixtureTypes.cs  Fixture/AssemblyInfo.cs
```

### Fixture (used by Tasks 4–6)

`AssemblyInfo.cs`: `[assembly: InternalsVisibleTo("Wrapped.Fixture.Host")]`. `FixtureTypes.cs`, namespace `UnityInternalsFixture`:

```csharp
public static class FixtureMarker { }
internal enum Color8 : byte { Red = 1, Green = 2 }
[System.Flags] internal enum Mask { None = 0, A = 1, B = 2 }
internal static class StaticApi
{
    internal const int Answer = 42;
    internal const string Greeting = "hi \"there\"";
    internal static int Counter;
    internal static readonly string Name = "fixture";
    internal static Color8 Current { get; set; } = Color8.Red;
    internal static int Add(int a, int b = 2) => a + b;
    internal static double Scale(double v, double factor = 1.5) => v * factor;
    internal static Color8 Next(Color8 c, Mask m = Mask.A | Mask.B) => c == Color8.Red ? Color8.Green : Color8.Red;
    internal static void Reset() => Counter = 0;
    internal static void WithRef(ref int x) { }        // Deferred (ref/out, step 3)
    internal static T Echo<T>(T x) => x;                // Deferred (generic method, step 3)
    internal static Holder MakeHolder() => new Holder(); // Deferred (wrapper conversion, step 2)
    [System.Obsolete("gone", true)] internal static void Dead() { } // Excluded ObsoleteError
    private static void Hidden() { }                    // NotAccessible
    internal enum Nested { X, Y }
}
internal class Holder { internal static int StaticValue => 7; internal int InstanceValue => 1; } // static wrapped, instance Deferred
internal struct Point { }               // Deferred (step 2)
internal interface IThing { }           // Deferred (step 2)
internal delegate void Callback();      // Deferred (step 3)
internal class Generic<T> { }           // Unsupported GenericType
internal static class Collide { internal static int value; internal static int Value; } // PascalCase → NameCollision
```

---

### Task 1: Assemblies, naming, ids

**Files:** create generator asmdef, tests asmdef, fixture asmdef + fixture files (above), `GeneratorInfo.cs`, `Naming/*`, `Model/MemberId.cs`; tests `Tests/NamingTests.cs`, `Tests/MemberIdTests.cs`.

**Interfaces — Produces:**
- `static class NamespaceMapper { string Map(string unityNamespace); }`
- `enum NamingStyle { Exact, PascalCase }`; `interface INamingStrategy { string MemberName(string original); }`; `ExactNaming`, `PascalCaseNaming` (upper-case first char only); `static INamingStrategy NamingStrategies.For(NamingStyle)`.
- `static class CSharpNames { string TypeRef(Type t, Func<Type,string> overrideMap = null); string Identifier(string name); string TypeDeclName(Type t); }` — `TypeRef`: keyword aliases for primitives (`int`, `string`, `void`, `object`, `bool`, `byte`, …), else `global::Ns.Outer.Inner`, generics `global::System.Collections.Generic.List<int>`, arrays `int[]`/`int[,]`; `overrideMap` lets callers substitute wrapped names. `Identifier` escapes C# keywords with `@`. `TypeDeclName` strips generic arity backtick.
- `static class DefaultValueFormatter { bool TryFormat(ParameterInfo p, string typeRef, out string literal); }` — invariant culture; `null`, `true/false`, `1.5f`, `1.5d`, `10L`, `10u`, `10UL`, `1.5m`, `'\u0041'`, escaped strings, `float.NaN`/`double.PositiveInfinity` etc., enums as `(typeRef)value` with underlying numeric value, value-type null → `default`.
- `static class MemberId { string Type(Type t); string Member(MemberInfo m); }` — format `Assembly|Namespace.Outer+Inner|Kind|Name(ParamFullName,ParamFullName)`; Kind ∈ `Type, Method, Property, Field, Const, Event, Ctor`; params only for methods/ctors.

- [ ] **Step 1: Write failing tests**
  - `NamespaceMapper.Map("UnityEditor") == "WrappedUnityEditor"`, `Map("UnityEditor.UIElements") == "WrappedUnityEditor.UIElements"`, `Map(null) == "WrappedGlobal"`, `Map("Unity.Foo") == "WrappedUnity.Foo"`.
  - `PascalCaseNaming.MemberName("selectedSizeIndex") == "SelectedSizeIndex"`; `ExactNaming` identity.
  - `CSharpNames.TypeRef(typeof(int)) == "int"`, `TypeRef(typeof(List<string>)) == "global::System.Collections.Generic.List<string>"`, `TypeRef(typeof(int[,])) == "int[,]"`, nested `TypeRef(typeof(Environment.SpecialFolder)) == "global::System.Environment.SpecialFolder"`, `Identifier("object") == "@object"`.
  - `DefaultValueFormatter` via fixture methods `StaticApi.Add` (b → `2`), `StaticApi.Scale` (factor → `1.5d`) — **run both under `CultureInfo.CurrentCulture = new CultureInfo("sk-SK")`** — `StaticApi.Next` (m → `(X)3` with passed typeRef `X`). Get fixture `MethodInfo`s via `typeof(FixtureMarker).Assembly.GetType("UnityInternalsFixture.StaticApi")` + `BindingFlags.NonPublic|Static`.
  - `MemberId.Member(Add)` == `"DandaUtils.UnityInternalsGenerator.Tests.Fixture|UnityInternalsFixture.StaticApi|Method|Add(System.Int32,System.Int32)"`; nested enum type id contains `StaticApi+Nested`.
- [ ] **Step 2: Run** tests → FAIL (types missing / compile error).
- [ ] **Step 3: Implement** the Produces list.
- [ ] **Step 4: Verify** Rider problems clean, Unity compile clean, tests PASS.
- [ ] **Step 5: Stop for review** (no commit).

### Task 2: Bridge discovery

**Files:** `Discovery/BridgeAccess.cs`, `Discovery/UsedAssemblyNames.cs`; test `Tests/BridgeAccessTests.cs`.

**Interfaces — Produces:**
- `static IReadOnlyCollection<string> BridgeAccess.GetGrantedFriends(Assembly a)` — `InternalsVisibleToAttribute.AssemblyName`, strip `, PublicKey=…`.
- `static string BridgeAccess.ChooseHostName(IReadOnlyList<IReadOnlyCollection<string>> grantsPerSource, ISet<string> usedNames, string persisted, out string warning)` — candidates = names matching `^Unity\.InternalAPI(Editor|Engine)Bridge(Dev)?\.\d{3}$`, not in `usedNames`. Keep `persisted` if it is a candidate and granted by the max count. Else order by: number of sources granting (desc), non-Dev before Dev, name ordinal. Persisted dropped → `warning` set (else null). No candidate → `null`.
- `static bool BridgeAccess.Grants(Assembly a, string hostName)`.
- `static ISet<string> UsedAssemblyNames.Collect(string projectRoot, string ownOutputAsmdefPath)` — `"name"` of every `*.asmdef` under `Assets`, `Packages`, `Library/PackageCache` (follow symlinks; use `Directory.EnumerateFiles(..., SearchOption.AllDirectories)`), plus `CompilationPipeline.GetAssemblies()` names; excludes the generator's own output asmdef.

- [ ] **Step 1: Failing tests**
  - Synthetic: grants `[{Engine.001..024, Editor.001..024}, {Engine.001..024}]`, used `{Editor.001, Engine.001, Engine.020}`, persisted null → `"Unity.InternalAPIEngineBridge.002"`.
  - Persisted `Engine.005` still free → `Engine.005`, warning null.
  - Persisted `Engine.005` now in `usedNames` → `Engine.002`, warning non-null (Review Focus 2).
  - Only Dev names granted → picks Dev `.001`.
  - Real: `GetGrantedFriends(typeof(UnityEditor.Editor).Assembly)` contains `"Unity.InternalAPIEngineBridge.001"`; `GetGrantedFriends(typeof(FixtureMarker).Assembly)` contains `"Wrapped.Fixture.Host"`.
  - Real: `UsedAssemblyNames.Collect(projectRoot, null)` contains `"Unity.InternalAPIEngineBridge.020"` (existing DandaUtils asmdef).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Verify** (problems, compile, tests PASS). **Step 5: Stop.**

### Task 3: CodeWriter

**Files:** `Emit/CodeWriter.cs`, `Emit/GeneratedFile.cs`; test `Tests/CodeWriterTests.cs`.

**Interfaces — Produces:**
- `sealed class CodeWriter { void Line(string text = ""); IDisposable Block(string header); /* writes header, "{", indents 4 spaces, "}" */ IDisposable Member(string memberId); /* lines written inside are attributed to id; nestable, innermost wins */ GeneratedFile ToFile(string relativePath); }`
- `sealed class GeneratedFile { string RelativePath; string Content; string MemberAt(int line1Based); /* innermost member id or the file's type id, null outside */ }` — `Content` uses `\n`, ends with single `\n`.

- [ ] **Step 1: Failing tests** — block indentation exact text; `MemberAt` returns inner id for line inside nested `Member` scopes, outer for lines between, null for header lines.
- [ ] **Step 2–4:** run FAIL → implement → PASS + compile clean. **Step 5: Stop.**

### Task 4: Model, conversions, classification

**Files:** `Model/WrapStatus.cs`, `Model/ReportEntry.cs`, `Model/GenerationInput.cs`, `Model/GenerationContext.cs`, `Rules/IConversionRule.cs`, `Rules/Conversion.cs`, `Rules/ConversionRules.cs`, `Rules/ITypeRule.cs`, `Rules/TypeRules.cs` (classification part); test `Tests/ClassificationTests.cs`.

**Interfaces — Produces:**
- `enum WrapStatus { Wrapped, Deferred, Unsupported, Excluded, NotAccessible, NoBridgeAccess, NameCollision, CompileFailed }`
- `sealed class ReportEntry { string Id; string Rule; WrapStatus Status; string Reason; }` (ctor with all four).
- `sealed class GenerationInput { IReadOnlyList<Assembly> Sources; string HostAssemblyName; NamingStyle Naming; IReadOnlyCollection<string> ExcludedIds; IReadOnlyCollection<string> ExcludedNamespaces; string UnityVersion; }`
- `sealed class GenerationContext` — ctor `(GenerationInput)`; `bool IsInternalCandidate(Type t)` (not `IsVisible`; top-level `NotPublic` or nested `IsNestedAssembly`/`IsNestedFamORAssem` with whole declaring chain public-or-internal; source assembly `Grants` host); `bool IsAccessible(MethodBase m)` (`IsPublic || IsAssembly || IsFamilyOrAssembly`); `bool IsWrappedType(Type t)` (type rule result `Wrapped`); `Conversion Convert(Type t)`; `void Report(ReportEntry e)`; `IReadOnlyList<ReportEntry> Entries` (sorted by Id on read); `INamingStrategy Naming`.
- `sealed class Conversion { WrapStatus Status; string Reason; string WrappedTypeRef; string ToWrapped(string expr); string ToUnity(string expr); }`
- `interface IConversionRule { string Name; bool Match(Type t, GenerationContext ctx); Conversion Create(Type t, GenerationContext ctx); }` — order: `UnsupportedConversionRule` (pointer, `IsByRefLike` via attribute name `System.Runtime.CompilerServices.IsByRefLikeAttribute`, `TypedReference`) → `ByRefConversionRule` (`IsByRef` → Deferred "ref/out (step 3)") → `PassthroughConversionRule` (`IsVisible`, `void`) → `EnumCastConversionRule` (`IsEnum` and `IsWrappedType`) → fallback Deferred "WrapperConversion (step 2)" for internal class/struct/interface/delegate, Unsupported otherwise. Enum cast: `ToWrapped(e) = "(" + wrappedRef + ")" + e`.
- `interface ITypeGenerator { void Emit(Type t, CodeWriter w, GenerationContext ctx); }` (defined here, implemented in Task 5; Task 4 rules may return a stub).
- `interface ITypeRule { string Name; bool Match(Type t, GenerationContext ctx); ITypeGenerator Generator; }` — classification: compiler-generated (`<` in name or `[CompilerGenerated]`) → Excluded; `ExcludedIds`/`ExcludedNamespaces` → Excluded; `IsGenericTypeDefinition` → Unsupported "GenericType"; enum → `EnumTypeRule`; class non-delegate → `StaticClassTypeRule`; struct/interface/delegate → Deferred with step in reason; internal type nested in **public** type → Deferred "PublicTypeInternalMembers (step 4)"; private/protected nested → NotAccessible. Source assembly not granting host → every internal type NoBridgeAccess (one entry per type).

- [ ] **Step 1: Failing tests** — build context over fixture assembly with host `"Wrapped.Fixture.Host"`; assert type statuses: `Color8`, `Mask`, `StaticApi`, `StaticApi+Nested`, `Holder` → Wrapped; `Point`, `IThing`, `Callback` → Deferred; ``Generic`1`` → Unsupported; host `"Other.Host"` → `StaticApi` NoBridgeAccess. Conversions: `int` passthrough `"int"`; `Color8` → `"global::WrappedUnityInternalsFixture.Color8"`; `Holder` Deferred; `int&` Deferred; `int*` Unsupported.
- [ ] **Step 2–4:** FAIL → implement → PASS + compile clean. **Step 5: Stop.**

### Task 5: Emission (types + members) and `WrapperGenerator`

**Files:** `Rules/TypeRules.cs` (generators), `Rules/IMemberRule.cs`, `Rules/MemberRules.cs`, `Pipeline/WrapperGenerator.cs`; test `Tests/EmissionTests.cs`.

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces: `ITypeGenerator` implementations for Enum/StaticClass rules; `interface IMemberRule { string Name; bool Match(MemberInfo m, GenerationContext ctx); void Emit(MemberInfo m, CodeWriter w, GenerationContext ctx); }`; `sealed class GenerationResult { IReadOnlyList<GeneratedFile> Files; IReadOnlyList<ReportEntry> Entries; }`; `static GenerationResult WrapperGenerator.Generate(GenerationInput input)`.

Exact output shapes (tests assert these):

```csharp
// <auto-generated>
// Generated by DandaUtils.UnityInternalsGenerator v1 for Unity <UnityVersion>. Do not edit.
// </auto-generated>
#pragma warning disable 0612, 0618
namespace WrappedUnityInternalsFixture
{
    [global::System.Flags]
    public enum Mask : int
    {
        None = 0,
        A = 1,
        B = 2,
    }
}
```

```csharp
    public static class StaticApi
    {
        public const int Answer = 42;
        public static int Counter { get => global::UnityInternalsFixture.StaticApi.Counter; set => global::UnityInternalsFixture.StaticApi.Counter = value; }
        public static string Name => global::UnityInternalsFixture.StaticApi.Name;
        public static global::WrappedUnityInternalsFixture.Color8 Current { get => (global::WrappedUnityInternalsFixture.Color8)global::UnityInternalsFixture.StaticApi.Current; set => global::UnityInternalsFixture.StaticApi.Current = (global::UnityInternalsFixture.Color8)value; }
        public static int Add(int a, int b = 2) => global::UnityInternalsFixture.StaticApi.Add(a, b);
        public static void Reset() => global::UnityInternalsFixture.StaticApi.Reset();

        public enum Nested : int
        {
            X = 0,
            Y = 1,
        }
    }
```
Non-static internal class → `public class Holder` with `private Holder() { }` as first member, then static members only; instance members → Deferred "InstanceMembers (step 2)".

Member rules (declared-only, `BindingFlags.Static|Public|NonPublic|DeclaredOnly`; instance members of wrapped classes → Deferred):
- Skip silently-but-reported: `IsSpecialName` methods (accessors handled by properties; `op_*` → Deferred "Operator (step 3)"), `.cctor`, events → Deferred "Event (step 3)".
- Generic methods → Deferred "GenericMethod (step 3)"; `CallingConventions.VarArgs` → Unsupported.
- `[Obsolete(..., true)]` → Excluded "ObsoleteError"; non-error obsolete copied as `[global::System.Obsolete("msg")]`.
- Any param/return conversion not Wrapped → member gets that conversion's status + reason.
- `params` → keep `params`; `this` (extension) dropped.
- Default not formattable → emit without default, entry Wrapped with Reason "default dropped".
- Field: `const` (`IsLiteral`) → `public const` with formatted value (enum consts as cast); `IsInitOnly` → get-only expression property; else get/set property.
- Property: per-accessor accessibility; indexers → Deferred "Indexer (step 2)".
- Names via `ctx.Naming.MemberName` + `CSharpNames.Identifier`; duplicate final name+param-signature within a type, or name equal to enclosing type name → NameCollision (all colliding members).
- Each type/member written inside `w.Member(id)`. One file per top-level wrapped type at `<WrappedNs as path, '.'→'/'>/<TypeDeclName>.g.cs`; nested wrapped types emitted inside parent.

- [ ] **Step 1: Failing tests**
  - `Generate` over fixture (host `Wrapped.Fixture.Host`, UnityVersion `"test"`): file `WrappedUnityInternalsFixture/Mask.g.cs` content equals the enum block above exactly; `StaticApi.g.cs` contains each member line above verbatim; `Holder.g.cs` contains `private Holder() { }` and `public static int StaticValue => global::UnityInternalsFixture.Holder.StaticValue;`.
  - Entries: `WithRef` Deferred, `Echo` Deferred, `MakeHolder` Deferred, `Dead` Excluded, `Hidden` NotAccessible, `Holder.InstanceValue` Deferred.
  - PascalCase naming: `Collide.value` + `Collide.Value` both NameCollision; Exact naming: both Wrapped.
  - Determinism: two `Generate` calls → same file paths and byte-identical contents.
- [ ] **Step 2–4:** FAIL → implement → PASS + compile clean. **Step 5: Stop.**

### Task 6: Pre-compiler and generation runner

**Files:** `Pipeline/ICSharpCompiler.cs`, `Pipeline/CompilerDiagnostic.cs`, `Pipeline/RoslynProcessCompiler.cs`, `Pipeline/CompilerReferences.cs`, `Pipeline/GenerationRunner.cs`; test `Tests/CompileTests.cs`.

**Interfaces:**
- Produces:
  - `sealed class CompilerDiagnostic { string File; int Line; string Code; string Message; bool IsError; }`
  - `interface ICSharpCompiler { IReadOnlyList<CompilerDiagnostic> Compile(IReadOnlyList<GeneratedFile> files, string assemblyName, IReadOnlyList<string> references); }`
  - `RoslynProcessCompiler : ICSharpCompiler` — writes files to a fresh `Temp/UnityInternalsGenerator/<guid>/` dir (project `Temp/`), runs `<EditorApplication.applicationContentsPath>/NetCoreRuntime/dotnet[.exe] <…>/DotNetSdkRoslyn/csc.dll @args.rsp` synchronously; rsp: `-target:library -nologo -noconfig -nostdlib+ -deterministic -langversion:9.0 -nowarn:0612,0618 -out:<dir>/<assemblyName>.dll`, one `-r:"path"` per reference, each source path. Parses stdout lines with `^(?<file>.+)\((?<line>\d+),\d+\): (?<sev>error|warning) (?<code>CS\d+): (?<msg>.*)$`; `File` normalized to path relative to the temp dir with `/`. Deletes temp dir afterwards.
  - `static IReadOnlyList<string> CompilerReferences.Collect(IReadOnlyList<Assembly> sources)` — `compiledAssemblyReferences` of `CompilationPipeline.GetAssemblies(AssembliesType.Editor)` entry named `DandaUtils.UnityInternalsGenerator.Editor`, plus each `source.Location`, distinct, sorted ordinal.
  - `sealed class RunResult { bool Success; IReadOnlyList<GeneratedFile> Files; IReadOnlyList<ReportEntry> Entries; IReadOnlyList<CompilerDiagnostic> RemainingErrors; int Passes; }`
  - `static RunResult GenerationRunner.Run(GenerationInput input, ICSharpCompiler compiler, IReadOnlyList<string> references, int maxPasses)` — loop: `Generate` → `Compile` → for each error, `file.MemberAt(line)` (file matched by relative-path suffix; Review Focus 5) → id; unmapped → that file's top-level type id; add ids to local auto-exclude set (merged into `ExcludedIds` for next pass); entries for them become `CompileFailed` with `Code: Message`. Stop when no errors (`Success`) or `maxPasses` reached (`Success=false`, `RemainingErrors` set). Auto-excludes are not persisted — recomputed every run.

- [ ] **Step 1: Failing tests**
  - Real compile: `RoslynProcessCompiler.Compile(Generate(fixture).Files, "Wrapped.Fixture.Host", CompilerReferences.Collect(fixtureSources))` → zero errors.
  - Broken file `class X { int }` → one error, `Line == 1`, `Code` starts with `CS`, `File` is the relative path.
  - Fake compiler returning error on the line of `StaticApi.Add` on pass 1, none on pass 2 → `Success`, `Passes == 2`, `Add` entry `CompileFailed`, `StaticApi.g.cs` no longer contains `Add(`.
  - Fake compiler always erroring, `maxPasses 3` → `Success == false`, `Passes == 3`, `RemainingErrors` non-empty (Review Focus 4).
  - Fake diagnostic with absolute path `C:/x/Temp/UnityInternalsGenerator/abc/WrappedUnityInternalsFixture/StaticApi.g.cs` still maps (Review Focus 5).
- [ ] **Step 2–4:** FAIL → implement → PASS + compile clean. **Step 5: Stop.**

### Task 7: Output and report writing

**Files:** `Output/OutputWriter.cs`, `Output/MetaFiles.cs`, `Output/ReportWriter.cs`; test `Tests/OutputTests.cs`.

**Interfaces — Produces:**
- `static string MetaFiles.Guid(string projectRelativePath)`; `static string MetaFiles.ForScript(guid)`, `ForFolder(guid)`, `ForAsmdef(guid)`, `ForText(guid)` — copy importer blocks verbatim from existing project metas of each kind (`.cs`, folder, `.asmdef`, `.md`/`.json`).
- `static void OutputWriter.Write(string projectRoot, string outputFolder, string hostAssemblyName, IReadOnlyList<GeneratedFile> files, string generationInfoJson, string reportMd, string reportJson)` — writes `WrappedUnityInternals.asmdef` (`name` = host, `includePlatforms: ["Editor"]`, `autoReferenced: true`, rest Unity defaults), each `.g.cs`, `GenerationInfo.json` (`{"generatorVersion":1,"unityVersion":"…","hostAssembly":"…"}`), `_GenerationReport.md`, `_GenerationReport.json`; only writes when content differs; creates missing metas (files and folders) with deterministic GUID; never rewrites existing metas; deletes generator-owned files no longer produced (`*.g.cs` + metas, emptied folders + metas); leaves any other file untouched.
- `static (string md, string json) ReportWriter.Build(IReadOnlyList<ReportEntry> entries, string unityVersion, string host)` — md: title, Unity version, host, table `Status | Count` (all statuses, fixed enum order), then section per non-Wrapped status listing `` - `id` — rule — reason `` sorted by id; json: `{"unityVersion","host","entries":[{"id","rule","status","reason"}]}` sorted by id, invariant, `\n`.

- [ ] **Step 1: Failing tests** (temp dir under project `Temp/`)
  - Write twice same input → second call modifies no file timestamps; meta GUID equals `MetaFiles.Guid(relPath)`.
  - Pre-existing meta with other GUID → kept unchanged.
  - Stale `Old.g.cs` + meta removed; `Notes.txt` user file kept (Review Focus 3).
  - Report md contains `| Deferred |` row with correct count; json entries sorted.
- [ ] **Step 2–4:** FAIL → implement → PASS + compile clean. **Step 5: Stop.**

### Task 8: Settings, menu, end-to-end run

**Files:** `Settings/UnityInternalsGeneratorSettings.cs`, `Settings/UnityInternalsGeneratorSettingsProvider.cs`, `GeneratorMenu.cs`.

**Interfaces — Produces:**
- `[FilePath("ProjectSettings/UnityInternalsGeneratorSettings.asset", FilePathAttribute.Location.ProjectFolder)] sealed class UnityInternalsGeneratorSettings : ScriptableSingleton<…>` — `m_OutputFolder = "Assets/WrappedUnityInternals"`, `m_HostAssemblyName = ""`, `m_NamingStyle = Exact`, `m_IncludedAssemblies = {"UnityEditor.CoreModule","UnityEngine.CoreModule"}`, `m_ExcludedNamespaces`, `m_ExcludedIds`, `m_MaxCompilePasses = 5`; public accessors; `Save()` → `Save(true)`.
- SettingsProvider at `Project/DandaUtils/Unity Internals Generator` drawing settings via `SerializedObject`, with "Generate" button.
- Menu `Tools/DandaUtils/Unity Internals/Generate`, `…/Open Report`, `…/Settings`. Generate: resolve included assemblies from `AppDomain` by name (missing → console error), `UsedAssemblyNames.Collect`, `ChooseHostName` (persist; log warning), sources not granting host get NoBridgeAccess entries, `GenerationRunner.Run`; on success `OutputWriter.Write` + `AssetDatabase.Refresh()`; on failure write nothing to output, log errors + report path in `Temp/UnityInternalsGenerator/_LastFailedReport.md`. Console summary: counts per status.
- `[InitializeOnLoad]` check: if `<Output>/GenerationInfo.json` exists and `unityVersion != Application.unityVersion` → `Debug.LogWarning` suggesting regenerate.

- [ ] **Step 1: Implement** settings, provider, menu.
- [ ] **Step 2: Verify compile** (problems → Unity compile clean).
- [ ] **Step 3: End-to-end** — run menu via `mcp__unityMCP__execute_menu_item` `Tools/DandaUtils/Unity Internals/Generate`. Expected: `Assets/WrappedUnityInternals/` created, asmdef name `Unity.InternalAPIEngineBridge.002` (or next free), Unity compile clean, console summary with non-zero Wrapped.
- [ ] **Step 4: Spot-check** via `mcp__unityMCP__execute_code`: `WrappedUnityEditor.Toolbar.RepaintToolbar()` runs without exception; `(int)WrappedUnityEditor.GameViewSizeType.FixedResolution == 1`. If a name differs in this Unity version, pick another internal static from the report's Wrapped entries.
- [ ] **Step 5: Determinism** — run Generate again; `git status --porcelain Assets/WrappedUnityInternals ProjectSettings/UnityInternalsGeneratorSettings.asset` shows no changes beyond first run's new files (compare `git diff --stat` before/after second run: identical).
- [ ] **Step 6: Report** — open `_GenerationReport.md`, confirm counts plausible (thousands of Deferred instance members, NoBridgeAccess absent for the two CoreModules). **Stop** for user review.
