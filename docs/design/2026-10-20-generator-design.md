# Unity Internals Wrapper Generator — Design

Date: 2026-10-20 · Project: s1-new (Unity 6000.0.59f2) · Status: design approved in chat, step 1 to plan

## 1. Intent

Hand-written `Assets/DandaUtils/Editor/UnityInternalsWrapper` exposes a few Unity internal APIs (Menu, Toolbar, HandleUtility, GameView, GameViewSize) by compiling inside a bridge assembly (`.asmref` into `UnityEditor.UI`, which Unity grants `InternalsVisibleTo`). Every new internal needs hand work, and it breaks per Unity version.

Goal: deterministic **code generator** (no LLM) that reflects over the Unity assemblies of the *running* editor and emits wrappers for **all** internal API, mirroring Unity's namespaces and signatures, so any internal can be called from normal code. Re-run on Unity upgrade → wrappers match that version. Everything it could not wrap is reported.

## 2. Goals / non-goals / anti-goals

Goals
- Mirror namespace: `UnityEditor.GameViewSize` → `WrappedUnityEditor.GameViewSize`; `UnityEngine.X` → `WrappedUnityEngine.X`; rule = prefix `Wrapped` to first namespace segment (`UnityEditor.UIElements.Foo` → `WrappedUnityEditor.UIElements.Foo`, `Unity.Foo.Bar` → `WrappedUnity.Foo.Bar`).
- Identical member names and signatures by default. Naming is a setting (`Exact` default, `PascalCase`; extensible) — change + regenerate.
- Works for any Unity version: everything discovered by reflection at generation time, nothing version-hardcoded.
- Detection: every type/member not wrapped appears in report with reason.
- Rule-based: ordered rule cases, each with its own generator.
- Built in steps, verified between steps (§8).

Non-goals (now)
- Private members via reflection (future step 7; design must leave room).
- Replacing/deleting the existing hand-written wrapper. It stays untouched; new work lives in different folder, namespace, assembly.
- Runtime/player use. Editor-only.

Anti-goals (failure even if it "works")
- Generator depending on its own output (broken output must never stop the generator compiling).
- Hand-maintained per-type lists or version `#if`s.
- Silent skips — anything dropped without a report line.
- LLM/AI in the loop.

## 3. Key findings (verified 2026-10-20 on 6000.0.59f2)

- `InternalsVisibleTo` lives in DLL metadata (Unity source: `Editor/Mono/AssemblyInfo/AssemblyInfo.cs` etc.). Read via `assembly.GetCustomAttributes<InternalsVisibleToAttribute>()`.
- `UnityEditor.CoreModule.dll` grants `Unity.InternalAPIEditorBridge.001–024`, `Unity.InternalAPIEditorBridgeDev.001–005`, `Unity.InternalAPIEngineBridge.001–024`, `Unity.InternalAPIEngineBridgeDev.001–005`.
- `UnityEngine.CoreModule.dll` grants only `Unity.InternalAPIEngineBridge.*`.
- Across `Editor/Data/Managed/UnityEngine/*.dll` (123 files): 104 grant `Unity.InternalAPIEngineBridge.0NN` (38 of them also EditorBridge). 19 grant none (e.g. `UnityEditor.UIElementsModule`, `UnityEditor.PropertiesModule`, `UnityEngine.HierarchyCoreModule`, facades `UnityEditor.dll`/`UnityEngine.dll`, Cecil) → their internals unwrappable via bridge → reported (future: reflection step).
- ⇒ A single editor-only asmdef named `Unity.InternalAPIEngineBridge.0NN` sees internals of both UnityEditor.CoreModule and UnityEngine.* modules. Existing `Assets/DandaUtils/Editor/UnityEngineInternalsWrapper` already uses `EngineBridge.020`.
- Bridge names already used in project (asmdefs in Assets/Packages/Library/PackageCache): `EditorBridge.001`, `EngineBridge.001`, `EngineBridge.020`. Two asmdefs with same name = compile error → generator must pick unused number.

## 4. Architecture

Two assemblies:

| Assembly | Location | Role |
|---|---|---|
| `DandaUtils.UnityInternalsGenerator.Editor` (normal editor asmdef) | `Assets/DandaUtils/Editor/UnityInternalsGenerator/` | the generator. Pure reflection; needs no internals access. |
| `Unity.InternalAPIEngineBridge.0NN` (generated asmdef, editor-only, autoReferenced) | output folder (setting) | generated wrappers. Root namespaces `WrappedUnityEditor`, `WrappedUnityEngine`, … |

Output folder default: `Assets/WrappedUnityInternals/` — **outside** `Assets/DandaUtils`, because DandaUtils is a symlink shared across projects that may run different Unity versions; generated code is version-specific and belongs to the project.

### Pipeline

```
Settings → Discover → Classify (rules) → Generate → Write → Compile-verify → Report
```

