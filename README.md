# SpiceNet

Multi-library .NET 9 implementation (clean-room) for loading a subset of NAIF SPICE kernels (Phase 1: LSK + SPK Types 2 & 3) and querying solar system body ephemerides.

Status: Core Phase 1 primitives, synthetic kernel parsers (DAF, SPK types 2 & 3), leap second parser (LSK), meta-kernel loader, ephemeris service, and integration tests implemented using handcrafted synthetic kernels. Real kernel validation + extended features pending.

## Projects
- `Spice.Core` – Domain primitives (Instant, Vector3d, StateVector, Duration, Chebyshev utilities, time conversions)
- `Spice.IO` – Low-level DAF reader (simplified synthetic format for MVP tests)
- `Spice.Kernels` – Parsers (SPK, LSK, Meta-kernel) + segment evaluator
- `Spice.Ephemeris` – High-level `EphemerisService` (segment selection + state queries)
- `Spice.Tests` – Unit & integration tests with synthetic data

## Sample Usage (Synthetic Kernels)
```csharp
using Spice.Core;
using Spice.Ephemeris;

var service = new EphemerisService();
service.Load("path/to/meta_kernel.tm"); // meta-kernel lists .tls and .bsp synthetic test kernels

var target = new BodyId(499);   // Example: Mars barycenter (synthetic here)
var center = new BodyId(0);     // Solar system barycenter (synthetic)
var epoch  = new Instant(50);   // TDB seconds past J2000 in synthetic range

var state = service.GetState(target, center, epoch);
Console.WriteLine($"Pos (km): {state.PositionKm.X}, {state.PositionKm.Y}, {state.PositionKm.Z}");
Console.WriteLine($"Vel (km/s): {state.VelocityKmPerSec.X}, {state.VelocityKmPerSec.Y}, {state.VelocityKmPerSec.Z}");
```

## Roadmap (Next Steps)
1. Support real SPK binary layout (full DAF directory structure, multiple records per segment)
2. Improved TT?TDB conversion (periodic terms)
3. Benchmark & performance tuning (vectorized Chebyshev evaluation, segment indexing)
4. Additional SPK types & real kernel regression tests vs CSPICE
5. Meta-kernel enhancements (all kernel classes, ordering semantics)
6. Optional async load & memory-mapped file support

## License
Planned: MIT (confirm CSPICE license compatibility before distributing binaries). This is not an official NAIF/JPL distribution.
