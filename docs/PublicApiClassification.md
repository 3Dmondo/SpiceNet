# Public API Classification (Phase 0 – Prompt 26)

Purpose: Determine which currently public symbols remain part of the supported facade and which should be internalized before locking the baseline with Public API analyzers.

Status Legend:
- Keep: Remains public facade / primitive.
- Internalize: Change to internal (tests gain access via InternalsVisibleTo).
- Review: Temporarily public; may become internal after downstream refactor (document rationale).
- Deprecate: Candidate for removal / merge.

## Facade Principles
1. External consumers should need only: core value primitives (identifiers, vectors, instants, state), the ephemeris service, and (optionally) an entry point for loading kernels if not exposed via the service.
2. Low-level parsing, DAF traversal, and evaluator details are implementation concerns; making them public increases maintenance burden.
3. Public surface should be minimal, stable, and unit documented.

## Recommended Public Surface (Target Set)
| Symbol | Assembly | Kind | Classification | Rationale |
|--------|----------|------|----------------|-----------|
| `Vector3d` | Spice.Core | struct | Keep | Fundamental math primitive (km) |
| `StateVector` | Spice.Core | struct/record (assumed) | Keep | Returned by service; position+velocity container |
| `Instant` | Spice.Core | struct/record (assumed) | Keep | Time primitive (TDB seconds since J2000) |
| `BodyId` | Spice.Core | struct/record (assumed) | Keep | Strongly typed body identifier |
| `FrameId` | Spice.Core | struct/record (assumed) | Review | Might be hidden until frame transforms exposed; keep if already widely used in tests |
| `EphemerisService` | Spice.Ephemeris | class | Keep | Main user entry point for state queries & kernel loading |

(If additional tiny primitives exist in `Primitives.cs` they follow same pattern: Keep if needed in method signatures.)

## Candidates to Internalize
| Symbol | Assembly | Kind | Action | Notes |
|--------|----------|------|--------|-------|
| `Chebyshev` | Spice.Core | static class | Internalize | Implementation detail of evaluation; hide to reduce API; re-expose later if needed for advanced users |
| `DafReader` | Spice.IO | class | Internalize | Likely superseded by `FullDafReader`; unify implementations |
| `FullDafReader` | Spice.IO | class | Internalize | Low-level DAF traversal not required externally |
| `IEphemerisDataSource` | Spice.IO | interface | Internalize | Abstraction for lazy coefficient access; not needed externally |
| `EphemerisDataSource` (factory/static) | Spice.IO | class | Internalize | Implementation detail for lazy loading |
| `TestPoLoader` (if public) | Spice.IO | class | Internalize | Test harness / integration only |
| `SpkSegment` | Spice.Kernels | record | Internalize | Structural segment representation; external code should not depend on layout |
| `SpkKernel` | Spice.Kernels | record | Internalize | Container of segments; not needed if service is primary facade |
| `SpkSegmentEvaluator` | Spice.Kernels | static class | Internalize | Implementation detail of interpolation |
| `SpkKernelParser` | Spice.Kernels | class | Internalize | Synthetic parser (Phase 1) – unify under real parser or service loader |
| `RealSpkKernelParser` | Spice.Kernels | static class | Internalize | Parser entry replaced by `EphemerisService` high-level loading (optionally a thin wrapper kept public if needed) |
| `MetaKernelParser` | Spice.Kernels | static class | Internalize | Hide; `EphemerisService.Load` is facade |
| `LskParser` | Spice.Kernels | static class | Internalize | Implementation detail of leap second ingestion |
| `DafCommentUtility` | Spice.Kernels | static class | Internalize | Implementation/support only |
| `KernelRegistry` (if public) | Spice.Ephemeris | class | Internalize | Internal bookkeeping for loaded kernels |

## Symbols Needing Confirmation (Review)
| Symbol | Assembly | Current Use | Proposed Status | Justification Path |
|--------|----------|-------------|-----------------|--------------------|
| `FrameId` | Spice.Core | Appears in `SpkSegment.Frame` | Review (maybe Keep) | If frames appear in future orientation/transform API, keep; else internalize until needed |
| Any time conversion service types (e.g., `TimeConversionService`) | Spice.Core/Kernels | Used by service | Internalize | Provide façade wrappers or extension methods later if demanded |

## Additional Actions
| Task | Description |
|------|-------------|
| API Scan Baseline | Commit current `artifacts/api-scan.json` as `docs/api-scan.initial.json` for historical reference (do not treat as authoritative going forward—PublicAPI analyzer will). |
| Add InternalsVisibleTo | Add attributes to each library project for `Spice.Tests`, `Spice.IntegrationTests`, `Spice.Benchmarks` where tests require internal types. |
| Documentation Section | Root README new section "Supported Public Surface" listing only final Keep symbols. |
| Analyzer Setup | Add `Microsoft.CodeAnalysis.PublicApiAnalyzers` package; generate `PublicAPI.Shipped.txt` with Keep list; remove others before baseline capture. |
| Backward Compatibility Note | Add statement: "Pre-1.0.0: Public surface may change without semantic version guarantees outside listed facade types." |

## Open Questions
1. Should raw kernel parsing be optionally accessible for tooling scenarios? (If yes, expose a narrowly scoped `IKernelLoader` interface instead of concrete parsers.)
2. Do we need to expose frame identifiers now or defer until orientation work (later prompts)?
3. Will advanced users require direct Chebyshev evaluation utilities? (Defer until request.)

## Next Steps (Execution Order)
1. Internalize listed candidates; adjust tests via `InternalsVisibleTo`.
2. Re-run ApiScan; validate only Keep/Review symbols appear.
3. Add Public API analyzer baseline (treat Review items as Unshipped or mark with TODO comment).
4. Update README + Prompt26 to reflect Phase 0 progress.

## Appendix: Future Exposure Criteria
- New type must appear in at least one public method signature of `EphemerisService` OR represent an immutable value concept widely required externally (time, id, vector).
- Parser / IO utilities remain internal unless a tooling extension scenario emerges; in that case expose via small, stable interfaces.

---
*Generated automatically for Phase 0 planning; refine after first pruning pass.*
