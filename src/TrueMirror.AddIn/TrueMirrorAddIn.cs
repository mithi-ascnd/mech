using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using TrueMirror.Core;
using TrueMirror.Core.Interop;
using TrueMirror.Core.Transcribe;
using Xarial.XCad.Base.Attributes;
using Xarial.XCad.SolidWorks;
using Xarial.XCad.SolidWorks.Documents;
// AddCommandGroup<TEnum>() is an extension method living in this namespace - without
// it only the non-generic CommandGroupSpec overload is visible.
using Xarial.XCad.UI.Commands;

namespace TrueMirror.AddIn
{
    [Title("TrueMirror")]
    [Description("Opposite-hand parts with a live feature tree")]
    public enum Commands_e
    {
        [Title("Mirror to Independent Part")]
        [Description("Rebuilds the active part mirrored about the selected face or plane, " +
                     "with a native editable feature tree and no external references")]
        MirrorToIndependentPart,

        [Title("Dump Feature Tree")]
        [Description("Writes the active part's feature tree and transcription coverage " +
                     "to a text file")]
        DumpFeatureTree,

        [Title("Verify Sketch Coordinate Space")]
        [Description("One-off diagnostic that proves which coordinate space the sketch API " +
                     "expects. Run once per SOLIDWORKS version.")]
        VerifyCoordinateSpace
    }

    [ComVisible(true)]
    [Guid("B824F3E0-3A47-4746-A0E6-E63A13D2CDDA")]
    public class TrueMirrorAddIn : SwAddInEx
    {
        public override void OnConnect()
        {
            var commandGroup = CommandManager.AddCommandGroup<Commands_e>();
            commandGroup.CommandClick += OnCommandClick;
        }

        private void OnCommandClick(Commands_e spec)
        {
            try
            {
                switch (spec)
                {
                    case Commands_e.MirrorToIndependentPart:
                        MirrorToIndependentPart();
                        break;

                    case Commands_e.DumpFeatureTree:
                        DumpFeatureTree();
                        break;

                    case Commands_e.VerifyCoordinateSpace:
                        VerifyCoordinateSpace();
                        break;
                }
            }
            catch (Exception ex)
            {
                // Surface the real exception. At this maturity the stack trace is the product.
                Application.ShowMessageBox("TrueMirror failed." + Environment.NewLine +
                                           Environment.NewLine + ex);
            }
        }

        // -------------------------------------------------------------------

        private void MirrorToIndependentPart()
        {
            if (!(Application.Documents.Active is ISwPart part))
            {
                Application.ShowMessageBox(
                    "Open a part document first. TrueMirror does not handle assemblies " +
                    "or drawings.");
                return;
            }

            var source = part.Model;

            var plane = MirrorPlaneFactory.FromSelection(source, out var planeError);

            if (plane == null)
            {
                Application.ShowMessageBox("Cannot mirror: " + planeError);
                return;
            }

            var options = new TranscriptionOptions();
            var transcriber = new Transcriber(Application.Sw, options);

            var report = transcriber.Run(source, plane, out var mirrorDoc);

            var reportPath = WriteReport(source, report.Render());

            Application.ShowMessageBox(
                Summarize(report) + Environment.NewLine +
                Environment.NewLine +
                "Full report:" + Environment.NewLine + reportPath);
        }

        private static string Summarize(TrueMirror.Core.Report.FidelityReport report)
        {
            if (report.Total == 0)
            {
                // Surface the actual reason instead of a dead end. The commonest case by far
                // is an imported STEP part, where "nothing transcribed" is the correct answer
                // and the user needs to be told why rather than left guessing.
                return report.Warnings.Count > 0
                    ? string.Join(Environment.NewLine + Environment.NewLine, report.Warnings)
                    : "Nothing was transcribed. See the report for why.";
            }

            var line = $"{report.NativeCount} of {report.Total} features rebuilt natively " +
                       $"({report.NativePercent:0.0}%).";

            if (report.FallbackCount > 0)
            {
                line += Environment.NewLine +
                        $"{report.FallbackCount} fell back - the tree is parametric up to " +
                        "the first fallback.";
            }

            var mp = report.MassProperties;

            if (mp != null && mp.Ran)
            {
                line += Environment.NewLine +
                        $"Volume delta vs. source: {mp.VolumeRelativeDelta:E2} " +
                        $"(a correct mirror is 0).";
            }

            return line;
        }

        // -------------------------------------------------------------------

        private void DumpFeatureTree()
        {
            if (!(Application.Documents.Active is ISwPart part))
            {
                Application.ShowMessageBox(
                    "Open a part document first. TrueMirror does not handle assemblies " +
                    "or drawings.");
                return;
            }

            var reader = new FeatureTreeReader();
            var nodes = reader.Read(part.Part);

            var report = FeatureTreeReport.Render(part.Model?.GetTitle(), nodes);
            var path = WriteReport(part.Model, report, ".truemirror.txt");

            Application.ShowMessageBox(
                "Captured " + nodes.Count + " features." + Environment.NewLine +
                Environment.NewLine +
                "Report written to:" + Environment.NewLine + path);
        }

        private void VerifyCoordinateSpace()
        {
            if (!(Application.Documents.Active is ISwPart part))
            {
                Application.ShowMessageBox(
                    "Open a scratch part document first. This diagnostic adds a reference " +
                    "plane and a sketch, so do not run it on a part you care about.");
                return;
            }

            var result = SketchWriter.VerifyCoordinateSpace(part.Model);
            Application.ShowMessageBox(result);
        }

        // -------------------------------------------------------------------

        /// <summary>
        /// Writes beside the source file when it has been saved, otherwise to the Desktop so
        /// an unsaved scratch part still produces a readable report.
        /// </summary>
        private static string WriteReport(
            SolidWorks.Interop.sldworks.IModelDoc2 doc,
            string content,
            string suffix = ".truemirror-report.txt")
        {
            var path = ResolveOutputPath(doc, suffix);
            File.WriteAllText(path, content);
            return path;
        }

        private static string ResolveOutputPath(
            SolidWorks.Interop.sldworks.IModelDoc2 doc, string suffix)
        {
            var modelPath = SafePathName(doc);

            if (!string.IsNullOrEmpty(modelPath))
            {
                var directory = Path.GetDirectoryName(modelPath);
                var name = Path.GetFileNameWithoutExtension(modelPath);

                if (!string.IsNullOrEmpty(directory))
                {
                    return Path.Combine(directory, name + suffix);
                }
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "truemirror-" + stamp + suffix);
        }

        private static string SafePathName(SolidWorks.Interop.sldworks.IModelDoc2 doc)
        {
            try { return doc?.GetPathName(); } catch { return null; }
        }
    }
}