1. **Discover** — iterate `AppDomain.CurrentDomain.GetAssemblies()` filtered by settings (default: Unity-shipped assemblies from `EditorApplication.applicationContentsPath/Managed`). Read IVT per assembly. Choose host name: bridge-name family granted by most assemblies (today `Unity.InternalAPIEngineBridge`), lowest number not used by any asmdef in project (scan `Assets`, `Packages`, `Library/PackageCache`, plus `CompilationPipeline.GetAssemblies()`); keep the number stable across regenerations (stored in settings) unless it collides. Assemblies not granting the chosen name → all their internals reported `NoBridgeAccess`.
2. **Model** — build a `TypeMap`: for each Unity `Type` encountered, how it is represented on the wrapped side (Passthrough public type / generated wrapper / mirror enum / unsupported). Built lazily, memoized, so member rules can ask about any type.
3. **Classify** — every candidate type and member runs through ordered rules; first `Match` wins. No match → `Unsupported(reason)`. Matching rule not yet implemented in current step → `Deferred(ruleName)` (separate report category so we see what next steps unlock).
4. **Generate** — rule's generator writes code via a plain indented `CodeWriter` (StringBuilder; no Roslyn dependency). One file per top-level wrapped type: `<Output>/<RootNs>/<Sub/Ns>/<TypeName>.g.cs`. Header comment: generator version, Unity version, "do not edit".
5. **Write** — write to temp, then replace output folder contents (delete stale `.g.cs`), keep `.meta` GUIDs stable for files that still exist. Write `.asmdef`.
6. **Compile-verify** — `AssetDatabase.Refresh`; hook `CompilationPipeline.assemblyCompilationFinished` for host assembly; map `CompilerMessage` file:line → member id using a line map recorded by `CodeWriter` (or `// @id` markers). Step 1–4: report compile errors only. Step 5: auto-exclude failing members, regenerate, bounded passes (e.g. 5); persist auto-exclusions across domain reload (SessionState/settings).
7. **Report** — `<Output>/_GenerationReport.md` (and JSON for tooling) + console summary: counts per category, and per type/member: id, rule, status (`Wrapped | Deferred | Unsupported | Excluded | NoBridgeAccess | NameCollision | CompileFailed`), reason.

### Three rule families

Rules are small classes; order + enable flags in settings.

- **Type rules** — `ITypeRule { bool Match(Type, Ctx); ITypeGenerator Generator; }`
  StaticClass ✓, Enum ✓, InstanceClass, Struct, Delegate, Interface, PublicTypeInternalMembers, Nested, Inheritable (proxy), Generic → Unsupported.
- **Member rules** — `IMemberRule { bool Match(MemberInfo, Ctx); void Emit(...); }`
  Method ✓, Property ✓, Field ✓, Const ✓, Event, Constructor, Indexer, Operator.
- **Conversion rules** — `IConversionRule { bool Match(Type, Ctx); string ToWrapped(expr); string ToUnity(expr); Type WrappedType; }`
  Passthrough (public, non-generic-with-internal-args) ✓, EnumCast ✓, WrapperWrap/Unwrap, Array, List/IEnumerable, Nullable, ref/out, DelegateAdapter, Unsupported (pointer, ref struct, `Span<T>`, `TypedReference`, generic param with internal constraint).

Member rules never know type representations; they ask conversion rules "can T cross, and how?". Adding a conversion rule unlocks every member using T.

✓ = in step 1.

### Representation decisions (all steps)

- **Internal enum** → mirror enum, same underlying type, values, `[Flags]`. Conversion = plain cast `(UnityEditor.X)value` inside bridge (no `CastTo` expression trees).
- **Internal static class** → `public static class` with forwarding members.
- **Internal instance class** (step 2) → `public class` wrapper:
  - wrap null → null (so `== null` reads like original);
  - mirrors internal hierarchy (`WrappedUnityEditor.GameView : WrappedUnityEditor.PlayModeView`); chain stops at first public base, exposed as `Target` typed to that base (e.g. `EditorWindow`) + implicit conversion to it;
  - `==`/`Equals`/`GetHashCode` by underlying instance;
  - central `Wrap(object)` picks most-derived wrapper, so `as`/`is` work.
- **Delegates/events** (step 3) → mirror delegate + cached adapter per subscriber (generalized HandleUtility pattern).
- **Internal members of public types** (step 4) → `WrappedUnityEditor.EditorWindow` wrapper exposing only internal members as real properties/methods, implicit conversion both ways. Note: same simple name as public type → `using UnityEditor; using WrappedUnityEditor;` together makes `EditorWindow` ambiguous; consumers alias or fully qualify. (Accepted consequence of mirrored namespaces.)
- **Inheritable internals** (step 6) → abstract wrapper you subclass (`class MyToolbar : WrappedUnityEditor.SubToolbar`); generator emits hidden internal proxy subclass of real `UnityEditor.SubToolbar` forwarding overrides to wrapper.
- **Privates** (step 7) → same rule system with reflection-based emitters (cached delegates). Model must therefore not assume "accessible = internal"; carry an `Access` kind per member.

