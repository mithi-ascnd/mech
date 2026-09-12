using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TrueMirror.Core
{
    /// <summary>
    /// Renders a captured feature tree as text.
    ///
    /// This is the M0 deliverable and it doubles as the corpus triage tool: the coverage
    /// summary at the bottom is an early, honest read on how much of a real part the
    /// transcriber will have to handle, and the UNKNOWN TYPES section is the to-do list
    /// for <see cref="FeatureSupport"/>.
    /// </summary>
    public static class FeatureTreeReport
    {
        public static string Render(string documentTitle, IReadOnlyList<FeatureNode> nodes)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            var sb = new StringBuilder();

            sb.AppendLine("TrueMirror - feature tree dump (M0)");
            sb.AppendLine("Document : " + (documentTitle ?? "<unknown>"));
            sb.AppendLine("Captured : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("Features : " + nodes.Count + " (including sub-features)");
            sb.AppendLine();
            sb.AppendLine(new string('-', 78));
            sb.AppendLine();

            foreach (var node in nodes)
            {
                RenderNode(sb, node);
            }

            RenderCoverage(sb, nodes);
            RenderUnknownTypes(sb, nodes);
            RenderWarnings(sb, nodes);

            return sb.ToString();
        }

        private static void RenderNode(StringBuilder sb, FeatureNode node)
        {
            var indent = new string(' ', node.Depth * 2);

            sb.Append(indent);
            sb.Append(node.Index.ToString(CultureInfo.InvariantCulture).PadLeft(3));
            sb.Append("  ");
            sb.Append((node.Name ?? "<no name>").PadRight(32));
            sb.Append("  ");
            sb.Append((node.TypeName ?? "<no type>").PadRight(24));
            sb.Append("  ");
            sb.Append(Describe(node.Support));

            if (node.IsSuppressed)
            {
                sb.Append("  [suppressed]");
            }

            if (node.HasDefinition)
            {
                sb.Append("  def:" + (node.DefinitionType ?? "?"));
            }

            sb.AppendLine();

            if (node.Sketch != null)
            {
                RenderSketch(sb, indent, node.Sketch);
            }

            foreach (var warning in node.Warnings)
            {
                sb.AppendLine(indent + "       ! " + warning);
            }
        }

        private static void RenderSketch(StringBuilder sb, string indent, SketchInfo sketch)
        {
            var types = sketch.SegmentsByType.Count == 0
                ? "none"
                : string.Join(", ", sketch.SegmentsByType
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => kv.Key + " x" + kv.Value));

            sb.AppendLine(indent + "       segments: " + sketch.SegmentCount +
                          " (" + types + ")");
            sb.AppendLine(indent + "       points: " + sketch.PointCount +
                          "  relations: " + sketch.RelationCount +
                          "  fully defined: " + Describe(sketch.IsFullyDefined));
        }

        private static void RenderCoverage(StringBuilder sb, IReadOnlyList<FeatureNode> nodes)
        {
            var counted = nodes.Where(n => FeatureSupport.CountsTowardCoverage(n.Support)).ToList();

            sb.AppendLine();
            sb.AppendLine(new string('-', 78));
            sb.AppendLine();
            sb.AppendLine("COVERAGE (trivial nodes such as default planes and folders excluded)");
            sb.AppendLine();

            if (counted.Count == 0)
            {
                sb.AppendLine("  No transcribable features found.");
                sb.AppendLine();
                return;
            }

            foreach (var level in new[]
                     {
                         SupportLevel.PhaseV01,
                         SupportLevel.PhaseV02,
                         SupportLevel.PhaseV03,
                         SupportLevel.OutOfScope,
                         SupportLevel.Unknown
                     })
            {
                var count = counted.Count(n => n.Support == level);
                var pct = 100.0 * count / counted.Count;

                sb.AppendLine("  " + Describe(level).PadRight(12) +
                              count.ToString(CultureInfo.InvariantCulture).PadLeft(4) +
                              "  " + pct.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5) + "%");
            }

            var cumulative = 0;
            sb.AppendLine();
            foreach (var level in new[] { SupportLevel.PhaseV01, SupportLevel.PhaseV02, SupportLevel.PhaseV03 })
            {
                cumulative += counted.Count(n => n.Support == level);
                var pct = 100.0 * cumulative / counted.Count;
                sb.AppendLine("  through " + Describe(level) + ": " +
                              pct.ToString("0.0", CultureInfo.InvariantCulture) + "% of " +
                              counted.Count + " features");
            }

            sb.AppendLine();
        }

        private static void RenderUnknownTypes(StringBuilder sb, IReadOnlyList<FeatureNode> nodes)
        {
            var unknown = nodes
                .Where(n => n.Support == SupportLevel.Unknown && !string.IsNullOrEmpty(n.TypeName))
                .GroupBy(n => n.TypeName, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ToList();

            if (unknown.Count == 0)
            {
                return;
            }

            sb.AppendLine(new string('-', 78));
            sb.AppendLine();
            sb.AppendLine("UNKNOWN TYPES - add these to FeatureSupport.Map");
            sb.AppendLine();
            foreach (var group in unknown)
            {
                sb.AppendLine("  " + group.Key.PadRight(28) + " x" + group.Count() +
                              "   e.g. " + group.First().Name);
            }
            sb.AppendLine();
        }

        private static void RenderWarnings(StringBuilder sb, IReadOnlyList<FeatureNode> nodes)
        {
            var total = nodes.Sum(n => n.Warnings.Count);
            if (total == 0)
            {
                return;
            }

            sb.AppendLine(new string('-', 78));
            sb.AppendLine();
            sb.AppendLine("PROBE WARNINGS: " + total + " across " +
                          nodes.Count(n => n.Warnings.Count > 0) + " features.");
            sb.AppendLine("An API probe failed on these. Expected on exotic features; if a probe");
            sb.AppendLine("fails on every feature, the interop signature is likely wrong.");
            sb.AppendLine();
        }

        private static string Describe(SupportLevel level)
        {
            switch (level)
            {
                case SupportLevel.PhaseV01:   return "v0.1";
                case SupportLevel.PhaseV02:   return "v0.2";
                case SupportLevel.PhaseV03:   return "v0.3";
                case SupportLevel.OutOfScope: return "out-of-scope";
                case SupportLevel.Trivial:    return "trivial";
                default:                      return "UNKNOWN";
            }
        }

        private static string Describe(bool? value)
            => value.HasValue ? (value.Value ? "yes" : "no") : "unknown";
    }
}
