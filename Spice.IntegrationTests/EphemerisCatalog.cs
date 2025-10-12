// Ephemeris catalog: now supports dynamic loading from generated Docs/SsdCatalog catalogs (ssd_catalog.json + testpo_catalog.json)
// Falls back to hardcoded list if catalogs are absent.
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spice.IntegrationTests;

internal sealed record EphemerisEntry(
  string Number,
  string TestPoFileName,
  string PreferredBspFileName,
  long SizeBytes,
  bool UnderSizeLimit,
  string? AlternateSmallVariant = null
)
{
  public string DirectoryName => $"de{Number}";
  public string TestPoUrl => $"https://ssd.jpl.nasa.gov/ftp/eph/planets/ascii/de{Number}/testpo.{Number}";
  public string BspUrl => $"https://ssd.jpl.nasa.gov/ftp/eph/planets/bsp/{PreferredBspFileName}";
}

internal static class EphemerisCatalog
{
  // Size limit ~150 MB (binary kernels larger than this skipped unless enabled)
  const long SizeLimitBytes = 150L * 1024 * 1024;

  // LEGACY STATIC FALLBACK (kept for determinism if dynamic catalogs not present)
  static readonly EphemerisEntry[] _fallback = new[]
  {
    Entry("102", 228.1, under:false),
    Entry("200", 54.2),
    Entry("202", 14.3),
    Entry("403", 62.3),
    Entry("405", 62.4),
    Entry("410", 12.5),
    Entry("413", 15.6),
    Entry("414", 62.4),
    Entry("418", 15.7),
    Entry("421", 16.0),
    Entry("423", 41.5),
    Entry("424", 62.3),
    entrySmallVariant("430","de430_1850-2150.bsp",31.2),
    entrySmallVariant("430t","de430t.bsp",127.7),
    entrySmallVariant("432t","de432t.bsp",127.7),
    Entry("434",114.2),
    Entry("435",114.2),
    Entry("436",114.2),
    Entry("438",114.2),
    Entry("440",114.3),
    Entry("440t",145.7, under:false),
  };

  static EphemerisEntry Entry(string num, double sizeMB, bool under = true)
  {
    long bytes = (long)(sizeMB * 1024 * 1024);
    bool underLimit = under && bytes <= SizeLimitBytes;
    return new EphemerisEntry(num, $"testpo.{num}", $"de{num}.bsp", bytes, underLimit);
  }

  static EphemerisEntry entrySmallVariant(string num, string bspName, double sizeMB, bool under=true)
  {
    long bytes = (long)(sizeMB * 1024 * 1024);
    bool underLimit = under && bytes <= SizeLimitBytes;
    return new EphemerisEntry(num, $"testpo.{num}", bspName, bytes, underLimit);
  }

  // -------------- Dynamic Loading --------------
  static EphemerisEntry[]? _dynamic;
  static bool _attemptedLoad;
  static readonly object _lock = new();
  static readonly Regex BspNumberRegex = new("^de(\\d+).*(?:\\.bsp)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

  static void EnsureLoaded()
  {
    if (_attemptedLoad) return;
    lock (_lock)
    {
      if (_attemptedLoad) return;
      try
      {
        var baseDir = AppContext.BaseDirectory; // test bin folder
        string catDir = Path.Combine(baseDir, "Catalog");
        string ssdPath = Path.Combine(catDir, "ssd_catalog.json");
        // testpo catalog optional
        string testpoPath = Path.Combine(catDir, "testpo_catalog.json");
        if (!File.Exists(ssdPath))
        {
          _dynamic = null; // fallback
        }
        else
        {
          using var ssdStream = File.OpenRead(ssdPath);
          using var doc = JsonDocument.Parse(ssdStream);
          var root = doc.RootElement;
          if (!root.TryGetProperty("Files", out var filesEl) || filesEl.ValueKind != JsonValueKind.Array)
          {
            _dynamic = null;
          }
          else
          {
            var bspGroups = new Dictionary<string, List<(string name,long size)>>();
            foreach (var f in filesEl.EnumerateArray())
            {
              var name = f.GetProperty("Name").GetString() ?? string.Empty;
              if (!name.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase)) continue;
              if (!BspNumberRegex.IsMatch(name)) continue;
              var m = BspNumberRegex.Match(name);
              var num = m.Groups[1].Value; // digits only
              long size = f.TryGetProperty("SizeBytes", out var sb) && sb.ValueKind == JsonValueKind.Number ? sb.GetInt64() : -1;
              if (size <= 0) continue; // skip unknown size
              // Only planetary ephemerides: reside under planets/bsp/ (RelativePath check)
              if (f.TryGetProperty("RelativePath", out var rp) && rp.GetString() is string rel)
              {
                if (!rel.StartsWith("planets/bsp/", StringComparison.OrdinalIgnoreCase)) continue;
              }
              if (!bspGroups.TryGetValue(num, out var list))
              {
                list = new(); bspGroups[num] = list;
              }
              list.Add((name,size));
            }

            var dynamicList = new List<EphemerisEntry>();
            foreach (var kv in bspGroups.OrderBy(k => int.Parse(k.Key)))
            {
              var variants = kv.Value.OrderBy(v => v.size).ToList();
              var primary = variants.First();
              string? alternate = variants.Skip(1).FirstOrDefault(v => v.size <= SizeLimitBytes).name; // first other small variant
              bool under = primary.size <= SizeLimitBytes;
              dynamicList.Add(new EphemerisEntry(kv.Key, $"testpo.{kv.Key}", primary.name, primary.size, under, alternate));
            }
            _dynamic = dynamicList.ToArray();
          }
        }
      }
      catch
      {
        _dynamic = null; // swallow & fallback
      }
      finally { _attemptedLoad = true; }
    }
  }

  public static IReadOnlyList<EphemerisEntry> All
  {
    get { EnsureLoaded(); return (IReadOnlyList<EphemerisEntry>?)_dynamic ?? _fallback; }
  }

  public static IEnumerable<EphemerisEntry> ResolveSelection()
  {
    EnsureLoaded();
    var source = (IEnumerable<EphemerisEntry>) (_dynamic ?? _fallback);
    var allowLarge = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SPICE_ALLOW_LARGE_KERNELS"));
    var listEnv = Environment.GetEnvironmentVariable("SPICE_EPH_LIST");
    if (!string.IsNullOrWhiteSpace(listEnv))
    {
      var wanted = new HashSet<string>(listEnv.Split(',', StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
      foreach (var e in source)
        if (wanted.Contains(e.Number) && (allowLarge || e.UnderSizeLimit))
          yield return e;
      yield break;
    }
    foreach (var e in source)
      if (e.UnderSizeLimit || allowLarge)
        yield return e;
  }
}
