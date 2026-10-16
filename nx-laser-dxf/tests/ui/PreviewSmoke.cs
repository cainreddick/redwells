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
        static bool smokeOk = true;

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
                    var down = ft.GetMethod("OnMouseDown", bf, null, new[] { typeof(object), typeof(MouseEventArgs) }, null);
                    var move = ft.GetMethod("OnMouseMove", bf, null, new[] { typeof(object), typeof(MouseEventArgs) }, null);
                    var up = ft.GetMethod("OnMouseUp", bf, null, new[] { typeof(object), typeof(MouseEventArgs) }, null);
                    Func<Vec2, System.Drawing.PointF> scr = delegate (Vec2 v) { return (System.Drawing.PointF)toScreen.Invoke(preview, new object[] { v }); };
                    Action<MethodInfo, MouseButtons, System.Drawing.PointF> fire = delegate (MethodInfo m, MouseButtons b, System.Drawing.PointF q)
                    {
                        m.Invoke(preview, new object[] { null, new MouseEventArgs(b, 1, (int)q.X, (int)q.Y, 0) });
                    };

                    // Box-select the three labels of the lower-right hole group (holes at y 585..625 in DXF coords).
                    var sel = (System.Collections.Generic.List<PlacedText>)ft.GetField("selection", bf).GetValue(preview);
                    Rect2 group = new Rect2(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
                    foreach (PlacedText t in run.Labels)
                        if (t.Box.MinX > 480 && t.Box.MinY > 80 && t.Box.MaxY < 150)
                            group = new Rect2(Math.Min(group.MinX, t.Box.MinX), Math.Min(group.MinY, t.Box.MinY), Math.Max(group.MaxX, t.Box.MaxX), Math.Max(group.MaxY, t.Box.MaxY));
                    fire(down, MouseButtons.Left, scr(new Vec2(group.MinX - 12, group.MinY - 12)));
                    fire(move, MouseButtons.Left, scr(new Vec2(group.MaxX + 3, group.MaxY + 3)));
                    Shot("preview_box_selecting.png");
                    fire(up, MouseButtons.Left, scr(new Vec2(group.MaxX + 3, group.MaxY + 3)));
                    Console.WriteLine("box selected " + sel.Count + " label(s)");
                    Shot("preview_multi_selected.png");

                    // Drag one of them: all selected move together.
                    Vec2 grab = sel[0].Box.Center;
                    var before = new System.Collections.Generic.List<Vec2>();
                    foreach (var t in sel) before.Add(t.Box.Center);
                    fire(down, MouseButtons.Left, scr(grab));
                    fire(move, MouseButtons.Left, scr(new Vec2(grab.X - 20, grab.Y + 10)));
                    fire(up, MouseButtons.Left, scr(new Vec2(grab.X - 20, grab.Y + 10)));
                    // Every selected label must move by the same amount (about -20, +10 mm; the mouse moves in whole pixels).
                    bool together = true;
                    double mx = sel[0].Box.Center.X - before[0].X, my = sel[0].Box.Center.Y - before[0].Y;
                    for (int i = 0; i < sel.Count; i++)
                    {
                        double ix = sel[i].Box.Center.X - before[i].X, iy = sel[i].Box.Center.Y - before[i].Y;
                        Console.WriteLine("  label " + i + " moved by (" + Fmt.N(ix, 2) + ", " + Fmt.N(iy, 2) + ")");
                        if (Math.Abs(ix - mx) > 1e-9 || Math.Abs(iy - my) > 1e-9 || Math.Abs(ix + 20) > 2 || Math.Abs(iy - 10) > 2) together = false;
                    }
                    Console.WriteLine("dragged together: " + together);
                    smokeOk &= together && sel.Count == 3;
                    Shot("preview_multi_moved.png");

                    // Edit text/size on the selection: screenshot the dialog, set 2 mm, OK.
                    After(1000, delegate
                    {
                        Shot("preview_edit_size.png");
                        Form dlg = Top("Edit text / size");
                        foreach (Control ctl in dlg.Controls)
                        {
                            var num = ctl as NumericUpDown;
                            if (num != null) num.Value = 2.0M;
                        }
                        dlg.DialogResult = DialogResult.OK;
                    });
                    ft.GetMethod("EditSelection", bf).Invoke(preview, null);
                    bool resized = true;
                    foreach (var t in sel) if (Math.Abs(t.Height - 2.0) > 1e-9) resized = false;
                    Console.WriteLine("resized all to 2 mm: " + resized);
                    smokeOk &= resized;
                    After(800, delegate
                    {
                        Shot("preview_multi_resized.png");
                        preview.DialogResult = DialogResult.OK;
                    });
                });
            });
            bool ok = PreviewWindow.Show(ed, "S180-06068-47009-1  (S180-06068-47009-1.dxf)");
            ed.Apply();
            Console.WriteLine("preview returned " + ok + "; " + run.EditSummary + "; target labelled: " + (target.Label == "R6"));

            // Batch preview: title shows the plate number; "Skip plate" returns Skip.
            Synth s2 = Synth.Irregular();
            s2.M.Attributes = IdentityTests.Attrs();
            ExportRun run2 = ExportPipeline.Prepare(s2.M, s2.TopFace, c, c.ToAnalysisOptions(), "smoke");
            After(1200, delegate
            {
                Form bp = Top("Laser DXF preview - plate 2 of 5");
                var qtyProp = bp.GetType().GetProperty("Qty");
                Console.WriteLine("qty box shows the counted " + qtyProp.GetValue(bp, null));
                Shot("preview_batch.png");
                qtyProp.SetValue(bp, 3, null); // user overrides 4 -> 3
                bp.GetType().GetMethod("TrySkip", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(bp, null);
            });
            int qty = 4;
            PreviewResult br = PreviewWindow.ShowBatch(new LayoutEditor(run2), "S180-06068-47009  (S180-06068-47009-1.dxf)", 2, 5, ref qty);
            Console.WriteLine("batch preview returned " + br + ", qty " + qty);
            smokeOk &= br == PreviewResult.Skip && qty == 3;

            After(1000, delegate
            {
                Shot("options_dialog.png");
                Top("Laser DXF - ").DialogResult = DialogResult.OK;
            });
            bool ok2 = OptionsDialog.Show(c, "S180-06068-47009/1");
            Console.WriteLine("options returned " + ok2);
            return ok && ok2 && run.EditSummary.StartsWith("1 label(s) added") && run.EditSummary.Contains("3 item(s) moved") && target.Label == "R6" && smokeOk ? 0 : 1;
        }
    }
}
