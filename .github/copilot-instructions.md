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
   - Query facade orchestrates: (a) time conversion, (b) segment selection, (c) interpolation, (d) frame assumptions.
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
     - Domain primitives: Instant, TimeScales, BodyId, FrameId, StateVector, Vector3d, Matrix3x3, Quaterniond, PhysicalConstants.
     - Time conversion services (pure logic) + extension methods.
     - Numeric utilities (polynomial evaluation, Chebyshev helpers) with vectorization where beneficial.

  2. Spice.IO
     - Low-level kernel access: abstraction IKernelSource (stream/memory/mmap), binary readers using Span<byte>.
     - DAF/Segment table parsing building neutral representations.
     - Text kernel line tokenizer with robust comment + continuation handling.

  3. Spice.Kernels
     - Parsers producing strongly typed kernel models: LskKernel, SpkKernel, SpkSegment (generic & typed), MetaKernel.
     - Segment selection & interpolation strategies (Chebyshev evaluation for ephemeris records).
     - Index & caching policies.

  4. Spice.Ephemeris
     - High-level EphemerisService / interface IEphemerisProvider.
     - Body resolution & state queries (including chaining across reference frames if needed).
     - Kernel management & load/unload orchestration.

  5. Spice.Tests (xUnit)
     - Golden numeric tests (with tolerances), parser tests (LSK, SPK headers), time conversions.
     - Property-based tests for algebraic identities (optional later via FsCheck).

  6. (Optional later) Spice.Benchmarks (BenchmarkDotNet) for hot paths (Chebyshev evaluation, segment selection, time conversions).

Namespaces align with project (file-scoped):
  Spice.Core.*
  Spice.IO.*
  Spice.Kernels.*
  Spice.Ephemeris.*

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

============================================================
SECTION: DOMAIN MODEL (INITIAL SKETCH)
============================================================
Value Types / Records:
  readonly record struct BodyId(int Value);
  readonly record struct FrameId(int Value);
  readonly record struct Instant(long TdbSecondsFromJ2000); // core time representation
  readonly record struct Duration(double Seconds);
  readonly record struct Vector3d(double X, double Y, double Z);
  readonly record struct StateVector(Vector3d PositionKm, Vector3d VelocityKmPerSec);
  readonly record struct ChebyshevCoefficients(double[] Coefs); // consider ReadOnlyMemory<double>

Other:
  enum TimeScale { UTC, TAI, TT, TDB }
  static class TimeConversions (extension methods for Instant, DateTimeOffset) => conversions require loaded leap seconds.
  record LskKernel(IReadOnlyList<(Instant EffectiveFrom, double TaiMinusUtcSeconds)> Entries, Instant? LastChange ...);
  record SpkKernel(HeaderInfo Header, IReadOnlyList<SpkSegment> Segments);
  abstract record SpkSegment(BodyId Target, BodyId Center, FrameId Frame, int DataType, ...);
  Derived segment records per SPK type (e.g., SpkType2Segment, SpkType3Segment, etc.) each containing interpolation meta + raw coefficient store reference.

============================================================
SECTION: SUPPORTED SPK SEGMENT TYPES (CLARIFY NEEDED)
============================================================
Common planetary ephemeris kernels (DE series) typically use Type 2 or 3 (Chebyshev position or position & velocity). Need explicit confirmation which of:
  - Type 2 (Chebyshev position, separate derivation for velocity)
  - Type 3 (Chebyshev position & velocity)
  - Type 5 (Two-body propagation / discrete states)
  - Type 13 (Chebyshev position & velocity for TCB scale variant)
For MVP assume Type 2 + Type 3 unless clarified otherwise.

============================================================
SECTION: FUNCTIONAL FLOWS
============================================================
Loading:
  EphemerisService.Load(metaKernelPath)
    -> Parse meta-kernel (list of KERNELS_TO_LOAD)
    -> For each file: dispatch by extension (".bsp" => SpkKernelParser; ".tls" => LskParser)
    -> Update internal KernelRegistry & recompute leap second table.

Querying:
  EphemerisService.GetState(BodyId target, BodyId center, Instant t)
    -> Resolve candidate segments (binary search by time window)
    -> Segment selection precedence (highest priority / last-loaded? mimic CSPICE logic eventually)
    -> Interpolate using segment's data type algorithm (Chebyshev evaluation with scaled time tau in [-1,1])
    -> Return StateVector.

Time Conversion (UTC -> TDB):
  Parse UTC => DateTimeOffset
  Apply leap seconds to get TAI
  TAI -> TT (add 32.184s)
  TT -> TDB (approx: add relativistic periodic terms; allow strategy pattern or pluggable refinement; start with simple linear ~same as TT for MVP) 

