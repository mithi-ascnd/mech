using System;
using TrueMirror.Core.Geometry;
using Xunit;

namespace TrueMirror.Tests
{
    /// <summary>
    /// These run WITHOUT SOLIDWORKS. The geometry layer has no interop dependency, so the
    /// most error-prone part of the tool can be verified on any Windows machine with no
    /// licence and no CAD install.
    ///
    /// Each test mirrors one in tools/mirror_math_reference.py, which was executed during
    /// development. Keep the two in step: if you change the mirroring convention here,
    /// change it there and re-run both.
    /// </summary>
    public class MirrorMathTests
    {
        private const double Tol = 1e-12;

        private static readonly Vec3 X = Vec3.UnitX;
        private static readonly Vec3 Y = Vec3.UnitY;
        private static readonly Vec3 Z = Vec3.UnitZ;

        private static MirrorPlane Yz => new MirrorPlane(Vec3.Zero, X);

        // ---- reflection basics -------------------------------------------

        [Fact]
        public void PointReflectsAcrossYz()
            => Assert.True(Yz.ReflectPoint(new Vec3(3, 2, 1)).IsClose(new Vec3(-3, 2, 1)));

        [Fact]
        public void PointOnPlaneIsFixed()
            => Assert.True(Yz.ReflectPoint(new Vec3(0, 5, 7)).IsClose(new Vec3(0, 5, 7)));

        [Fact]
        public void ReflectionIsAnInvolution()
        {
            var p = new Vec3(3, 2, 1);
            Assert.True(Yz.ReflectPoint(Yz.ReflectPoint(p)).IsClose(p));
        }

        [Fact]
        public void DirectionIgnoresPlaneOffset()
        {
            var offset = new MirrorPlane(new Vec3(10, 0, 0), X);
            Assert.True(offset.ReflectDirection(new Vec3(1, 2, 3)).IsClose(new Vec3(-1, 2, 3)));
        }

        [Fact]
        public void OffsetPlaneReflectsAboutItsOwnPosition()
        {
            var offset = new MirrorPlane(new Vec3(5, 0, 0), X);
            Assert.True(offset.ReflectPoint(new Vec3(7, 1, 1)).IsClose(new Vec3(3, 1, 1)));
        }

        [Fact]
        public void DistancesArePreserved()
        {
            var a = new Vec3(1, 2, 3);
            var b = new Vec3(4, 6, 3);
            var before = (a - b).Length;
            var after = (Yz.ReflectPoint(a) - Yz.ReflectPoint(b)).Length;
            Assert.True(Math.Abs(before - after) < Tol);
        }

        // ---- the handedness result ---------------------------------------

        [Fact]
        public void NaiveReflectionOfAllAxesIsLeftHanded()
        {
            // This is the trap MirrorTransform exists to avoid. Documented as a test so it
            // cannot be "simplified" away by someone who has not hit it.
            var front = new SketchFrame(Vec3.Zero, X, Y);

            var naiveX = Yz.ReflectDirection(front.XAxis);
            var naiveY = Yz.ReflectDirection(front.YAxis);
            var naiveNormal = naiveX.Cross(naiveY);

            Assert.True(naiveNormal.IsClose(-Yz.ReflectDirection(front.Normal)));
        }

        [Fact]
        public void MirrorFrameRestoresRightHandedness()
        {
            var front = new SketchFrame(Vec3.Zero, X, Y);
            var mirrored = MirrorTransform.MirrorFrame(front, Yz);

            Assert.True(mirrored.Normal.IsClose(mirrored.XAxis.Cross(mirrored.YAxis)));
            Assert.True(mirrored.Normal.IsClose(Yz.ReflectDirection(front.Normal)));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(0, 1)]
        [InlineData(3, -4)]
        [InlineData(-2.5, 7.25)]
        [InlineData(100, -0.001)]
        public void MirrorUvAgreesWithReflectingThroughModelSpace(double u, double v)
        {
            var tilted = new SketchFrame(
                new Vec3(1, 2, 3),
                new Vec3(1, 1, 0).Normalized(),
                new Vec3(-1, 1, 0).Normalized());

            var plane = new MirrorPlane(new Vec3(2, 0, 0), X);
            var mirroredFrame = MirrorTransform.MirrorFrame(tilted, plane);

            var expected = plane.ReflectPoint(tilted.ToModel(u, v));
            var actual = mirroredFrame.ToModel(MirrorTransform.MirrorUv(u, v));

            Assert.True(expected.IsClose(actual, 1e-12),
                $"expected {expected}, got {actual}");
        }

        // ---- angles -------------------------------------------------------

        [Fact]
        public void MirroredArcSweepsTheOtherWay()
        {
            const double start = 0.0;
            const double end = Math.PI / 2;

            Assert.True(MirrorTransform.SweepCcw(start, end) > 0);

            var ms = MirrorTransform.MirrorAngle(start);
            var me = MirrorTransform.MirrorAngle(end);

            Assert.True(MirrorTransform.SweepCcw(ms, me) < 0);
        }

        [Fact]
        public void AngleMirroringIsAnInvolution()
        {
            const double a = 1.234;
            Assert.True(Math.Abs(MirrorTransform.MirrorAngle(MirrorTransform.MirrorAngle(a)) - a) < Tol);
        }

        [Fact]
        public void SignedAnglesInvertButMagnitudesMustNot()
        {
            // Draft angles are signed and invert. Depths and radii are magnitudes and must
            // never be routed through MirrorSignedAngle - this test documents the contract.
            Assert.Equal(-0.1, MirrorTransform.MirrorSignedAngle(0.1), 12);
        }

        // ---- thread handedness -------------------------------------------

        [Fact]
        public void ThreadHandednessIsPreservedByDefault()
        {
            // Geometrically a mirrored RH thread is LH. That is almost always wrong for a
            // fastener, so the default preserves it.
            Assert.True(MirrorTransform.MirrorThreadHandedness(true, preserveHandedness: true));
            Assert.False(MirrorTransform.MirrorThreadHandedness(true, preserveHandedness: false));
        }

        // ---- guards --------------------------------------------------------

        [Fact]
        public void NormalizingAZeroVectorThrowsRatherThanReturningGarbage()
            => Assert.Throws<InvalidOperationException>(() => Vec3.Zero.Normalized());

        [Fact]
        public void MirrorPlaneNormalizesItsNormal()
        {
            var plane = new MirrorPlane(Vec3.Zero, new Vec3(5, 0, 0));
            Assert.True(Math.Abs(plane.Normal.Length - 1.0) < Tol);
        }
    }
}
