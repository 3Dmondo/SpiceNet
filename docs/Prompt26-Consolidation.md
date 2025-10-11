# Prompt 26: Consolidation / Quality Alignment Pass

Status: Planned
Priority: High (execute before expanding feature surface beyond Prompt 25)
Owner: Core maintainers

## Rationale
Phase 2 deliverables (Prompts 13–25) introduced overlapping tolerance logic, duplicated documentation of AU/EMRAT handling, partially diverging roadmap status tables, and provisional mapping rules (testpo code ? NAIF ID). A focused consolidation pass reduces drift, prevents silent tolerance regressions, and establishes a stable baseline prior to adding new kernel / time model complexity.

## Phase 0: Public API Surface Audit (Precedes All Other Work)
Goal: Minimize and lock the externally supported API before deeper consolidation so later internalization does not create churn.

Tasks:
1. Inventory Current Public Surface
   - Script: reflection over each runtime assembly (excluding test/bench) capturing: namespace, type (class/struct/record/interface), member kind, accessibility.
   - Output JSON: `artifacts/api-scan.json` (checked into repo for diff in PR).
2. Classify Symbols
   - Categories: Facade (must stay public), Value Primitive, Parsing Entry Point, Internal Helper (candidate for `internal`), Accidental Exposure (should be removed or internalized).
   - Produce `docs/PublicApiClassification.md` summarizing decisions table.
3. Internalize
   - Change accessibility of non-facade types to `internal` (keep value structs like `Vector3d`, `BodyId`, `Instant`, `StateVector`, and `EphemerisService` public; others by decision).
   - Add `InternalsVisibleTo` for `Spice.Tests`, `Spice.IntegrationTests`, `Spice.Benchmarks` only if required.
4. Introduce Public API Baseline Analyzer
   - Add Roslyn analyzer package (Microsoft.CodeAnalysis.PublicApiAnalyzers) via central package management.
   - Generate `PublicAPI.Shipped.txt` & `PublicAPI.Unshipped.txt` in each packable project (initial content = post-pruning surface).
5. Documentation Update
   - Add README section: "Supported Public Surface" referencing the baseline + stability policy (semantic versioning statement placeholder).
6. Verification
   - Build with analyzers: zero new public API warnings after baseline capture.
   - Tests compile & pass referencing internalized members through `InternalsVisibleTo`.
7. Lock
   - Add CI step to fail if public surface changes without updating PublicAPI files.

Exit Criteria:
- No accidental parsing/IO helpers left public.
- Analyzer baseline committed; subsequent changes require explicit approval.
- Classification doc explains rationale for any temporarily public items that might be internal later.

## Objectives
1. Single Source of Truth (SSOT) for numeric tolerances & unit conversion constants.
2. Consistent documentation (root README, per-project READMEs, copilot-instructions manifest) with synchronized roadmap status & tolerance narrative.
3. Formalized mapping inventory (testpo codes ? NAIF body IDs) with validation test.
4. Central tolerance policy service reused by integration + unit tests (no duplicated literals).
5. Diagnostic artifacts (JSON) for integration comparison statistics (basis for future CI trend graphs).
6. Dead / divergent doc sections reconciled (e.g., 40x vs 2xx relaxation discrepancies).
7. Lightweight quality gates (analyzers / style) added where gaps exist.
8. Remove obsolete Earth/Moon EMB + EMRAT special-case documentation & confirm no residual code path remains.
9. Prepare scaffolding for later prompts (stats + mapping feed golden regression & metadata enrichment).
10. Minimized, locked public API surface (Phase 0) to reduce long-term maintenance.

## Scope (In / Out)
In:
- Refactors limited to test harness, docs, internal constant centralization.
- Non-breaking public API clarifications (XML docs) where missing.
- Build scripts / CI additions strictly for validation (fail on unsynchronized docs?).
Out:
- New kernel types, new time models (remain in future prompts).
- Performance micro-optimizations (Prompt 25 domain) unless required by refactor side-effects.

