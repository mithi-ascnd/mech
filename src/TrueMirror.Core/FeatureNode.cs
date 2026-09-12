using System.Collections.Generic;

namespace TrueMirror.Core
{
    /// <summary>
    /// A single node in a captured SOLIDWORKS feature tree.
    /// Deliberately plain data: no SOLIDWORKS interop types leak out of the reader,
    /// so the report writer and the future transcriber can be unit tested without SOLIDWORKS.
    /// </summary>
    public sealed class FeatureNode
    {
        /// <summary>1-based position in a depth-first walk of the tree.</summary>
        public int Index { get; set; }

        /// <summary>Nesting depth. 0 for top-level features, 1+ for sub-features.</summary>
        public int Depth { get; set; }

        /// <summary>Feature name as shown in the FeatureManager, e.g. "Boss-Extrude1".</summary>
        public string Name { get; set; }

        /// <summary>
        /// Value of IFeature::GetTypeName2, e.g. "Extrusion", "Cut", "ProfileFeature".
        /// This is the key the transcriber dispatches on, so capturing it accurately is
        /// the whole point of M0.
        /// </summary>
        public string TypeName { get; set; }

        public bool IsSuppressed { get; set; }

        /// <summary>Whether IFeature::GetDefinition returned a non-null feature data object.</summary>
        public bool HasDefinition { get; set; }

        /// <summary>CLR type name of the object returned by GetDefinition, when there was one.</summary>
        public string DefinitionType { get; set; }

        /// <summary>Populated only when this node is a sketch.</summary>
        public SketchInfo Sketch { get; set; }

        /// <summary>Anything that threw while probing this feature. Never fatal.</summary>
        public List<string> Warnings { get; } = new List<string>();

        public SupportLevel Support { get; set; }
    }

    public sealed class SketchInfo
    {
        public int SegmentCount { get; set; }
        public int PointCount { get; set; }
        public int RelationCount { get; set; }

        /// <summary>Count of sketch segments by swSketchSegments_e name, e.g. {"Line": 4, "Arc": 2}.</summary>
        public Dictionary<string, int> SegmentsByType { get; } = new Dictionary<string, int>();

        /// <summary>
        /// True when the sketch is fully constrained. Under-defined source sketches are a
        /// known hazard for the transcriber: mirroring them faithfully reproduces the
        /// ambiguity rather than resolving it.
        /// </summary>
        public bool? IsFullyDefined { get; set; }
    }

    public enum SupportLevel
    {
        /// <summary>GetTypeName2 returned a string this build has never seen. Extend the map.</summary>
        Unknown = 0,

        /// <summary>Targeted by spec phase v0.1 - no entity references to re-resolve.</summary>
        PhaseV01,

        /// <summary>Targeted by v0.2 - needs the geometric entity resolver.</summary>
        PhaseV02,

        /// <summary>Targeted by v0.3.</summary>
        PhaseV03,

        /// <summary>Out of scope for v1. The transcriber will fall back to an imported body.</summary>
        OutOfScope,

        /// <summary>Reference geometry and other nodes that need no transcription work.</summary>
        Trivial
    }
}
