# Unity Internals Generator — Selection Filter (Design addendum)

Date: 2026-10-21 · Builds on: `2026-10-20-unity-internals-generator-design.md` (step 1 shipped: generator at `Assets/DandaUtils/Editor/UnityInternalsGenerator/`, output `Assets/WrappedUnityInternals/`, 2141 files / 6799 wrapped members). Doc: `Docs/Build/UNITY_INTERNALS_GENERATOR.md`.

## 1. Intent

Full generation produces too many files. User wants to generate only chosen parts — per namespace, class, method, property, field, enum — through a searchable tick/untick tree inside Unity.

## 2. Goals / non-goals / anti-goals

Goals
- **Allowlist**: only selected things generate. Empty selection → generate nothing + console warning (output folder ends up with only asmdef/report/info). (my call)
- **Whole-node rule**: ticking a namespace or a type = that node and everything in it, **including members/nested types Unity adds in future versions** (user decided).
- **Explicit members**: ticking individual members selects exactly those; new members of that type stay off.
- **Exceptions**: unticking a child under a whole-ticked node stores an exception ("whole except X").
- **Auto-dependencies**: internal types a selected member needs to compile/convert (today: internal enums in signatures; later: wrapper types) are generated automatically, shown in UI as "auto-included because of <id>". Declaring types of selected members are implicitly generated as containers (a selected nested member implies its outer wrapper type, members of that outer not selected unless ticked).
- **Shared, committed**: selection stored by stable ids (`MemberId`) in `ProjectSettings/UnityInternalsGeneratorSettings.asset`.
- **Missing selections visible**: selected ids not found in current Unity → report status `SelectedMissing` (new) listed in report + shown in UI.
- **Deferred tickable**: user may tick Deferred/Unsupported items; they generate once later steps support them. Each row shows current status.
- **UI**: EditorWindow with search field + tri-state tree (see §4).

Non-goals
- Signature/text search (search = type + member names only, substring, case-insensitive). (my call)
- Per-member naming overrides, multi-selection profiles.

Anti-goals
- A ticked thing silently not generated (must show status/reason).
- Output that fails to compile because a dependency wasn't pulled in.
- Selection that silently shrinks after Unity upgrade.
- UI freezing the editor (tree ~44k nodes must stay responsive).

## 3. Selection model

Stored rules (settings, sorted ordinal, deterministic):
- `m_SelectedWhole: List<string>` — namespace ids (`ns:<Assembly>|<Namespace>`? see below) and type ids (`MemberId.Type`). Whole = node + all descendants now and future.
- `m_SelectedMembers: List<string>` — member ids (`MemberId.Member`) and nested type ids selected explicitly.
- `m_Exceptions: List<string>` — ids explicitly unticked under a whole node.

Namespace id: `Namespace` string alone (`UnityEditor.Experimental`), assembly-agnostic — whole-namespace also covers sub-namespaces? **No**: namespace whole = types directly in that namespace and its nested namespaces shown under it in the tree (tree groups by full namespace path segments; ticking `UnityEditor` ticks every namespace node under it). (my call: hierarchical — ticking a parent namespace node selects everything below it.)

Resolution (`SelectionResolver`, pure, testable): given classified candidates (all types/members from current classification) + rules → `IsSelected(id)`:
1. If id or any ancestor (declaring types, namespace path) is in Exceptions → not selected (nearest rule wins: an explicit select deeper than the exception re-selects; deepest rule wins overall).
2. Else if in SelectedMembers or any ancestor in SelectedWhole → selected.
3. Else not selected.
Deepest-rule-wins applies across all three lists.

Generation then = selected Wrapped members + their declaring types (as containers) + dependency closure (types referenced by conversions of selected members, recursively for their own containers). Unselected members of included container types are emitted? **No** — only selected members; container type gets only its selected members + required nested types. Enum types are atomic: selecting/auto-including an enum emits all its values.

