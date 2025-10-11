// Diagnostic console demo using only public SpiceNet facade APIs.
// Usage:
//   dotnet run --project Spice.Console.Demo -- <kernel-or-meta> <targetId> <centerId> <etSeconds> [--list] [--comments]
// Notes:
// - For a meta-kernel (.tm) include LSK + SPK paths inside.
// - For a single SPK (.bsp) the tool loads it via EphemerisService.LoadRealSpkLazy.
// - Segment listing and raw comment extraction rely on internal types and are intentionally unavailable in the public demo facade.
//   Requests for --list / --comments print advisory messages instead.

using Spice.Core;
using Spice.Ephemeris;
using System.Globalization;

if (args.Length < 4)
{
  Console.WriteLine("SpiceNet Demo\n" +
                    "Args: <kernel-or-meta> <target> <center> <etSeconds> [--list] [--comments]\n" +
                    "Example: dotnet run --project Spice.Console.Demo -- de440.bsp 499 0 0\n" +
                    "Example: dotnet run --project Spice.Console.Demo -- planets.tm 399 0 1000000");
  return;
}

bool list = args.Contains("--list", StringComparer.OrdinalIgnoreCase);
bool comments = args.Contains("--comments", StringComparer.OrdinalIgnoreCase) || args.Contains("--show-comments", StringComparer.OrdinalIgnoreCase);
string path = args[0];
if (!File.Exists(path)) { Console.WriteLine($"File not found: {path}"); return; }
if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetId) ||
    !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var centerId) ||
    !double.TryParse(args[3], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var et))
{ Console.WriteLine("Invalid numeric arguments"); return; }

if (list)
  Console.WriteLine("--list requested: segment enumeration is not part of the current public API.");
if (comments)
  Console.WriteLine("--comments requested: raw DAF comment extraction is internal; expose via future diagnostic API if needed.");

var target = new BodyId(targetId);
var center = new BodyId(centerId);
var instant = new Instant((long)Math.Round(et));

try
{
  using var svc = new EphemerisService();
  var ext = Path.GetExtension(path).ToLowerInvariant();
  if (ext == ".tm") svc.Load(path);
  else if (ext == ".bsp") svc.Load(path);
  else { Console.WriteLine($"Unsupported extension '{ext}'. Expected .tm or .bsp"); return; }

  if (!svc.TryGetState(target, center, instant, out var state))
  {
    Console.WriteLine("No state available (no covering segment or barycentric composition path).");
    return;
  }

  Console.WriteLine($"Epoch (TDB s past J2000): {instant.TdbSecondsFromJ2000}");
  Console.WriteLine($"Target {target.Value} wrt {center.Value}");
  Console.WriteLine($"Position (km):  X={state.PositionKm.X:F9}  Y={state.PositionKm.Y:F9}  Z={state.PositionKm.Z:F9}");
  Console.WriteLine($"Velocity (km/s): VX={state.VelocityKmPerSec.X:F12}  VY={state.VelocityKmPerSec.Y:F12}  VZ={state.VelocityKmPerSec.Z:F12}");
}
catch (Exception ex)
{
  Console.WriteLine("Error: " + ex.Message);
}
