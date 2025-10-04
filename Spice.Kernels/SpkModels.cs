// CSPICE Port Reference: N/A (original managed design)
namespace Spice.Kernels;

using Spice.Core;

/// <summary>
/// In-memory SPK kernel representation containing a collection of generic SPK segments.
/// </summary>
public sealed record SpkKernel(IReadOnlyList<SpkSegment> Segments);

/// <summary>
/// Generic interim SPK segment representation (Types 2 & 3 for MVP) exposing raw coefficient block.
/// Times are TDB seconds past J2000 (same scale as Instant).
/// </summary>
public sealed record SpkSegment(
  BodyId Target,
  BodyId Center,
  FrameId Frame,
  int DataType,
  double StartTdbSec,
  double StopTdbSec,
  int CoefficientOffset, // offset (in doubles) from start of coefficient area
  int CoefficientCount,  // number of doubles for this segment
  double[] Coefficients  // materialized coefficient slice (length == CoefficientCount)
);
