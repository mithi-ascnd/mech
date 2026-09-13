using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using TrueMirror.Core.Geometry;
using TrueMirror.Core.Interop;
using TrueMirror.Core.Model;
using TrueMirror.Core.Report;
using TrueMirror.Core.Resolver;

namespace TrueMirror.Core.Transcribe
{
    /// <summary>
    /// Rebuilds a mirrored part feature by feature.
    ///
    /// INVARIANT the entity resolver depends on: before transcribing feature N, the target
    /// body must be exactly the mirror image of the source body at state N-1. That is why
    /// features are replayed in strict tree order and why a rebuild is forced after each
    /// one. Break the ordering and geometric resolution silently degrades from "correct" to
    /// "plausible", which is the worst kind of wrong.
    /// </summary>
    public sealed class Transcriber
    {
        private readonly ISldWorks m_App;
        private readonly TranscriptionOptions m_Options;

        public Transcriber(ISldWorks app, TranscriptionOptions options = null)
        {
            m_App = app ?? throw new ArgumentNullException(nameof(app));
            m_Options = options ?? new TranscriptionOptions();
        }

        public FidelityReport Run(IModelDoc2 sourceDoc, MirrorPlane plane, out IModelDoc2 mirrorDoc)
        {
            if (sourceDoc == null) throw new ArgumentNullException(nameof(sourceDoc));
            if (plane == null) throw new ArgumentNullException(nameof(plane));

            mirrorDoc = null;

            var report = new FidelityReport
            {
                SourceDocument = SafeTitle(sourceDoc),
                MirrorPlaneDescription = $"point {plane.Point}, normal {plane.Normal}",
                ThreadHandednessPreserved = m_Options.PreserveThreadHandedness
            };

            if (!(sourceDoc is IPartDoc sourcePart))
            {
                report.Warnings.Add("source is not a part document");
                return report;
            }

            // ---- read -------------------------------------------------------
            // AccessSelections needs the owning document, so hand it to the reader.
            var reader = new FeatureReader(sourceDoc);
            IReadOnlyList<CapturedFeature> features;

            try
            {
                features = reader.ReadAll(sourcePart);
            }
            catch (Exception ex)
            {
                report.Warnings.Add("could not read the source feature tree: " + ex.Message);
                return report;
            }

            foreach (var w in reader.Warnings) report.Warnings.Add(w);

            // Imported parts (STEP / IGES / Parasolid) carry solid bodies but NO parametric
            // history - the exporting CAD system discarded the recipe. There is nothing to
            // transcribe, and reporting "0 of N rebuilt natively" reads like a tool failure
            // when it is actually the correct and only possible answer.
            //
            // Detected without relying on a type name: a part modelled in SOLIDWORKS always
            // has at least one sketch. Bodies but no sketches means imported geometry.
            if (IsImportedGeometry(features, sourcePart))
            {
                report.Warnings.Add(
                    "This part has solid bodies but no sketches, which means it was imported " +
                    "(STEP/IGES/Parasolid) rather than modelled in SOLIDWORKS. Imported parts " +
                    "carry no feature history, so there is no recipe to mirror. Use " +
                    "SOLIDWORKS' own Insert > Mirror Part for this file - TrueMirror can only " +
                    "help with parts that have a real feature tree.");
                report.ImportedBodyNote = "source is imported geometry with no feature history";
                return report;
            }

            // ---- create the target -----------------------------------------
            mirrorDoc = CreateEmptyPart(out var createError);
            if (mirrorDoc == null)
            {
                report.Warnings.Add("could not create the mirror document: " + createError);
                return report;
            }

            report.MirrorDocument = SafeTitle(mirrorDoc);

            // ---- transcribe -------------------------------------------------
            var context = new TranscribeContext(mirrorDoc, plane, m_Options);
            var stopped = false;

            foreach (var feature in features)
            {
                if (stopped)
                {
                    report.Results.Add(Skip(feature,
                        "not attempted - transcription stopped at an earlier feature"));
                    continue;
                }

                if (feature.IsSuppressed)
                {
                    report.Results.Add(new FeatureResult
                    {
                        Index = feature.Index,
                        Name = feature.Name,
                        TypeName = feature.TypeName,
                        Outcome = FeatureOutcome.SkippedSuppressed,
                        Detail = "suppressed in the source"
                    });
                    continue;
                }

                var result = Transcribe(feature, context);
                report.Results.Add(result);

                if (!result.IsFallback) continue;

                // First fallback ends native transcription. Everything from here on is
                // covered by one imported body - see ImportedBodyFallback for why an
                // intermediate-state body is deliberately not attempted.
                stopped = true;

                if (m_Options.FallBackToImportedBody)
                {
                    ApplyImportedBodyFallback(sourceDoc, context, report);
                }
                else
                {
                    report.ImportedBodyNote = "fallback disabled in options";
                }
            }

            // ---- validate ---------------------------------------------------
            if (m_Options.ValidateMassProperties)
            {
                report.MassProperties =
                    new MassPropertyValidator().Compare(sourceDoc, mirrorDoc, plane);
            }
            else
            {
                report.MassProperties.Ran = false;
                report.MassProperties.SkipReason = "disabled in options";
            }

            return report;
        }

