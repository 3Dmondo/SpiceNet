# Consolidation

Status: In Progress (Most core tasks complete; pending report, optional benchmarks, final API lock)
Priority: High
Owner: Core maintainers

## Rationale
Consolidation reduces drift, prevents silent tolerance regressions, and establishes a stable baseline prior to adding kernel / time model complexity.

## Objectives
1. Single Source of Truth (SSOT) for numeric tolerances & unit conversion constants (refer to `docs/Tolerances.md`).
2. Consistent documentation without duplication.
3. Formalized mapping inventory with validation tests.
4. Central tolerance policy reused by integration + unit tests.
5. Diagnostic artifacts (JSON) for integration comparison statistics.
6. Dead / divergent doc sections reconciled.
7. Obsolete narratives removed; barycentric chaining note moved to `SpkDafFormat.md`.
8. Prepare scaffolding for later prompts (stats + mapping feed regression & metadata enrichment).
9. Minimized, locked public API surface.
10. Simplify loading: direct `EphemerisService.Load` calls.

## Remaining Action Items (Short List)
1. Finalize diagnostic CLI (public API only; direct kernel loading).
2. (Optional) Add micro benchmarks.
3. Complete `docs/RefactorReport_Prompt26.md` with final metrics post-merge diff.
4. Introduce Public API analyzer baseline lock (shipped/unshipped files) just before tagging completion.
5. CI gate enabling tolerance literal test & stats schema validation.
