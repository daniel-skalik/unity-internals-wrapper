# Unity Internals Generator — Selection Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allowlist selection (namespace / type / member, with exceptions and future-member semantics) that limits what the generator emits, edited in a searchable tri-state tree window.

**Architecture:** Classification stays selection-agnostic (so dependency conversions keep working). A new `Catalog` (node tree of namespaces → types → planned members, each with status + dependency ids) is built from the classified context. A pure `SelectionResolver` combines catalog + `SelectionRules` (two id lists, deepest rule wins) into the emitted set (selected members, containers, dependency closure, missing ids). `WrapperGenerator` emits only that set; report entries gain a `Selected` flag. The Browser window is a thin IMGUI `TreeView` over a pure, tested `BrowserModel`.

**Tech Stack:** Unity 6000.0.59f2, C# 9, UnityEditor.IMGUI.Controls.TreeView, NUnit EditMode.

**Spec:** `C:/Users/Danko/AppData/Roaming/JetBrains/Rider2026.1/scratches/2026-10-21-unity-internals-selection-design.md` (+ base `…/2026-10-20-unity-internals-generator-design.md`; engineering doc `D:/Projects/s1-new/Docs/Build/UNITY_INTERNALS_GENERATOR.md`).

## Global Constraints

- Generator root `Assets/DandaUtils/Editor/UnityInternalsGenerator/` (may be symlink — Bash `grep -R`/`ls -R` or absolute Read). Namespaces mirror folders under `DandaUtils.Editor.UnityInternalsGenerator`; new folders `Selection/`, `Browser/`.
- Allowlist: empty selection → nothing generated + console warning. Ticking namespace/type = whole incl. future members/nested types. Exceptions under whole nodes. Deepest rule wins.
- Dependency beats exception **iff needed**: a type needed by an emitted member is emitted even if unticked/excepted; not emitted when nothing needs it. Selection never marks anything `Excluded`.
- `CompileFailed` auto-exclusion stays and is NOT overridden by dependency need (cascade, reported).
- Report entries keep real status + `selected` flag; new `WrapStatus.SelectedMissing`; md counts `NotSelected`; md per-status sections list only selected entries.
- Old `ExcludedIds`/`ExcludedNamespaces` semantics removed from `GenerationInput`; settings migrate them into exceptions.
- Determinism (committed output + settings): ordinal sorting, no duplicates in stored id lists, `\n`, invariant culture.
- Conventions: `m_` fields, `== false`, sealed, usings inside namespace, guard clauses, single-line expression bodies.
- Git: no commits/stage/stash by agents. Verify: Rider `get_file_problems` → Unity compile → EditMode tests (`DandaUtils.UnityInternalsGenerator.Tests`) via unityMCP (token in env notes).
- Existing 104 tests stay green (adapt those that used exclusions, Task 4).

## Review Focus

1. **Ticked id gone after Unity upgrade** → `SelectedMissing` entry in report + listed in Browser "Missing selections". (Task 3 test, Task 5 test)
2. **Needed dependency enum is nested in an unselected class** → outer class emitted as container holding only the nested enum. (Task 3 test)
3. **Toggling a node that has deeper rules beneath** → deeper rules cleared, no redundant rule left when ancestor already implies the state. (Task 7 test)
4. **Real-size catalog (2 CoreModules, ~44k nodes)** → catalog build < 15 s, `BrowserModel.Rows` for a search string < 200 ms. (Task 7 test, real assemblies)
5. **Stored lists hand-edited with duplicates/unsorted** → normalized on save, output identical. (Task 6 test)

---

## File Structure

