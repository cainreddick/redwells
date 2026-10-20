// Minimal vector PDF writer (lines, curves, rectangles, Helvetica text, PNG/JPEG images, layers).
// NX journals can't pull in a PDF library, and this is all the instruction sheet needs.
// Coordinates are millimetres from the bottom-left corner of the page.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    public enum PdfAlign { Left, Center, Right }

    public sealed class PdfPage
    {
        public readonly double WidthMm, HeightMm;
        readonly StringBuilder c = new StringBuilder();

        public PdfPage(double widthMm, double heightMm)
        {
            WidthMm = widthMm;
            HeightMm = heightMm;
            // Work in millimetres: scale user space by 72/25.4 points per mm.
            c.Append("2.83464567 0 0 2.83464567 0 0 cm\n1 J 1 j\n");
        }

        internal string Content { get { return c.ToString(); } }
        internal readonly List<PdfImage> Images = new List<PdfImage>();

        static string N(double v) { return Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture); }

        public void Stroke(double r, double g, double b, double widthMm)
        {
            c.Append(N(r)).Append(' ').Append(N(g)).Append(' ').Append(N(b)).Append(" RG ").Append(N(widthMm)).Append(" w\n");
        }

        public void Fill(double r, double g, double b)
        {
            c.Append(N(r)).Append(' ').Append(N(g)).Append(' ').Append(N(b)).Append(" rg\n");
        }

        public void Line(Vec2 a, Vec2 b)
        {
            c.Append(N(a.X)).Append(' ').Append(N(a.Y)).Append(" m ").Append(N(b.X)).Append(' ').Append(N(b.Y)).Append(" l S\n");
        }

        public void Polyline(IList<Vec2> pts, bool closed)
        {
            if (pts.Count < 2) return;
            c.Append(N(pts[0].X)).Append(' ').Append(N(pts[0].Y)).Append(" m");
            for (int i = 1; i < pts.Count; i++) c.Append(' ').Append(N(pts[i].X)).Append(' ').Append(N(pts[i].Y)).Append(" l");
            c.Append(closed ? " s\n" : " S\n");
        }

        public void Rect(double x, double y, double w, double h, bool fill, bool stroke)
        {
            if (!fill && !stroke) return;
            c.Append(N(x)).Append(' ').Append(N(y)).Append(' ').Append(N(w)).Append(' ').Append(N(h)).Append(" re ")
             .Append(fill && stroke ? "B" : fill ? "f" : "S").Append('\n');
        }

        /// <summary>Circle as four cubic Beziers.</summary>
        public void Circle(Vec2 ctr, double r)
        {
            const double k = 0.5522847498;
            double x = ctr.X, y = ctr.Y, d = r * k;
            c.Append(N(x + r)).Append(' ').Append(N(y)).Append(" m\n");
            c.Append(N(x + r)).Append(' ').Append(N(y + d)).Append(' ').Append(N(x + d)).Append(' ').Append(N(y + r)).Append(' ').Append(N(x)).Append(' ').Append(N(y + r)).Append(" c\n");
            c.Append(N(x - d)).Append(' ').Append(N(y + r)).Append(' ').Append(N(x - r)).Append(' ').Append(N(y + d)).Append(' ').Append(N(x - r)).Append(' ').Append(N(y)).Append(" c\n");
            c.Append(N(x - r)).Append(' ').Append(N(y - d)).Append(' ').Append(N(x - d)).Append(' ').Append(N(y - r)).Append(' ').Append(N(x)).Append(' ').Append(N(y - r)).Append(" c\n");
            c.Append(N(x + d)).Append(' ').Append(N(y - r)).Append(' ').Append(N(x + r)).Append(' ').Append(N(y - d)).Append(' ').Append(N(x + r)).Append(' ').Append(N(y)).Append(" c S\n");
        }

        /// <summary>Circular arc, counter-clockwise from startDeg through sweepDeg, as cubic Beziers of at most 90 degrees.</summary>
        public void Arc(Vec2 ctr, double r, double startDeg, double sweepDeg)
        {
            int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepDeg) / 90 - 1e-9));
            double step = sweepDeg / n * Math.PI / 180, a = startDeg * Math.PI / 180;
            double k = 4.0 / 3 * Math.Tan(step / 4);
            c.Append(N(ctr.X + r * Math.Cos(a))).Append(' ').Append(N(ctr.Y + r * Math.Sin(a))).Append(" m\n");
            for (int i = 0; i < n; i++)
            {
                double a0 = a + step * i, a1 = a0 + step;
                double c0 = Math.Cos(a0), s0 = Math.Sin(a0), c1 = Math.Cos(a1), s1 = Math.Sin(a1);
                c.Append(N(ctr.X + r * (c0 - k * s0))).Append(' ').Append(N(ctr.Y + r * (s0 + k * c0))).Append(' ')
                 .Append(N(ctr.X + r * (c1 + k * s1))).Append(' ').Append(N(ctr.Y + r * (s1 - k * c1))).Append(' ')
                 .Append(N(ctr.X + r * c1)).Append(' ').Append(N(ctr.Y + r * s1)).Append(" c\n");
            }
            c.Append("S\n");
        }

        /// <summary>Starts drawing on a layer (optional content group) declared in PdfDocument.Layers.</summary>
        public void BeginLayer(int index)
        {
            c.Append("/OC /OC").Append(index).Append(" BDC\n");
        }

        public void EndLayer()
        {
            c.Append("EMC\n");
        }

        /// <summary>Closed polygon filled with the fill colour.</summary>
        public void FillPolygon(IList<Vec2> pts)
        {
            if (pts.Count < 3) return;
            c.Append(N(pts[0].X)).Append(' ').Append(N(pts[0].Y)).Append(" m");
            for (int i = 1; i < pts.Count; i++) c.Append(' ').Append(N(pts[i].X)).Append(' ').Append(N(pts[i].Y)).Append(" l");
            c.Append(" h f\n");
        }

        /// <summary>Draws the image stretched to the w x h box with its bottom-left corner at (x, y).</summary>
        public void Image(PdfImage img, double x, double y, double w, double h)
        {
            int i = Images.IndexOf(img);
            if (i < 0) { i = Images.Count; Images.Add(img); }
            c.Append("q ").Append(N(w)).Append(" 0 0 ").Append(N(h)).Append(' ').Append(N(x)).Append(' ').Append(N(y)).Append(" cm /Im").Append(i).Append(" Do Q\n");
        }

        /// <summary>Single-line text; (x, y) is on the baseline. Size is the font size in mm.</summary>
        public void Text(double x, double y, double sizeMm, string text, bool bold, PdfAlign align)
        {
            Text(x, y, sizeMm, text, bold, align, 0);
        }

        /// <summary>Text turned rotationDeg anticlockwise about (x, y); the alignment runs along the text.</summary>
        public void Text(double x, double y, double sizeMm, string text, bool bold, PdfAlign align, double rotationDeg)
        {
            double w = PdfFonts.Width(text, sizeMm, bold);
            double shift = align == PdfAlign.Center ? w / 2 : align == PdfAlign.Right ? w : 0;
            double a = rotationDeg * Math.PI / 180, cs = Math.Cos(a), sn = Math.Sin(a);
            x -= shift * cs;
            y -= shift * sn;
            c.Append("BT /").Append(bold ? "F2" : "F1").Append(' ').Append(N(sizeMm)).Append(" Tf ");
            if (rotationDeg == 0) c.Append(N(x)).Append(' ').Append(N(y)).Append(" Td");
            else c.Append(N(cs)).Append(' ').Append(N(sn)).Append(' ').Append(N(-sn)).Append(' ').Append(N(cs)).Append(' ').Append(N(x)).Append(' ').Append(N(y)).Append(" Tm");
            c.Append(" (").Append(Escape(text)).Append(") Tj ET\n");
        }

        static string Escape(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s ?? "")
            {
                if (ch == '\\' || ch == '(' || ch == ')') sb.Append('\\').Append(ch);
                else if (ch < 32) sb.Append(' ');
                else sb.Append(ch);
            }
            return sb.ToString();
        }
    }

    /// <summary>Helvetica / Helvetica-Bold advance widths (standard AFM, 1/1000 em) for text layout.</summary>
    public static class PdfFonts
    {
        static readonly short[] Regular =
        {
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
            1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
            333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
            556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
        };

        static readonly short[] Bold =
        {
            278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
            975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
            333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
            611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584
        };

        public static double Width(string s, double sizeMm, bool bold)
        {
            short[] t = bold ? Bold : Regular;
            double units = 0;
            foreach (char ch in s ?? "")
            {
                if (ch >= 32 && ch <= 126) units += t[ch - 32];
                else if (ch == 'Ø' || ch == 'ø') units += 778;
                else units += 556;
            }
            return units / 1000.0 * sizeMm;
        }

        /// <summary>Greedy word wrap to the given width.</summary>
        public static List<string> Wrap(string text, double sizeMm, bool bold, double maxWidthMm)
        {
            var lines = new List<string>();
            string line = "";
            foreach (string word in (text ?? "").Split(' '))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Width(candidate, sizeMm, bold) > maxWidthMm)
                {
                    lines.Add(line);
                    line = word;
                }
                else line = candidate;
            }
            if (line.Length > 0 || lines.Count == 0) lines.Add(line);
            return lines;
        }
    }

    public sealed class PdfDocument
    {
        public readonly List<PdfPage> Pages = new List<PdfPage>();
        public string Title = "", Author = "", Subject = "";
        /// <summary>Layer names (PDF optional content groups), referenced by index from PdfPage.BeginLayer.</summary>
        public readonly List<string> Layers = new List<string>();

        static readonly Encoding Win1252 = Encoding.GetEncoding(1252);

        public byte[] Save()
        {
            var ms = new MemoryStream();
            var offsets = new List<long>();
            Action<string> write = delegate (string s) { byte[] b = Win1252.GetBytes(s); ms.Write(b, 0, b.Length); };

            // Object numbers: 1 catalog, 2 pages, 3 Helvetica, 4 Helvetica-Bold, 5 info, then page/content pairs,
            // then layers, then each page's images (an image with transparency is followed by its soft mask).
            int firstLayer = 6 + Pages.Count * 2;
            int n = 5 + Pages.Count * 2 + Layers.Count;
            var imageObj = new List<int>[Pages.Count];
            for (int i = 0; i < Pages.Count; i++)
            {
                imageObj[i] = new List<int>();
                foreach (PdfImage img in Pages[i].Images)
                {
                    imageObj[i].Add(n + 1);
                    n += img.Alpha != null ? 2 : 1;
                }
            }
            var objects = new string[n + 1];
            var binary = new byte[n + 1][];
            for (int i = 0; i < Pages.Count; i++)
                for (int k = 0; k < Pages[i].Images.Count; k++)
                {
                    PdfImage img = Pages[i].Images[k];
                    int o = imageObj[i][k];
                    objects[o] = "<< /Type /XObject /Subtype /Image /Width " + img.Width + " /Height " + img.Height + " /ColorSpace " + img.ColorSpace +
                                 " /BitsPerComponent 8 /Filter " + img.Filter + (img.Decode != null ? " /Decode " + img.Decode : "") +
                                 (img.Alpha != null ? " /SMask " + (o + 1) + " 0 R" : "") + " /Length " + img.Data.Length + " >>";
                    binary[o] = img.Data;
                    if (img.Alpha != null)
                    {
                        objects[o + 1] = "<< /Type /XObject /Subtype /Image /Width " + img.Width + " /Height " + img.Height +
                                         " /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /Length " + img.Alpha.Length + " >>";
                        binary[o + 1] = img.Alpha;
                    }
                }
            var kids = new StringBuilder();
            for (int i = 0; i < Pages.Count; i++) kids.Append(6 + i * 2).Append(" 0 R ");
            var ocgs = new StringBuilder();
            var props = new StringBuilder();
            for (int i = 0; i < Layers.Count; i++)
            {
                objects[firstLayer + i] = "<< /Type /OCG /Name (" + Lit(Layers[i]) + ") >>";
                ocgs.Append(firstLayer + i).Append(" 0 R ");
                props.Append("/OC").Append(i).Append(' ').Append(firstLayer + i).Append(" 0 R ");
            }
            string ocgList = ocgs.ToString().Trim();
            objects[1] = Layers.Count == 0 ? "<< /Type /Catalog /Pages 2 0 R >>"
                : "<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [" + ocgList + "] /D << /Order [" + ocgList + "] /ON [" + ocgList + "] >> >> >>";
            objects[2] = "<< /Type /Pages /Kids [" + kids.ToString().Trim() + "] /Count " + Pages.Count + " >>";
            objects[3] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";
            objects[4] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>";
            objects[5] = "<< /Title (" + Lit(Title) + ") /Author (" + Lit(Author) + ") /Subject (" + Lit(Subject) +
                         ") /Producer (Laser DXF journal) /CreationDate (D:" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ") >>";
            for (int i = 0; i < Pages.Count; i++)
            {
                PdfPage p = Pages[i];
                double w = p.WidthMm * 72 / 25.4, h = p.HeightMm * 72 / 25.4;
                objects[6 + i * 2] = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + w.ToString("0.##", CultureInfo.InvariantCulture) + " " +
                                     h.ToString("0.##", CultureInfo.InvariantCulture) + "] /Resources << /Font << /F1 3 0 R /F2 4 0 R >>" +
                                     (Layers.Count > 0 ? " /Properties << " + props.ToString().Trim() + " >>" : "") + XObjects(imageObj[i]) + " >> /Contents " + (7 + i * 2) + " 0 R >>";
                string content = p.Content;
                objects[7 + i * 2] = "<< /Length " + Win1252.GetByteCount(content) + " >>\nstream\n" + content + "endstream";
            }

            write((Layers.Count > 0 ? "%PDF-1.5" : "%PDF-1.4") + "\n%âãÏÓ\n"); // layers need 1.5
            for (int i = 1; i <= n; i++)
            {
                offsets.Add(ms.Position);
                if (binary[i] == null) write(i + " 0 obj\n" + objects[i] + "\nendobj\n");
                else
                {
                    write(i + " 0 obj\n" + objects[i] + "\nstream\n");
                    ms.Write(binary[i], 0, binary[i].Length);
                    write("\nendstream\nendobj\n");
                }
            }
            long xref = ms.Position;
            var x = new StringBuilder();
            x.Append("xref\n0 ").Append(n + 1).Append("\n0000000000 65535 f \n");
            foreach (long off in offsets) x.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            x.Append("trailer\n<< /Size ").Append(n + 1).Append(" /Root 1 0 R /Info 5 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            write(x.ToString());
            return ms.ToArray();
        }

        static string XObjects(List<int> objs)
        {
            if (objs.Count == 0) return "";
            var sb = new StringBuilder(" /XObject << ");
            for (int k = 0; k < objs.Count; k++) sb.Append("/Im").Append(k).Append(' ').Append(objs[k]).Append(" 0 R ");
            return sb.Append(">>").ToString();
        }

        static string Lit(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }
    }
}
