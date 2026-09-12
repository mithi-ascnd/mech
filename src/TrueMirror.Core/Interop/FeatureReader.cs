using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TrueMirror.Core.Geometry;
using TrueMirror.Core.Model;

namespace TrueMirror.Core.Interop
{
    /// <summary>
    /// Lifts SOLIDWORKS features into plain <see cref="CapturedFeature"/> data.
    ///
    /// SOURCE-DOCUMENT SAFETY. Reading a feature's references requires
    /// IExtrudeFeatureData2::AccessSelections, which takes an edit lock on the source
    /// document. Every AccessSelections MUST be paired with ReleaseSelectionAccess in a
    /// finally block, or the user's source part is left in a modified, locked state - the
    /// worst possible failure mode for a tool that claims to be read-only.
    ///
    /// Every member used here was confirmed by reflecting over the shipped interop
    /// assembly, not written from memory. Two corrections that came out of that:
    ///   - AccessSelections takes the owning MODEL DOCUMENT, not the feature's parent, so
    ///     the document is passed in rather than discovered.
    ///   - IRevolveFeatureData2 has no IsSolid; boss vs. cut is decided by the feature's
    ///     type name instead.
    /// </summary>
    public sealed class FeatureReader
    {
        private readonly List<string> m_Warnings = new List<string>();
        private readonly IModelDoc2 m_Doc;

        /// <param name="doc">
        /// The document that owns the features. Required because AccessSelections is
        /// declared as AccessSelections(object TopDoc, object Component).
        /// </param>
        public FeatureReader(IModelDoc2 doc = null)
        {
            m_Doc = doc;
        }

        public IReadOnlyList<string> Warnings => m_Warnings;

