// CSPICE Port Reference: N/A (original managed design)
using Spice.Core;
using Spice.Kernels;
using Spice.IO;

namespace Spice.Ephemeris;

/// <summary>
/// High-level ephemeris service loading kernels (LSK, SPK) via a meta-kernel and providing state queries.
/// Segment selection precedence: among segments matching (target, center) that cover the epoch, choose the one with the
/// greatest <see cref="SpkSegment.StartTdbSec"/> (latest starting segment) as per prompt specification.
/// Supports both synthetic SPK (eager) and real SPK lazy loading (Prompt 15). Adds per-target/center segment index
/// with binary-search fast path (Prompt 18).
/// </summary>
public sealed class EphemerisService : IDisposable
{
  readonly KernelRegistry _registry = new();
  readonly List<SpkSegment> _segments = new();
  readonly List<IEphemerisDataSource> _dataSources = new();
  LskKernel? _lsk;

  struct Key : IEquatable<Key>
  {
    public readonly int Target; public readonly int Center;
    public Key(int t, int c){Target=t;Center=c;}
    public bool Equals(Key other)=>Target==other.Target && Center==other.Center;
    public override bool Equals(object? obj)=>obj is Key k && Equals(k);
    public override int GetHashCode()=>HashCode.Combine(Target,Center);
  }

  sealed class SegmentListIndex
  {
    public SpkSegment[] Segments = Array.Empty<SpkSegment>(); // sorted by StartTdbSec ascending
    public double[] Starts = Array.Empty<double>();
    public void Build(IEnumerable<SpkSegment> segs)
    {
      Segments = segs.OrderBy(s=>s.StartTdbSec).ToArray();
      Starts = Segments.Select(s=>s.StartTdbSec).ToArray();
    }
    public bool TryLocate(double et, out SpkSegment? seg)
    {
      // Binary search latest start <= et, then walk backwards until coverage found (should be at or before index)
      int idx = Array.BinarySearch(Starts, et);
      if (idx < 0) idx = ~idx - 1; // index of greatest start < et
      for (int i = idx; i >=0; i--)
      {
        var candidate = Segments[i];
        if (et > candidate.StopTdbSec) continue; // started earlier but ended before epoch
        if (et >= candidate.StartTdbSec && et <= candidate.StopTdbSec)
        {
          // This is by construction the latest start covering et because we traverse backwards from latest start <= et.
          seg = candidate; return true;
        }
      }
      seg = null; return false;
    }
  }

  readonly Dictionary<Key, SegmentListIndex> _index = new();
  bool _indexDirty = true;

  public IReadOnlyList<string> KernelPaths => _registry.KernelPaths;

  /// <summary>Load a meta-kernel (.tm). Synthetic SPK files are parsed eagerly; call <see cref="LoadRealSpkLazy"/> for lazy real SPK loading.</summary>
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
            // Assume synthetic for legacy tests. Real kernels should use LoadRealSpkLazy.
            var spk = SpkKernelParser.Parse(s);
            _segments.AddRange(spk.Segments);
            _indexDirty = true;
          }
          break;
        default:
          break; // ignore unsupported
      }
    }
  }

  /// <summary>
  /// Load a real SPK file lazily (memory-mapped by default) adding its segments. Coefficients are fetched on-demand.
  /// </summary>
  public void LoadRealSpkLazy(string spkPath, bool memoryMap = true)
  {
    if (spkPath is null) throw new ArgumentNullException(nameof(spkPath));
    var kernel = RealSpkKernelParser.ParseLazy(spkPath, memoryMap);
    foreach (var seg in kernel.Segments)
      if (seg.DataSource is not null && !_dataSources.Contains(seg.DataSource))
        _dataSources.Add(seg.DataSource);
    _segments.AddRange(kernel.Segments);
    _indexDirty = true;
  }

  void EnsureIndex()
  {
    if (!_indexDirty) return;
    _index.Clear();
    foreach (var g in _segments.GroupBy(s=>new Key(s.Target.Value, s.Center.Value)))
    {
      var idx = new SegmentListIndex();
      idx.Build(g);
      _index[g.Key] = idx;
    }
    _indexDirty = false;
  }

  /// <summary>Attempt to get state; returns false if no covering segment found.</summary>
  public bool TryGetState(BodyId target, BodyId center, Instant t, out StateVector state)
  {
    EnsureIndex();
    if (_index.TryGetValue(new Key(target.Value, center.Value), out var idx) && idx.TryLocate(t.TdbSecondsFromJ2000, out var seg) && seg is not null)
    {
      state = SpkSegmentEvaluator.EvaluateState(seg, t); return true;
    }
    state = default; return false;
  }

  /// <summary>Get state or throw if no covering segment.</summary>
  public StateVector GetState(BodyId target, BodyId center, Instant t)
  {
    if (!TryGetState(target, center, t, out var state))
      throw new InvalidOperationException($"No SPK segment covers epoch {t} for target {target.Value} center {center.Value}.");
    return state;
  }

  public void Dispose()
  {
    foreach (var ds in _dataSources)
      ds.Dispose();
    _dataSources.Clear();
  }
}
