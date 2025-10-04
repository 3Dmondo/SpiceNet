using Shouldly;
using Spice.Core;
using Spice.Kernels;

namespace Spice.Tests;

public class LskParserTests
{
  static string SampleLsk = """
\begindata
DELTET/DELTA_AT = ( 32, @1999-JAN-01
                    33, @2006-JAN-01 )
"""; // minimal synthetic

  [Fact]
  public void Parses_LeapSeconds_And_Integrates_TimeConversion()
  {
    using var ms = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(SampleLsk));
    var lsk = LskParser.Parse(ms);
    lsk.Entries.Count.ShouldBe(2);
    lsk.Entries[0].TaiMinusUtcSeconds.ShouldBe(32);
    lsk.Entries[1].TaiMinusUtcSeconds.ShouldBe(33);

    TimeConversionService.SetLeapSeconds(lsk);

    var pre2006 = new DateTimeOffset(2005,12,31,23,59,59, TimeSpan.Zero);
    TimeConversionService.GetTaiMinusUtc(pre2006).ShouldBe(32);

    var post2006 = new DateTimeOffset(2006,1,1,0,0,0, TimeSpan.Zero);
    TimeConversionService.GetTaiMinusUtc(post2006).ShouldBe(33);

    // Interval across leap second (adds an extra atomic second)
    var startUtc = new DateTimeOffset(2005,12,31,23,59,30, TimeSpan.Zero);
    var endUtc = new DateTimeOffset(2006,1,1,0,0,30, TimeSpan.Zero);
    var tdbSeconds = TimeConversionService.UtcIntervalToTdbSeconds(startUtc, endUtc);
    tdbSeconds.ShouldBe(61d, 1e-9);
  }

  [Fact]
  public void MissingBlock_Throws()
  {
    const string bad = "\\begindata\nKPL/MISC = ( 'nothing' )";
    using var ms = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(bad));
    Should.Throw<InvalidDataException>(() => LskParser.Parse(ms));
  }

  [Fact]
  public void InvalidDate_Throws()
  {
    const string badDate = "\\begindata\nDELTET/DELTA_AT = ( 32, @1999-FOO-01 )";
    using var ms = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(badDate));
    Should.Throw<InvalidDataException>(() => LskParser.Parse(ms));
  }
}
