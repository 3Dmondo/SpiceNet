// CSPICE Port Reference: N/A (original managed design)
using Spice.Core;
using Spice.Kernels;
using Spice.IO;

namespace Spice.Ephemeris;

/// <summary>
/// High-level ephemeris service loading kernels (LSK, SPK) via a meta-kernel and providing state queries.
/// Segment selection precedence: among segments matching (target, center) that cover the epoch, choose the one with the
/// greatest <see cref="SpkSegment.StartTdbSec"/> (latest starting segment).
/// Implements a per (target,center) segment index for O(log n) lookup and a barycentric relative state resolver:
/// state(target, center) = state(target, SSB) - state(center, SSB) when a direct segment is absent (SSB id = 0).
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
      int idx = Array.BinarySearch(Starts, et);
      if (idx < 0) idx = ~idx - 1; // greatest start <= et
      for (int i = idx; i >=0; i--)
      {
        var candidate = Segments[i];
        if (et > candidate.StopTdbSec) continue;
        if (et >= candidate.StartTdbSec && et <= candidate.StopTdbSec)
        { seg = candidate; return true; }
      }
      seg = null; return false;
    }
  }

  readonly Dictionary<Key, SegmentListIndex> _index = new();
  bool _indexDirty = true;

  // Cache for barycentric (relative to SSB=0) states at specific epochs; key tuple used sparingly per query path.
  readonly Dictionary<(int body,long etSeconds), StateVector> _baryCache = new();

  public IReadOnlyList<string> KernelPaths => _registry.KernelPaths;

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
            _lsk = lsk;
            TimeConversionService.SetLeapSeconds(lsk);
          }
          break;
        case ".bsp":
          using (var s = File.OpenRead(path))
          {
            var spk = SpkKernelParser.Parse(s);
            _segments.AddRange(spk.Segments);
            _indexDirty = true;
          }
          break;
        default:
          break;
      }
    }
  }

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

  bool TryLocateSegment(int target, int center, long etSeconds, out SpkSegment? seg)
  {
    EnsureIndex();
    if (_index.TryGetValue(new Key(target, center), out var idx) && idx.TryLocate(etSeconds, out seg) && seg is not null)
      return true;
    seg = null; return false;
  }

  public bool TryGetState(BodyId target, BodyId center, Instant t, out StateVector state)
  {
    if (TryLocateSegment(target.Value, center.Value, t.TdbSecondsFromJ2000, out var seg) && seg is not null)
    {
      state = SpkSegmentEvaluator.EvaluateState(seg, t); return true;
    }
    // Fallback: compose via barycentric SSB path if possible.
    if (TryGetRelativeState(target, center, t, out state)) return true;
    state = default; return false;
  }

  /// <summary>
  /// Attempt to compute state(target, center) via barycentric composition: state(t,c)=state(t,0)-state(c,0).
  /// Requires both bodies resolvable to SSB (0). Returns false if either cannot be resolved.
  /// </summary>
  public bool TryGetRelativeState(BodyId target, BodyId center, Instant t, out StateVector state)
  {
    if (target.Value == center.Value) { state = StateVector.Zero; return true; }
    if (target.Value == 0 || center.Value == 0)
    {
      // One is SSB: direct attempt already done; compute direct barycentric if available.
      if (TryResolveBarycentric(target.Value, t, out var targB) && TryResolveBarycentric(center.Value, t, out var cenB))
      { state = targB.Subtract(cenB); return true; }
      state = default; return false;
    }
    if (TryResolveBarycentric(target.Value, t, out var tB) && TryResolveBarycentric(center.Value, t, out var cB))
    { state = tB.Subtract(cB); return true; }
    state = default; return false;
  }

  bool TryResolveBarycentric(int body, Instant t, out StateVector state)
  {
    if (body == 0) { state = StateVector.Zero; return true; }
    var key = (body, t.TdbSecondsFromJ2000);
    if (_baryCache.TryGetValue(key, out state)) return true;

    // Direct segment body->SSB?
    if (TryLocateSegment(body, 0, t.TdbSecondsFromJ2000, out var seg) && seg is not null)
    {
      state = SpkSegmentEvaluator.EvaluateState(seg, t);
      _baryCache[key] = state; return true;
    }

    // Otherwise locate any segment body->X and recursively resolve X->SSB.
    EnsureIndex();
    // Choose a covering segment with smallest |center| id precedence heuristically.
    var candidates = _segments.Where(s => s.Target.Value == body && t.TdbSecondsFromJ2000 >= s.StartTdbSec && t.TdbSecondsFromJ2000 <= s.StopTdbSec).OrderBy(s=>s.Center.Value).ToList();
    foreach (var cand in candidates)
    {
      if (cand.Center.Value == body) continue; // avoid self-loop
      var partial = SpkSegmentEvaluator.EvaluateState(cand, t); // state(body, center)
      if (TryResolveBarycentric(cand.Center.Value, t, out var centerBary))
      {
        // partial = body relative to center, so body barycentric = partial + center barycentric
        state = partial.Add(centerBary);
        _baryCache[key] = state; return true;
      }
    }
    state = default; return false;
  }

  public StateVector GetState(BodyId target, BodyId center, Instant t)
  {
    if (!TryGetState(target, center, t, out var state))
      throw new InvalidOperationException($"No SPK segment (direct or composable) covers epoch {t} for target {target.Value} center {center.Value}.");
    return state;
  }

  public void Dispose()
  {
    foreach (var ds in _dataSources)
      ds.Dispose();
    _dataSources.Clear();
  }
}
