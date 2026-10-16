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
            public double Height;
            public Rect2 Box;
        }

        sealed class HoleSnapshot
        {
            public string Label, Source;
            public double CutDiameter;
        }

        readonly ExportRun run;
        readonly Dictionary<PlacedText, Snapshot> original = new Dictionary<PlacedText, Snapshot>();
        readonly Dictionary<CutHole, HoleSnapshot> originalHoles = new Dictionary<CutHole, HoleSnapshot>();
        readonly int originalCount;
        public readonly List<PlacedText> Items = new List<PlacedText>();
        /// <summary>Round holes (the ones that can carry a label).</summary>
        public readonly List<CutHole> Holes = new List<CutHole>();

        public LayoutEditor(ExportRun run)
        {
            this.run = run;
            Items.AddRange(run.Labels);
            if (run.Etch != null) Items.Add(run.Etch);
            foreach (var t in Items)
                original[t] = new Snapshot { Origin = t.Origin, Rotation = t.RotationDeg, Text = t.Text, Height = t.Height, Box = t.Box };
            originalCount = Items.Count;
            foreach (var h in run.Profile.Holes)
            {
                if (h.Circle == null) continue;
                Holes.Add(h);
                originalHoles[h] = new HoleSnapshot { Label = h.Label, Source = h.LabelSource, CutDiameter = h.CutDiameter };
            }
        }

        // ------------------------------------------------------------ holes

        public PlacedText LabelOf(CutHole h)
        {
            foreach (var t in Items) if (t.Hole == h && !t.Deleted) return t;
            return null;
        }

        public List<CutHole> Unlabelled()
        {
            var l = new List<CutHole>();
            foreach (var h in Holes) if (LabelOf(h) == null) l.Add(h);
            return l;
        }

        /// <summary>Labels offered as "drop" buttons in the preview: the label table, in order.</summary>
        public List<string> DropLabels()
        {
            var l = new List<string>();
            foreach (var r in run.Config.LabelTable) if (r.Label.Length > 0 && !l.Contains(r.Label)) l.Add(r.Label);
            return l;
        }

        /// <summary>How far (mm) from a hole's edge a dropped label can be and still count as that hole's label.</summary>
        public const double AttachDistance = 6.0;

        /// <summary>Drops a label by hand, centred on the given point. If it lands next to a round
        /// hole that has no label yet, it becomes that hole's label (counted in the report, and the
        /// label table's cutDiameter for it applies, as for automatic labels).</summary>
        public PlacedText AddLabelAt(string text, Vec2 centre)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            double h = run.Config.LabelHeight, w = StrokeFont.Measure(text, h);
            var t = new PlacedText
            {
                Text = text, Height = h, Origin = new Vec2(centre.X - w / 2, centre.Y - h / 2),
                Position = "placed by hand", Edited = true, Added = true
            };
            t.UpdateBox();

            CutHole near = null;
            double best = AttachDistance;
            foreach (var hole in Holes)
            {
                if (LabelOf(hole) != null) continue;
                double d = Layout2.PointRect(hole.Circle.Center, t.Box) - hole.Circle.Radius;
                if (d <= best) { best = d; near = hole; }
            }
            if (near != null)
            {
                t.Hole = near;
                near.Label = text;
                near.LabelSource = "placed by hand in the preview";
                foreach (var r in run.Config.LabelTable)
                {
                    if (!string.Equals(r.Label, text, StringComparison.OrdinalIgnoreCase) || double.IsNaN(r.CutDiameter)) continue;
                    SetCutDiameter(near, r.CutDiameter);
                    break;
                }
            }
            Items.Add(t);
            return t;
        }

        void SetCutDiameter(CutHole h, double dia)
        {
            int idx = run.Geometry.CircleCenters.IndexOf(h.Circle.Center);
            h.CutDiameter = dia;
            h.Circle.Radius = dia / 2;
            if (idx >= 0) run.Geometry.CircleRadii[idx] = dia / 2;
        }

        /// <summary>Label choices for the "add label" prompt: the table labels plus the countersink label.</summary>
        public List<string> LabelChoices()
        {
            var l = new List<string>();
            foreach (var r in run.Config.LabelTable) if (!l.Contains(r.Label)) l.Add(r.Label);
            if (run.Config.CountersinkLabel.Length > 0 && !l.Contains(run.Config.CountersinkLabel)) l.Add(run.Config.CountersinkLabel);
            return l;
        }

        public string DescribeHole(CutHole h)
        {
            string where = "Hole dia " + Fmt.N(originalHoles[h].CutDiameter, 3) + " at (" + Fmt.N(h.Center.X, 1) + ", " + Fmt.N(h.Center.Y, 1) + ")";
            PlacedText t = LabelOf(h);
            if (t != null) return where + ": labelled \"" + t.Text + "\" (" + h.LabelSource + ")";
            return where + ": no label" + (h.Note.Length > 0 ? " - " + h.Note : "");
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

        public const double MinHeight = 0.5, MaxHeight = 100;

        /// <summary>Changes the text height (mm), keeping the text centred where it was.</summary>
        public void SetHeight(PlacedText t, double height)
        {
            height = Math.Max(MinHeight, Math.Min(MaxHeight, height));
            if (Math.Abs(height - t.Height) < 1e-9) return;
            Vec2 c = t.Box.Center;
            t.Height = height;
            Rect2 atZero = PlacedText.BoxAt(new Vec2(0, 0), StrokeFont.Measure(t.Text, t.Height), t.Height, t.RotationDeg);
            t.Origin = c - atZero.Center;
            t.UpdateBox();
            t.Edited = true;
        }

        /// <summary>Non-deleted items whose box touches the rectangle (for drag-box selection).</summary>
        public List<PlacedText> ItemsIn(Rect2 r)
        {
            var l = new List<PlacedText>();
            foreach (var t in Items) if (!t.Deleted && Layout2.RectRect(t.Box, r) == 0) l.Add(t);
            return l;
        }

        public void Delete(PlacedText t)
        {
            t.Deleted = true;
            t.Edited = true;
        }

        public void Reset()
        {
            Items.RemoveRange(originalCount, Items.Count - originalCount);
            foreach (var kv in originalHoles)
            {
                kv.Key.Label = kv.Value.Label;
                kv.Key.LabelSource = kv.Value.Source;
                if (kv.Key.CutDiameter != kv.Value.CutDiameter) SetCutDiameter(kv.Key, kv.Value.CutDiameter);
            }
            foreach (var t in Items)
            {
                Snapshot s = original[t];
                t.Origin = s.Origin;
                t.RotationDeg = s.Rotation;
                t.Text = s.Text;
                t.Height = s.Height;
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
            int moved = 0, deleted = 0, added = 0;
            foreach (var t in Items)
            {
                if (!t.Edited) continue;
                if (t.Added) { if (!t.Deleted) added++; }
                else if (t.Deleted) deleted++;
                else moved++;
            }
            // Keep the hole records (used by the report) in step with the labels as edited.
            foreach (var h in Holes)
            {
                PlacedText t = LabelOf(h);
                if (t == null && h.Label.Length > 0) { h.Label = ""; h.Note = "label deleted in the preview"; }
                else if (t != null) h.Label = t.Text;
            }
            foreach (var t in Items)
                if (t.Added && !t.Deleted && t.Hole == null)
                    run.Notes.Add("Label \"" + t.Text + "\" placed by hand at (" + Fmt.N(t.Box.Center.X, 1) + ", " + Fmt.N(t.Box.Center.Y, 1) + ") is not next to a hole.");
            var parts = new List<string>();
            if (added > 0) parts.Add(added + " label(s) added");
            if (moved > 0) parts.Add(moved + " item(s) moved/rotated/edited");
            if (deleted > 0) parts.Add(deleted + " deleted");
            run.EditSummary = parts.Count == 0 ? "" : string.Join(", ", parts.ToArray()) + " in the preview";
            foreach (var t in Problems()) run.Warnings.Add("After hand edits: " + Describe(t));
            run.Labels.Clear();
            foreach (var t in Items) if (!t.IsEtch) run.Labels.Add(t);
            ExportPipeline.Finish(run);
        }
    }
}
