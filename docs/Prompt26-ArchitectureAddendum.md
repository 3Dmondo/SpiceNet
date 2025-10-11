# Prompt 26 – Architecture / Code Quality Addendum

Purpose: Deep-dive review of current codebase focusing on unused / redundant classes, SRP adherence, naming clarity, readability & maintainability gaps surfaced late in Prompt 26. This addendum feeds follow?up cleanups prior to final API lock.

## 1. Redundant / Potentially Obsolete Classes
| Component | Current Role | Overlap / Issue | Recommendation |
|-----------|--------------|-----------------|----------------|
| `DafReader` (Spice.IO) | Synthetic minimal DAF header+summaries reader (Phase 1 fixtures) | Superseded by `FullDafReader` which implements real DAF traversal (file record, summary/name pairs, control words, endianness). Maintaining both increases cognitive load. | Deprecate & remove after ensuring no remaining production usage. Migrate any synthetic tests to build data via `FullDafReader` or direct binary builder helpers scoped to test project. |
| `SpkKernelParser` (synthetic) | Parses simplified synthetic SPK built atop `DafReader` | Real kernels handled by `RealSpkKernelParser`; synthetic parser duplicates segment construction logic & Chebyshev assumptions. | Mark for removal or merge into a unified internal `SpkKernelLoader` with a mode flag (synthetic vs real) if synthetic fixtures still required. Otherwise delete with tests refactored to use real path or direct segment construction factories. |
| `TestPoLoader` | Parses a simplified bespoke testpo variant (BODY= JD= ... format) | Integration test suite already uses richer `TestPoParser` (original line format with component indices). Maintaining two loaders risks divergence. | Consolidate: either (a) extend `TestPoParser` to optionally parse simplified form; then remove `TestPoLoader`, or (b) move `TestPoLoader` into a test-only helper namespace and mark obsolete. |
| `LskParser` | Minimal leap second kernel parser | Feature scope may expand (full NAIF LSK grammar, comments, multiple assignments). Naming acceptable but currently only used internally. | Keep internal. Consider future rename to `LskKernelParser` for parallelism with `RealSpkKernelParser`. Add TODO references for unsupported constructs (DELTET/K, etc.). |
| `RealSpkKernelParser` | Parses real SPK Types 2 & 3 with trailer logic | Name leaks implementation detail (“Real”). As additional types arrive, name becomes misleading. | Rename (internal) to `SpkKernelLoader` or `SpkSpkParser`. Provide facade methods: `Parse(stream)` / `ParseLazy(path)`; mark old type obsolete prior to removal. |
| `SpkSegmentEvaluator` | Evaluates Type 2 / 3 segments (single & multi record) | Single responsibility good, but derivative evaluation & record location intermixed; record locator logic could move to index layer for reuse & test isolation. | Extract `SpkRecordLocator` (internal) containing `LocateRecord` & validation helpers. Keeps evaluator focused on math. |
| `EphemerisService` | Kernel loading (meta-kernel, direct SPK), segment registry, index building, barycentric resolution, caching | Borderline “god class” (multiple responsibilities: I/O, indexing, caching, query orchestration). Unit test surface broadens risk. | Decompose internally (no public surface change):
  - `SegmentIndex` (current `SegmentListIndex` generalized)
  - `BarycentricResolver` (recursion & cache)
  - `KernelLoader` (meta-kernel + LSK parsing)
  Service composes these; improves focused testability & future extension (logging, diagnostics). |

## 2. Unused Code Confirmation
A targeted search shows no production references to:
- `TestPoLoader.Parse` outside test support usage.
- `DafReader` used only by `SpkKernelParser` (synthetic path). 
Thus removal sequence is low risk once synthetic tests migrate.

## 3. Single Responsibility Principle (SRP) Observations
| Area | SRP Status | Detail |
|------|------------|--------|
| `EphemerisService` | Needs decomposition | Combines loading, time scaling side-effect (leap seconds), indexing, resolution, caching. |
| `SpkSegmentEvaluator` | Acceptable but crowded | Record search + Chebyshev evaluation + derivative logic co-located; extraction would clarify concerns. |
| `RealSpkKernelParser` | Acceptable (parsing only) | Could host validation helpers externally to encourage reuse & lighten file. |
| `TolerancePolicy` | Good | Encapsulates tolerance matrix & representation cleanly. |
| `TestPoComparisonTests` | Overloaded test | Performs parsing, aggregation, JSON emission, schema validation, assertion logic in one method. Could factor stats aggregation into a helper to simplify test diff noise and facilitate reuse for future reporting. |

## 4. Naming & Consistency
| Current Name | Issue | Suggested Rename |
|--------------|-------|------------------|
| `RealSpkKernelParser` | “Real” qualifier becomes legacy once synthetic path retired. | `SpkKernelLoader` / `SpkParser` |
| `SpkKernelParser` (synthetic) | Ambiguous vs real parser. | Remove or rename `SyntheticSpkKernelParser` (temporary before deletion). |
| `DafReader` vs `FullDafReader` | Inconsistent scope descriptors (“Full” vs none). | Keep only one: `DafReader` (full implementation) once unified. |
| `Init` / `IntervalLength` in `SpkSegment` | `Init` ambiguous (could read as initialization). | `InitialEpoch` & `RecordIntervalLength` or `InitEpoch`, `IntLenSeconds` (pick consistent explicit style). |
| `RecordSizeDoubles` | Redundant units but clear; OK. | Consider `RecordDoubleCount` (optional). |

