using System;

namespace TrueMirror.Core.Geometry
{
    /// <summary>
    /// The plane a part is being mirrored about, defined by a point on it and a unit normal.
    ///
    /// Verified in tools/mirror_math_reference.py: reflection is an involution, preserves
    /// distances, fixes points on the plane, and directions ignore the plane's offset.
    /// </summary>
    public sealed class MirrorPlane
    {
        public Vec3 Point { get; }

        /// <summary>Unit normal. Always normalized by the constructor.</summary>
        public Vec3 Normal { get; }

        public MirrorPlane(Vec3 point, Vec3 normal)
        {
            Point = point;
            Normal = normal.Normalized();
        }

        /// <summary>p' = p - 2((p - P) . n) n</summary>
        public Vec3 ReflectPoint(Vec3 p)
        {
            var d = (p - Point).Dot(Normal);
            return p - Normal * (2.0 * d);
        }

        /// <summary>
        /// v' = v - 2(v . n) n
        ///
        /// Directions are unaffected by where the plane sits, only by its orientation.
        /// Using ReflectPoint on a direction is a classic bug: it silently adds the
        /// plane's offset into what should be a free vector.
        /// </summary>
        public Vec3 ReflectDirection(Vec3 v) => v - Normal * (2.0 * v.Dot(Normal));

        public double SignedDistance(Vec3 p) => (p - Point).Dot(Normal);

        /// <summary>
        /// True when every supplied point lies on the plane, i.e. the mirror would be a
        /// no-op in that direction. Used to reject degenerate user selections early.
        /// </summary>
        public bool ContainsPoint(Vec3 p, double tol = 1e-9) => Math.Abs(SignedDistance(p)) <= tol;

        public override string ToString() => $"MirrorPlane(point={Point}, normal={Normal})";
    }
}
