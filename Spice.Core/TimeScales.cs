namespace Spice.Core;

/// <summary>
/// Supported time scales for conversions. Limited subset for Phase 1.
/// </summary>
public enum TimeScale
{
  UTC,
  TAI,
  TT,
  TDB
}

/// <summary>
/// Leap second kernel model (LSK) capturing TAI-UTC offset steps. Entries must be ordered by ascending effective UTC.
/// Each entry's offset applies from its EffectiveUtc (inclusive) until the next entry.
/// </summary>
/// <param name="Entries">Ordered collection of leap second entries.</param>
public sealed record LskKernel(IReadOnlyList<LeapSecondEntry> Entries)
{
  public static LskKernel FromEntries(IEnumerable<LeapSecondEntry> entries)
  {
    var ordered = entries.OrderBy(e => e.EffectiveUtc).ToArray();
    if (ordered.Length == 0) throw new ArgumentException("No leap second entries supplied", nameof(entries));
    return new LskKernel(ordered);
  }
}

/// <summary>
/// Single leap second table entry.
/// </summary>
/// <param name="EffectiveUtc">UTC instant (at 00:00:00 of day the new offset becomes active).</param>
/// <param name="TaiMinusUtcSeconds">Cumulative TAI-UTC in seconds after the leap second insertion at EffectiveUtc.</param>
public readonly record struct LeapSecondEntry(DateTimeOffset EffectiveUtc, double TaiMinusUtcSeconds);

/// <summary>
/// Time conversion utilities. Placeholder implementation: TT == TDB. Instant uses whole-second precision.
/// NOTE: We compute relative seconds since J2000 so constant TT-TAI offset (32.184s) cancels and is omitted.
/// </summary>
public static partial class TimeConversionService
{
  // J2000 epoch: 2000-01-01 12:00:00 TT == 2000-01-01 11:59:27.816 TAI == 2000-01-01 11:58:55.816 UTC.
  static readonly DateTimeOffset J2000Utc = new DateTimeOffset(2000,1,1,11,58,55,0, TimeSpan.Zero).AddMilliseconds(816);

  static LskKernel? _lsk;
  static double _taiMinusUtcAtJ2000; // cached from installed kernel

  /// <summary>Install leap second kernel for subsequent conversions.</summary>
  public static void SetLeapSeconds(LskKernel kernel)
  {
    _lsk = kernel ?? throw new ArgumentNullException(nameof(kernel));
    _taiMinusUtcAtJ2000 = GetTaiMinusUtc(J2000Utc);
  }

  /// <summary>Return current installed LSK or throw.</summary>
  static LskKernel RequireLsk() => _lsk ?? throw new InvalidOperationException("Leap second kernel not set. Call SetLeapSeconds().");

  /// <summary>Find TAI-UTC at given UTC using installed leap second table.</summary>
  public static double GetTaiMinusUtc(DateTimeOffset utc)
  {
    var entries = RequireLsk().Entries;
    // Binary search last entry whose EffectiveUtc <= utc
    int lo = 0, hi = entries.Count - 1, idx = -1;
    while (lo <= hi)
    {
      int mid = (lo + hi) / 2;
      if (entries[mid].EffectiveUtc <= utc)
      {
        idx = mid; lo = mid + 1;
      }
      else hi = mid - 1;
    }
    if (idx < 0) throw new ArgumentOutOfRangeException(nameof(utc), "UTC earlier than first leap second entry");
    return entries[idx].TaiMinusUtcSeconds;
  }

  /// <summary>Convert UTC to TAI seconds past J2000 epoch.</summary>
  public static double UtcToTaiSecondsSinceJ2000(DateTimeOffset utc)
  {
    var taiMinusUtc = GetTaiMinusUtc(utc);
    var utcSpan = utc - J2000Utc;
    double deltaLeap = taiMinusUtc - _taiMinusUtcAtJ2000;
    return utcSpan.TotalSeconds + deltaLeap;
  }

  /// <summary>Convert UTC to TT seconds since J2000 (relative), constant 32.184s cancels in relative measure so identical to TAI value.</summary>
  public static double UtcToTtSecondsSinceJ2000(DateTimeOffset utc) => UtcToTaiSecondsSinceJ2000(utc);

  /// <summary>Placeholder identity conversion TT -> TDB (future refinement will add periodic terms).</summary>
  public static double TtToTdbSecondsSinceJ2000(double ttSecondsSinceJ2000) => ttSecondsSinceJ2000;

  /// <summary>Convert UTC to TDB seconds since J2000 (UTC -> TT -> TDB chain simplified).</summary>
  public static double UtcToTdbSecondsSinceJ2000(DateTimeOffset utc)
  {
    var tt = UtcToTtSecondsSinceJ2000(utc);
    return TtToTdbSecondsSinceJ2000(tt);
  }

  /// <summary>Convert UTC to Instant (whole-second rounding of TDB seconds past J2000).</summary>
  public static Instant UtcToInstant(DateTimeOffset utc)
  {
    var tdbSec = UtcToTdbSecondsSinceJ2000(utc);
    return new Instant(checked((long)Math.Round(tdbSec))); // bankers rounding
  }

  /// <summary>Compute TDB duration in seconds between two UTC instants (end - start).</summary>
  public static double UtcIntervalToTdbSeconds(DateTimeOffset startUtc, DateTimeOffset endUtc) =>
    UtcToTdbSecondsSinceJ2000(endUtc) - UtcToTdbSecondsSinceJ2000(startUtc);
}