## Work Breakdown
A. Tolerance & Constants Unification
  A1. Introduce internal static class `Spice.Core.Constants` exposing: `AstronomicalUnitKm`, `SecondsPerDay`, `AuPerDayToKmPerSecFactor`, maybe `J2000EpochTdbSeconds`.
  A2. Add internal `TolerancePolicy.Get(ephemerisNumber, hasAuConstant)` returning (posAu, velAuPerDay) & derived km/km/s conversions.
  A3. Replace literals (`1e-13`, `1e-16`, `1e-9`, `1e-12`, `1e-8`, `1e-11`, `1e-7`, `1e-10`) across tests & integration harness.
  A4. Add unit tests targeting `TolerancePolicy` matrix: combinations of (hasAU true/false, ephemeris prefix 2xx / 40x / other).

B. Mapping Inventory
  B1. Create `Spice.IntegrationTests/TestData/BodyMapping.json` entries: `{ "testpo": 3, "naif": 399, "rationale": "Earth mapping" }`, Moon etc. (extendable).
  B2. Implement loader `TestPoBodyMapping.Load()` returning dictionary; integrate into testpo parser normalization path.
  B3. Remove inline remap logic (3?399,10?301) from any code (verify none remain after edit) & update docs.
  B4. Add validation test: duplicate testpo codes, missing required Earth/Moon pairs fail.

C. Stats Artifact
  C1. Extend comparison harness to aggregate per-ephemeris metrics: max/mean position error (AU), max/mean velocity error (AU/day), sample count, strictMode flag, ephemerisNumber.
  C2. Serialize to `TestData/cache/de<eph>/comparison_stats.<eph>.json` (overwrite each run). Keep deterministic ordering & 3 decimal scientific formatting.
  C3. Add schema test ensuring keys: `ephemeris`, `samples`, `strictMode`, `positionMaxAu`, `positionMeanAu`, `velocityMaxAuDay`, `velocityMeanAuDay`, `generatedUtc`.

D. Documentation Synchronization
  D1. Author single canonical tolerance section snippet in `docs/Tolerances.md` and include (verbatim copy) in root README, IntegrationTests README, manifest.
  D2. Add doc sync test: compute SHA256 of normalized snippet vs embedded copies.
  D3. Harmonize roadmap tables; ensure Prompt 26 present everywhere.
  D4. Remove all EMB/EMRAT historical explanatory blocks; add short note: "Previously documented EARTH/MOON EMB + EMRAT derivation removed; generic barycentric chaining now used.".

E. Analyzer / Quality Gate
  E1. Enable `#nullable enable` in each project (add to top-level `Directory.Build.props` or individual csproj) & address new warnings minimally (suppress or fix where trivial).
  E2. Introduce simple CI script (test) that parses each README roadmap table into JSON and asserts equality of prompt set + statuses.
  E3. Add guard test scanning repository for forbidden tolerance literals outside `TolerancePolicy`.

F. Kernel & Evaluator Micro Refactors (Correctness / Clarity – Non-breaking)
  F1. `SpkSegmentEvaluator`: replace linear record search with binary search over `RecordMids` using uniform spacing assumption (or fallback to linear if radii non-uniform). Provide benchmark to confirm neutral impact for small N.
  F2. Add explicit check ensuring `RecordCount * RecordSizeDoubles + 4 == totalDoubles` (redundant defensive validation optionally in parser).
  F3. Expose helper `DafAddress.ToByteOffset(long wordAddress)` centralizing 1-based to byte computation (used in parser & data source) to reduce duplication.
  F4. `FullDafReader.ReadControlWord` – clarify mixed synthetic/double logic; add explicit branch & test cases for: (a) native double int representable, (b) synthetic 32-bit, (c) invalid large double -> throw.
  F5. Add guard in barycentric recursion (`EphemerisService.TryResolveBarycentric`) tracking visited centers to avoid theoretical cycles.
  F6. Replace repeated LINQ in hot paths (`_segments.Where(...).OrderBy(...)`) with pre-built lookup or simple loops; micro-benchmark after.

G. Service & Index Improvements
  G1. `EphemerisService` segment index: store per key sorted array plus separate array of stop times enabling single pass bound check.
  G2. Add fast path for exact segment start boundary ET (BinarySearch equality branch exit early).
  G3. Provide `TryGetBarycentric(BodyId body, Instant t, out StateVector)` public convenience (wraps internal barycentric path) for diagnostics/tests.

