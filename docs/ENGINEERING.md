# Unity Internals Generator

> **Moved 2026-10-21:** source now lives in this repo at `Assets/UnityInternalsGenerator/` (copied unchanged from s1-new `Assets/DandaUtils/Editor/UnityInternalsGenerator/`). Paths below still use the old s1-new location; names (asmdefs, namespaces `DandaUtils.Editor.UnityInternalsGenerator`, menus `Tools/DandaUtils/...`) are not yet renamed.


> Editor code generator that wraps Unity `internal` APIs of selected engine/editor assemblies as public `Wrapped*` classes, compiled in a host assembly that Unity grants `InternalsVisibleTo`.

**Generator path:** `Assets/DandaUtils/Editor/UnityInternalsGenerator/` (asmdef `DandaUtils.UnityInternalsGenerator.Editor`, Editor-only)
**Output path:** `Assets/WrappedUnityInternals/` (asmdef `Unity.InternalAPIEngineBridge.0NN`, Editor-only, regenerated; `.gitattributes` forces LF)
**Tests:** `Assets/DandaUtils/Editor/UnityInternalsGenerator/Tests/` (asmdef `DandaUtils.UnityInternalsGenerator.Tests`)

`Assets/DandaUtils` is shared across projects; the generator must not rely on project-specific defines.

---

## Bridge mechanism

Unity assemblies (for example `UnityEngine.CoreModule`) carry `[InternalsVisibleTo("Unity.InternalAPI(Editor|Engine)Bridge[Dev].NNN")]`. The generated asmdef is named after one such granted name, so its code can call the internals directly. `Discovery/BridgeAccess.cs`: `GetGrantedFriends`, `Grants`, `ChooseHostName` (picks a name granted by every included assembly and not used by any other asmdef; keeps the persisted choice when still valid). `Discovery/UsedAssemblyNames.cs` collects names already taken (asmdefs under Assets, Packages, Library/PackageCache, plus `CompilationPipeline` assemblies, excluding the generator output asmdef).

The chosen host name is written back to the settings asset (`HostAssemblyName`).

## Pipeline

| Stage | Class (under generator root) | Role |
|-------|------------------------------|------|
| Orchestration | `GenerationCommand.cs` | Resolves sources, picks host, builds `GenerationInput`, runs, writes, refreshes, logs |
| Input | `Model/GenerationInput.cs` | Sources, host, naming, `Selection` (`SelectionRules`; null means `SelectionRules.All`), runner-owned `AutoExcludedIds` |
| Context | `Model/GenerationContext.cs` | Memoized type classification and member plans, conversions, report sink, file-path collision rejection, emitted-set filter (`ApplySelection`, `IsEmitted`) |
| Selection | `Selection/` (`SelectionRules`, `CatalogBuilder`, `Catalog`, `CatalogNode`, `SelectionResolver`, `SelectionResult`, `SelectionEditing`) | Node tree of the classified input, allowlist rules, resolution to the emitted set, tri-state editing |
| Browser | `Browser/BrowserModel.cs`, `Browser/UnityInternalsBrowserWindow.cs` | Search/filter rows (pure) and the IMGUI selection window |
| Type rules | `Rules/TypeRules.cs`, `Rules/TypeClassification.cs` | Decide the status of each candidate type, and emit it (enum mirror, static class, instance wrapper, public-type wrapper) |
| Member rules | `Rules/MemberRules.cs`, `Rules/MemberPlanner.cs` | Per-member status, name collisions, parameter/default/`Caller*` attribute syntax |
| Conversions | `Rules/ConversionRules.cs`, `Rules/Conversion.cs` | Maps internal types to wrapper types, casts, wraps/unwraps |
| Wrapper shape | `Rules/WrapperSyntax.cs` | The names the instance wrappers own (`m_Value`, `Wrap`, `Target`) and the registry location |
| Naming | `Naming/` (`NamingStrategies`, `NamespaceMapper`, `CSharpNames`, `DefaultValueFormatter`) | `Exact` or `PascalCase` member names; namespaces get a `Wrapped` prefix |
| Emission | `Pipeline/WrapperGenerator.cs`, `Emit/CodeWriter.cs`, `Emit/GeneratedFile.cs` | One file per top-level wrapped type plus the wrapper registry; each line is tagged with its owning member id |
| Pre-compile | `Pipeline/RoslynProcessCompiler.cs`, `Pipeline/CompilerReferences.cs` | Out-of-project compile with the bundled Unity `dotnet` and `DotNetSdkRoslyn/csc.dll` (`-preferreduilang:en-US`, 10 min timeout, process-tree kill) |
| Compile loop | `Pipeline/GenerationRunner.cs` | Maps compiler errors back to member/type ids through line owners, auto-excludes them, repeats up to `MaxCompilePasses` |
| Output | `Output/OutputWriter.cs`, `Output/MetaFiles.cs`, `Output/ReportWriter.cs` | Writes only changed files, deterministic `.meta`, deletes stale files, report md/json |

