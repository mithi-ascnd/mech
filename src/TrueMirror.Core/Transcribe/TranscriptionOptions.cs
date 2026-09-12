namespace TrueMirror.Core.Transcribe
{
    public sealed class TranscriptionOptions
    {
        /// <summary>
        /// Thread handedness is an engineering decision, not a geometric one.
        ///
        /// Reflecting a right-hand thread yields a left-hand thread. That is geometrically
        /// faithful and almost always wrong for the engineer: an opposite-hand bracket still
        /// takes right-hand fasteners. Default to preserving, and say so in the report.
        /// </summary>
        public bool PreserveThreadHandedness { get; set; } = true;

        /// <summary>
        /// When a feature cannot be rebuilt natively, insert the mirrored body as it stands
        /// and carry on from the next feature. The result is a hybrid tree - parametric up
        /// to the failure, imported after it - which is still strictly better than the
        /// single imported body SOLIDWORKS produces today.
        ///
        /// Turn off to stop at the first failure instead, which is what you want while
        /// developing a new transcriber.
        /// </summary>
        public bool FallBackToImportedBody { get; set; } = true;

        /// <summary>
        /// Rebuild and check for errors after every feature rather than once at the end.
        /// Costs time on large parts; without it a failure cannot be attributed to a feature.
        /// </summary>
        public bool VerifyAfterEachFeature { get; set; } = true;

        /// <summary>Linear tolerance for geometric entity matching, in metres.</summary>
        public double ResolverLinearTolerance { get; set; } = 1e-7;

        /// <summary>Relative tolerance for areas, lengths and radii.</summary>
        public double ResolverRelativeTolerance { get; set; } = 1e-6;

        /// <summary>
        /// Compare the result's mass properties against SOLIDWORKS' own Mirror Part output.
        /// SOLIDWORKS ships the reference implementation of the geometry we are trying to
        /// match, which makes this the strongest available oracle and it costs almost nothing.
        /// </summary>
        public bool ValidateMassProperties { get; set; } = true;

        /// <summary>Relative tolerance for the volume and area comparison.</summary>
        public double MassPropertyRelativeTolerance { get; set; } = 1e-9;
    }
}
