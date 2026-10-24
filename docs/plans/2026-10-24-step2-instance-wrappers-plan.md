# Step 2 implementation plan

Design: `docs/design/2026-10-24-step2-instance-wrappers-design.md`. Executed 2026-10-24.

| # | Task | Files | Verified by |
|---|---|---|---|
| 1 | Fixture: 3-level hierarchy under a public base, struct with state, interface + implementation, abstract type, reserved-name type, members crossing the boundary | `Tests/Fixture/FixtureTypes.cs` | compiles; later tests |
| 2 | `Conversion` gains suffixes; `WrapperConversionRule`; `Fallback` stops deferring wrapped types | `Rules/Conversion.cs`, `Rules/ConversionRules.cs`, `Rules/WrapperSyntax.cs` | `ClassificationTests.Convert_*` |
| 3 | Context: `IsInstanceWrapper`, `WrapperBase`, `PublicBase`, `HidesWrapperBaseMember`; struct/interface pre-checks dropped | `Model/GenerationContext.cs` | `ClassificationTests.Types_*` |
| 4 | Type rules + generators: `StructTypeRule`, `InterfaceTypeRule`, `InstanceClassTypeRule`, `WrapperTypeGenerator`; `StaticClassTypeRule` narrowed to real static classes | `Rules/TypeRules.cs` | `EmissionTests` (struct exact content, hierarchy, registry) |
| 5 | Member rules: instance modifiers/access, `ConstructorMemberRule`, `IndexerMemberRule`, `new` hiding | `Rules/MemberRules.cs` | `EmissionTests` |
| 6 | Planner: instance members allowed, ctor/indexer checks, reserved names, signature-only collisions for ctors/indexers | `Rules/MemberPlanner.cs` | `ClassificationTests`, `CatalogTests` |
| 7 | `WrappedRuntime.WrapperRegistry` emission (chunked, ordinal, per-type line owners) | `Pipeline/WrapperGenerator.cs` | `EmissionTests` |
| 8 | Catalog: new node kinds, ctor/indexer dependencies | `Selection/CatalogNode.cs`, `Selection/CatalogBuilder.cs` | `CatalogTests`, `SelectionResolverTests` |
| 9 | Keep-output mode for the Roslyn compiler; runtime acceptance tests (compile fixture wrappers, load, exercise Wrap/equality/hierarchy/struct copy/ctor/indexer/interface) | `Pipeline/RoslynProcessCompiler.cs`, `Tests/WrapperRuntimeTests.cs` | the tests themselves |
| 10 | Real run over the two CoreModules (explicit test) | `Tests/RealRunTests.cs` | run manually, counts in the summary |
| 11 | Docs: STATUS step 2 done, ENGINEERING rules/statuses, DECISIONS rows | `docs/*` | review |

Review after each task: read the diff against the design, run the affected EditMode tests.
