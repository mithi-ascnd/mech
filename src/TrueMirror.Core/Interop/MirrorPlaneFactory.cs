using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TrueMirror.Core.Geometry;

namespace TrueMirror.Core.Interop
{
    /// <summary>
    /// Builds a <see cref="MirrorPlane"/> from whatever the user has selected.
    ///
    /// Accepts a planar model face or a reference plane, matching what
    /// Insert &gt; Mirror Part accepts, so the command feels native.
    /// </summary>
    public static class MirrorPlaneFactory
    {
        public static MirrorPlane FromSelection(IModelDoc2 doc, out string error)
        {
            error = null;

            if (doc == null)
            {
                error = "no document";
                return null;
            }

            var selMgr = doc.SelectionManager as ISelectionMgr;

            if (selMgr == null || selMgr.GetSelectedObjectCount2(-1) == 0)
            {
                error = "select a planar face or a plane to mirror about, then run the command";
                return null;
            }

            if (selMgr.GetSelectedObjectCount2(-1) > 1)
            {
                error = "select exactly one face or plane";
                return null;
            }

            var type = (swSelectType_e)selMgr.GetSelectedObjectType3(1, -1);
            var selected = selMgr.GetSelectedObject6(1, -1);

            switch (type)
            {
                case swSelectType_e.swSelFACES:
                    return FromFace(selected as IFace2, out error);

                case swSelectType_e.swSelDATUMPLANES:
                    return FromRefPlane(selected as IRefPlane, out error);

                default:
                    error = $"selection is a {type}; mirror about a planar face or a plane";
                    return null;
            }
        }

        private static MirrorPlane FromFace(IFace2 face, out string error)
        {
            error = null;

            if (face == null)
            {
                error = "the selected face could not be read";
                return null;
            }

            try
            {
                if (!(face.GetSurface() is ISurface surface) || !surface.IsPlane())
                {
                    error = "the selected face is not planar";
                    return null;
                }

                // PlaneParams: normal xyz followed by a root point xyz on the plane.
                if (!(surface.PlaneParams is double[] p) || p.Length < 6)
                {
                    error = "could not read the plane parameters of the selected face";
                    return null;
                }

                var normal = new Vec3(p[0], p[1], p[2]);
                var point = new Vec3(p[3], p[4], p[5]);

                if (normal.Length < 1e-12)
                {
                    error = "the selected face reported a degenerate normal";
                    return null;
                }

                return new MirrorPlane(point, normal);
            }
            catch (Exception ex)
            {
                error = "reading the selected face threw: " + ex.Message;
                return null;
            }
        }

        private static MirrorPlane FromRefPlane(IRefPlane refPlane, out string error)
        {
            error = null;

            if (refPlane == null)
            {
                error = "the selected plane could not be read";
                return null;
            }

            try
            {
                if (!(refPlane.Transform is IMathTransform transform)
                    || !(transform.ArrayData is double[] t) || t.Length < 13)
                {
                    error = "could not read the transform of the selected plane";
                    return null;
                }

                // A reference plane's local Z axis is its normal; the translation is a point
                // on it. Rotation is stored column-major, so elements 6..8 are the Z axis.
                var normal = new Vec3(t[6], t[7], t[8]);
                var point = new Vec3(t[9], t[10], t[11]);

                if (normal.Length < 1e-12)
                {
                    error = "the selected plane reported a degenerate normal";
                    return null;
                }

                return new MirrorPlane(point, normal);
            }
            catch (Exception ex)
            {
                error = "reading the selected plane threw: " + ex.Message;
                return null;
            }
        }
    }
}