Report: every candidate not selected → status `NotSelected` counted (summary only; md lists counts, not every id — json keeps entries). (my call — keeps committed report small.) New statuses: `NotSelected`, `SelectedMissing`. Auto-included types reported Wrapped with reason `auto-included: needed by <id>`.

## 4. UI — Browser window

Menu `Tools/DandaUtils/Unity Internals/Browser`; button on settings page.

```
Unity Internals Generator                              [Generate]
[search: gameview______________] [Show: All ▾] [Status: All ▾]
Selected: 12 types, 48 members · auto-included: 3 · would generate: 9 files

▼ [~] UnityEditor
   ▼ [■] GameView              class · whole (incl. future)
       [✓] selectedSizeIndex   property · Deferred (step 2)
       [ ] SizeSelectionCallback  method · Deferred (step 2)   ← exception
   ▼ [~] GameViewSizes
       [✓] instance            property · Wrapped
   ▶ [ ] Toolbar
     (auto) GameViewSizeType   enum · needed by GameViewSizes.instance
```

- Tree: namespace path segments → types (nested types under parent) → members. Built from one classification pass of included assemblies (no emission). Uses `UnityEditor.IMGUI.Controls.TreeView` (handles 40k+ rows virtualized). (builder may choose UI Toolkit `MultiColumnTreeView` if it performs; IMGUI TreeView is the default call)
- Checkbox tri-state: empty / partial `~` / whole `■`; member rows plain ✓. Clicking empty type/namespace → whole. Clicking whole → empty (clears rules beneath). Clicking partial → whole. Clicking member under whole → adds exception (and back removes it).
- Search: substring, case-insensitive on type name + member name; matching branches auto-expand; parents of matches shown.
- Filters: Show = All / Selected / Unselected; Status = All / Wrapped / Deferred / Unsupported / Excluded / NotAccessible.
- Row label: name, kind, status (+reason tooltip), "auto" rows for dependency-included, "missing" rows for SelectedMissing ids (listed under a top "Missing selections" node).
- Summary line: selected types/members count, auto-included count, would-generate files count (computed by resolver, no emission).
- Changes mark settings dirty; saved on window close/Generate/explicit Save (debounced); undo not required. (my call)
- Classification cache: built on window open and after domain reload lazily; progress bar while building.

## 5. Constraints

Same as step 1: deterministic committed output, C# 9, project conventions, no new packages, generator independent of output, no git mutations by agents, verification via Rider + Unity compile + EditMode tests.

## 6. Migration

Existing settings have no selection → empty → next Generate deletes current 2141 files (only generator-owned files). Window shows a one-time hint "Nothing selected — tick items to generate". (my call: no auto-seed with "everything")

## 7. Left to builder

Exact class layout (resolver, tree data source, window), id format for namespace nodes, debounce mechanics, test fixture extensions, exact report wording.

## 8. Decisions after builder check

User decided:
- **Dependency beats exception/exclusion — iff needed.** A type that a selected (and otherwise wrappable) member needs is generated even if the user unticked/excluded it, and only for as long as some selected member needs it. UI shows it as "auto (needed by <id>)" even when its own checkbox is unticked. If nothing selected needs it, an unticked type is not generated. ⇒ Pipeline order: resolve selection → compute dependency closure ignoring exceptions for dependency types → classify/emit. Classification must not mark a dependency-needed type `Excluded` just because of a selection exception.

My calls:
- **Old `ExcludedIds` / `ExcludedNamespaces` removed**, folded into `m_Exceptions` (migrate any existing values on load; today both are empty). Settings page and `GenerationInput` drop them; exclusion = selection exception everywhere. `CompileFailed` auto-exclusion (internal to a run) stays as is and is NOT overridden by dependency need — a dependency that fails to compile takes its dependents down with it (reported, cascade reason).
- **Report keeps real status** (`Wrapped/Deferred/…`) per entry plus `"selected": true|false` in json; md summary adds `NotSelected` and `SelectedMissing` counts; md per-status sections list only selected entries (+ SelectedMissing). `NotSelected` is a count/flag, not a `WrapStatus` value; `SelectedMissing` is a new `WrapStatus`.
