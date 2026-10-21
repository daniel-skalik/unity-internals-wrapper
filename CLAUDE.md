# CLAUDE.md

Unity editor code generator that wraps Unity `internal` APIs into public mirrored types (`WrappedUnityEditor.*`, `WrappedUnityEngine.*`), compiled inside a Unity `InternalsVisibleTo` bridge assembly. Deterministic, reflection-based, no LLM.

## Start here
1. `docs/STATUS.md` — current state, next steps, open questions.
2. `docs/DECISIONS.md` — every decision so far (user vs Claude calls).
3. `docs/ENGINEERING.md` — how the code works (pipeline, rules, selection, Browser, report, settings).
4. `docs/design/` — design docs (base generator, selection filter). `docs/plans/` — implementation plans already executed.

## Layout
- `Assets/UnityInternalsGenerator/` — source (editor asmdef + `Tests/` with `Tests/Fixture`). Moved as-is from the s1-new project; names still `DandaUtils.*` (rename pending, see STATUS).

## Conventions (C#)
- Private fields `m_CamelCase`, static `s_`, consts `c_`; PascalCase types/members; `sealed` classes.
- `== false` instead of `!`; guard clauses; single-line expression bodies for stateless members.
- `using` directives inside the namespace; namespaces mirror folders.
- C# 9 (Unity), no new dependencies.
- Generated output and settings must be deterministic: ordinal sorting, `\n` line endings, invariant culture, no timestamps. Source files LF.

## Working agreements
- Process: brainstorm → written design → plan → subagent-driven execution with per-task review, final review.
- Ask before building features with real caveats/limitations.
- Verification: compile + tests in a Unity project (6000.0.59f2 verified): Rider static analysis first, then Unity compile result, then EditMode tests.
- Git: ask before committing or pushing.
