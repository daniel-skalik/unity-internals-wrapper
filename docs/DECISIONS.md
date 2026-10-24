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
| B16 | Public types with internal members listed as Deferred (step 4) so they can be ticked now — superseded by section E | User | — |
| B17 | Browser saves on every tick (no Save button) | Claude | more settings writes |
| B18 | `SelectionRules.All` not editable (throws) | Claude | — |
| B19 | Opening Browser never persists a changed host name | Claude | — |

## C. Repo move (2026-10-21)

| # | Decision | By |
|---|---|---|
| C1 | Separate from DandaUtils into this repo; for now moved **as is** (no UPM package, no renames). Further work happens here | User |
| C2 | Proposed (not done): rename to `WrappedUnityInternals` (namespace, asmdefs, menus `Tools/Wrapped Unity Internals/…`, settings file), UPM package `com.danielskalik.wrappedunityinternals` | User named it; rest pending |

## D. Step 2 — instance class, struct and interface wrappers (2026-10-24)

Design: `docs/design/2026-10-24-step2-instance-wrappers-design.md`. Implements A6/A7/A8.

| # | Decision | By | Cost if wrong |
|---|---|---|---|
| D1 | Every wrapper level declares its **own** `m_Value` typed to its own internal type (`internal new readonly …` below the root) and chains `: base(value)`, instead of casting a single root field | Claude | one stored reference per hierarchy level |
| D2 | A wrapper's `Wrap(<unity type>)` is `internal` (a public member may not expose an internal type, CS0051); the single public entry point is the generated `WrappedRuntime.WrapperRegistry.Wrap(object)` | Claude | — |
| D3 | `Target` + implicit conversion only when the first public base is not `object` (C# forbids a user-defined conversion to a base class, and `object` is reachable anyway) | Claude | — |
| D4 | **Internal interface → wrapper class**, not a mirrored public interface: the Unity type could not implement a mirror, so values would need adapters in both directions and the "consumer implements it" direction is the inheritable-proxy work of step 6 | Claude | consumers cannot implement internal interfaces until step 6; a mirror would be a breaking change later |
| D5 | Registry keyed by the **exact** runtime type, walking the base chain, so `Wrap` returns the most-derived wrapper available; a per-wrapper `Wrap` falls back to `new T(value)` when the registry hands back a non-`T` (a public class between two internal ones makes the lower wrapper a root) | Claude | — |
| D6 | Registry registrations split into `AddNN(map)` chunks of 200, ordinal-sorted, each line attributed to its type id so the compile loop can auto-exclude it | Claude | — |
| D7 | A derived wrapper that repeats a member of an emitted wrapper base emits it with `new` rather than dropping it (both forward to the same virtual call) | Claude | duplicated API surface per level |
| D8 | `m_Value`, `Wrap`, `Target`, `Equals`, `GetHashCode` are reserved on instance wrappers: a wrapped member with that name becomes `NameCollision`, it is not renamed | Claude | 82 members of the CoreModules unreachable |
| D9 | Constructors of abstract types → `Deferred` "Inheritable (step 6)"; a parameterless struct constructor → `Unsupported` | Claude | — |
| D10 | Struct wrappers hold a **mutable** `m_Value` so instance field setters write through; equality compares the underlying value | Claude | — |
| D11 | Only members an interface itself declares are emitted (`DeclaredOnly`, as for classes) | Claude | inherited interface members unreachable |
| D12 | `RoslynProcessCompiler` takes an optional output directory it keeps, so tests can load the compiled wrappers and assert runtime behaviour | Claude | — |
| D13 | Indexer ids carry their index parameter types (`…|Property|Item(Ns.T)`), so two indexers of one type stay distinct in the catalog, the selection and the report; non-indexer properties keep the bare `…|Property|<Name>` form, so persisted selections are unaffected | Claude | a persisted indexer selection from a pre-step-2 settings file would read as missing — no such selection can exist, indexers were never emitted before |
| D14 | `Target` is reserved only on the shapes that declare it (class wrapper with a non-`object` public base, and the wrappers inheriting it); struct, interface and `object`-based class wrappers may wrap a member or nested type named `Target` | Claude | — |

## E. Step 4 — internal members of public types (2026-10-24)

Design: `docs/design/2026-10-24-step4-public-type-internals-design.md`. Implements §4/§8.4 of the generator design and retires B16.

| # | Decision | By | Cost if wrong |
|---|---|---|---|
| E1 | Public types become **ordinary classified types** (`GenerationContext.IsClassifiable` admits them), so the catalog, report, member planner, resolver and Browser reach them through the paths they already use; `CatalogBuilder` loses its separate public-type path | Claude | one classification path carries two very different kinds of type |
| E2 | A public type qualifies when it declares an accessible-internal member, **or** declares a non-visible nested type, **or** declares a public nested type that qualifies — the last two purely as containers | Claude | container-only hosts read `Wrapped` while generating nothing |
| E3 | Including **private and protected** nested types in E2 is what fixes the known catalog issue "private/protected nested types of public types parented inconsistently": the container node now always exists | Claude | more `Wrapped` type entries in the report (645 in the CoreModules) |
| E4 | Only accessible-internal members are mirrored (`internal`, `protected internal`); public members get no node, no verdict, no report entry. `protected`/`private` stay step 7 | Claude (per issue scope) | — |
| E5 | One type rule `PublicTypeInternalMembers` placed first, whose generator dispatches by shape, instead of letting the four shape rules match public types — one rule name in the report for the whole case | Claude | the report does not say which shape was emitted |
| E6 | `Target` on a public type's wrapper is **the type itself**, not its first public base, and struct and interface wrappers of public types declare it too (unlike D3/D14 for internal types) | Claude | an internal member named `Target` on a public type is `NameCollision` and unreachable |
| E7 | The implicit conversion runs **both ways** for public class and struct wrappers; a public **interface** wrapper gets neither (C# forbids an interface as either operand of a user-defined conversion, CS0552) and exposes only `Target` | Claude | internal members of a public interface need `Target`, not a conversion |
| E8 | **No hierarchy between wrappers of public types** — each is a root with its own `DeclaredOnly` members; a base type's internals live on the base type's wrapper, reachable in one step through the conversions | Claude | `WrappedUnityEditor.EditorWindow` does not inherit `ScriptableObject`'s wrapped internals |
| E9 | Wrappers of public types are **not registered** in `WrapperRegistry` and their `Wrap` constructs directly: the registry exists for base-chain dispatch (D5), which E8 removes | Claude | `WrapperRegistry.Wrap(someUnityObject)` returns null for a public type that has a wrapper |
| E10 | A public type in a signature stays **passthrough** (unchanged `PassthroughConversionRule` order), so wrapping it changes no other wrapper's API; and it is therefore not a selection dependency (`GenerationContext.NeedsWrapper` replaces `IsWrappedType` in `CatalogBuilder.Dependencies`) | Claude | the wrapper of a public type is never used as a parameter or return type |
| E11 | The `using UnityEditor;` + `using WrappedUnityEditor;` ambiguity (CS0104) is **documented, not worked around**; a disambiguating suffix would break A3 | User (A2/A3), restated here | consumers must alias or qualify the most familiar Unity type names |