`WrapperGenerator.Generate` is pure (no I/O): classify all, reject path collisions, `CatalogBuilder.Build`, `SelectionResolver.Resolve`, `GenerationContext.ApplySelection`, emit one file per emitted top-level type. Top-level types whose generated paths collide case-insensitively are all dropped and reported `NameCollision` with reason `file path collision: <path>` (`CollidingGroups`, `GenerationContext.RejectPathCollision`); this runs over all wrapped top-level types before the catalog is built (member plans are memoized lazily and must see final classifications), so colliding types are rejected even when not selected. `WrapperGenerator.BuildCatalog(input)` runs the same steps up to the catalog without resolving or emitting (used by the Browser).

## Wrappers for instance types

Internal classes, structs and interfaces become public wrappers holding the real value in an `internal` field `m_Value`
(`Rules/TypeRules.cs`: `WrapperTypeGenerator`, kinds `Class`/`Struct`/`Interface`; the rules are `InstanceClassTypeRule`,
`StructTypeRule`, `InterfaceTypeRule`, with `StaticClassTypeRule` matching only `abstract sealed` classes and
`ClassTypeGenerator` emitting those). Every wrapper also declares `internal static T Wrap(<unity type>)` — internal,
because a public member may not expose an internal type (CS0051).

- **Hierarchy** (class wrappers only): `GenerationContext.WrapperBase` walks the internal base chain and stops at the
  first `IsVisible` base; an emitted internal base found first becomes the wrapper's base (unemitted internal bases are
  skipped). A root wrapper exposes that first public base — unless it is `object`, where a user-defined conversion would
  be illegal — as `public Target` plus an implicit conversion (`GenerationContext.PublicBase`). Every level declares its
  own `m_Value` typed to its own internal type (`internal new readonly …` below the root) and chains `: base(value)`, so
  member access and unwrapping need no casts.
- **Equality** lives on the root wrapper: `Equals(object)`, `GetHashCode()`, `==`/`!=` by underlying instance for
  classes and interfaces, by underlying value for structs. `Wrap(null)` is `null`, so `wrapper == null` reads like the
  original reference.
- **Central Wrap**: `WrappedRuntime/WrapperRegistry.g.cs` (names in `Rules/WrapperSyntax.cs`) holds one
  `Dictionary<Type, Func<object, object>>` factory per emitted class wrapper, keyed by the exact Unity type;
  `public static object Wrap(object)` walks the runtime type's base chain and so returns the most-derived wrapper
  available, which makes `as`/`is` behave. Registrations are split into `AddNN(map)` chunks of 200, ordinal-sorted by
  type id, and each line is attributed to its type id so the compile loop can auto-exclude a type that breaks its own
  registration. The file is omitted when no class wrapper is emitted. A per-wrapper `Wrap` falls back to
  `new T(value)` when the registry returns a wrapper that is not a `T` (a public class between two internal ones makes
  the lower wrapper a root).
