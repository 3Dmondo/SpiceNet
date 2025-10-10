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

Initial functional scope (Phase 1 / MVP):
  - Binary SPK kernel reading (minimum necessary SPK segment types to support common planetary ephemerides: clarify subset).
  - LSK (Leap Seconds) text kernel parsing.
  - Basic meta-kernel (FURNSH-equivalent) loader for grouping kernel paths.
  - Time system conversions: UTC <-> TAI <-> TT <-> TDB/ET using loaded leap second + analytic approximation for TT-TDB (allow pluggable refinement).
  - Retrieval of barycentric/planet-centric state vectors for planetary bodies and major satellites from loaded kernels.
  - Unit tests validating state retrieval vs reference values.

============================================================
SECTION: ARCHITECTURAL GUIDING PRINCIPLES
============================================================
1. SOLID with emphasis on SRP; each class/record has a single reason to change.
2. Immutability by default: use 'record' / 'readonly record struct' for small value types; prefer 'init' setters over constructors for aggregate configuration objects.
3. Separation of concerns:
   - Parsing/IO layer (kernel decoding, binary layouts, endianness, indexing) isolated from domain math & query orchestration.
   - Domain math primitives independent of kernel format specifics.
   - Query facade orchestrates: (a) time conversion, (b) segment selection, (c) interpolation, (d) frame assumptions).
4. Avoid premature abstraction; introduce interfaces only when multiple implementations are plausible.
5. Favor static extension methods for pure functional transformations (Vector math, interpolation, time scale conversions) grouped by thematic static classes.
6. Enforce explicit units. Adopt kilometers (km) for position, km/s for velocity, seconds (SI) for durations, TDB seconds past J2000 for ephemeris time.
7. Expose high-level API free of raw pointer semantics; encapsulate unsafe code (if needed) internally.
8. Optimize after correctness. Provide baseline straightforward implementation with benchmarks to drive targeted optimization.
9. Internalize incidental complexity of SPK DAF architecture; external consumers operate at semantic level: GetState(bodyId, relativeToId, Instant).
10. Rich XML documentation on all public APIs; summary + param semantics + units + references.

============================================================
SECTION: PROPOSED PROJECT STRUCTURE (INITIAL)
============================================================
Solution: SpiceNet.sln

Projects (all target net9.0):
  1. Spice.Core
  2. Spice.IO
  3. Spice.Kernels
  4. Spice.Ephemeris
  5. Spice.Tests
  6. (Optional) Spice.Benchmarks

============================================================
SECTION: CODING CONVENTIONS / STYLE ALIGNMENT
============================================================
Honor existing .editorconfig:
  - Indent size 2 spaces, file-scoped namespaces, expression-bodied members where allowed.
  - Use predefined types (int, double) & pattern matching features.
  - Prefer readonly fields & struct immutability.
  - Favor 'readonly record struct' for small vector/matrix/instant types.
  - Use 'checked' context only where overflow risk requires detection.
  - Avoid unnecessary heap allocations; prefer Span/ReadOnlySpan for transient buffers.
  - Central Package Management (CPM) via Directory.Packages.props for all NuGet versions; individual project files must omit Version attributes.
  - After any automated edits the agent MUST build (dotnet build) and run tests (dotnet test); failures must be fixed before responding completion.
  - Implicit usings are enabled; do NOT add explicit using directives for namespaces already covered by implicit usings unless required for disambiguation.
  - Prefer collection expressions / collection initializers (e.g., `[1.0, 2.0]`, `[]`, or `new() { ... }`).
  - When porting logic directly from a CSPICE C source file, add header comment: `// CSPICE Port Reference: <relative-path>` else mark as original design.
  - For performance-sensitive parsing, prefer Span<T>, MemoryMarshal, and (when justified) internal unsafe blocks guarded by benchmarks.
  - All new public APIs require XML docs and explicit unit tests.

============================================================
SECTION: DOMAIN MODEL (INITIAL SKETCH)
============================================================
(unchanged for brevity)

============================================================
SECTION: SEQUENTIAL PROMPTS FOR COPILOT AGENT (PHASE 1 COMPLETED)
============================================================
PROMPT 1 .. PROMPT 12 (Completed) — see Git history & tests.

============================================================
SECTION: NEXT PHASE ROADMAP (PHASE 2: REAL KERNEL SUPPORT)
============================================================
PROMPT 13: Full DAF low-level reader. (Done)
PROMPT 14: Real SPK parsing Types 2 & 3. (Done)
PROMPT 15: EphemerisDataSource abstraction & lazy coeff access. (Done)
PROMPT 16: testpo integration (Initial parsing + cache + comparison harness). (In Progress: basic comparisons active, Earth/Moon mapping added.)
PROMPT 16a/16b: code/center inventory & mapping. (Partial – provisional mapping 3?399, 10?301.)
PROMPT 16c: Relative state resolver (barycentric chain). (Done)
PROMPT 16d: Integration refactor using relative states. (In Progress)
PROMPT 16e: Diagnostic CLI testpo-diagnose. (Planned)
PROMPT 16f: Velocity semantics validation. (Planned)
PROMPT 17: Golden tolerance harness. (Partially realized with dynamic AU-based tolerances.)
PROMPT 18: Segment indexing / fast lookup. (Baseline binary search done.)
PROMPT 19+: Pending per original list.

============================================================
SECTION: DATA SOURCES & TEST FIXTURES GUIDELINES
============================================================
- Real kernel fixtures SHOULD be the smallest public-domain slices sufficient for tests.
- testpo reference files: cached per ephemeris; parser normalizes Earth (3?399) & Moon (10?301) codes.
- Never commit large proprietary or license-restricted kernels.

============================================================
SECTION: VALIDATION & ERROR BUDGET (UPDATED)
============================================================
Integration test comparisons currently operate in AU (positions) and AU/day (velocities) matching testpo output:
  Primary strict tolerances (when AU constant found in BSP comments):
    Position: 1e-13 AU  (~1.50e-5 km ? 1.5 cm)
    Velocity: 1e-16 AU/day (~1.73e-13 km/s)
  If AU constant missing, tolerances are relaxed ×10,000:
    Position: 1e-9 AU  (~149.6 m)
    Velocity: 1e-12 AU/day (~1.73e-9 km/s)
  For early DE2xx series (ephemeris number starting with '2') an additional ×100 relaxation (overall ×1e6) applies:
    Position: 1e-7 AU  (~14.96 km)
    Velocity: 1e-10 AU/day (~1.73e-11 km/s)
  Earth/Moon special handling derives Earth & Moon barycentric states via EMB + EMRAT (extracted from SPK comments) to reduce large residuals.
Target long-term goal: reconcile and re-express tolerances directly in km and km/s once all unit pathways & scaling verified.
Investigate any systematic deviation >1e-10 relative (AU or AU/day domain) when strict tolerance regime active.

============================================================
SECTION: CONTRIBUTION WORKFLOW (UPDATED NOTES)
============================================================
- Update this manifest when tolerances, mappings, or special-case logic (e.g., Earth/Moon) change.
- Provide rationale in PR description for any tolerance relaxation.
- Benchmarks must run locally before merging performance-related PRs; include summary.

============================================================
END OF MANIFEST
