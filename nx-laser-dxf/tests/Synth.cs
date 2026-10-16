// Builds synthetic plate bodies (BodyModel) with real-looking B-rep topology:
// top/bottom faces, side walls, and holes of several kinds.
// Plate occupies z = 0 (bottom) .. z = T (top).
namespace LaserDxf.Tests
{
    using System;
    using System.Collections.Generic;
    using LaserDxf.Core;

    public sealed class Seg
    {
        public bool IsArc;
        public Vec2 A, B;          // line end points / arc start & end
        public Vec2 C;             // arc centre
        public double R;
        public bool Ccw = true;    // arc direction when walking the outline CCW

        public static Seg Line(double x0, double y0, double x1, double y1)
        {
            return new Seg { A = new Vec2(x0, y0), B = new Vec2(x1, y1) };
        }

        public static Seg Arc(double cx, double cy, double r, double a0Deg, double a1Deg, bool ccw)
        {
            double r0 = a0Deg * Math.PI / 180, r1 = a1Deg * Math.PI / 180;
            return new Seg
            {
                IsArc = true, C = new Vec2(cx, cy), R = r, Ccw = ccw,
                A = new Vec2(cx + r * Math.Cos(r0), cy + r * Math.Sin(r0)),
                B = new Vec2(cx + r * Math.Cos(r1), cy + r * Math.Sin(r1))
            };
        }
    }

    public sealed class Synth
    {
        public readonly BodyModel M = new BodyModel();
        public readonly double T;
        public int TopFace, BottomFace;

        public Synth(double thickness)
        {
            T = thickness;
            M.PartName = "SYNTH";
            TopFace = AddFace(FaceKind.Planar);
            BottomFace = AddFace(FaceKind.Planar);
        }

        public int AddFace(FaceKind k)
        {
            var f = new TopoFace { Id = M.Faces.Count + 1, Kind = k, NxType = k.ToString() };
            M.Faces.Add(f);
            return f.Id;
        }

        TopoEdge NewEdge(EdgeKind k, int[] faces)
        {
            var e = new TopoEdge { Id = M.Edges.Count + 1, Kind = k, NxType = k == EdgeKind.Line ? "Linear" : k.ToString() };
            M.Edges.Add(e);
            foreach (int f in faces)
            {
                e.FaceIds.Add(f);
                M.Face(f).EdgeIds.Add(e.Id);
            }
            return e;
        }

        public TopoEdge Line(Vec3 a, Vec3 b, params int[] faces)
        {
            var e = NewEdge(EdgeKind.Line, faces);
            e.Points.Add(a);
            e.Points.Add(b);
            e.Length = a.DistanceTo(b);
            return e;
        }

        /// <summary>Arc about +Z axis at height z, from a0 sweeping by sweepDeg (negative = clockwise).</summary>
        public TopoEdge Arc(Vec2 c, double z, double r, double a0Deg, double sweepDeg, params int[] faces)
        {
            var e = NewEdge(EdgeKind.Circular, faces);
            for (int i = 0; i <= 4; i++)
            {
                double a = (a0Deg + sweepDeg * i / 4.0) * Math.PI / 180;
                e.Points.Add(new Vec3(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a), z));
            }
            if (Math.Abs(Math.Abs(sweepDeg) - 360) < 1e-9) e.Points[4] = e.Points[0];
            e.HasCircle = true;
            e.Center = new Vec3(c.X, c.Y, z);
            e.Radius = r;
            e.Axis = sweepDeg > 0 ? Vec3.UnitZ : -Vec3.UnitZ;
            e.Length = Math.Abs(sweepDeg) * Math.PI / 180 * r;
            return e;
        }

        public TopoEdge Circle(Vec2 c, double z, double r, params int[] faces)
        {
            return Arc(c, z, r, 0, 360, faces);
        }

        static Vec3 P(Vec2 p, double z) { return new Vec3(p.X, p.Y, z); }

