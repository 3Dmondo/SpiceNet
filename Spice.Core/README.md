# Spice.Core
Domain primitives, time scales, numeric utilities (Chebyshev, vector math). Immutable value types only. No I/O logic.

## Implemented
- `Instant` (TDB seconds past J2000)
- Vector & state records (position km, velocity km/s)
- Chebyshev polynomial evaluation (position + derivative)
- Basic TT?TDB periodic offset approximation (baseline)
- Leap second support integration points (consumed by higher layer)

## Pending / Roadmap Alignment
- Higher order TT?TDB model (Prompt 19)
- Strategy interfaces: `ILeapSecondProvider`, `ITdbOffsetModel` (Prompt 23)
- Vectorized (SIMD) Chebyshev evaluation & pooling (Prompt 25)
- Frame transformation primitives (post PCK/FK minimal parsing)

## Design Notes
- All structs are readonly for thread-safety & zero accidental mutation.
- Units enforced in naming & XML docs (km, km/s, seconds).
- No kernel-specific assumptions or address math here (separation of concerns).
