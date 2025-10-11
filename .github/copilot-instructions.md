# Copilot Shared Instruction Manifest (.mf)
# Purpose: Provide consistent high-level context and quality constraints for porting a subset of NAIF CSPICE toolkit
# into a modern, maintainable, testable .NET 9 / C# 14 multi-library ecosystem focused on reading JPL ephemerides
# (text and binary kernels) and exposing precise solar system body state data.
# This file is consumed by subsequent Copilot agent prompt sessions. Keep it stable; evolve deliberately.

============================================================
SECTION: OVERALL OBJECTIVE
============================================================
Build a cohesive set of narrowly-scoped C# libraries (prefer multiple small projects over one monolith) that:
  1. Load and parse essential SPICE kernel types required to extract precise solar system body ephemerides.
  2. Provide strongly typed, immutable, well-documented domain primitives (time scales, identifiers, frames,
     state vectors, orientations) with clear unit semantics.
  3. Offer a clean, discoverable API for: (a) loading kernels, (b) querying states (position & velocity) between bodies,
     (c) handling leap seconds & time conversions (UTC <-> TAI <-> TDB/ET), (d) performing basic frame transformations (initial minimal subset).
  4. Embrace modern .NET 9 performance features (Span<T>, ReadOnlySpan<T>, vectorization via System.Numerics, optional SIMD intrinsics) while
     retaining readability and SRP.
  5. Enable rigorous automated test coverage (golden numeric comparisons vs authoritative CSPICE output for selected cases).
  6. Provide an extensible foundation to incrementally add other kernel support (CK, PCK, DSK, etc.) later.

============================================================
SECTION: ARCHITECTURAL GUIDING PRINCIPLES
============================================================
1. SOLID with emphasis on SRP; each class/record has a single reason to change.
2. Immutability by default: use records / readonly structs.
3. Separation of concerns: parsing/IO vs math vs orchestration.
4. Avoid premature abstraction; add interfaces only when multiple implementations are plausible.
5. Favor static extension methods for pure transformations.
6. Explicit units (km, km/s, TDB seconds past J2000).
7. Hide unsafe / pointer details internally.
8. Optimize after correctness (benchmark driven).
9. Internalize DAF complexity; external API is semantic (GetState).
10. Rich XML docs for public APIs.

============================================================
SECTION: PROJECT STRUCTURE (INITIAL)
============================================================
Solution: SpiceNet.sln
Projects (net9.0):
  1. Spice.Core
  2. Spice.IO
  3. Spice.Kernels
  4. Spice.Ephemeris
  5. Spice.Tests
  6. Spice.IntegrationTests
  7. (Optional) Spice.Benchmarks
  8. Tooling (Console, ApiScan)

============================================================
SECTION: CODING CONVENTIONS / STYLE ALIGNMENT
============================================================
Honor .editorconfig: 2-space indent, file-scoped namespaces, predefined types, readonly fields, span-based parsing, minimal allocations, collection expressions, no duplicate tolerance literals (central policy only), all new public APIs need XML docs & tests.

============================================================
SECTION: DOMAIN MODEL (INITIAL SKETCH)
============================================================
Core primitives: BodyId, FrameId, Duration, Instant, StateVector, Vector3d.

============================================================
SECTION: SEQUENTIAL PROMPTS FOR COPILOT AGENT (PHASE 1 COMPLETED)
============================================================
PROMPT 1 .. PROMPT 12 (Completed) — see Git history & tests.

============================================================
SECTION: NEXT PHASE ROADMAP (PHASE 2: REAL KERNEL SUPPORT)
============================================================
Status Legend: ✅ Done | ▶ Pending | ⏸ Postponed

PROMPT 13: ✅ Full DAF low-level reader (directory traversal, endianness, validation).
PROMPT 14: ✅ Real SPK multi-record Type 2 & 3 parsing (descriptors, per-record MID/RADIUS).
PROMPT 15: ✅ EphemerisDataSource abstraction (stream vs memory-mapped, lazy loading).
PROMPT 16: ✅ JPL testpo integration (parser, mapping JSON, comparison harness).
PROMPT 17: ✅ Tolerance-based golden tests; stats artifact emission.
PROMPT 18: ✅ Caching/index layer (binary search segment lookup, fast path).
PROMPT 19: ✅ Extended TT->TDB conversion (higher-order periodic terms, pluggable strategy stub).
PROMPT 20: ⏸ Body & frame metadata loader (FK/PCK minimal subset) – postponed.
PROMPT 21: ⏸ Diagnostic / validation CLI enrichment – postponed.
PROMPT 22: ✅ GitHub Actions CI workflow (PR/master tests; tag -> full tests + NuGet publish; caching implemented).
PROMPT 23: ▶ Refactor TimeConversionService into pluggable strategies (ILeapSecondProvider, ITdbOffsetModel).
PROMPT 24: ▶ Structured logging (loading & segment selection trace via ILogger, in-memory test logger).
PROMPT 25: ▶ Performance consolidation (SIMD Chebyshev, pooling, bounds-check minimization, perf report).
PROMPT 26: ✅ Consolidation / Quality Alignment (tolerances, mapping, stats, docs, meta-kernel removal, API lock prep).

============================================================
SECTION: PROMPT 22 – CI WORKFLOW SUMMARY
============================================================
GitHub Actions (./github/workflows/ci.yml):
- pull_request (-> master): restore, build, unit tests only (Spice.Tests).
- push to master: same unit tests.
- tag push v*: full build + unit & integration tests + pack & publish NuGet packages (Spice.Core, Spice.IO, Spice.Kernels, Spice.Ephemeris) with version from tag (strip leading 'v').
- Metadata in csproj: Author Edmondo Silvestri, License MIT, Repository URL.
- Secret NUGET_API_KEY used on tag publish.
- Artifacts: .nupkg uploaded on tag builds.

============================================================
SECTION: DATA SOURCES & TEST FIXTURES GUIDELINES
============================================================
- Keep fixtures minimal (time-window reduced public-domain kernels).
- testpo subset curated; cite source.
- No proprietary / license-restricted kernels.

============================================================
SECTION: VALIDATION & ERROR BUDGET
============================================================
- Target double-precision parity; investigate relative deviation > 1e-10.
- Document approximations (TT->TDB series order, frame simplifications) in README.
- All tolerances centralized (TolerancePolicy).

============================================================
SECTION: CONTRIBUTION WORKFLOW (UPDATED NOTES)
============================================================
- Update roadmap & docs when completing a prompt.
- PRs: unit tests must pass; integration tests gated to tag releases.
- Create tag vX.Y.Z to publish packages.
- Public API changes require updating PublicAPI.Unshipped.txt.
- Benchmark performance-sensitive changes before/after (when Benchmarks project active).

============================================================
SECTION: NEXT ACTION
============================================================
Proceed with Prompt 23 implementation (pluggable time strategies) keeping public surface unchanged.

============================================================
END OF MANIFEST
