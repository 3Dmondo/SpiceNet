using Shouldly;
using Spice.Core;

namespace Spice.Tests;

public class TimeConversionTests
{
  static TimeConversionTests()
  {
    // Synthetic minimal leap second table (subset) for testing:
    // Effective UTC    TAI-UTC(s)
    // 1999-01-01       32
    // 2006-01-01       33 (real leap second inserted end of 2005)
    var lsk = LskKernel.FromEntries(new []
    {
      new LeapSecondEntry(new DateTimeOffset(1999,1,1,0,0,0, TimeSpan.Zero), 32d),
      new LeapSecondEntry(new DateTimeOffset(2006,1,1,0,0,0, TimeSpan.Zero), 33d)
    });
    TimeConversionService.SetLeapSeconds(lsk);
  }

  [Fact]
  public void J2000_Utc_Is_Zero_Tdb_Seconds()
  {
    var j2000Utc = new DateTimeOffset(2000,1,1,11,58,55, TimeSpan.Zero).AddMilliseconds(816);
    var tdbSec = TimeConversionService.UtcToTdbSecondsSinceJ2000(j2000Utc);
    tdbSec.ShouldBe(0d, 1e-9); // enforced alignment by implementation
  }

  [Fact]
  public void Tdb_Tt_Periodic_Delta_Bounded()
  {
    var baseUtc = new DateTimeOffset(2000,1,1,11,58,55, TimeSpan.Zero).AddMilliseconds(816);
    // Sample every 30 days for one year
    for (int day = 0; day <= 360; day += 30)
    {
      var utc = baseUtc.AddDays(day);
      var tt = TimeConversionService.UtcToTtSecondsSinceJ2000(utc);
      var tdb = TimeConversionService.UtcToTdbSecondsSinceJ2000(utc);
      var delta = tdb - tt; // periodic offset relative to J2000 alignment (can swing roughly +/- ~1.7ms)
      delta.ShouldBeLessThan(0.0035); // conservative bound (< 3.5 ms)
      delta.ShouldBeGreaterThan(-0.0035);
    }
  }

  [Fact]
  public void Chain_Consistency_Utc_Tdb_Relative_Delta_Small()
  {
    var j2000Utc = new DateTimeOffset(2000,1,1,11,58,55, TimeSpan.Zero).AddMilliseconds(816);
    var laterUtc = j2000Utc.AddHours(1); // +3600s wall clock
    var taiDelta = TimeConversionService.UtcToTaiSecondsSinceJ2000(laterUtc);
    var tdbDelta = TimeConversionService.UtcToTdbSecondsSinceJ2000(laterUtc);
    var diff = tdbDelta - taiDelta; // should be small periodic correction (~ms)
    diff.ShouldNotBe(0d); // with periodic terms included we expect a non-zero offset
    diff.ShouldBeGreaterThan(-0.004);
    diff.ShouldBeLessThan(0.004);
    taiDelta.ShouldBe(3600d, 1e-6); // leap seconds constant in this interval
  }

  [Fact]
  public void Interval_Crossing_Leap_Second_Gains_Extra_Second()
  {
    // Interval spanning leap second at boundary 2005-12-31 -> 2006-01-01.
    var startUtc = new DateTimeOffset(2005,12,31,23,59,30, TimeSpan.Zero);
    var endUtc = new DateTimeOffset(2006,1,1,0,0,30, TimeSpan.Zero);

    var wallSeconds = (endUtc - startUtc).TotalSeconds; // 60 seconds wall clock
    wallSeconds.ShouldBe(60d);

    var tdbSeconds = TimeConversionService.UtcIntervalToTdbSeconds(startUtc, endUtc);
    // One leap second inserted => 61s in atomic timescales (periodic TDB terms largely cancel over short interval)
    tdbSeconds.ShouldBe(61d, 1e-6);
  }

  [Fact]
  public void GetTaiMinusUtc_Throws_When_Before_First_Entry()
  {
    var early = new DateTimeOffset(1990,1,1,0,0,0, TimeSpan.Zero);
    Should.Throw<ArgumentOutOfRangeException>(() => TimeConversionService.GetTaiMinusUtc(early));
  }
}
