using System.Collections.Generic;
using TrueMirror.Core.Geometry;

namespace TrueMirror.Core.Model
{
    /// <summary>
    /// A sketch lifted out of SOLIDWORKS into plain data.
    ///
    /// Geometry is held in the sketch's own 2D space, which is what SOLIDWORKS itself
    /// reports from ISketchPoint, and is also the space in which mirroring reduces to
    /// the single rule (u, v) -> (u, -v).
    /// </summary>
    public sealed class CapturedSketch
    {
        public string Name { get; set; }

        /// <summary>The sketch's coordinate system in model space.</summary>
        public SketchFrame Frame { get; set; }

        public List<SketchEntity> Entities { get; } = new List<SketchEntity>();

        public List<CapturedRelation> Relations { get; } = new List<CapturedRelation>();

        /// <summary>
        /// False when the source sketch was not fully constrained. Mirroring reproduces the
        /// ambiguity rather than resolving it, so this is surfaced in the fidelity report.
        /// </summary>
        public bool? IsFullyDefined { get; set; }

        public bool Is3D { get; set; }
    }

    public abstract class SketchEntity
    {
        /// <summary>
        /// Construction geometry drives other geometry but is not part of any profile.
        /// It must round-trip or downstream features lose their references.
        /// </summary>
        public bool IsConstruction { get; set; }

        /// <summary>
        /// Index within the source sketch. Relations are stored against these indices so
        /// they can be re-applied after the entities are recreated.
        /// </summary>
        public int Index { get; set; }

        public abstract SketchEntity Mirrored();
    }

    public sealed class SketchLineEntity : SketchEntity
    {
        public Vec2 Start { get; set; }
        public Vec2 End { get; set; }

        public override SketchEntity Mirrored() => new SketchLineEntity
        {
            Index = Index,
            IsConstruction = IsConstruction,
            Start = MirrorTransform.MirrorUv(Start),
            End = MirrorTransform.MirrorUv(End)
        };
    }

    public sealed class SketchArcEntity : SketchEntity
    {
        public Vec2 Center { get; set; }
        public Vec2 Start { get; set; }
        public Vec2 End { get; set; }

        /// <summary>+1 counter-clockwise, -1 clockwise, matching ISketchManager::CreateArc.</summary>
        public int Direction { get; set; } = 1;

        public bool IsCircle { get; set; }

        public double Radius => (Start - Center).Length;

        /// <summary>
        /// Reflecting the three defining points is not enough. Under (u, v) -> (u, -v) the
        /// traversal direction reverses, so the direction flag must flip too. Mirroring the
        /// points alone yields an arc that spans the complementary sweep - geometry that
        /// looks plausible in a thumbnail and is wrong.
        ///
        /// A full circle has no meaningful sweep, so its flag is left alone.
        /// </summary>
        public override SketchEntity Mirrored() => new SketchArcEntity
        {
            Index = Index,
            IsConstruction = IsConstruction,
            Center = MirrorTransform.MirrorUv(Center),
            Start = MirrorTransform.MirrorUv(Start),
            End = MirrorTransform.MirrorUv(End),
            IsCircle = IsCircle,
            Direction = IsCircle ? Direction : -Direction
        };
    }

    public sealed class SketchSplineEntity : SketchEntity
    {
        public List<Vec2> ControlPoints { get; } = new List<Vec2>();

        public override SketchEntity Mirrored()
        {
            var mirrored = new SketchSplineEntity
            {
                Index = Index,
                IsConstruction = IsConstruction
            };

            foreach (var p in ControlPoints)
            {
                mirrored.ControlPoints.Add(MirrorTransform.MirrorUv(p));
            }

            return mirrored;
        }
    }

    public sealed class SketchPointEntity : SketchEntity
    {
        public Vec2 Position { get; set; }

        public override SketchEntity Mirrored() => new SketchPointEntity
        {
            Index = Index,
            IsConstruction = IsConstruction,
            Position = MirrorTransform.MirrorUv(Position)
        };
    }

    /// <summary>
    /// An entity type the reader recognised but cannot reproduce (ellipse, parabola, text).
    /// Kept in the model so the fidelity report can name what was lost rather than
    /// silently dropping it.
    /// </summary>
    public sealed class UnsupportedSketchEntity : SketchEntity
    {
        public string Kind { get; set; }

        public override SketchEntity Mirrored() => new UnsupportedSketchEntity
        {
            Index = Index,
            IsConstruction = IsConstruction,
            Kind = Kind
        };
    }

    public sealed class CapturedRelation
    {
        /// <summary>Value of swConstraintType_e from the source.</summary>
        public int ConstraintType { get; set; }

        public string TypeName { get; set; }

        /// <summary>Indices into <see cref="CapturedSketch.Entities"/>.</summary>
        public List<int> EntityIndices { get; } = new List<int>();
    }
}
