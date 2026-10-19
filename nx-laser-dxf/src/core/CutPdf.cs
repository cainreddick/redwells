// The DXF as a PDF, for laser shops that can't open the DXF: exactly the same
// entities at true 1:1 scale on a page just big enough for the plate. Cut geometry
// black, etching blue, each on a PDF layer named like the DXF layer, and nothing
// else on the page so the file can be imported into CAM software as it is.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;

    public static class CutPdf
    {
        /// <summary>RGB for an AutoCAD colour index (the basic colours; anything else prints black).</summary>
        public static double[] Rgb(int aci)
        {
            switch (aci)
            {
                case 1: return new double[] { 1, 0, 0 };
                case 2: return new double[] { 1, 1, 0 };
                case 3: return new double[] { 0, 1, 0 };
                case 4: return new double[] { 0, 1, 1 };
                case 5: return new double[] { 0, 0, 1 };
                case 6: return new double[] { 1, 0, 1 };
                default: return new double[] { 0, 0, 0 }; // 7 is white on black / black on white
            }
        }

        public static PdfDocument Build(ExportRun run)
        {
            LaserConfig c = run.Config;
            DxfDocument dxf = run.Profile.Doc;
            Bounds2 ext = dxf.Extents();
            double m = c.CutPdfMargin;
            var page = new PdfPage(ext.Width + 2 * m, ext.Height + 2 * m);
            var doc = new PdfDocument { Title = run.EtchText, Author = run.Identity.DrawnBy, Subject = run.FileName + " at 1:1 (mm)" };
            doc.Pages.Add(page);
            Vec2 shift = new Vec2(m - ext.MinX, m - ext.MinY);

            // One pass per DXF layer, in the DXF's layer order, so each lands in its own PDF layer.
            var layers = new List<string>();
            var colours = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (DxfLayer l in dxf.Layers) { if (!colours.ContainsKey(l.Name)) layers.Add(l.Name); colours[l.Name] = l.Color; }
            foreach (DxfEntity e in dxf.Entities) if (!colours.ContainsKey(e.Layer)) { layers.Add(e.Layer); colours[e.Layer] = 7; }

            foreach (string layer in layers)
            {
                var on = new List<DxfEntity>();
                foreach (DxfEntity e in dxf.Entities) if (string.Equals(e.Layer, layer, StringComparison.OrdinalIgnoreCase)) on.Add(e);
                if (on.Count == 0) continue;
                if (c.CutPdfLayers)
                {
                    page.BeginLayer(doc.Layers.Count);
                    doc.Layers.Add(layer);
                }
                int current = -1;
                foreach (DxfEntity e in on)
                {
                    int aci = e.Color == 256 || e.Color == 0 ? colours[layer] : e.Color;
                    if (aci != current)
                    {
                        double[] rgb = Rgb(aci);
                        page.Stroke(rgb[0], rgb[1], rgb[2], c.CutPdfLineWidth);
                        current = aci;
                    }
                    Draw(page, e, shift);
                }
                if (c.CutPdfLayers) page.EndLayer();
            }
            return doc;
        }

        static void Draw(PdfPage page, DxfEntity e, Vec2 shift)
        {
            var line = e as DxfLine;
            var circle = e as DxfCircle;
            var arc = e as DxfArc;
            var poly = e as DxfPolyline;
            var text = e as DxfText;
            if (line != null) page.Line(line.A + shift, line.B + shift);
            else if (circle != null) page.Circle(circle.Center + shift, circle.Radius);
            else if (arc != null)
            {
                double sweep = (arc.EndDeg - arc.StartDeg) % 360;
                if (sweep <= 1e-9) sweep += 360;
                page.Arc(arc.Center + shift, arc.Radius, arc.StartDeg, sweep);
            }
            else if (poly != null) page.Polyline(Shifted(poly.Points, shift), poly.Closed);
            else if (text != null)
            {
                // DXF TEXT has no outline of its own: draw it with the single-stroke etch font.
                foreach (var stroke in StrokeFont.Render(text.Value, text.Position + shift, text.Height, text.RotationDeg))
                    page.Polyline(stroke, false);
            }
        }

        static List<Vec2> Shifted(List<Vec2> pts, Vec2 shift)
        {
            var l = new List<Vec2>(pts.Count);
            foreach (var p in pts) l.Add(p + shift);
            return l;
        }
    }
}
