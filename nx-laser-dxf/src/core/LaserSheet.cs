// The PDF instruction sheet that goes to the laser shop with the DXF, drawn in the company
// drawing border (zones, banner, revision table, title block with logo). It shows the plate
// as cut (etching in blue, exactly as in the DXF) with overall X/Y reference dimensions,
// the laser cutting notes and a hole key. Landscape A4 or A3.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    public static class LaserSheet
    {
        // Border geometry (mm), as the company A4 border: frame 20 mm from the left edge (binding)
        // and 10 mm from the others, a 5 mm zone strip, 50 mm zones centred on the sheet.
        const double FrameLeft = 20, FrameOther = 10, Strip = 5, Zone = 50;
        const double TitleW = 180, TitleH = 46;   // title block incl. the revision rows
        const double DimSpace = 12;               // room for the reference dimensions, left of and below the plate
        static readonly double[] Scales = { 5, 2, 1, 0.5, 0.2, 0.1, 0.05, 0.02 }; // paper mm per model mm

        sealed class Rgb
        {
            public double R, G, B;
            public Rgb(double r, double g, double b) { R = r; G = g; B = b; }
        }

        static readonly Rgb Black = new Rgb(0, 0, 0);
        static readonly Rgb Blue = new Rgb(0, 0.25, 0.85);
        static readonly Rgb Red = new Rgb(0.8, 0, 0);
        static readonly Rgb BannerRed = new Rgb(1, 0, 0);

        public static string ScaleText(double s)
        {
            if (s >= 1) return Fmt.N(s, 2) + ":1";
            return "1:" + Fmt.N(1 / s, 2);
        }

        /// <summary>Largest standard scale at which a plate of size w x h fits the area.</summary>
        public static double FitScale(double w, double h, double areaW, double areaH)
        {
            foreach (double s in Scales)
                if (w * s <= areaW && h * s <= areaH) return s;
            return Math.Min(areaW / Math.Max(w, 1), areaH / Math.Max(h, 1));
        }

        /// <summary>Where the plate view goes: inside the frame, above the title block and below the banner.</summary>
        public static void ViewArea(double pw, double ph, out double x, out double y, out double w, out double h)
        {
            x = FrameLeft + Strip + 3;
            y = FrameOther + Strip + TitleH + 3;
            w = pw - FrameOther - Strip - 3 - x;
            h = ph - FrameOther - Strip - 16 - y;
        }

        /// <summary>A4, unless the plate would be drawn smaller than 1:1 there and A3 shows it larger.</summary>
        public static void ChooseSheet(ExportRun run, out string name, out double pw, out double ph, out double scale)
        {
            string want = run.Config.SheetSize.ToUpperInvariant();
            double w = run.Profile.Width, h = run.Profile.Height;
            double dim = run.Config.ReferenceDimensions ? DimSpace : 0;
            double ax, ay, aw, ah;
            ViewArea(297, 210, out ax, out ay, out aw, out ah);
            double s4 = FitScale(w, h, aw - dim, ah - dim);
            ViewArea(420, 297, out ax, out ay, out aw, out ah);
            double s3 = FitScale(w, h, aw - dim, ah - dim);
            // A3 only when A4 would have to shrink the plate and A3 shows it larger.
            bool a3 = want == "A3" || (want != "A4" && s4 < 1 - 1e-9 && s3 > s4 * 1.01);
            name = a3 ? "A3" : "A4";
            pw = a3 ? 420 : 297;
            ph = a3 ? 297 : 210;
            scale = a3 ? s3 : s4;
        }

        public static PdfDocument Build(ExportRun run)
        {
            LaserConfig c = run.Config;
            string sheetName;
            double pw, ph, s;
            ChooseSheet(run, out sheetName, out pw, out ph, out s);
            var page = new PdfPage(pw, ph);
            var doc = new PdfDocument { Title = run.EtchText + " laser cutting instruction", Author = run.Identity.DrawnBy, Subject = run.FileName };
            doc.Pages.Add(page);

            DrawBorder(page, c, pw, ph);

            // ---------- the plate ----------
            double vx, vy, vw, vh;
            ViewArea(pw, ph, out vx, out vy, out vw, out vh);
            double dim = c.ReferenceDimensions ? DimSpace : 0;
            Bounds2 b = PlateBounds(run);
            double ox = vx + dim + (vw - dim - b.Width * s) / 2, oy = vy + dim + (vh - dim - b.Height * s) / 2;
            Func<Vec2, Vec2> P = delegate (Vec2 m) { return new Vec2(ox + (m.X - b.MinX) * s, oy + (m.Y - b.MinY) * s); };

            Color(page, Black, 0.3);
            double headY = ph - FrameOther - Strip - 9.5;
            page.Text(vx, headY, 2.8, (c.SheetTitle.Length > 0 ? c.SheetTitle + "   -   " : "") + "VIEW FROM THE ETCH FACE   -   SCALE " + ScaleText(s), true, PdfAlign.Left);
            page.Text(vx, headY - 3.6, 2.3, "DO NOT SCALE: USE THE DXF FOR ALL GEOMETRY." +
                      (c.ReferenceDimensions ? "  DIMENSIONS IN ( ) ARE FOR REFERENCE ONLY." : ""), false, PdfAlign.Left);

            Color(page, Black, 0.35);
            foreach (DxfEntity e in run.Profile.CutEntities) DrawEntity(page, e, P, s);

            Color(page, Blue, 0.25);
            foreach (PlacedText t in AllText(run)) DrawEtchText(page, t, P, s, c.SheetMinTextHeight);

            if (c.ReferenceDimensions)
            {
                Vec2 lo = P(new Vec2(b.MinX, b.MinY)), hi = P(new Vec2(b.MaxX, b.MaxY));
                Color(page, Black, 0.18);
                DimX(page, lo.X, hi.X, lo.Y, lo.Y - 7, "(" + Fmt.N(b.Width, 1) + ")");
                DimY(page, lo.Y, hi.Y, lo.X, lo.X - 7, "(" + Fmt.N(b.Height, 1) + ")");
            }

            // ---------- notes (bottom left) and title block (bottom right) ----------
            double tbR = pw - FrameOther - Strip, tbB = FrameOther + Strip, tbL = tbR - TitleW;
            DrawNotes(page, run, FrameLeft + Strip + 2, tbB + TitleH - 1, tbL - (FrameLeft + Strip) - 5, tbB + 1);
            DrawTitleBlock(page, run, tbL, tbB, ScaleText(s), sheetName);
            DrawRevisionMark(page, run, pw - FrameOther, FrameOther);
            return doc;
        }

        /// <summary>Extents of the cut geometry (model mm).</summary>
        public static Bounds2 PlateBounds(ExportRun run)
        {
            Bounds2 b = Bounds2.Empty;
            foreach (DxfEntity e in run.Profile.CutEntities) e.AddBounds(ref b);
            if (b.IsEmpty) { b.MinX = b.MinY = 0; b.MaxX = run.Profile.Width; b.MaxY = run.Profile.Height; }
            return b;
        }

        // ------------------------------------------------------------ reference dimensions

        static void Arrow(PdfPage page, Vec2 tip, Vec2 dir)
        {
            const double len = 2.5, half = 0.45;
            Vec2 n = new Vec2(-dir.Y, dir.X);
            Vec2 back = tip - dir * len;
            page.FillPolygon(new List<Vec2> { tip, back + n * half, back - n * half });
        }

        static void DimX(PdfPage page, double x1, double x2, double fromY, double y, string text)
        {
            page.Line(new Vec2(x1, fromY - 1), new Vec2(x1, y - 1.5));
            page.Line(new Vec2(x2, fromY - 1), new Vec2(x2, y - 1.5));
            page.Line(new Vec2(x1, y), new Vec2(x2, y));
            Arrow(page, new Vec2(x1, y), new Vec2(-1, 0));
            Arrow(page, new Vec2(x2, y), new Vec2(1, 0));
            page.Text((x1 + x2) / 2, y + 0.9, 2.5, text, false, PdfAlign.Center);
        }

        static void DimY(PdfPage page, double y1, double y2, double fromX, double x, string text)
        {
            page.Line(new Vec2(fromX - 1, y1), new Vec2(x - 1.5, y1));
            page.Line(new Vec2(fromX - 1, y2), new Vec2(x - 1.5, y2));
            page.Line(new Vec2(x, y1), new Vec2(x, y2));
            Arrow(page, new Vec2(x, y1), new Vec2(0, -1));
            Arrow(page, new Vec2(x, y2), new Vec2(0, 1));
            page.Text(x - 0.9, (y1 + y2) / 2, 2.5, text, false, PdfAlign.Center, 90);
        }

        // ------------------------------------------------------------ border

        static void DrawBorder(PdfPage page, LaserConfig c, double pw, double ph)
        {
            double x0 = FrameLeft, x1 = pw - FrameOther, y0 = FrameOther, y1 = ph - FrameOther;
            double ix0 = x0 + Strip, ix1 = x1 - Strip, iy0 = y0 + Strip, iy1 = y1 - Strip;
            double cx = pw / 2, cy = ph / 2;
            Color(page, Black, 0.35);
            page.Rect(x0, y0, x1 - x0, y1 - y0, false, true);
            page.Rect(ix0, iy0, ix1 - ix0, iy1 - iy0, false, true);

            // Zones: 50 mm, centred on the sheet; numbered from the left, lettered from the top.
            List<double> cols = Divisions(x0, x1, cx), rows = Divisions(y0, y1, cy);
            Color(page, Black, 0.25);
            for (int i = 1; i < cols.Count - 1; i++)
            {
                page.Line(new Vec2(cols[i], y0), new Vec2(cols[i], iy0));
                page.Line(new Vec2(cols[i], iy1), new Vec2(cols[i], y1));
            }
            for (int i = 1; i < rows.Count - 1; i++)
            {
                page.Line(new Vec2(x0, rows[i]), new Vec2(ix0, rows[i]));
                page.Line(new Vec2(ix1, rows[i]), new Vec2(x1, rows[i]));
            }
            const double zs = 3.6, cap = zs * 0.72;
            for (int i = 0; i < cols.Count - 1; i++)
            {
                double mid = (cols[i] + cols[i + 1]) / 2;
                string n = (i + 1).ToString();
                page.Text(mid, iy1 + (Strip - cap) / 2, zs, n, false, PdfAlign.Center);
                page.Text(mid, y0 + (Strip - cap) / 2, zs, n, false, PdfAlign.Center);
            }
            for (int i = 0; i < rows.Count - 1; i++)
            {
                double mid = (rows[i] + rows[i + 1]) / 2;
                string letter = ((char)('A' + (rows.Count - 2 - i))).ToString(); // A at the top
                page.Text(x0 + Strip / 2, mid - cap / 2, zs, letter, false, PdfAlign.Center);
                page.Text(ix1 + Strip / 2, mid - cap / 2, zs, letter, false, PdfAlign.Center);
            }

            // Centring marks.
            Color(page, Black, 0.7);
            page.Line(new Vec2(cx, y1), new Vec2(cx, y1 - 10));
            page.Line(new Vec2(cx, y0), new Vec2(cx, y0 + 10));
            page.Line(new Vec2(x0, cy), new Vec2(x0 + 10, cy));
            page.Line(new Vec2(x1, cy), new Vec2(x1 - 10, cy));

            if (c.Banner.Length > 0)
            {
                Color(page, BannerRed, 0.3);
                page.Text(cx, iy1 - 5, 5, c.Banner, false, PdfAlign.Center);
            }
        }

        /// <summary>Zone boundaries from lo to hi: every 50 mm either side of the centre.</summary>
        static List<double> Divisions(double lo, double hi, double centre)
        {
            var l = new List<double> { lo };
            double first = centre - Math.Floor((centre - lo) / Zone - 1e-9) * Zone;
            for (double v = first; v < hi - 1e-9; v += Zone) if (v > lo + 1e-9) l.Add(v);
            l.Add(hi);
            return l;
        }


        static void Color(PdfPage p, Rgb c, double width)
        {
            p.Stroke(c.R, c.G, c.B, width);
            p.Fill(c.R, c.G, c.B);
        }

        static IEnumerable<PlacedText> AllText(ExportRun run)
        {
            foreach (var t in run.Labels) if (!t.Deleted) yield return t;
            if (run.Etch != null && !run.Etch.Deleted) yield return run.Etch;
        }

        static void DrawEntity(PdfPage page, DxfEntity e, Func<Vec2, Vec2> P, double s)
        {
            var line = e as DxfLine;
            var circle = e as DxfCircle;
            var arc = e as DxfArc;
            var poly = e as DxfPolyline;
            if (line != null) page.Line(P(line.A), P(line.B));
            else if (circle != null) page.Circle(P(circle.Center), circle.Radius * s);
            else if (arc != null)
            {
                double sweep = (arc.EndDeg - arc.StartDeg) % 360;
                if (sweep <= 0) sweep += 360;
                int n = Math.Max(4, (int)Math.Ceiling(sweep / 3));
                var pts = new List<Vec2>();
                for (int i = 0; i <= n; i++) pts.Add(P(arc.PointAt(arc.StartDeg + sweep * i / n)));
                page.Polyline(pts, false);
            }
            else if (poly != null)
            {
                var pts = new List<Vec2>();
                foreach (var q in poly.Points) pts.Add(P(q));
                page.Polyline(pts, poly.Closed);
            }
        }

        /// <summary>Etch text drawn with the same stroke font as the DXF. If it would print smaller
        /// than minHeight it is enlarged so it stays readable; a hole label then grows away from its
        /// hole (keeping a small gap) instead of over it.</summary>
        static void DrawEtchText(PdfPage page, PlacedText t, Func<Vec2, Vec2> P, double s, double minHeight)
        {
            double h = Math.Max(t.Height * s, minHeight);
            double w = StrokeFont.Measure(t.Text, h);
            Rect2 atZero = PlacedText.BoxAt(new Vec2(0, 0), w, h, t.RotationDeg);
            Vec2 centre = P(t.Box.Center);
            if (h > t.Height * s + 1e-9 && t.Hole != null && t.Hole.Circle != null)
            {
                Vec2 hc = P(t.Hole.Circle.Center);
                double hr = t.Hole.Circle.Radius * s;
                Vec2 u = centre - hc;
                double len = u.Length;
                u = len < 1e-9 ? new Vec2(1, 0) : u * (1 / len);
                const double gap = 0.6; // mm on paper between the hole and its label
                for (int i = 0; i < 400; i++)
                {
                    Rect2 box = new Rect2(centre.X + atZero.MinX - atZero.Center.X, centre.Y + atZero.MinY - atZero.Center.Y,
                                          centre.X + atZero.MaxX - atZero.Center.X, centre.Y + atZero.MaxY - atZero.Center.Y);
                    if (Layout2.PointRect(hc, box) - hr >= gap) break;
                    centre = centre + u * 0.05;
                }
            }
            Vec2 origin = centre - atZero.Center;
            foreach (var stroke in StrokeFont.Render(t.Text, origin, h, t.RotationDeg))
                page.Polyline(stroke, false);
        }

        // ------------------------------------------------------------ notes and hole key

        public static Dictionary<string, string> NoteTokens(ExportRun run)
        {
            Dictionary<string, string> t = run.Identity.Tokens();
            t["DXF"] = run.FileName;
            t["ETCH"] = run.Etch != null && !run.Etch.Deleted ? run.Etch.Text : run.EtchText;
            t["THK"] = Fmt.N(run.Analysis.Thickness * run.Profile.UnitScale, 2);
            return t;
        }

        /// <summary>One line per label actually on the plate: "R6  x12  6 mm reamed dowel hole (laser cut dia 5.8)".</summary>
        public static List<string> HoleKey(ExportRun run)
        {
            var count = new Dictionary<string, int>();
            var dias = new Dictionary<string, List<double>>();
            var order = new List<string>();
            foreach (var t in run.Labels)
            {
                if (t.Deleted) continue;
                if (!count.ContainsKey(t.Text)) { count[t.Text] = 0; dias[t.Text] = new List<double>(); order.Add(t.Text); }
                count[t.Text]++;
                if (t.Hole != null && !double.IsNaN(t.Hole.CutDiameter))
                {
                    double d = Math.Round(t.Hole.CutDiameter, 3);
                    if (!dias[t.Text].Contains(d)) dias[t.Text].Add(d);
                }
            }
            var lines = new List<string>();
            foreach (string label in order)
            {
                string desc = "";
                foreach (var r in run.Config.LabelTable)
                    if (string.Equals(r.Label, label, StringComparison.OrdinalIgnoreCase) && r.Description.Length > 0) { desc = r.Description; break; }
                if (desc.Length == 0 && string.Equals(label, run.Config.CountersinkLabel, StringComparison.OrdinalIgnoreCase)) desc = run.Config.CountersinkDescription;
                var ds = new List<string>();
                foreach (double d in dias[label]) ds.Add("Ø" + Fmt.N(d, 2));
                lines.Add(label + "  x" + count[label] + (desc.Length > 0 ? "  -  " + desc : "") +
                          (ds.Count > 0 ? "  (laser cut " + string.Join(" / ", ds.ToArray()) + ")" : ""));
            }
            return lines;
        }


        /// <summary>Notes and hole key in the box (left, top) - (left + width, bottom). The text size
        /// starts at 2.5 mm and shrinks until everything fits (not below 1.5 mm).</summary>
        static void DrawNotes(PdfPage page, ExportRun run, double x, double top, double width, double bottom)
        {
            LaserConfig c = run.Config;
            Dictionary<string, string> tokens = NoteTokens(run);
            List<string> key = HoleKey(run);
            double size = 2.5;
            List<List<string>> notes = null, keyLines = null;
            for (; ; size -= 0.1)
            {
                double lead = size * 1.38;
                notes = new List<List<string>>();
                int lines = 1;
                foreach (string raw in c.SheetNotes)
                {
                    bool strong = raw.StartsWith("!");
                    var l = PdfFonts.Wrap(Identity.Format(strong ? raw.Substring(1) : raw, tokens, null), size, strong, width - size * 2.4);
                    notes.Add(l);
                    lines += l.Count;
                }
                keyLines = new List<List<string>>();
                if (key.Count > 0)
                {
                    lines++;
                    foreach (string k in key)
                    {
                        var l = PdfFonts.Wrap(k, size, false, width - size * 2.4);
                        keyLines.Add(l);
                        lines += l.Count;
                    }
                }
                if (lines * lead + 1.6 <= top - bottom || size <= 1.5) break;
            }

            double ld = size * 1.38, y = top - size, indent = size * 2.4;
            Color(page, Black, 0.3);
            page.Text(x, y, size * 1.15, "LASER CUTTING NOTES", true, PdfAlign.Left);
            y -= ld + 0.8;
            for (int n = 0; n < notes.Count; n++)
            {
                bool strong = c.SheetNotes[n].StartsWith("!");
                Color(page, strong ? Red : Black, 0.3);
                for (int i = 0; i < notes[n].Count && y > bottom; i++)
                {
                    if (i == 0) page.Text(x, y, size, (n + 1) + ".", strong, PdfAlign.Left);
                    page.Text(x + indent, y, size, notes[n][i], strong, PdfAlign.Left);
                    y -= ld;
                }
            }
            if (keyLines.Count > 0 && y > bottom)
            {
                Color(page, Black, 0.3);
                page.Text(x, y, size, "HOLE LABELS (etched next to each hole, shown blue):", true, PdfAlign.Left);
                y -= ld;
                Color(page, Blue, 0.3);
                foreach (var k in keyLines)
                    for (int i = 0; i < k.Count && y > bottom; i++)
                    {
                        page.Text(x + indent, y, size, k[i], false, PdfAlign.Left);
                        y -= ld;
                    }
            }
        }

        // ------------------------------------------------------------ title block

        /// <summary>Estimated mass in kg from the net face area, thickness and the material's density.</summary>
        public static double EstimatedMass(ExportRun run)
        {
            LaserConfig c = run.Config;
            double density = c.Density;
            string mat = (run.Identity.Material ?? "").ToUpperInvariant();
            foreach (var kv in c.DensityByMaterial)
                if (kv.Key.Length > 0 && mat.Contains(kv.Key.ToUpperInvariant())) { density = kv.Value; break; }
            double k = run.Profile.UnitScale;
            double areaMm2 = Math.Abs(run.Analysis.NetArea) * k * k;
            return areaMm2 * run.Analysis.Thickness * k * density / 1e6;
        }

        /// <summary>The logo from the config folder (once per run), or null with the reason in LogoProblem.</summary>
        static PdfImage Logo(LaserConfig c)
        {
            if (c.LogoImage != null || c.LogoProblem != null || c.Logo.Length == 0) return c.LogoImage;
            string path = c.Logo;
            try
            {
                if (!Path.IsPathRooted(path))
                {
                    string dir = File.Exists(c.SourcePath) ? Path.GetDirectoryName(c.SourcePath) : null;
                    if (dir == null) { c.LogoProblem = "PDF sheet: no config file, so the logo \"" + c.Logo + "\" can't be found - company name printed instead."; return null; }
                    path = Path.Combine(dir, path);
                }
                if (!File.Exists(path)) { c.LogoProblem = "PDF sheet: logo file " + path + " not found - company name printed instead."; return null; }
                c.LogoImage = PdfImage.Load(File.ReadAllBytes(path));
            }
            catch (Exception ex)
            {
                c.LogoProblem = "PDF sheet: can't use the logo " + path + " (" + ex.Message + ") - company name printed instead.";
            }
            return c.LogoImage;
        }

        static void Label(PdfPage page, double x, double top, string text)
        {
            page.Text(x + 0.8, top - 1.6, 1.3, text, false, PdfAlign.Left);
        }

        /// <summary>Single-line value, shrunk to fit maxWidth.</summary>
        static void Value(PdfPage page, double x, double y, double size, string text, bool bold, PdfAlign align, double maxWidth)
        {
            string v = text ?? "";
            while (v.Length > 1 && size > 1.2 && PdfFonts.Width(v, size, bold) > maxWidth) size *= 0.94;
            page.Text(x, y, size, v, bold, align);
        }

        static void DrawTitleBlock(PdfPage page, ExportRun run, double L, double B, string scale, string sheet)
        {
            LaserConfig c = run.Config;
            PartIdentity id = run.Identity;
            double R = L + TitleW, T = B + TitleH;
            Color(page, Black, 0.3);
            page.Rect(L, B, TitleW, TitleH, false, true);
            foreach (double h in new[] { 32.0, 35, 40 }) page.Line(new Vec2(L, B + h), new Vec2(R, B + h));
            Color(page, Black, 0.2);
            // revision rows
            page.Line(new Vec2(L + 8, B + 35), new Vec2(L + 8, T));
            page.Line(new Vec2(L + 150, B + 35), new Vec2(L + 150, T));
            // main block columns
            foreach (double x in new[] { 31.0, 75, 168 }) page.Line(new Vec2(L + x, B), new Vec2(L + x, B + 32));
            page.Line(new Vec2(L, B + 24), new Vec2(L + 31, B + 24));
            page.Line(new Vec2(L + 12.3, B + 24), new Vec2(L + 12.3, B + 32));
            page.Line(new Vec2(L + 31, B + 8), new Vec2(L + 75, B + 8));
            page.Line(new Vec2(L + 53, B), new Vec2(L + 53, B + 8));
            foreach (double h in new[] { 8.0, 16, 24 }) page.Line(new Vec2(L + 75, B + h), new Vec2(R, B + h));
            page.Line(new Vec2(L + 130, B), new Vec2(L + 130, B + 8));

            Color(page, Black, 0.25);
            // revision history
            string rev = id.RevisionRaw.Length > 0 ? id.Revision : "-";
            page.Text(L + 4, B + 41.9, 2.4, rev, false, PdfAlign.Center);
            Value(page, L + 9, B + 41.9, 2.2, Identity.Format(c.RevisionDescription, NoteTokens(run), null), false, PdfAlign.Left, 139);
            Value(page, L + 165, B + 41.9, 2.2, id.DrawnBy, false, PdfAlign.Center, 28);
            page.Text(L + 1.2, B + 36.6, 2.6, "REV.", false, PdfAlign.Left);
            page.Text(L + 79, B + 36.6, 2.6, "DESCRIPTION", false, PdfAlign.Center);
            page.Text(L + 165, B + 36.6, 2.6, "DRAWN", false, PdfAlign.Center);
            page.Text(L + 90, B + 32.9, 1.75, "REVISION HISTORY - CURRENT REVISION SHOWN IN TOP ROW - DRAWING DATE REFERS TO LATEST REVISION", false, PdfAlign.Center);

            // projection (as on the company border) and units
            Label(page, L, B + 32, "PROJECTION:");
            Vec2 pc = new Vec2(L + 3.6, B + 26.6);
            page.Circle(pc, 2.0);
            page.Circle(pc, 1.0);
            page.Line(new Vec2(pc.X - 2.7, pc.Y), new Vec2(pc.X + 2.7, pc.Y));
            page.Line(new Vec2(pc.X, pc.Y - 2.7), new Vec2(pc.X, pc.Y + 2.7));
            page.Polyline(new List<Vec2> { new Vec2(L + 7.3, pc.Y - 1.0), new Vec2(L + 11.3, pc.Y - 2.0), new Vec2(L + 11.3, pc.Y + 2.0), new Vec2(L + 7.3, pc.Y + 1.0) }, true);
            page.Text(L + 21.6, B + 28.4, 1.35, "DIMENSIONS IN mm", false, PdfAlign.Center);
            page.Text(L + 21.6, B + 26.4, 1.35, "CONFORMS TO BS 8888", false, PdfAlign.Center);

            // general tolerances
            if (c.Tolerances.Count > 0)
            {
                double lead = Math.Min(1.65, 21.5 / c.Tolerances.Count), ty = B + 24 - 1.9;
                foreach (string raw in c.Tolerances)
                {
                    bool strong = raw.StartsWith("!");
                    string t = strong ? raw.Substring(1) : raw;
                    int bar = t.IndexOf('|');
                    string left = bar >= 0 ? t.Substring(0, bar) : t, right = bar >= 0 ? t.Substring(bar + 1) : "";
                    int pad = 0;
                    while (pad < left.Length && left[pad] == ' ') pad++;
                    page.Text(L + 1.8 + pad * 0.9, ty, 1.25, left.Trim(), strong, PdfAlign.Left);
                    if (right.Length > 0) page.Text(L + 29.5, ty, 1.25, right, strong, PdfAlign.Right);
                    ty -= lead;
                }
            }

            // legal owner: logo (or company name), address, confidentiality
            Label(page, L + 31, B + 32, "LEGAL OWNER:");
            double ocx = L + 53;
            PdfImage logo = Logo(c);
            if (logo != null)
            {
                double lw = 30, lh = lw / logo.AspectRatio;
                if (lh > 6.8) { lh = 6.8; lw = lh * logo.AspectRatio; }
                page.Image(logo, ocx - lw / 2, B + 25.6 - lh / 2, lw, lh);
            }
            else
            {
                if (c.LogoProblem != null && !run.Warnings.Contains(c.LogoProblem)) run.Warnings.Add(c.LogoProblem);
                Value(page, ocx, B + 24.4, 3.2, c.Company, true, PdfAlign.Center, 42);
            }
            double oy = B + 20.6;
            foreach (string line in c.OwnerLines)
            {
                Value(page, ocx, oy, 1.15, line, false, PdfAlign.Center, 42);
                oy -= 1.4;
            }
            if (c.Confidentiality.Length > 0)
            {
                oy -= 0.3;
                foreach (string line in PdfFonts.Wrap(c.Confidentiality, 0.85, false, 42))
                {
                    if (oy < B + 8.6) break;
                    page.Text(ocx, oy, 0.85, line, false, PdfAlign.Center);
                    oy -= 1.0;
                }
            }
            Label(page, L + 31, B + 8, "DATE:");
            Value(page, L + 32.2, B + 1.8, 2.2, run.Date, false, PdfAlign.Left, 14); // left of the centring mark
            Label(page, L + 53, B + 8, "APPROVER:");
            Value(page, L + 64, B + 1.8, 2.4, c.Approver.Length > 0 ? c.Approver : "-", false, PdfAlign.Center, 20);

            // treatments, description, material, part number
            Label(page, L + 75, B + 32, "SURFACE TREATMENT:");
            Value(page, L + 76.5, B + 25.6, 2.8, c.SurfaceTreatment, false, PdfAlign.Left, 90);
            Label(page, L + 75, B + 24, "HEAT TREATMENT:");
            Value(page, L + 76.5, B + 17.6, 2.8, c.HeatTreatment, false, PdfAlign.Left, 90);
            Label(page, L + 75, B + 16, "DESCRIPTION:");
            Value(page, L + 76.5, B + 9.4, 3.4, id.Name.Length > 0 ? id.Name : "-", false, PdfAlign.Left, 90);
            Label(page, L + 75, B + 8, "MATERIAL:");
            string thk = Fmt.N(run.Analysis.Thickness * run.Profile.UnitScale, 2) + " mm";
            Value(page, L + 76.5, B + 1.8, 2.6, id.Material.Length > 0 ? id.Material + "  -  " + thk : thk, false, PdfAlign.Left, 52);
            Label(page, L + 130, B + 8, "PART NUMBER:");
            Value(page, L + 131.2, B + 1.8, 3.8, id.Id, true, PdfAlign.Left, 35.5);

            // size, scale, sheet, mass
            Label(page, L + 168, B + 32, "SIZE:");
            page.Text(R - 1.5, B + 25.4, 3.2, sheet, false, PdfAlign.Right);
            Label(page, L + 168, B + 24, "SCALE:");
            Value(page, R - 1.5, B + 17.4, 3.2, scale, false, PdfAlign.Right, 10);
            Label(page, L + 168, B + 16, "SHEET:");
            Value(page, R - 1, B + 9.4, 3.2, "1 OF 1", false, PdfAlign.Right, 11);
            Label(page, L + 168, B + 8, "EST MASS:");
            double mass = EstimatedMass(run);
            Value(page, R - 1, B + 1.8, 2.2, mass > 0 ? Fmt.N(mass, 3) + " kg" : "-", false, PdfAlign.Right, 11);
        }

        /// <summary>"REVISION" and the revision in a triangle, outside the frame at the bottom right.</summary>
        static void DrawRevisionMark(PdfPage page, ExportRun run, double frameRight, double frameBottom)
        {
            string rev = run.Identity.RevisionRaw.Length > 0 ? run.Identity.Revision : "-";
            double baseY = frameBottom - 4.7, ax = frameRight + 3.8;
            Color(page, Black, 0.25);
            page.Polyline(new List<Vec2> { new Vec2(frameRight - 1.5, baseY), new Vec2(frameRight + 9, baseY), new Vec2(ax, frameBottom + 4.4) }, true);
            Value(page, ax, baseY + 1.2, 3.4, rev, false, PdfAlign.Center, 6);
            page.Text(frameRight - 1.8, baseY, 4.2, "REVISION", false, PdfAlign.Right);
        }
    }
}
