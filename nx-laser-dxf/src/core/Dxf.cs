// Minimal DXF document model and ASCII writer (R12 / R2000).
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public enum DxfVersion { R12, R2000 }

    public sealed class DxfLayer
    {
        public string Name;
        public int Color;

        public DxfLayer(string name, int color) { Name = name; Color = color; }
    }

    public abstract class DxfEntity
    {
        public string Layer = "0";
        /// <summary>ACI colour; 256 = BYLAYER.</summary>
        public int Color = 256;

        public abstract void AddBounds(ref Bounds2 b);
    }

    public sealed class DxfLine : DxfEntity
    {
        public Vec2 A, B;

        public override void AddBounds(ref Bounds2 b) { b.Add(A); b.Add(B); }
    }

    public sealed class DxfCircle : DxfEntity
    {
        public Vec2 Center;
        public double Radius;

        public override void AddBounds(ref Bounds2 b)
        {
            b.Add(new Vec2(Center.X - Radius, Center.Y - Radius));
            b.Add(new Vec2(Center.X + Radius, Center.Y + Radius));
        }
    }

    /// <summary>Arc, counter-clockwise from StartDeg to EndDeg.</summary>
    public sealed class DxfArc : DxfEntity
    {
        public Vec2 Center;
        public double Radius, StartDeg, EndDeg;

        public Vec2 PointAt(double deg)
        {
            double r = deg * Math.PI / 180;
            return new Vec2(Center.X + Radius * Math.Cos(r), Center.Y + Radius * Math.Sin(r));
        }

        public override void AddBounds(ref Bounds2 b)
        {
            b.Add(PointAt(StartDeg));
            b.Add(PointAt(EndDeg));
            for (int k = 0; k < 4; k++)
                if (EdgeGeom2.AngleInCcwRange(k * 90.0, StartDeg, EndDeg)) b.Add(PointAt(k * 90.0));
        }
    }

    public sealed class DxfPolyline : DxfEntity
    {
        public List<Vec2> Points = new List<Vec2>();
        public bool Closed;

        public override void AddBounds(ref Bounds2 b) { foreach (var p in Points) b.Add(p); }
    }

    /// <summary>Single-line text; Position is the left end of the baseline.</summary>
    public sealed class DxfText : DxfEntity
    {
        public Vec2 Position;
        public double Height, RotationDeg;
        public string Value = "";
        /// <summary>Approximate width used only for extents.</summary>
        public double Width;

        public override void AddBounds(ref Bounds2 b)
        {
            double r = RotationDeg * Math.PI / 180;
            Vec2 u = new Vec2(Math.Cos(r), Math.Sin(r)), v = new Vec2(-Math.Sin(r), Math.Cos(r));
            b.Add(Position);
            b.Add(Position + u * Width);
            b.Add(Position + v * Height);
            b.Add(Position + u * Width + v * Height);
        }
    }

    public sealed class DxfDocument
    {
        public readonly List<DxfLayer> Layers = new List<DxfLayer>();
        public readonly List<DxfEntity> Entities = new List<DxfEntity>();

        public Bounds2 Extents()
        {
            Bounds2 b = Bounds2.Empty;
            foreach (var e in Entities) e.AddBounds(ref b);
            if (b.IsEmpty)
            {
                b.MinX = b.MinY = 0;
                b.MaxX = b.MaxY = 1;
            }
            return b;
        }
    }

    public static class DxfWriter
    {
        sealed class W
        {
            public readonly StringBuilder Sb = new StringBuilder();
            int handle = 0x30;

            public string NewHandle() { return (handle++).ToString("X"); }
            public string Seed { get { return handle.ToString("X"); } }

            public void G(int code, string value)
            {
                Sb.Append(code.ToString(Fmt.Inv).PadLeft(3)).Append("\r\n").Append(value).Append("\r\n");
            }

            public void G(int code, int value) { G(code, value.ToString(Fmt.Inv)); }

            public void G(int code, double value)
            {
                double r = Math.Round(value, 6);
                if (r == 0) r = 0;
                G(code, r.ToString("0.0#####", Fmt.Inv));
            }

            public void Pt(int code, Vec2 p)
            {
                G(code, p.X);
                G(code + 10, p.Y);
                G(code + 20, 0.0);
            }
        }

        public static string Write(DxfDocument doc, DxfVersion version)
        {
            return version == DxfVersion.R12 ? WriteR12(doc) : WriteR2000(doc);
        }

        static string Clean(string s)
        {
            // DXF group values are single lines; keep the code page simple.
            var sb = new StringBuilder();
            foreach (char c in s ?? "") sb.Append(c < 32 ? ' ' : c);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ R12

        static string WriteR12(DxfDocument doc)
        {
            var w = new W();
            Bounds2 ext = doc.Extents();

            w.G(0, "SECTION"); w.G(2, "HEADER");
            w.G(9, "$ACADVER"); w.G(1, "AC1009");
            w.G(9, "$INSBASE"); w.Pt(10, new Vec2(0, 0));
            w.G(9, "$EXTMIN"); w.Pt(10, new Vec2(ext.MinX, ext.MinY));
            w.G(9, "$EXTMAX"); w.Pt(10, new Vec2(ext.MaxX, ext.MaxY));
            w.G(9, "$LIMMIN"); w.G(10, 0.0); w.G(20, 0.0);
            w.G(9, "$LIMMAX"); w.G(10, ext.MaxX); w.G(20, ext.MaxY);
            w.G(0, "ENDSEC");

            w.G(0, "SECTION"); w.G(2, "TABLES");
            w.G(0, "TABLE"); w.G(2, "LTYPE"); w.G(70, 1);
            w.G(0, "LTYPE"); w.G(2, "CONTINUOUS"); w.G(70, 0); w.G(3, "Solid line"); w.G(72, 65); w.G(73, 0); w.G(40, 0.0);
            w.G(0, "ENDTAB");
            w.G(0, "TABLE"); w.G(2, "LAYER"); w.G(70, doc.Layers.Count + 1);
            WriteLayerR12(w, "0", 7);
            foreach (var l in doc.Layers) WriteLayerR12(w, l.Name, l.Color);
            w.G(0, "ENDTAB");
            w.G(0, "TABLE"); w.G(2, "STYLE"); w.G(70, 1);
            w.G(0, "STYLE"); w.G(2, "STANDARD"); w.G(70, 0); w.G(40, 0.0); w.G(41, 1.0); w.G(50, 0.0); w.G(71, 0); w.G(42, 2.5); w.G(3, "txt"); w.G(4, "");
            w.G(0, "ENDTAB");
            w.G(0, "ENDSEC");

            w.G(0, "SECTION"); w.G(2, "BLOCKS"); w.G(0, "ENDSEC");

            w.G(0, "SECTION"); w.G(2, "ENTITIES");
            foreach (var e in doc.Entities)
            {
                var line = e as DxfLine;
                var circle = e as DxfCircle;
                var arc = e as DxfArc;
                var poly = e as DxfPolyline;
                var text = e as DxfText;
                if (line != null)
                {
                    Common12(w, "LINE", e);
                    w.Pt(10, line.A);
                    w.Pt(11, line.B);
                }
                else if (circle != null)
                {
                    Common12(w, "CIRCLE", e);
                    w.Pt(10, circle.Center);
                    w.G(40, circle.Radius);
                }
                else if (arc != null)
                {
                    Common12(w, "ARC", e);
                    w.Pt(10, arc.Center);
                    w.G(40, arc.Radius);
                    w.G(50, arc.StartDeg);
                    w.G(51, arc.EndDeg);
                }
                else if (poly != null)
                {
                    Common12(w, "POLYLINE", e);
                    w.G(66, 1);
                    w.Pt(10, new Vec2(0, 0));
                    w.G(70, poly.Closed ? 1 : 0);
                    foreach (var p in poly.Points)
                    {
                        Common12(w, "VERTEX", e);
                        w.Pt(10, p);
                    }
                    w.G(0, "SEQEND");
                    w.G(8, e.Layer);
                }
                else if (text != null)
                {
                    Common12(w, "TEXT", e);
                    w.Pt(10, text.Position);
                    w.G(40, text.Height);
                    w.G(1, Clean(text.Value));
                    if (text.RotationDeg != 0) w.G(50, text.RotationDeg);
                    w.G(7, "STANDARD");
                }
            }
            w.G(0, "ENDSEC");
            w.G(0, "EOF");
            return w.Sb.ToString();
        }

        static void WriteLayerR12(W w, string name, int color)
        {
            w.G(0, "LAYER"); w.G(2, name); w.G(70, 0); w.G(62, color); w.G(6, "CONTINUOUS");
        }

        static void Common12(W w, string type, DxfEntity e)
        {
            w.G(0, type);
            w.G(8, e.Layer);
            if (e.Color != 256) w.G(62, e.Color);
        }

        // ---------------------------------------------------------------- R2000

        static string WriteR2000(DxfDocument doc)
        {
            var body = new W();          // everything after the header
            Bounds2 ext = doc.Extents();

            // Fixed object handles.
            const string rootDict = "C", groupDict = "D";
            string hVport = body.NewHandle(), hVportRec = body.NewHandle();
            string hLtype = body.NewHandle(), hLtByBlock = body.NewHandle(), hLtByLayer = body.NewHandle(), hLtCont = body.NewHandle();
            string hLayer = body.NewHandle();
            string hStyle = body.NewHandle(), hStyleStd = body.NewHandle();
            string hView = body.NewHandle(), hUcs = body.NewHandle();
            string hAppid = body.NewHandle(), hAppAcad = body.NewHandle();
            string hDimstyle = body.NewHandle(), hDimStd = body.NewHandle();
            string hBlkRec = body.NewHandle(), hMs = body.NewHandle(), hPs = body.NewHandle();

            body.G(0, "SECTION"); body.G(2, "CLASSES"); body.G(0, "ENDSEC");
            body.G(0, "SECTION"); body.G(2, "TABLES");

            Table(body, "VPORT", hVport, 1);
            Record(body, "VPORT", hVportRec, hVport, "AcDbViewportTableRecord");
            body.G(2, "*ACTIVE"); body.G(70, 0);
            body.G(10, 0.0); body.G(20, 0.0); body.G(11, 1.0); body.G(21, 1.0);
            body.G(12, (ext.MinX + ext.MaxX) / 2); body.G(22, (ext.MinY + ext.MaxY) / 2);
            body.G(13, 0.0); body.G(23, 0.0); body.G(14, 10.0); body.G(24, 10.0); body.G(15, 10.0); body.G(25, 10.0);
            body.G(16, 0.0); body.G(26, 0.0); body.G(36, 1.0); body.G(17, 0.0); body.G(27, 0.0); body.G(37, 0.0);
            body.G(40, Math.Max(ext.Height, ext.Width) * 1.1); body.G(41, 1.5); body.G(42, 50.0); body.G(43, 0.0); body.G(44, 0.0);
            body.G(50, 0.0); body.G(51, 0.0); body.G(71, 0); body.G(72, 100); body.G(73, 1); body.G(74, 3);
            body.G(75, 0); body.G(76, 0); body.G(77, 0); body.G(78, 0);
            body.G(0, "ENDTAB");

            Table(body, "LTYPE", hLtype, 3);
            foreach (var lt in new[] { new[] { hLtByBlock, "ByBlock", "" }, new[] { hLtByLayer, "ByLayer", "" }, new[] { hLtCont, "Continuous", "Solid line" } })
            {
                Record(body, "LTYPE", lt[0], hLtype, "AcDbLinetypeTableRecord");
                body.G(2, lt[1]); body.G(70, 0); body.G(3, lt[2]); body.G(72, 65); body.G(73, 0); body.G(40, 0.0);
            }
            body.G(0, "ENDTAB");

            Table(body, "LAYER", hLayer, doc.Layers.Count + 1);
            var layers = new List<DxfLayer> { new DxfLayer("0", 7) };
            layers.AddRange(doc.Layers);
            foreach (var l in layers)
            {
                Record(body, "LAYER", body.NewHandle(), hLayer, "AcDbLayerTableRecord");
                body.G(2, l.Name); body.G(70, 0); body.G(62, l.Color); body.G(6, "Continuous");
            }
            body.G(0, "ENDTAB");

            Table(body, "STYLE", hStyle, 1);
            Record(body, "STYLE", hStyleStd, hStyle, "AcDbTextStyleTableRecord");
            body.G(2, "Standard"); body.G(70, 0); body.G(40, 0.0); body.G(41, 1.0); body.G(50, 0.0); body.G(71, 0); body.G(42, 2.5);
            body.G(3, "txt"); body.G(4, "");
            body.G(0, "ENDTAB");

            Table(body, "VIEW", hView, 0); body.G(0, "ENDTAB");
            Table(body, "UCS", hUcs, 0); body.G(0, "ENDTAB");

            Table(body, "APPID", hAppid, 1);
            Record(body, "APPID", hAppAcad, hAppid, "AcDbRegAppTableRecord");
            body.G(2, "ACAD"); body.G(70, 0);
            body.G(0, "ENDTAB");

            Table(body, "DIMSTYLE", hDimstyle, 1);
            body.G(100, "AcDbDimStyleTable"); body.G(71, 0);
            body.G(0, "DIMSTYLE"); body.G(105, hDimStd); body.G(330, hDimstyle);
            body.G(100, "AcDbSymbolTableRecord"); body.G(100, "AcDbDimStyleTableRecord");
            body.G(2, "Standard"); body.G(70, 0);
            body.G(0, "ENDTAB");

            Table(body, "BLOCK_RECORD", hBlkRec, 2);
            Record(body, "BLOCK_RECORD", hMs, hBlkRec, "AcDbBlockTableRecord"); body.G(2, "*Model_Space");
            Record(body, "BLOCK_RECORD", hPs, hBlkRec, "AcDbBlockTableRecord"); body.G(2, "*Paper_Space");
            body.G(0, "ENDTAB");
            body.G(0, "ENDSEC");

            body.G(0, "SECTION"); body.G(2, "BLOCKS");
            foreach (var blk in new[] { new[] { hMs, "*Model_Space", "0" }, new[] { hPs, "*Paper_Space", "1" } })
            {
                body.G(0, "BLOCK"); body.G(5, body.NewHandle()); body.G(330, blk[0]); body.G(100, "AcDbEntity");
                if (blk[2] == "1") body.G(67, 1);
                body.G(8, "0"); body.G(100, "AcDbBlockBegin"); body.G(2, blk[1]); body.G(70, 0);
                body.Pt(10, new Vec2(0, 0)); body.G(3, blk[1]); body.G(1, "");
                body.G(0, "ENDBLK"); body.G(5, body.NewHandle()); body.G(330, blk[0]); body.G(100, "AcDbEntity");
                if (blk[2] == "1") body.G(67, 1);
                body.G(8, "0"); body.G(100, "AcDbBlockEnd");
            }
            body.G(0, "ENDSEC");

            body.G(0, "SECTION"); body.G(2, "ENTITIES");
            foreach (var e in doc.Entities) Entity2000(body, e, hMs);
            body.G(0, "ENDSEC");

            body.G(0, "SECTION"); body.G(2, "OBJECTS");
            body.G(0, "DICTIONARY"); body.G(5, rootDict); body.G(330, "0"); body.G(100, "AcDbDictionary"); body.G(281, 1);
            body.G(3, "ACAD_GROUP"); body.G(350, groupDict);
            body.G(0, "DICTIONARY"); body.G(5, groupDict); body.G(330, rootDict); body.G(100, "AcDbDictionary"); body.G(281, 1);
            body.G(0, "ENDSEC");
            body.G(0, "EOF");

            var head = new W();
            head.G(0, "SECTION"); head.G(2, "HEADER");
            head.G(9, "$ACADVER"); head.G(1, "AC1015");
            head.G(9, "$DWGCODEPAGE"); head.G(3, "ANSI_1252");
            head.G(9, "$INSBASE"); head.Pt(10, new Vec2(0, 0));
            head.G(9, "$EXTMIN"); head.Pt(10, new Vec2(ext.MinX, ext.MinY));
            head.G(9, "$EXTMAX"); head.Pt(10, new Vec2(ext.MaxX, ext.MaxY));
            head.G(9, "$LIMMIN"); head.G(10, 0.0); head.G(20, 0.0);
            head.G(9, "$LIMMAX"); head.G(10, ext.MaxX); head.G(20, ext.MaxY);
            head.G(9, "$LUNITS"); head.G(70, 2);
            head.G(9, "$INSUNITS"); head.G(70, 4);     // millimetres
            head.G(9, "$MEASUREMENT"); head.G(70, 1);  // metric
            head.G(9, "$HANDSEED"); head.G(5, body.Seed);
            head.G(0, "ENDSEC");
            return head.Sb.ToString() + body.Sb.ToString();
        }

        static void Table(W w, string name, string handle, int count)
        {
            w.G(0, "TABLE"); w.G(2, name); w.G(5, handle); w.G(330, "0");
            w.G(100, "AcDbSymbolTable"); w.G(70, count);
        }

        static void Record(W w, string type, string handle, string owner, string subclass)
        {
            w.G(0, type); w.G(5, handle); w.G(330, owner);
            w.G(100, "AcDbSymbolTableRecord"); w.G(100, subclass);
        }

        static void Entity2000(W w, DxfEntity e, string owner)
        {
            var line = e as DxfLine;
            var circle = e as DxfCircle;
            var arc = e as DxfArc;
            var poly = e as DxfPolyline;
            var text = e as DxfText;
            string type = line != null ? "LINE" : circle != null ? "CIRCLE" : arc != null ? "ARC" : poly != null ? "LWPOLYLINE" : "TEXT";
            w.G(0, type); w.G(5, w.NewHandle()); w.G(330, owner);
            w.G(100, "AcDbEntity"); w.G(8, e.Layer);
            if (e.Color != 256) w.G(62, e.Color);
            if (line != null)
            {
                w.G(100, "AcDbLine");
                w.Pt(10, line.A);
                w.Pt(11, line.B);
            }
            else if (circle != null)
            {
                w.G(100, "AcDbCircle");
                w.Pt(10, circle.Center);
                w.G(40, circle.Radius);
            }
            else if (arc != null)
            {
                w.G(100, "AcDbCircle");
                w.Pt(10, arc.Center);
                w.G(40, arc.Radius);
                w.G(100, "AcDbArc");
                w.G(50, arc.StartDeg);
                w.G(51, arc.EndDeg);
            }
            else if (poly != null)
            {
                w.G(100, "AcDbPolyline");
                w.G(90, poly.Points.Count);
                w.G(70, poly.Closed ? 1 : 0);
                w.G(43, 0.0);
                foreach (var p in poly.Points)
                {
                    w.G(10, p.X);
                    w.G(20, p.Y);
                }
            }
            else
            {
                w.G(100, "AcDbText");
                w.Pt(10, text.Position);
                w.G(40, text.Height);
                w.G(1, Clean(text.Value));
                if (text.RotationDeg != 0) w.G(50, text.RotationDeg);
                w.G(7, "Standard");
                w.G(100, "AcDbText");
            }
        }
    }
}
