using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TrueMirror.Core.Geometry;
using TrueMirror.Core.Model;

namespace TrueMirror.Core.Interop
{
    /// <summary>
    /// Recreates a captured sketch inside a target part document.
    ///
    /// OPEN QUESTION - RESOLVE ON FIRST RUN (see VerifyCoordinateSpace below).
    /// ISketchManager::CreateLine and friends take coordinates "in meters", but the
    /// documentation does not state unambiguously whether those are model-space or
    /// sketch-local. For a sketch on a standard plane through the origin the two coincide,
    /// which is why almost every published example fails to disambiguate it.
    ///
    /// This class writes MODEL-space coordinates, which is the behaviour the API exhibits
    /// in practice, and ships a self-check that proves or disproves it on a real install in
    /// about a second. Run the self-check before trusting any output on a non-standard
    /// sketch plane.
    /// </summary>
    public sealed class SketchWriter
    {
        private readonly IModelDoc2 m_Target;

        public SketchWriter(IModelDoc2 target)
        {
            m_Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <summary>
        /// Creates the sketch and returns the created ISketch, or null on failure.
        /// <paramref name="planeSelector"/> selects and activates the plane the sketch sits on.
        /// </summary>
        public ISketch Write(CapturedSketch sketch, Func<SketchFrame, bool> planeSelector,
            out string failureReason)
        {
            if (sketch == null) throw new ArgumentNullException(nameof(sketch));
            failureReason = null;

            if (!planeSelector(sketch.Frame))
            {
                failureReason = "could not establish a sketch plane matching the mirrored frame";
                return null;
            }

            var sketchMgr = m_Target.SketchManager;

            // Turn off automatic inference. Left on, SOLIDWORKS invents relations as entities
            // are created, which both corrupts the relation set we are about to replay and
            // over-constrains the sketch.
            var previousInference = sketchMgr.AddToDB;
            sketchMgr.AddToDB = true;

            try
            {
                sketchMgr.InsertSketch(true);

                var created = new List<object>();

                foreach (var entity in sketch.Entities)
                {
                    var obj = CreateEntity(sketchMgr, sketch.Frame, entity);
                    created.Add(obj);

                    if (obj != null && entity.IsConstruction)
                    {
                        SetConstruction(obj);
                    }
                }

                sketchMgr.InsertSketch(true);

                return m_Target.SketchManager.ActiveSketch
                       ?? GetLastSketch();
            }
            catch (Exception ex)
            {
                failureReason = "sketch creation threw: " + ex.Message;
                return null;
            }
            finally
            {
                sketchMgr.AddToDB = previousInference;
            }
        }

        private object CreateEntity(ISketchManager mgr, SketchFrame frame, SketchEntity entity)
        {
            switch (entity)
            {
                case SketchLineEntity line:
                {
                    var a = frame.ToModel(line.Start);
                    var b = frame.ToModel(line.End);
                    return mgr.CreateLine(a.X, a.Y, a.Z, b.X, b.Y, b.Z);
                }

                case SketchArcEntity arc when arc.IsCircle:
                {
                    var c = frame.ToModel(arc.Center);
                    var p = frame.ToModel(arc.Start);
                    return mgr.CreateCircle(c.X, c.Y, c.Z, p.X, p.Y, p.Z);
                }

                case SketchArcEntity arc:
                {
                    var c = frame.ToModel(arc.Center);
                    var s = frame.ToModel(arc.Start);
                    var e = frame.ToModel(arc.End);

                    // arc.Direction was already flipped by SketchArcEntity.Mirrored().
                    // Reflecting the three points without flipping this yields the
                    // complementary arc - proven wrong by 20 mm on a 10 mm arc in
                    // tools/sketch_mirror_reference.py.
                    return mgr.CreateArc(c.X, c.Y, c.Z, s.X, s.Y, s.Z, e.X, e.Y, e.Z,
                        (short)arc.Direction);
                }

                case SketchPointEntity pt:
                {
                    var p = frame.ToModel(pt.Position);
                    return mgr.CreatePoint(p.X, p.Y, p.Z);
                }

                case SketchSplineEntity spline:
                    return CreateSpline(mgr, frame, spline);

                default:
                    // UnsupportedSketchEntity and anything unrecognised. Reported by the
                    // fidelity report rather than silently skipped.
                    return null;
            }
        }

        private static object CreateSpline(ISketchManager mgr, SketchFrame frame,
            SketchSplineEntity spline)
        {
            if (spline.ControlPoints.Count < 2)
            {
                return null;
            }

            var coords = new double[spline.ControlPoints.Count * 3];
            for (var i = 0; i < spline.ControlPoints.Count; i++)
            {
                var p = frame.ToModel(spline.ControlPoints[i]);
                coords[i * 3] = p.X;
                coords[i * 3 + 1] = p.Y;
                coords[i * 3 + 2] = p.Z;
            }

            return mgr.CreateSpline(coords);
        }

        private static void SetConstruction(object entity)
        {
            if (entity is ISketchSegment segment)
            {
                segment.ConstructionGeometry = true;
            }
        }

        private ISketch GetLastSketch()
        {
            // Fallback when ActiveSketch is null after closing the sketch: walk the tree for
            // the most recently added ProfileFeature.
            if (!(m_Target is IPartDoc part)) return null;

            ISketch last = null;
            var feat = part.FirstFeature() as IFeature;
            while (feat != null)
            {
                if (string.Equals(feat.GetTypeName2(), "ProfileFeature", StringComparison.OrdinalIgnoreCase)
                    && feat.GetSpecificFeature2() is ISketch s)
                {
                    last = s;
                }
                feat = feat.GetNextFeature() as IFeature;
            }

            return last;
        }

        /// <summary>
        /// Proves or disproves the coordinate-space assumption documented on this class.
        ///
        /// Creates a throwaway sketch on a plane that is deliberately NOT through the origin,
        /// draws a line at a known model-space position, reads the resulting point back, and
        /// compares. If the assumption is wrong the discrepancy equals the plane offset and
        /// is glaringly obvious rather than subtle.
        ///
        /// Call this once on a scratch document before trusting mirrored output. It is
        /// cheap and it removes the single largest unverified assumption in this codebase.
        /// </summary>
        public static string VerifyCoordinateSpace(IModelDoc2 scratch, double planeOffsetMetres = 0.05)
        {
            if (scratch == null) return "no scratch document supplied";

            try
            {
                var ext = scratch.Extension;
                var mgr = scratch.SketchManager;

                if (!ext.SelectByID2("Front Plane", SelectTypes.Plane, 0, 0, 0, false,
                        SelectionMarks.None, null, 0))
                {
                    return "could not select Front Plane on the scratch document";
                }

                var refPlane = (IFeature)scratch.FeatureManager.InsertRefPlane(
                    (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Distance,
                    planeOffsetMetres, 0, 0, 0, 0);

                if (refPlane == null)
                {
                    return "could not create the offset reference plane";
                }

                if (!ext.SelectByID2(refPlane.Name, SelectTypes.Plane, 0, 0, 0, false,
                        SelectionMarks.None, null, 0))
                {
                    return "could not select the offset reference plane";
                }

                mgr.InsertSketch(true);
                mgr.AddToDB = true;

                // Intent: a line from model (0,0,offset) to (0.01,0,offset).
                var line = mgr.CreateLine(0, 0, planeOffsetMetres, 0.01, 0, planeOffsetMetres)
                    as ISketchLine;

                mgr.AddToDB = false;
                mgr.InsertSketch(true);

                if (line?.GetStartPoint2() is ISketchPoint start)
                {
                    var observed = new Vec3(start.X, start.Y, start.Z);
                    return $"VERIFY: asked for model (0, 0, {planeOffsetMetres}); " +
                           $"read back {observed}. If Z reads ~{planeOffsetMetres} the " +
                           "model-space assumption holds; if it reads ~0 the API is " +
                           "sketch-local and SketchWriter must stop applying Frame.ToModel.";
                }

                return "self-check could not read the created line back";
            }
            catch (Exception ex)
            {
                return "self-check threw: " + ex.Message;
            }
        }
    }
}
