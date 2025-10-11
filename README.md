# SpiceNet

Multi-library .NET 9 implementation (clean-room) for loading a subset of NAIF SPICE kernels and querying solar system body ephemerides.

## Status Overview
Phase 1 (synthetic kernel support) complete:
- Core domain primitives (vectors, state, time types, Chebyshev evaluation)
- Leap second handling + TT?TDB periodic approximation (baseline)
- Synthetic DAF reader + SPK (Types 2 & 3) parser (single-record simplified format)
- Segment evaluator (Type 2 derived velocity, Type 3 direct)
- LSK parser (minimal) & meta-kernel parser
- Ephemeris service with precedence-based segment selection
- Integration tests (synthetic SPK + LSK) and time conversion tests
- Initial benchmarks scaffold

## Phase 2 Roadmap (Real Kernel Support)
Goal: Read real ephemeris (SPK) binary kernels and compare against authoritative reference data (JPL testpo & CSPICE numeric parity). Progress below reflects current implementation state.

| Prompt | Goal | Status | Notes |
|--------|------|--------|-------|
| 13 | Full DAF low-level reader (summary/name traversal, endianness) | ? Done | `FullDafReader` spec-compliant, dual-encoding fallback |
| 14 | Real SPK parsing (multi-record Type 2 & 3) | ? Done | Trailer parsing; per-record MID/RADIUS captured |
| 15 | EphemerisDataSource (stream/mmapped, lazy) | ? Done | Lazy & memory-mapped modes |
| 16 | testpo integration (initial) | ? Core | Parser + cache + mapping remap + comparison harness |
| 16a | testpo code/center inventory & JSON report | Planned | (Optional enrichment) |
| 16b | testpo?NAIF ID mapping layer | ? Done | `TestData/BodyMapping.json` + validation test |
| 16c | Relative state resolver (barycentric chaining) | ? Done | Generic; no EMB special-case |
| 16d | Integration test refactor (relative states) | ? Done | AU-domain validation vs dynamic tolerances |
| 16e | Diagnostic CLI: testpo-diagnose | Planned | To be added to Demo/Benchmarks |
| 16f | Velocity semantics validation | Planned | Pending CSPICE parity harness |
| 17 | Golden comparison tests (strict tolerances) | Partial | Policy tiers implemented; stricter CSPICE cross-check TBD |
| 18 | Segment indexing / fast lookup | ? Partial | Per (t,c) sorted arrays + boundary fast path |
| 19-25 | Remaining roadmap items | Planned | See manifest |
| 26 | Consolidation / Quality Alignment | In Progress | Most tasks complete; report & final API lock pending |

## Supported Public Surface (Stability Baseline)
Public facade types:

Spice.Core:
- `BodyId`, `FrameId`, `Duration`, `Instant`, `StateVector`, `Vector3d`

Spice.Ephemeris:
- `EphemerisService`

Pre-1.0.0: surface may evolve with explicit baseline updates.

## Implementation Notes (Current)
- DAF reader: control words (NEXT, PREV, NSUM) + synthetic 32-bit fallback, dual endianness helper (`DafAddress`).
- SPK Types 2 & 3 parsed with trailer `[INIT, INTLEN, RSIZE, N]`; validation: `RecordCount * RecordSize + 4 == totalDoubles`.
- Lazy coefficient loading via data source abstraction (stream / mmap).
- Per-record MID/RADIUS arrays with binary search + boundary fast path.
- Barycentric composition generic (no legacy Earth/Moon path); cycle guard present.
- Central tolerance policy + tests; repository search enforces no stray literals.
- Mapping JSON + validation test (Earth/Moon baseline; extendable).
- Stats JSON artifact emitted per ephemeris with deterministic key order + schema validation.
- Control word decoding tests (double, synthetic int) and record boundary evaluator tests.

## Tolerances
Single authoritative specification lives in `docs/Tolerances.md` (no duplication here). All tests obtain bounds via `TolerancePolicy.Get`.

## Prompt 26 Consolidation – Completed Items
- A: Central `TolerancePolicy` + literal purge + tier tests
- B: Mapping inventory (`BodyMapping.json`) + loader + validation test
- C: Stats artifact JSON + schema tests
- D: Docs reference single tolerance source (root README links only)
- F1–F6: Evaluator/search refactors (binary search + fallback, validation check, DAF helper, control word clarity/tests, cycle guard, LINQ removal)
- G1–G3: Segment index arrays + boundary fast path + `TryGetBarycentric`
- H3–H5: New unit/integration tests (record selection, tolerance tiers, mapping, control words, stats schema)

Pending (Prompt 26 wrap-up):
- H1/H2: Finalize Demo CLI & benchmarks separation
- I: Optional micro-benchmarks (record selection, barycentric warm/cold)
- J2/J3: README checklist tick + `docs/RefactorReport_Prompt26.md` final report
- Final: Public API lock (add analyzer shipped list)

## Upcoming Focus
1. CSPICE numeric parity sampling harness (tighten Prompt 17 strict tier confirmation).
2. Diagnostic CLI enrichment (segment coverage, JSON export, optional CSV sampling).
3. Benchmark set (Chebyshev eval vectorization, index lookup micro-benchmarks).
4. Public API analyzer lock-in (post Prompt 26 report).

## JPL testpo Reference Data
Source: https://ssd.jpl.nasa.gov/ftp/eph/planets/test-data/

## Sample Usage
```csharp
using Spice.Ephemeris;
using Spice.Core;
var service = new EphemerisService();
service.Load("planets.tm");
var state = service.GetState(new BodyId(399), new BodyId(0), Instant.FromSeconds(1_000_000));
```

## Development Principles
- Clean-room reimplementation; explicit units; immutable value types.
- Optimize only after correctness (benchmark-driven).

## Contributing
Changes affecting tolerances or mapping must update the central files (`docs/Tolerances.md`, mapping JSON) – do not duplicate tables elsewhere.

## License
Planned: MIT (pending CSPICE license compatibility confirmation).

## Acknowledgments
NAIF / JPL (SPICE documentation) • JPL SSD (testpo reference data)
