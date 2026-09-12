using System;

namespace TrueMirror.Core.Geometry
{
    /// <summary>
    /// The crux of TrueMirror. Everything downstream derives from the single sign flip
    /// established here, so read this before touching any transcriber.
    ///
    /// Reflection has determinant -1. Reflecting all three axes of a right-handed sketch
    /// frame yields a LEFT-handed frame, because
    ///
    ///     R(x) x R(y) == -R(x x y) == -R(n)
    ///
    /// SOLIDWORKS will not accept a left-handed sketch frame. Right-handedness is restored
    /// by negating exactly one in-plane axis; we negate Y. The consequence is that a sketch
    /// point at local (u, v) in the source lands at local (u, -v) in the mirror.
    ///
    /// That flip is why arcs reverse sweep direction, why sketch angles negate, and why
    /// draft angles change sign. All of it is one fact wearing different hats.
    ///
    /// Verified in tools/mirror_math_reference.py, including a direct check that
    /// MirrorUv agrees with reflecting through model space (residual 4.4e-16) and that
    /// the naive all-axes reflection really is left-handed.
    /// </summary>
    public static class MirrorTransform
    {
        public const double TwoPi = 2.0 * Math.PI;

        /// <summary>
        /// Reflects a sketch frame and returns a right-handed frame on the mirrored plane.
        /// Pair every use of this with <see cref="MirrorUv"/> - they are two halves of one
        /// convention and using either alone produces a mirrored-but-wrong sketch.
        /// </summary>
        public static SketchFrame MirrorFrame(SketchFrame frame, MirrorPlane plane)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (plane == null) throw new ArgumentNullException(nameof(plane));

            return new SketchFrame(
                origin: plane.ReflectPoint(frame.Origin),
                xAxis: plane.ReflectDirection(frame.XAxis),
                yAxis: -plane.ReflectDirection(frame.YAxis));
        }

        /// <summary>
        /// Maps a sketch-local coordinate into the frame produced by <see cref="MirrorFrame"/>.
        /// </summary>
        public static Vec2 MirrorUv(Vec2 p) => new Vec2(p.U, -p.V);

        public static Vec2 MirrorUv(double u, double v) => new Vec2(u, -v);

        /// <summary>
        /// A direction angle measured CCW from local +X becomes -angle under (u, v) -> (u, -v).
        /// Normalized to [0, 2pi).
        /// </summary>
        public static double MirrorAngle(double angleRadians)
        {
            var a = (-angleRadians) % TwoPi;
            return a < 0 ? a + TwoPi : a;
        }

        /// <summary>
        /// Signed shortest sweep from a to b, positive counter-clockwise. Used to decide
        /// whether a reflected arc needs its endpoints swapped.
        /// </summary>
        public static double SweepCcw(double a, double b)
        {
            var d = (b - a) % TwoPi;
            if (d < 0) d += TwoPi;
            return d <= Math.PI ? d : d - TwoPi;
        }

        /// <summary>
        /// True when a signed angular quantity - a draft angle, a revolve angle measured
        /// with direction - must have its sign inverted in the mirrored part.
        /// Magnitudes (a blind depth, a fillet radius) must NOT be passed through here.
        /// </summary>
        public static double MirrorSignedAngle(double angleRadians) => -angleRadians;

        /// <summary>
        /// Thread handedness is an engineering decision, not a geometric one.
        ///
        /// Reflecting a right-hand thread produces a left-hand thread. That is
        /// geometrically correct and almost always wrong for the engineer: an
        /// opposite-hand bracket still takes right-hand fasteners. Default to preserving
        /// handedness and record the choice in the fidelity report.
        /// </summary>
        public static bool MirrorThreadHandedness(bool sourceIsRightHand, bool preserveHandedness)
            => preserveHandedness ? sourceIsRightHand : !sourceIsRightHand;
    }
}
