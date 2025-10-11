# Prompt 26: Consolidation / Quality Alignment Pass

Status: In Progress (Most core tasks complete; pending report, optional benchmarks, final API lock)
Priority: High
Owner: Core maintainers

## Rationale
Phase 2 deliverables (Prompts 13–25) introduced overlapping tolerance logic, duplicated mapping handling, diverging roadmap tables, and provisional remap rules. This pass reduces drift, prevents silent tolerance regressions, and establishes a stable baseline prior to adding kernel / time model complexity.

## Phase 0: Public API Surface Audit (Completed)
Outcome Summary:
- Public surface pruned to primitives (`BodyId`, `FrameId`, `Duration`, `Instant`, `StateVector`, `Vector3d`) plus `EphemerisService`.
- Baseline analyzer (public API) captured; CI enforcement deferred until end of Prompt 26 to avoid churn.
- XML documentation present for public symbols.

## Objectives
1. Single Source of Truth (SSOT) for numeric tolerances & unit conversion constants (refer to `docs/Tolerances.md`).
2. Consistent documentation with synchronized roadmap status & tolerance narrative (link, not duplicate).
3. Formalized mapping inventory (testpo codes ? NAIF body IDs) with validation test.
4. Central tolerance policy service reused by integration + unit tests (no duplicated literals – all references indirect).
5. Diagnostic artifacts (JSON) for integration comparison statistics (basis for future CI trend graphs).
6. Dead / divergent doc sections reconciled.
7. Remove obsolete Earth/Moon derivation explanations & confirm no residual code path remains.
8. Prepare scaffolding for later prompts (stats + mapping feed regression & metadata enrichment).
9. Minimized, locked public API surface (Phase 0 complete; lock gate added at end).
10. Simplify loading: remove meta-kernel parser indirection; load kernels directly via repeated `EphemerisService.Load` calls (explicit ordering preserved).

## Completion Matrix
| Task | Status | Notes |
|------|--------|-------|
| A1 | ? | `TolerancePolicy` centralised |
| A2 | ? | Literal purge enforced by test search |
| A3 | ? | Tier matrix unit tests (`TolerancePolicyTests`) |
| B1 | ? | `TestData/BodyMapping.json` (Earth/Moon) |
| B2 | ? | Loader in `TestPoParser` |
| B3 | ? | No inline remap logic remains |
| B4 | ? | Validation test (`BodyMapping_Validation`) |
| C1 | ? | Aggregation in `TestPoComparisonTests` |
| C2 | ? | JSON emitted per ephemeris `comparison_stats.*.json` |
| C3 | ? | Schema checks in integration + in-test generation validation |
| D1 | ? | Single authoritative `docs/Tolerances.md` (README links only) |
| D2 | ? | Roadmap mirrored only in root README (others reference) |
| D3 | ? | EMB/EMRAT narrative removed; barycentric chaining note in `SpkDafFormat.md` |
| F1 | ? | Binary search + fallback in `SpkSegmentEvaluator` |
| F2 | ? | Validation `(final - initial +1)` check in real parser |
| F3 | ? | `DafAddress` helper |
| F4 | ? | Clarified control word decoding + unit tests |
| F5 | ? | Cycle guard in barycentric recursion |
| F6 | ? | LINQ hot path removal (`TryResolveBarycentric`) |
| G1 | ? | Segment index with sorted arrays + stop times |
| G2 | ? | Exact boundary fast path in segment index |
| G3 | ? | `TryGetBarycentric` helper exposed internally |
| H1 | ?? Pending | Demo CLI audit (meta-kernel removal reflected) |
| H2 | ?? Pending | Benchmarks project placeholder only |
| H3 | ? | Record boundary + control word + mapping tests added |
| H4 | ? | Legacy Earth/Moon path gone; integration comparison passes tiers |
| H5 | ? | Stats JSON schema validation (integration + test) |
| I1 | ? Optional | Micro benchmark scaffold not added |
| I2 | ? Optional | Warm/cold barycentric benchmark pending |
| J1 | ? | Public types unchanged; existing XML docs retained |
| J2 | ? | README updated (direct Load workflow) |
| J3 | ?? Pending | `docs/RefactorReport_Prompt26.md` final metrics |
| Load Simplification | ? | Meta-kernel parser removed; direct multi-call `Load` pattern documented |
| API Lock | ?? Pending | Add PublicAPI analyzer shipped file at close |

Legend: ? done • ?? in progress/pending finalization • ? optional (not planned in this pass)

## Remaining Action Items (Short List)
1. Finalize diagnostic CLI (public API only; reflect direct kernel loading, no .tm parsing).
2. (Optional) Add micro benchmarks (record selection, barycentric warm vs cold).
3. Complete `docs/RefactorReport_Prompt26.md` with final metrics (J3) post-merge diff.
4. Introduce Public API analyzer baseline lock (shipped/unshipped files) just before tagging completion.
5. CI gate enabling tolerance literal test & stats schema validation.

## Success Metric
Net deletion > addition for duplicated literals & doc redundancy; coverage stable or improved; benchmarks show no >5% regression in state queries; tolerance policy changes auditable via single source file.
