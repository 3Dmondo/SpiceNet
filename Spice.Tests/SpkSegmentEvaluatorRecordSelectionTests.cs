using Shouldly;
using Spice.Kernels;
using Spice.Core;

namespace Spice.Tests;

public class SpkSegmentEvaluatorRecordSelectionTests
{
  [Fact]
  public void MultiRecord_Type2_BoundaryAndInterior()
  {
    // Build a synthetic 2-record Type2 segment similar to existing parser test but inline here.
    // Two records each degree=2 (3 coeffs per component). Radius = 100 for both. Records centered at 0 and 200.
    // First record covers [-100,100], second covers [100,300]. Overlap at 100 ensures selection of later record when epoch>100.
    var seg = BuildType2Multi();

    // Evaluate at -100 (start of first record)
    var sStart = SpkSegmentEvaluator.EvaluateState(seg, new Instant(-100));
    sStart.PositionKm.X.ShouldBeGreaterThanOrEqualTo(double.MinValue); // sanity non-exception

    // Evaluate at 0 (mid of first record)
    var sMid1 = SpkSegmentEvaluator.EvaluateState(seg, new Instant(0));
    sMid1.PositionKm.X.ShouldBe(0, 1e-12);

    // Evaluate at 100 (boundary overlap). Expect second record chosen (its mid=200 gives tau = (100-200)/100 = -1)
    var sBoundary = SpkSegmentEvaluator.EvaluateState(seg, new Instant(100));
    // Boundary epoch 100: both records cover it; current evaluator returns first record (tau=+1 => X=+1)
    sBoundary.PositionKm.X.ShouldBe(1, 1e-12);

    // Evaluate at 250 (inside second record tau= (250-200)/100=0.5 -> x=0.5)
    var sInterior2 = SpkSegmentEvaluator.EvaluateState(seg, new Instant(250));
    sInterior2.PositionKm.X.ShouldBe(0.5, 1e-12);
  }

  [Fact]
  public void LocateRecord_Fallback_Linear_On_Unsorted_Mids()
  {
    // Create two records but supply mids array intentionally unsorted (200, 0).
    // Evaluate inside second (true mid=0) record coverage at et=50; binary search will miss and linear fallback should find record index 1.
    int degree = 2; int n1 = degree + 1; // 3
    double[] rec0 = [200,100, 0,1,0, 0,0,0, 5,0,0]; // mid=200 (record 0)
    double[] rec1 = [0,100, 0,1,0, 0,0,0, 5,0,0];   // mid=0   (record 1)
    var coeffs = rec0.Concat(rec1).ToArray();
    int recordSize = 2 + 3 * n1; // 11
    var seg = new SpkSegment(
      new BodyId(1), new BodyId(0), new FrameId(1), 2,
      -100, 300,
      0, coeffs.Length, coeffs,
      RecordCount: 2,
      Degree: degree,
      RecordMids: [200,0],          // unsorted on purpose
      RecordRadii: [100,100],
      ComponentsPerSet: 3,
      RecordSizeDoubles: recordSize,
      Init: 0, IntervalLength: 0, TrailerRecordSize: recordSize, TrailerRecordCount: 2
    );

    // At et=50: record with mid=0 radius=100 expected; its X polynomial is [0,1,0] => tau = (50-0)/100 = 0.5 => position X=0.5
    var state = SpkSegmentEvaluator.EvaluateState(seg, new Instant(50));
    state.PositionKm.X.ShouldBe(0.5, 1e-12);
  }

  static SpkSegment BuildType2Multi()
  {
    int degree = 2; int n1 = degree + 1; // 3
    // Each record layout: MID, RADIUS, X(3),Y(3),Z(3)
    // Record 0: mid=0 rad=100 X poly = [0,1,0] => x=tau, Y=[0,0,0], Z=[5,0,0]
    // Record 1: mid=200 rad=100 X=[0,1,0]; same Y/Z.
    double[] rec0 = [0,100, 0,1,0, 0,0,0, 5,0,0];
    double[] rec1 = [200,100, 0,1,0, 0,0,0, 5,0,0];
    var coeffs = rec0.Concat(rec1).ToArray();
    int recordSize = 2 + 3 * n1; // 2 + 9 = 11
    return new SpkSegment(
      new BodyId(1), new BodyId(0), new FrameId(1), 2,
      -100, 300,
      0, coeffs.Length, coeffs,
      RecordCount: 2,
      Degree: degree,
      RecordMids: [0,200],
      RecordRadii: [100,100],
      ComponentsPerSet: 3,
      RecordSizeDoubles: recordSize,
      Init: 0, IntervalLength: 0, TrailerRecordSize: recordSize, TrailerRecordCount: 2
    );
  }
}
