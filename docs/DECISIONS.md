# Decisions log

Every decision made so far, who made it, and what it costs if wrong. Newest topics at the bottom.
"User" = decided by the repo owner. "Claude" = call made during implementation (reviewable, reversible).

## A. Core generator (step 1, 2026-10-20)

| # | Decision | By | Cost if wrong |
|---|---|---|---|
| A1 | Deterministic code generator, no LLM, reflection over the running editor's assemblies; works on any Unity version | User | — |
| A2 | Mirror namespaces: `UnityEditor.X` → `WrappedUnityEditor.X` (prefix `Wrapped` to first segment); global namespace → `WrappedGlobal` | User / Claude (global) | rename churn |
| A3 | Identical member names + signatures by default; naming is a setting (`Exact` default, `PascalCase`) — applies to member names only, type names stay exact | User / Claude (types exact) | PascalCase won't rename types |
| A4 | Host assembly = own asmdef named with a free `Unity.InternalAPIEngineBridge.0NN` friend name (Unity's IVT bridge names), chosen by most-granting family, lowest unused number, persisted in settings | Claude, approved | collision → regenerate picks new name + warning |
| A5 | Rule-based pipeline: type rules, member rules, conversion rules; first match wins; unmatched → reported | User idea, Claude design | — |
| A6 | Internal classes → wrapper classes (null stays null, mirrored hierarchy, `Target` + implicit conversion to first public base) — planned step 2 | Claude, approved | — |
| A7 | Wrapper storage: `internal` field holding the real Unity value (structs stay value types) — step 2 | Claude | — |
| A8 | Nested types stay nested in wrappers; one file per top-level type | Claude | — |
| A9 | Generated output is **committed to git** → determinism mandatory (ordinal sort, `\n`, invariant culture, no timestamps, deterministic `.meta` GUIDs = MD5 of seed+path, never rewrite existing metas) | User | — |
| A10 | Broken output must never reach Assets: pre-compile with Unity's bundled Roslyn (`NetCoreRuntime/dotnet` + `DotNetSdkRoslyn/csc.dll`) as a synchronous process, auto-exclude failing members, bounded passes; write only on clean compile | Claude (replaced AssemblyBuilder idea) | — |
| A11 | Pre-compile references = generator asmdef's `compiledAssemblyReferences` + source assemblies only (mirror Unity's real compile). Members needing Bee/Cecil get CompileFailed (15 in CoreModules) | Claude (reverted an earlier "add Bee refs" ruling that made pre-compile pass but Unity fail) | those 15 need explicit refs later |
| A12 | Default included assemblies: `UnityEditor.CoreModule`, `UnityEngine.CoreModule` | Claude | — |
| A13 | Output folder default `Assets/WrappedUnityInternals/` (outside shared utils; version-specific, per project) | Claude | — |
| A14 | Member ids: `Assembly|Namespace.Outer+Inner|Kind|Name(Params)`, params rendered without assembly qualification (`List`1[System.Int32]`, `&`, `*`, `!!0`, generic arity `Echo``1`) | Claude | id format rework |
| A15 | Report statuses: Wrapped, Deferred (planned later step), Unsupported (never), Excluded, NotAccessible, NoBridgeAccess, NameCollision, CompileFailed, SelectedMissing | Claude | — |
| A16 | Member order in output: fields → properties → methods, each by MetadataToken | Claude | cosmetic |
| A17 | Csc forced to English (`-preferreduilang:en-US`) so committed report doesn't vary by OS language | Claude | — |
| A18 | `[CallerFilePath/MemberName/LineNumber]` copied onto wrapper params | Claude | — |
| A19 | File path collisions (case-insensitive) → all colliding types NameCollision | Claude | — |
| A20 | Generated `.asmdef` left untouched when same name exists (something in the user's editor injects TestRunner GUID refs into new asmdefs after import; unknown source) | Claude | same-name asmdef never corrected |
| A21 | `.gitattributes`: `Assets/WrappedUnityInternals/** text eol=lf` in the host project | Claude | — |
| A22 | No Unity-upgrade guard (versionDefines) — recovery documented: delete output folder, regenerate (Safe Mode doesn't load generator) | **Open — user to decide** | — |

## B. Selection filter (2026-10-21)

| # | Decision | By | Cost if wrong |
|---|---|---|---|
| B1 | Allowlist: only selected things generate; empty selection → nothing + warning | User / Claude (empty) | — |
| B2 | Ticking namespace/type = whole, **including members Unity adds in future** | User | — |
| B3 | Ticking members = exactly those; new members stay off | User | — |
| B4 | Unticking under a whole node = stored exception; deepest rule wins | User / Claude | — |
| B5 | Dependency beats exception **iff needed**: a type needed by an emitted member is emitted even if unticked; not emitted when nothing needs it | User | — |
| B6 | CompileFailed is never resurrected by dependency need (cascade reported) | Claude | — |
| B7 | Two stored lists (`Included`, `Excluded`) instead of three | Claude | — |
| B8 | Namespace ids `ns:<full.namespace>` (global `ns:`), hierarchical, assembly-agnostic | Claude | — |
| B9 | Containers: a ticked member generates its class as a shell with only ticked members; enums atomic; class with nothing emittable → no file (also dropped as nested class) | Claude | — |
| B10 | Old `ExcludedIds/ExcludedNamespaces` folded into exceptions; selection never produces `Excluded` | Claude | — |
| B11 | Report: entries keep real status + `selected` flag; md counts selected per status + `NotSelected` row; JSON keeps only selected entries + `notSelectedCount` (was 9.8 MB) | Claude | unselected statuses only visible in Browser |
| B12 | Missing ticked ids → `SelectedMissing`; missing exception ids → distinct reason; members of auto-excluded/non-wrapped containers → declaring-type reason, not "missing" | Claude | — |
| B13 | Browser: IMGUI TreeView, search on names; dotted search (`UnityEditor.Toolbar`, `Toolbar.AddSubToolbar`) = last part matches name, prefix matches parent path on segment boundary | User / Claude | — |
| B14 | Browser sorted A→Z case-insensitive, namespaces and types interleaved | User | — |
| B15 | Expand matched containers while searching | User | — |
| B16 | Public types with internal members listed as Deferred (step 4) so they can be ticked now | User | — |
| B17 | Browser saves on every tick (no Save button) | Claude | more settings writes |
| B18 | `SelectionRules.All` not editable (throws) | Claude | — |
| B19 | Opening Browser never persists a changed host name | Claude | — |

## C. Repo move (2026-10-21)

| # | Decision | By |
|---|---|---|
| C1 | Separate from DandaUtils into this repo; for now moved **as is** (no UPM package, no renames). Further work happens here | User |
| C2 | Proposed (not done): rename to `WrappedUnityInternals` (namespace, asmdefs, menus `Tools/Wrapped Unity Internals/…`, settings file), UPM package `com.danielskalik.wrappedunityinternals` | User named it; rest pending |
