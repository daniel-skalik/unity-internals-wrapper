# Status & next steps (as of 2026-10-21)

## What exists
- `Assets/UnityInternalsGenerator/` — full generator source, moved unchanged from s1-new (`Assets/DandaUtils/Editor/UnityInternalsGenerator/`). Still named `DandaUtils.*` (namespaces, asmdefs, menus `Tools/DandaUtils/Unity Internals/…`, settings page `Project/DandaUtils/…`, settings file `ProjectSettings/UnityInternalsGeneratorSettings.asset`, meta GUID seed `DandaUtils.UnityInternalsGenerator:`).
- Tests: `Assets/UnityInternalsGenerator/Tests/` (208 EditMode tests, all passing at move time; fixture assembly `Tests/Fixture` with IVT to `Wrapped.Fixture.Host`).
- Docs: `docs/ENGINEERING.md` (how the code works), `docs/DECISIONS.md`, `docs/design/*`, `docs/plans/*`.

## To compile/test
Source is plain Unity editor code — it must sit inside a Unity project's `Assets/` (or be referenced as a package) to compile. Verified with Unity 6000.0.59f2. Tests run via Unity Test Runner (EditMode, assembly `DandaUtils.UnityInternalsGenerator.Tests`; perf test category `Slow`).

## Done
- **Step 1 (skeleton):** discovery, host bridge asmdef, rule framework, internal enums + static members of internal classes, Roslyn pre-compile + auto-exclude loop, deterministic output + metas, report, settings, menu. Real run: 2141 files / 6799 wrapped members from the two CoreModules.
- **Selection filter:** allowlist rules, catalog, resolver (dependencies, containers, missing), Browser window (search incl. dotted paths, filters, tri-state tree, A→Z), report changes, settings migration.

## Planned steps (from design)
1. ~~Skeleton~~ ✅
2. Instance class + struct + interface wrappers, Wrap/Unwrap conversion, hierarchy, constructors, indexers.
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
- Struct-typed static fields return copies (documented).
- `using UnityEditor;` + `using WrappedUnityEditor;` together → ambiguous names (accepted by design).
- Some test hygiene: duplicated FakeCompiler/LineOf helpers, a few weak assertions.

## Host project (s1-new) leftovers
`Assets/WrappedUnityInternals/` (generated output for the test selection `Toolbar.RepaintToolbar`), `ProjectSettings/UnityInternalsGeneratorSettings.asset`, `.gitattributes` line, `Docs/Build/UNITY_INTERNALS_GENERATOR.md` — still in s1-new.
