// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedMember.Global
namespace Spice.Core;

/// <summary>
/// Immutable 3D vector of doubles representing a spatial quantity in kilometers (km) unless otherwise specified.
/// </summary>
public readonly record struct Vector3d(double X, double Y, double Z)
{
  /// <summary>Zero vector (0,0,0).</summary>
  public static readonly Vector3d Zero = new(0d, 0d, 0d);

  /// <summary>Create a new vector from components.</summary>
  public static Vector3d From(double x, double y, double z) => new(x, y, z);

  /// <summary>Component-wise addition.</summary>
  public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
  /// <summary>Component-wise subtraction.</summary>
  public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
  /// <summary>Negation.</summary>
  public static Vector3d operator -(Vector3d v) => new(-v.X, -v.Y, -v.Z);
  /// <summary>Scale by scalar (vector * scalar).</summary>
  public static Vector3d operator *(Vector3d v, double s) => new(v.X * s, v.Y * s, v.Z * s);
  /// <summary>Scale by scalar (scalar * vector).</summary>
  public static Vector3d operator *(double s, Vector3d v) => v * s;
  /// <summary>Divide by scalar.</summary>
  public static Vector3d operator /(Vector3d v, double s) => new(v.X / s, v.Y / s, v.Z / s);

  /// <summary>Dot product a·b (km^2).</summary>
  public static double Dot(in Vector3d a, in Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

  /// <summary>Euclidean norm |v| (km).</summary>
  public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);

  /// <summary>Return normalized vector (unitless). If zero vector, returns zero.</summary>
  public Vector3d Normalize()
  {
    var len = Length();
    return len == 0d ? Zero : this / len;
  }
}

/// <summary>
/// Extension helpers for <see cref="Vector3d"/> providing functional style operations.
/// </summary>
public static class Vector3dExtensions
{
  /// <summary>Add two vectors.</summary>
  public static Vector3d Add(this in Vector3d a, in Vector3d b) => a + b;
  /// <summary>Subtract b from a.</summary>
  public static Vector3d Subtract(this in Vector3d a, in Vector3d b) => a - b;
  /// <summary>Scale vector by scalar.</summary>
  public static Vector3d Scale(this in Vector3d v, double s) => v * s;
}
