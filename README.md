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
| 16b | testpo?NAIF ID mapping layer | Partial | Provisional mapping 3?399, 10?301 |
| 16c | Relative state resolver (barycentric chaining) | ? Done | Implemented in `EphemerisService` |
| 16d | Integration test refactor (relative states) | In Progress | Positions + velocities validated in AU domain |
| 16e | Diagnostic CLI: testpo-diagnose | Planned | Extend benchmarks/tooling project |
| 16f | Velocity semantics validation | Planned | Cross-check vs CSPICE |
| 17 | Golden comparison tests (strict tolerances) | Partial | Dynamic tolerance regime implemented |
| 18 | Segment indexing / fast lookup | ? Partial | Binary search per (target,center) |
| 19-25 | Remaining roadmap items | Planned | See manifest |

## Implementation Notes (Current)
- **DAF Reader**: Reads control words as double precision (NEXT, PREV, NSUM) with fallback to legacy synthetic 32?bit form. Big & little endian supported.
- **Addressing**: 1-based word addresses ? `(recordIndex * 1024) + wordIndex*8` bytes; record = 128 words (1024 bytes).
- **SPK Types 2 & 3**: Multi-record Chebyshev + 4-double trailer `[INIT, INTLEN, RSIZE, N]`; degree validated via `RSIZE = 2 + K*(DEG+1)` (K=3 or 6).
- **Lazy Loading**: MID/RADIUS eagerly; coefficients on demand via `IEphemerisDataSource` (stream or memory-mapped).
- **Earth/Moon Special Handling**: `EphemerisService` derives Earth & Moon barycentric states through EMB + EMRAT relation when required (Earth=EMB?Moon_geo/(1+EMRAT), Moon_bary=Earth+Moon_geo) using EMRAT extracted from BSP comment area.
- **testpo Integration**: Reference values parsed; Earth (3) mapped to 399, Moon (10) to 301 prior to lookup.

## Current Golden Comparison Tolerances
Comparison is performed in astronomical units (AU for position, AU/day for velocity) matching testpo output, after dividing km and km/s results by AU and AU/day (AU/86400). Strict regime used when AU constant ("AU") is found in BSP comment area; otherwise tolerances relax.

Strict (AU constant present):
- Position: 1e-13 AU  (? 1.50e-5 km ? 1.5 cm)
- Velocity: 1e-16 AU/day (? 1.73e-13 km/s)

Relaxed (AU constant missing): ×10,000
- Position: 1e-9 AU  (? 149.6 m)
- Velocity: 1e-12 AU/day (? 1.73e-9 km/s)

Additional early ephemeris relaxation (ephemeris number starting with '2'): extra ×100 (overall ×1,000,000 vs strict)
- Position: 1e-7 AU (? 14.96 km)
- Velocity: 1e-10 AU/day (? 1.73e-11 km/s)

Target parity objective remains <1e-6 km and <1e-9 km/s (?6.6846e-12 AU, 1.15e-14 AU/day) once all scaling paths are verified and legacy relaxation removed.

## Upcoming Focus (Revised)
1. Finalize mapping & inventory (16a/16b) and codify mapping file.
2. Add diagnostic CLI (16e) for residual path tracing.
3. Confirm velocity parity vs CSPICE (16f) and tighten velocity tolerance.
4. Formal golden stats export (JSON) for CI regression charts.
5. Expand time conversion strategy (19) & structured logging (24).

## JPL testpo Reference Data
Source: https://ssd.jpl.nasa.gov/ftp/eph/planets/test-data/
Handling: Cached per ephemeris under integration test data cache. Parser stops after header sentinel `EOT`.

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
- [x] Earth/Moon composite logic (EMRAT) in service
- [x] Dynamic testpo tolerance adaptation (AU presence)
- [ ] Formal testpo mapping inventory (16a/16b)
- [ ] Diagnostic CLI & velocity validation
- [ ] Golden statistics artifact (17)
- [ ] Advanced TT?TDB / pluggable time model
- [ ] Structured logging / performance consolidation

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
