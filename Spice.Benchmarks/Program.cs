// CSPICE Port Reference: N/A (original managed design)
using Spice.Core;
using Spice.Kernels;
using Spice.Ephemeris;
using System.Globalization;

// Temporary diagnostic main replacing BenchmarkDotNet runner.
// Usage:
//   dotnet run -c Debug -- <file> <targetId> <centerId> <etSeconds> [--list] [--comments]
// Adding --list prints structural info for all parsed segments (real SPK).
// Adding --comments prints the DAF comment area (and parsed symbol assignments) then exits if only inspection desired.

if (args.Length < 4)
{
  Console.WriteLine("SpiceNet EPHEMERIS QUICK TEST\n" +
                    "Args: <kernel-or-meta> <target> <center> <etSeconds> [--list] [--comments]\n" +
                    "Example: dotnet run -- de_test.bsp 499 0 0\n" +
                    "Example: dotnet run -- de_test.bsp 499 0 0 --list --comments");
  Console.WriteLine("(Benchmark harness disabled for this diagnostic run.)");
  return;
}

bool list = args.Contains("--list", StringComparer.OrdinalIgnoreCase);
bool showComments = args.Contains("--comments", StringComparer.OrdinalIgnoreCase) || args.Contains("--show-comments", StringComparer.OrdinalIgnoreCase);
string file = args[0];
if (!File.Exists(file)) { Console.WriteLine($"File not found: {file}"); return; }
if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetId) ||
    !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var centerId) ||
    !double.TryParse(args[3], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var et))
{
  Console.WriteLine("Invalid numeric arguments");
  return;
}

var target = new BodyId(targetId);
var center = new BodyId(centerId);
var epoch = new Instant((long)Math.Round(et));

try
{
  // If user only wants comments, do that early for .bsp
  if (showComments && Path.GetExtension(file).Equals(".bsp", StringComparison.OrdinalIgnoreCase))
  {
    var (comments, symbols) = DafCommentUtility.Extract(file);
    Console.WriteLine("-- COMMENT AREA (filtered printable lines) --");
    if (comments.Length == 0) Console.WriteLine("(no comments)");
    else foreach (var c in comments) Console.WriteLine(c);

    if (symbols.Count > 0)
    {
      Console.WriteLine();
      Console.WriteLine("-- PARSED SYMBOL ASSIGNMENTS --");
      foreach (var s in symbols)
      {
        Console.WriteLine(s.ToString());
      }
    }
    Console.WriteLine();
    // Continue with state evaluation unless user passed only --comments without list? We still proceed unless target/center meaningless.
  }

  StateVector state;
  string ext = Path.GetExtension(file).ToLowerInvariant();
  if (ext == ".tm")
  {
    using var svc = new EphemerisService();
    svc.Load(file);
    if (list) Console.WriteLine("--list ignored for meta-kernel mode");
    state = svc.GetState(target, center, epoch);
  }
  else if (ext == ".bsp")
  {
    var kernel = RealSpkKernelParser.ParseLazy(file, memoryMap: true);

    if (list)
    {
      Console.WriteLine("SEGMENT STRUCTURE SUMMARY:");
      foreach (var seg in kernel.Segments)
      {
        Console.WriteLine($"Target={seg.Target.Value} Center={seg.Center.Value} Frame={seg.Frame.Value} Type={seg.DataType}");
        Console.WriteLine($"  ET Window: [{seg.StartTdbSec}, {seg.StopTdbSec}]  Records={seg.RecordCount} Degree={seg.Degree}");
        if (seg.RecordCount > 1)
          Console.WriteLine($"  RSIZE={seg.RecordSizeDoubles} INIT={seg.Init} INTLEN={seg.IntervalLength} Trailer(RSIZE={seg.TrailerRecordSize},N={seg.TrailerRecordCount})");
        Console.WriteLine($"  DAF Addresses: {seg.DataSourceInitialAddress}..{seg.DataSourceFinalAddress} (?={seg.DataSourceFinalAddress - seg.DataSourceInitialAddress + 1})");
      }
    }

    SpkSegment? best = null;
    foreach (var seg in kernel.Segments)
    {
      if (seg.Target != target || seg.Center != center) continue;
      if (epoch.TdbSecondsFromJ2000 < seg.StartTdbSec || epoch.TdbSecondsFromJ2000 > seg.StopTdbSec) continue;
      if (best is null || seg.StartTdbSec > best.StartTdbSec) best = seg;
    }
    if (best is null)
    {
      Console.WriteLine("No covering segment found for supplied epoch.");
      Console.WriteLine("Available segments (target/center/start/stop/type/records/degree):");
      foreach (var s in kernel.Segments)
        Console.WriteLine($" {s.Target.Value}/{s.Center.Value} [{s.StartTdbSec},{s.StopTdbSec}] type {s.DataType} recs {s.RecordCount} deg {s.Degree}");
      return;
    }
    state = SpkSegmentEvaluator.EvaluateState(best, epoch);
  }
  else
  {
    Console.WriteLine($"Unsupported file extension '{ext}'. Expected .bsp or .tm");
    return;
  }

  Console.WriteLine($"Epoch (TDB s past J2000): {epoch.TdbSecondsFromJ2000}");
  Console.WriteLine($"Target {target.Value} wrt {center.Value}");
  Console.WriteLine($"Position (km):  X={state.PositionKm.X:F9}  Y={state.PositionKm.Y:F9}  Z={state.PositionKm.Z:F9}");
  Console.WriteLine($"Velocity (km/s): VX={state.VelocityKmPerSec.X:F12}  VY={state.VelocityKmPerSec.Y:F12}  VZ={state.VelocityKmPerSec.Z:F12}");
}
catch (Exception ex)
{
  Console.WriteLine("Error: " + ex.Message);
  Console.WriteLine(ex);
}
