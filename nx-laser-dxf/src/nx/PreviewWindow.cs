// Preview & adjust window (Windows Forms): shows the finished layout and lets the
// user drag, rotate, edit or delete the hole labels and the part number before the
// DXF is written. All editing logic lives in LaserDxf.Core.LayoutEditor; this file
// only draws and handles the mouse/keyboard. It deliberately doesn't import NXOpen.
namespace LaserDxf.Nx
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Windows.Forms;
    using LaserDxf.Core;

    public static class PreviewWindow
    {
        /// <summary>Returns true if the user clicked Export.</summary>
        public static bool Show(LayoutEditor editor, string title)
        {
            using (var f = new PreviewForm(editor, title))
                return f.ShowDialog() == DialogResult.OK;
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

        double scale = 1, offX, offY;
        bool userZoomed;
        PlacedText selected;
        Point lastMouse;
        bool dragging, panning;

        static readonly Color CutColour = Color.Black;
        static readonly Color OkColour = Color.FromArgb(0, 70, 230);
        static readonly Color CloseColour = Color.FromArgb(230, 120, 0);
        static readonly Color OverlapColour = Color.FromArgb(220, 0, 0);

        public PreviewForm(LayoutEditor editor, string title)
        {
            ed = editor;
            Text = "Laser DXF preview - " + title;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1200, 850);
            MinimumSize = new Size(700, 480);
            TopMost = true;
            KeyPreview = true;
            Font = new Font("Segoe UI", 9f);

            canvas = new PreviewCanvas { Dock = DockStyle.Fill, BackColor = Color.FromArgb(236, 234, 224) };
            status = new Label { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8, 4, 8, 4), BackColor = SystemColors.Info };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(6, 6, 6, 0), FlowDirection = FlowDirection.LeftToRight };

            rotateBtn = MakeButton(bar, "Rotate 90° (R)", delegate { if (selected != null) { ed.Rotate90(selected); Changed(); } });
            editBtn = MakeButton(bar, "Edit text... (F2)", delegate { EditSelected(); });
            deleteBtn = MakeButton(bar, "Delete (Del)", delegate { if (selected != null) { ed.Delete(selected); selected = null; Changed(); } });
            MakeButton(bar, "Reset all", delegate { ed.Reset(); selected = null; Changed(); });
            MakeButton(bar, "Fit view (Home)", delegate { userZoomed = false; FitAll(); canvas.Invalidate(); });
            bar.Controls.Add(new Label { Width = 60 });
            Button export = MakeButton(bar, "Export DXF", delegate { TryExport(); });
            export.Font = new Font(Font, FontStyle.Bold);
            Button cancel = MakeButton(bar, "Cancel", delegate { DialogResult = DialogResult.Cancel; });
            CancelButton = cancel;

            // Fill control first, bottom bars after: WinForms docks the last-added control first.
            Controls.Add(canvas);
            Controls.Add(status);
            Controls.Add(bar);

            canvas.Paint += OnPaint;
            canvas.MouseDown += OnMouseDown;
            canvas.MouseMove += OnMouseMove;
            canvas.MouseUp += delegate { dragging = false; panning = false; };
            canvas.MouseWheel += OnWheel;
            canvas.MouseDoubleClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left && selected != null) EditSelected(); };
            canvas.Resize += delegate { if (!userZoomed) FitAll(); };
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
                using (var pen = new Pen(c, 1.2f))
                {
                    foreach (var stroke in StrokeFont.Render(t.Text, t.Origin, t.Height, t.RotationDeg))
                    {
                        var pts = new PointF[stroke.Count];
                        for (int i = 0; i < stroke.Count; i++) pts[i] = S(stroke[i]);
                        if (pts.Length >= 2) g.DrawLines(pen, pts);
                    }
                }
                if (t == selected || st != ItemStatus.Ok)
                {
                    using (var pen = new Pen(t == selected ? Color.FromArgb(40, 40, 40) : c, 1f))
                    {
                        if (t == selected) pen.DashStyle = DashStyle.Dash;
                        PointF a = S(new Vec2(t.Box.MinX, t.Box.MaxY)), b = S(new Vec2(t.Box.MaxX, t.Box.MinY));
                        g.DrawRectangle(pen, a.X - 2, a.Y - 2, b.X - a.X + 4, b.Y - a.Y + 4);
                    }
                }
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

        // ------------------------------------------------------------ mouse & keys

        void OnMouseDown(object sender, MouseEventArgs e)
        {
            canvas.Focus();
            lastMouse = e.Location;
            if (e.Button == MouseButtons.Left)
            {
                selected = ed.HitTest(M(e.Location), 4 / scale);
                dragging = selected != null;
                Changed();
            }
            else if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle) panning = true;
        }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            int dx = e.X - lastMouse.X, dy = e.Y - lastMouse.Y;
            lastMouse = e.Location;
            if (dragging && selected != null)
            {
                ed.Move(selected, new Vec2(dx / scale, -dy / scale));
                Changed();
            }
            else if (panning)
            {
                offX += dx;
                offY += dy;
                userZoomed = true;
                canvas.Invalidate();
            }
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

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool shift = (keyData & Keys.Shift) != 0;
            if (key == Keys.Home) { userZoomed = false; FitAll(); canvas.Invalidate(); return true; }
            if (selected != null)
            {
                double step = shift ? 5 : 0.5;
                Vec2 d = new Vec2(0, 0);
                if (key == Keys.Left) d = new Vec2(-step, 0);
                else if (key == Keys.Right) d = new Vec2(step, 0);
                else if (key == Keys.Up) d = new Vec2(0, step);
                else if (key == Keys.Down) d = new Vec2(0, -step);
                if (d.X != 0 || d.Y != 0) { ed.Move(selected, d); Changed(); return true; }
                if (key == Keys.R) { ed.Rotate90(selected); Changed(); return true; }
                if (key == Keys.Delete) { ed.Delete(selected); selected = null; Changed(); return true; }
                if (key == Keys.F2) { EditSelected(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ------------------------------------------------------------ actions

        void EditSelected()
        {
            if (selected == null) return;
            string text = Prompt("Edit text", selected.IsEtch ? "Part number:" : "Hole label:", selected.Text);
            if (text == null) return;
            ed.SetText(selected, text);
            if (selected.Deleted) selected = null;
            Changed();
        }

        string Prompt(string title, string label, string value)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.TopMost = true;
                f.Font = Font;
                f.ClientSize = new Size(380, 100);
                f.Controls.Add(new Label { Text = label, Left = 10, Top = 12, Width = 360 });
                var box = new TextBox { Text = value, Left = 10, Top = 32, Width = 360 };
                var ok = new Button { Text = "OK", Left = 214, Top = 64, Width = 75, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 295, Top = 64, Width = 75, DialogResult = DialogResult.Cancel };
                f.Controls.Add(box);
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog(this) == DialogResult.OK ? box.Text : null;
            }
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
            bool has = selected != null;
            rotateBtn.Enabled = editBtn.Enabled = deleteBtn.Enabled = has;
            if (has) status.Text = ed.Describe(selected) + "\nDrag or use the arrow keys to move (Shift = 5 mm steps), R to rotate, F2 or double-click to edit, Del to delete.";
            else
            {
                int n = 0;
                foreach (var t in ed.Items) if (!t.Deleted) n++;
                int p = ed.Problems().Count;
                status.Text = n + " text item(s)" + (p > 0 ? ", " + p + " need attention (orange = tight, red = crossing something)." : ", all clear.") +
                              "\nClick a label or the part number to select it. Mouse wheel zooms, right-drag pans, Home fits the view.";
            }
            canvas.Invalidate();
        }
    }
}
