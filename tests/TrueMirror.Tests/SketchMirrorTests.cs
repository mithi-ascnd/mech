using System;
using TrueMirror.Core.Geometry;
using TrueMirror.Core.Model;
using Xunit;

namespace TrueMirror.Tests
{
    /// <summary>
    /// Mirrors tools/sketch_mirror_reference.py. Runs without SOLIDWORKS.
    ///
    /// The headline test here is <see cref="MirroredArcTracesTheReflectedPathPointwise"/>
    /// and its companion <see cref="WithoutTheDirectionFlipTheArcIsGeometricallyWrong"/>.
    /// The Python run quantified the bug at 20 mm of error on a 10 mm arc - geometry that
    /// still renders plausibly, which is exactly why it needs a test rather than an eyeball.
    /// </summary>
    public class SketchMirrorTests
    {
        private static readonly MirrorPlane Plane =
            new MirrorPlane(new Vec3(0.05, 0, 0), Vec3.UnitX);

        private static readonly SketchFrame Frame =
            new SketchFrame(Vec3.Zero, Vec3.UnitX, Vec3.UnitY);

        private static SketchFrame MirroredFrame => MirrorTransform.MirrorFrame(Frame, Plane);

        // ---- involution ---------------------------------------------------

        [Fact]
        public void LineMirroredTwiceIsTheOriginal()
        {
            var line = new SketchLineEntity
            {
                Start = new Vec2(0.01, 0.02),
                End = new Vec2(0.03, -0.04)
            };

            var twice = (SketchLineEntity)line.Mirrored().Mirrored();

            Assert.True(twice.Start.IsClose(line.Start));
            Assert.True(twice.End.IsClose(line.End));
        }

        [Fact]
        public void ArcMirroredTwiceIsTheOriginal()
        {
            var arc = QuarterArc();
            var twice = (SketchArcEntity)arc.Mirrored().Mirrored();

            Assert.True(twice.Center.IsClose(arc.Center));
            Assert.True(twice.Start.IsClose(arc.Start));
            Assert.True(twice.End.IsClose(arc.End));
            Assert.Equal(arc.Direction, twice.Direction);
        }

        // ---- the arc direction rule ---------------------------------------

        [Fact]
        public void MirroredArcFlipsItsDirectionFlag()
        {
            var arc = QuarterArc();
            var mirrored = (SketchArcEntity)arc.Mirrored();

            Assert.Equal(1, arc.Direction);
            Assert.Equal(-1, mirrored.Direction);
        }

        [Fact]
        public void MirroredArcKeepsItsRadius()
        {
            var arc = QuarterArc();
            var mirrored = (SketchArcEntity)arc.Mirrored();

            Assert.True(Math.Abs(mirrored.Radius - arc.Radius) < 1e-12);
        }

        [Fact]
        public void MirroredArcTracesTheReflectedPathPointwise()
        {
            var arc = QuarterArc();
            var mirrored = (SketchArcEntity)arc.Mirrored();

            var worst = 0.0;
            for (var i = 0; i <= 100; i++)
            {
                var t = i / 100.0;
                var expected = Plane.ReflectPoint(Frame.ToModel(Sample(arc, t)));
                var actual = MirroredFrame.ToModel(Sample(mirrored, t));
                worst = Math.Max(worst, (expected - actual).Length);
            }

            Assert.True(worst < 1e-12, $"worst pointwise error was {worst:E3} m");
        }

        [Fact]
        public void WithoutTheDirectionFlipTheArcIsGeometricallyWrong()
        {
            // Reproduces the bug deliberately: mirror the three points but keep the flag.
            var arc = QuarterArc();
            var naive = new SketchArcEntity
            {
                Center = MirrorTransform.MirrorUv(arc.Center),
                Start = MirrorTransform.MirrorUv(arc.Start),
                End = MirrorTransform.MirrorUv(arc.End),
                Direction = arc.Direction      // NOT flipped - this is the bug
            };

            var worst = 0.0;
            for (var i = 0; i <= 100; i++)
            {
                var t = i / 100.0;
                var expected = Plane.ReflectPoint(Frame.ToModel(Sample(arc, t)));
                var actual = MirroredFrame.ToModel(Sample(naive, t));
                worst = Math.Max(worst, (expected - actual).Length);
            }

            // Python measured ~20 mm on this 10 mm arc. Assert it is grossly wrong so the
            // test fails loudly if someone "fixes" SketchArcEntity by removing the flip.
            Assert.True(worst > 1e-3,
                $"expected the naive mirror to be grossly wrong, but error was only {worst:E3} m");
        }

        // ---- circles -------------------------------------------------------

        [Fact]
        public void FullCircleKeepsItsDirectionFlag()
        {
            var circle = new SketchArcEntity
            {
                Center = new Vec2(0, 0),
                Start = new Vec2(0.01, 0),
                End = new Vec2(0.01, 0),
                Direction = 1,
                IsCircle = true
            };

            Assert.Equal(1, ((SketchArcEntity)circle.Mirrored()).Direction);
        }

        // ---- construction geometry ----------------------------------------

        [Fact]
        public void ConstructionFlagSurvivesMirroring()
        {
            var line = new SketchLineEntity
            {
                Start = new Vec2(0, 0),
                End = new Vec2(0.01, 0),
                IsConstruction = true
            };

            Assert.True(line.Mirrored().IsConstruction);
        }

        [Fact]
        public void EntityIndexSurvivesMirroringSoRelationsStillBind()
        {
            var line = new SketchLineEntity
            {
                Index = 7,
                Start = new Vec2(0, 0),
                End = new Vec2(0.01, 0)
            };

            Assert.Equal(7, line.Mirrored().Index);
        }

        // ---- helpers -------------------------------------------------------

        private static SketchArcEntity QuarterArc() => new SketchArcEntity
        {
            Center = new Vec2(0, 0),
            Start = new Vec2(0.01, 0),
            End = new Vec2(0, 0.01),
            Direction = 1
        };

        /// <summary>Point at parameter t along an arc, respecting its direction flag.</summary>
        private static Vec2 Sample(SketchArcEntity arc, double t)
        {
            var radius = arc.Radius;
            var a0 = AngleOf(arc, arc.Start);
            var a1 = AngleOf(arc, arc.End);

            var sweep = (a1 - a0) % (2 * Math.PI);
            if (sweep < 0) sweep += 2 * Math.PI;
            if (arc.Direction < 0) sweep -= 2 * Math.PI;

            var a = a0 + sweep * t;
            return new Vec2(
                arc.Center.U + radius * Math.Cos(a),
                arc.Center.V + radius * Math.Sin(a));
        }

        private static double AngleOf(SketchArcEntity arc, Vec2 p)
        {
            var a = Math.Atan2(p.V - arc.Center.V, p.U - arc.Center.U) % (2 * Math.PI);
            return a < 0 ? a + 2 * Math.PI : a;
        }
    }
}
