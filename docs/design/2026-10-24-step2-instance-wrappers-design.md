# Step 2 — instance class, struct and interface wrappers

Date: 2026-10-24 · Issue: BRN-388 · Builds on `docs/design/2026-10-20-generator-design.md` §4 (rule families, representation decisions) and §8.2, decisions A6/A7/A8.

## Goal

Make instance types first-class: internal classes, structs and interfaces become public wrappers that hold the real
Unity value, their instance members (plus constructors and indexers) are emitted, and any member whose signature
mentions such a type can cross the boundary. Everything step 3–7 covers stays `Deferred`/`Unsupported` with a reason.

## Shapes

### Instance class

```csharp
public class Branch                                         // internal UnityInternalsFixture.Branch : FixtureNode (public)
{
    internal readonly global::UnityInternalsFixture.Branch m_Value;

    internal Branch(global::UnityInternalsFixture.Branch value) { m_Value = value; }

    internal static Branch Wrap(global::UnityInternalsFixture.Branch value) => value == null ? null : global::WrappedRuntime.WrapperRegistry.Wrap(value) as Branch ?? new Branch(value);

    public global::UnityInternalsFixture.FixtureNode Target => m_Value;

    public static implicit operator global::UnityInternalsFixture.FixtureNode(Branch wrapper) => wrapper?.m_Value;

    public override bool Equals(object obj) => obj is Branch other && global::System.Object.ReferenceEquals(m_Value, other.m_Value);

    public override int GetHashCode() => m_Value.GetHashCode();

    public static bool operator ==(Branch left, Branch right) => global::System.Object.ReferenceEquals(left, null) ? global::System.Object.ReferenceEquals(right, null) : left.Equals(right);

    public static bool operator !=(Branch left, Branch right) => (left == right) == false;

    // emitted members, then emitted nested types
}
```

- `Wrap(null)` is `null`, so `wrapper == null` reads like the original reference.
- `Wrap` is **internal**: its parameter is an internal Unity type, and a public member may not expose one (CS0051).
  The public entry point is `WrappedRuntime.WrapperRegistry.Wrap(object)`.
- `==`/`!=`/`Equals`/`GetHashCode` compare the underlying instance and live on the **root** wrapper only, so two
  wrappers of different levels over the same instance still agree.

### Hierarchy

The wrapper base chain is walked over the internal base chain and stops at the **first public base**:

- `b.IsVisible` reached first → root wrapper, with `Target` and an implicit conversion to `b`.
- `b` is an emitted instance wrapper → the wrapper derives from `b`'s wrapper.
- otherwise keep walking, so unemitted internal bases are skipped.

`Target` and the implicit conversion are emitted only when that public base is not `System.Object`: C# forbids a
user-defined conversion to a base class, and `object` is reachable anyway.

Every level declares its **own** `m_Value`, typed to its own internal type (`internal new readonly …` below the root),
and the level ctor chains `: base(value)`. That costs one reference per level and buys correctly typed member access
and unwrapping with no casts anywhere.

### Central Wrap

One generated file, `WrappedRuntime/WrapperRegistry.g.cs`, holding a `Dictionary<Type, Func<object, object>>` with one
entry per emitted class wrapper and:

```csharp
public static object Wrap(object value)
{
    if (value == null) return null;

    for (var type = value.GetType(); type != null; type = type.BaseType)
    {
        if (s_Factories.TryGetValue(type, out var factory)) return factory(value);
    }
    return null;
}
```

Keying on the exact runtime type and walking up the base chain yields the most-derived *available* wrapper, so `as`/`is`
behave. Registrations are split into `AddNN(map)` chunks of 200 so a large real run cannot produce one over-long
initializer. Entries are ordinal-sorted by type id and each line is attributed to its type id, so the
compile-feedback loop can auto-exclude a type that breaks its own registration.

A per-wrapper `Wrap` falls back to `new T(value)` when the registry hands back a wrapper that is not a `T` — that
happens when a public class sits between two internal classes, which makes the lower wrapper a root instead of a
derived one.

### Struct

```csharp
public struct Point
{
    internal global::UnityInternalsFixture.Point m_Value;                     // not readonly: instance field setters write through it

    internal Point(global::UnityInternalsFixture.Point value) { m_Value = value; }

    internal static Point Wrap(global::UnityInternalsFixture.Point value) => new Point(value);

    public override bool Equals(object obj) => obj is Point other && m_Value.Equals(other.m_Value);
    public override int GetHashCode() => m_Value.GetHashCode();
    public static bool operator ==(Point left, Point right) => left.m_Value.Equals(right.m_Value);
    public static bool operator !=(Point left, Point right) => (left == right) == false;
}
```