## 5. Readability & Maintainability
Strengths:
- Consistent use of immutable records for value types.
- Clear unit annotations in XML summaries.
- Defensive validation around SPK trailers (`expectedPayload + 4`).

Gaps / Improvements:
1. **Magic Numbers**: Control word tolerance `1e-12` scattered; centralize into a private const (`ControlWordIntegralEpsilon`).
2. **Error Messages**: Some parse errors generic (“Invalid coefficient address range”) – append contextual identifiers (segment target/center, addresses) to aid debugging.
3. **Chebyshev Derivative Implementation**: Add brief comment citing mathematical relation (U_k recurrence) for maintainers unfamiliar with Chebyshev polynomial families.
4. **ThreadLocal Scratch**: `SpkSegmentEvaluator` uses `ThreadLocal<double[]>` but never trims; consider array pooling via `ArrayPool<double>` for simpler lifetime management unless proven hot.
5. **Index Rebuild**: `EnsureIndex` performs `GroupBy` (now optimized in parts). After decomposition, incremental update (append-only) could avoid full rebuild when loading additional kernels.
6. **Testing Granularity**: Core logic (LocateRecord, control word parsing) tested indirectly; add narrow unit tests for pure functions for clearer failure localization.
7. **Docs / Code Drift**: `SpkDafFormat.md` mentions potential uniform spacing optimization (index formula) but evaluator uses binary search; add TODO referencing potential consistent INTLEN usage when validated.

## 6. Public Surface Hardening Prior to Lock
Action items before introducing Public API analyzer shipped list:
- Internalize: `SpkSegment`, `SpkKernel`, `SpkSegmentEvaluator`, `RealSpkKernelParser`, `MetaKernelParser`, `LskParser`, `FullDafReader`, `IEphemerisDataSource`, `EphemerisDataSource` factory, `DafCommentUtility`.
- Evaluate `FrameId`: if not required externally yet, internalize until frame transformations shipped.
- Confirm no unintended exposure via `InternalsVisibleTo` adjustments.

## 7. Proposed Cleanup Sequence (Post-Prompt 26 but Pre API Lock)
1. Deprecate synthetic path: mark `SpkKernelParser` & `DafReader` with `[Obsolete("Replaced by full DAF + SPK loader; will be removed")]` (internal only) – run tests; migrate fixtures.
2. Rename `RealSpkKernelParser` ? `SpkKernelLoader` (internal) and update call sites.
3. Decompose `EphemerisService` (internal classes only – no external breaking change) to reduce complexity hotspot for future feature prompts (TT?>TDB model, logging, diagnostics).
4. Refine `SpkSegment` field names (`Init` ? `InitialEpoch`, `IntervalLength` ? `RecordIntervalSeconds`). Provide migration shim constructors if necessary (internal).
5. Introduce constant(s) for repeated literal tolerances and documented reasons (control word epsilon).
6. Add unit tests for: `LocateRecord` edge overlaps, derivative polynomial correctness (compare analytic derivative for known polynomial), DAF control word parse fallback selection table.
7. Finalize report metrics (update `RefactorReport_Prompt26.md`).
8. Apply Public API analyzer baseline & lock.

## 8. Risk & Benefit Summary
| Change | Risk | Benefit | Mitigation |
|--------|------|---------|------------|
| Removing synthetic parser | Minimal (tests adapt) | Eliminates duplicate logic; reduces maintenance | Migrate tests first, keep branch fallback until green |
| Decomposing service | Medium (regression potential) | Improved testability & future extensibility | Incremental extraction with integration test safety net |
| Renaming parser / segment fields | Low (internal) | Semantic clarity | Bulk rename + compiler guidance |
| Internalizing low-level classes | Low (if unused by consumers) | Shrinks public surface for stability | Publish a migration note in CHANGELOG / README |

## 9. Quick Win Checklist
- [ ] Mark obsolete: synthetic DAF/SPK types.
- [ ] Introduce `ControlWordIntegralEpsilon` const.
- [ ] Add Chebyshev derivative comment.
- [ ] Add dedicated test for `LocateRecord` with intentionally non-uniform spacing (trigger fallback).
- [ ] Update `RefactorReport_Prompt26.md` metrics after removal patch.

## 10. Conclusion
Codebase is structurally sound; largest maintainability lever now is reduction of duplicate parsing layers and decomposing `EphemerisService`. Executing the proposed sequence before API lock will solidify a lean, auditable core ready for subsequent prompts (time model enhancements, additional SPK types, diagnostics, benchmarks).

---
*Prepared as an architectural addendum to Prompt 26; adopt items selectively based on release timeline & risk appetite.*
