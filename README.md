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
| 15 | EphemerisDataSource (stream/mmapped, lazy) | ? Done | Endianness-aware; lazy coeff fetch |
| 16 | testpo integration (initial) | ? In Progress | Parser + cache + comparison harness active |
| 16a | testpo code/center inventory & JSON report | Planned | Distill distinct (target,center) pairs |
| 16b | testpo?NAIF ID mapping layer | Planned | Replace provisional mapping with JSON file (Prompt 26.B) |
| 16c | Relative state resolver (barycentric chaining) | ? Done | Implemented in `EphemerisService` |
| 16d | Integration test refactor (relative states) | In Progress | AU-domain validation |
| 16e | Diagnostic CLI: testpo-diagnose | Planned | Benchmarks/tooling project extension |
| 16f | Velocity semantics validation | Planned | Cross-check vs CSPICE |
| 17 | Golden comparison tests (strict tolerances) | Partial | Dynamic tolerance regime implemented |
| 18 | Segment indexing / fast lookup | ? Partial | Binary search per (target,center) |
| 19-25 | Remaining roadmap items | Planned | See manifest |
| 26 | Consolidation / Quality Alignment | In Progress (Phase 0 Complete) | Surface baseline captured; proceeding with consolidation tasks |

## Supported Public Surface (Stability Baseline)
Public facade types:

Spice.Core:
- `BodyId`, `FrameId`, `Duration`, `Instant`, `StateVector`, `Vector3d`

Spice.Ephemeris:
- `EphemerisService`

Pre-1.0.0: surface may evolve with explicit baseline updates.

## Implementation Notes (Current)
- DAF reader: control words (NEXT, PREV, NSUM) + synthetic fallback, dual endianness.
- SPK Types 2 & 3 parsed with trailer `[INIT, INTLEN, RSIZE, N]`.
- Lazy coefficient loading via data source abstraction.
- Barycentric composition is generic (no special-case Earth/Moon path).
- Provisional inline testpo remap being replaced by JSON mapping (Prompt 26.B).

## Tolerances
Single authoritative specification lives in `docs/Tolerances.md` (no duplication here). All tests obtain bounds via `TolerancePolicy.Get`.

## Upcoming Focus (Prompt 26)
1. Mapping JSON & loader (remove inline remaps).
2. Stats artifact export & schema validation.
3. Documentation synchronization via links (no duplicated tolerance tables).
4. Evaluator/index refactors (record search, cycle guard, control word clarity).
5. Expanded tests & optional benchmarks.
6. Consolidation report & final public API lock.

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
