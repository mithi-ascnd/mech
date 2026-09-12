using System.Collections.Generic;
using TrueMirror.Core.Geometry;
using TrueMirror.Core.Resolver;

namespace TrueMirror.Core.Model
{
    /// <summary>
    /// A feature lifted out of SOLIDWORKS into plain data, ready to be mirrored and re-authored.
    /// No interop types leak into this layer, so the mirroring logic is unit-testable.
    /// </summary>
    public abstract class CapturedFeature
    {
        public string Name { get; set; }

        /// <summary>Raw IFeature::GetTypeName2 value.</summary>
        public string TypeName { get; set; }

        public bool IsSuppressed { get; set; }

        /// <summary>Depth-first index in the source tree. Rebuild order must follow this.</summary>
        public int Index { get; set; }

        /// <summary>Produces the mirrored form of this feature.</summary>
        public abstract CapturedFeature Mirrored(MirrorPlane plane);
    }

    /// <summary>
    /// Extruded boss or cut.
    ///
    /// DERIVED RESULT, worth stating because it is counter-intuitive: the direction flag
    /// passes through the mirror UNCHANGED.
    ///
    /// SOLIDWORKS defines extrude direction relative to the sketch normal (along it for a
    /// boss, opposite for a cut), and MirrorTransform.MirrorFrame yields a mirrored sketch
    /// whose normal is exactly the reflected source normal:
    ///
    ///     n' = x' cross y' = R(x) cross (-R(y)) = -(R(x) cross R(y)) = -(-R(n)) = R(n)
    ///
    /// So "along the sketch normal" already reflects itself. Flipping Dir here would
    /// extrude the mirrored feature backwards - a common and confusing bug.
    ///
    /// Draft angles are different: they are signed in-plane quantities and DO invert.
    /// </summary>
    public sealed class ExtrudeFeature : CapturedFeature
    {
        /// <summary>Name of the sketch this consumes.</summary>
        public string ProfileSketchName { get; set; }

        public bool IsCut { get; set; }

        /// <summary>True for single-ended (the Sd parameter of FeatureExtrusion3).</summary>
        public bool SingleEnded { get; set; } = true;

        /// <summary>The Dir parameter. Passes through unchanged - see the class remarks.</summary>
        public bool ReverseDirection { get; set; }

        /// <summary>swEndConditions_e for direction 1.</summary>
        public int EndCondition1 { get; set; }

        /// <summary>swEndConditions_e for direction 2.</summary>
        public int EndCondition2 { get; set; }

        /// <summary>Depth in metres, direction 1. A magnitude - never negated by mirroring.</summary>
        public double Depth1 { get; set; }

        public double Depth2 { get; set; }

        public bool Draft1Enabled { get; set; }
        public bool Draft2Enabled { get; set; }

        /// <summary>Draft angle in radians. Signed, so it inverts under mirroring.</summary>
        public double DraftAngle1 { get; set; }
        public double DraftAngle2 { get; set; }

        public bool DraftInward1 { get; set; }
        public bool DraftInward2 { get; set; }

        public bool Merge { get; set; } = true;

        /// <summary>swStartConditions_e.</summary>
        public int StartCondition { get; set; }

        public double StartOffset { get; set; }
        public bool FlipStartOffset { get; set; }

        /// <summary>
        /// Signatures of faces or planes referenced by up-to-surface / offset-from-surface
        /// end conditions. Empty for blind and through-all extrudes, which is why those are
        /// the v0.1 target: nothing to re-resolve.
        /// </summary>
        public List<FaceSignature> EndReferences { get; } = new List<FaceSignature>();

        public bool NeedsEntityResolution => EndReferences.Count > 0;

        public override CapturedFeature Mirrored(MirrorPlane plane)
        {
            var m = new ExtrudeFeature
            {
                Index = Index,
                Name = Name,
                TypeName = TypeName,
                IsSuppressed = IsSuppressed,
                ProfileSketchName = ProfileSketchName,
                IsCut = IsCut,
                SingleEnded = SingleEnded,

                // Unchanged. See class remarks - the sketch frame already carries the flip.
                ReverseDirection = ReverseDirection,

                EndCondition1 = EndCondition1,
                EndCondition2 = EndCondition2,

                // Magnitudes. Never negate these.
                Depth1 = Depth1,
                Depth2 = Depth2,

                Draft1Enabled = Draft1Enabled,
                Draft2Enabled = Draft2Enabled,

                // Signed in-plane angles. These DO invert.
                DraftAngle1 = MirrorTransform.MirrorSignedAngle(DraftAngle1),
                DraftAngle2 = MirrorTransform.MirrorSignedAngle(DraftAngle2),

                DraftInward1 = DraftInward1,
                DraftInward2 = DraftInward2,

                Merge = Merge,
                StartCondition = StartCondition,
                StartOffset = StartOffset,
                FlipStartOffset = FlipStartOffset
            };

            foreach (var r in EndReferences)
            {
                m.EndReferences.Add(r.Reflected(plane));
            }

            return m;
        }
    }

