using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace TrueMirror.Core
{
    /// <summary>
    /// Walks a part's FeatureManager tree and captures it as plain <see cref="FeatureNode"/> data.
    ///
    /// Design rule: this class never throws because of an unexpected part. It walks arbitrary
    /// user documents, and a single odd feature must not abort the dump - the dump is how we
    /// discover odd features in the first place. Every optional probe is wrapped, and failures
    /// are recorded on the node as warnings.
    ///
    /// This reader is strictly read-only. It does not call AccessSelections, does not select
    /// anything, and does not modify the source document.
    /// </summary>
    public sealed class FeatureTreeReader
    {
        private int m_Index;

        public IReadOnlyList<FeatureNode> Read(IPartDoc part)
        {
            if (part == null)
            {
                throw new ArgumentNullException(nameof(part));
            }

            m_Index = 0;
            var nodes = new List<FeatureNode>();

            var feat = part.FirstFeature() as IFeature;
            while (feat != null)
            {
                Visit(feat, depth: 0, nodes: nodes);
                feat = feat.GetNextFeature() as IFeature;
            }

            return nodes;
        }

        private void Visit(IFeature feat, int depth, List<FeatureNode> nodes)
        {
            var node = new FeatureNode
            {
                Index = ++m_Index,
                Depth = depth
            };

            node.Name = Probe(node, "Name", () => feat.Name);
            node.TypeName = Probe(node, "GetTypeName2", () => feat.GetTypeName2());
            node.Support = FeatureSupport.Classify(node.TypeName);

            Probe(node, "IsSuppressed", () =>
            {
                node.IsSuppressed = feat.IsSuppressed();
                return (string)null;
            });

            // GetDefinition tells us whether a typed ...FeatureData object is reachable for
            // this feature. That is what the transcriber will read parameters from, so knowing
            // which features expose one - and which CLR type - directly sizes the work.
            Probe(node, "GetDefinition", () =>
            {
                var def = feat.GetDefinition();
                node.HasDefinition = def != null;
                node.DefinitionType = def?.GetType().Name;
                return (string)null;
            });

            if (string.Equals(node.TypeName, "ProfileFeature", StringComparison.OrdinalIgnoreCase))
            {
                node.Sketch = ReadSketch(feat, node);
            }

            nodes.Add(node);

            var sub = feat.GetFirstSubFeature() as IFeature;
            while (sub != null)
            {
                Visit(sub, depth + 1, nodes);
                sub = sub.GetNextSubFeature() as IFeature;
            }
        }

        private SketchInfo ReadSketch(IFeature feat, FeatureNode node)
        {
            var sketch = Probe(node, "GetSpecificFeature2", () => feat.GetSpecificFeature2() as ISketch);
            if (sketch == null)
            {
                return null;
            }

            var info = new SketchInfo();

            Probe(node, "GetSketchSegments", () =>
            {
                if (sketch.GetSketchSegments() is object[] segments)
                {
                    info.SegmentCount = segments.Length;
                    foreach (var obj in segments)
                    {
                        CountSegment(obj as ISketchSegment, info);
                    }
                }
                return (string)null;
            });

            Probe(node, "GetSketchPoints2", () =>
            {
                if (sketch.GetSketchPoints2() is object[] points)
                {
                    info.PointCount = points.Length;
                }
                return (string)null;
            });

            Probe(node, "RelationManager", () =>
            {
                var relMgr = sketch.RelationManager;
                if (relMgr != null)
                {
                    // Filter 0 = all relations.
                    info.RelationCount = relMgr.GetRelationsCount(0);
                }
                return (string)null;
            });

            Probe(node, "GetConstrainedStatus", () =>
            {
                // Verified against SolidWorks.Interop.swconst: the enum is swConstrainedStatus_e
                // (NOT swSketchConstrainedStatus_e) and the member is swFullyConstrained.
                // swOverConstrained is deliberately not treated as "fully defined" - it is an
                // error state, and the transcriber should not silently reproduce it.
                var status = (swConstrainedStatus_e)sketch.GetConstrainedStatus();
                info.IsFullyDefined = status == swConstrainedStatus_e.swFullyConstrained;
                return (string)null;
            });

            return info;
        }

        private static void CountSegment(ISketchSegment segment, SketchInfo info)
        {
            if (segment == null)
            {
                return;
            }

            // Deliberately NOT using ISketchSegment::GetType(). The interop declares a
            // GetType() returning swSketchSegments_e, which collides with object.GetType()
            // and makes C# overload resolution a coin toss that differs across interop
            // versions. Type-testing the concrete interfaces is unambiguous and compiles
            // predictably. All seven interfaces verified present in
            // SolidWorks.Interop.sldworks.
            string name;

            switch (segment)
            {
                case ISketchLine _:     name = "Line";     break;
                case ISketchArc _:      name = "Arc";      break;
                case ISketchEllipse _:  name = "Ellipse";  break;
                case ISketchParabola _: name = "Parabola"; break;
                case ISketchSpline _:   name = "Spline";   break;
                case ISketchText _:     name = "Text";     break;
                default:                name = "Other";    break;
            }

            info.SegmentsByType.TryGetValue(name, out var count);
            info.SegmentsByType[name] = count + 1;
        }

        /// <summary>
        /// Runs an interop probe, recording any COM or interop failure on the node instead of
        /// propagating it. Returns default(T) on failure.
        /// </summary>
        private static T Probe<T>(FeatureNode node, string label, Func<T> probe)
        {
            try
            {
                return probe();
            }
            catch (Exception ex)
            {
                node.Warnings.Add(label + " failed: " + ex.GetType().Name + ": " + ex.Message);
                return default(T);
            }
        }
    }
}
