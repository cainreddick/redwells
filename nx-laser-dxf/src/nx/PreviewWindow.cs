// Preview & adjust window (Windows Forms): shows the finished layout and lets the
// user drag, rotate, edit, resize or delete the hole labels and the part number -
// one at a time or several at once - and drop extra labels, before the DXF is
// written. All editing logic lives in LaserDxf.Core.LayoutEditor; this file only
// draws and handles the mouse/keyboard. It deliberately doesn't import NXOpen.
namespace LaserDxf.Nx
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Windows.Forms;
    using LaserDxf.Core;

    public enum PreviewResult { Export, Skip, Cancel }

    public static class PreviewWindow
    {
        /// <summary>Returns true if the user clicked Export.</summary>
        public static bool Show(LayoutEditor editor, string title)
        {
            using (var f = new PreviewForm(editor, title, false))
                return f.ShowDialog() == DialogResult.OK;
        }

        /// <summary>Batch mode: Export this plate, Skip it, or Cancel the rest of the batch.</summary>
        public static PreviewResult ShowBatch(LayoutEditor editor, string title, int index, int count)
        {
            using (var f = new PreviewForm(editor, "plate " + index + " of " + count + " - " + title, true))
            {
                DialogResult r = f.ShowDialog();
                return r == DialogResult.OK ? PreviewResult.Export : r == DialogResult.Ignore ? PreviewResult.Skip : PreviewResult.Cancel;
            }
        }
    }

    sealed class PreviewCanvas : Panel
    {
        public PreviewCanvas()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }
    }

    sealed class PreviewForm : Form
    {
        readonly LayoutEditor ed;
        readonly PreviewCanvas canvas;
        readonly Label status;
        readonly Button rotateBtn, editBtn, deleteBtn;
        readonly bool batch;

        double scale = 1, offX, offY;
        bool userZoomed;
        readonly List<PlacedText> selection = new List<PlacedText>();
        Point lastMouse;
        bool dragging, panning;
        bool boxSelecting, boxAdditive;   // drag-box selection on empty plate
        Point boxStart, boxEnd;
        string placing;                   // label being dropped (after pressing its button), or null
        Vec2 ghost;
        bool ghostVisible;
        bool discardConfirmed;            // Cancel already asked "discard changes?"

        static readonly Color CutColour = Color.Black;
        static readonly Color OkColour = Color.FromArgb(0, 70, 230);
        static readonly Color CloseColour = Color.FromArgb(230, 120, 0);
        static readonly Color OverlapColour = Color.FromArgb(220, 0, 0);
        static readonly Color SelectColour = Color.FromArgb(40, 40, 40);

        public PreviewForm(LayoutEditor editor, string title, bool batchMode)
        {
            ed = editor;
            batch = batchMode;
            Text = "Laser DXF preview - " + title;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1200, 850);
            MinimumSize = new Size(760, 480);
            TopMost = true;
            KeyPreview = true;
            Font = new Font("Segoe UI", 9f);

            canvas = new PreviewCanvas { Dock = DockStyle.Fill, BackColor = Color.FromArgb(236, 234, 224) };
            status = new Label { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8, 4, 8, 4), BackColor = SystemColors.Info };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(6, 6, 6, 0), FlowDirection = FlowDirection.LeftToRight };

            foreach (string label in ed.DropLabels())
            {
                string l = label;
                Button b = MakeButton(bar, l, delegate { StartPlacing(l); });
                b.Font = new Font(Font, FontStyle.Bold);
                b.ForeColor = OkColour;
                b.MinimumSize = new Size(56, 28);
            }
            bar.Controls.Add(new Label { Width = 12 });
            editBtn = MakeButton(bar, "Edit text / size... (F2)", delegate { EditSelection(); });
            rotateBtn = MakeButton(bar, "Rotate 90° (R)", delegate { RotateSelection(); });
            deleteBtn = MakeButton(bar, "Delete (Del)", delegate { DeleteSelection(); });
            MakeButton(bar, "Reset all", delegate { ed.Reset(); selection.Clear(); StopPlacing(); Changed(); });
            MakeButton(bar, "Fit view (Home)", delegate { userZoomed = false; FitAll(); canvas.Invalidate(); });
            bar.Controls.Add(new Label { Width = 40 });
            Button export = MakeButton(bar, "Export DXF", delegate { TryExport(); });
            export.Font = new Font(Font, FontStyle.Bold);
            if (batch) MakeButton(bar, "Skip plate", delegate { TrySkip(); });
            MakeButton(bar, batch ? "Cancel batch" : "Cancel", delegate { TryCancel(); });

            // Fill control first, bottom bars after: WinForms docks the last-added control first.
            Controls.Add(canvas);
            Controls.Add(status);
            Controls.Add(bar);

            canvas.Paint += OnPaint;
            canvas.MouseDown += OnMouseDown;
            canvas.MouseMove += OnMouseMove;
            canvas.MouseUp += OnMouseUp;
            canvas.MouseWheel += OnWheel;
            canvas.MouseDoubleClick += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && placing == null && selection.Count > 0) EditSelection();
            };
            canvas.MouseLeave += delegate { if (ghostVisible) { ghostVisible = false; canvas.Invalidate(); } };
            canvas.Resize += delegate { if (!userZoomed) FitAll(); };
            FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                // Closing with the window's X counts as Cancel: confirm if there are edits to lose.
                if (DialogResult == DialogResult.OK || discardConfirmed) return;
                if (batch)
                {
                    if (MessageBox.Show(this, "Stop the batch here? Plates already exported are kept; this and the remaining plates are not written.",
                            "Laser DXF", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true;
                }
                else if (HasEdits() && !ConfirmDiscard()) e.Cancel = true;
            };
            Shown += delegate { FitAll(); Changed(); canvas.Focus(); };
        }

        static Button MakeButton(Control parent, string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 28, Margin = new Padding(3, 0, 3, 0) };
            b.Click += click;
            parent.Controls.Add(b);
            return b;
        }

        // ------------------------------------------------------------ view transform

        void FitAll()
        {
            double w = Math.Max(ed.Run.Profile.Width, 1), h = Math.Max(ed.Run.Profile.Height, 1);
            double cw = Math.Max(canvas.ClientSize.Width - 40, 50), ch = Math.Max(canvas.ClientSize.Height - 40, 50);
            scale = Math.Min(cw / w, ch / h);
            offX = (canvas.ClientSize.Width - w * scale) / 2;
            offY = canvas.ClientSize.Height - (canvas.ClientSize.Height - h * scale) / 2;
        }

        PointF S(Vec2 p) { return new PointF((float)(offX + p.X * scale), (float)(offY - p.Y * scale)); }
        Vec2 M(Point s) { return new Vec2((s.X - offX) / scale, (offY - s.Y) / scale); }

        // ------------------------------------------------------------ drawing

        void OnPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(CutColour, 1f))
            {
                foreach (DxfEntity ent in ed.Run.Profile.CutEntities) DrawEntity(g, pen, ent);
            }
            foreach (PlacedText t in ed.Items)
            {
                if (t.Deleted) continue;
                ItemStatus st = ed.Status(t);
                Color c = st == ItemStatus.Ok ? OkColour : st == ItemStatus.Close ? CloseColour : OverlapColour;
                DrawText(g, c, t.Text, t.Origin, t.Height, t.RotationDeg);
                bool sel = selection.Contains(t);
                if (sel || st != ItemStatus.Ok)
                {
                    using (var pen = new Pen(sel ? SelectColour : c, sel ? 1.5f : 1f))
                    {
                        if (sel) pen.DashStyle = DashStyle.Dash;
                        PointF a = S(new Vec2(t.Box.MinX, t.Box.MaxY)), b = S(new Vec2(t.Box.MaxX, t.Box.MinY));
                        g.DrawRectangle(pen, a.X - 2, a.Y - 2, b.X - a.X + 4, b.Y - a.Y + 4);
                    }
                }
            }
            if (boxSelecting)
            {
                using (var pen = new Pen(SelectColour, 1f) { DashStyle = DashStyle.Dot })
                using (var fill = new SolidBrush(Color.FromArgb(30, OkColour)))
                {
                    Rectangle r = ScreenRect(boxStart, boxEnd);
                    g.FillRectangle(fill, r);
                    g.DrawRectangle(pen, r);
                }
            }
            DrawGhost(g);
        }

        void DrawText(Graphics g, Color c, string text, Vec2 origin, double height, double rotation)
        {
            using (var pen = new Pen(c, 1.2f))
            {
                foreach (var stroke in StrokeFont.Render(text, origin, height, rotation))
                {
                    var pts = new PointF[stroke.Count];
                    for (int i = 0; i < stroke.Count; i++) pts[i] = S(stroke[i]);
                    if (pts.Length >= 2) g.DrawLines(pen, pts);
                }
            }
        }

        /// <summary>The label being dropped, drawn under the cursor.</summary>
        void DrawGhost(Graphics g)
        {
            if (placing == null || !ghostVisible) return;
            double h = ed.Run.Config.LabelHeight, w = StrokeFont.Measure(placing, h);
            Vec2 origin = new Vec2(ghost.X - w / 2, ghost.Y - h / 2);
            DrawText(g, Color.FromArgb(150, OkColour), placing, origin, h, 0);
            using (var pen = new Pen(Color.FromArgb(150, OkColour), 1f) { DashStyle = DashStyle.Dot })
            {
                PointF a = S(new Vec2(origin.X, origin.Y + h)), b = S(new Vec2(origin.X + w, origin.Y));
                g.DrawRectangle(pen, a.X - 2, a.Y - 2, b.X - a.X + 4, b.Y - a.Y + 4);
            }
        }

        void DrawEntity(Graphics g, Pen pen, DxfEntity ent)
        {
            var line = ent as DxfLine;
            var circle = ent as DxfCircle;
            var arc = ent as DxfArc;
            var poly = ent as DxfPolyline;
            if (line != null) g.DrawLine(pen, S(line.A), S(line.B));
            else if (circle != null)
            {
                PointF tl = S(new Vec2(circle.Center.X - circle.Radius, circle.Center.Y + circle.Radius));
                float d = (float)(2 * circle.Radius * scale);
                g.DrawEllipse(pen, tl.X, tl.Y, d, d);
            }
            else if (arc != null)
            {
                double sweep = (arc.EndDeg - arc.StartDeg) % 360;
                if (sweep <= 0) sweep += 360;
                int n = Math.Max(4, (int)(sweep / 3));
                var pts = new PointF[n + 1];
                for (int i = 0; i <= n; i++) pts[i] = S(arc.PointAt(arc.StartDeg + sweep * i / n));
                g.DrawLines(pen, pts);
            }
            else if (poly != null && poly.Points.Count >= 2)
            {
                var pts = new List<PointF>();
                foreach (var p in poly.Points) pts.Add(S(p));
                if (poly.Closed) pts.Add(pts[0]);
                g.DrawLines(pen, pts.ToArray());
            }
        }

        static Rectangle ScreenRect(Point a, Point b)
        {
            return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        // ------------------------------------------------------------ mouse

        void OnMouseDown(object sender, MouseEventArgs e)
        {
            canvas.Focus();
            lastMouse = e.Location;
            bool ctrl = (ModifierKeys & Keys.Control) != 0;
            if (e.Button == MouseButtons.Left && placing != null)
            {
                PlacedText dropped = ed.AddLabelAt(placing, M(e.Location));
                selection.Clear();
                if (dropped != null) selection.Add(dropped);
                if ((ModifierKeys & Keys.Shift) == 0) StopPlacing(); // Shift-click keeps dropping
                Changed();
            }
            else if (e.Button == MouseButtons.Left)
            {
                PlacedText hit = ed.HitTest(M(e.Location), 4 / scale);
                if (hit != null && ctrl)
                {
                    // Ctrl+click adds to / removes from the selection.
                    if (selection.Contains(hit)) selection.Remove(hit); else selection.Add(hit);
                }
                else if (hit != null)
                {
                    // Clicking an item that's already selected keeps the group, so it can be dragged together.
                    if (!selection.Contains(hit)) { selection.Clear(); selection.Add(hit); }
                    dragging = true;
                }
                else
                {
                    // Empty plate: start a selection box (Ctrl keeps what's already selected).
                    if (!ctrl) selection.Clear();
                    boxSelecting = true;
                    boxAdditive = ctrl;
                    boxStart = boxEnd = e.Location;
                }
                Changed();
            }
            else if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle) panning = true;
        }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            int dx = e.X - lastMouse.X, dy = e.Y - lastMouse.Y;
            lastMouse = e.Location;
            if (placing != null)
            {
                ghost = M(e.Location);
                ghostVisible = true;
                canvas.Invalidate();
            }
            if (dragging && selection.Count > 0)
            {
                Vec2 d = new Vec2(dx / scale, -dy / scale);
                foreach (var t in selection) ed.Move(t, d);
                Changed();
            }
            else if (boxSelecting)
            {
                boxEnd = e.Location;
                canvas.Invalidate();
            }
            else if (panning)
            {
                offX += dx;
                offY += dy;
                userZoomed = true;
                canvas.Invalidate();
            }
        }

        void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (boxSelecting)
            {
                boxSelecting = false;
                Rectangle r = ScreenRect(boxStart, e.Location);
                if (r.Width > 3 || r.Height > 3)
                {
                    Vec2 a = M(new Point(r.Left, r.Bottom)), b = M(new Point(r.Right, r.Top));
                    if (!boxAdditive) selection.Clear();
                    foreach (var t in ed.ItemsIn(new Rect2(a.X, a.Y, b.X, b.Y)))
                        if (!selection.Contains(t)) selection.Add(t);
                }
                Changed();
            }
            dragging = false;
            panning = false;
        }

        void OnWheel(object sender, MouseEventArgs e)
        {
            Vec2 m = M(e.Location);
            scale *= Math.Pow(1.2, e.Delta / 120.0);
            offX = e.X - m.X * scale;
            offY = e.Y + m.Y * scale;
            userZoomed = true;
            canvas.Invalidate();
        }

        // ------------------------------------------------------------ keys

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool shift = (keyData & Keys.Shift) != 0;
            bool ctrl = (keyData & Keys.Control) != 0;
            if (key == Keys.Home) { userZoomed = false; FitAll(); canvas.Invalidate(); return true; }
            if (key == Keys.Escape)
            {
                if (placing != null) StopPlacing();
                else if (selection.Count > 0) selection.Clear();
                else TryCancel();
                Changed();
                return true;
            }
            if (ctrl && key == Keys.A)
            {
                selection.Clear();
                foreach (var t in ed.Items) if (!t.Deleted && !t.IsEtch) selection.Add(t); // all hole labels
                Changed();
                return true;
            }
            if (selection.Count > 0)
            {
                double step = shift ? 5 : 0.5;
                Vec2 d = new Vec2(0, 0);
                if (key == Keys.Left) d = new Vec2(-step, 0);
                else if (key == Keys.Right) d = new Vec2(step, 0);
                else if (key == Keys.Up) d = new Vec2(0, step);
                else if (key == Keys.Down) d = new Vec2(0, -step);
                if (d.X != 0 || d.Y != 0)
                {
                    foreach (var t in selection) ed.Move(t, d);
                    Changed();
                    return true;
                }
                if (key == Keys.R) { RotateSelection(); return true; }
                if (key == Keys.Delete) { DeleteSelection(); return true; }
                if (key == Keys.F2) { EditSelection(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ------------------------------------------------------------ actions

        void StartPlacing(string label)
        {
            placing = label;
            selection.Clear();
            canvas.Cursor = Cursors.Cross;
            canvas.Focus();
            Changed();
        }

        void StopPlacing()
        {
            placing = null;
            ghostVisible = false;
            canvas.Cursor = Cursors.Default;
        }

        void RotateSelection()
        {
            foreach (var t in selection) ed.Rotate90(t);
            Changed();
        }

        void DeleteSelection()
        {
            foreach (var t in selection) ed.Delete(t);
            selection.Clear();
            Changed();
        }

        /// <summary>Text and size for one item; size only (applied to all) for several.</summary>
        void EditSelection()
        {
            if (selection.Count == 0) return;
            PlacedText first = selection[0];
            bool single = selection.Count == 1;
            string what = single ? (first.IsEtch ? "Part number" : "Hole label") : selection.Count + " selected items";

            using (var f = new Form())
            {
                f.Text = "Edit text / size";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.TopMost = true;
                f.Font = Font;
                f.ClientSize = new Size(380, 150);
                f.Controls.Add(new Label { Text = what + ":", Left = 10, Top = 12, Width = 360 });
                var box = new TextBox { Left = 10, Top = 32, Width = 360, Text = single ? first.Text : "" };
                if (!single) { box.Enabled = false; box.Text = "(text unchanged - several items selected)"; }
                f.Controls.Add(box);
                f.Controls.Add(new Label { Text = "Text height (mm):", Left = 10, Top = 70, Width = 120 });
                var size = new NumericUpDown
                {
                    Left = 134, Top = 67, Width = 80, DecimalPlaces = 1, Increment = 0.5M,
                    Minimum = (decimal)LayoutEditor.MinHeight, Maximum = (decimal)LayoutEditor.MaxHeight,
                    Value = (decimal)Math.Max(LayoutEditor.MinHeight, Math.Min(LayoutEditor.MaxHeight, Math.Round(first.Height, 1)))
                };
                f.Controls.Add(size);
                if (!single)
                {
                    bool mixed = false;
                    foreach (var t in selection) if (Math.Abs(t.Height - first.Height) > 1e-9) mixed = true;
                    if (mixed) f.Controls.Add(new Label { Text = "(sizes differ - all will get this size)", Left = 220, Top = 70, Width = 160 });
                }
                var ok = new Button { Text = "OK", Left = 214, Top = 110, Width = 75, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 295, Top = 110, Width = 75, DialogResult = DialogResult.Cancel };
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                if (f.ShowDialog(this) != DialogResult.OK) return;

                double h = (double)size.Value;
                foreach (var t in selection)
                {
                    if (single) ed.SetText(t, box.Text);
                    if (!t.Deleted) ed.SetHeight(t, h);
                }
                selection.RemoveAll(delegate (PlacedText t) { return t.Deleted; }); // emptied text deletes
            }
            Changed();
        }

        bool HasEdits()
        {
            foreach (var t in ed.Items) if (t.Edited) return true;
            return false;
        }

        bool ConfirmDiscard()
        {
            return MessageBox.Show(this, "Discard your changes and close without writing the DXF?", "Laser DXF",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        void TryCancel()
        {
            if (batch)
            {
                if (MessageBox.Show(this, "Stop the batch here? Plates already exported are kept; this and the remaining plates are not written.",
                        "Laser DXF", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            else if (HasEdits() && !ConfirmDiscard()) return;
            discardConfirmed = true;
            DialogResult = DialogResult.Cancel;
        }

        void TrySkip()
        {
            if (HasEdits() && !ConfirmDiscard()) return;
            discardConfirmed = true;
            DialogResult = DialogResult.Ignore;
        }

        void TryExport()
        {
            List<PlacedText> problems = ed.Problems();
            if (problems.Count > 0)
            {
                string list = "";
                for (int i = 0; i < Math.Min(problems.Count, 8); i++) list += "\n  - " + ed.Describe(problems[i]);
                if (problems.Count > 8) list += "\n  - ...";
                DialogResult r = MessageBox.Show(this, problems.Count + " text item(s) still need attention:" + list + "\n\nExport anyway?",
                    "Laser DXF", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }
            DialogResult = DialogResult.OK;
        }

        void Changed()
        {
            bool has = selection.Count > 0;
            rotateBtn.Enabled = editBtn.Enabled = deleteBtn.Enabled = has;
            if (placing != null)
                status.Text = "Click on the plate to drop the " + placing + " label (centred on the cursor). Shift-click to drop several; Esc to stop." +
                              "\nDropped next to a hole, it becomes that hole's label. You can drag it afterwards.";
            else if (selection.Count == 1)
                status.Text = ed.Describe(selection[0]) + "  [" + Fmt.N(selection[0].Height, 1) + " mm high]" +
                              "\nDrag or arrow keys to move (Shift = 5 mm), R rotate, F2 edit text/size, Del delete. Ctrl+click to select more.";
            else if (has)
            {
                int bad = 0;
                foreach (var t in selection) if (ed.Status(t) != ItemStatus.Ok) bad++;
                status.Text = selection.Count + " items selected" + (bad > 0 ? ", " + bad + " still need attention" : ", all clear") + "." +
                              "\nDrag one of them or use the arrow keys to move them together (Shift = 5 mm). R rotates each, F2 sets their text size, Del deletes, Esc deselects.";
            }
            else
            {
                int n = 0;
                foreach (var t in ed.Items) if (!t.Deleted) n++;
                int p = ed.Problems().Count;
                int u = ed.Unlabelled().Count;
                status.Text = n + " text item(s)" + (p > 0 ? ", " + p + " need attention (orange = tight, red = crossing something)" : ", all clear") +
                              "; " + u + " round hole(s) without a label." +
                              "\nClick text to select it; Ctrl+click or drag a box to select several (Ctrl+A = all hole labels). Press R6/M8 and click to add a label. Wheel zooms, right-drag pans.";
            }
            canvas.Invalidate();
        }
    }
}
