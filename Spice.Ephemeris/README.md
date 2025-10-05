# Spice.Ephemeris
High-level ephemeris query facade (EphemerisService) orchestrating kernel loading and state retrieval.

## Current Capabilities
- Loads meta-kernel listing LSK + SPK binaries (synthetic & real Types 2/3).
- Resolves leap seconds (basic) and converts UTC (planned) ? TDB Instant via Spice.Core utilities.
- Selects first matching segment (linear scan) satisfying: target, center, epoch ? [start, stop].
- Evaluates state via `SpkSegmentEvaluator` (Chebyshev interpolation; derivative for Type 2 velocity).

## Planned Enhancements
| Roadmap Ref | Feature | Notes |
|-------------|---------|-------|
| 16–17 | testpo integration & golden comparisons | Will surface API-to-reference statistics |
| 18 | Segment indexing & interval binary search | Per-target sorted segment table; O(log n) lookup |
| 19 | Advanced TT?TDB model pluggable strategies | Strategy injection (ILeapSecondProvider, ITdbOffsetModel) |
| 21 | Diagnostic CLI tool | Built atop this layer for coverage & CSV export |
| 24 | Structured logging (selection trace) | In-memory provider for test assertions |

## Usage (Concept Sketch)
```csharp
var eph = new EphemerisService();
eph.LoadMetaKernel("planets.tm");
var state = eph.GetState(new BodyId(499), new BodyId(0), new Instant(1_000_000));
Console.WriteLine(state.PositionKm);
```

## Non-Goals (Current Phase)
- Frame transformations beyond implicit SPK reference frame ID.
- Aberration corrections / light-time modeling.
- Orientation (CK/PCK) driven transformations (future phases).

See `../docs/SpkDafFormat.md` for low-level format details implemented by underlying IO + Kernel layers.
