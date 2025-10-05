# Spice.Kernels
Parsers & models for SPK (Types 2 & 3) plus higher-level segment abstractions & interpolation helpers.

## Supported Features
- DAF-backed SPK segment enumeration via `FullDafReader` (real layout, multi-record, doubly-linked summary traversal).
- SPK Types: 2 (position Chebyshev; velocity from derivative) & 3 (position + velocity Chebyshev) multi-record segments.
- Trailer parsing: `[INIT, INTLEN, RSIZE, N]` validated against payload size; degree inferred from RSIZE and component count.
- Endianness handling (big / little) with heuristic detection from ND / NI plausibility; coefficient words byte-swapped when needed.
- Lazy & eager parsing modes (`RealSpkKernelParser.Parse` vs `ParseLazy`) using `IEphemerisDataSource` (stream or memory mapped) to avoid full materialization of large kernels.
- Per-record scaling arrays (MID, RADIUS) extracted to enable precise Chebyshev evaluation.

## Roadmap Status (subset)
| Prompt | Kernel Layer Concern | Status |
|--------|----------------------|--------|
| 13 | DAF traversal & control word decoding | ? |
| 14 | Real SPK multi-record parsing (Types 2,3) | ? |
| 15 | Lazy coefficient data source abstraction | ? |
| 18 | Segment indexing / fast lookup | ? |
| 25 | Vectorized Chebyshev evaluation | ? |

## Usage (Eager Parse)
```csharp
using var fs = File.OpenRead("example.bsp");
var kernel = RealSpkKernelParser.Parse(fs);
foreach (var seg in kernel.Segments)
  Console.WriteLine($"Target={seg.Target.Id} Center={seg.Center.Id} Type={seg.DataType} Records={seg.RecordCount} Degree={seg.Degree}");
```

## Usage (Lazy Parse / Memory Mapped)
```csharp
var kernel = RealSpkKernelParser.ParseLazy("example.bsp", memoryMap:true);
```

## Limitations / TODO
- No segment precedence or interval indexing yet (linear scan at query time).
- Only Types 2 & 3 supported (future: 5, 13, 20, 21).
- No frame transformations or aberration corrections (handled later in Ephemeris layer).

See `../docs/SpkDafFormat.md` for a concise description of the binary layout implemented here.
