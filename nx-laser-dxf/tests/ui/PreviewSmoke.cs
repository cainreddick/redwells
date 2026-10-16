// UI smoke test (needs X, run via xvfb-run): drives the preview window and the options
// dialog on a synthetic plate from timers, and saves screenshots of each step.
namespace LaserDxf.Tests
{
    using System;
    using System.Reflection;
    using System.Windows.Forms;
    using LaserDxf.Core;
    using LaserDxf.Nx;

    public static class PreviewSmoke
    {
        static string outDir;

        // Mono's DrawToBitmap doesn't paint child controls, so grab the X screen instead.
        static void Shot(string name)
        {
            Application.DoEvents();
            var p = System.Diagnostics.Process.Start("import", "-window root \"" + System.IO.Path.Combine(outDir, name) + "\"");
            p.WaitForExit();
        }

        static void After(int ms, Action a)
        {
            var t = new Timer { Interval = ms };
            t.Tick += delegate { t.Stop(); a(); };
            t.Start();
        }

        static Form Top(string titleStart)
        {
            Form found = null;
            foreach (Form f in Application.OpenForms) if (f.Text.StartsWith(titleStart)) found = f;
            return found;
        }

        public static int Main(string[] args)
        {
            outDir = args.Length > 0 ? args[0] : "build/test-output";
            Synth s = Synth.Irregular();
            s.SimpleHole(250, 320, 6.0);   // like the field case: holes the table doesn't know
            s.SimpleHole(262, 360, 6.8);
            s.SimpleHole(274, 400, 6.0);
            s.M.Attributes = IdentityTests.Attrs();
            var c = new LaserConfig();
            ExportRun run = ExportPipeline.Prepare(s.M, s.TopFace, c, c.ToAnalysisOptions(), "smoke");
            var ed = new LayoutEditor(run);
            CutHole target = null;
            foreach (CutHole h in ed.Unlabelled()) if (Math.Abs(h.CutDiameter - 6.0) < 1e-6) { target = h; break; }

            After(1500, delegate
            {
                Form preview = Top("Laser DXF preview");
                Type ft = preview.GetType();
                BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;
                // Press the "R6" button, move over the plate, click next to the hole.
                ft.GetMethod("StartPlacing", bf).Invoke(preview, new object[] { "R6" });
                var toScreen = ft.GetMethod("S", bf);
                var p = (System.Drawing.PointF)toScreen.Invoke(preview, new object[] { new Vec2(target.Center.X + 6.5, target.Center.Y) });
                var at = new MouseEventArgs(MouseButtons.None, 0, (int)p.X, (int)p.Y, 0);
                ft.GetMethod("OnMouseMove", bf, null, new[] { typeof(object), typeof(MouseEventArgs) }, null).Invoke(preview, new object[] { null, at });
                Shot("preview_dropping.png");
                ft.GetMethod("OnMouseDown", bf, null, new[] { typeof(object), typeof(MouseEventArgs) }, null).Invoke(preview, new object[] { null, new MouseEventArgs(MouseButtons.Left, 1, (int)p.X, (int)p.Y, 0) });
                After(800, delegate
                {
                    Shot("preview_after_drop.png");
                    preview.DialogResult = DialogResult.OK;
                });
            });
            bool ok = PreviewWindow.Show(ed, "S180-06068-47009-1  (S180-06068-47009-1.dxf)");
            ed.Apply();
            Console.WriteLine("preview returned " + ok + "; " + run.EditSummary + "; target labelled: " + (target.Label == "R6"));

            After(1000, delegate
            {
                Shot("options_dialog.png");
                Top("Laser DXF - ").DialogResult = DialogResult.OK;
            });
            bool ok2 = OptionsDialog.Show(c, "S180-06068-47009/1");
            Console.WriteLine("options returned " + ok2);
            return ok && ok2 && run.EditSummary.StartsWith("1 label(s) added") && target.Label == "R6" ? 0 : 1;
        }
    }
}