============================================================
SECTION: PERFORMANCE & MEMORY GUIDELINES
============================================================
- Favor memory-mapped file support (FileStream + MemoryMappedFile) behind an abstraction for large SPK kernels.
- Defer materializing coefficient arrays; store spans pointing into shared memory buffer when safe.
- Cache per-segment polynomial degree & number of records to accelerate record index resolution.
- Vectorize Chebyshev polynomial evaluation (Clenshaw) using System.Numerics where beneficial for 3-component evaluation simultaneously.
- Provide benchmark harness later to guide further tuning.

============================================================
SECTION: ERROR HANDLING & VALIDATION
============================================================
Custom exception hierarchy under Spice.* (e.g., SpiceKernelNotFoundException, SpiceTimeConversionException, SpiceSegmentNotFoundException).
Fail fast on structural inconsistencies (invalid DAF summary count, unexpected endianness) with clear diagnostic messages.
Return Try* variants (e.g., TryGetState) for performance-sensitive loops to avoid exception overhead in expected-miss cases.

============================================================
SECTION: TEST STRATEGY
============================================================
- Use xUnit + Shouldly (preferred) else Assert.* only.
- Store small reference kernels (public domain) under test assets.
- Golden values produced once via authoritative CSPICE + script; assert absolute & relative tolerances (e.g., |delta| < 1e-9 km, rel < 1e-12 where feasible).
- Property tests for invariants: zero velocity derivative checks (where polynomial degree < needed), frame identity transformations.
- Fuzz tests for time range boundaries (segment start/end off-by-epsilon).

============================================================
SECTION: DOCUMENTATION
============================================================
- XML doc comments mandatory on public types & members; include units & references to NAIF required reading sections (no large verbatim copies, just citations).
- Provide README per project summarizing scope & boundaries.
- Top-level README: usage snippet (load LSK + SPK + query state) & disclaimers about not being an official NAIF distribution.

============================================================
SECTION: LICENSE & COMPLIANCE NOTES
============================================================
- Confirm NAIF CSPICE license compatibility. Avoid embedding original CSPICE source; reimplement clean-room logic referencing public documentation only.
- Cite CSPICE and JPL/NAIF in documentation.
- Do not copy large comment blocks verbatim; summarize.

============================================================
SECTION: SOURCE GENERATION (OPTIONAL LATER)
============================================================
Potential Roslyn source generator for:
  - Autogenerating known body & frame ID constants from a small declarative table.
  - Embedding build metadata (library version, commit hash).
Defer until core functionality is stable.

============================================================
SECTION: SEQUENTIAL PROMPTS FOR COPILOT AGENT
============================================================
PROMPT 1:
"Initialize a new solution 'SpiceNet.sln' targeting .NET 9. Create projects: Spice.Core, Spice.IO, Spice.Kernels, Spice.Ephemeris, Spice.Tests (xUnit). Configure Directory.Build.props for common settings (nullable enable, warnings as errors). Create Directory.Packages.props for Central Package Management including test & assertion packages (xUnit, Shouldly, coverlet). Add project references (Ephemeris -> Kernels -> IO + Core; Kernels -> IO + Core; IO -> Core; Tests -> all). Add initial README placeholders."

PROMPT 2:
"In Spice.Core implement immutable value types: Vector3d, StateVector, Duration, Instant, BodyId, FrameId (readonly record structs). Add basic operations (addition, subtraction, scaling) as static extension methods. Implement Chebyshev evaluation utility (scalar & vector) with XML docs. Include unit tests for arithmetic & Chebyshev correctness (simple known polynomial)."

PROMPT 3:
"Implement TimeScales utilities in Spice.Core: enumerations + TimeConversionService (static partial). Provide UTC->TAI, TAI->TT, TT->TDB (placeholder simple TT==TDB). Introduce LskKernel model (record) but no parser yet. Add tests with synthetic leap second table verifying conversions."

PROMPT 4:
"In Spice.IO implement a DAFReader capable of: (a) validating file identification word (e.g., 'DAF/SPK '), (b) reading ND, NI, RECORDS counts, (c) enumerating summaries returning raw double/int arrays, (d) extracting segment metadata blocks into a neutral structure. Use Span<byte>, BinaryPrimitives for endianness. Provide tests with a minimal handcrafted binary fixture (create test builder)."

PROMPT 5:
"In Spice.Kernels implement SpkKernelParser that consumes a Stream and yields SpkKernel with SpkSegments (generic interim representation capturing: Target, Center, Frame, DataType, StartTdbSec, StopTdbSec, RecordStart, RecordEnd, etc.). For now support Type 2 & Type 3 only with raw coefficient blocks stored as double[] slices. Add tests using a small synthetic SPK built by a test helper that encodes one segment and one record."

