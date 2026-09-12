namespace TrueMirror.Core.Interop
{
    /// <summary>
    /// SOLIDWORKS selection marks.
    ///
    /// Feature creation calls do not take their references as arguments. They read whatever
    /// is in the selection list, and distinguish the roles of those selections by an integer
    /// "mark" supplied to IModelDocExtension::SelectByID2. Get a mark wrong and the feature
    /// is created with the reference in the wrong slot - usually a rebuild error, sometimes
    /// a quietly wrong feature.
    ///
    /// Values below are transcribed from the Remarks table of the FeatureExtrusion3
    /// documentation (SOLIDWORKS API Help 2024).
    /// </summary>
    public static class SelectionMarks
    {
        /// <summary>The profile sketch being extruded.</summary>
        public const int Profile = 0;

        /// <summary>End-condition reference entity (up-to-surface, up-to-face).</summary>
        public const int EndConditionReference = 1;

        /// <summary>Bodies the feature affects, when UseAutoSelect is false.</summary>
        public const int FeatureScopeBody = 8;

        /// <summary>Edge defining the extrusion direction.</summary>
        public const int DirectionEdge = 16;

        /// <summary>Start-condition reference entity.</summary>
        public const int StartConditionReference = 32;

        /// <summary>Plain selection with no special role, e.g. fillet edges.</summary>
        public const int None = 0;
    }

    /// <summary>
    /// String type names accepted by IModelDocExtension::SelectByID2.
    /// </summary>
    public static class SelectTypes
    {
        public const string Plane = "PLANE";
        public const string Face = "FACE";
        public const string Edge = "EDGE";
        public const string Vertex = "VERTEX";
        public const string Sketch = "SKETCH";
        public const string SolidBody = "SOLIDBODY";
        public const string Axis = "AXIS";
        public const string ExternalSketchSegment = "EXTSKETCHSEGMENT";
    }
}
