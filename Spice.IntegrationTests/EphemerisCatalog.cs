// Original design: Ephemeris catalog describing DE planetary ephemeris kernels and policies.
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

  // Table derived from user provided size list (approx bytes using MB*1_048_576).
  static readonly EphemerisEntry[] _all = new[]
  {
    Entry("102", 228.1, under:false), // above limit
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
    // Large (> limit) examples intentionally omitted from default set (e.g. 422, 433, 441, 431t) unless explicitly enabled.
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

  public static IReadOnlyList<EphemerisEntry> All => _all;

  public static IEnumerable<EphemerisEntry> ResolveSelection()
  {
    var allowLarge = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SPICE_ALLOW_LARGE_KERNELS"));
    var listEnv = Environment.GetEnvironmentVariable("SPICE_EPH_LIST");
    if (!string.IsNullOrWhiteSpace(listEnv))
    {
      var wanted = new HashSet<string>(listEnv.Split(',', StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
      foreach (var e in _all)
      {
        if (wanted.Contains(e.Number) && (allowLarge || e.UnderSizeLimit)) yield return e;
      }
      yield break;
    }
    foreach (var e in _all)
    {
      if (e.UnderSizeLimit || allowLarge) yield return e;
    }
  }
}
