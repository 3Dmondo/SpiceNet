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
| 16a | testpo code/center inventory & JSON report | ? Planned | Distill distinct (target,center) pairs |
| 16b | testpo?NAIF ID mapping layer | Partial | Provisional mapping 3?399, 10?301 (to be replaced by mapping file) |
| 16c | Relative state resolver (barycentric chaining) | ? Done | Implemented in `EphemerisService` |
| 16d | Integration test refactor (relative states) | In Progress | Positions + velocities validated in AU domain |
| 16e | Diagnostic CLI: testpo-diagnose | Planned | Extend benchmarks/tooling project |
| 16f | Velocity semantics validation | Planned | Cross-check vs CSPICE |
| 17 | Golden comparison tests (strict tolerances) | Partial | Dynamic tolerance regime implemented |
| 18 | Segment indexing / fast lookup | ? Partial | Binary search per (target,center) |
| 19-25 | Remaining roadmap items | Planned | See manifest |
| 26 | Consolidation / Quality Alignment | Planned | Remove obsolete docs, unify tolerances & mappings |

## Current Public API Surface (Post-Pruning)
Public surface deliberately minimal; implementation / parsing types are internal.

Spice.Core
- `BodyId` (value id wrapper)
- `FrameId` (value id wrapper)
- `Duration` (seconds)
- `Instant` (TDB whole seconds past J2000)
- `StateVector` (position km, velocity km/s)
- `Vector3d` (3D vector km)

Spice.Ephemeris
- `EphemerisService` (kernel load + state queries)

(Everything else currently internal: SPK/DAF parsers, kernel/segment models, time conversion utilities, leap second model, Chebyshev evaluators, IO abstractions.)

Planned: introduce PublicApiAnalyzers baseline before broadening any surface (Prompt 26).

## Implementation Notes (Current)
- **DAF Reader**: Reads control words as double precision (NEXT, PREV, NSUM) with fallback to legacy synthetic 32-bit form. Big & little endian supported.
- **Addressing**: 1-based word addresses ? `(recordIndex * 1024) + wordIndex*8` bytes; record = 128 words (1024 bytes).
- **SPK Types 2 & 3**: Multi-record Chebyshev + 4-double trailer `[INIT, INTLEN, RSIZE, N]`; degree validated via `RSIZE = 2 + K*(DEG+1)` (K=3 or 6).
- **Lazy Loading**: MID/RADIUS eagerly; coefficients on demand via `IEphemerisDataSource` (stream or memory-mapped).
- **Generic Barycentric Composition**: Relative states composed via SSB chaining (state(target,center)=state(target,0)-state(center,0)); previous EMB+EMRAT special-case path removed/obsolete.
- **testpo Integration**: Reference values parsed; temporary inline mapping (3?399, 10?301) scheduled for external JSON manifest (Prompt 26).

## Current Golden Comparison Tolerances
Strict (AU constant present): position 1e-13 AU, velocity 1e-16 AU/day.
Relaxation escalations applied when AU missing or legacy ephemeris detected (see integration test README) – target convergence: <1e-6 km, <1e-9 km/s.

## Upcoming Focus (Revised)
1. Finalize mapping & inventory (16a/16b) and codify mapping file.
2. Add diagnostic CLI (16e) for residual path tracing.
3. Confirm velocity parity vs CSPICE (16f) and tighten velocity tolerance.
4. Formal golden stats export (JSON) for CI regression charts.
5. Expand time conversion strategy (19) & structured logging (24).
6. Execute Prompt 26 consolidation (unify tolerances, mapping, remove obsolete EMB/EMRAT doc references, add PublicApiAnalyzers baseline).

## JPL testpo Reference Data
Source: https://ssd.jpl.nasa.gov/ftp/eph/planets/test-data/
Handling: Cached per ephemeris under integration test data cache. Parser stops after header sentinel `EOT`.

## Sample Usage (Real Kernel Parsing – WIP)
```csharp
using Spice.Ephemeris;
using Spice.Core;
var service = new EphemerisService();
service.Load("planets.tm");
var state = service.GetState(new BodyId(399), new BodyId(0), Instant.FromSeconds(1_000_000));
```

## Development Principles
- Clean-room reimplementation; no CSPICE source inclusion.
- Explicit units, immutable value types, low allocation.
- Benchmark-driven optimization after correctness.

## Contributing
Contribution workflow formalization deferred until after golden comparison layer lands. Provide rationale for any tolerance changes.

## License
Planned: MIT (confirm CSPICE license compatibility before distributing binaries). Not an official NAIF/JPL distribution.

## Acknowledgments
- NAIF / JPL for public SPICE documentation.
- JPL SSD for testpo ephemeris reference data.
