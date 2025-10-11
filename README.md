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
| 16b | testpo?NAIF ID mapping layer | Planned | Will replace provisional mapping with JSON file (Prompt 26.B) |
| 16c | Relative state resolver (barycentric chaining) | ? Done | Implemented in `EphemerisService` |
| 16d | Integration test refactor (relative states) | In Progress | Positions + velocities validated in AU domain |
| 16e | Diagnostic CLI: testpo-diagnose | Planned | Extend benchmarks/tooling project |
| 16f | Velocity semantics validation | Planned | Cross-check vs CSPICE |
| 17 | Golden comparison tests (strict tolerances) | Partial | Dynamic tolerance regime implemented |
| 18 | Segment indexing / fast lookup | ? Partial | Binary search per (target,center) |
| 19-25 | Remaining roadmap items | Planned | See manifest |
| 26 | Consolidation / Quality Alignment | In Progress (Phase 0 Complete) | Public surface baseline captured; proceeding with Tasks A–J; CI lock deferred |

## Supported Public Surface (Stability Baseline)
Phase 0 (Prompt 26) pruned and documented the externally supported API. Current public types:

Spice.Core:
- `BodyId` – NAIF body identifier wrapper (int)
- `FrameId` – Frame identifier wrapper (int) (may remain if needed for future orientation APIs)
- `Duration` – Interval in seconds
- `Instant` – TDB whole seconds past J2000
- `StateVector` – Position (km) & velocity (km/s)
- `Vector3d` – 3D vector (km)

Spice.Ephemeris:
- `EphemerisService` – Kernel loading & state queries

Policy (pre-1.0.0): This facade may evolve but additions/removals require explicit PublicAPI baseline updates. Internal parsing, evaluation, time conversion, and IO abstractions are intentionally non-public.

CI public API lock (automated failure on surface drift) will be introduced at the end of Prompt 26 after remaining consolidation refactors (tolerances, mapping, stats) settle to avoid baseline churn.

## Implementation Notes (Current)
- **DAF Reader**: Reads control words as double precision (NEXT, PREV, NSUM) with fallback to legacy synthetic 32-bit form. Big & little endian supported.
- **Addressing**: 1-based word addresses ? bytes via `(wordIndex-1)*8`; record = 128 words (1024 bytes).
- **SPK Types 2 & 3**: Multi-record Chebyshev + 4-double trailer `[INIT, INTLEN, RSIZE, N]`; degree validated via `RSIZE = 2 + K*(DEG+1)` (K=3 or 6).
- **Lazy Loading**: MID/RADIUS eagerly; coefficients on demand via `IEphemerisDataSource` (stream or memory-mapped).
- **Generic Barycentric Composition**: Relative states composed via SSB chaining (state(target,center)=state(target,0)-state(center,0)); obsolete EMB/EMRAT special-case removed.
- **testpo Integration**: Reference values parsed; provisional inline mapping to be replaced by external JSON (Prompt 26.B).

## Tolerances
Centralized in `Spice.Core.TolerancePolicy` (fine?tuned). Canonical table: `docs/Tolerances.md`.

Key tiers (when AU constant present):
- Modern High Fidelity (ephemeris > 414 and != 421): 2e-14 AU position, 3e-17 AU/day velocity (strict=true)
- Legacy Series (ephemeris ? 414 excluding 421): 6e-14 AU position, 5e-14 AU/day velocity
- Problematic (DE421): 2e-12 AU position, 5e-15 AU/day velocity
Fallback (no AU constant): 5e-8 AU position, 1e-10 AU/day velocity

Derived km / km/s tolerances computed via shared constants (`Constants.AstronomicalUnitKm`, `Constants.AuPerDayToKmPerSec`).

`Constants.LegacyDeAU` contains historical AU values per DE series facilitating validation when AU symbol missing.

Future tightening or unification will be data-driven (stats artifact Prompt 26.C). All hard-coded tolerance literals removed from tests; only `TolerancePolicy.Get` used.

## Upcoming Focus (Revised for Prompt 26)
1. Implement mapping JSON & loader; remove inline remaps (26.B).
2. Add stats artifact export & schema validation (26.C).
3. Documentation synchronization & tolerance snippet hash test (26.D).
4. Quality gates: literal scan, roadmap sync, (final) public API CI lock (26.E & Step 7).
5. Evaluator/index refactors (26.F/G) including binary search guard & cycle prevention.
6. Expanded tests & benchmarks (26.H/I) and consolidation report (26.J).

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
During Prompt 26 consolidation, changes affecting tolerances, mapping, or public surface must update the corresponding baseline files and docs. Provide rationale in PR descriptions.

## License
Planned: MIT (confirm CSPICE license compatibility before distributing binaries). Not an official NAIF/JPL distribution.

## Acknowledgments
- NAIF / JPL for public SPICE documentation.
- JPL SSD for testpo ephemeris reference data.
