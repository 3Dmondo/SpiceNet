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
  4. Embrace modern .NET 9 performance features while retaining clarity.
  5. Maintain rigorous automated test coverage and auditable numeric tolerances.
  6. Provide an extensible foundation for later kernel support.

============================================================
SECTION: ARCHITECTURAL GUIDING PRINCIPLES
============================================================
1. SOLID / SRP.
2. Immutability by default.
3. Strict separation: IO/parsing vs domain math vs query orchestration.
4. Minimal necessary abstraction; interfaces only when multiple implementations are expected.
5. Explicit units in all public APIs.
6. Encapsulate DAF/SPK complexity; facade exposes semantic operations.
7. Optimize after correctness; benchmark before & after.
8. Minimal public surface (primitives + `EphemerisService`).
9. Deterministic artifacts (stats JSON) for regression tracking.
10. Comprehensive XML documentation.

============================================================
SECTION: PROJECT STRUCTURE
============================================================
Solution: SpiceNet.sln
Projects (net9.0): Core, IO, Kernels, Ephemeris, Tests, IntegrationTests, Benchmarks (optional), Console.Demo.

============================================================
SECTION: CODING CONVENTIONS
============================================================
(Refer to .editorconfig; summarized previously – unchanged.)

============================================================
SECTION: SEQUENTIAL PROMPTS (PHASE 2)
============================================================
Status Legend: ✅ Completed | ▶ Pending | ⭕ Optional

| Prompt | Title | Status | Notes |
|--------|-------|--------|-------|
| 13 | Full DAF Reader | ✅ | Endianness + summaries + names + control words |
| 14 | Real SPK Multi-Record | ✅ | Types 2/3 trailer & per-record MID/RADIUS |
| 15 | EphemerisDataSource (Lazy/MM) | ✅ | Stream vs memory-map abstraction |
| 16 | testpo Integration | ✅ | Parser + mapping JSON + comparisons |
| 17 | Tolerance Golden Tests | ✅ | Policy tiers + stats JSON artifact |
| 18 | Segment Indexing | ✅ | Binary search + boundary fast path |
| 19 | Extended TT->TDB Model | ✅ | Pluggable offset strategy (basic vs extended harmonics) |
| 20 | Body/Frame Metadata Loader | ▶ | Minimal FK/PCK subset |
| 21 | Diagnostic CLI Enrichment | ▶ | Public facade only tooling |
| 22 | CI Workflow | ▶ | Build/test + artifact publish |
| 23 | Pluggable Time Strategies | ▶ | ILeapSecondProvider / ITdbOffsetModel refactor |
| 24 | Structured Logging | ▶ | Segment selection trace |
| 25 | Perf Consolidation | ▶ | SIMD Chebyshev + pooling + perf report |
| 26 | Consolidation | ✅ | Tolerances, mapping, stats, docs, meta-kernel removal |

Post-Consolidation Sub-Prompts:

| Prompt | Description | Status | Priority |
|--------|-------------|--------|----------|
| 26B | Public API Analyzer Baseline (API Lock) | ✅ | High |
| 26C | Diagnostic CLI Audit (H1) ensure public-only | ▶ | High |
| 26D | Micro Benchmarks (record selection, barycentric warm/cold) | ⭕ | Optional |

============================================================
SECTION: PROMPT 19 IMPLEMENTATION SUMMARY
============================================================
Added pluggable TT->TDB offset model inside `TimeConversionService` with interface `ITdbOffsetModel` and two strategies: basic (2-term) and extended (adds 3rd & 4th harmonics). Default remains basic to preserve prior behavior; switching models resets J2000 alignment.

============================================================
SECTION: NEXT RECOMMENDED STEP
============================================================
Proceed to Prompt 20 (Body/Frame Metadata Loader) after completing diagnostics audit (26C) if tooling requires frame name resolution; otherwise implement minimal FK/PCK parser returning radii & frame name map (internal) + tests.

============================================================
END OF MANIFEST