- **Structs** keep a mutable `m_Value`, so instance field setters write through, and are passed by value (no boxing).
- **Interfaces** become wrapper *classes* over the interface value — no mirrored interface, no `Target`, no registry
  entry (the runtime type is an implementation class) — so `Wrap` constructs directly. Only members the interface itself
  declares are emitted.
- **Conversions**: `WrapperConversionRule` matches any `GenerationContext.IsInstanceWrapper` type and produces
  `global::WrappedNs.T.Wrap(expr)` inbound and `(expr)?.m_Value` (`(expr).m_Value` for structs) outbound.
- **Members**: instance fields, properties and methods read through `m_Value` instead of the Unity type
  (`MemberSyntax.Owner`) and drop `static`. `ConstructorMemberRule` emits
  `public T(<params>) : this(new global::Ns.T(<args>)) { }`; `IndexerMemberRule` emits `this[…]` over `m_Value[…]`.
  A member that repeats a name (and, where overloadable, a signature) an emitted wrapper base declares gets `new`
  (`GenerationContext.HidesWrapperBaseMember`). The names a wrapper declares itself (`m_Value`, `Wrap`, `Target`,
  `Equals`, `GetHashCode`) are reserved: an otherwise wrapped member with that name is `NameCollision` with reason
  `Name reserved by the wrapper '<name>'`. `Target` counts only where the emitted shape really declares it
  (`WrapperSyntax.IsReserved(name, declaresTarget)` with `GenerationContext.DeclaresTarget`: a class wrapper with a
  public base other than `object`, including the wrappers that inherit it); on a struct wrapper, an interface wrapper or
  a class wrapper over `object` the name stays free. Constructors and indexers carry no member name, so they collide
  only on an identical emitted signature. A wrapped nested type of an instance wrapper is checked against the same
  reserved names (`GenerationContext.ClassifyCore`), so `internal enum Target` inside an internal class that derives
  from a public base is `NameCollision` instead of a nested type that duplicates the wrapper's own `Target`.
- Constructors of abstract types are `Deferred` (`Inheritable (step 6)`) because the wrapper calls the real
  constructor; a parameterless struct constructor is `Unsupported`.
- **`ref struct`s** (`GenerationContext.IsByRefLike`) are `Unsupported` with reason `ref struct` before any type rule
  runs: a ByRefLike value cannot be a wrapper field (CS8345) and cannot be boxed for `Equals(object)`. The same helper
  backs `UnsupportedConversionRule`, which rejects them in parameter and return position.

## Wrappers for public types (internal members of public types)

Most reachable Unity internals are `internal` members of **public** types (`UnityEditor.Menu.AddMenuItem`). A public
type that declares accessible-internal members, or that contains a type needing it as a container, is classified by
`PublicTypeInternalMembersRule` (rule name `PublicTypeInternalMembers`, first in `TypeRules.CreateOrdered`) and emitted
by `PublicTypeGenerator`, which dispatches to the same shape generators internal types use: static class
(`abstract sealed`) to a `public static class` with static forwarding members and no constructor, struct to a struct
wrapper, interface to an interface wrapper class, anything else to a class wrapper. Design:
`docs/design/2026-10-24-step4-public-type-internals-design.md`.

- **Only the internal members are mirrored.** `MemberPlanner.DeclaredMembers` keeps only members matching
  `MemberPlanner.IsInternalMember` (`internal` or `protected internal`) when the declaring type is `IsVisible`; public
  members get no plan, no verdict, no report entry and no catalog node, because they are already reachable without the
  wrapper. `protected` and `private` members wait for step 7. A property with a public getter and an internal setter is
  one member and is emitted whole. An internal constructor becomes a public wrapper constructor.