### Naming

`INamingStrategy` applied to member names (and optionally type names). `Exact` default. `PascalCase` converts camelCase members. Collisions after renaming (two members → same name, or member == type name) → `NameCollision` in report, member skipped. C# keywords escaped with `@`.

### Settings

`ScriptableSingleton<UnityInternalsGeneratorSettings>` with `[FilePath("ProjectSettings/UnityInternalsGeneratorSettings.asset", ProjectFolder)]` — per project, not inside symlinked DandaUtils. Fields: output folder, host bridge name (auto-chosen, persisted), naming strategy, assembly include/exclude, namespace include/exclude, type/member exclude (by id), rule order/enable flags, auto-exclusions (step 5). Inspector via Project Settings provider or simple EditorWindow.

### Entry points

Menu `Tools/DandaUtils/Unity Internals/Generate`, `…/Open Report`, `…/Settings`. On editor load: if output header Unity version ≠ `Application.unityVersion` → console warning suggesting regenerate (no auto-run).

## 5. Constraints

- Unity 6000.0.59f2 now; must not hardcode version-specific types/names. Reflection-only discovery.
- C# 9 (Unity), project conventions: `m_` private fields, `== false` not `!`, sealed new classes, PascalCase.
- No new packages/dependencies.
- Generator asmdef independent of output asmdef.
- Git: user commits themselves.

## 6. Member identity

Stable member id for reports/exclusions: `Assembly|Namespace.Type|Kind|Name(ParamTypeFullNames)`. Used by exclude lists and compile-error mapping.

## 7. Left to the builder

Internal class layout of generator, exact interfaces, CodeWriter details, report formatting, settings UI, test strategy (suggested: EditMode tests running classify/generate against a small fixture assembly with internal types, plus a real run against UnityEditor.CoreModule compiled by Unity).

## 8. Steps (verify after each)

1. **Skeleton** — settings, discovery, host selection + asmdef, report, CodeWriter, rule framework, AssemblyBuilder pre-compile gate (§9.1) with minimal auto-exclude. Default assembly include: `UnityEditor.CoreModule`, `UnityEngine.CoreModule` only. Rules: StaticClass + Enum types (incl. nested enums, kept nested); Method/Property/Field/Const members; conversions Passthrough + EnumCast. Also emit *static* members of internal non-static classes (as `public class` with only static members, no public ctor) — instance members `Deferred`. Verify: pre-compile clean, project compiles with zero errors in Unity, output deterministic (generate twice → identical bytes), report categories sane, spot-check a few calls work (e.g. a static internal method round-trips).
2. InstanceClass + Struct + Interface wrappers, Wrap/Unwrap conversion, hierarchy, Constructor, Indexer rules.
3. Delegates/events, Array, List/IEnumerable, Nullable, ref/out, Operator.
4. Public types' internal members.
5. Full compile-feedback loop hardening (bounded passes, persistence, widen default assembly include).
6. Inheritable proxies (SubToolbar case).
7. Privates via reflection.

## 9. Decisions after builder check

User decided:
- **Generated output is committed to git** (team + CI use it). ⇒ Output must be deterministic: stable ordering (sort types/members by id), no timestamps in files (header has generator version + Unity version only), stable `.meta` GUIDs (derive GUID deterministically from file path, e.g. MD5 of relative path, when creating new `.meta`; never rewrite existing ones), stable bridge number persisted in settings file (`ProjectSettings/` is committed too). On machines that don't regenerate, "Unity version differs" check is only a console warning.

My calls (builder may refine, not reverse):
1. **Containment** — broken output must never freeze project compile. Generator builds generated sources first off-project with `UnityEditor.Compilation.AssemblyBuilder` into a temp DLL (references: Unity engine/editor assemblies; assembly name = chosen bridge name so IVT applies). Only on clean build are files copied into `Assets/…` and refreshed. Pre-compile errors map file:line → member id via CodeWriter line map → auto-exclude → regenerate (bounded passes). Project-side `assemblyCompilationFinished` check kept as second safety net.
2. **Nested types** stay nested in wrappers (`WrappedUnityEditor.GameView.SomeEnum`), file per top-level type. Member id uses `+` for nesting like reflection (`Outer+Inner`).
3. **Wrapper storage** — wrappers hold the Unity value in an `internal` field typed to the internal Unity type (legal: internal member of public type). Internal structs → `public struct` wrapper with `internal UnityEditor.X m_Value` → value semantics, no boxing, `ref` params pass `ref w.m_Value`. Class wrappers same pattern with class.
4. **Batch mode** — not in scope; menu entry only. AssemblyBuilder path keeps batch support possible later.
5. Old hand-written wrapper stays and remains used by CanvasScenePicker, LayoutSaver, ScreenSizeTester — no consumer drives step order.
