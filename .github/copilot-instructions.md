# Copilot Shared Instruction Manifest (.mf)
# Purpose: Provide consistent high-level context and quality constraints for a unified .NET 10 / C# 14 Spice library focused on reading NAIF SPICE kernels (Types 2 & 3) and exposing precise ephemerides.

============================================================
SECTION: OVERALL OBJECTIVE
============================================================
Build a cohesive library that:
  1. Loads and parses essential SPICE kernel types required to extract precise solar system body ephemerides.
  2. Provides strongly typed, immutable, well-documented domain primitives (time scales, identifiers, frames,
     state vectors) with clear unit semantics.
  3. Offers a clean, discoverable API for: (a) loading kernels, (b) querying states (position & velocity) between bodies,
     (c) handling leap seconds & time conversions (UTC <-> TAI <-> TDB/ET).
  4. Embraces modern .NET performance features while retaining readability and SRP.
  5. Enables rigorous automated test coverage (golden numeric comparisons vs reference output).

============================================================
SECTION: ARCHITECTURAL GUIDING PRINCIPLES
============================================================
1. SOLID; each class/record has a single reason to change.
2. Immutability by default: readonly structs / records.
3. Separation of concerns: parsing/IO vs math vs orchestration.
4. Explicit units (km, km/s, TDB seconds past J2000).
5. Optimize after correctness (benchmark driven).
6. Internalize DAF complexity; external API is semantic (`EphemerisService`).

============================================================
SECTION: PROJECT STRUCTURE
============================================================
Solution: SpiceNet.sln (net10.0)
Projects:
  - `Spice` (library, published): public facade (`Spice.Ephemeris`) + primitives (`Spice.Core`).
  - Aux (not published): `Spice.Tests`, `Spice.IntegrationTests`, `Spice.Benchmarks`, `Spice.Console.Demo`, `Spice.ApiScan`, `Spice.SsdCatalog`.

============================================================
SECTION: CODING CONVENTIONS / STYLE ALIGNMENT
============================================================
Honor .editorconfig: 2-space indent, file-scoped namespaces, predefined types, readonly fields, span-based parsing, minimal allocations, collection expressions, centralized tolerances, XML docs for public APIs.

============================================================
SECTION: DOMAIN MODEL (PUBLIC FACADE)
============================================================
Core primitives: `BodyId`, `FrameId`, `Duration`, `Instant`, `StateVector`, `Vector3d`.
Facade: `EphemerisService`.

============================================================
SECTION: TEST FIXTURES GUIDELINES
============================================================
- Keep fixtures minimal (time-window reduced public-domain kernels).
- `testpo` subset curated; cite source.
- No proprietary / license-restricted kernels.

============================================================
SECTION: VALIDATION & ERROR BUDGET
============================================================
- Target double-precision parity; investigate relative deviation > 1e-10.
- Approximations documented; tolerances centralized in `docs/Tolerances.md`.

============================================================
SECTION: CONTRIBUTION WORKFLOW NOTES
============================================================
- Update docs when completing milestones.
- Unit tests must pass; integration tests gated to tag releases if enabled.
- Public API changes require updating `PublicAPI.Unshipped.txt`.

============================================================
END OF MANIFEST
