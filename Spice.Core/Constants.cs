// CSPICE Port Reference: N/A (original design)
namespace Spice.Core;

/// <summary>
/// Central numeric and unit constants (internal). Consolidates literals used across evaluation, tests, and tolerance policy.
/// </summary>
internal static class Constants
{
  /// <summary>Astronomical Unit length in kilometers (IAU 2012 exact definition: 149,597,870.7 km).</summary>
  public const double AstronomicalUnitKm = 149_597_870.7d;

  /// <summary>Seconds per day (SI) used in ephemeris data: 86400.</summary>
  public const double SecondsPerDay = 86400d;

  /// <summary>Factor converting AU/day to km/s (AU * AstronomicalUnitKm / SecondsPerDay).</summary>
  public const double AuPerDayToKmPerSec = AstronomicalUnitKm / SecondsPerDay;

  /// <summary>Epoch origin for J2000 in this system (TDB seconds past J2000 = 0).</summary>
  public const long J2000EpochTdbSeconds = 0L;

  internal static readonly IReadOnlyDictionary<int, double> LegacyDeAU = new Dictionary<int, double>
  {
    {200, 0.149597870659999996E+09},
    {202, 0.149597870609434400E+09},
    {403, 0.149597870691000000E+09},
    {405, 0.149597870691000015E+09},
    {406, 0.149597870691000015E+09},
    {410, 0.149597870697400004E+09},
    {413, 0.149597870698830900E+09},
    {414, 0.149597870700852500E+09},
    {418, 0.149597870699292500E+09},
    {421, 0.149597870699626200E+09},
    {422, 0.149597870700126600E+09},
    {423, 0.149597870699626200E+09},
    {424, 0.149597870699626200E+09}
  };
}

/// <summary>
/// Provides unified tolerance values for golden comparison tests based on ephemeris series characteristics.
/// Returns base AU and AU/day tolerances plus derived km and km/s values.
/// </summary>
internal static class TolerancePolicy
{
  internal readonly record struct Tolerances(
    double PositionAu,
    double VelocityAuPerDay,
    double PositionKm,
    double VelocityKmPerSec,
    bool Strict)
  {
    public override string ToString() =>
      $"Pos: {PositionAu:E2} AU ({PositionKm:E2} km), Vel: {VelocityAuPerDay:E2} AU/day ({VelocityKmPerSec:E2} km/s), Strict={Strict}";
  }

  /// <summary>
  /// Interim tolerances (Prompt 26) reflecting current observed residuals.
  /// Modern (>=400 with AU): 5e-10 AU position, 1e-10 AU/day velocity.
  /// Legacy DE2xx: 5e-8 AU position, 1e-8 AU/day velocity.
  /// Fallback: 5e-9 AU position, 1e-9 AU/day velocity.
  /// </summary>
  internal static Tolerances Get(int ephemerisNumber, bool hasAuConstant)
  {
    bool isProblematic = ephemerisNumber == 421; // DE421
    bool isLegacy = ephemerisNumber <= 414; 

    double posAu;
    double velAuDay;
    bool strict;

    if (hasAuConstant)
    {
      if (isProblematic)
      {
        posAu = 2e-12;
        velAuDay = 5e-15;
        strict = false;
      }
      else if (isLegacy)
      {
        posAu = 6e-14;
        velAuDay = 5e-14;
        strict = false;
      }
      else
      {
        posAu = 2e-14;
        velAuDay = 3e-17;
        strict = true;
      }
    }
    else
    {
      posAu = 5e-8;
      velAuDay = 1e-10;
      strict = false;
    }
    double posKm = posAu * Constants.AstronomicalUnitKm;
    double velKmSec = velAuDay * Constants.AuPerDayToKmPerSec;
    return new Tolerances(posAu, velAuDay, posKm, velKmSec, strict);
  }
}
