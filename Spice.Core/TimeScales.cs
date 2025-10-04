// CSPICE Port Reference: N/A (original managed design)
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
/// Time conversion utilities. Adds analytic approximation for TT->TDB using standard low-order periodic terms.
/// Instant uses whole-second precision for now.
/// NOTE: We compute relative seconds since J2000 so constant TT-TAI offset (32.184s) cancels and is omitted.
/// </summary>
public static partial class TimeConversionService
{
  // J2000 epoch: 2000-01-01 12:00:00 TT == 2000-01-01 11:59:27.816 TAI == 2000-01-01 11:58:55.816 UTC.
  static readonly DateTimeOffset J2000Utc = new DateTimeOffset(2000,1,1,11,58,55,0, TimeSpan.Zero).AddMilliseconds(816);

  static LskKernel? _lsk;
  static double _taiMinusUtcAtJ2000; // cached from installed kernel

  // Constants for TT->TDB approximation (see NAIF Frames / TDB required reading simplified formula).
  // TDB - TT ? 0.001657 sin(g) + 0.00001385 sin(2g)  (seconds)
  // g = 357.53° + 0.9856003° * (JD_TT - 2451545.0)
  const double Deg2Rad = Math.PI / 180.0;
  const double G0 = 357.53;        // degrees at J2000
  const double G_RATE = 0.9856003;  // degrees per day
  const double TERM1 = 0.001657;    // seconds
  const double TERM2 = 0.00001385;  // seconds
  const double JD_J2000 = 2451545.0; // Julian Day at J2000 TT
  const double SecondsPerDay = 86400.0;

  static bool _tdbOffsetInitialized;
  static double _deltaTdbAtJ2000; // periodic term value at J2000 (removed to keep epoch alignment)

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

  static double ComputePeriodicDelta(double ttSecondsSinceJ2000)
  {
    double jdTt = JD_J2000 + ttSecondsSinceJ2000 / SecondsPerDay;
    double daysFromJ2000 = jdTt - JD_J2000;
    double gDeg = G0 + G_RATE * daysFromJ2000; // mean anomaly of the Sun
    double g = gDeg * Deg2Rad;
    return TERM1 * Math.Sin(g) + TERM2 * Math.Sin(2 * g); // seconds
  }

  /// <summary>
  /// Approximate TT->TDB conversion adding periodic relativistic terms. Accuracy ~<2 ms, suitable for many ephemeris uses.
  /// Ensures TDB==TT at J2000 by subtracting the J2000 periodic value.
  /// </summary>
  public static double TtToTdbSecondsSinceJ2000(double ttSecondsSinceJ2000)
  {
    if (!_tdbOffsetInitialized)
    {
      _deltaTdbAtJ2000 = ComputePeriodicDelta(0);
      _tdbOffsetInitialized = true;
    }
    double delta = ComputePeriodicDelta(ttSecondsSinceJ2000) - _deltaTdbAtJ2000;
    return ttSecondsSinceJ2000 + delta; // relative epoch shift with J2000 alignment
  }

  /// <summary>Convert UTC to TDB seconds since J2000 (UTC -> TT -> TDB chain).</summary>
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