PROMPT 6:
"Extend Chebyshev logic to compute position (and velocity if Type 3) from coefficient record given evaluation epoch. Implement SpkSegmentEvaluator with method EvaluateState(SpkSegment seg, Instant t) returning StateVector. Add tests verifying exact reconstruction of polynomial used to build synthetic segment."

PROMPT 7:
"Implement LskParser in Spice.Kernels parsing text leap second kernel (lines starting with 'DELTET/DELTA_AT=' etc.). Populate LskKernel. Integrate with TimeConversionService via registration method TimeConversionService.SetLeapSeconds(LskKernel). Add tests parsing a miniature LSK file."

PROMPT 8:
"Implement MetaKernelParser supporting simple KERNELS_TO_LOAD block (quoted paths). Provide KernelRegistry service to track loaded kernels. Add tests ensuring relative and absolute path handling."

PROMPT 9:
"Implement EphemerisService in Spice.Ephemeris: holds KernelRegistry, provides Load(metaKernelPath) and GetState(BodyId target, BodyId center, Instant t). Segment selection: choose segment where t in [start, stop] with latest (max start) precedence. Provide tests with overlapping synthetic segments verifying precedence."

PROMPT 10:
"Add high-level integration test: Load synthetic LSK + SPK via meta-kernel, query state at mid-epoch, assert expected position & velocity results. Document sample usage in root README."

PROMPT 11 (Performance Optional):
"Add BenchmarkDotNet project Spice.Benchmarks measuring Chebyshev evaluation and segment lookup with 10k random epochs. Optimize hotspots (vectorization, caching). Document results in /docs/perf.md." 

PROMPT 12 (Refinement):
"Replace placeholder TT->TDB conversion with more accurate series (e.g., using standard approximate formula with periodic terms). Add tests verifying difference within expected microsecond range vs reference values."

============================================================
SECTION: OPEN CLARIFICATIONS REQUIRED BEFORE SOME STEPS
============================================================
Please clarify (these influence implementation details):
  1. Which SPK data types must be supported in Phase 1 (confirm list: 2, 3, others)?
  2. Which ephemeris datasets (e.g., DE430, DE440) will be used for validation? Provide example kernel file names.
  3. Acceptable external dependencies? (BenchmarkDotNet, FluentAssertions, FsCheck). Any restrictions?
  4. Accept use of unsafe code + MemoryMarshal for performance-critical parsing? (Default: internal only if measurable benefit.)
  5. Should we implement memory-mapped file support immediately or defer to later optimization phase?
  6. Precision expectations: required absolute / relative tolerance for position (km) & velocity (km/s)?
  7. Need frame transformations beyond default inertial (e.g., J2000) in Phase 1? If yes, list frames.
  8. Licensing constraints or headers to embed in each source file?
  9. Do we need asynchronous loading APIs or is synchronous acceptable for Phase 1?
 10. Maximum kernel file size we should design for (impacts streaming vs full load)?

Answered Clarifications (Authoritative for subsequent prompts):
  1. SPK Types: Support Types 2 and 3 in MVP; design abstractions to extend to additional types (e.g., 5, 13) later without breaking API.
  2. Validation Datasets: Use JPL planetary ephemerides DE440 and DE441 (provide sample kernel filenames in tests when added).
  3. Dependencies: Do NOT use FluentAssertions; prefer Shouldly for assertions. Allow BenchmarkDotNet. Use FsCheck only if it adds clear value and poses no reliability risk. Keep core libraries dependency-light.
  4. Unsafe Code: Permitted internally for proven performance wins (e.g., span pinning, MemoryMarshal) while keeping public API safe.
  5. Memory-Mapped Files: Defer initial implementation; architect I/O layer for pluggable memory-mapped source later.
  6. Precision: Target parity with official CSPICE numerical results (within double precision round-off; effectively matching CSPICE to machine precision for tested cases).
  7. Frames: No additional frame transformations beyond default inertial/J2000 in Phase 1.
  8. License: Plan MIT license (subject to CSPICE license compatibility review). No per-file license headers required.
  9. Loading APIs: Prefer asynchronous kernel loading (provide async variants; synchronous wrappers optional).
 10. Kernel Size: Assume potentially very large kernel files; design for streaming/segment-on-demand access and avoid full materialization by default.

============================================================
SECTION: CONTRIBUTION WORKFLOW (FUTURE)
============================================================
- Enforce analyzers: Microsoft + recommended Roslyn, treat warnings as errors (except specific numeric precision warnings if noisy).
- Add GitHub Actions: build, test, (optional) benchmark diff summary.

============================================================
END OF MANIFEST
