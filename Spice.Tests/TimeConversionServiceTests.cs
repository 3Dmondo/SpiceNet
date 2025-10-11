using Shouldly;
using Spice.Core;

namespace Spice.Tests;

public class TimeConversionServiceTests
{
  static TimeConversionServiceTests()
  {
    // Install minimal leap second kernel (ordered) for tests.
    var lsk = LskKernel.FromEntries(new[]
    {
      new LeapSecondEntry(new DateTimeOffset(1999,1,1,0,0,0,TimeSpan.Zero), 32),
      new LeapSecondEntry(new DateTimeOffset(2006,1,1,0,0,0,TimeSpan.Zero), 33)
    });
    TimeConversionService.SetLeapSeconds(lsk);
  }

  [Fact]
  public void Extended_Model_Applies_Small_Periodic_Adjustment()
  {
    // Sample several TT epochs over +/- 5 years from J2000 (~15.7e7 seconds span)
    double[] samples =
    [
      -2_000_000d,
      0d,
      1_000_000d,
      10_000_000d,
      50_000_000d,
      100_000_000d,
      150_000_000d
    ];

    // Ensure starting in Basic model
    TimeConversionService.ConfigureTdbOffsetModel(TimeConversionService.TdbOffsetModel.Basic);
    var basicOffsets = samples.Select(t => Offset(t)).ToArray();

    TimeConversionService.ConfigureTdbOffsetModel(TimeConversionService.TdbOffsetModel.Extended);
    var extendedOffsets = samples.Select(t => Offset(t)).ToArray();

    int nonZeroDeltaCount = 0;
    for (int i = 0; i < samples.Length; i++)
    {
      double delta = extendedOffsets[i] - basicOffsets[i];
      // Extended adds higher harmonics; difference should be tiny (< 5 microseconds), zero-aligned at J2000.
      delta.ShouldBeLessThan(5e-6, $"Delta at sample index {i} exceeded 5 microseconds");
      if (Math.Abs(samples[i]) > 1 && Math.Abs(delta) > 5.0e-8) // >0.05 microseconds away from J2000
        nonZeroDeltaCount++;
    }
    nonZeroDeltaCount.ShouldBeGreaterThan(0); // model actually changes something

    // Switching back to Basic reinitializes alignment; J2000 offset equality still holds.
    TimeConversionService.ConfigureTdbOffsetModel(TimeConversionService.TdbOffsetModel.Basic);
    var basicAtZeroAfterSwitch = Offset(0);
    TimeConversionService.ConfigureTdbOffsetModel(TimeConversionService.TdbOffsetModel.Extended);
    var extendedAtZeroAfterSwitch = Offset(0);
    (extendedAtZeroAfterSwitch - basicAtZeroAfterSwitch).ShouldBe(0, 1e-12); // alignment preserved at J2000
  }

  static double Offset(double ttSecondsSinceJ2000)
  {
    // Use internal pathway: TtToTdbSecondsSinceJ2000 - identity part
    // Compute TDB minus TT by subtracting raw input
    var tdb = TimeConversionService.TtToTdbSecondsSinceJ2000(ttSecondsSinceJ2000);
    return tdb - ttSecondsSinceJ2000; // offset seconds
  }
}
