using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using TrueMirror.Core.Geometry;

namespace TrueMirror.Core.Interop
{
    /// <summary>
    /// Finds or creates a plane in the target document matching a mirrored sketch frame.
    ///
    /// This is where the v0.1 scope boundary actually bites, so it is explicit rather than
    /// implicit. A sketch on Front/Top/Right mirrors onto a standard plane (possibly a
    /// different one), which we can select by name. A sketch on an arbitrary reference plane
    /// or a model face needs a constructed plane, and constructing an arbitrarily oriented
    /// plane through the API needs reference geometry that does not exist yet in the target.
    ///
    /// Rather than half-solve that, v0.1 reports the case honestly and the transcriber falls
    /// back. v0.2 resolves it by creating reference geometry alongside the transcription.
    /// </summary>
    public sealed class PlaneResolver
    {
        private const double AngularTolerance = 1e-6;
        private const double LinearTolerance = 1e-9;

        private readonly IModelDoc2 m_Target;

        public PlaneResolver(IModelDoc2 target)
        {
            m_Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <summary>
        /// The three default planes and their frames in a fresh part document.
        /// Axis conventions match SOLIDWORKS: Front is XY (normal +Z), Top is XZ
        /// (normal -Y in right-handed terms, expressed here as x cross y), Right is YZ.
        /// </summary>
        private static readonly (string Name, Vec3 X, Vec3 Y)[] StandardPlanes =
        {
            ("Front Plane", new Vec3(1, 0, 0), new Vec3(0, 1, 0)),
            ("Top Plane",   new Vec3(1, 0, 0), new Vec3(0, 0, -1)),
            ("Right Plane", new Vec3(0, 0, -1), new Vec3(0, 1, 0))
        };

        /// <summary>
        /// Selects a plane matching <paramref name="frame"/>, returning its name.
        /// Returns null when no standard plane matches, which is a fallback trigger,
        /// not an error.
        /// </summary>
        public string TrySelectPlaneFor(SketchFrame frame, out string reason)
        {
            reason = null;

            if (frame == null)
            {
                reason = "no sketch frame";
                return null;
            }

            var normal = frame.Normal;

            foreach (var (name, x, y) in StandardPlanes)
            {
                var candidateNormal = x.Cross(y);

                // Co-planar is enough: a sketch can sit on a plane with either normal sense
                // and any in-plane rotation. Geometry is written in model coordinates, so
                // only the plane itself has to match, not the axis alignment.
                var aligned = Math.Abs(Math.Abs(candidateNormal.Dot(normal)) - 1.0) <= AngularTolerance;
                var throughOrigin = Math.Abs(frame.Origin.Dot(candidateNormal)) <= LinearTolerance;

                if (aligned && throughOrigin)
                {
                    var ext = m_Target.Extension;
                    if (ext.SelectByID2(name, SelectTypes.Plane, 0, 0, 0, false,
                            SelectionMarks.None, null, 0))
                    {
                        return name;
                    }

                    reason = $"'{name}' matched geometrically but could not be selected";
                    return null;
                }
            }

            reason = $"sketch plane (normal {normal}, origin {frame.Origin}) is not one of " +
                     "the three standard planes - out of scope for v0.1";
            return null;
        }

        /// <summary>
        /// Names of the default planes, used when clearing or diagnosing a fresh document.
        /// </summary>
        public static IReadOnlyList<string> StandardPlaneNames()
        {
            var names = new List<string>();
            foreach (var (name, _, _) in StandardPlanes) names.Add(name);
            return names;
        }
    }
}
