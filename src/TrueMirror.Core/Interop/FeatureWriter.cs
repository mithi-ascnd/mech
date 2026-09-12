using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TrueMirror.Core.Model;

namespace TrueMirror.Core.Interop
{
    /// <summary>
    /// Creates native features in the target part from mirrored <see cref="CapturedFeature"/> data.
    ///
    /// Signature provenance - every call below was transcribed from the SOLIDWORKS API Help
    /// 2024 pages rather than written from memory:
    ///   FeatureExtrusion3  23 params  (verified)
    ///   FeatureCut4        27 params  (verified)
    ///   FeatureRevolve2    20 params  (verified from the documented parameter order)
    ///
    /// Fillets deliberately do NOT use a varargs method. swFmFillet is one of the types
    /// IFeatureManager::CreateDefinition supports, so fillets go through the typed
    /// ISimpleFilletFeatureData2 path, which is both the documented approach and far less
    /// error-prone than a 20-argument positional call.
    ///
    /// These calls read their references from the selection list, not from arguments.
    /// Selection state is therefore part of the contract - see <see cref="SelectionMarks"/>.
    /// </summary>
    public sealed class FeatureWriter
    {
        private readonly IModelDoc2 m_Target;

        public FeatureWriter(IModelDoc2 target)
        {
            m_Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        private IFeatureManager Features => m_Target.FeatureManager;
        private IModelDocExtension Ext => m_Target.Extension;

        public void ClearSelection() => m_Target.ClearSelection2(true);

        /// <summary>Selects a named entity, returning false rather than throwing.</summary>
        public bool Select(string name, string type, int mark)
            => Ext.SelectByID2(name, type, 0, 0, 0, true, mark, null, 0);

        // -------------------------------------------------------------------
        // Extrude / cut
        // -------------------------------------------------------------------

        public IFeature CreateExtrude(ExtrudeFeature f, string profileSketchName,
            out string failureReason)
        {
            failureReason = null;

            ClearSelection();

            if (!Select(profileSketchName, SelectTypes.Sketch, SelectionMarks.Profile))
            {
                failureReason = $"could not select profile sketch '{profileSketchName}'";
                return null;
            }

            try
            {
                var feature = f.IsCut
                    ? CreateCutExtrude(f)
                    : CreateBossExtrude(f);

                if (feature == null)
                {
                    failureReason = f.IsCut
                        ? "FeatureCut4 returned null"
                        : "FeatureExtrusion3 returned null";
                }

                return feature;
            }
            catch (Exception ex)
            {
                failureReason = "extrude creation threw: " + ex.Message;
                return null;
            }
        }

        private IFeature CreateBossExtrude(ExtrudeFeature f) => Features.FeatureExtrusion3(
            /*  1 Sd                */ f.SingleEnded,
            /*  2 Flip              */ false,
            /*  3 Dir               */ f.ReverseDirection,
            /*  4 T1                */ f.EndCondition1,
            /*  5 T2                */ f.EndCondition2,
            /*  6 D1                */ f.Depth1,
            /*  7 D2                */ f.Depth2,
            /*  8 Dchk1             */ f.Draft1Enabled,
            /*  9 Dchk2             */ f.Draft2Enabled,
            /* 10 Ddir1             */ f.DraftInward1,
            /* 11 Ddir2             */ f.DraftInward2,
            /* 12 Dang1             */ f.DraftAngle1,
            /* 13 Dang2             */ f.DraftAngle2,
            /* 14 OffsetReverse1    */ false,
            /* 15 OffsetReverse2    */ false,
            /* 16 TranslateSurface1 */ false,
            /* 17 TranslateSurface2 */ false,
            /* 18 Merge             */ f.Merge,
            /* 19 UseFeatScope      */ false,
            /* 20 UseAutoSelect     */ true,
            /* 21 T0                */ f.StartCondition,
            /* 22 StartOffset       */ f.StartOffset,
            /* 23 FlipStartOffset   */ f.FlipStartOffset);

        private IFeature CreateCutExtrude(ExtrudeFeature f) => Features.FeatureCut4(
            /*  1 Sd                      */ f.SingleEnded,
            /*  2 Flip                    */ false,
            /*  3 Dir                     */ f.ReverseDirection,
            /*  4 T1                      */ f.EndCondition1,
            /*  5 T2                      */ f.EndCondition2,
            /*  6 D1                      */ f.Depth1,
            /*  7 D2                      */ f.Depth2,
            /*  8 Dchk1                   */ f.Draft1Enabled,
            /*  9 Dchk2                   */ f.Draft2Enabled,
            /* 10 Ddir1                   */ f.DraftInward1,
            /* 11 Ddir2                   */ f.DraftInward2,
            /* 12 Dang1                   */ f.DraftAngle1,
            /* 13 Dang2                   */ f.DraftAngle2,
            /* 14 OffsetReverse1          */ false,
            /* 15 OffsetReverse2          */ false,
            /* 16 TranslateSurface1       */ false,
            /* 17 TranslateSurface2       */ false,
            /* 18 NormalCut               */ false,   // sheet metal only
            /* 19 UseFeatScope            */ false,
            /* 20 UseAutoSelect           */ true,
            /* 21 AssemblyFeatureScope    */ false,
            /* 22 AutoSelectComponents    */ false,
            /* 23 PropagateFeatureToParts */ false,
            /* 24 T0                      */ f.StartCondition,
            /* 25 StartOffset             */ f.StartOffset,
            /* 26 FlipStartOffset         */ f.FlipStartOffset,
            /* 27 OptimizeGeometry        */ false);  // sheet metal only

        // -------------------------------------------------------------------
        // Revolve
        // -------------------------------------------------------------------

        public IFeature CreateRevolve(RevolveFeature f, string profileSketchName,
            string axisName, out string failureReason)
        {
            failureReason = null;

            ClearSelection();

            if (!Select(profileSketchName, SelectTypes.Sketch, SelectionMarks.Profile))
            {
                failureReason = $"could not select profile sketch '{profileSketchName}'";
                return null;
            }

            if (!string.IsNullOrEmpty(axisName)
                && !Select(axisName, SelectTypes.Axis, SelectionMarks.Profile))
            {
                failureReason = $"could not select revolve axis '{axisName}'";
                return null;
            }

            try
            {
                var feature = Features.FeatureRevolve2(
                    /*  1 SingleDir                   */ f.SingleDirection,
                    /*  2 IsSolid                     */ true,
                    /*  3 IsThin                      */ false,
                    /*  4 IsCut                       */ f.IsCut,
                    /*  5 ReverseDir                  */ f.ReverseDirection,
                    /*  6 BothDirectionUpToSameEntity */ false,
                    /*  7 Dir1Type                    */ (int)swEndConditions_e.swEndCondBlind,
                    /*  8 Dir2Type                    */ (int)swEndConditions_e.swEndCondBlind,
                    /*  9 Dir1Angle                   */ f.Angle1,
                    /* 10 Dir2Angle                   */ f.Angle2,
                    /* 11 OffsetReverse1              */ false,
                    /* 12 OffsetReverse2              */ false,
                    /* 13 OffsetDistance1             */ 0.0,
                    /* 14 OffsetDistance2             */ 0.0,
                    /* 15 ThinType                    */ (int)swThinWallType_e.swThinWallOneDirection,
                    /* 16 ThinThickness1              */ 0.0,
                    /* 17 ThinThickness2              */ 0.0,
                    /* 18 Merge                       */ f.Merge,
                    /* 19 UseFeatScope                */ false,
                    /* 20 UseAutoSelect               */ true);

                if (feature == null)
                {
                    failureReason = "FeatureRevolve2 returned null";
                }

                return feature;
            }
            catch (Exception ex)
            {
                failureReason = "revolve creation threw: " + ex.Message;
                return null;
            }
        }

        // -------------------------------------------------------------------
        // Fillet / chamfer
        // -------------------------------------------------------------------

        /// <summary>
        /// Creates a constant-radius fillet over already-selected edges.
        ///
        /// Uses CreateDefinition(swFmFillet) rather than FeatureFillet3. swFmFillet is on
        /// the documented CreateDefinition list, so the typed ISimpleFilletFeatureData2
        /// path is available here - unlike extrude, where no such path exists and the
        /// varargs call is forced.
        ///
        /// Caller must have selected the edges first; this method does not select, because
        /// edge selection comes from the geometric resolver, not from a name.
        /// </summary>
        public IFeature CreateFillet(FilletFeature f, out string failureReason)
        {
            failureReason = null;

            try
            {
                var definition = Features.CreateDefinition((int)swFeatureNameID_e.swFmFillet)
                    as ISimpleFilletFeatureData2;

                if (definition == null)
                {
                    failureReason = "CreateDefinition(swFmFillet) returned null";
                    return null;
                }

                definition.Initialize((int)swSimpleFilletType_e.swConstRadiusFillet);
                definition.DefaultRadius = f.Radius;
                definition.PropagateToTangentFaces = f.PropagateToTangentFaces;

                var feature = Features.CreateFeature(definition);

                if (feature == null)
                {
                    failureReason = "CreateFeature returned null for the fillet definition";
                }

                return feature;
            }
            catch (Exception ex)
            {
                failureReason = "fillet creation threw: " + ex.Message;
                return null;
            }
        }

        // -------------------------------------------------------------------
        // Rebuild and error checking
        // -------------------------------------------------------------------

        /// <summary>
        /// Rebuilds and reports whether the most recent feature is in error.
        ///
        /// Called after EVERY feature, not once at the end. A failure at feature 9 must be
        /// reported as feature 9; deferring the check to the end yields "the part is broken"
        /// with no way to attribute it.
        /// </summary>
        public bool RebuildAndVerify(IFeature feature, out string error)
        {
            error = null;

            try
            {
                m_Target.ForceRebuild3(false);

                if (feature == null)
                {
                    error = "no feature to verify";
                    return false;
                }

                // GetErrorCode2 reports severity via an out bool, not a message string.
                var code = feature.GetErrorCode2(out var isWarning);

                if (code != (int)swFeatureError_e.swFeatureErrorNone)
                {
                    // A warning still rebuilds. Treat it as success but keep the code so
                    // the caller can surface it - failing on warnings would reject many
                    // perfectly usable parts.
                    if (isWarning)
                    {
                        error = $"rebuilt with warning {code}";
                        return true;
                    }

                    error = $"rebuild error {code}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "rebuild threw: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Selects a set of live SOLIDWORKS entities returned by the resolver.
        /// Entities are selected by object, not by name, because the resolver works
        /// geometrically and names in the rebuilt part do not correspond to the source.
        /// </summary>
        public bool SelectEntities(IEnumerable<object> entities, int mark, out string failureReason)
        {
            failureReason = null;
            ClearSelection();

            // IEntity::Select4 takes the SelectData COCLASS interface, not ISelectData.
            // CreateSelectData returns an object implementing both, so cast straight to
            // SelectData - casting via ISelectData compiles on some interop versions and
            // not others.
            if (!(m_Target.SelectionManager is ISelectionMgr selMgr))
            {
                failureReason = "selection manager was not available";
                return false;
            }

            if (!(selMgr.CreateSelectData() is SelectData selectData))
            {
                failureReason = "could not create selection data";
                return false;
            }

            selectData.Mark = mark;

            var index = 0;
            foreach (var entity in entities)
            {
                index++;

                if (!(entity is IEntity ent))
                {
                    failureReason = $"reference {index} is not a selectable entity";
                    return false;
                }

                if (!ent.Select4(true, selectData))
                {
                    failureReason = $"reference {index} could not be selected";
                    return false;
                }
            }

            return true;
        }
    }
}