```
Selection/SelectionRules.cs        two id lists + IsSelected (deepest rule wins)
Selection/CatalogNode.cs           node data (+ NodeKind enum)
Selection/Catalog.cs               node index, children, ancestry
Selection/CatalogBuilder.cs        ctx → Catalog
Selection/SelectionResolver.cs     Catalog + rules → SelectionResult
Selection/SelectionResult.cs
Selection/SelectionEditing.cs      Toggle / DisplayState (pure)
Browser/BrowserModel.cs            search/filter → rows (pure)
Browser/UnityInternalsBrowserWindow.cs   IMGUI TreeView window
Settings/SettingsMigration.cs      pure migration/normalization
Modify: Model/GenerationInput.cs, Model/GenerationContext.cs, Model/ReportEntry.cs, Model/WrapStatus.cs,
        Rules/MemberPlanner.cs, Rules/TypeRules.cs, Pipeline/WrapperGenerator.cs, Pipeline/GenerationRunner.cs,
        Output/ReportWriter.cs, Settings/UnityInternalsGeneratorSettings.cs, Settings/…SettingsProvider.cs,
        GenerationCommand.cs, GeneratorMenu.cs, Docs/Build/UNITY_INTERNALS_GENERATOR.md
Tests: Tests/SelectionRulesTests.cs, CatalogTests.cs, SelectionResolverTests.cs, SelectionGenerationTests.cs, SelectionEditingTests.cs, BrowserModelTests.cs, SettingsMigrationTests.cs (+ adapt ClassificationTests, EmissionTests, CompileTests, OutputTests)
```

**Plan decision (refines spec §3):** the spec's three lists collapse to two — `Included` (whole for containers, plain select for leaves; explicit members are just leaf ids in it) and `Excluded` (exceptions). Same semantics, fewer cases.

**Node ids:** types `MemberId.Type(t)`, members `MemberId.Member(m)`, namespaces `"ns:" + fullNamespace` (assembly-agnostic, e.g. `ns:UnityEditor.Experimental`), global namespace `"ns:"`. Namespace parent of `ns:A.B` is `ns:A`; top namespace parent is null (global `ns:` is its own root, parent null).

---

### Task 1: SelectionRules

**Files:** Create `Selection/SelectionRules.cs`; Test `Tests/SelectionRulesTests.cs`.

**Interfaces — Produces:**
- `sealed class SelectionRules { SelectionRules(IEnumerable<string> included, IEnumerable<string> excluded); IReadOnlyList<string> Included; IReadOnlyList<string> Excluded; /* both distinct, sorted ordinal */ static SelectionRules All { get; } /* selects everything */ static SelectionRules Empty { get; } bool IsEmpty; bool? RuleAt(string id) /* true incl, false excl, null none; excluded wins if id in both */; bool IsSelected(string id, Func<string, string> parentOf) /* walk id→ancestors, first non-null RuleAt decides; none → false; All → true */ }`
- `static class SelectionIds { string Namespace(string ns) /* "ns:" + (ns ?? "") */; string NamespaceParent(string nsId) /* ns:A.B → ns:A; ns:A → null; ns: → null */ }`

- [ ] **Step 1: Failing tests** — tree a→b→c (parentOf map): `Included{a}` → c selected; `Included{a}, Excluded{b}` → c not, a selected; `Included{a,c}, Excluded{b}` → c selected (deepest wins), b not; empty → nothing; `All.IsSelected("x", _ => null)` true; ctor dedups + sorts (`{"b","a","a"}` → `["a","b"]`); `RuleAt` with id in both lists → false; `SelectionIds.Namespace(null) == "ns:"`, `NamespaceParent("ns:A.B") == "ns:A"`, `NamespaceParent("ns:A") == null`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4:** Rider + compile + tests PASS. **Step 5: Stop** (no commit).

### Task 2: Catalog + plan memoization

**Files:** Create `Selection/CatalogNode.cs`, `Selection/Catalog.cs`, `Selection/CatalogBuilder.cs`; Modify `Model/GenerationContext.cs`, `Rules/TypeRules.cs` (TypeEmission/ClassTypeGenerator use memoized plan); Test `Tests/CatalogTests.cs`.

