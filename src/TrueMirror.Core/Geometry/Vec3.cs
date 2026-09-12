using System;
using System.Globalization;

namespace TrueMirror.Core.Geometry
{
    /// <summary>
    /// Immutable 3D vector in SOLIDWORKS model space (metres).
    ///
    /// Ported from tools/mirror_math_reference.py, which carries the executable tests
    /// for this math. Keep the two in step.
    /// </summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public const double Tolerance = 1e-12;

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 Zero => new Vec3(0, 0, 0);
        public static Vec3 UnitX => new Vec3(1, 0, 0);
        public static Vec3 UnitY => new Vec3(0, 1, 0);
        public static Vec3 UnitZ => new Vec3(0, 0, 1);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 v, double k) => new Vec3(v.X * k, v.Y * k, v.Z * k);
        public static Vec3 operator *(double k, Vec3 v) => v * k;
        public static Vec3 operator -(Vec3 v) => new Vec3(-v.X, -v.Y, -v.Z);

        public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;

        public Vec3 Cross(Vec3 o) => new Vec3(
            Y * o.Z - Z * o.Y,
            Z * o.X - X * o.Z,
            X * o.Y - Y * o.X);

        public double Length => Math.Sqrt(Dot(this));

        public Vec3 Normalized()
        {
            var n = Length;
            if (n < Tolerance)
            {
                throw new InvalidOperationException("Cannot normalize a zero-length vector.");
            }

            return this * (1.0 / n);
        }

        public bool IsClose(Vec3 o, double tol = 1e-9) => (this - o).Length <= tol;

        /// <summary>Converts to the double[3] arrays the SOLIDWORKS API expects.</summary>
        public double[] ToArray() => new[] { X, Y, Z };

        public static Vec3 FromArray(double[] values)
        {
            if (values == null || values.Length < 3)
            {
                throw new ArgumentException("Expected an array of at least 3 doubles.", nameof(values));
            }

            return new Vec3(values[0], values[1], values[2]);
        }

        public bool Equals(Vec3 other) => IsClose(other, Tolerance);

        public override bool Equals(object obj) => obj is Vec3 v && Equals(v);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = X.GetHashCode();
                h = (h * 397) ^ Y.GetHashCode();
                h = (h * 397) ^ Z.GetHashCode();
                return h;
            }
        }

        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "({0:G6}, {1:G6}, {2:G6})", X, Y, Z);
    }
}