        /// <summary>
        /// Brings the source body in as imported geometry after native transcription failed.
        ///
        /// Both steps must succeed to claim imported geometry. A half-applied fallback - body
        /// inserted but not mirrored - would leave a RIGHT-hand part masquerading as a
        /// left-hand one, which is the single most dangerous thing this tool could produce.
        /// So a failed mirror is reported as an outright failure, not a partial success.
        /// </summary>
        private static void ApplyImportedBodyFallback(IModelDoc2 sourceDoc,
            TranscribeContext ctx, FidelityReport report)
        {
            var sourcePath = SafePathName(sourceDoc);
            var fallback = new ImportedBodyFallback(ctx.Target);

            if (!fallback.InsertSourceBody(sourcePath, out var insertError))
            {
                report.ImportedBodyNote = insertError;
                return;
            }

            var planeName = ctx.Planes.TrySelectPlaneFor(
                new SketchFrame(ctx.Plane.Point, AnyPerpendicular(ctx.Plane.Normal),
                    ctx.Plane.Normal.Cross(AnyPerpendicular(ctx.Plane.Normal))),
                out var planeReason);

            if (planeName == null)
            {
                report.ImportedBodyNote =
                    "body inserted but the mirror plane is not a standard plane in the " +
                    "target document (" + planeReason + "). The inserted body is NOT " +
                    "mirrored - discard this result.";
                return;
            }

            if (!fallback.MirrorInsertedBodies(planeName, out var mirrorError))
            {
                report.ImportedBodyNote =
                    "body inserted but could not be mirrored (" + mirrorError +
                    "). The inserted body is the WRONG HAND - discard this result.";
                return;
            }

            report.ImportedBodyInserted = true;
        }

        /// <summary>
        /// True when the source is imported geometry rather than a modelled part.
        ///
        /// Heuristic, deliberately not based on a feature type name: SOLIDWORKS type-name
        /// strings for import features vary by format and version, whereas "a modelled part
        /// always contains at least one sketch" holds universally. Bodies present plus zero
        /// sketches is therefore a reliable tell.
        /// </summary>
        private static bool IsImportedGeometry(
            IReadOnlyList<CapturedFeature> features, IPartDoc part)
        {
            if (features.Any(f => f is SketchFeature)) return false;

            try
            {
                return new GeometryProbe().GetSolidBodies(part).Any();
            }
            catch
            {
                // If we cannot tell, do not block the run - fall through to normal handling.
                return false;
            }
        }

        /// <summary>Any unit vector perpendicular to n, chosen stably.</summary>
        private static Vec3 AnyPerpendicular(Vec3 n)
        {
            var candidate = Math.Abs(n.Dot(Vec3.UnitX)) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
            return (candidate - n * candidate.Dot(n)).Normalized();
        }

        // -------------------------------------------------------------------