- **Which public types qualify** (`GenerationContext.IsPublicHost`, memoized, consulted from `IsClassifiable`): it
  declares an accessible-internal member, or it declares a non-visible nested type (internal, protected **or private**,
  compiler-generated ones skipped), or it declares a public nested type that qualifies itself. The last two make the
  type a container, so every nested catalog type hangs under its declaring type. A container with nothing emittable
  produces no file, like an internal class whose members are all private. The rest of `ClassifyCore` runs unchanged, so
  a qualifying public type can still come out `Unsupported` (generic type definition, `ref struct`), `NoBridgeAccess` or
  `Excluded`.
- **`Target` is the type itself**, not the first public base (`GenerationContext.TargetType`), and struct and interface
  wrappers of public types declare it too. It is reserved on them like on any instance wrapper, so an internal member
  named `Target` is `NameCollision`.
- **The conversion runs both ways**: `implicit operator <UnityType>(<wrapper>)` as for internal wrappers, plus
  `implicit operator <wrapper>(<UnityType>)` returning `Wrap(value)`, which only a public type allows. A public
  *interface* gets neither (C# forbids an interface as either operand of a user-defined conversion, CS0552), so its
  wrapper exposes only `Target`.
- **No hierarchy, no registry entry.** Every wrapper of a public type is a root exposing its own `DeclaredOnly`
  members; `WrappedUnityEditor.EditorWindow` does not derive from `WrappedUnityEngine.ScriptableObject`. Reach a base
  type's internals through that base type's own wrapper: the implicit conversions get there in one step
  (`WrappedUnityEngine.ScriptableObject w = myEditorWindow;`). Because these wrappers never inherit one another they
  are left out of `WrapperRegistry` (`WrapperGenerator.EmitRegistry` skips visible types) and their `Wrap` constructs
  directly. Internal types deriving from a public type are unchanged: their wrapper stays a root with `Target` typed to
  that public base.
- **Public types still pass through signatures as themselves** (`PassthroughConversionRule` matches every visible type
  before `WrapperConversionRule`), so wrapping a public type changes no other wrapper's API. The wrapper of a public
  type appears in generated code only as the nesting prefix of a wrapper nested inside it
  (`WrappedUnityEditor.PublicHost.InnerMode`). Consequently a public type in a signature is not a selection dependency:
  `CatalogBuilder.Dependencies` filters on `GenerationContext.NeedsWrapper` (non-visible and `Wrapped`).

## Statuses

`WrapStatus`: `Wrapped`, `Deferred` (planned later: delegates, events, operators, generic methods, arrays/collections/`Nullable`, `ref`/`out`, generic parameters, constructors of inheritable types), `Unsupported` (pointers, `TypedReference`, `ref struct`, generic type definitions, parameterless struct constructors, `VarArgs`, unformattable const values, no rule matched), `Excluded` (`AutoExclusion`, compiler-generated, obsolete-error, static ctor, nested in an excluded type; selection never produces it), `NotAccessible`, `NoBridgeAccess` (id `<Assembly>|*`), `NameCollision`, `CompileFailed` (auto-excluded after pre-compile; reason is `<Code>: <Message>`), `SelectedMissing` (report-only: a selection rule id absent from the current Unity). Nested types hidden by an auto-excluded type read `Declaring type auto-excluded: failed to compile`. Auto-excludes are never persisted.

Ids: `Assembly|Namespace.Outer+Inner|Kind|Name(ParamType,...)` (`Model/MemberId.cs`; type ids are `Assembly|Namespace.Outer+Inner|Type|Inner`). Methods, constructors and indexers carry their parameter types, so the two indexers of one type (both named `Item`) get different ids; a non-indexer property is `…|Property|<Name>` with no parentheses. Namespace node ids are `ns:<FullNamespace>` (assembly-agnostic; global namespace `ns:`), built by `SelectionIds.Namespace`; the parent of `ns:A.B` is `ns:A`, a top-level namespace has no parent.

## Selection

Classification is selection-agnostic; the selection only decides what is emitted.

- **Catalog** (`Selection/CatalogBuilder.cs`, `Catalog.cs`, `CatalogNode.cs`): one node per namespace, classified type (compiler-generated types omitted) and considered member (`GenerationContext.MemberVerdicts`; members only under `Wrapped` non-enum types). `NodeKind` covers `Namespace`, `Class`, `StaticClass`, `Struct`, `Interface`, `Enum`, `Method`, `Constructor`, `Property`, `Indexer`, `Field`, `Const`, `Other` (the Browser only prints the kind). A nested type's parent is its declaring type when that has a node, otherwise its namespace. Each member node carries the ids of the wrapped internal types its signature needs (`DependencyTypeIds`: return/field/property type, method, constructor and indexer parameters). A wrapper's base is not a dependency — the base-chain walk skips unemitted internal bases, so the wrapper compiles on its own. Siblings (`Catalog.ChildrenOf`) form one list sorted by name `OrdinalIgnoreCase`, ties by kind (namespace, type, member) then id ordinal, so namespaces and types interleave.
- **Public types with internal members** are ordinary classified types (see "Wrappers for public types"), so they produce nodes, report entries, selection, dependency auto-inclusion and `SelectedMissing` behaviour through the same paths as internal types; their member nodes are only the accessible-internal ones, and an internal nested type sits under its public declaring type. Example: `UnityEditor.Menu` lists `AddMenuItem`, both `Wrapped`.
- **Rules** (`Selection/SelectionRules.cs`): two normalized id lists, `Included` and `Excluded` (exceptions). `IsSelected` walks the id and its ancestors; the first (deepest) rule found decides; no rule means not selected. Including a namespace or type selects everything beneath it, including members and nested types that appear in later Unity versions. `SelectionRules.All` selects everything (tests, Browser catalog build) and is not editable; `IsEmpty` is true when there are no `Included` ids.
- **Resolution** (`Selection/SelectionResolver.cs` -> `SelectionResult`): emitted members = selected `Wrapped` members; emitted types = selected `Wrapped` enums, plus every type an emitted member's signature needs, plus the declaring-type containers of all emitted members and types. A type needed by an emitted member is emitted even when unticked or excepted (dependency beats exception iff needed); it is not emitted when nothing needs it. Selection exceptions never mark anything `Excluded`, and dependency need never resurrects a `CompileFailed` (auto-excluded) type or member; the cascade is reported. `AutoIncludedBy` maps each emitted-but-unselected type to the ordinal-smallest requiring id (a member or a type id). `MissingIds` = rule ids (included or excepted) absent from the catalog. `EmittedFileCount` = emitted top-level types.
- **Empty classes:** a class with nothing emittable (no emitted members, no emitted nested types) produces no file and, when nested, no nested class. This holds also with `SelectionRules.All`.
- **Empty selection:** nothing is generated; `GenerationCommand` logs a `[UnityInternalsGenerator]` warning pointing at the Browser.
- **Editing** (`Selection/SelectionEditing.cs`): `Set(rules, catalog, id, selected)` clears every rule on or beneath id, then adds a rule on id only when the inherited state differs (no redundant rules); `Toggle` (None/Partial -> selected, Whole -> unselected); `RemoveRule` (drops an id from both lists, used for missing rows); `States` computes `CheckState` (`None`/`Partial`/`Whole`) for every node in one pass. `Set`/`Toggle`/`RemoveRule` throw `ArgumentException` on `SelectionRules.All`.

## Browser window

`Tools/DandaUtils/Unity Internals/Browser` (also the "Open Browser" button on the settings page) opens `Browser/UnityInternalsBrowserWindow.cs` (title "Unity Internals"). On open and on Refresh it builds the catalog (`GenerationCommand.CreateInput(out error, persistHost: false)`, so opening the Browser never writes a changed `HostAssemblyName`; selection replaced by `SelectionRules.All`, then `WrapperGenerator.BuildCatalog`) under a progress bar on the next editor tick; the catalog is not persisted (session-only fields are `[NonSerialized]`, because the domain-reload backup of an `EditorWindow` also restores private fields), so every domain reload rebuilds it. Per change the window calls `SelectionEditing.States` and `SelectionResolver.Resolve` once and rebuilds the rows from `BrowserModel.Rows` (`Browser/BrowserModel.cs`: case-insensitive search over type and member names, Show `All`/`Selected`/`Unselected`, Status filter; a node is visible when it or a descendant passes; without a search rows follow the expanded set kept in the serialized `TreeViewState`).

- Search: a query without `.` matches names containing it. A query with `.` is split at the last `.`: the name must contain the part after it (empty matches any name) and the node's parent display path (namespace segments plus the declaring type chain, e.g. `UnityEditor.Toolbar` for a member of `Toolbar`; lowercased once in the `BrowserModel` constructor) must end with the part before it on a segment boundary. `UnityEditor.Toolbar` finds the `Toolbar` type, `Menu.AddMenuItem` finds `AddMenuItem` under `UnityEditor.Menu`.
- In search mode, matches and their ancestors are shown; ancestors on a match path are always open and have no foldout (`BrowserRow.CanExpand` false, `BrowserTreeView.CanChangeExpandedState`). A matched container that is expanded in the real `TreeViewState` also shows all its children (not filtered by the search, still by Show/Status), recursively following the expanded set; collapsed, it shows only its match paths. `BrowserRow.HasChildren` is true whenever the row can show children, so foldouts appear in both modes.

- Toolbar: search field (rows rebuilt 150 ms after the last keystroke via `EditorApplication.update`), Show popup, Status popup (`All`, `Wrapped`, `Deferred`, `Unsupported`, `Excluded`, `NotAccessible`, `NameCollision`), Refresh, Generate.
- Summary line: `Selected: {types} types, {members} members · auto-included: {n} · missing: {m} · would generate: {files} files` (selected type / member nodes of `SelectionResult.SelectedIds`, `AutoIncludedBy.Count`, `MissingIds.Count`, `EmittedFileCount`).
- Rows: tri-state checkbox (`Partial` drawn via `EditorGUI.showMixedValue`), name, kind, status (reason as tooltip), suffix `auto · needed by <name>` for `AutoIncludedBy` types. A checkbox change calls `SelectionEditing.Set` with the already computed state (same rule as `Toggle`) and hands keyboard focus back to the tree. A top pseudo-node "Missing selections" lists `MissingIds` (status `missing selection` or `missing exception`), all drawn ticked; unticking one calls `SelectionEditing.RemoveRule`.
- Empty selection shows the help box "Nothing selected — tick items to generate".
- Every checkbox change and missing-row removal is stored at once via `UnityInternalsGeneratorSettings.SetSelection` + `Save` (no Save button, no unsaved state), so Generate from the menu or settings page always sees the current selection. Generate runs `GenerationCommand.Execute` on the next editor tick. Open and Refresh re-read the rules from the settings.
- Tree int ids are catalog node index + 1 (stable for the same catalog, so expansion survives rebuilds); the pseudo-node rows use ids from `int.MaxValue` downwards.

## Settings

`ProjectSettings/UnityInternalsGeneratorSettings.asset` (`Settings/UnityInternalsGeneratorSettings.cs`, `ScriptableSingleton`; UI in Project Settings via `UnityInternalsGeneratorSettingsProvider`). Fields: `OutputFolder` (default `Assets/WrappedUnityInternals`), `HostAssemblyName`, `NamingStyle` (`Exact` default), `IncludedAssemblies` (default `UnityEditor.CoreModule`, `UnityEngine.CoreModule`), `IncludedIds` and `ExcludedIds` (the selection; hidden on the settings page, which shows their counts and an "Open Browser" button), `MaxCompilePasses` (default 5). `Selection` builds a cached `SelectionRules` from the two lists; `SetSelection(rules)` stores them normalized (distinct, non-empty, ordinal-sorted) and marks the asset dirty; `Save()` persists.

Migration (`Settings/SettingsMigration.cs`, run from `OnEnable`): the legacy hidden `ExcludedNamespaces` list is moved into `ExcludedIds` as `ns:` ids and cleared; both id lists are normalized; the asset is saved only when something changed. Legacy `ExcludedIds` keep their ids and now act as selection exceptions (they no longer mark anything `Excluded`).

## Menu

`Tools/DandaUtils/Unity Internals/` (`GeneratorMenu.cs`): `Generate`, `Browser`, `Open Report`, `Settings`. On domain load the menu class logs the pending summary stored in `SessionState` key `DandaUtils.UnityInternalsGenerator.PendingSummary` (the refresh reloads the domain, so the summary is also logged immediately before) and warns when `GenerationInfo.json` was produced by a different Unity version.

## Regenerating

Run `Tools/DandaUtils/Unity Internals/Generate`. On pre-compile failure nothing is written; the report goes to `Temp/UnityInternalsGenerator/_LastFailedReport.md`. Recovery after a Unity upgrade when the output no longer compiles: delete `Assets/WrappedUnityInternals`, let Unity recompile, then Generate. Safe Mode does not load the generator, so do this outside Safe Mode.

## Output files

`_GenerationReport.md` and `_GenerationReport.json` (every considered type/member with its real status, reason and `selected` flag, plus one entry per missing rule id: `SelectedMissing` with reason `selected id not found in current Unity` or, for a missing exception id, `exception id not found in current Unity`; but when the id is a member whose declaring type is in the catalog and not `Wrapped` (its members vanished, e.g. after a compile auto-exclusion), the entry is `Excluded`, selected, reason `Declaring type auto-excluded: failed to compile` (declaring chain auto-excluded) or `Declaring type is <Status>` (`WrapperGenerator.MissingEntry`, `MemberId.DeclaringTypeId`)). The json lists only selected entries plus a top-level `notSelectedCount`; the md is unaffected. `selected` is true when the selection picks the id or the id is auto-included (an auto-included entry without a reason reads `auto-included: needed by <id>`); catalog-absent compiler-generated entries are never selected; `CompileFailed` entries always read selected (only emitted ids reach the compiler). The md count table (`ReportWriter.StatusCounts`, also used by the console summary of `GenerationCommand`) counts selected entries per status plus a `NotSelected` row, and the md per-status sections list only selected entries, `GenerationInfo.json` (`generatorVersion`, `unityVersion`, `host`), `WrappedUnityInternals.asmdef`, `<WrappedNamespace>/<Type>.g.cs`, and `WrappedRuntime/WrapperRegistry.g.cs` when any class wrapper is emitted.

## Determinism

Types sorted by FullName ordinal, members by metadata token, entries by id then rule, files by ordinal path; no timestamps in sources or reports; `-deterministic` and an English csc locale keep committed report text stable; `.meta` content comes from `MetaFiles`; `OutputWriter` rewrites only changed files. Two consecutive runs produce byte-identical output.

## Known caveats

- Struct-typed fields (static and instance): the wrapper property returns a copy; writes to struct members through it do not reach the original.
- Internal interfaces are wrapper classes, so consumers cannot implement them (that is step 6), and members a derived interface inherits are not re-emitted on its wrapper.
- Members of instance wrappers named `m_Value`, `Wrap`, `Target`, `Equals` or `GetHashCode` are dropped as `NameCollision` rather than renamed. A wrapper of a public type always declares `Target`, so an internal member of a public type with that name is unreachable.
- A class wrapper stores one reference per hierarchy level (`m_Value` per level).
- `using UnityEditor;` together with `using WrappedUnityEditor;` makes same-named types ambiguous (CS0104); qualify or alias (`using WrappedEditorWindow = WrappedUnityEditor.EditorWindow;`). Since step 4 the colliding names include the most familiar Unity types, because a public type's wrapper carries that type's own simple name. Accepted by design (A2/A3): a disambiguating suffix would break the identical-names promise.
- About 15 types end `CompileFailed` because their signatures reference Bee/Cecil assemblies that are not in the reference set.
- `Caller*` attributes are copied onto wrapper parameters, so the caller info reflects the wrapper caller.
