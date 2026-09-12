using System;
using SolidWorks.Interop.sldworks;
using TrueMirror.Core.Geometry;
using TrueMirror.Core.Report;

namespace TrueMirror.Core.Interop
{
    /// <summary>
    /// Compares the transcribed mirror against the source part.
    ///
    /// This is the strongest oracle available and it is free: a correct mirror has exactly
    /// the same volume and surface area as its source, and its centre of mass is the
    /// source's centre of mass reflected about the mirror plane. No reference model needed,
    /// no eyeballing.
    ///
    /// It is a NECESSARY, not sufficient, check. Two different parts can share a volume.
    /// It will not catch a mirrored arc that happens to preserve volume, which is why the
    /// arc direction rule is proven separately in tools/sketch_mirror_reference.py. What it
    /// does catch is the large class of errors that change geometry at all.
    /// </summary>
    public sealed class MassPropertyValidator
    {
        public MassPropertyComparison Compare(IModelDoc2 source, IModelDoc2 mirror, MirrorPlane plane)
        {
            var result = new MassPropertyComparison();

            if (source == null || mirror == null)
            {
                result.Ran = false;
                result.SkipReason = "source or mirror document was not available";
                return result;
            }

            try
            {
                var sourceProps = Measure(source);
                var mirrorProps = Measure(mirror);

                if (sourceProps == null || mirrorProps == null)
                {
                    result.Ran = false;
                    result.SkipReason = "mass properties could not be computed " +
                                        "(a document may contain no solid body)";
                    return result;
                }

                result.Ran = true;
                result.SourceVolume = sourceProps.Value.Volume;
                result.MirrorVolume = mirrorProps.Value.Volume;
                result.SourceArea = sourceProps.Value.Area;
                result.MirrorArea = mirrorProps.Value.Area;

                var expected = plane.ReflectPoint(sourceProps.Value.Centre);
                result.CentreOfMassDelta = (expected - mirrorProps.Value.Centre).Length;

                return result;
            }
            catch (Exception ex)
            {
                result.Ran = false;
                result.SkipReason = "mass-property comparison threw: " + ex.Message;
                return result;
            }
        }

        private readonly struct Props
        {
            public Props(double volume, double area, Vec3 centre)
            {
                Volume = volume;
                Area = area;
                Centre = centre;
            }

            public double Volume { get; }
            public double Area { get; }
            public Vec3 Centre { get; }
        }

        private static Props? Measure(IModelDoc2 doc)
        {
            var massProp = doc.Extension.CreateMassProperty() as IMassProperty;
            if (massProp == null) return null;

            // IMassProperty has no Recalculate member (verified by reflecting the interop
            // assembly). CreateMassProperty returns a freshly evaluated object, so values
            // are current as long as the document was rebuilt first - which the
            // transcriber guarantees via ForceRebuild3 after each feature.

            var centre = massProp.CenterOfMass as double[];

            return new Props(
                massProp.Volume,
                massProp.SurfaceArea,
                centre != null && centre.Length >= 3
                    ? new Vec3(centre[0], centre[1], centre[2])
                    : Vec3.Zero);
        }
    }
}