H. Testing Enhancements
  H1. Add unit tests for: segment record boundary selection (et exactly mid±radius), tolerance policy matrix, mapping loader, control word decoding edge cases.
  H2. Add integration test verifying removal of EMB/EMRAT logic does not regress Earth/Moon testpo deltas beyond legacy tolerance.
  H3. Add snapshot test capturing stats JSON structural stability (ignore numeric values, assert presence & type).

I. Benchmark Updates (Optional but Recommended)
  I1. Add benchmark for new binary search record selection vs old linear.
  I2. Add benchmark for `EphemerisService` barycentric retrieval (warm cache vs cold) to watch for regressions after cycle guard addition.

J. Housekeeping
  J1. Ensure all public types touched receive/retain XML docs after refactor.
  J2. Update root README checklist to mark mapping/tolerance centralization once merged.
  J3. Add `docs/RefactorReport_Prompt26.md` summarizing changes & metrics (lines removed, tests added) generated manually.

## Code Review Findings (Summary ? Drives Above Tasks)
- Duplicated numeric tolerance literals across docs & tests (A, D, E).
- Inline mapping (3?399, 10?301) not externalized (B).
- No central constants for AU, seconds/day (A1).
- Record search linear (F1) – acceptable now but easy binary search improvement.
- Potential recursive cycle risk in barycentric resolution if malformed segments (F5).
- Control word heuristic mixes endian pieces; clarify & test (F4).
- Duplicate word->byte offset math scattered (F3).
- Stats artifact & regression harness missing (C).
- Nullability disabled; future correctness aid (E1).
- EMB/EMRAT removed in docs but ensure absence in code (D4, validation search test).
- Public API surface larger than necessary prior to consolidation (Phase 0).

## Acceptance Criteria
- Public API audit completed; classification & baseline analyzer files committed.
- No remaining hard-coded numeric tolerances outside `TolerancePolicy` (search proves single definition path).
- Mapping file drives Earth/Moon remap; removal of old literals verified via search.
- All README tolerance sections identical (byte-for-byte when normalized for line endings) except intentional context preamble differences.
- Integration run produces JSON stats; schema validated by test.
- Roadmap tables synchronized (automated comparison test passes).
- Documentation clearly indicates current parity goals & relaxation pathways.
- Binary search record selection passes existing evaluator tests.
- Control word tests cover synthetic + native double encodings.

## Non-Goals
- Tightening tolerances beyond existing logic.
- Introducing new external dependencies (keep pure BCL unless analyzer already used).
- Large-scale performance rework (reserved for later prompts if needed).

## Risks & Mitigations
| Risk | Mitigation |
|------|------------|
| Hidden duplicate tolerance literal persists | Use solution-wide text search for `1e-13`, `1e-16`, `1e-9`, etc. in CI gate |
| Mapping file drift vs code logic | Single loader + test ensuring every special-case branch references mapping |
| Stats JSON increases CI noise | Gate commit size; keep files small (<5 KB) & optionally gitignore large variants |
| Doc divergence reoccurs | Add roadmap sync test (E2) |
| Binary search incorrect radius assumption | Fallback to linear if radii unequal beyond epsilon |
| Control word heuristic regression | Add explicit regression tests for legacy synthetic test file |
| Over-internalization breaks tests | Use reflection scan after internalization to ensure facade types remain |

## Follow-Up (Outputs Feeding Later Prompts)
- Stats JSON becomes baseline for Prompt 17 golden regression tracking.
- Mapping file forms seed for Prompt 20 (body metadata enrichment).
- Central constants facilitate Prompt 19 advanced time model plugging (shared units).
- Binary search & index enhancements foundation for further segment type support.
- Public API baseline enables stable semantic versioning once initial release prepared.

## Implementation Order Suggestion
0. Phase 0 (Public API Audit & Pruning)
1. A (constants) ? 2. B (mapping) ? 3. D (docs sync) ? 4. C (stats artifact) ? 5. E (gate) ? 6. F/G (refactors) ? 7. H (tests) ? 8. I (benchmarks) ? 9. J (report) ? Final verification.

## Success Metric
Post-consolidation diff shows net deletion > addition for duplicated literals and doc inconsistencies while test coverage unchanged or improved (added mapping & stats tests). Benchmarks show no >5% regression in existing evaluator / state query scenarios. Public API analyzer baseline prevents accidental surface growth.
