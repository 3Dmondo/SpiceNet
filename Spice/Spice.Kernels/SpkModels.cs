// CSPICE Port Reference: N/A (original managed design)
namespace Spice.Kernels;

using Spice.Core;
using Spice.IO;

/// <summary>
/// In-memory SPK kernel representation containing a collection of generic SPK segments.
/// </summary>
internal sealed record SpkKernel(IReadOnlyList<SpkSegment> Segments);

/// <summary>
/// SPK segment representation supporting both synthetic single-record segments (Phase 1) and
/// real multi-record segments (Phase 2) for data types 2 and 3. Adds optional lazy coefficient
/// access (Prompt 15) through an ephemeris data source when full coefficient materialization
/// is not desired (large kernels). When <see cref="Lazy"/> is true the <see cref="Coefficients"/>
/// array may be empty and coefficients are fetched on-demand per record.
/// Includes trailer metadata for real type 2/3 segments: INIT (segment INIT value), INTLEN (uniform record length in seconds),
/// RSIZE (record size in doubles including MID/RADIUS), TN (record count from trailer) for validation & future indexing.
/// </summary>
internal sealed record SpkSegment(
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
  int RecordSizeDoubles = 0,  // total doubles per record including MID & RADIUS
  // Real kernel trailer metadata (type 2/3)
  double Init = 0d,
  double IntervalLength = 0d,
  int TrailerRecordSize = 0,
  int TrailerRecordCount = 0,
  // Lazy data source metadata
  IEphemerisDataSource? DataSource = null,
  long DataSourceInitialAddress = 0, // 1-based INITIAL address from summary (start of first record data)
  long DataSourceFinalAddress = 0,
  bool Lazy = false
);
