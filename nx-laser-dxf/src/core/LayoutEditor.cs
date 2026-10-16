// Hand edits to the text layout before export (used by the preview window).
// Pure logic: hit testing, move / rotate / edit / delete / reset, and checking
// each item against the plate edge, the holes and the other text.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;

    public enum ItemStatus { Ok, Close, Overlap }

    public sealed class LayoutEditor
    {
        sealed class Snapshot
        {
            public Vec2 Origin;
            public double Rotation;
            public string Text;
            public Rect2 Box;
        }

        readonly ExportRun run;
        readonly Dictionary<PlacedText, Snapshot> original = new Dictionary<PlacedText, Snapshot>();
        public readonly List<PlacedText> Items = new List<PlacedText>();

        public LayoutEditor(ExportRun run)
        {
            this.run = run;
            Items.AddRange(run.Labels);
            if (run.Etch != null) Items.Add(run.Etch);
            foreach (var t in Items)
                original[t] = new Snapshot { Origin = t.Origin, Rotation = t.RotationDeg, Text = t.Text, Box = t.Box };
        }

        public ExportRun Run { get { return run; } }

        /// <summary>Topmost (last drawn) non-deleted item whose box, grown by tol, contains p.</summary>
        public PlacedText HitTest(Vec2 p, double tol)
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                PlacedText t = Items[i];
                if (!t.Deleted && Layout2.PointRect(p, t.Box) <= tol) return t;
            }
            return null;
        }

        public void Move(PlacedText t, Vec2 d)
        {
            t.Origin = t.Origin + d;
            t.Box = new Rect2(t.Box.MinX + d.X, t.Box.MinY + d.Y, t.Box.MaxX + d.X, t.Box.MaxY + d.Y);
            t.Edited = true;
        }

        /// <summary>Turns the text 90 degrees anticlockwise about the centre of its box.</summary>
        public void Rotate90(PlacedText t)
        {
            Vec2 c = t.Box.Center;
            t.RotationDeg = (t.RotationDeg + 90) % 360;
            Rect2 atZero = PlacedText.BoxAt(new Vec2(0, 0), StrokeFont.Measure(t.Text, t.Height), t.Height, t.RotationDeg);
            t.Origin = c - atZero.Center;
            t.UpdateBox();
            t.Edited = true;
        }

        /// <summary>Changes the text, keeping it centred where it was.</summary>
        public void SetText(PlacedText t, string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) { Delete(t); return; }
            Vec2 c = t.Box.Center;
            t.Text = text;
            Rect2 atZero = PlacedText.BoxAt(new Vec2(0, 0), StrokeFont.Measure(t.Text, t.Height), t.Height, t.RotationDeg);
            t.Origin = c - atZero.Center;
            t.UpdateBox();
            t.Edited = true;
        }

        public void Delete(PlacedText t)
        {
            t.Deleted = true;
            t.Edited = true;
        }

        public void Reset()
        {
            foreach (var t in Items)
            {
                Snapshot s = original[t];
                t.Origin = s.Origin;
                t.RotationDeg = s.Rotation;
                t.Text = s.Text;
                t.Box = s.Box;
                t.Deleted = false;
                t.Edited = false;
            }
        }

        /// <summary>Room around the item: to the plate edge, holes, cut-outs and other text.</summary>
        public double Clearance(PlacedText t)
        {
            double d = run.Geometry.Clearance(t.Box, -1);
            foreach (var o in Items)
                if (o != t && !o.Deleted) d = Math.Min(d, Layout2.RectRect(o.Box, t.Box));
            return d;
        }

        public double Required(PlacedText t)
        {
            return t.IsEtch ? run.Config.EtchClearance : run.Config.HoleClearance;
        }

        public ItemStatus Status(PlacedText t)
        {
            double d = Clearance(t);
            if (d <= 0) return ItemStatus.Overlap;
            return d < Required(t) - 1e-9 ? ItemStatus.Close : ItemStatus.Ok;
        }

        public string Describe(PlacedText t)
        {
            double d = Clearance(t);
            string what = t.IsEtch ? "Part number" : "Label";
            string where = " at (" + Fmt.N(t.Box.Center.X, 1) + ", " + Fmt.N(t.Box.Center.Y, 1) + ")";
            switch (Status(t))
            {
                case ItemStatus.Overlap: return what + " \"" + t.Text + "\"" + where + ": crosses the cut line, a hole or other text, or is off the plate";
                case ItemStatus.Close: return what + " \"" + t.Text + "\"" + where + ": only " + Fmt.N(d, 1) + " mm clear (wanted " + Fmt.N(Required(t), 1) + " mm)";
                default: return what + " \"" + t.Text + "\"" + where + ": OK, " + Fmt.N(Math.Min(d, 999), 1) + " mm clear";
            }
        }

        public List<PlacedText> Problems()
        {
            var l = new List<PlacedText>();
            foreach (var t in Items) if (!t.Deleted && Status(t) != ItemStatus.Ok) l.Add(t);
            return l;
        }

        /// <summary>Applies the edits to the run: rebuilds the DXF and records what changed.</summary>
        public void Apply()
        {
            int moved = 0, deleted = 0;
            foreach (var t in Items)
            {
                if (!t.Edited) continue;
                if (t.Deleted) deleted++; else moved++;
            }
            run.EditSummary = moved + deleted == 0 ? "" : moved + " item(s) moved/rotated/edited, " + deleted + " deleted in the preview";
            foreach (var t in Problems()) run.Warnings.Add("After hand edits: " + Describe(t));
            ExportPipeline.Finish(run);
        }
    }
}