        /// <summary>Outline given CCW as connected segments.</summary>
        public void Outline(List<Seg> segs)
        {
            var sideFaces = new List<int>();
            foreach (var s in segs) sideFaces.Add(AddFace(s.IsArc ? FaceKind.Cylindrical : FaceKind.Planar));
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                int side = sideFaces[i];
                int prevSide = sideFaces[(i + segs.Count - 1) % segs.Count];
                if (s.IsArc)
                {
                    double a0 = Math.Atan2(s.A.Y - s.C.Y, s.A.X - s.C.X) * 180 / Math.PI;
                    double a1 = Math.Atan2(s.B.Y - s.C.Y, s.B.X - s.C.X) * 180 / Math.PI;
                    double sweep = a1 - a0;
                    if (s.Ccw) { while (sweep <= 0) sweep += 360; }
                    else { while (sweep >= 0) sweep -= 360; }
                    Arc(s.C, T, s.R, a0, sweep, TopFace, side);
                    Arc(s.C, 0, s.R, a0, sweep, BottomFace, side);
                }
                else
                {
                    Line(P(s.A, T), P(s.B, T), TopFace, side);
                    Line(P(s.A, 0), P(s.B, 0), BottomFace, side);
                }
                Line(P(s.A, 0), P(s.A, T), prevSide, side); // vertical edge at segment start
            }
        }

        public void Rectangle(double w, double h)
        {
            Outline(new List<Seg> { Seg.Line(0, 0, w, 0), Seg.Line(w, 0, w, h), Seg.Line(w, h, 0, h), Seg.Line(0, h, 0, 0) });
        }

        public void SimpleHole(double x, double y, double d)
        {
            var c = new Vec2(x, y);
            int cyl = AddFace(FaceKind.Cylindrical);
            Circle(c, T, d / 2, TopFace, cyl);
            Circle(c, 0, d / 2, BottomFace, cyl);
        }

        /// <summary>Same hole but each circle split into two half arcs (as some imports give).</summary>
        public void SplitHole(double x, double y, double d)
        {
            var c = new Vec2(x, y);
            int cyl1 = AddFace(FaceKind.Cylindrical);
            int cyl2 = AddFace(FaceKind.Cylindrical);
            Arc(c, T, d / 2, 0, 180, TopFace, cyl1);
            Arc(c, T, d / 2, 180, 180, TopFace, cyl2);
            Arc(c, 0, d / 2, 0, 180, BottomFace, cyl1);
            Arc(c, 0, d / 2, 180, 180, BottomFace, cyl2);
            Line(new Vec3(x + d / 2, y, 0), new Vec3(x + d / 2, y, T), cyl1, cyl2);
            Line(new Vec3(x - d / 2, y, 0), new Vec3(x - d / 2, y, T), cyl1, cyl2);
        }

        /// <summary>Countersink opening on the top (onTop) or bottom face.</summary>
        public void Countersunk(double x, double y, double bore, double csDia, bool onTop)
        {
            var c = new Vec2(x, y);
            double depth = (csDia - bore) / 2; // 90 degree countersink
            int cone = AddFace(FaceKind.Conical);
            int cyl = AddFace(FaceKind.Cylindrical);
            int csFace = onTop ? TopFace : BottomFace;
            int farFace = onTop ? BottomFace : TopFace;
            double zCs = onTop ? T : 0, zMid = onTop ? T - depth : depth, zFar = onTop ? 0 : T;
            Circle(c, zCs, csDia / 2, csFace, cone);
            Circle(c, zMid, bore / 2, cone, cyl);
            Circle(c, zFar, bore / 2, farFace, cyl);
        }

        public void Counterbore(double x, double y, double bore, double cbDia, double cbDepth)
        {
            var c = new Vec2(x, y);
            int cyl1 = AddFace(FaceKind.Cylindrical);
            int floor = AddFace(FaceKind.Planar);
            int cyl2 = AddFace(FaceKind.Cylindrical);
            Circle(c, T, cbDia / 2, TopFace, cyl1);
            Circle(c, T - cbDepth, cbDia / 2, cyl1, floor);
            Circle(c, T - cbDepth, bore / 2, floor, cyl2);
            Circle(c, 0, bore / 2, BottomFace, cyl2);
        }

        public void BlindHole(double x, double y, double d, double depth)
        {
            var c = new Vec2(x, y);
            int cyl = AddFace(FaceKind.Cylindrical);
            int floor = AddFace(FaceKind.Planar);
            Circle(c, T, d / 2, TopFace, cyl);
            Circle(c, T - depth, d / 2, cyl, floor);
        }

        /// <summary>Rectangular through cut-out (walks CW so it is a hole).</summary>
        public void RectCutout(double x0, double y0, double x1, double y1)
        {
            var pts = new[] { new Vec2(x0, y0), new Vec2(x0, y1), new Vec2(x1, y1), new Vec2(x1, y0) };
            var walls = new int[4];
            for (int i = 0; i < 4; i++) walls[i] = AddFace(FaceKind.Planar);
            for (int i = 0; i < 4; i++)
            {
                Vec2 a = pts[i], b = pts[(i + 1) % 4];
                Line(P(a, T), P(b, T), TopFace, walls[i]);
                Line(P(a, 0), P(b, 0), BottomFace, walls[i]);
                Line(P(a, 0), P(a, T), walls[(i + 3) % 4], walls[i]);
            }
        }

        /// <summary>Irregular plate resembling the user's example: straight left side, rounded
        /// top-left corner, a pointed right side with a rounded tip, chamfer-like diagonals.</summary>
        public static Synth Irregular()
        {
            var s = new Synth(6);
            double r = 40;
            var segs = new List<Seg>
            {
                Seg.Line(r, 0, 360, 0),
                Seg.Arc(360, r, r, -90, -45 + 0.0, true),
                Seg.Line(360 + r * Math.Cos(-Math.PI / 4), r + r * Math.Sin(-Math.PI / 4), 770 - 25 * Math.Cos(Math.PI / 4), 400 - 25 * Math.Sin(Math.PI / 4)),
                Seg.Arc(770 - 25 * Math.Sqrt(2), 400, 25, -45, 45, true),
                Seg.Line(770 - 25 * Math.Sqrt(2) + 25 * Math.Cos(Math.PI / 4), 400 + 25 * Math.Sin(Math.PI / 4), 430, 805 - 0),
                Seg.Line(430, 805, r, 805),
                Seg.Arc(r, 805 - r, r, 90, 180, true),
                Seg.Line(0, 805 - r, 0, r),
                Seg.Arc(r, r, r, 180, 270, true),
            };
            s.Outline(segs);
            // grid of plain holes and a few labelled ones like the example
            double[] xs = { 57, 230, 405 };
            double[] ys = { 57, 230, 405, 575, 760 };
            foreach (double x in xs) foreach (double y in ys) s.SimpleHole(x, y, 13);
            s.SimpleHole(135, 75, 55);
            s.SimpleHole(335, 720, 55);
            s.SimpleHole(90, 290, 5.8);
            s.SimpleHole(110, 275, 5.5);
            s.SimpleHole(130, 250, 5.8);
            s.SimpleHole(560, 470, 5.8);
            s.SimpleHole(600, 420, 5.5);
            s.Countersunk(585, 260, 9, 18, true);
            s.SimpleHole(735, 400, 13);
            return s;
        }
    }
}