**Interfaces:**
- Consumes: `ctx.ClassifyAll()`, `ctx.Classify(t)`, `MemberPlanner.Plan(...)`, `ctx.Convert`, `ctx.IsWrappedType`, `MemberId`, `SelectionIds`.
- Produces:
  - `GenerationContext.PlanFor(Type t) → IReadOnlyList<PlannedMember>` — memoized wrapper over `MemberPlanner.Plan` with the class member rules (so entries are reported exactly once); `TypeEmission` uses it. Also `IReadOnlyList<(MemberInfo Member, WrapStatus Status, string Reason)> ctx.MemberVerdicts(Type t)` — every member considered for t with its verdict (needed for catalog rows of non-wrapped members). Builder may shape this as a small sealed class instead of a tuple.
  - `enum NodeKind { Namespace, Class, StaticClass, Enum, Method, Property, Field, Const, Other }`
  - `sealed class CatalogNode { string Id; string ParentId; string Name; NodeKind Kind; WrapStatus Status; string Reason; IReadOnlyList<string> DependencyTypeIds /* wrapped internal types (by type id) this member's signature needs; empty for non-members */; bool IsTopLevelType; }`
  - `sealed class Catalog { IReadOnlyList<CatalogNode> Nodes /* sorted by Id ordinal */; CatalogNode Get(string id) /* null if absent */; IReadOnlyList<CatalogNode> ChildrenOf(string id) /* null id → roots; sorted: namespaces, types, members by Name ordinal */; string ParentOf(string id); bool Contains(string id); }`
  - `static Catalog CatalogBuilder.Build(GenerationContext ctx)` — calls `ClassifyAll`; nodes for: every classified type of the sources (any status except compiler-generated `Excluded`), namespace nodes for every namespace path segment that has ≥1 type node, members of **Wrapped** class types (any verdict) via `MemberVerdicts`. Type parent = declaring type id or namespace id; member parent = type id. Enum types: no member nodes (atomic). Dependencies: for each Wrapped member, every param/return/property/field type `x` (unwrapping nothing — only direct types) with `ctx.IsWrappedType(x)` → `MemberId.Type(x)`, plus their declaring chain is NOT added here (resolver adds containers).

