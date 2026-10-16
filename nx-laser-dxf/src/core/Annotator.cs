// Places hole labels and the part-number etch, and adds them to the DXF on the
// ETCH layer (as single-stroke polylines or DXF TEXT).
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;

    public sealed class PlacedText
    {
        public string Text = "";
        public double Height;
        public double RotationDeg;
        public Rect2 Box;
        /// <summary>Left end of the baseline (before rotation).</summary>
        public Vec2 Origin;
        public string Position = "";
        public double Clearance;
        public bool Clear;
        public CutHole Hole;
        public bool IsEtch;
        public bool Deleted;
        public bool Edited;
        /// <summary>Label added by hand in the preview.</summary>
        public bool Added;

        /// <summary>Recomputes Box from Origin, Text, Height and RotationDeg (any multiple of 90).</summary>
        public void UpdateBox()
        {
            Box = BoxAt(Origin, StrokeFont.Measure(Text, Height), Height, RotationDeg);
        }

        public static Rect2 BoxAt(Vec2 origin, double w, double h, double rotationDeg)
        {
            double r = rotationDeg * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
            Bounds2 b = Bounds2.Empty;
            foreach (var p in new[] { new Vec2(0, 0), new Vec2(w, 0), new Vec2(w, h), new Vec2(0, h) })
                b.Add(new Vec2(origin.X + p.X * cos - p.Y * sin, origin.Y + p.X * sin + p.Y * cos));
            return new Rect2(Math.Round(b.MinX, 9), Math.Round(b.MinY, 9), Math.Round(b.MaxX, 9), Math.Round(b.MaxY, 9));
        }
    }

    /// <summary>Everything text must keep away from, in DXF (mm) coordinates.</summary>
    public sealed class Obstacles
    {
        public List<Vec2> Outer = new List<Vec2>();
        public List<List<Vec2>> Cutouts = new List<List<Vec2>>();
        public List<Vec2> CircleCenters = new List<Vec2>();
        public List<double> CircleRadii = new List<double>();
        public List<Rect2> Texts = new List<Rect2>();

        public static Obstacles From(BodyModel m, FaceAnalysis a, ProfileResult p)
        {
            var o = new Obstacles();
            o.Outer = Polygon(m, a, a.Outer, p.UnitScale);
            foreach (var h in p.Holes)
            {
                if (h.Circle != null)
                {
                    o.CircleCenters.Add(h.Circle.Center);
                    o.CircleRadii.Add(h.Circle.Radius);
                }
                else o.Cutouts.Add(Polygon(m, a, h.Hole.Loop, p.UnitScale));
            }
            return o;
        }

        static List<Vec2> Polygon(BodyModel m, FaceAnalysis a, FaceLoop loop, double k)
        {
            var pts = new List<Vec2>();
            foreach (var oe in loop.Edges)
            {
                List<Vec3> e = PlateAnalyzer.EdgePolyline(m.Edge(oe.EdgeId));
                for (int i = 0; i < e.Count - 1; i++)
                    pts.Add(a.Frame.ToLocal(e[oe.Reversed ? e.Count - 1 - i : i]) * k);
            }
            return pts;
        }

        /// <summary>Same geometry, different text boxes.</summary>
        public Obstacles WithTexts(IEnumerable<Rect2> texts)
        {
            var o = new Obstacles { Outer = Outer, Cutouts = Cutouts, CircleCenters = CircleCenters, CircleRadii = CircleRadii };
            o.Texts.AddRange(texts);
            return o;
        }

        /// <summary>Smallest distance from the box to anything; negative/zero when it overlaps
        /// something or is not on the plate. skipCircle = the labelled hole itself.</summary>
        public double Clearance(Rect2 r, int skipCircle)
        {
            Vec2 c = r.Center;
            if (!Layout2.PointInPolygon(c, Outer)) return -1000;
            double d = Layout2.PolylineRect(Outer, r);
            for (int i = 0; i < CircleCenters.Count; i++)
            {
                if (i == skipCircle) continue;
                d = Math.Min(d, Layout2.PointRect(CircleCenters[i], r) - CircleRadii[i]);
            }
            foreach (var cut in Cutouts)
            {
                if (Layout2.PointInPolygon(c, cut)) return -1000;
                d = Math.Min(d, Layout2.PolylineRect(cut, r));
            }
            foreach (var t in Texts) d = Math.Min(d, Layout2.RectRect(t, r));
            return d;
        }
    }

    public static class Annotator
    {
        static readonly string[] LabelPositions = { "right", "above", "left", "below", "above right", "above left", "below left", "below right" };

        static Rect2 LabelBox(string pos, Vec2 c, double r, double gap, double w, double h)
        {
            double d = r + gap;
            double q = d * Math.Sqrt(0.5);
            switch (pos)
            {
                case "right": return new Rect2(c.X + d, c.Y - h / 2, c.X + d + w, c.Y + h / 2);
                case "above": return new Rect2(c.X - w / 2, c.Y + d, c.X + w / 2, c.Y + d + h);
                case "left": return new Rect2(c.X - d - w, c.Y - h / 2, c.X - d, c.Y + h / 2);
                case "below": return new Rect2(c.X - w / 2, c.Y - d - h, c.X + w / 2, c.Y - d);
                case "above right": return new Rect2(c.X + q, c.Y + q, c.X + q + w, c.Y + q + h);
                case "above left": return new Rect2(c.X - q - w, c.Y + q, c.X - q, c.Y + q + h);
                case "below left": return new Rect2(c.X - q - w, c.Y - q - h, c.X - q, c.Y - q);
                default: return new Rect2(c.X + q, c.Y - q - h, c.X + q + w, c.Y - q);
            }
        }

        /// <summary>Places a label next to each labelled hole: right, above, left, below, then diagonals.
        /// The first position clear of holes, other labels and the plate edge wins; otherwise the
        /// position with the most room is used and a warning is logged.</summary>
        public static List<PlacedText> PlaceLabels(ProfileResult p, Obstacles obs, LaserConfig c, List<string> warnings)
        {
            var placed = new List<PlacedText>();
            var holes = new List<CutHole>();
            foreach (var h in p.Holes) if (h.Circle != null && h.Label.Length > 0) holes.Add(h);
            // top-to-bottom, left-to-right: deterministic and reads naturally
            holes.Sort(delegate (CutHole x, CutHole y)
            {
                int cy = -x.Center.Y.CompareTo(y.Center.Y);
                return Math.Abs(x.Center.Y - y.Center.Y) > 1e-6 ? cy : x.Center.X.CompareTo(y.Center.X);
            });

            foreach (var h in holes)
            {
                PlacedText best = PlaceLabel(h, h.Label, obs, c);
                if (!best.Clear)
                    warnings.Add("Label " + h.Label + " for the hole at (" + Fmt.N(h.Center.X, 1) + ", " + Fmt.N(h.Center.Y, 1) +
                                 ") has no clear spot - placed " + best.Position + ", check it in the DXF.");
                obs.Texts.Add(best.Box);
                placed.Add(best);
            }
            return placed;
        }

        /// <summary>Places one label next to its hole: right, above, left, below, then diagonals; the
        /// first clear position wins, otherwise the one with the most room (Clear = false).</summary>
        public static PlacedText PlaceLabel(CutHole h, string text, Obstacles obs, LaserConfig c)
        {
            double hgt = c.LabelHeight;
            int self = obs.CircleCenters.IndexOf(h.Circle.Center);
            double w = StrokeFont.Measure(text, hgt);
            PlacedText best = null;
            foreach (string pos in LabelPositions)
            {
                Rect2 box = LabelBox(pos, h.Circle.Center, h.Circle.Radius, c.LabelGap, w, hgt);
                // edges need edgeClearance, everything else holeClearance
                double edge = Layout2.PolylineRect(obs.Outer, box);
                double other = obs.Clearance(box, self);
                double score = Math.Min(edge - c.EdgeClearance, other - c.HoleClearance);
                var t = new PlacedText { Text = text, Height = hgt, Box = box, Origin = new Vec2(box.MinX, box.MinY), Position = pos, Clearance = score, Hole = h };
                if (best == null || score > best.Clearance) best = t;
                if (score >= 0) { best = t; break; }
            }
            best.Clear = best.Clearance >= 0;
            return best;
        }

        /// <summary>Finds the spot on the plate with the most room for the etch text (largest clear
        /// area), horizontal first, then rotated 90 degrees, shrinking the text down to the minimum height.</summary>
        public static PlacedText PlaceEtch(string text, Obstacles obs, Bounds2 plate, LaserConfig c, List<string> warnings)
        {
            if (text.Trim().Length == 0)
            {
                warnings.Add("The part-number text is empty - nothing etched.");
                return null;
            }
            PlacedText fallback = null;
            double minH = Math.Min(c.EtchMinHeight, c.EtchHeight);
            for (double h = c.EtchHeight; h >= minH - 1e-9; h -= 0.5)
            {
                double w = StrokeFont.Measure(text, h);
                foreach (double rot in c.EtchAllowRotate90 ? new[] { 0.0, 90.0 } : new[] { 0.0 })
                {
                    double bw = rot == 0 ? w : h, bh = rot == 0 ? h : w;
                    Vec2 centre;
                    double clear = BestSpot(obs, plate, bw, bh, out centre);
                    Rect2 box = Rect2.FromCenter(centre, bw, bh);
                    var t = new PlacedText
                    {
                        Text = text, Height = h, RotationDeg = rot, Box = box, Clearance = clear,
                        Origin = rot == 0 ? new Vec2(box.MinX, box.MinY) : new Vec2(box.MaxX, box.MinY),
                        Position = (rot == 0 ? "horizontal" : "vertical") + " at (" + Fmt.N(centre.X, 1) + ", " + Fmt.N(centre.Y, 1) + ")"
                    };
                    if (clear >= c.EtchClearance)
                    {
                        t.Clear = true;
                        if (h < c.EtchHeight) warnings.Add("Part number shrunk to " + Fmt.N(h, 1) + " mm high to fit.");
                        obs.Texts.Add(box);
                        return t;
                    }
                    if (fallback == null || clear > fallback.Clearance) fallback = t;
                }
            }
            warnings.Add("No spot with " + Fmt.N(c.EtchClearance, 1) + " mm clearance for the part number - placed " + fallback.Position +
                         " with " + Fmt.N(Math.Max(fallback.Clearance, 0), 1) + " mm. Check it in the DXF.");
            obs.Texts.Add(fallback.Box);
            return fallback;
        }

        /// <summary>Centres the etch text on a point the user picked: horizontal, else rotated 90 degrees,
        /// shrinking to the minimum height if needed. Returns null (caller falls back to automatic)
        /// if the point is not on the plate.</summary>
        public static PlacedText PlaceEtchAt(string text, Vec2 at, Obstacles obs, LaserConfig c, List<string> warnings)
        {
            if (text.Trim().Length == 0) return null;
            if (!Layout2.PointInPolygon(at, obs.Outer))
            {
                warnings.Add("The picked point (" + Fmt.N(at.X, 1) + ", " + Fmt.N(at.Y, 1) + ") is not on the plate - part number placed automatically.");
                return null;
            }
            PlacedText best = null;
            double minH = Math.Min(c.EtchMinHeight, c.EtchHeight);
            for (double h = c.EtchHeight; h >= minH - 1e-9; h -= 0.5)
            {
                double w = StrokeFont.Measure(text, h);
                foreach (double rot in c.EtchAllowRotate90 ? new[] { 0.0, 90.0 } : new[] { 0.0 })
                {
                    Rect2 box = Rect2.FromCenter(at, rot == 0 ? w : h, rot == 0 ? h : w);
                    double clear = obs.Clearance(box, -1);
                    var t = new PlacedText
                    {
                        Text = text, Height = h, RotationDeg = rot, Box = box, Clearance = clear,
                        Origin = rot == 0 ? new Vec2(box.MinX, box.MinY) : new Vec2(box.MaxX, box.MinY),
                        Position = (rot == 0 ? "horizontal" : "vertical") + " at the picked point (" + Fmt.N(at.X, 1) + ", " + Fmt.N(at.Y, 1) + ")"
                    };
                    if (clear >= c.EtchClearance)
                    {
                        t.Clear = true;
                        if (h < c.EtchHeight) warnings.Add("Part number shrunk to " + Fmt.N(h, 1) + " mm high to fit at the picked point.");
                        obs.Texts.Add(box);
                        return t;
                    }
                    if (best == null || clear > best.Clearance) best = t;
                }
            }
            warnings.Add("The part number overlaps something or is close to an edge at the picked point - check it in the DXF.");
            obs.Texts.Add(best.Box);
            return best;
        }

        /// <summary>Grid search, refined twice around the best candidates.</summary>
        static double BestSpot(Obstacles obs, Bounds2 plate, double w, double h, out Vec2 centre)
        {
            double step = Math.Max(Math.Max(plate.Width, plate.Height) / 80.0, 0.5);
            var cands = new List<KeyValuePair<double, Vec2>>();
            for (double y = plate.MinY + h / 2; y <= plate.MaxY - h / 2 + 1e-9; y += step)
                for (double x = plate.MinX + w / 2; x <= plate.MaxX - w / 2 + 1e-9; x += step)
                {
                    var p = new Vec2(x, y);
                    cands.Add(new KeyValuePair<double, Vec2>(obs.Clearance(Rect2.FromCenter(p, w, h), -1), p));
                }
            centre = new Vec2((plate.MinX + plate.MaxX) / 2, (plate.MinY + plate.MaxY) / 2);
            if (cands.Count == 0) return -1000;
            cands.Sort(delegate (KeyValuePair<double, Vec2> a, KeyValuePair<double, Vec2> b) { return b.Key.CompareTo(a.Key); });

            double bestScore = cands[0].Key;
            centre = cands[0].Value;
            int seeds = Math.Min(6, cands.Count);
            for (int s = 0; s < seeds; s++)
            {
                Vec2 c0 = cands[s].Value;
                double sc = cands[s].Key;
                double st = step;
                for (int round = 0; round < 3; round++)
                {
                    st /= 3;
                    Vec2 bestLocal = c0;
                    for (int dy = -3; dy <= 3; dy++)
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            var p = new Vec2(c0.X + dx * st, c0.Y + dy * st);
                            double v = obs.Clearance(Rect2.FromCenter(p, w, h), -1);
                            if (v > sc) { sc = v; bestLocal = p; }
                        }
                    c0 = bestLocal;
                }
                if (sc > bestScore) { bestScore = sc; centre = c0; }
            }
            return bestScore;
        }

        /// <summary>Adds the text to the document on the etch layer.</summary>
        public static void Emit(DxfDocument doc, PlacedText t, LaserConfig c)
        {
            if (t == null) return;
            int colour = c.EntityColors ? c.EtchLayer.Color : 256;
            if (c.TextMode == TextMode.Text)
            {
                doc.Entities.Add(new DxfText
                {
                    Layer = c.EtchLayer.Name, Color = colour, Position = t.Origin, Height = t.Height,
                    RotationDeg = t.RotationDeg, Value = t.Text, Width = StrokeFont.Measure(t.Text, t.Height)
                });
                return;
            }
            foreach (var stroke in StrokeFont.Render(t.Text, t.Origin, t.Height, t.RotationDeg))
            {
                var pl = new DxfPolyline { Layer = c.EtchLayer.Name, Color = colour };
                pl.Points.AddRange(stroke);
                if (pl.Points.Count > 3 && pl.Points[0].DistanceTo(pl.Points[pl.Points.Count - 1]) < 1e-9)
                {
                    pl.Points.RemoveAt(pl.Points.Count - 1);
                    pl.Closed = true;
                }
                doc.Entities.Add(pl);
            }
        }
    }
}
