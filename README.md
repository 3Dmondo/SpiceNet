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
| 16 | testpo integration (initial) | ? Partial | Basic parser + download cache; semantics/mapping pending |
| 16a | testpo code/center inventory & JSON report | ?? Planned | Distill distinct (target,center) pairs |
| 16b | Provisional testpo?NAIF ID mapping layer | ?? Planned | Heuristic + override file `testpo_mapping.json` |
| 16c | Relative state resolver (barycentric chaining) | ? Done | Implemented in `EphemerisService` (target,center via SSB) |
| 16d | Integration test refactor using relative states | ?? Planned | Positions only until semantics validated |
| 16e | Diagnostic CLI: testpo-diagnose (path + residuals) | ?? Planned | Extend benchmarks/tooling project |
| 16f | Velocity semantics validation & enable vel asserts | ?? Planned | Compare derived vs reference or CSPICE |
| 17 | Golden comparison tests (strict tolerances) | ?? Planned | After 16a–f completion |
| 18 | Segment indexing / fast lookup | ? Partial | Per-(target,center) binary search index implemented; perf tests pending |
| 19 | Higher-order TT?TDB model, pluggable strategies | ?? Planned | Keep current analytic as default |
| 20 | Minimal FK/PCK parsing (body radii, frames) | ?? Planned | Foundation for frames/orientation |
| 21 | Diagnostic CLI tool (coverage, CSV export) | ?? Planned | Will absorb testpo diagnostics |
| 22 | CI workflow & artifacts | ?? Planned | Add caching & optional benchmarks |
| 23 | Time conversion strategy interfaces | ?? Planned | `ILeapSecondProvider`, `ITdbOffsetModel` |
| 24 | Structured logging (segment selection trace) | ?? Planned | In-memory logger for tests |
| 25 | Performance consolidation (SIMD, pooling) | ?? Planned | Document gains in `docs/perf.md` |

## Implementation Notes (Current)
- **DAF Reader**: Reads control words as double precision (NEXT, PREV, NSUM) with fallback to legacy synthetic 32?bit form. Big & little endian supported.
- **Addressing**: 1-based word addresses ? `(recordIndex * 1024) + wordIndex*8` bytes; record = 128 words (1024 bytes).
- **SPK Types 2 & 3**: Multi-record Chebyshev + 4-double trailer `[INIT, INTLEN, RSIZE, N]`; degree validated via `RSIZE = 2 + K*(DEG+1)` (K=3 or 6).
- **Lazy Loading**: MID/RADIUS eagerly; coefficients on demand via `IEphemerisDataSource` (stream or memory-mapped).
- **Endianness**: Automatic detection; coefficient words byte-swapped when needed.
- **Segment Index**: Per (target, center) sorted array with binary search for latest-start covering segment (Prompt 18 partial).
- **Relative State Composition**: Implemented barycentric chaining (target vs center via SSB=0) enabling indirect state queries when direct segment absent (Prompt 16c).
- **testpo Integration (Early)**: Downloader, caching, line-by-line component parser (positions & velocities) present; semantic mapping and golden validation pending 16a–f.

## Upcoming Focus (Revised)
1. testpo code/center inventory & mapping heuristics (16a,16b).
2. Refactor integration tests to exercise relative state resolution & positions only (16d).
3. Diagnostic CLI additions (coverage & testpo-diagnose) (16e / 21 synergy).
4. Velocity semantics confirmation & enable velocity assertions (16f).
5. Golden tolerance harness (17) with summary JSON artifact.
6. Higher-order time conversion model & pluggable providers (19/23).

## JPL testpo Reference Data (Planned)
Source: https://ssd.jpl.nasa.gov/ftp/eph/planets/test-data/
- Retrieval script (future) to trim datasets & produce reproducible cache.
- Mapping override file for ambiguous codes.
- Golden tests will output statistics: max/mean/RMS position & velocity errors.

## Sample Usage (Real Kernel Parsing – WIP)
```csharp
using Spice.Kernels;
using var fs = File.OpenRead("de440_small.bsp");
var kernel = RealSpkKernelParser.Parse(fs);
foreach (var seg in kernel.Segments)
  Console.WriteLine($"Target={seg.Target.Value} Center={seg.Center.Value} Type={seg.DataType} Records={seg.RecordCount}");
```

## Completed vs Pending Checklist
- [x] Core primitives & math
- [x] Synthetic SPK parsing (types 2,3 single record)
- [x] LSK parser & time conversion (baseline)
- [x] Meta-kernel + service orchestration
- [x] Integration tests (synthetic)
- [x] Full DAF reader (real layout enumeration)
- [x] Real SPK parsing (multi-record Types 2 & 3)
- [x] EphemerisDataSource + lazy SPK loading & benchmarks
- [x] Relative state (barycentric) resolution
- [x] Segment indexing (baseline)
- [ ] testpo inventory & mapping (16a/16b)
- [ ] testpo integration refactor (16d)
- [ ] testpo diagnostics CLI (16e)
- [ ] Velocity semantics validation (16f)
- [ ] Golden comparison harness (17)
- [ ] Advanced TT?TDB / pluggable time model
- [ ] Diagnostic CLI & CI workflows
- [ ] Expanded kernel type & frame support

## Development Principles
- Clean-room reimplementation; no CSPICE source inclusion.
- Explicit units, immutable value types, low allocation.
- Benchmark-driven optimization after correctness.

## Contributing
Contribution workflow formalization deferred until after golden comparison layer lands.

## License
Planned: MIT (confirm CSPICE license compatibility before distributing binaries). Not an official NAIF/JPL distribution.

## Acknowledgments
- NAIF / JPL for public SPICE documentation.
- JPL SSD for testpo ephemeris reference data.