Value semantics are preserved (one field, no indirection) and the wrapper is passed as a struct — no boxing.

### Interface

An internal interface becomes a **wrapper class** over the interface value, not a mirrored public interface
(decision D4): a mirrored interface could not be implemented by the Unity type, so values crossing the boundary would
need an adapter in both directions, and the "user implements it" direction is the inheritable-proxy work of step 6.
The wrapper is shaped like a root class wrapper, minus `Target`/implicit conversion (C# forbids user-defined
conversions to or from an interface) and minus the registry (the runtime type of the value is an implementation
class, never the interface), so `Wrap` constructs directly.

Only members the interface itself declares are emitted (`DeclaredOnly`, as for classes); inherited interface members
are not.

### Conversion rules

`WrapperConversion` matches any type the context reports as an instance wrapper:

| direction | class / interface | struct |
|---|---|---|
| to wrapped | `global::WrappedNs.T.Wrap(expr)` | `global::WrappedNs.T.Wrap(expr)` |
| to Unity | `(expr)?.m_Value` | `(expr).m_Value` |

`Conversion` gained suffixes (it was prefix-only). The `Fallback` rule no longer defers wrapped types.

### Members

- **Instance** fields, properties and methods differ from the static ones only in the access expression
  (`m_Value.X` instead of `global::Ns.T.X`) and the missing `static` modifier.
- **Constructor** → `public T(<params>) : this(new global::Ns.T(<args>)) { }`. Deferred `Inheritable (step 6)` on
  abstract types (the real ctor cannot be called); `Unsupported` for a parameterless struct ctor.
- **Indexer** → `public V this[<params>] { get => conv(m_Value[args]); set => m_Value[args] = unconv(value); }`.
- **Hiding**: a member of a derived wrapper that repeats a name (and, for methods/indexers, a signature) already
  emitted by an emitted wrapper base gets `new` — Unity types override base internals often, and both wrappers
  forward to the same virtual call, so keeping both is correct.
- **Reserved names**: an otherwise wrapped member of an instance wrapper whose emitted name is `m_Value`, `Wrap`,
  `Target`, `Equals` or `GetHashCode` is reported `NameCollision` ("Name reserved by the wrapper"), because the
  wrapper declares those itself.
- Constructors and indexers have no name of their own, so they collide only on an identical emitted signature.

## Statuses this step changes

| reason | before | after |
|---|---|---|
| `Struct wrapper (step 2)` | type Deferred | Wrapped |
| `Interface wrapper (step 2)` | type Deferred | Wrapped |
| `InstanceMembers (step 2)` | member Deferred | Wrapped (or a later-step reason from its signature) |
| `Indexer (step 2)` | member Deferred | Wrapped |
| `WrapperConversion (step 2)` | member Deferred | Wrapped |
| — | ctor `No member rule matched` (Unsupported) | Wrapped, or `Inheritable (step 6)` on abstract types |

New: `Inheritable (step 6)`, `Parameterless struct constructor`, `Name reserved by the wrapper '<name>'`.
Untouched: delegates, events, operators, generic methods, arrays/collections/`Nullable`, `ref`/`out`, generic
parameters, public types' internal members, privates.

## Catalog / selection

- `NodeKind` gains `Struct`, `Interface`, `Constructor`, `Indexer` (the Browser only prints the kind, so no UI change).
- `DependencyTypeIds` now also covers constructor parameters and indexer index parameters, so selecting such a member
  auto-includes the wrapped types its signature needs.
- A wrapper's base is not a dependency: an unemitted internal base is skipped by the base-chain walk, so the wrapper
  still compiles on its own.

## Out of scope

Delegates/events, arrays, collections, `Nullable`, `ref`/`out`, operators, generic methods (step 3); internal members
of public types (step 4); inheritable proxies (step 6); privates (step 7).

## Caveats (owner, please read)

1. **Struct-typed instance fields** behave like the already-documented static case: the wrapper exposes them as a
   property, so the value is a copy and `wrapper.Corner.X = 1` does not compile (and could not write through).
2. **Interfaces cannot be implemented** by consumers — that is step 6. Today you can only receive and call them.
3. **Inherited interface members are not re-emitted** on a derived interface's wrapper.
4. **One stored reference per hierarchy level** (`m_Value` per wrapper level).
5. Members named `Wrap`/`Target`/`Equals`/`GetHashCode`/`m_Value` on an internal instance type are dropped as
   `NameCollision` rather than renamed.
