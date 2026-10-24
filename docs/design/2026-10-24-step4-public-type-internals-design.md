# Step 4 — internal members of public types

Date: 2026-10-24 · Implements §4 "Internal members of public types" and §8.4 of `2026-10-20-generator-design.md`, decision B16 · Issue BRN-390

## Intent

`UnityEditor.Menu.AddMenuItem`, `UnityEditor.EditorWindow.m_Parent`, `UnityEngine.GUIUtility.s_EditorScreenPointOffset` —
most of Unity's reachable internal API is not in internal *types* at all, it is `internal` members of **public** types.
Step 1–2 never classified a public type, so the Browser lists these as `Deferred`
`PublicTypeInternalMembers (step 4)` (B16) and nothing is generated. This step turns them into real wrappers.

## Shape

A public Unity type that declares accessible-internal members gets a wrapper in the mirrored namespace that mirrors
**only those members** — its public members stay where they already are, reachable without the wrapper:

```csharp
namespace WrappedUnityEditor
{
    public class EditorWindow
    {
        internal readonly global::UnityEditor.EditorWindow m_Value;

        internal EditorWindow(global::UnityEditor.EditorWindow value) { m_Value = value; }

        internal static EditorWindow Wrap(global::UnityEditor.EditorWindow value) => value == null ? null : new EditorWindow(value);

        public global::UnityEditor.EditorWindow Target => m_Value;

        public static implicit operator global::UnityEditor.EditorWindow(EditorWindow wrapper) => wrapper?.m_Value;

        public static implicit operator EditorWindow(global::UnityEditor.EditorWindow value) => Wrap(value);

        public override bool Equals(object obj) => ...   // by underlying instance, as for internal wrappers

        public global::UnityEngine.Vector2 GetWindowSize() => m_Value.GetWindowSize();   // an internal member
    }
}
```

The shape is the one step 2 already emits, with three differences, all of them because the wrapped type is public:

1. **`Target` is the type itself**, not the first public base: a public type is its own first public type. Struct and
   interface wrappers of public types declare it too — unlike internal ones, where a struct has no public base and a
   class over `object` gets nothing (D3).
2. **The conversion runs both ways.** The wrapper → Unity operator is the step 2 one; Unity → wrapper is new and only
   possible here, because the operand is a public type. It is `Wrap(value)`, so `null` stays `null`.
   An *interface* gets neither: C# forbids an interface as either operand of a user-defined conversion (CS0552), so a
   public interface's wrapper exposes only `Target`.
3. **No registry entry.** `WrapperRegistry` exists to hand back the most-derived wrapper for a runtime type (D5);
   wrappers of public types never inherit one another (see "no hierarchy" below), so a registration could only ever
   return a wrapper the caller cannot use. Their `Wrap` constructs directly, like an interface wrapper's.

Shapes by kind, exactly as for internal types: static class (`abstract sealed`) → `public static class` with static
forwarding members and no constructor; struct → struct wrapper; interface → interface wrapper class; anything else →
class wrapper. A public type whose internal members are all static but which is not a `static class` still gets the
class-wrapper shape — the static members are emitted `public static` and are reachable as
`WrappedUnityEditor.Menu.AddMenuItem(...)`; there is no public constructor unless the type declares an internal one.

An internal constructor of a public type becomes a public wrapper constructor, so `new WrappedUnityEditor.X(...)` builds
a real `UnityEditor.X` through its internal constructor.

### No hierarchy between wrappers of public types

`WrappedUnityEditor.EditorWindow` does **not** derive from `WrappedUnityEngine.ScriptableObject`, even when both are
wrapped. Every wrapper of a public type is a root exposing only its own `DeclaredOnly` members, which is what the rest
of the generator already does for internal types (D11). The base type's internal members are reached through the base
type's own wrapper, and the implicit conversions get you there in one step:
`WrappedUnityEngine.ScriptableObject w = myEditorWindow;` (standard implicit conversion `EditorWindow → ScriptableObject`
followed by the user-defined one). Mirroring the chain would mean `Target` and the conversions repeated with `new` at
every level, and conversion-resolution ambiguities to reason about, for no API that is not already reachable.

Internal types that derive from a public type are unchanged: their wrapper stays a root with `Target` typed to that
public base (step 2), *not* the public base's wrapper.

## Classification

Today `GenerationContext.IsClassifiable` rejects every `IsVisible` type, and `CatalogBuilder` bolts the public-type
nodes on afterwards with a hardcoded `Deferred` status and its own member scan. This step deletes that second path:
public types that qualify become ordinary classified types, so the catalog, the report, the member planner, the
selection resolver and the Browser see them through the paths they already use.

