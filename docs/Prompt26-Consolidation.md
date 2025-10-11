# Prompt 26: Consolidation / Quality Alignment Pass

Status: In Progress (Phase 0 Completed; proceeding with Tasks A–J; CI lock deferred to final integration)
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

## Scope (In / Out)
In: test harness refactors, docs, internal constant/tolerance centralization, mapping & stats infrastructure, light defensive validation.
Out: new kernel types, new time models, performance micro-optimizations beyond side?effect fixes.

## Work Breakdown
A. Tolerance & Constants Unification
  A1. Introduce internal central constants & `TolerancePolicy` (documented once in `docs/Tolerances.md`).
  A2. Replace literals across tests & integration harness.
  A3. Add unit tests covering tolerance tier matrix (modern / legacy / problematic / fallback).

B. Mapping Inventory
  B1. `Spice.IntegrationTests/TestData/BodyMapping.json` (testpo?NAIF entries: Earth, Moon, extendable).
  B2. Loader to apply remaps during parsing.
  B3. Remove any inline remap logic.
  B4. Validation test for duplicates & required pairs.

C. Stats Artifact
  C1. Aggregate per-ephemeris metrics: position/velocity max & mean (AU / AU/day), sample count, strict flag, ephemeris number.
  C2. Serialize deterministic JSON: `TestData/cache/de<eph>/comparison_stats.<eph>.json`.
  C3. Schema test (presence & type of keys only).

D. Documentation Synchronization
  D1. Canonical tolerance snippet lives solely in `docs/Tolerances.md`; other docs link instead of duplicating.
  D2. Roadmap tables harmonized (single authoritative table in root README; others reference/link or lightweight mirror without tolerance duplication).
  D3. Remove obsolete EMB/EMRAT narrative; add short note referencing generic barycentric chaining.

F. Kernel & Evaluator Micro Refactors
  F1. `SpkSegmentEvaluator` record selection binary search (fallback to linear if spacing non-uniform beyond epsilon).
  F2. Validation check: `RecordCount * RecordSizeDoubles + 4 == totalDoubles`.
  F3. Helper for DAF word?byte conversion to remove duplication.
  F4. Clarify control word decoding branches; add tests (native, synthetic 32-bit, invalid).
  F5. Cycle guard in barycentric recursion (`EphemerisService.TryResolveBarycentric`).
  F6. Replace repeated LINQ in hot paths with simple loops / precomputed lookups.

G. Service & Index Improvements
  G1. Segment index: per key sorted arrays + stop time arrays for O(log n) lookup & bound check.
  G2. Fast path for exact segment boundary epoch.
  G3. Convenience `TryGetBarycentric` for diagnostics/tests.

H. Testing & Tooling Adjustments
  H1. Relocate original diagnostic CLI (previously in `Spice.Benchmarks`) into new `Spice.Console.Demo` project using ONLY public APIs; strip internal segment/comment inspection (future diagnostic surface TBD).
  H2. Convert `Spice.Benchmarks` back to pure BenchmarkDotNet harness (placeholder until performance tasks resume).
  H3. Unit tests: record boundary selection, tolerance tiers, mapping loader, control word edge cases.
  H4. Integration test ensuring removal of legacy Earth/Moon path does not regress deltas beyond legacy tier.
  H5. Snapshot-style structure test for stats JSON (ignoring numeric values).

I. Benchmark Updates (Optional)
  I1. Record selection (binary vs linear) micro-benchmark.
  I2. Barycentric retrieval (warm vs cold) benchmark.

J. Housekeeping
  J1. Ensure public types touched retain XML docs.
  J2. Root README checklist updated (mapping & tolerance centralization complete once merged).
  J3. `docs/RefactorReport_Prompt26.md` summarizing diffs (lines removed, tests added, tolerance literal purge count).

## Code Review Findings Driving Tasks
- Tolerance literals scattered (A, D).
- Inline mapping logic persisted (B).
- Record search linear (F1).
- Potential recursion cycle risk (F5).
- Control word decoding ambiguity (F4).
- Word?byte math duplicated (F3).
- Missing stats artifact (C).
- Need consolidated doc narrative (D).

## Acceptance Criteria
- Single authoritative tolerance snippet only in `docs/Tolerances.md`; other docs link to it.
- No remaining hard-coded tolerance literals outside `TolerancePolicy`.
- Mapping file drives Earth/Moon remap; search confirms absence of inline remap code.
- Stats JSON produced; schema test passes.
- Roadmap tables synchronized (no drift in listed prompts/status where mirrored).
- Binary search record selection passes existing & new evaluator tests.
- Control word tests cover native, synthetic, invalid branches.
- Public API unchanged post Phase 0 until final lock; final gate added at end.
- Diagnostic CLI successfully relocated; benchmarks project free of ad-hoc CLI logic.

## Risks & Mitigations
| Risk | Mitigation |
|------|------------|
| Hidden tolerance literal persists | Repository search in test validating absence outside policy |
| Mapping drift | Central JSON + validation test |
| Stats JSON noise | Keep schema small & deterministic ordering |
| Binary search incorrect radius assumption | Fallback to linear when non-uniform spacing detected |
| Control word regression | Dedicated unit test suite |
| Cycle in barycentric chain | Visited set guard |
| Diagnostic tool needs internal data | Future narrow public diagnostic API rather than internal leakage |

## Follow-Up (Feeds Later Prompts)
- Stats JSON seeds regression tracking.
- Mapping file seeds body metadata enrichment.
- Central constants facilitate advanced time model plug-in.
- Index/evaluator refinements prepare for additional segment types.
- Public API baseline supports future semantic versioning.

## Implementation Order Suggestion
0. Phase 0 (Completed)
1. A (constants/tolerance centralization) ? 2. B (mapping) ? 3. D (doc sync via links) ? 4. C (stats artifact) ? 5. F/G (refactors) ? 6. H (tooling/tests relocation) ? 7. I (benchmarks) ? 8. J (report) ? Final verification & public API lock.

## Success Metric
Net deletion > addition for duplicated literals & doc redundancy; coverage stable or improved; benchmarks show no >5% regression in state queries; tolerance policy changes auditable via single source file.
