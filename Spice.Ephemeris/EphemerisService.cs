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
/// Thread-safety: instances are not thread-safe; create separate instances per parallel scenario.
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
    public double[] Stops = Array.Empty<double>();
    public void Build(IEnumerable<SpkSegment> segs)
    {
      Segments = segs.OrderBy(s=>s.StartTdbSec).ToArray();
      int len = Segments.Length;
      Starts = new double[len];
      Stops = new double[len];
      for (int i=0;i<len;i++){ var s=Segments[i]; Starts[i]=s.StartTdbSec; Stops[i]=s.StopTdbSec; }
    }
    public bool TryLocate(double et, out SpkSegment? seg)
    {
      // Binary search on starts
      int idx = Array.BinarySearch(Starts, et);
      if (idx >= 0)
      {
        // Exact start match: walk forward among same-start segments to find first covering.
        for (int j = idx; j < Segments.Length && Starts[j] == et; j++)
        { var cand = Segments[j]; if (et <= cand.StopTdbSec) { seg = cand; return true; } }
        // If none cover, fall through to predecessor search.
      }
      else idx = ~idx - 1; // predecessor index

      if (idx >= 0)
      {
        // Fast path boundary check: if et equals stop of predecessor segment.
        var pred = Segments[idx];
        if (et <= pred.StopTdbSec && et >= pred.StartTdbSec)
        { seg = pred; return true; }
      }

      // If predecessor failed, optionally check successor whose start is just after et in case of zero-width start==stop.
      int succ = idx + 1;
      if (succ >=0 && succ < Segments.Length)
      {
        var ssucc = Segments[succ];
        if (et >= ssucc.StartTdbSec && et <= ssucc.StopTdbSec) { seg = ssucc; return true; }
      }

      // Linear fallback (rare; ordering anomalies)
      for (int i=0;i<Segments.Length;i++)
      { var c=Segments[i]; if (et >= c.StartTdbSec && et <= c.StopTdbSec) { seg = c; return true; } }
      seg = null; return false;
    }
  }

  readonly Dictionary<Key, SegmentListIndex> _index = new();
  bool _indexDirty = true;

  // Cache for barycentric (relative to SSB=0) states at specific epochs; key tuple used sparingly per query path.
  readonly Dictionary<(int body,long etSeconds), StateVector> _baryCache = new();

  /// <summary>
  /// Gets the ordered list of kernel file paths successfully loaded (via meta-kernel or direct calls).
  /// Useful for diagnostics and reproducibility. Order matches load order.
  /// </summary>
  public IReadOnlyList<string> KernelPaths => _registry.KernelPaths;

  /// <summary>
  /// Load a meta-kernel (.tm style) which enumerates a set of kernel file paths. Supported kernel types:
  /// LSK (.tls) and binary SPK (.bsp). Each encountered kernel is parsed and its segments registered.
  /// Repeated calls append additional kernels (no duplicate prevention beyond path equality).
  /// </summary>
  /// <param name="metaKernelPath">Filesystem path to the meta-kernel text file.</param>
  /// <exception cref="ArgumentNullException">If <paramref name="metaKernelPath"/> is null.</exception>
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

  /// <summary>
  /// Load a real binary SPK kernel (.bsp) leveraging lazy coefficient access. Coefficients remain on the underlying
  /// data source (stream or memory-mapped file) and are retrieved on-demand per record evaluation.
  /// </summary>
  /// <param name="spkPath">Path to the SPK file.</param>
  /// <param name="memoryMap">True to prefer memory-mapped access (reduced per-read overhead) else stream-based.</param>
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

  /// <summary>
  /// Try to obtain the inertial state vector of <paramref name="target"/> relative to <paramref name="center"/>
  /// at ephemeris time <paramref name="t"/>. Returns true if either a direct covering segment exists or a barycentric
  /// composition path (target->SSB minus center->SSB) could be resolved.
  /// </summary>
  /// <param name="target">Target body id.</param>
  /// <param name="center">Center (observer) body id.</param>
  /// <param name="t">Ephemeris time (TDB seconds past J2000).</param>
  /// <param name="state">Resolved state vector (km, km/s) if successful, otherwise default.</param>
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

  /// <summary>Convenience: attempt barycentric state (body relative to SSB=0). Returns false if no path.</summary>
  internal bool TryGetBarycentric(BodyId body, Instant t, out StateVector state)
    => TryResolveBarycentric(body.Value, t, out state);

  /// <summary>
  /// Attempt to compute state(target, center) via barycentric composition: state(t,c)=state(t,0)-state(c,0).
  /// Requires both bodies resolvable to SSB (0). Returns false if either cannot be resolved (no traversal path).
  /// </summary>
  /// <param name="target">Target body id.</param>
  /// <param name="center">Center body id.</param>
  /// <param name="t">Ephemeris time (TDB seconds past J2000).</param>
  /// <param name="state">Resulting composed state if successful.</param>
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

  bool TryResolveBarycentric(int body, Instant t, out StateVector state) => TryResolveBarycentric(body, t, new HashSet<int>(), out state);

  bool TryResolveBarycentric(int body, Instant t, HashSet<int> visited, out StateVector state)
  {
    if (body == 0) { state = StateVector.Zero; return true; }
    if (!visited.Add(body)) { state = default; return false; } // cycle guard
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
    // Collect candidate segments (target->center) covering epoch without LINQ allocations.
    List<SpkSegment>? candidates = null;
    double etVal = t.TdbSecondsFromJ2000;
    for (int i = 0; i < _segments.Count; i++)
    {
      var s = _segments[i];
      if (s.Target.Value != body) continue;
      if (etVal < s.StartTdbSec || etVal > s.StopTdbSec) continue;
      (candidates ??= new()).Add(s);
    }
    if (candidates is not null && candidates.Count > 1)
      candidates.Sort(static (a,b) => a.Center.Value.CompareTo(b.Center.Value));
    if (candidates is not null)
    {
      for (int i = 0; i < candidates.Count; i++)
      {
        var cand = candidates[i];
        if (cand.Center.Value == body) continue;
        var partial = SpkSegmentEvaluator.EvaluateState(cand, t);
        if (TryResolveBarycentric(cand.Center.Value, t, visited, out var centerBary))
        {
          state = partial.Add(centerBary);
          _baryCache[key] = state; return true;
        }
      }
    }
    state = default; return false;
  }

  /// <summary>
  /// Get the state vector (throws if unavailable) of <paramref name="target"/> relative to <paramref name="center"/> at <paramref name="t"/>.
  /// Uses <see cref="TryGetState"/> internally and throws an <see cref="InvalidOperationException"/> when no suitable segment or
  /// composition path is found.
  /// </summary>
  /// <param name="target">Target body id.</param>
  /// <param name="center">Center body id.</param>
  /// <param name="t">Ephemeris time (TDB seconds past J2000).</param>
  /// <returns>Resolved state vector (km, km/s).</returns>
  /// <exception cref="InvalidOperationException">If no data covers the requested epoch & pair.</exception>
  public StateVector GetState(BodyId target, BodyId center, Instant t)
  {
    if (!TryGetState(target, center, t, out var state))
      throw new InvalidOperationException($"No SPK segment (direct or composable) covers epoch {t} for target {target.Value} center {center.Value}.");
    return state;
  }

  /// <summary>
  /// Dispose underlying ephemeris data sources (e.g., memory-mapped SPK files). After disposal further calls are invalid.
  /// </summary>
  public void Dispose()
  {
    foreach (var ds in _dataSources)
      ds.Dispose();
    _dataSources.Clear();
  }
}