**A public type qualifies** (`GenerationContext.IsPublicHost`, memoized) when it

- declares an accessible-internal member (`internal` or `protected internal`; `MemberPlanner.IsInternalMember`), **or**
- declares a nested type that is not visible (internal, protected, private — compiler-generated ones skipped), **or**
- declares a public nested type that itself qualifies.

The last two make the declaring type a **container**, so a nested catalog type always hangs under its declaring type.
Including *private* and *protected* nested types here is what fixes the known issue "private/protected nested types of
public types parented inconsistently in the catalog": they are `NotAccessible` nodes whose parent node used to be
missing, so they were parented to their namespace instead. A container that has nothing emittable produces no file,
exactly as an internal class whose members are all private does.

Everything else in `ClassifyCore` runs unchanged, so a qualifying public type can also come out
`Unsupported` (generic type definition, `ref struct`), `NoBridgeAccess` (its assembly grants nobody) or `Excluded`
(compiler-generated, auto-excluded) — all more accurate than today's blanket `Deferred`.

The `declaring.IsVisible → Deferred "PublicTypeInternalMembers (step 4)"` branch for internal types nested in public
types is removed; they now take the ordinary nested path (`Declaring type is <status>` when the public declaring type
is not `Wrapped`), and are emitted nested inside the public type's wrapper by `TypeBodies.EmitNested`.

**Rule.** One type rule, `PublicTypeInternalMembers`, matching any visible non-enum type, placed first; its generator
dispatches to the shape generator the type needs. One rule name in the report for the whole case.

**Members.** `MemberPlanner.DeclaredMembers` keeps only accessible-internal members when the declaring type is visible.
Public members get no plan, no verdict, no report entry and no catalog node — the same set the old
`CatalogBuilder.InternalMembers` listed. A property with a public getter and an internal setter is one member and is
emitted whole (the getter is needed for the setter to be usable).

**Conversions are unchanged**: `PassthroughConversionRule` matches every visible type before `WrapperConversionRule`
does, so a public type in a signature stays itself. That is the point — a wrapper of a public type is a way to reach
hidden members, not a new currency. The only place the wrapper of a public type appears in generated code is as the
nesting prefix of a wrapper nested inside it (`WrappedUnityEditor.PublicHost.InnerMode`).

Consequently a public type in a member signature is **not** a selection dependency: `CatalogBuilder.Dependencies` now
filters on `GenerationContext.NeedsWrapper` (non-visible and `Wrapped`) instead of `IsWrappedType`, so selecting
`Menu.AddMenuItem` does not drag in a wrapper nobody references.

## Name ambiguity (accepted, §4 of the design doc)

`WrappedUnityEditor.EditorWindow` has the same simple name as `UnityEditor.EditorWindow`, so
`using UnityEditor; using WrappedUnityEditor;` together makes `EditorWindow` ambiguous (CS0104). Consumers qualify or
alias (`using WrappedEditorWindow = WrappedUnityEditor.EditorWindow;`). This was accepted when the mirrored-namespace
rule was chosen (A2) and is not worked around: any disambiguating suffix would break the "identical names and
signatures" promise (A3). Step 4 is where it starts to bite, because the colliding names are now the names of the most
familiar Unity types.

## Statuses, report, selection

- Qualifying public types and their internal members are `Wrapped` with rule `PublicTypeInternalMembers` and report
  entries like any other type. The string `PublicTypeInternalMembers (step 4)` disappears from the code base.
- `SelectedMissing` for a member of a public type that a newer Unity dropped now works by construction: the known issue
  ("reads `Declaring type is Deferred`") was caused by the declaring type node being permanently `Deferred`.
  `WrapperGenerator.MissingEntry` falls back to the declaring-type reason only when the declaring type is **not**
  `Wrapped`, which no longer happens for a public host, so the entry is `SelectedMissing` with
  `selected id not found in current Unity`.
- Reserved wrapper names (D8) apply to instance wrappers of public types as well, `Target` included (they declare it).
  A public type with an internal member named `Wrap`, `Target`, `m_Value`, `Equals` or `GetHashCode` reports
  `NameCollision`, it is not renamed.

## Out of scope

Protected and private members of public types (step 7 — `protected internal` is in, it really is internal-accessible),
inheritable proxies (step 6), widening the default assembly include (step 5), mirroring base chains between wrappers of
public types.