        /// <summary>
        /// Walks the part and captures features in replay order.
        /// Unrecognised features become <see cref="UnsupportedFeature"/> so the transcriber
        /// can fall back at exactly the right point instead of silently dropping them.
        ///
        /// ABSORBED SKETCHES. A sketch consumed by an extrude or revolve does NOT appear at
        /// the top level of the tree - it is absorbed as a SUB-feature of the feature that
        /// consumed it. Walking only GetNextFeature therefore misses the profile of every
        /// solid feature in the part, which would leave nothing to extrude.
        ///
        /// So when a consuming feature is found, its absorbed sketch is emitted FIRST, as
        /// its own entry, and the consumer is linked to it by name. Replay order matters:
        /// the target document must contain the sketch before the feature that uses it.
        /// </summary>
        public IReadOnlyList<CapturedFeature> ReadAll(IPartDoc part)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));

            var captured = new List<CapturedFeature>();
            var index = 0;

            var feat = part.FirstFeature() as IFeature;
            while (feat != null)
            {
                var typeName = TryGet(() => feat.GetTypeName2(), null, null);

                if (ConsumesASketch(typeName))
                {
                    var absorbed = ReadAbsorbedSketch(feat, ref index);
                    var consumer = ReadFeature(feat, ++index);

                    if (absorbed != null)
                    {
                        captured.Add(absorbed);
                        LinkProfile(consumer, absorbed.Sketch?.Name);
                    }
                    else
                    {
                        m_Warnings.Add(
                            $"feature {index} ({TryGet(() => feat.Name, null, null)}): " +
                            "no absorbed profile sketch was found");
                    }

                    captured.Add(consumer);
                }
                else
                {
                    captured.Add(ReadFeature(feat, ++index));
                }

                feat = feat.GetNextFeature() as IFeature;
            }

            return captured;
        }

        private static bool ConsumesASketch(string typeName)
            => typeName == "Extrusion" || typeName == "Cut" || typeName == "Revolution";

        /// <summary>
        /// Finds the sketch absorbed by a consuming feature and captures it.
        /// Returns null when the feature has no absorbed sketch, which happens for
        /// extrudes driven by an external or shared sketch.
        /// </summary>
        private SketchFeature ReadAbsorbedSketch(IFeature consumer, ref int index)
        {
            var sub = TryGet(() => consumer.GetFirstSubFeature() as IFeature, null, null);

            while (sub != null)
            {
                if (TryGet(() => sub.GetTypeName2(), null, null) == "ProfileFeature")
                {
                    var captured = ReadSketch(sub, ++index) as SketchFeature;

                    if (captured != null)
                    {
                        captured.Index = index;
                        captured.Name = TryGet(() => sub.Name, null, null);
                        captured.TypeName = "ProfileFeature";

                        // Name the captured sketch after the real feature so the consumer
                        // can be linked to it unambiguously.
                        if (captured.Sketch != null)
                        {
                            captured.Sketch.Name = captured.Name;
                        }
                    }

                    return captured;
                }

                sub = TryGet(() => sub.GetNextSubFeature() as IFeature, null, null);
            }

            return null;
        }

        private static void LinkProfile(CapturedFeature consumer, string sketchName)
        {
            switch (consumer)
            {
                case ExtrudeFeature extrude:
                    extrude.ProfileSketchName = sketchName;
                    break;
                case RevolveFeature revolve:
                    revolve.ProfileSketchName = sketchName;
                    break;
            }
        }

        private CapturedFeature ReadFeature(IFeature feat, int index)
        {
            string name = null;
            string typeName = null;

            try
            {
                name = feat.Name;
                typeName = feat.GetTypeName2();
            }
            catch (Exception ex)
            {
                m_Warnings.Add($"feature {index}: could not read identity: {ex.Message}");
            }

            var suppressed = TryGet(() => feat.IsSuppressed(), false,
                $"feature {index} ({name}): IsSuppressed failed");

            CapturedFeature result;

            switch (typeName)
            {
                case "ProfileFeature":
                    result = ReadSketch(feat, index);
                    break;

                case "Extrusion":
                    result = ReadExtrude(feat, index, isCut: false);
                    break;

                case "Cut":
                    result = ReadExtrude(feat, index, isCut: true);
                    break;

                case "Revolution":
                    result = ReadRevolve(feat, index, isCut: false);
                    break;

                case "Fillet":
                    result = ReadFillet(feat, index, isChamfer: false);
                    break;

                case "Chamfer":
                    result = ReadFillet(feat, index, isChamfer: true);
                    break;

                default:
                    result = new UnsupportedFeature
                    {
                        Reason = $"no transcriber for feature type '{typeName}'"
                    };
                    break;
            }

            result.Index = index;
            result.Name = name;
            result.TypeName = typeName;
            result.IsSuppressed = suppressed;

            return result;
        }

        // -------------------------------------------------------------------
        // Sketch
        // -------------------------------------------------------------------

        private CapturedFeature ReadSketch(IFeature feat, int index)
        {
            if (!(TryGet(() => feat.GetSpecificFeature2(), null,
                    $"feature {index}: GetSpecificFeature2 failed") is ISketch sketch))
            {
                return new UnsupportedFeature { Reason = "sketch could not be accessed" };
            }

            var captured = new CapturedSketch
            {
                Name = TryGet(() => feat.Name, null, null)
            };

            captured.Frame = ReadFrame(sketch, index);

            captured.IsFullyDefined = TryGet<bool?>(
                () => (swConstrainedStatus_e)sketch.GetConstrainedStatus()
                      == swConstrainedStatus_e.swFullyConstrained,
                null,
                $"sketch {index}: GetConstrainedStatus failed");

            ReadSegments(sketch, captured, index);
            ReadRelations(sketch, captured, index);

            return new SketchFeature { Sketch = captured };
        }

        private SketchFrame ReadFrame(ISketch sketch, int index)
        {
            var data = TryGet(() => sketch.ModelToSketchTransform?.ArrayData as double[], null,
                $"sketch {index}: could not read ModelToSketchTransform");

            if (data == null || data.Length < 13)
            {
                m_Warnings.Add($"sketch {index}: falling back to the XY frame at the origin");
                return new SketchFrame(Vec3.Zero, Vec3.UnitX, Vec3.UnitY);
            }

            // ModelToSketchTransform maps MODEL -> SKETCH. The sketch's frame expressed in
            // model space is therefore the INVERSE of that transform. For a rigid transform
            // the inverse rotation is the transpose, so the frame's axes are the transform's
            // rows rather than its columns.
            var rotation = new[]
            {
                data[0], data[1], data[2],
                data[3], data[4], data[5],
                data[6], data[7], data[8]
            };

            var translation = new Vec3(data[9], data[10], data[11]);

            var xAxis = new Vec3(rotation[0], rotation[3], rotation[6]);
            var yAxis = new Vec3(rotation[1], rotation[4], rotation[7]);

            // origin_model = -R^T * t
            var origin = new Vec3(
                -(rotation[0] * translation.X + rotation[3] * translation.Y + rotation[6] * translation.Z),
                -(rotation[1] * translation.X + rotation[4] * translation.Y + rotation[7] * translation.Z),
                -(rotation[2] * translation.X + rotation[5] * translation.Y + rotation[8] * translation.Z));

            return new SketchFrame(origin, xAxis, yAxis);
        }

        private void ReadSegments(ISketch sketch, CapturedSketch captured, int index)
        {
            if (!(TryGet(() => sketch.GetSketchSegments() as object[], null,
                    $"sketch {index}: GetSketchSegments failed") is object[] segments))
            {
                return;
            }

            for (var i = 0; i < segments.Length; i++)
            {
                var entity = ReadSegment(segments[i], i);
                if (entity != null) captured.Entities.Add(entity);
            }
        }

        private SketchEntity ReadSegment(object obj, int i)
        {
            try
            {
                var construction = obj is ISketchSegment seg && seg.ConstructionGeometry;

                switch (obj)
                {
                    case ISketchLine line:
                        return new SketchLineEntity
                        {
                            Index = i,
                            IsConstruction = construction,
                            Start = PointOf(line.GetStartPoint2()),
                            End = PointOf(line.GetEndPoint2())
                        };

                    case ISketchArc arc:
                    {
                        var start = PointOf(arc.GetStartPoint2());
                        var end = PointOf(arc.GetEndPoint2());

                        // A closed circle reports coincident start and end points.
                        var isCircle = start.IsClose(end, 1e-12);

                        return new SketchArcEntity
                        {
                            Index = i,
                            IsConstruction = construction,
                            Center = PointOf(arc.GetCenterPoint2()),
                            Start = start,
                            End = end,
                            IsCircle = isCircle,
                            Direction = ArcDirection(arc)
                        };
                    }

                    case ISketchPoint point:
                        return new SketchPointEntity
                        {
                            Index = i,
                            IsConstruction = construction,
                            Position = new Vec2(point.X, point.Y)
                        };

                    default:
                        return new UnsupportedSketchEntity
                        {
                            Index = i,
                            IsConstruction = construction,
                            Kind = obj?.GetType().Name ?? "null"
                        };
                }
            }
            catch (Exception ex)
            {
                m_Warnings.Add($"sketch segment {i}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// An arc's sense is carried by its normal vector: +Z in sketch space is CCW.
        /// SOLIDWORKS reports this in SKETCH coordinates, so the Z component's sign is
        /// exactly the direction flag CreateArc wants.
        /// </summary>
        private static int ArcDirection(ISketchArc arc)
        {
            if (arc.GetNormalVector() is double[] n && n.Length >= 3)
            {
                return n[2] >= 0 ? 1 : -1;
            }

            return 1;
        }

        private static Vec2 PointOf(object pointObj)
            => pointObj is ISketchPoint p ? new Vec2(p.X, p.Y) : new Vec2(0, 0);

        private void ReadRelations(ISketch sketch, CapturedSketch captured, int index)
        {
            try
            {
                var mgr = sketch.RelationManager;
                if (mgr == null) return;

                // Filter 0 = all relations.
                if (!(mgr.GetRelations(0) is object[] relations)) return;

                foreach (var obj in relations)
                {
                    if (!(obj is ISketchRelation relation)) continue;

                    var captured_relation = new CapturedRelation
                    {
                        ConstraintType = relation.GetRelationType(),
                        TypeName = ((swConstraintType_e)relation.GetRelationType()).ToString()
                    };

                    captured.Relations.Add(captured_relation);
                }
            }
            catch (Exception ex)
            {
                m_Warnings.Add($"sketch {index}: relations could not be read: {ex.Message}");
            }
        }

        // -------------------------------------------------------------------
        // Extrude
        // -------------------------------------------------------------------

        private CapturedFeature ReadExtrude(IFeature feat, int index, bool isCut)
        {
            if (!(TryGet(() => feat.GetDefinition(), null,
                    $"feature {index}: GetDefinition failed") is IExtrudeFeatureData2 data))
            {
                return new UnsupportedFeature
                {
                    Reason = "extrude feature data was not available"
                };
            }

            var accessed = false;

            try
            {
                accessed = data.AccessSelections(m_Doc, null);

                var draft1 = TryGet(() => data.GetDraftAngle(true), 0.0, null);
                var draft2 = TryGet(() => data.GetDraftAngle(false), 0.0, null);

                return new ExtrudeFeature
                {
                    IsCut = isCut,
                    SingleEnded = !TryGet(() => data.BothDirections, false, null),
                    ReverseDirection = TryGet(() => data.ReverseDirection, false, null),
                    EndCondition1 = TryGet(() => data.GetEndCondition(true),
                        (int)swEndConditions_e.swEndCondBlind, null),
                    EndCondition2 = TryGet(() => data.GetEndCondition(false),
                        (int)swEndConditions_e.swEndCondBlind, null),
                    Depth1 = TryGet(() => data.GetDepth(true), 0.0, null),
                    Depth2 = TryGet(() => data.GetDepth(false), 0.0, null),

                    // GetDraftWhileExtruding is the real flag - a zero angle with draft
                    // enabled is legal, so inferring from the angle would lose it.
                    Draft1Enabled = TryGet(() => data.GetDraftWhileExtruding(true), Math.Abs(draft1) > 1e-12, null),
                    Draft2Enabled = TryGet(() => data.GetDraftWhileExtruding(false), Math.Abs(draft2) > 1e-12, null),
                    DraftAngle1 = draft1,
                    DraftAngle2 = draft2,
                    DraftInward1 = !TryGet(() => data.GetDraftOutward(true), false, null),
                    DraftInward2 = !TryGet(() => data.GetDraftOutward(false), false, null),

                    Merge = TryGet(() => data.Merge, true, null),

                    // FromType / FromOffsetDistance / FromOffsetReverse ARE the start
                    // condition. Defaulting to the sketch plane would silently drop any
                    // offset start.
                    StartCondition = TryGet(() => data.FromType,
                        (int)swStartConditions_e.swStartSketchPlane, null),
                    StartOffset = TryGet(() => data.FromOffsetDistance, 0.0, null),
                    FlipStartOffset = TryGet(() => data.FromOffsetReverse, false, null)
                };
            }
            catch (Exception ex)
            {
                m_Warnings.Add($"feature {index}: extrude read failed: {ex.Message}");
                return new UnsupportedFeature { Reason = "extrude parameters unreadable" };
            }
            finally
            {
                // Non-negotiable. Skipping this leaves the user's source part locked.
                if (accessed)
                {
                    try { data.ReleaseSelectionAccess(); } catch { /* nothing useful to do */ }
                }
            }
        }

        // -------------------------------------------------------------------
        // Revolve
        // -------------------------------------------------------------------

        private CapturedFeature ReadRevolve(IFeature feat, int index, bool isCut)
        {
            if (!(TryGet(() => feat.GetDefinition(), null,
                    $"feature {index}: GetDefinition failed") is IRevolveFeatureData2 data))
            {
                return new UnsupportedFeature { Reason = "revolve feature data was not available" };
            }

            var accessed = false;

            try
            {
                accessed = data.AccessSelections(m_Doc, null);

                return new RevolveFeature
                {
                    // IRevolveFeatureData2 has no IsSolid member; the feature's type name
                    // is what distinguishes a boss-revolve from a cut-revolve.
                    IsCut = isCut,
                    Angle1 = TryGet(() => data.GetRevolutionAngle(true), Math.PI * 2, null),
                    Angle2 = TryGet(() => data.GetRevolutionAngle(false), 0.0, null),
                    ReverseDirection = TryGet(() => data.ReverseDirection, false, null),
                    SingleDirection = true,

                    // The axis is resolved later from the selected axis entity. Reading it
                    // here would require holding a live reference across the mirror, which is
                    // exactly what cannot survive the document boundary.
                    AxisPoint = Vec3.Zero,
                    AxisDirection = Vec3.UnitY,

                    Merge = true
                };
            }
            catch (Exception ex)
            {
                m_Warnings.Add($"feature {index}: revolve read failed: {ex.Message}");
                return new UnsupportedFeature { Reason = "revolve parameters unreadable" };
            }
            finally
            {
                if (accessed)
                {
                    try { data.ReleaseSelectionAccess(); } catch { }
                }
            }
        }

        // -------------------------------------------------------------------
        // Fillet / chamfer
        // -------------------------------------------------------------------

        private CapturedFeature ReadFillet(IFeature feat, int index, bool isChamfer)
        {
            if (!(TryGet(() => feat.GetDefinition(), null,
                    $"feature {index}: GetDefinition failed") is ISimpleFilletFeatureData2 data))
            {
                return new UnsupportedFeature
                {
                    Reason = isChamfer
                        ? "chamfer feature data was not available"
                        : "only constant-radius fillets are supported"
                };
            }

            var accessed = false;
            var probe = new GeometryProbe();

            try
            {
                accessed = data.AccessSelections(m_Doc, null);

                var fillet = new FilletFeature
                {
                    IsChamfer = isChamfer,
                    Radius = TryGet(() => data.DefaultRadius, 0.0, null),
                    PropagateToTangentFaces = TryGet(() => data.PropagateToTangentFaces, true, null)
                };

                // Signatures of the referenced edges and faces. These are the references that
                // cannot cross the document boundary and must be geometrically re-resolved.
                if (accessed && data.GetEdgeCount() > 0
                    && data.Edges is object[] edges)
                {
                    foreach (var obj in edges)
                    {
                        FaceSignatureFrom(probe, obj, fillet);
                    }
                }

                return fillet;
            }
            catch (Exception ex)
            {
                m_Warnings.Add($"feature {index}: fillet read failed: {ex.Message}");
                return new UnsupportedFeature { Reason = "fillet parameters unreadable" };
            }
            finally
            {
                if (accessed)
                {
                    try { data.ReleaseSelectionAccess(); } catch { }
                }
            }
        }

        private static void FaceSignatureFrom(GeometryProbe probe, object obj, FilletFeature fillet)
        {
            switch (obj)
            {
                case IEdge edge:
                {
                    var sig = probe.Describe(edge);
                    if (sig != null) fillet.References.Add(sig);
                    break;
                }
                case IFace2 face:
                {
                    var sig = probe.Describe(face);
                    if (sig != null) fillet.References.Add(sig);
                    break;
                }
            }
        }

        // -------------------------------------------------------------------

        private T TryGet<T>(Func<T> probe, T fallback, string warningLabel)
        {
            try
            {
                return probe();
            }
            catch (Exception ex)
            {
                if (warningLabel != null)
                {
                    m_Warnings.Add(warningLabel + ": " + ex.Message);
                }

                return fallback;
            }
        }
    }
}
