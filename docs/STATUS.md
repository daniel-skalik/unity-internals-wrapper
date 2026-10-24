# Status & next steps (as of 2026-10-24)

## What exists
- `Assets/UnityInternalsGenerator/` — full generator source, moved unchanged from s1-new (`Assets/DandaUtils/Editor/UnityInternalsGenerator/`). Still named `DandaUtils.*` (namespaces, asmdefs, menus `Tools/DandaUtils/Unity Internals/…`, settings page `Project/DandaUtils/…`, settings file `ProjectSettings/UnityInternalsGeneratorSettings.asset`, meta GUID seed `DandaUtils.UnityInternalsGenerator:`).
- Tests: `Assets/UnityInternalsGenerator/Tests/` (253 EditMode tests, all passing; plus one `[Explicit]` real run over the CoreModules. Fixture assembly `Tests/Fixture` with IVT to `Wrapped.Fixture.Host`; `WrapperRuntimeTests` compiles the fixture wrappers with the bundled Roslyn, loads the DLL and exercises them).
- Docs: `docs/ENGINEERING.md` (how the code works), `docs/DECISIONS.md`, `docs/design/*`, `docs/plans/*`.

## To compile/test
Source is plain Unity editor code — it must sit inside a Unity project's `Assets/` (or be referenced as a package) to compile. Verified with Unity 6000.0.59f2. Tests run via Unity Test Runner (EditMode, assembly `DandaUtils.UnityInternalsGenerator.Tests`; perf test category `Slow`).

## Done
- **Step 1 (skeleton):** discovery, host bridge asmdef, rule framework, internal enums + static members of internal classes, Roslyn pre-compile + auto-exclude loop, deterministic output + metas, report, settings, menu. Real run: 6799 wrapped members from the two CoreModules (file counts re-measured for the step 2 comparison below).
- **Selection filter:** allowlist rules, catalog, resolver (dependencies, containers, missing), Browser window (search incl. dotted paths, filters, tri-state tree, A→Z), report changes, settings migration.
- **Step 2 (instance types):** instance class, struct and interface wrappers (`internal m_Value`, mirrored base chain up to the first public base with `Target` + implicit conversion, equality by underlying instance), the generated central `WrappedRuntime.WrapperRegistry.Wrap(object)`, the WrapperWrap/Unwrap conversion rule, constructor and indexer member rules, instance members, `new` for members a derived wrapper repeats, reserved wrapper names. Design `docs/design/2026-10-24-step2-instance-wrappers-design.md`, plan `docs/plans/2026-10-24-step2-instance-wrappers-plan.md`.

  Real run over `UnityEditor.CoreModule` + `UnityEngine.CoreModule` (whole selection, Unity 6000.0.59f2, 3 pre-compile passes, clean):

  | | files | Wrapped | Deferred | Unsupported | Excluded | NotAccessible | NameCollision | CompileFailed |
  |---|---|---|---|---|---|---|---|---|
  | step 1 | 912 | 6799 | 14840 | 124 | 1822 | 19069 | 0 | 15 |
  | step 2 | 2503 | 22522 | 2188 | 278 | 1924 | 19222 | 82 | 25 |

  (`NameCollision` is new because wrapper-reserved names are now rejected; the remaining `CompileFailed` are instance members whose signatures reach assemblies outside the reference set, see A11. The one extra `Unsupported` over the first step 2 measurement is a `ref struct`, now rejected by classification instead of by the compile loop, which is what removes two `CompileFailed`.)

## Planned steps (from design)
1. ~~Skeleton~~ ✅
2. ~~Instance class + struct + interface wrappers, Wrap/Unwrap conversion, hierarchy, constructors, indexers~~ ✅
3. Delegates/events, arrays, List/IEnumerable, Nullable, ref/out, operators, generic methods.
4. Internal members of public types (e.g. `UnityEditor.Menu.AddMenuItem`) — currently listed in Browser as Deferred.
5. Compile-feedback hardening, widen default assemblies.
6. Inheritable proxies (subclass internal abstract types, e.g. `SubToolbar`).
7. Privates via reflection.

## Open questions for the owner
- A22: Unity-upgrade guard (output switches itself off on other Unity versions) vs documented manual recovery.
- C2: do the rename to `WrappedUnityInternals` + package layout now?
- Real-input test of the Browser (mouse/keyboard) has never been done — only reflection + unit tests.

## Known minor issues (deferred, none blocking)
- Selected member of a public type that disappears in a newer Unity reads "Declaring type is Deferred" instead of SelectedMissing.
- Search: collapsed matched container still shows matching descendants while its arrow says collapsed.
- Private/protected nested types of public types parented inconsistently in the catalog.
- Browser rebuilds the catalog (~1.3 s) on every domain reload while visible; index-based TreeView ids remap after a catalog change.
- `[Obsolete]` not copied onto wrapped types / enum values; enum values get no report entries.
- csc `WaitForExit` has a 10-min timeout but no test; failure-path report marks still-present members CompileFailed.
- Struct-typed static fields and struct-typed instance fields return copies (documented).
- Internal interfaces are wrapper classes, so consumers cannot implement them (step 6), and inherited interface members are not re-emitted on a derived interface's wrapper.
- Members of internal instance types named `m_Value`/`Wrap`/`Equals`/`GetHashCode`, and `Target` on the shapes that declare it (a class wrapper with a non-`object` public base, and the wrappers inheriting it), are dropped as `NameCollision` instead of renamed (82 in the CoreModules).
- A class wrapper stores one reference per hierarchy level.
- `using UnityEditor;` + `using WrappedUnityEditor;` together → ambiguous names (accepted by design).
- Some test hygiene: duplicated FakeCompiler/LineOf helpers, a few weak assertions.

## Host project (s1-new) leftovers
`Assets/WrappedUnityInternals/` (generated output for the test selection `Toolbar.RepaintToolbar`), `ProjectSettings/UnityInternalsGeneratorSettings.asset`, `.gitattributes` line, `Docs/Build/UNITY_INTERNALS_GENERATOR.md` — still in s1-new.
