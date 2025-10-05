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
| 13 | Full DAF low-level reader (summary/name traversal, endianness) | ? Done | `FullDafReader` spec-compliant, dual encoding fallback for control words |
| 14 | Real SPK parsing (multi-record Type 2 & 3) | ? Done | Trailer (INIT, INTLEN, RSIZE, N) parsing; per-record MID/RADIUS captured |
| 15 | EphemerisDataSource (stream/mmapped, lazy) | ? Done | Endianness-aware; zero-copy MMF path |
| 16 | testpo loader scaffold | ? Planned | Loader interface drafted (not yet committed) |
| 17 | Golden comparison tests vs testpo | ? Planned | Will gate numerical parity tolerances |
| 18 | Segment indexing / fast lookup | ? Planned | Interval binary search + per-target index |
| 19 | Higher-order TT?TDB model, pluggable strategies | ? Planned | Keep default fast analytic; plug advanced terms |
| 20 | Minimal FK/PCK parsing (body radii, frames) | ? Planned | Required for geometry & future orientation |
| 21 | Diagnostic CLI tool | ? Planned | Coverage listing, CSV export, diff vs testpo |
| 22 | CI workflow & artifacts | ? Planned | Build/test, optional micro benchmark, cache test data |
| 23 | Time conversion strategy interfaces | ? Planned | `ILeapSecondProvider`, `ITdbOffsetModel` |
| 24 | Structured logging for selection decisions | ? Planned | In-memory logger for test assertions |
| 25 | Perf consolidation (SIMD Chebyshev, pooling) | ? Planned | Document before/after in `docs/perf.md` |

## Implementation Notes (Current)
- **DAF Reader**: Reads control words as double precision (NEXT, PREV, NSUM) with fallback to synthetic 32?bit int form used in early tests. Handles both little & big endian by plausibility of ND/NI.
- **Addressing**: 1-based word addresses mapped to `(recordIndex * 1024) + wordIndex*8` byte offsets; record = 128 8-byte words.
- **SPK Types 2 & 3**: Multi-record Chebyshev payload followed by 4-double trailer `[INIT, INTLEN, RSIZE, N]`. Degree validated via `RSIZE = 2 + K*(DEG+1)` where `K=3 (T2)` or `6 (T3)`.
- **Lazy Loading**: Only MID/RADIUS vectors read eagerly; coefficients streamed on demand (or mmapped) using `IEphemerisDataSource`.
- **Endianness**: Coefficient 64-bit words reversed when underlying kernel differs from host endianness.

## Upcoming Focus
1. Implement `TestPoLoader` and trimmed test dataset (subset epochs for major bodies) under `Spice.Tests/TestData`.
2. Introduce `SegmentIndex` building sorted (start, end, pointer) arrays per target for O(log n) lookup.
3. Add golden comparisons (position < 1e-6 km, velocity < 1e-9 km/s) with summary statistics (mean / max errors) dumped to test output.
4. Provide diagnostic CLI (`Spice.Tool`) enumerating segments & coverage.
5. Extend time conversion accuracy & plug strategy model.

## JPL testpo Reference Data (Planned)
Source: https://ssd.jpl.nasa.gov/ftp/eph/planets/test-data/
- Retrieval script (`scripts/fetch-testpo.ps1`) to produce a curated minimal subset.
- Store only needed epochs; cite source & retrieval date.
- Golden tests will compare state deltas & accumulate statistics.

## Sample Usage (Real Kernel Parsing – WIP)
```csharp
using Spice.Kernels;
using var fs = File.OpenRead("de440_small.bsp");
var kernel = RealSpkKernelParser.Parse(fs);
foreach (var seg in kernel.Segments)
  Console.WriteLine($"Target={seg.Target.Id} Center={seg.Center.Id} Type={seg.DataType} Records={seg.RecordCount}");
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
- [ ] testpo loader scaffold
- [ ] Golden comparison harness (tolerances & stats)
- [ ] Segment indexing & performance layer
- [ ] Advanced TT?TDB / pluggable time model
- [ ] Diagnostic CLI & CI workflows
- [ ] Expanded kernel type & frame support

## Development Principles
- Clean-room reimplementation; no CSPICE source inclusion.
- Explicit units, immutable value types, low allocation.
- Benchmark-driven optimization only after correctness established.

## Contributing
Contribution workflow formalization deferred until after golden comparison layer lands.

## License
Planned: MIT (confirm CSPICE license compatibility before distributing binaries). Not an official NAIF/JPL distribution.

## Acknowledgments
- NAIF / JPL for public SPICE documentation.
- JPL SSD for testpo ephemeris reference data.
