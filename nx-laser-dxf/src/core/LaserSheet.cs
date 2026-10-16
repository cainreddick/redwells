// The PDF instruction sheet that goes to the laser shop with the DXF: the plate as cut
// (etching in blue, exactly as in the DXF), the etching instruction, a hole key, and a
// title block. Landscape A4 or A3 with a border.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;

    public static class LaserSheet
    {
        const double Margin = 10;          // page edge to border
        const double BandHeight = 62;      // notes + title block band at the bottom
        static readonly double[] Scales = { 5, 2, 1, 0.5, 0.2, 0.1, 0.05, 0.02 }; // paper mm per model mm

        sealed class Rgb
        {
            public double R, G, B;
            public Rgb(double r, double g, double b) { R = r; G = g; B = b; }
        }

        static readonly Rgb Black = new Rgb(0, 0, 0);
        static readonly Rgb Blue = new Rgb(0, 0.25, 0.85);
        static readonly Rgb Red = new Rgb(0.8, 0, 0);
        static readonly Rgb Grey = new Rgb(0.45, 0.45, 0.45);

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

        static void ViewArea(double pw, double ph, out double x, out double y, out double w, out double h)
        {
            x = Margin + 6;
            y = Margin + BandHeight + 10;
            w = pw - 2 * Margin - 12;
            h = ph - 2 * Margin - BandHeight - 22;
        }

        /// <summary>A4, unless the plate would be drawn smaller than 1:1 there and A3 shows it larger.</summary>
        public static void ChooseSheet(ExportRun run, out string name, out double pw, out double ph, out double scale)
        {
            string want = run.Config.SheetSize.ToUpperInvariant();
            double w = run.Profile.Width, h = run.Profile.Height;
            double ax, ay, aw, ah;
            ViewArea(297, 210, out ax, out ay, out aw, out ah);
            double s4 = FitScale(w, h, aw, ah);
            ViewArea(420, 297, out ax, out ay, out aw, out ah);
            double s3 = FitScale(w, h, aw, ah);
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

            // Border.
            Color(page, Black, 0.5);
            page.Rect(Margin, Margin, pw - 2 * Margin, ph - 2 * Margin, false, true);

            // ---------- the plate ----------
            double vx, vy, vw, vh;
            ViewArea(pw, ph, out vx, out vy, out vw, out vh);
            double ox = vx + (vw - run.Profile.Width * s) / 2, oy = vy + (vh - run.Profile.Height * s) / 2;
            Func<Vec2, Vec2> P = delegate (Vec2 m) { return new Vec2(ox + m.X * s, oy + m.Y * s); };

            page.Text(vx, ph - Margin - 7, 3.2, "VIEW FROM THE ETCH FACE   -   SCALE " + ScaleText(s) + "   -   DO NOT SCALE: USE THE DXF FOR ALL GEOMETRY", true, PdfAlign.Left);

            Color(page, Black, 0.35);
            foreach (DxfEntity e in run.Profile.CutEntities) DrawEntity(page, e, P, s);

            Color(page, Blue, 0.25);
            foreach (PlacedText t in AllText(run)) DrawEtchText(page, t, P, s, c.SheetMinTextHeight);

            // ---------- bottom band: notes (left) and title block (right) ----------
            double titleW = pw >= 400 ? 175 : 140;
            double bandY = Margin, bandX = Margin, bandW = pw - 2 * Margin;
            Color(page, Black, 0.5);
            page.Line(new Vec2(bandX, bandY + BandHeight), new Vec2(bandX + bandW, bandY + BandHeight));
            double tbX = bandX + bandW - titleW;
            page.Line(new Vec2(tbX, bandY), new Vec2(tbX, bandY + BandHeight));

            DrawNotes(page, run, bandX + 3, bandY + BandHeight - 5, tbX - bandX - 6, bandY + 2);
            DrawTitleBlock(page, run, tbX, bandY, titleW, BandHeight, ScaleText(s), sheetName);
            return doc;
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

        static void DrawNotes(PdfPage page, ExportRun run, double x, double top, double width, double bottom)
        {
            LaserConfig c = run.Config;
            const double size = 2.6, lead = 3.6;
            double y = top;
            Color(page, Black, 0.3);
            page.Text(x, y, 3.2, "LASER CUTTING NOTES", true, PdfAlign.Left);
            y -= lead + 1.2;

            Dictionary<string, string> tokens = NoteTokens(run);
            int n = 1;
            foreach (string raw in c.SheetNotes)
            {
                bool strong = raw.StartsWith("!");
                string text = Identity.Format(strong ? raw.Substring(1) : raw, tokens, null);
                Color(page, strong ? Red : Black, 0.3);
                string num = n++ + ".";
                List<string> lines = PdfFonts.Wrap(text, size, strong, width - 6);
                for (int i = 0; i < lines.Count && y > bottom + 2; i++)
                {
                    if (i == 0) page.Text(x, y, size, num, strong, PdfAlign.Left);
                    page.Text(x + 6, y, size, lines[i], strong, PdfAlign.Left);
                    y -= lead;
                }
            }

            List<string> key = HoleKey(run);
            if (key.Count > 0 && y > bottom + 2)
            {
                y -= 1;
                Color(page, Black, 0.3);
                page.Text(x, y, size, "HOLE LABELS (etched next to each hole, shown blue):", true, PdfAlign.Left);
                y -= lead;
                foreach (string k in key)
                {
                    if (y <= bottom + 2) { page.Text(x + 6, y + lead - 0.01, size, "...", false, PdfAlign.Left); break; }
                    Color(page, Blue, 0.3);
                    page.Text(x + 6, y, size, k, false, PdfAlign.Left);
                    y -= lead;
                }
            }
        }

        // ------------------------------------------------------------ title block

        static void DrawTitleBlock(PdfPage page, ExportRun run, double x, double y, double w, double h, string scale, string sheet)
        {
            LaserConfig c = run.Config;
            PartIdentity id = run.Identity;
            double rowH = h / 6;
            double half = w / 2;
            Color(page, Black, 0.3);
            for (int i = 1; i < 6; i++) page.Line(new Vec2(x, y + i * rowH), new Vec2(x + w, y + i * rowH));

            // Row 6 (top): company + sheet title
            double r6 = y + 5 * rowH;
            page.Line(new Vec2(x + half * 0.7, r6), new Vec2(x + half * 0.7, r6 + rowH));
            page.Text(x + 2, r6 + rowH / 2 - 1.6, 4.5, c.Company.Length > 0 ? c.Company : " ", true, PdfAlign.Left);
            page.Text(x + half * 0.7 + 2, r6 + rowH / 2 - 1.3, 3.6, c.SheetTitle, true, PdfAlign.Left);

            Cell(page, x, y + 4 * rowH, w, rowH, "DESCRIPTION", id.Name);
            Cell(page, x, y + 3 * rowH, half * 1.3, rowH, "PART No", id.Id, 4.2);
            Cell(page, x + half * 1.3, y + 3 * rowH, w - half * 1.3, rowH, "REV", id.RevisionRaw.Length > 0 ? id.Revision : "-", 4.2);
            Cell(page, x, y + 2 * rowH, half * 1.3, rowH, "MATERIAL", id.Material.Length > 0 ? id.Material : "-");
            Cell(page, x + half * 1.3, y + 2 * rowH, w - half * 1.3, rowH, "THICKNESS", Fmt.N(run.Analysis.Thickness * run.Profile.UnitScale, 2) + " mm");
            double third = w / 3;
            Cell(page, x, y + rowH, third, rowH, "QTY", "");
            Cell(page, x + third, y + rowH, third, rowH, "SCALE", scale);
            Cell(page, x + 2 * third, y + rowH, third, rowH, "SHEET", sheet);
            Cell(page, x, y, third, rowH, "DRAWN", id.DrawnBy);
            Cell(page, x + third, y, third, rowH, "DATE", run.Date);
            Cell(page, x + 2 * third, y, third, rowH, "DXF FILE", run.FileName, 2.2);
        }

        static void Cell(PdfPage page, double x, double y, double w, double h, string label, string value)
        {
            Cell(page, x, y, w, h, label, value, 3.0);
        }

        static void Cell(PdfPage page, double x, double y, double w, double h, string label, string value, double size)
        {
            page.Line(new Vec2(x, y), new Vec2(x, y + h));
            page.Line(new Vec2(x + w, y), new Vec2(x + w, y + h));
            Color(page, Grey, 0.3);
            page.Text(x + 1.5, y + h - 2.6, 1.9, label, false, PdfAlign.Left);
            Color(page, Black, 0.3);
            string v = value ?? "";
            while (v.Length > 1 && size > 1.2 && PdfFonts.Width(v, size, true) > w - 3) size *= 0.92; // shrink long values to fit
            page.Text(x + 1.5, y + 1.6, size, v, true, PdfAlign.Left);
        }
    }
}
