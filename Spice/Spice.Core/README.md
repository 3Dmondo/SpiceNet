# Spice.Core

Domain primitives, time scales, numeric utilities (Chebyshev, vector math). Immutable value types only.

Public primitives
- `Instant` (TDB seconds past J2000)
- `Vector3d` and `StateVector` (position km, velocity km/s)
- `BodyId`, `FrameId`, `Duration`

Design notes
- Readonly structs for thread-safety and immutability.
- Explicit units in naming and XML docs.
- No I/O logic; separation from kernel parsing.
