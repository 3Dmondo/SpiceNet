# Spice.Ephemeris

High-level ephemeris query facade (`EphemerisService`) orchestrating kernel loading and state retrieval.

Capabilities
- Load kernel files via repeated `EphemerisService.Load("file.ext")` calls (`.tls` leap second, `.bsp` SPK types 2 & 3).
- Lazy or eager SPK parsing (memory-mapped vs stream).
- Barycentric composition with cycle guard.
- Segment selection precedence (latest start among coverings).

Usage
```csharp
var svc = new EphemerisService();
svc.Load("naif0012.tls");            // leap seconds
svc.Load("de440s.bsp");              // planetary SPK
var state = svc.GetState(new BodyId(499), new BodyId(0), Instant.FromSeconds(1_000_000));
```

Notes
- This facade is the only public API entry point for kernel loading and state queries.
