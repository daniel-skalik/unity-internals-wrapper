# Step 4 plan — internal members of public types

Design: `docs/design/2026-10-24-step4-public-type-internals-design.md`. Issue BRN-390. Test-first per task.

| # | Task | Files |
|---|---|---|
| 1 | Fixture types: public static/instance/struct host, public-in-public nesting, private-nested-only container, reserved name, internal ctor | `Tests/Fixture/FixtureTypes.cs` |
| 2 | `MemberPlanner.IsInternalMember` + `DeclaredMembers` filters to internal members on visible types | `Rules/MemberPlanner.cs` |
| 3 | `GenerationContext`: `IsClassifiable` admits qualifying public types (`IsPublicHost`, memoized), drop the `NestedInPublicType` branch, `TargetType`/`DeclaresTarget`/`NeedsWrapper` | `Model/GenerationContext.cs` |
| 4 | `PublicTypeInternalMembersRule` + `PublicTypeGenerator`; `WrapperTypeGenerator` emits `Target` and both conversions for public types, struct-safe unwrap, no registry route | `Rules/TypeRules.cs` |
| 5 | `CatalogBuilder`: delete the public-type path and the reason const; `Dependencies` uses `NeedsWrapper` | `Selection/CatalogBuilder.cs` |
| 6 | `EmitRegistry` skips wrappers of public types | `Pipeline/WrapperGenerator.cs` |
| 7 | Tests: classification, catalog parenting, emission (exact file content for static/instance/struct/nested), runtime both-way conversions, selection resolution + `SelectedMissing`, determinism | `Tests/*` |
| 8 | Docs: STATUS step 4 done + known issues, ENGINEERING rule description + caveat, DECISIONS rows | `docs/*` |
| 9 | Verification: EditMode tests, real run over the CoreModules (counts before/after) | — |
