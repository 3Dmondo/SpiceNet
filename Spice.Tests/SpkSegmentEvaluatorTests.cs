using Shouldly;
using Spice.Core;
using Spice.Kernels;

namespace Spice.Tests;

public class SpkSegmentEvaluatorTests
{
  static SpkSegment MakeType2Segment(double start, double stop, double[] x, double[] y, double[] z)
  {
    var coeffs = new double[x.Length + y.Length + z.Length];
    Array.Copy(x, 0, coeffs, 0, x.Length);
    Array.Copy(y, 0, coeffs, x.Length, y.Length);
    Array.Copy(z, 0, coeffs, x.Length + y.Length, z.Length);
    return new SpkSegment(new BodyId(1), new BodyId(0), new FrameId(1), 2, start, stop, 0, coeffs.Length, coeffs);
  }

  static SpkSegment MakeType3Segment(double start, double stop, double[] px, double[] py, double[] pz, double[] vx, double[] vy, double[] vz)
  {
    var coeffs = new double[px.Length * 6];
    int n = px.Length;
    Array.Copy(px, 0, coeffs, 0, n);
    Array.Copy(py, 0, coeffs, n, n);
    Array.Copy(pz, 0, coeffs, 2 * n, n);
    Array.Copy(vx, 0, coeffs, 3 * n, n);
    Array.Copy(vy, 0, coeffs, 4 * n, n);
    Array.Copy(vz, 0, coeffs, 5 * n, n);
    return new SpkSegment(new BodyId(2), new BodyId(0), new FrameId(1), 3, start, stop, 0, coeffs.Length, coeffs);
  }

  [Theory]
  [InlineData(-100,100,0)]
  [InlineData(-100,100,50)]
  [InlineData(-100,100,-75)]
  [InlineData(-100,100,100)]
  public void Type2_Evaluates_Position_And_Derived_Velocity(double start, double stop, double et)
  {
    // Position Chebyshev series components (degree 2): f(tau)= c0 + c1*tau + c2*(2tau^2 -1)
    // Derivative wrt tau: f'(tau)= c1 + 4*c2*tau ; wrt time: f'(t)= (c1 + 4*c2*tau)/radius
    double[] cx = [1,2,3];   // => f'x = 2 + 12 tau
    double[] cy = [-1,0,2];  // => f'y = 0 + 8 tau
    double[] cz = [0.5,-0.5,1]; // => f'z = -0.5 + 4 tau
    var seg = MakeType2Segment(start, stop, cx, cy, cz);

    var state = SpkSegmentEvaluator.EvaluateState(seg, new Instant((long)et));

    double mid = 0.5*(start+stop);
    double radius = 0.5*(stop-start);
    double tau = radius==0?0:(et-mid)/radius;

    static double Pos(double[] c, double tau) => c[0] + c[1]*tau + c[2]*(2*tau*tau -1);
    static double Vel(double[] c, double tau, double radius) => (c[1] + 4*c[2]*tau)/radius;

    state.PositionKm.X.ShouldBe(Pos(cx,tau),1e-12);
    state.PositionKm.Y.ShouldBe(Pos(cy,tau),1e-12);
    state.PositionKm.Z.ShouldBe(Pos(cz,tau),1e-12);

    state.VelocityKmPerSec.X.ShouldBe(Vel(cx,tau,radius),1e-12);
    state.VelocityKmPerSec.Y.ShouldBe(Vel(cy,tau,radius),1e-12);
    state.VelocityKmPerSec.Z.ShouldBe(Vel(cz,tau,radius),1e-12);
  }

  [Theory]
  [InlineData(-50,50,0)]
  [InlineData(-50,50,25)]
  [InlineData(-50,50,-30)]
  public void Type3_Evaluates_Position_And_Velocity(double start, double stop, double et)
  {
    // Position series degree 2; velocity series degree 1 (padded with zero) representing exact derivative.
    double[] px = [1,2,3]; // derivative tau: 2 + 12 tau
    double[] py = [0,1,0]; // derivative: 1
    double[] pz = [2,-1,0]; // derivative: -1

    double[] vx = [2,12,0]; // k=1 coeff 2, k=2 coeff 12 corresponds to derivative tau form (c1 + 4c2 tau)
    double[] vy = [1,0,0];
    double[] vz = [-1,0,0];

    var seg = MakeType3Segment(start, stop, px, py, pz, vx, vy, vz);
    var state = SpkSegmentEvaluator.EvaluateState(seg, new Instant((long)et));

    double mid = 0.5*(start+stop);
    double radius = 0.5*(stop-start);
    double tau = radius==0?0:(et-mid)/radius;

    static double Pos(double[] c, double tau) => c[0] + c[1]*tau + c[2]*(2*tau*tau -1);
    static double VelFromSeries(double[] c, double tau) => c[0] + c[1]*tau + c[2]*(2*tau*tau -1); // already velocity Chebyshev

    state.PositionKm.X.ShouldBe(Pos(px,tau),1e-12);
    state.PositionKm.Y.ShouldBe(Pos(py,tau),1e-12);
    state.PositionKm.Z.ShouldBe(Pos(pz,tau),1e-12);

    state.VelocityKmPerSec.X.ShouldBe(VelFromSeries(vx,tau),1e-12);
    state.VelocityKmPerSec.Y.ShouldBe(VelFromSeries(vy,tau),1e-12);
    state.VelocityKmPerSec.Z.ShouldBe(VelFromSeries(vz,tau),1e-12);
  }

  [Fact]
  public void Evaluate_OutOfRange_Throws()
  {
    var seg = MakeType2Segment(0, 10, [1,0,0],[0,1,0],[0,0,1]);
    Should.Throw<ArgumentOutOfRangeException>(() => SpkSegmentEvaluator.EvaluateState(seg, new Instant(-1)));
    Should.Throw<ArgumentOutOfRangeException>(() => SpkSegmentEvaluator.EvaluateState(seg, new Instant(11)));
  }
}
