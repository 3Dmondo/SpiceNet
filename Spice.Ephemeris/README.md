# Spice.Ephemeris
High-level ephemeris query facade (`EphemerisService`) orchestrating kernel loading and state retrieval.

## Current Capabilities
- Direct loading of individual kernel files via repeated `EphemerisService.Load("file.ext")` calls (`.tls` leap second, `.bsp` SPK types 2 & 3).
- Lazy or eager SPK parsing (memory-mapped vs stream) selected per call.
- Barycentric composition (generic SSB chaining, no Earth/Moon special-case) with cycle guard.
- Segment selection precedence: among matching (target,center) covering the epoch choose segment with latest start time.
- Binary search + boundary fast path segment lookup using per (target,center) sorted arrays.
- Chebyshev evaluation (Type 2: position + derivative-derived velocity, Type 3: position + velocity polynomials).
- Central tolerance policy consumed by integration tests (see `docs/Tolerances.md`).

## Removed During Consolidation
- Meta-kernel (.tm) parser indirection: explicit ordering achieved by calling `Load` multiple times in desired sequence.

## Planned Enhancements
| Roadmap Ref | Feature | Notes |
|-------------|---------|-------|
| 16–17 | Extended testpo golden comparisons | Additional diagnostics & tighter parity tiers |
| 18 | Further index optimizations | Potential interval arithmetic direct record index hints |
| 19 | Advanced TT?TDB model strategies | Pluggable time offset model |
| 21 | Diagnostic CLI enrichment | Coverage listing & CSV export using ONLY public facade |
| 24 | Structured logging (selection trace) | In-memory provider for assertions |
| 25 | Performance vectorization | Chebyshev eval SIMD + pooling |

## `EphemerisService.Load`
```csharp
public void Load(string path, bool memoryMap = true)
```
Inputs:
- `.tls`: parses leap seconds and installs TAI-UTC offsets.
- `.bsp`: loads SPK (Types 2 & 3). If `memoryMap=true` uses lazy coefficient access; else eager stream parse.
Multiple calls accumulate kernels; order of calls defines precedence when segments have identical start epochs.

## Usage
```csharp
var svc = new EphemerisService();
svc.Load("naif0012.tls");            // leap seconds
svc.Load("de440s.bsp");              // planetary SPK
var state = svc.GetState(new BodyId(499), new BodyId(0), Instant.FromSeconds(1_000_000));
```

## Non-Goals (Current Phase)
- Frame transformations beyond implicit SPK reference frame id.
- Light-time / aberration corrections.
- Orientation (CK/PCK) driven rotations (future phases).

See `../docs/SpkDafFormat.md` for low-level format details implemented by underlying IO + Kernel layers.
