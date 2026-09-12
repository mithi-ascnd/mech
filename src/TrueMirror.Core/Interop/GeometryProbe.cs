using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TrueMirror.Core.Geometry;
using TrueMirror.Core.Resolver;

namespace TrueMirror.Core.Interop
{
    /// <summary>
    /// Builds mirror-invariant <see cref="FaceSignature"/>s from live SOLIDWORKS geometry.
    ///
    /// Why bounding-box centres rather than true centroids: IFace2 exposes no centroid, and
    /// the bounding box IS mirror-covariant - reflecting a face reflects its box, so the box
    /// centre reflects with it. A true area centroid would be marginally more discriminating
    /// but needs surface integration for no practical gain here, because Kind + area + radius
    /// already separate almost everything that shares a box centre.
    ///
    /// Every probe is wrapped: a face whose surface cannot be classified degrades to
    /// SurfaceKind.Unknown and simply fails to match, which triggers fallback rather than a
    /// crash or a wrong match.
    /// </summary>
    public sealed class GeometryProbe
    {
        /// <summary>Signatures for every face of every solid body in the document.</summary>
        public IReadOnlyList<FaceSignature> ProbeFaces(IPartDoc part)
        {
            var signatures = new List<FaceSignature>();

            foreach (var body in GetSolidBodies(part))
            {
                if (!(body.GetFaces() is object[] faces)) continue;

                foreach (var obj in faces)
                {
                    if (obj is IFace2 face)
                    {
                        var sig = Describe(face);
                        if (sig != null) signatures.Add(sig);
                    }
                }
            }

            return signatures;
        }

        /// <summary>Signatures for every edge of every solid body in the document.</summary>
        public IReadOnlyList<FaceSignature> ProbeEdges(IPartDoc part)
        {
            var signatures = new List<FaceSignature>();

            foreach (var body in GetSolidBodies(part))
            {
                if (!(body.GetEdges() is object[] edges)) continue;

                foreach (var obj in edges)
                {
                    if (obj is IEdge edge)
                    {
                        var sig = Describe(edge);
                        if (sig != null) signatures.Add(sig);
                    }
                }
            }

            return signatures;
        }

        public IEnumerable<IBody2> GetSolidBodies(IPartDoc part)
        {
            if (part == null) yield break;

            object[] bodies;
            try
            {
                bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            }
            catch
            {
                yield break;
            }

            if (bodies == null) yield break;

            foreach (var obj in bodies)
            {
                if (obj is IBody2 body) yield return body;
            }
        }

        public FaceSignature Describe(IFace2 face)
        {
            try
            {
                var kind = SurfaceKind.Unknown;
                var radius = 0.0;

                if (face.GetSurface() is ISurface surface)
                {
                    if (surface.IsPlane())
                    {
                        kind = SurfaceKind.Plane;
                    }
                    else if (surface.IsCylinder())
                    {
                        kind = SurfaceKind.Cylinder;
                        // CylinderParams: origin xyz, axis xyz, radius.
                        radius = ParamAt(surface.CylinderParams, 6);
                    }
                    else if (surface.IsCone())
                    {
                        kind = SurfaceKind.Cone;
                        radius = ParamAt(surface.ConeParams, 6);
                    }
                    else if (surface.IsSphere())
                    {
                        kind = SurfaceKind.Sphere;
                        // SphereParams: centre xyz, radius.
                        radius = ParamAt(surface.SphereParams, 3);
                    }
                    else if (surface.IsTorus())
                    {
                        kind = SurfaceKind.Torus;
                    }
                    else
                    {
                        kind = SurfaceKind.Bspline;
                    }
                }

                return new FaceSignature
                {
                    Kind = kind,
                    Centroid = BoxCentre(face.GetBox() as double[]),
                    Measure = face.GetArea(),
                    Radius = radius,
                    Tag = face
                };
            }
            catch
            {
                return null;
            }
        }

        public FaceSignature Describe(IEdge edge)
        {
            try
            {
                var kind = SurfaceKind.Unknown;
                var radius = 0.0;

                if (edge.GetCurve() is ICurve curve)
                {
                    if (curve.IsLine())
                    {
                        kind = SurfaceKind.Plane;      // a straight edge
                    }
                    else if (curve.IsCircle())
                    {
                        kind = SurfaceKind.Cylinder;   // a circular edge
                        // CircleParams: centre xyz, axis xyz, radius.
                        radius = ParamAt(curve.CircleParams, 6);
                    }
                    else
                    {
                        kind = SurfaceKind.Bspline;
                    }
                }

                return new FaceSignature
                {
                    Kind = kind,
                    Centroid = EdgeMidpoint(edge),
                    Measure = EdgeLength(edge),
                    Radius = radius,
                    Tag = edge
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Midpoint of an edge's two vertices. Mirror-covariant, and cheap.
        ///
        /// A closed circular edge has no distinct start and end vertex; those return null,
        /// and we fall back to the curve's circle centre, which is equally covariant.
        /// </summary>
        private static Vec3 EdgeMidpoint(IEdge edge)
        {
            var start = VertexPoint(edge.GetStartVertex());
            var end = VertexPoint(edge.GetEndVertex());

            if (start.HasValue && end.HasValue)
            {
                return (start.Value + end.Value) * 0.5;
            }

            if (edge.GetCurve() is ICurve curve && curve.IsCircle())
            {
                var p = curve.CircleParams as double[];
                if (p != null && p.Length >= 3)
                {
                    return new Vec3(p[0], p[1], p[2]);
                }
            }

            return Vec3.Zero;
        }

        /// <summary>
        /// IEdge exposes no length member (verified by reflecting the interop assembly).
        /// Length comes from the underlying curve: ask it for its parameter range, then
        /// for the arc length over that range.
        /// </summary>
        private static double EdgeLength(IEdge edge)
        {
            try
            {
                if (!(edge.GetCurve() is ICurve curve)) return 0.0;

                if (!curve.GetEndParams(out var start, out var end, out _, out _))
                {
                    return 0.0;
                }

                return curve.GetLength3(start, end);
            }
            catch
            {
                return 0.0;
            }
        }

        private static Vec3? VertexPoint(object vertexObj)
        {
            if (!(vertexObj is IVertex vertex)) return null;

            if (vertex.GetPoint() is double[] p && p.Length >= 3)
            {
                return new Vec3(p[0], p[1], p[2]);
            }

            return null;
        }

        /// <summary>
        /// Centre of the axis-aligned bounding box returned as [xMin, yMin, zMin, xMax, yMax, zMax].
        /// </summary>
        private static Vec3 BoxCentre(double[] box)
        {
            if (box == null || box.Length < 6) return Vec3.Zero;

            return new Vec3(
                (box[0] + box[3]) * 0.5,
                (box[1] + box[4]) * 0.5,
                (box[2] + box[5]) * 0.5);
        }

        private static double ParamAt(object paramsObj, int index)
        {
            return paramsObj is double[] p && p.Length > index ? p[index] : 0.0;
        }
    }
}
