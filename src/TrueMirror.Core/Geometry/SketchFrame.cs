using System;

namespace TrueMirror.Core.Geometry
{
    /// <summary>
    /// A sketch's local coordinate system expressed in model space.
    /// SOLIDWORKS sketch frames are right-handed: XAxis x YAxis == Normal.
    /// </summary>
    public sealed class SketchFrame
    {
        public Vec3 Origin { get; }
        public Vec3 XAxis { get; }
        public Vec3 YAxis { get; }

        public SketchFrame(Vec3 origin, Vec3 xAxis, Vec3 yAxis)
        {
            Origin = origin;
            XAxis = xAxis;
            YAxis = yAxis;
        }

        public Vec3 Normal => XAxis.Cross(YAxis);

        public bool IsRightHanded(double tol = 1e-9)
            => Math.Abs(Normal.Length - 1.0) <= tol;

        public Vec3 ToModel(double u, double v) => Origin + XAxis * u + YAxis * v;

        public Vec3 ToModel(Vec2 p) => ToModel(p.U, p.V);

        public Vec2 ToSketch(Vec3 p)
        {
            var d = p - Origin;
            return new Vec2(d.Dot(XAxis), d.Dot(YAxis));
        }

        /// <summary>
        /// Builds a frame from the 16-element transform array SOLIDWORKS returns from
        /// IMathTransform::ArrayData. Layout is rotation (9), translation (3), scale (1),
        /// then 3 unused. The rotation is stored column-major: elements 0-2 are the x axis.
        /// </summary>
        public static SketchFrame FromTransformArray(double[] t)
        {
            if (t == null || t.Length < 13)
            {
                throw new ArgumentException(
                    "Expected at least 13 elements from IMathTransform::ArrayData.", nameof(t));
            }

            return new SketchFrame(
                origin: new Vec3(t[9], t[10], t[11]),
                xAxis: new Vec3(t[0], t[1], t[2]),
                yAxis: new Vec3(t[3], t[4], t[5]));
        }

        public override string ToString()
            => $"SketchFrame(origin={Origin}, x={XAxis}, y={YAxis})";
    }

    /// <summary>A point or vector in a sketch's 2D local space.</summary>
    public readonly struct Vec2
    {
        public double U { get; }
        public double V { get; }

        public Vec2(double u, double v)
        {
            U = u;
            V = v;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.U + b.U, a.V + b.V);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.U - b.U, a.V - b.V);
        public static Vec2 operator *(Vec2 a, double k) => new Vec2(a.U * k, a.V * k);

        public double Length => Math.Sqrt(U * U + V * V);

        public bool IsClose(Vec2 o, double tol = 1e-9) => (this - o).Length <= tol;

        public override string ToString() => $"({U:G6}, {V:G6})";
    }
}
