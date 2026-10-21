# unity-internals-wrapper

Generate public C# wrappers for Unity's `internal` editor and engine APIs, so you can call them from normal project code with the same names and signatures.

```csharp
// UnityEditor.Toolbar is internal — not callable from your code.
// After generation:
WrappedUnityEditor.Toolbar.RepaintToolbar();
```

The generator is deterministic and reflection-based (no AI in the loop). It reads the Unity assemblies of the editor you are running, so the wrappers always match your Unity version. Regenerate after upgrading.

> **Status:** early / work in progress. Step 1 (internal enums + static members of internal classes) and the selection Browser are done. See [Roadmap](#roadmap).

## How it works

Unity grants `InternalsVisibleTo` to a set of reserved "bridge" assembly names (`Unity.InternalAPIEngineBridge.001`–`024`, …). The generator:

1. Reads which bridge names the selected Unity assemblies grant and picks one that's unused in your project.
2. Classifies every internal type and member through ordered rules (type, member, conversion rules).
3. Emits wrapper source in mirrored namespaces: `UnityEditor.X` → `WrappedUnityEditor.X`, `UnityEngine.X` → `WrappedUnityEngine.X`.
4. Pre-compiles it off-project with Unity's bundled Roslyn compiler. Members that fail to compile are auto-excluded, so broken code never lands in `Assets/`.
5. Writes the result into its own asmdef (named with the chosen bridge name), with deterministic files and `.meta` GUIDs, ready to commit.
6. Writes a report listing every type and member and why it was or wasn't wrapped.

## Features

- **Same signatures:** member names and parameters match Unity's. Optional PascalCase naming setting.
- **Selective generation:** a searchable tree **Browser** lets you tick namespaces, classes or single members.
  - Ticking a whole class also covers members Unity adds in future versions.
  - Untick inside a ticked class to make exceptions.
  - Internal enums used by your selection are pulled in automatically.
  - Search by name or dotted path (`UnityEditor.Toolbar`, `Toolbar.AddSubToolbar`).
- **Nothing silently skipped:** the report (`_GenerationReport.md` / `.json`) gives a status and reason for everything: Wrapped, Deferred (planned), Unsupported, CompileFailed, SelectedMissing, …
- **Safe:** generated code is compiled before it's written; the generator never depends on its own output.
- **Deterministic:** sorted output, LF line endings, invariant culture, stable GUIDs. Two runs give byte-identical results.

## Requirements

- Unity 6 (verified on **6000.0.59f2**). Other versions should work since everything is discovered by reflection, but they're untested.
- Editor only (Windows verified).

## Install

Copy `Assets/UnityInternalsGenerator/` into your project's `Assets/`. It compiles as its own editor assembly. Tests are under `Tests/` (EditMode, need the Unity Test Framework).

## Usage

1. **Tools → DandaUtils → Unity Internals → Browser.** Search for and tick what you need.
2. Press **Generate** (in the Browser, or **Tools → DandaUtils → Unity Internals → Generate**).
3. Wrappers appear in `Assets/WrappedUnityInternals/` (configurable). Use them from any editor code:

   ```csharp
   using WrappedUnityEditor;
   // ...
   Toolbar.RepaintToolbar();
   ```

4. Check **Tools → DandaUtils → Unity Internals → Open Report** for what was and wasn't wrapped.

Settings live in **Project Settings → DandaUtils → Unity Internals Generator** (output folder, naming, included assemblies) and are saved to `ProjectSettings/UnityInternalsGeneratorSettings.asset`.

> Menu and namespace names still carry the `DandaUtils` prefix from the project this came from. A rename to `WrappedUnityInternals` is planned.

## Roadmap

1. ✅ Skeleton: internal enums, static members of internal classes.
2. Instance wrappers for internal classes, structs and interfaces (hierarchy, constructors, indexers).
3. Delegates/events, arrays, collections, `Nullable`, `ref`/`out`, operators, generic methods.
4. Internal members of public types (e.g. `UnityEditor.Menu.AddMenuItem`). These are already listed in the Browser as Deferred.
5. Compile-loop hardening, more assemblies by default.
6. Subclassable wrappers for internal abstract types (e.g. `SubToolbar`).
7. Private members via reflection.

## Caveats

- Internal APIs can change or disappear in any Unity release. Regenerate after upgrading; the report lists anything you selected that no longer exists.
- If generated code no longer compiles after a Unity upgrade, delete the output folder and regenerate.
- `using UnityEditor;` and `using WrappedUnityEditor;` together can make names ambiguous; qualify or alias them.
- Static fields of struct type return copies.

## Docs

- [`docs/ENGINEERING.md`](docs/ENGINEERING.md): how the code works.
- [`docs/STATUS.md`](docs/STATUS.md): current state and next steps.
- [`docs/DECISIONS.md`](docs/DECISIONS.md): design decisions.
- [`docs/design/`](docs/design), [`docs/plans/`](docs/plans): design docs and implementation plans.

## License

[MIT](LICENSE) © 2026 Daniel Skalik.

This license covers the generator. Unity's APIs and assemblies remain under Unity's own terms. Generated wrappers contain no Unity source code, only calls into it.
