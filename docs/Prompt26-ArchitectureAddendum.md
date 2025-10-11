# Prompt 26 – Architecture / Code Quality Addendum

Purpose: Deep-dive review of current codebase focusing on unused / redundant classes, SRP adherence, naming clarity, readability & maintainability gaps surfaced late in Prompt 26. This addendum feeds follow?up cleanups prior to final API lock.

## 1. Redundant / Potentially Obsolete Classes (Updated)
| Component | Prior Role | Current Status | Action Outcome |
|-----------|------------|----------------|----------------|
| `DafReader` (synthetic) | Minimal synthetic DAF header reader | Removed | Tests migrated / synthetic path no longer needed |
| `SpkKernelParser` (synthetic) | Simplified SPK parser | Removed | Replaced by real parser only |
| `TestPoLoader` | Simplified testpo variant loader | Removed | Unified on richer `TestPoParser` |
| `LskParser` | Minimal LSK parser | Retained internal | Future rename optional |
| `RealSpkKernelParser` | Real SPK Types 2/3 parser | Retained (rename deferred) | Will rename during decomposition phase |
| `SpkSegmentEvaluator` | Record locate + Chebyshev eval | Retained | Extraction of locator deferred |
| `EphemerisService` | Loading + indexing + barycentric | Retained | Decomposition deferred (post-lock) |

## 2. Unused Code Confirmation
Obsolete synthetic parsing & loaders removed; no residual references detected in tests or service.

## 3. SRP Observations (Current)
| Area | SRP Status | Planned Follow-Up |
|------|-----------|------------------|
| `EphemerisService` | Monolithic | Extract loader + resolver (post API lock) |
| `SpkSegmentEvaluator` | Mixed (locate + math) | Split locator (future) |
| `TolerancePolicy` | Good | None |
| `TestPoComparisonTests` | Overloaded | Potential helper extraction (optional) |

## 4. Naming & Consistency
Pending rename of `RealSpkKernelParser` ? `SpkKernelLoader` left for post-lock refactor; segment field renames deferred to avoid test churn before baseline capture.

## 5. Readability & Maintainability Updates
Completed quick wins:
- Control word integral epsilon centralized (`ControlWordIntegralEpsilon`).
- Chebyshev derivative method annotated with U_k recurrence rationale.
- Added explicit test covering record selection linear fallback (unsorted mids).
Pending improvements (tracked): enhanced error context, array pooling evaluation, incremental index rebuild.

## 6. Public Surface Hardening
Remaining before lock: add Public API analyzer baseline including only facade primitives + `EphemerisService`.

## 7. Cleanup Sequence (Revised – Post Lock)
1. Decompose `EphemerisService` (internal refactor only).
2. Rename `RealSpkKernelParser` ? `SpkKernelLoader`.
3. Optional: Extract `SpkRecordLocator` from evaluator for isolated tests.
4. Segment field rename (`Init` ? `InitialEpoch`, `IntervalLength` ? `RecordIntervalSeconds`).
5. Introduce targeted micro benchmarks to validate no regressions.

## 8. Risk & Benefit Summary
Unchanged; removal phase completed without breaking tests.

## 9. Quick Win Checklist (Updated)
- [x] Remove synthetic DAF/SPK types.
- [x] Introduce `ControlWordIntegralEpsilon` const.
- [x] Add Chebyshev derivative comment.
- [x] Add dedicated test for `LocateRecord` fallback (unsorted mids).
- [ ] Update `RefactorReport_Prompt26.md` metrics after final diff.

## 10. Conclusion
Synthetic legacy paths eliminated; core evaluator & parser documented; consolidation tasks converging toward API lock. Remaining minor metrics update will precede analyzer baseline commit.

---
*Updated after removal & quick-win implementation.*