    /// <summary>Revolved boss or cut.</summary>
    public sealed class RevolveFeature : CapturedFeature
    {
        public string ProfileSketchName { get; set; }

        public bool IsCut { get; set; }

        /// <summary>Axis start point in model space.</summary>
        public Vec3 AxisPoint { get; set; }

        /// <summary>Axis direction in model space.</summary>
        public Vec3 AxisDirection { get; set; }

        /// <summary>Revolve angle in radians, direction 1.</summary>
        public double Angle1 { get; set; }

        public double Angle2 { get; set; }

        public bool SingleDirection { get; set; } = true;

        public bool ReverseDirection { get; set; }

        public bool Merge { get; set; } = true;

        /// <summary>
        /// The axis is a model-space line, so it reflects as a point plus a direction.
        ///
        /// The revolve ANGLE is a magnitude, not a signed quantity, so it is not negated.
        /// The sense of rotation is determined by the axis direction, which has already
        /// been reflected - reflecting the axis and negating the angle would double-count
        /// and rotate the wrong way.
        /// </summary>
        public override CapturedFeature Mirrored(MirrorPlane plane) => new RevolveFeature
        {
            Index = Index,
            Name = Name,
            TypeName = TypeName,
            IsSuppressed = IsSuppressed,
            ProfileSketchName = ProfileSketchName,
            IsCut = IsCut,
            AxisPoint = plane.ReflectPoint(AxisPoint),
            AxisDirection = plane.ReflectDirection(AxisDirection),
            Angle1 = Angle1,
            Angle2 = Angle2,
            SingleDirection = SingleDirection,
            ReverseDirection = ReverseDirection,
            Merge = Merge
        };
    }

    /// <summary>Constant-radius fillet or chamfer. The v0.2 workhorse.</summary>
    public sealed class FilletFeature : CapturedFeature
    {
        public bool IsChamfer { get; set; }

        /// <summary>Radius, or chamfer distance, in metres. A magnitude.</summary>
        public double Radius { get; set; }

        /// <summary>Chamfer angle in radians. Only meaningful when IsChamfer.</summary>
        public double ChamferAngle { get; set; }

        public bool PropagateToTangentFaces { get; set; } = true;

        /// <summary>
        /// The edges or faces this applies to. These are exactly the references that cannot
        /// be carried across documents and must be re-resolved geometrically.
        /// </summary>
        public List<FaceSignature> References { get; } = new List<FaceSignature>();

        public override CapturedFeature Mirrored(MirrorPlane plane)
        {
            var m = new FilletFeature
            {
                Index = Index,
                Name = Name,
                TypeName = TypeName,
                IsSuppressed = IsSuppressed,
                IsChamfer = IsChamfer,
                Radius = Radius,
                ChamferAngle = ChamferAngle,
                PropagateToTangentFaces = PropagateToTangentFaces
            };

            foreach (var r in References)
            {
                m.References.Add(r.Reflected(plane));
            }

            return m;
        }
    }

    /// <summary>A sketch node in the feature tree.</summary>
    public sealed class SketchFeature : CapturedFeature
    {
        public CapturedSketch Sketch { get; set; }

        public override CapturedFeature Mirrored(MirrorPlane plane)
        {
            var mirrored = new CapturedSketch
            {
                Name = Sketch.Name,
                Frame = MirrorTransform.MirrorFrame(Sketch.Frame, plane),
                IsFullyDefined = Sketch.IsFullyDefined,
                Is3D = Sketch.Is3D
            };

            foreach (var e in Sketch.Entities)
            {
                mirrored.Entities.Add(e.Mirrored());
            }

            // Relations are index-based and reflection-invariant in type: a coincident stays
            // coincident, a tangent stays tangent. Horizontal/vertical survive because the
            // mirrored frame's axes are the reflected axes (with y negated), so in-plane
            // axis alignment is preserved.
            foreach (var r in Sketch.Relations)
            {
                var copy = new CapturedRelation
                {
                    ConstraintType = r.ConstraintType,
                    TypeName = r.TypeName
                };
                copy.EntityIndices.AddRange(r.EntityIndices);
                mirrored.Relations.Add(copy);
            }

            return new SketchFeature
            {
                Index = Index,
                Name = Name,
                TypeName = TypeName,
                IsSuppressed = IsSuppressed,
                Sketch = mirrored
            };
        }
    }

    /// <summary>
    /// A feature the transcriber cannot rebuild natively. Kept in the plan so the fidelity
    /// report can name it and the fallback can trigger at the right point, rather than the
    /// feature vanishing silently.
    /// </summary>
    public sealed class UnsupportedFeature : CapturedFeature
    {
        public string Reason { get; set; }

        public override CapturedFeature Mirrored(MirrorPlane plane) => new UnsupportedFeature
        {
            Index = Index,
            Name = Name,
            TypeName = TypeName,
            IsSuppressed = IsSuppressed,
            Reason = Reason
        };
    }
}
