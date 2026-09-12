using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace TrueMirror.Core.Interop
{
    /// <summary>
    /// The escape hatch: when a feature cannot be rebuilt natively, bring the source body in
    /// as imported geometry so the user still gets a usable independent part.
    ///
    /// SEMANTICS - read before changing. This inserts the source part's FINAL body, not its
    /// state at the failed feature. Reconstructing an intermediate state would mean rolling
    /// the source tree back with EditRollback, which MODIFIES THE USER'S SOURCE DOCUMENT.
    /// TrueMirror is read-only on the source, and that guarantee is worth more than a
    /// slightly tidier fallback.
    ///
    /// The consequence is deliberate and is what the report's verdict already describes:
    /// native transcription STOPS at the first fallback, and everything from that point on
    /// is covered by one imported body. The result is parametric up to the failure and
    /// imported after it - still strictly better than the single imported body that
    /// SOLIDWORKS' own break-link mirror produces today.
    ///
    /// The link to the source is broken immediately. An independent part is the entire
    /// point of the tool; leaving an external reference would defeat it.
    /// </summary>
    public sealed class ImportedBodyFallback
    {
        private readonly IModelDoc2 m_Target;

        public ImportedBodyFallback(IModelDoc2 target)
        {
            m_Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <summary>
        /// Inserts the source part into the target as a derived, link-broken body.
        /// Returns true only when the insert actually succeeded - the caller must not
        /// report imported geometry unless this returned true.
        /// </summary>
        public bool InsertSourceBody(string sourcePath, out string failureReason)
        {
            failureReason = null;

            if (string.IsNullOrEmpty(sourcePath))
            {
                failureReason = "the source part has never been saved, so it cannot be " +
                                "inserted as a derived part - save it and re-run";
                return false;
            }

            try
            {
                // Named flags only. Every member below was verified present in
                // SolidWorks.Interop.swconst rather than written as a magic number.
                var options =
                    (int)swInsertPartOptions_e.swInsertPartImportSolids |
                    (int)swInsertPartOptions_e.swInsertPartImportMaterial |
                    (int)swInsertPartOptions_e.swInsertPartImportCustomProperties |
                    (int)swInsertPartOptions_e.swInsertPartDontZoomAll |
                    (int)swInsertPartOptions_e.swInsertPartBreakLink;

                if (!(m_Target is IPartDoc part))
                {
                    failureReason = "the target document is not a part";
                    return false;
                }

                // Third argument is ConfigurationName; empty string means the active configuration.
                var inserted = part.InsertPart3(sourcePath, options, string.Empty);

                if (inserted == null)
                {
                    failureReason = "InsertPart3 returned null for " + sourcePath;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                failureReason = "inserting the derived body threw: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Mirrors every solid body in the target about the named plane.
        ///
        /// Called after <see cref="InsertSourceBody"/> - the inserted body is a copy of the
        /// source, so it still needs reflecting to become the opposite hand.
        /// </summary>
        public bool MirrorInsertedBodies(string planeName, out string failureReason)
        {
            failureReason = null;

            try
            {
                var ext = m_Target.Extension;

                if (!ext.SelectByID2(planeName, SelectTypes.Plane, 0, 0, 0, false,
                        SelectionMarks.None, null, 0))
                {
                    failureReason = $"could not select the mirror plane '{planeName}' " +
                                    "in the target document";
                    return false;
                }

                var mirrored = m_Target.FeatureManager.InsertMirrorFeature2(
                    false,  // BMirrorBody - false mirrors features/bodies, not the whole part
                    false,  // Geometry pattern
                    false,  // Merge
                    false,  // KnitSurfaces
                    (int)swFeatureScope_e.swFeatureScope_AllBodies);

                if (mirrored == null)
                {
                    failureReason = "InsertMirrorFeature2 returned null";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                failureReason = "mirroring the imported body threw: " + ex.Message;
                return false;
            }
        }
    }
}
