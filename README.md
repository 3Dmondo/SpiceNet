# SpiceNet

Unified .NET 10 library for loading a subset of NAIF SPICE kernels and querying solar system body ephemerides.

Projects in this repo:
- `Spice` (library, published): unified codebase and only public API surface (facade in `Spice.Ephemeris`, primitives in `Spice.Core`).
- Aux projects (not published): `Spice.Tests`, `Spice.IntegrationTests`, `Spice.Benchmarks`, `Spice.Console.Demo`, `Spice.ApiScan`, `Spice.SsdCatalog`, `Spice.WebDataGenerator`.

Public API surface (facade):
- `Spice.Core`: `BodyId`, `FrameId`, `Duration`, `Instant`, `StateVector`, `Vector3d`
- `Spice.Ephemeris`: `EphemerisService`

Basic usage:
```csharp
using Spice.Ephemeris;
using Spice.Core;

var svc = new EphemerisService();
svc.Load("naif0012.tls");    // leap seconds
svc.Load("de440s.bsp");      // SPK (Types 2 & 3 supported)
var state = svc.GetState(new BodyId(399), new BodyId(0), Instant.FromSeconds(1_000_000));
```

Notes:
- Multiple `Load` calls accumulate kernels; order is preserved.
- Returned `StateVector` is position (km) and velocity (km/s).

Development
- Tests and integration harnesses are in aux projects (not published).
- Public API is enforced by analyzers (`PublicAPI.Shipped.txt`).
- `Spice.WebDataGenerator` is the auxiliary CLI for emitting compact web-ready ephemeris and metadata assets, including first-pass text-kernel-derived body metadata. See `docs/WebDataGenerator.md`.
- `scripts/Update-WebDataMetadataSnapshot.ps1` downloads the official NAIF generic metadata kernels into a local ignored cache and refreshes the committed metadata snapshot at `Spice.WebDataGenerator/ReferenceData/body-metadata.json`.
