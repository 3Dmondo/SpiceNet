// CSPICE Port Reference: N/A (original managed design)
using Spice.Core;
using Spice.Kernels;

namespace Spice.Ephemeris;

/// <summary>
/// High-level ephemeris service loading kernels (LSK, SPK) via a meta-kernel and providing state queries.
/// Segment selection precedence: among segments matching (target, center) that cover the epoch, choose the one with the
/// greatest <see cref="SpkSegment.StartTdbSec"/> (latest starting segment) as per prompt specification.
/// </summary>
public sealed class EphemerisService
{
  readonly KernelRegistry _registry = new();
  readonly List<SpkSegment> _segments = new();
  LskKernel? _lsk;

  public IReadOnlyList<string> KernelPaths => _registry.KernelPaths;

  /// <summary>Load a meta-kernel file (.tm). Existing state (segments, LSK) is retained; new kernels appended with precedence applied by start time at query.</summary>
  public void Load(string metaKernelPath)
  {
    if (metaKernelPath is null) throw new ArgumentNullException(nameof(metaKernelPath));
    using var mkStream = File.OpenRead(metaKernelPath);
    var meta = MetaKernelParser.Parse(mkStream, metaKernelPath);
    _registry.AddMetaKernel(meta);

    foreach (var path in meta.KernelPaths)
    {
      var ext = Path.GetExtension(path).ToLowerInvariant();
      switch (ext)
      {
        case ".tls":
          using (var s = File.OpenRead(path))
          {
            var lsk = LskParser.Parse(s);
            _lsk = lsk; // last wins
            TimeConversionService.SetLeapSeconds(lsk);
          }
          break;
        case ".bsp":
          using (var s = File.OpenRead(path))
          {
            var spk = SpkKernelParser.Parse(s);
            _segments.AddRange(spk.Segments);
          }
          break;
        default:
          // Ignore unsupported for now
          break;
      }
    }
  }

  /// <summary>Attempt to get state; returns false if no covering segment found.</summary>
  public bool TryGetState(BodyId target, BodyId center, Instant t, out StateVector state)
  {
    SpkSegment? best = null;
    foreach (var seg in _segments)
    {
      if (seg.Target != target || seg.Center != center) continue;
      if (t.TdbSecondsFromJ2000 < seg.StartTdbSec || t.TdbSecondsFromJ2000 > seg.StopTdbSec) continue;
      if (best is null || seg.StartTdbSec > best.StartTdbSec)
        best = seg;
    }
    if (best is null)
    {
      state = default;
      return false;
    }
    state = SpkSegmentEvaluator.EvaluateState(best, t);
    return true;
  }

  /// <summary>Get state or throw if no covering segment.</summary>
  public StateVector GetState(BodyId target, BodyId center, Instant t)
  {
    if (!TryGetState(target, center, t, out var state))
      throw new InvalidOperationException($"No SPK segment covers epoch {t} for target {target.Value} center {center.Value}.");
    return state;
  }
}
