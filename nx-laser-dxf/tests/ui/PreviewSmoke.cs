// UI smoke test (needs X, run via xvfb-run): opens the options dialog and the preview
// window on a synthetic plate, drives them from a timer, and saves screenshots.
namespace LaserDxf.Tests
{
    using System;
    using System.Drawing;
    using System.Windows.Forms;
    using LaserDxf.Core;
    using LaserDxf.Nx;

    public static class PreviewSmoke
    {
        // Mono's DrawToBitmap doesn't paint child controls, so grab the X screen instead.
        static void Shot(string path)
        {
            Application.DoEvents();
            var p = System.Diagnostics.Process.Start("import", "-window root \"" + path + "\"");
            p.WaitForExit();
        }

        public static int Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : "build/test-output";
            Synth s = Synth.Irregular();
            s.M.Attributes = IdentityTests.Attrs();
            var c = new LaserConfig();
            ExportRun run = ExportPipeline.Prepare(s.M, s.TopFace, c, c.ToAnalysisOptions(), "smoke");
            var ed = new LayoutEditor(run);

            // Drag the part number over the plate edge so the screenshot shows a flagged item.
            ed.Move(run.Etch, new Vec2(-run.Etch.Box.MinX - 20, 0));

            var timer = new Timer { Interval = 1500 };
            timer.Tick += delegate
            {
                timer.Stop();
                foreach (Form f in Application.OpenForms)
                {
                    Shot(System.IO.Path.Combine(outDir, "preview_window.png"));
                    f.DialogResult = DialogResult.OK;
                    break;
                }
            };
            timer.Start();
            bool ok = PreviewWindow.Show(ed, "S180-06068-47009-1  (S180-06068-47009-1.dxf)");
            ed.Apply();
            Console.WriteLine("preview returned " + ok + "; warnings after edits: " + run.Warnings.Count);
            foreach (string w in run.Warnings) if (w.StartsWith("After hand edits")) Console.WriteLine("  " + w);

            var t2 = new Timer { Interval = 1000 };
            t2.Tick += delegate
            {
                t2.Stop();
                foreach (Form f in Application.OpenForms)
                {
                    Shot(System.IO.Path.Combine(outDir, "options_dialog.png"));
                    f.DialogResult = DialogResult.OK;
                    break;
                }
            };
            t2.Start();
            bool ok2 = OptionsDialog.Show(c, "S180-06068-47009/1");
            Console.WriteLine("options returned " + ok2 + "; preview " + c.ShowPreview + ", placement " + c.EtchPlacement);
            return ok && ok2 ? 0 : 1;
        }
    }
}
