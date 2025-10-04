// CSPICE Port Reference: N/A (original managed design)
namespace Spice.Kernels;

using Spice.Core;

/// <summary>
/// In-memory SPK kernel representation containing a collection of generic SPK segments.
/// </summary>
public sealed record SpkKernel(IReadOnlyList<SpkSegment> Segments);

/// <summary>
/// SPK segment representation supporting both synthetic single-record segments (Phase 1) and
/// real multi-record segments (Phase 2) for data types 2 and 3.
/// Coefficient layout:
///   Type 2 (position only): per record => MID, RADIUS, then 3*(DEG+1) Chebyshev coeffs (X, Y, Z sequences).
///   Type 3 (position+velocity): per record => MID, RADIUS, then 6*(DEG+1) coeffs (pos then vel components).
/// For single-record synthetic segments the evaluator derives MID/RADIUS from Start/Stop and ignores Record* fields.
/// For multi-record segments RecordMids/RecordRadii length equals RecordCount. Coefficients contain concatenated
/// records (including their MID/RADIUS leading values) with uniform RecordSizeDoubles.
/// </summary>
public sealed record SpkSegment(
  BodyId Target,
  BodyId Center,
  FrameId Frame,
  int DataType,
  double StartTdbSec,
  double StopTdbSec,
  int CoefficientOffset,
  int CoefficientCount,
  double[] Coefficients,
  // Multi-record metadata (null for single record synthetic segments)
  int RecordCount = 1,
  int Degree = 0,
  double[]? RecordMids = null,
  double[]? RecordRadii = null,
  int ComponentsPerSet = 0, // 3 (type2) or 6 (type3) when multi-record
  int RecordSizeDoubles = 0  // total doubles per record including MID & RADIUS
);
