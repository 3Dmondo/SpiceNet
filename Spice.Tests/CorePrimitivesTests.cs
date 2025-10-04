using Shouldly;
using Spice.Core;

namespace Spice.Tests;

public class CorePrimitivesTests
{
  const double Tol = 1e-12;

  [Fact]
  public void Vector3d_Arithmetic_Works()
  {
    var a = new Vector3d(1, 2, 3);
    var b = new Vector3d(-4, 5, 0.5);

    var sum = a + b;
    sum.ShouldBe(new Vector3d(-3, 7, 3.5));

    var diff = a - b;
    diff.ShouldBe(new Vector3d(5, -3, 2.5));

    var scaled = a * 2.0;
    scaled.ShouldBe(new Vector3d(2, 4, 6));

    var len = a.Length();
    len.ShouldBe(Math.Sqrt(1 + 4 + 9));

    Vector3d.Zero.Normalize().ShouldBe(Vector3d.Zero); // zero safe
  }

  [Fact]
  public void StateVector_Arithmetic_Works()
  {
    var s1 = new StateVector(new Vector3d(1, 0, 0), new Vector3d(0.1, 0.2, 0.3));
    var s2 = new StateVector(new Vector3d(-1, 2, 5), new Vector3d(0.05, -0.2, 0));

    var sum = s1.Add(s2);
    sum.PositionKm.ShouldBe(new Vector3d(0, 2, 5));
    sum.VelocityKmPerSec.X.ShouldBe(0.15, Tol);
    sum.VelocityKmPerSec.Y.ShouldBe(0.0, Tol);
    sum.VelocityKmPerSec.Z.ShouldBe(0.3, Tol);

    var diff = s1.Subtract(s2);
    diff.PositionKm.ShouldBe(new Vector3d(2, -2, -5));
    diff.VelocityKmPerSec.X.ShouldBe(0.05, Tol);
    diff.VelocityKmPerSec.Y.ShouldBe(0.4, Tol);
    diff.VelocityKmPerSec.Z.ShouldBe(0.3, Tol);

    var scaled = s1.Scale(10);
    scaled.PositionKm.ShouldBe(new Vector3d(10, 0, 0));
    scaled.VelocityKmPerSec.X.ShouldBe(1, Tol);
    scaled.VelocityKmPerSec.Y.ShouldBe(2, Tol);
    scaled.VelocityKmPerSec.Z.ShouldBe(3, Tol);
  }

  [Fact]
  public void Duration_And_Instant_Operations()
  {
    var d1 = new Duration(10.5);
    var d2 = new Duration(5.25);
    (d1 + d2).Seconds.ShouldBe(15.75);
    (d1 - d2).Seconds.ShouldBe(5.25);
    (d2 * 2).Seconds.ShouldBe(10.5);

    var t0 = Instant.J2000;
    var t1 = t0.Add(d1); // fractional seconds truncated via rounding to nearest even -> 10 in this case
    (t1 - t0).Seconds.ShouldBe(10); // current Instant representation only whole seconds
  }

  [Fact]
  public void Chebyshev_Scalar_Correct()
  {
    // f(tau)= c0*T0 + c1*T1 + c2*T2 where T0=1, T1=t, T2=2t^2-1
    // c0=1, c1=2, c2=3 => f(t) = 1 + 2t + 3*(2t^2-1) = 6t^2 +2t -2
    double[] coeffs = [1,2,3];
    var val = Chebyshev.Evaluate(coeffs, 0.5); // 6*0.25 + 1 -2 = 0.5
    val.ShouldBe(0.5, Tol);
  }

  [Fact]
  public void Chebyshev_Vector_Correct()
  {
    double[] cx = [1,0,0]; // => 1
    double[] cy = [0,1,0]; // => tau
    double[] cz = [0,0,1]; // => T2=2tau^2 -1
    var v = Chebyshev.EvaluateVector(cx, cy, cz, 0.25);
    v.X.ShouldBe(1d, Tol);
    v.Y.ShouldBe(0.25, Tol);
    v.Z.ShouldBe(2*0.25*0.25 -1, Tol);
  }

  [Fact]
  public void Chebyshev_Interleaved_Equals_Separate()
  {
    // Two components degree 2: layout [c0_a,c0_b,c1_a,c1_b,c2_a,c2_b]
    double[] interleaved = [1, 0, 2, -1, 3, 4];
    Span<double> results = stackalloc double[2];
    double tau = -0.3;

    Chebyshev.EvaluateInterleaved(interleaved, 2, 2, results, tau);

    // Component 0 coefficients: 1,2,3
    var a = Chebyshev.Evaluate([1d,2d,3d], tau);
    // Component 1 coefficients: 0,-1,4
    var b = Chebyshev.Evaluate([0d,-1d,4d], tau);

    results[0].ShouldBe(a, Tol);
    results[1].ShouldBe(b, Tol);
  }
}
