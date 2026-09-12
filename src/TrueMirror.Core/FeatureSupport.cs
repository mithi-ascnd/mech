using System;
using System.Collections.Generic;

namespace TrueMirror.Core
{
    /// <summary>
    /// Maps IFeature::GetTypeName2 strings onto the spec's phase plan.
    ///
    /// IMPORTANT: this map is a starting hypothesis, not ground truth. SOLIDWORKS type-name
    /// strings are poorly documented and vary by feature flavour (a cut-revolve and a
    /// boss-revolve may or may not share "Revolution"; a mirror feature reports differently
    /// depending on whether it mirrors bodies or features).
    ///
    /// Completing this map is the actual deliverable of M0. Run the dump over the corpus,
    /// collect every type reported as <see cref="SupportLevel.Unknown"/>, and classify it here.
    /// Do not guess entries in advance - a wrong entry silently mis-scopes the project.
    /// </summary>
    public static class FeatureSupport
    {
        private static readonly Dictionary<string, SupportLevel> Map =
            new Dictionary<string, SupportLevel>(StringComparer.OrdinalIgnoreCase)
        {
            // --- v0.1: sketch-based, no topological references -------------------
            { "ProfileFeature", SupportLevel.PhaseV01 },  // a sketch
            { "Extrusion",      SupportLevel.PhaseV01 },  // boss-extrude
            { "Cut",            SupportLevel.PhaseV01 },  // cut-extrude
            { "Revolution",     SupportLevel.PhaseV01 },

            // --- v0.2: needs the geometric entity resolver -----------------------
            { "Fillet",         SupportLevel.PhaseV02 },
            { "Chamfer",        SupportLevel.PhaseV02 },

            // --- v0.3: breadth ---------------------------------------------------
            { "LPattern",       SupportLevel.PhaseV03 },  // linear pattern
            { "CirPattern",     SupportLevel.PhaseV03 },  // circular pattern
            { "MirrorPattern",  SupportLevel.PhaseV03 },
            { "Shell",          SupportLevel.PhaseV03 },
            { "Draft",          SupportLevel.PhaseV03 },
            { "HoleWzd",        SupportLevel.PhaseV03 },  // Hole Wizard

            // --- explicitly out of scope for v1 ----------------------------------
            { "Sweep",          SupportLevel.OutOfScope },
            { "Loft",           SupportLevel.OutOfScope },
            { "SweepCut",       SupportLevel.OutOfScope },
            { "LoftCut",        SupportLevel.OutOfScope },
            { "SurfaceCut",     SupportLevel.OutOfScope },

            // --- present in every tree, nothing to transcribe --------------------
            { "RefPlane",       SupportLevel.Trivial },
            { "OriginProfileFeature", SupportLevel.Trivial },
            { "CoordSys",       SupportLevel.Trivial },
            { "RefAxis",        SupportLevel.Trivial },
            { "MaterialFolder", SupportLevel.Trivial },
            { "HistoryFolder",  SupportLevel.Trivial },
            { "SensorFolder",   SupportLevel.Trivial },
            { "DetailCabinet",  SupportLevel.Trivial },
            { "SolidBodyFolder",   SupportLevel.Trivial },
            { "SurfaceBodyFolder", SupportLevel.Trivial },
            { "EnvFolder",      SupportLevel.Trivial },
            { "LightsFolder",   SupportLevel.Trivial },
            { "FavoriteFolder", SupportLevel.Trivial },
            { "EqnFolder",      SupportLevel.Trivial },
            { "Annotations",    SupportLevel.Trivial },
            { "Comments",       SupportLevel.Trivial },
        };

        public static SupportLevel Classify(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return SupportLevel.Unknown;
            }

            return Map.TryGetValue(typeName, out var level) ? level : SupportLevel.Unknown;
        }

        /// <summary>
        /// Features that count toward the spec's headline "percent rebuilt natively" metric.
        /// Trivial nodes are excluded: crediting the origin sketch and three default planes
        /// would inflate the number without meaning anything.
        /// </summary>
        public static bool CountsTowardCoverage(SupportLevel level)
            => level != SupportLevel.Trivial;
    }
}