        private FeatureResult Transcribe(CapturedFeature feature, TranscribeContext ctx)
        {
            try
            {
                var mirrored = feature.Mirrored(ctx.Plane);

                switch (mirrored)
                {
                    case SketchFeature sketch:
                        return TranscribeSketch(sketch, ctx);

                    case ExtrudeFeature extrude:
                        return TranscribeExtrude(extrude, ctx);

                    case RevolveFeature revolve:
                        return TranscribeRevolve(revolve, ctx);

                    case FilletFeature fillet:
                        return TranscribeFillet(fillet, ctx);

                    case UnsupportedFeature unsupported:
                        return Fallback(feature, FeatureOutcome.FallbackUnsupported,
                            unsupported.Reason);

                    default:
                        return Fallback(feature, FeatureOutcome.FallbackUnsupported,
                            "no transcriber for " + mirrored.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                return Fallback(feature, FeatureOutcome.FallbackUnsupported,
                    "transcription threw: " + ex.Message);
            }
        }

        private FeatureResult TranscribeSketch(SketchFeature feature, TranscribeContext ctx)
        {
            var planeName = ctx.Planes.TrySelectPlaneFor(feature.Sketch.Frame, out var reason);

            if (planeName == null)
            {
                return Fallback(feature, FeatureOutcome.FallbackUnsupported, reason);
            }

            var unsupportedEntities = feature.Sketch.Entities
                .OfType<UnsupportedSketchEntity>()
                .Select(e => e.Kind)
                .Distinct()
                .ToList();

            // NOTE: the lambda parameter must not be named '_', or it shadows the
            // discard in 'out _' and the compiler binds the out-argument to the lambda
            // parameter's type instead.
            var sketch = ctx.Sketches.Write(
                feature.Sketch,
                frame => ctx.Planes.TrySelectPlaneFor(frame, out _) != null,
                out var failure);

            if (sketch == null)
            {
                return Fallback(feature, FeatureOutcome.FallbackUnsupported, failure);
            }

            var createdName = SafeSketchName(sketch);
            ctx.RegisterSketch(feature.Sketch.Name, createdName);

            var detail = "on " + planeName;

            if (unsupportedEntities.Count > 0)
            {
                detail += "; dropped unsupported entities: " +
                          string.Join(", ", unsupportedEntities);
            }

            if (feature.Sketch.IsFullyDefined == false)
            {
                detail += "; source sketch was under-defined, so the mirror is too";
            }

            return Native(feature, detail);
        }

        private FeatureResult TranscribeExtrude(ExtrudeFeature feature, TranscribeContext ctx)
        {
            if (feature.NeedsEntityResolution)
            {
                var faces = ctx.Probe.ProbeFaces(ctx.TargetPart);

                if (!ctx.Resolver.TryResolveAll(feature.EndReferences, faces,
                        out var matches, out var why))
                {
                    return Fallback(feature, FeatureOutcome.FallbackUnresolved, why);
                }

                if (!ctx.Features.SelectEntities(
                        matches.Select(m => m.Tag),
                        SelectionMarks.EndConditionReference,
                        out var selectFailure))
                {
                    return Fallback(feature, FeatureOutcome.FallbackUnresolved, selectFailure);
                }
            }

            var sketchName = ctx.ResolveSketchName(feature.ProfileSketchName);

            if (sketchName == null)
            {
                return Fallback(feature, FeatureOutcome.FallbackUnsupported,
                    "the profile sketch was not transcribed");
            }

            var created = ctx.Features.CreateExtrude(feature, sketchName, out var failure);

            if (created == null)
            {
                return Fallback(feature, FeatureOutcome.FallbackUnsupported, failure);
            }

            return VerifyOrFallback(feature, created, ctx,
                feature.IsCut ? "cut-extrude" : "boss-extrude");
        }

        private FeatureResult TranscribeRevolve(RevolveFeature feature, TranscribeContext ctx)
        {
            var sketchName = ctx.ResolveSketchName(feature.ProfileSketchName);

            if (sketchName == null)
            {
                return Fallback(feature, FeatureOutcome.FallbackUnsupported,
                    "the profile sketch was not transcribed");
            }

            var created = ctx.Features.CreateRevolve(feature, sketchName, null, out var failure);

            if (created == null)
            {
                return Fallback(feature, FeatureOutcome.FallbackUnsupported, failure);
            }

            return VerifyOrFallback(feature, created, ctx, "revolve");
        }

        private FeatureResult TranscribeFillet(FilletFeature feature, TranscribeContext ctx)
        {
            if (feature.References.Count == 0)
            {
                return Fallback(feature, FeatureOutcome.FallbackUnresolved,
                    "no edge references were captured from the source fillet");
            }

            var edges = ctx.Probe.ProbeEdges(ctx.TargetPart);

            if (!ctx.Resolver.TryResolveAll(feature.References, edges, out var matches, out var why))
            {
                return Fallback(feature, FeatureOutcome.FallbackUnresolved, why);
            }

            if (!ctx.Features.SelectEntities(matches.Select(m => m.Tag),
                    SelectionMarks.None, out var selectFailure))
            {
                return Fallback(feature, FeatureOutcome.FallbackUnresolved, selectFailure);
            }

            var created = ctx.Features.CreateFillet(feature, out var failure);

            if (created == null)
            {
                return Fallback(feature, FeatureOutcome.FallbackUnsupported, failure);
            }

            return VerifyOrFallback(feature, created, ctx,
                $"{matches.Count} edge(s) resolved geometrically");
        }

        private FeatureResult VerifyOrFallback(CapturedFeature feature, IFeature created,
            TranscribeContext ctx, string detail)
        {
            if (!m_Options.VerifyAfterEachFeature)
            {
                return Native(feature, detail);
            }

            if (!ctx.Features.RebuildAndVerify(created, out var error))
            {
                return Fallback(feature, FeatureOutcome.FallbackRebuildError, error);
            }

            return Native(feature, detail);
        }

        // -------------------------------------------------------------------

        private IModelDoc2 CreateEmptyPart(out string error)
        {
            error = null;

            try
            {
                var template = m_App.GetUserPreferenceStringValue(
                    (int)SolidWorks.Interop.swconst.swUserPreferenceStringValue_e
                        .swDefaultTemplatePart);

                if (string.IsNullOrEmpty(template))
                {
                    error = "no default part template is configured in SOLIDWORKS";
                    return null;
                }

                var doc = m_App.NewDocument(template, 0, 0, 0) as IModelDoc2;

                if (doc == null)
                {
                    error = "NewDocument returned null for template " + template;
                }

                return doc;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static FeatureResult Native(CapturedFeature f, string detail) => new FeatureResult
        {
            Index = f.Index,
            Name = f.Name,
            TypeName = f.TypeName,
            Outcome = FeatureOutcome.Native,
            Detail = detail
        };

        private static FeatureResult Fallback(CapturedFeature f, FeatureOutcome outcome,
            string detail) => new FeatureResult
        {
            Index = f.Index,
            Name = f.Name,
            TypeName = f.TypeName,
            Outcome = outcome,
            Detail = detail ?? "no detail recorded"
        };

        private static FeatureResult Skip(CapturedFeature f, string detail) => new FeatureResult
        {
            Index = f.Index,
            Name = f.Name,
            TypeName = f.TypeName,
            Outcome = FeatureOutcome.FallbackUnsupported,
            Detail = detail
        };

        private static string SafeTitle(IModelDoc2 doc)
        {
            try { return doc.GetTitle(); } catch { return null; }
        }

        private static string SafePathName(IModelDoc2 doc)
        {
            try { return doc?.GetPathName(); } catch { return null; }
        }

        private static string SafeSketchName(ISketch sketch)
        {
            try { return (sketch as IFeature)?.Name; } catch { return null; }
        }

        /// <summary>
        /// Per-run state. Kept in one object so the invariant documented on
        /// <see cref="Transcriber"/> is visibly owned by a single thing.
        /// </summary>
        private sealed class TranscribeContext
        {
            private readonly Dictionary<string, string> m_SketchNames =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public TranscribeContext(IModelDoc2 target, MirrorPlane plane,
                TranscriptionOptions options)
            {
                Target = target;
                TargetPart = target as IPartDoc;
                Plane = plane;

                Planes = new PlaneResolver(target);
                Sketches = new SketchWriter(target);
                Features = new FeatureWriter(target);
                Probe = new GeometryProbe();
                Resolver = new EntityResolver(plane,
                    options.ResolverLinearTolerance,
                    options.ResolverRelativeTolerance);
            }

            public IModelDoc2 Target { get; }
            public IPartDoc TargetPart { get; }
            public MirrorPlane Plane { get; }
            public PlaneResolver Planes { get; }
            public SketchWriter Sketches { get; }
            public FeatureWriter Features { get; }
            public GeometryProbe Probe { get; }
            public EntityResolver Resolver { get; }

            /// <summary>
            /// Source sketch names do not survive into the target - SOLIDWORKS assigns its
            /// own. This maps one to the other so a later extrude can find its profile.
            /// </summary>
            public void RegisterSketch(string sourceName, string targetName)
            {
                if (string.IsNullOrEmpty(sourceName) || string.IsNullOrEmpty(targetName)) return;
                m_SketchNames[sourceName] = targetName;
            }

            public string ResolveSketchName(string sourceName)
            {
                if (string.IsNullOrEmpty(sourceName)) return null;
                return m_SketchNames.TryGetValue(sourceName, out var target) ? target : null;
            }
        }
    }
}
