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
/// Coefficient layout (synthetic test format):
///   Type 2: 3*(N+1) doubles => X(c0..cN), Y(c0..cN), Z(c0..cN)
///   Type 3: 6*(N+1) doubles => PosX,PosY,PosZ then VelX,VelY,VelZ (each (N+1) Chebyshev series)
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
