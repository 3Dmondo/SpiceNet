# SpiceNet

Multi-library .NET 9 implementation (clean-room) for loading a subset of NAIF SPICE kernels and querying solar system body ephemerides.

## Status Overview
Phase 1 (synthetic kernel support) complete:
- Core domain primitives (vectors, state, time types, Chebyshev evaluation)
- Leap second handling + TT?TDB periodic approximation
- Synthetic DAF reader + SPK (Types 2 & 3) parser (single-record simplified format)
- Segment evaluator (Type 2 derived velocity, Type 3 direct)
- LSK parser (minimal) & meta-kernel parser
- Ephemeris service with precedence-based segment selection
- Integration tests (synthetic SPK + LSK) and time conversion tests
- Initial benchmarks project scaffold present

## Phase 2 Roadmap (Real Kernel Support)
Goal: Read real ephemeris (SPK) binary kernels and compare against authoritative reference data (JPL testpo files & CSPICE).

Planned incremental prompts:
1. (13) Full DAF reader: directory traversal, summary & name records, endianness detection, raw array address enumeration. (COMPLETED)
2. (14) Real SPK segment parsing: descriptors (DC/IC), multiple records per segment, scaling (MID/RADIUS) per record, Types 2 & 3. (COMPLETED)
3. (15) EphemerisDataSource abstraction (Stream vs MemoryMapped), async open, lazy coefficient access (no full materialization) + benchmarks. (COMPLETED)
4. (16) JPL testpo integration: ASCII loader + comparison test scaffold. (COMPLETED)
5. (17) Golden comparison tests vs testpo (position < 1e-6 km, velocity < 1e-9 km/s) with statistics output. (COMPLETED)
6. (18) Segment index/caching layer (interval search + fast TryGetState path); benchmark improvements.
7. (19) Higher-order TT?TDB model + pluggable offset strategy; verify <10 ?s deviation vs CSPICE over multi-year sample.
8. (20) Minimal FK/PCK parsing for body radii & frame metadata groundwork.
9. (21) Diagnostic CLI tool (segment inventory, coverage, CSV export of states).
10. (22) CI workflow (build/test/optional benchmark) + artifact publication & small test data caching.
11. (23) TimeConversionService refactor to provider interfaces (ILeapSecondProvider, ITdbOffsetModel) for extensibility.
12. (24) Structured logging (ILogger) for kernel load + segment selection decisions; in-memory capture for tests.
13. (25) Performance consolidation: vectorized Chebyshev (SIMD), pooled scratch buffers, bounds-check minimization; document before/after metrics.

## Future / Stretch Goals
- Additional SPK types (5, 13) & orientation kernels (CK) groundwork
- Memory-mapped automatic eviction strategy / segment LRU cache
- Advanced frame transformations & light-time / aberration corrections
- Source generators for constant body/frame IDs
- More precise relativistic time models (TCB support)

## JPL testpo Reference Data
A curated subset of the JPL planetary ephemeris test data ("testpo") will be used for integration validation.
Source: https://ssd.jpl.nasa.gov/ftp/eph/planets/test-data/
Planned handling:
- Add a retrieval script under `scripts/fetch-testpo.ps1` (future)
- Store only a minimal trimmed subset (few epochs per planet) under `Spice.Tests/TestData/testpo/`
- Cite source & retrieval date in a README within the test data directory
- Tests will parse testpo lines into (epoch, state) and compare to interpolated SPK results.

## Sample Usage (Current Synthetic Flow)
```csharp
using Spice.Core;
using Spice.Ephemeris;

var service = new EphemerisService();
service.Load("path/to/meta_kernel.tm"); // lists .tls and .bsp synthetic test kernels

var target = new BodyId(499);   // Example: Mars barycenter (synthetic)
var center = new BodyId(0);     // Solar system barycenter (synthetic)
var epoch  = new Instant(50);   // TDB seconds past J2000 (synthetic window)

var state = service.GetState(target, center, epoch);
Console.WriteLine($"Pos (km): {state.PositionKm.X}, {state.PositionKm.Y}, {state.PositionKm.Z}");
Console.WriteLine($"Vel (km/s): {state.VelocityKmPerSec.X}, {state.VelocityKmPerSec.Y}, {state.VelocityKmPerSec.Z}");
```

## Development Principles
- Clean-room reimplementation; no CSPICE source inclusion.
- Explicit units, immutable value types, low allocation.
- Benchmark-driven optimization only after correctness established.

## Completed vs Pending Checklist
- [x] Core primitives & math
- [x] Synthetic SPK parsing (types 2,3 single record)
- [x] LSK parser & time conversion (basic + periodic TDB terms)
- [x] Meta-kernel + service orchestration
- [x] Integration tests (synthetic)
- [x] Full DAF reader (real layout enumeration)
- [x] Real SPK parsing (multi-record)
- [x] EphemerisDataSource + lazy SPK loading & benchmarks
- [x] testpo loader + comparison scaffold
- [x] Golden comparison harness (tolerances & stats)
- [ ] Segment indexing & performance layer
- [ ] Advanced TT?TDB / pluggable time model
- [ ] Diagnostic CLI & CI workflows
- [ ] Expanded kernel type & frame support

## Contributing
(Open contribution workflow to be formalized after Phase 2 foundation is in place.)

## License
Planned: MIT (confirm CSPICE license compatibility before distributing binaries). Not an official NAIF/JPL distribution.

## Acknowledgments
- NAIF / JPL for publicly documented SPICE architecture.
- JPL SSD for providing testpo reference ephemeris data.