- [ ] **Step 1: Failing tests** (fixture, host `Wrapped.Fixture.Host`): catalog contains `ns:UnityInternalsFixture` (parent null), `StaticApi` type node (parent ns, `IsTopLevelType`), `StaticApi+Nested` (parent StaticApi type id, Kind Enum), member node for `StaticApi.Add` (Kind Method, Status Wrapped), `StaticApi.WithRef` (Status Deferred), `Next` member DependencyTypeIds contains Color8 type id and Mask type id; `Point` type node Status Deferred with no member children; `Color8` node has no children; `ChildrenOf(null)` contains `ns:UnityInternalsFixture`; compiler-generated `<>c` absent. Existing EmissionTests still pass (plan memo doesn't change output, entries not duplicated: `Entries` has unique ids — existing test).
- [ ] **Step 2–4:** FAIL → implement → PASS (whole suite). **Step 5: Stop.**

### Task 3: SelectionResolver

**Files:** Create `Selection/SelectionResult.cs`, `Selection/SelectionResolver.cs`; Test `Tests/SelectionResolverTests.cs`.

**Interfaces:**
- Consumes: Task 1 `SelectionRules`, Task 2 `Catalog`.
- Produces:
  - `sealed class SelectionResult { IReadOnlyCollection<string> SelectedIds /* every catalog node whose IsSelected is true */; IReadOnlyCollection<string> EmittedMemberIds; IReadOnlyCollection<string> EmittedTypeIds; IReadOnlyDictionary<string, string> AutoIncludedBy /* type id → member id (ordinal-smallest) that needs it; only for types emitted but not themselves selected */; IReadOnlyList<string> MissingIds /* rule ids not in catalog, sorted */; int EmittedFileCount /* emitted top-level types */; bool IsSelected(string id); }`
  - `static SelectionResult SelectionResolver.Resolve(Catalog catalog, SelectionRules rules)`

Algorithm (exact):
1. `SelectedIds` = catalog ids where `rules.IsSelected(id, catalog.ParentOf)`.
2. Emitted members = selected member nodes with Status Wrapped.
3. Emitted types seed = selected Enum nodes with Status Wrapped (enums are atomic). Class types are never seeded directly: they are emitted only as containers (step 5) of emitted members or emitted nested types — a selected class with nothing emittable inside produces no file.
4. Dependencies: for each emitted member, each `DependencyTypeIds` entry → emitted type; if not in `SelectedIds` → `AutoIncludedBy[dep] = min(existing, memberId)`.
5. Containers: for every emitted type and every emitted member, add every ancestor **type** node (declaring chain) to emitted types (as container; they emit only their emitted members/nested types). Ancestor containers not themselves selected also get `AutoIncludedBy` (by the first id that required them).
6. `MissingIds` = (rules.Included ∪ rules.Excluded) − catalog ids, excluding `SelectionRules.All`.
7. `rules == All` → everything Wrapped emitted (equivalent to step 1 output).

- [ ] **Step 1: Failing tests** (fixture catalog from Task 2):
  - Empty → no emitted ids, `EmittedFileCount == 0`.
  - `Included{StaticApi}` → all Wrapped StaticApi members emitted (Add, Reset, Current…), `StaticApi+Nested` emitted (whole incl. nested), Deferred `WithRef` selected but not emitted, Color8 + Mask emitted with AutoIncludedBy set (needed by Next/Current).
  - `Included{StaticApi}, Excluded{StaticApi.Add id}` → Add not emitted, Reset emitted.
  - `Included{StaticApi.Next id}` only → emitted members == {Next}; emitted types ⊇ {StaticApi (container, AutoIncludedBy = Next id), Color8, Mask}; Holder not emitted.
  - Dependency beats exception: `Included{StaticApi.Next id}, Excluded{Color8 id}` → Color8 still emitted, AutoIncludedBy[Color8] == Next id.
  - Iff needed: `Included{ns:UnityInternalsFixture}, Excluded{Color8 id, StaticApi id, <every other type that uses Color8>}` — simpler: `Included{Holder id}, Excluded{Color8 id}` → Color8 not emitted.
  - Nested-container (Review Focus 2): add fixture member if absent: `internal static class UsesNested { internal static StaticApi.Nested Pick() => StaticApi.Nested.X; }`; `Included{UsesNested id}` → emitted types contain `StaticApi` (container) and `StaticApi+Nested`; emitted members contain none of StaticApi's own members.
  - Namespace whole: `Included{ns:UnityInternalsFixture}` emitted set equals `All` emitted set.
  - Missing (Review Focus 1): `Included{"Gone|X|Type|X"}` → `MissingIds == ["Gone|X|Type|X"]`.
- [ ] **Step 2–4:** FAIL → implement → PASS. **Step 5: Stop.**

### Task 4: Pipeline integration

**Files:** Modify `Model/GenerationInput.cs`, `Model/GenerationContext.cs`, `Model/ReportEntry.cs`, `Model/WrapStatus.cs`, `Rules/MemberPlanner.cs`, `Rules/TypeRules.cs`, `Pipeline/WrapperGenerator.cs`, `Pipeline/GenerationRunner.cs`; adapt `Tests/ClassificationTests.cs`, `Tests/EmissionTests.cs`, `Tests/CompileTests.cs`; Create `Tests/SelectionGenerationTests.cs`.

**Interfaces:**
- Produces:
  - `GenerationInput(IReadOnlyList<Assembly> sources, string hostAssemblyName, NamingStyle naming, SelectionRules selection, string unityVersion, IReadOnlyCollection<string> autoExcludedIds = null)`; `SelectionRules Selection` (null → `SelectionRules.All`). `ExcludedIds`/`ExcludedNamespaces` removed; `IsExcluded`/`IsExcludedId`/`UserExclusion` paths removed from context and planner.
  - `WrapStatus.SelectedMissing` appended last.
  - `ReportEntry(string id, string rule, WrapStatus status, string reason, bool selected = true)`; `bool Selected`.
  - `GenerationResult` gains `SelectionResult Selection` and `Catalog Catalog`.
  - `GenerationContext.ApplySelection(SelectionResult r)`; `bool IsEmitted(Type t)`; `bool IsEmitted(MemberInfo m)` — emission (`TypeEmission`, class/nested emission) skips anything not emitted.
- `WrapperGenerator.Generate`: classify → catalog → resolve → `ApplySelection` → rewrite entries: `Selected = result.IsSelected(id) || AutoIncludedBy.ContainsKey(id)`; auto-included type entries get Reason `"auto-included: needed by <id>"`; one entry per `MissingIds` with Status SelectedMissing, Rule `"Selection"`, Reason `"selected id not found in current Unity"`, Selected true → emit files for emitted top-level types only. Path-collision guard runs over emitted top-level types only.
- `GenerationRunner` passes `Selection` through unchanged on every pass.

- [ ] **Step 1: Failing tests** (`SelectionGenerationTests`):
  - `Generate(selection: Included{StaticApi.Next id})` → files exactly `[WrappedUnityInternalsFixture/Color8.g.cs, …/Mask.g.cs, …/StaticApi.g.cs]`; StaticApi.g.cs contains `Next(` and not `Add(`; entry for Add has `Selected == false`, Status Wrapped; Color8 entry Reason starts `"auto-included: needed by"`.
  - `Generate(selection: null)` → byte-identical files to the pre-change output (assert equals current `StaticApi.g.cs` text captured in the existing EmissionTests expectations — existing tests cover this; they must run with `null` selection).
  - `SelectionRules.Empty` → zero files.
  - Missing id → entry Status SelectedMissing.
  - Compile cascade: `GenerationRunner.Run` with fake compiler failing on Color8's file line and selection `Included{StaticApi.Next id}` → Color8 CompileFailed, Next entry Excluded with reason containing `"AutoExclusion"`, Success true (cascade kept; dependency need does not resurrect it).
  - Adapt old exclusion tests: `Types_ExcludedById_AndNamespace`, `Convert_ExcludedEnum_CarriesExcluded`, `Types_NestedInExcludedType_InheritsExcluded` → delete or rewrite to selection (not selected ≠ Excluded: classification status stays Wrapped).
- [ ] **Step 2–4:** FAIL → implement → PASS (whole suite). **Step 5: Stop.**

### Task 5: Report

**Files:** Modify `Output/ReportWriter.cs`; adapt/extend `Tests/OutputTests.cs`.

**Interfaces — Produces:** `ReportWriter.Build(entries, unityVersion, host)` unchanged signature. JSON entry gains `"selected":true|false` after `"status"`. MD: status table counts **selected** entries per WrapStatus (all enum values incl. SelectedMissing, enum order), then row `| NotSelected | <count of Selected == false> |`; per-status sections list only selected, non-Wrapped entries (SelectedMissing section included).

- [ ] **Step 1: Failing tests** — entries: 2 selected Wrapped, 1 unselected Deferred, 1 SelectedMissing → md has `| Wrapped | 2 |`, `| Deferred | 0 |`, `| NotSelected | 1 |`, `| SelectedMissing | 1 |`, a SelectedMissing section listing the id, no Deferred section; json contains `"selected":false` for the Deferred id.
- [ ] **Step 2–4:** FAIL → implement → PASS. **Step 5: Stop.**

### Task 6: Settings, migration, command

**Files:** Create `Settings/SettingsMigration.cs`; Modify `Settings/UnityInternalsGeneratorSettings.cs`, `Settings/UnityInternalsGeneratorSettingsProvider.cs`, `GenerationCommand.cs`; Test `Tests/SettingsMigrationTests.cs`.

**Interfaces — Produces:**
- Settings fields: keep `[SerializeField] List<string> m_ExcludedIds` (now = selection exceptions), add `[SerializeField] List<string> m_IncludedIds`, keep `[SerializeField, HideInInspector] List<string> m_ExcludedNamespaces` only for migration. API: `SelectionRules Selection { get; }`, `void SetSelection(SelectionRules rules)` (stores normalized lists, sets dirty), `Save()`. On `OnEnable`: `SettingsMigration.MigrateNamespaces(m_ExcludedNamespaces, m_ExcludedIds)` → appends `"ns:" + n` for each, clears namespaces list, normalizes; saves if anything changed.
- `static class SettingsMigration { static bool MigrateNamespaces(List<string> excludedNamespaces, List<string> excludedIds); static List<string> Normalize(IEnumerable<string> ids) /* distinct, non-empty, sorted ordinal */ }`
- `GenerationCommand`: public `static GenerationInput CreateInput(out string error)` (resolve sources, host, selection from settings) reused by Browser; `Execute()` logs warning `"Unity Internals Generator: nothing selected — open Tools/DandaUtils/Unity Internals/Browser"` when `Selection.IsEmpty` and still runs (output becomes empty set).
- Settings page: remove ExcludedNamespaces/ExcludedIds drawing; show `Included: N, Exceptions: M` + "Open Browser" button + existing Generate button.

- [ ] **Step 1: Failing tests** — `MigrateNamespaces(["UnityEditor.Foo"], ["b","a"])` → returns true, ids `["a","b","ns:UnityEditor.Foo"]`, namespaces empty; second call returns false; `Normalize(["b","","a","b"])` → `["a","b"]` (Review Focus 5).
- [ ] **Step 2–4:** FAIL → implement → PASS + compile. **Step 5: Stop.**

### Task 7: Selection editing + Browser model

**Files:** Create `Selection/SelectionEditing.cs`, `Browser/BrowserModel.cs`; Tests `Tests/SelectionEditingTests.cs`, `Tests/BrowserModelTests.cs`.

**Interfaces — Produces:**
- `enum CheckState { None, Partial, Whole }`
- `static class SelectionEditing { static SelectionRules Set(SelectionRules rules, Catalog catalog, string id, bool selected) /* remove every rule on descendants of id; then if IsSelected(id) under remaining rules == selected → no rule on id, else add rule (incl or excl) on id; return new normalized rules */; static SelectionRules Toggle(SelectionRules rules, Catalog catalog, string id) /* None/Partial → Set true; Whole → Set false */; static CheckState StateOf(Catalog catalog, SelectionRules rules, string id) /* leaf: Whole if selected else None; container: Whole if it and all descendants selected, None if none selected, else Partial */ }` — implement `StateOf` for all nodes in one bottom-up pass (`static IReadOnlyDictionary<string, CheckState> States(Catalog, SelectionRules)`) for UI speed.
- `enum ShowFilter { All, Selected, Unselected }`; `sealed class BrowserRow { string Id; int Depth; bool HasChildren; }`; `sealed class BrowserModel { BrowserModel(Catalog catalog); IReadOnlyList<BrowserRow> Rows(string search, ShowFilter show, WrapStatus? status, ISet<string> expanded, SelectionResult selection, IReadOnlyDictionary<string, CheckState> states); }` — search: case-insensitive substring on `Name` of type and member nodes; when search non-empty, all ancestors of matches are shown and treated as expanded; namespace nodes match only through descendants. Show/Status filters apply to leaves and types; containers shown when any descendant passes. Precompute lowercase names + child lists in ctor.

- [ ] **Step 1: Failing tests** — fixture catalog:
  - `Toggle` on `StaticApi` from Empty → `Included == [StaticApi id]`; Toggle again → Empty.
  - `Set(Included{StaticApi}, Add, false)` → `Excluded == [Add id]`; `StateOf(StaticApi) == Partial`; `Set(..., Add, true)` → back to `Included == [StaticApi id]`, `Excluded` empty (Review Focus 3: redundant rule removed).
  - `Set(Included{StaticApi.Add, StaticApi.Reset}, StaticApi, true)` → `Included == [StaticApi id]` (descendant rules cleared).
  - Under `Included{ns:UnityInternalsFixture}`, `Set(StaticApi, false)` → `Excluded == [StaticApi id]`.
  - `Rows("next", All, null, empty, …)` contains ns node, StaticApi, Next; not Holder. `Rows("", Selected, …)` with `Included{Holder}` contains Holder rows only (+ ns). `status: Deferred` shows WithRef, hides Add.
  - Perf (Review Focus 4): real catalog from `UnityEditor.CoreModule` + `UnityEngine.CoreModule` with host `Unity.InternalAPIEngineBridge.002` → `CatalogBuilder.Build` < 15 s; `new BrowserModel(c).Rows("gameview", All, null, …)` < 200 ms (Stopwatch; mark test `[Category("Slow")]`).
- [ ] **Step 2–4:** FAIL → implement → PASS. **Step 5: Stop.**

### Task 8: Browser window, menu, docs, E2E

**Files:** Create `Browser/UnityInternalsBrowserWindow.cs`; Modify `GeneratorMenu.cs`, `Settings/UnityInternalsGeneratorSettingsProvider.cs` (button), `Docs/Build/UNITY_INTERNALS_GENERATOR.md`.

**Interfaces — Consumes:** `GenerationCommand.CreateInput`, `WrapperGenerator`/classification context, `CatalogBuilder.Build`, `SelectionResolver.Resolve`, `SelectionEditing`, `BrowserModel`, settings `Selection`/`SetSelection`/`Save`, `GenerationCommand.Execute`.

Window (`EditorWindow`, title "Unity Internals"): toolbar = search field (`SearchField`), Show popup, Status popup, Save, Generate; summary line `Selected: {types} types, {members} members · auto-included: {n} · missing: {m} · would generate: {files} files`; IMGUI `TreeView` built from `BrowserModel.Rows` (rebuild rows on search/filter/expand change; checkbox toggle → `SelectionEditing.Toggle` → re-resolve → repaint); row = checkbox (Partial drawn as mixed value via `EditorGUI.showMixedValue`), name, kind, status (reason as tooltip), suffix `auto · needed by <name>` for `AutoIncludedBy` rows, `missing` rows under a top pseudo-node "Missing selections" (from `MissingIds`, untick removes rule). Catalog built on open with progress bar (`EditorUtility.DisplayProgressBar` try/finally) and on "Refresh" button; not persisted. Selection saved via `SetSelection`+`Save` on Save button, on Generate, and in `OnDisable` when dirty. Empty selection → help box "Nothing selected — tick items to generate". Menu `Tools/DandaUtils/Unity Internals/Browser`; settings page button opens it.

- [ ] **Step 1: Implement** window + menu + button.
- [ ] **Step 2: Verify compile** (Rider problems → Unity compile clean; full suite PASS).
- [ ] **Step 3: E2E** via `mcp__unityMCP__execute_code`: set settings selection to `Included{MemberId of UnityEditor.Toolbar.RepaintToolbar}` (compute id via `MemberId.Member` on reflected method) and save; run menu Generate; wait compile. Expected: output contains `WrappedUnityEditor/Toolbar.g.cs` with `RepaintToolbar` and few other files (dependencies only), Unity compile clean, `_GenerationReport.md` shows `NotSelected` large and `Wrapped` small; `WrappedUnityEditor.Toolbar.RepaintToolbar()` runs.
- [ ] **Step 4: Open window** via `execute_menu_item`, capture via `mcp__unityMCP__capture_scene_view`/screenshot tool if available, else state not visually verified. Search "Toolbar" shows Toolbar row ticked.
- [ ] **Step 5: Docs** — update `Docs/Build/UNITY_INTERNALS_GENERATOR.md`: selection model (ids, deepest rule, dependency iff needed, empty = nothing), Browser window, report fields, migration. Codebase facts only.
- [ ] **Step 6: Stop** for user review.
