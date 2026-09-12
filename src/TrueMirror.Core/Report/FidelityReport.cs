using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TrueMirror.Core.Report
{
    public enum FeatureOutcome
    {
        /// <summary>Rebuilt as a native, editable feature. This is the goal.</summary>
        Native,

        /// <summary>Suppressed in the source, so deliberately not rebuilt.</summary>
        SkippedSuppressed,

        /// <summary>No transcriber for this feature type.</summary>
        FallbackUnsupported,

        /// <summary>An entity reference could not be resolved uniquely.</summary>
        FallbackUnresolved,

        /// <summary>Created, but SOLIDWORKS reported a rebuild error.</summary>
        FallbackRebuildError
    }

    public sealed class FeatureResult
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string TypeName { get; set; }
        public FeatureOutcome Outcome { get; set; }
        public string Detail { get; set; }

        public bool IsFallback =>
            Outcome == FeatureOutcome.FallbackUnsupported ||
            Outcome == FeatureOutcome.FallbackUnresolved ||
            Outcome == FeatureOutcome.FallbackRebuildError;
    }

    public sealed class MassPropertyComparison
    {
        public bool Ran { get; set; }
        public string SkipReason { get; set; }

        public double SourceVolume { get; set; }
        public double MirrorVolume { get; set; }
        public double SourceArea { get; set; }
        public double MirrorArea { get; set; }

        /// <summary>Distance between our centre of mass and the reflected source centre of mass.</summary>
        public double CentreOfMassDelta { get; set; }

        public double VolumeRelativeDelta =>
            RelativeDelta(SourceVolume, MirrorVolume);

        public double AreaRelativeDelta =>
            RelativeDelta(SourceArea, MirrorArea);

        public bool Passed(double relativeTolerance, double linearTolerance)
            => Ran
               && VolumeRelativeDelta <= relativeTolerance
               && AreaRelativeDelta <= relativeTolerance
               && CentreOfMassDelta <= linearTolerance;

        private static double RelativeDelta(double a, double b)
        {
            var scale = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1e-15);
            return Math.Abs(a - b) / scale;
        }
    }

    /// <summary>
    /// The run report. It is the demo, the regression test and the headline metric all at
    /// once, so it must never overstate what happened - a tool that silently produces a
    /// wrong part is worse than one that refuses.
    /// </summary>
    public sealed class FidelityReport
    {
        public string SourceDocument { get; set; }
        public string MirrorDocument { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        public string MirrorPlaneDescription { get; set; }
        public bool ThreadHandednessPreserved { get; set; }

        public List<FeatureResult> Results { get; } = new List<FeatureResult>();

        public MassPropertyComparison MassProperties { get; set; } = new MassPropertyComparison();

        public List<string> Warnings { get; } = new List<string>();

        /// <summary>
        /// True only when an imported body was actually inserted and mirrored.
        ///
        /// This exists so the verdict can never claim geometry that is not there. If the
        /// fallback itself fails, the honest outcome is "parametric up to feature N and
        /// NOTHING after it", which the user must be told plainly rather than discovering
        /// when a hole is missing.
        /// </summary>
        public bool ImportedBodyInserted { get; set; }

        /// <summary>Why the imported-body fallback did not run, when it did not.</summary>
        public string ImportedBodyNote { get; set; }

        public int Total => Results.Count(r => r.Outcome != FeatureOutcome.SkippedSuppressed);
        public int NativeCount => Results.Count(r => r.Outcome == FeatureOutcome.Native);
        public int FallbackCount => Results.Count(r => r.IsFallback);
        public int SuppressedCount => Results.Count(r => r.Outcome == FeatureOutcome.SkippedSuppressed);

        public double NativePercent => Total == 0 ? 0.0 : 100.0 * NativeCount / Total;

        public string Render()
        {
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;

            sb.AppendLine("TrueMirror - fidelity report");
            sb.AppendLine("Source     : " + (SourceDocument ?? "<unknown>"));
            sb.AppendLine("Mirror     : " + (MirrorDocument ?? "<unsaved>"));
            sb.AppendLine("Plane      : " + (MirrorPlaneDescription ?? "<unknown>"));
            sb.AppendLine("Generated  : " + GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", ci));
            sb.AppendLine();

            sb.AppendLine(string.Format(ci,
                "  {0} features in source", Results.Count));
            sb.AppendLine(string.Format(ci,
                "  {0,3} rebuilt natively        ({1:0.0}%)", NativeCount, NativePercent));
            sb.AppendLine(string.Format(ci,
                "  {0,3} fell back", FallbackCount));
            sb.AppendLine(string.Format(ci,
                "  {0,3} skipped (suppressed in source)", SuppressedCount));
            sb.AppendLine();

            var fallbacks = Results.Where(r => r.IsFallback).ToList();
            if (fallbacks.Count > 0)
            {
                sb.AppendLine("FELL BACK:");
                foreach (var r in fallbacks)
                {
                    sb.AppendLine(string.Format(ci, "  {0,3}  {1} ({2}) - {3}",
                        r.Index, r.Name, r.TypeName, r.Detail));
                }
                sb.AppendLine();
            }

            if (FallbackCount > 0)
            {
                sb.AppendLine(ImportedBodyInserted
                    ? "Imported-body fallback: applied (inserted, link broken, mirrored)."
                    : "Imported-body fallback: NOT APPLIED" +
                      (string.IsNullOrEmpty(ImportedBodyNote) ? "." : " - " + ImportedBodyNote));
                sb.AppendLine();
            }

            RenderMassProperties(sb, ci);

            if (!ThreadHandednessPreserved)
            {
                sb.AppendLine("NOTE: thread handedness was MIRRORED. Right-hand threads in the");
                sb.AppendLine("source are left-hand in the output. This is geometrically faithful");
                sb.AppendLine("and usually not what you want on a fastener.");
                sb.AppendLine();
            }

            if (Warnings.Count > 0)
            {
                sb.AppendLine("WARNINGS:");
                foreach (var w in Warnings)
                {
                    sb.AppendLine("  - " + w);
                }
                sb.AppendLine();
            }

            sb.AppendLine(Verdict());

            return sb.ToString();
        }

        private void RenderMassProperties(StringBuilder sb, CultureInfo ci)
        {
            var mp = MassProperties;

            if (mp == null || !mp.Ran)
            {
                sb.AppendLine("Mass-property check: not run" +
                              (string.IsNullOrEmpty(mp?.SkipReason) ? "" : " (" + mp.SkipReason + ")"));
                sb.AppendLine();
                return;
            }

            sb.AppendLine("Mass properties vs. the source part:");
            sb.AppendLine(string.Format(ci, "  volume  {0:E6} -> {1:E6}   rel delta {2:E2}",
                mp.SourceVolume, mp.MirrorVolume, mp.VolumeRelativeDelta));
            sb.AppendLine(string.Format(ci, "  area    {0:E6} -> {1:E6}   rel delta {2:E2}",
                mp.SourceArea, mp.MirrorArea, mp.AreaRelativeDelta));
            sb.AppendLine(string.Format(ci, "  centre of mass vs. reflected source: {0:E2} m",
                mp.CentreOfMassDelta));
            sb.AppendLine();
        }

        private string Verdict()
        {
            if (Total == 0)
            {
                return "VERDICT: nothing to transcribe.";
            }

            if (FallbackCount == 0 && MassProperties != null && MassProperties.Ran
                && MassProperties.Passed(1e-9, 1e-9))
            {
                return "VERDICT: fully parametric mirror, geometry matches the source to " +
                       "within tolerance.";
            }

            if (FallbackCount == 0)
            {
                return "VERDICT: every feature rebuilt natively. Geometry unverified - " +
                       "run with mass-property validation enabled to confirm.";
            }

            if (ImportedBodyInserted)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "VERDICT: hybrid result - parametric for {0} of {1} features, with the " +
                    "remaining {2} covered by an imported mirrored body. Editable up to the " +
                    "first fallback.",
                    NativeCount, Total, FallbackCount);
            }

            return string.Format(CultureInfo.InvariantCulture,
                "VERDICT: INCOMPLETE - {0} of {1} features rebuilt natively, {2} could not be " +
                "rebuilt, and the imported-body fallback did not run{3}. The output is missing " +
                "geometry. Do not use it without checking what is absent.",
                NativeCount, Total, FallbackCount,
                string.IsNullOrEmpty(ImportedBodyNote) ? "" : " (" + ImportedBodyNote + ")");
        }
    }
}
