using Shouldly;
using Spice.Kernels;
using System.Text;

namespace Spice.Tests;

public class TextKernelParserTests
{
  static readonly string SampleKernel = """
\begintext
Synthetic metadata kernel for tests.

\begindata
BODY399_RADII = ( 6378.137 6378.137 6356.752 )
BODY399_POLE_RA = ( 0.0 -0.641D0 0.0 )
BODY399_POLE_DEC = ( 90.0 -0.557D0 0.0 )
BODY399_PM =
(
  190.147 360.9856235 0.0
)
BODY399_LONG_NAME = 'Earth'
\begintext
BODY499_RADII = ( 3396.19 3396.19 3376.20 )
""";

  [Fact]
  public void Parses_Assignments_Inside_Begindata_Block() {
    using var stream = new MemoryStream(Encoding.ASCII.GetBytes(SampleKernel));
    var document = TextKernelParser.Parse(stream);

    document.Assignments.Count.ShouldBe(5);
    document.AssignmentMap.ContainsKey("BODY499_RADII").ShouldBeFalse();

    var radii = document.AssignmentMap["BODY399_RADII"];
    radii.NumericValues.ShouldBe([6378.137, 6378.137, 6356.752], 1e-9);

    var poleRa = document.AssignmentMap["BODY399_POLE_RA"];
    poleRa.NumericValues.ShouldBe([0.0, -0.641, 0.0], 1e-12);

    var primeMeridian = document.AssignmentMap["BODY399_PM"];
    primeMeridian.NumericValues.ShouldBe([190.147, 360.9856235, 0.0], 1e-12);

    var longName = document.AssignmentMap["BODY399_LONG_NAME"];
    longName.RawTokens.ShouldBe(["Earth"]);
    longName.NumericValues.ShouldBeEmpty();
  }

  [Fact]
  public void Unterminated_Assignment_Throws() {
    const string badKernel = """
\begindata
BODY399_RADII = ( 6378.137 6378.137
""";

    using var stream = new MemoryStream(Encoding.ASCII.GetBytes(badKernel));
    Should.Throw<InvalidDataException>(() => TextKernelParser.Parse(stream));
  }
}
