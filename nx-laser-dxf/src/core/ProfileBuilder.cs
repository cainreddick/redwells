// Converts a FaceAnalysis into CUT-layer DXF entities.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;

    public sealed class CutHole
    {
        public HoleInfo Hole;
        /// <summary>Diameter written to the DXF (mm). NaN for non-circular cut-outs.</summary>
        public double CutDiameter = double.NaN;
        public Vec2 Center;
        public string Label = "";
        public string LabelSource = "";
        /// <summary>Why a round hole got no label (for the report).</summary>
        public string Note = "";
        public DxfCircle Circle;
    }

    public sealed class ProfileResult
    {
        public DxfDocument Doc = new DxfDocument();
        public List<CutHole> Holes = new List<CutHole>();
        public int OuterEntityCount;
        public int SkippedLoops;
        public double Width, Height;
        public double UnitScale = 1;
        public List<string> Warnings = new List<string>();
    }

    public static class ProfileBuilder
    {
        public static double UnitScaleFor(string units)
        {
            return string.Equals(units, "Inches", StringComparison.OrdinalIgnoreCase) ? 25.4 : 1.0;
        }

        public static ProfileResult Build(BodyModel m, FaceAnalysis a, LaserConfig c)
        {
            var r = new ProfileResult();
            r.UnitScale = UnitScaleFor(m.Units);
            if (r.UnitScale != 1) r.Warnings.Add("Part units are " + m.Units + " - converted to mm.");
            double k = r.UnitScale;

            r.Doc.Layers.Add(c.CutLayer);
            r.Doc.Layers.Add(c.EtchLayer);

            // Outer profile.
            int before = r.Doc.Entities.Count;
            AddLoop(m, a, a.Outer, c, k, r);
            r.OuterEntityCount = r.Doc.Entities.Count - before;
            if (!a.Outer.Closed) r.Warnings.Add("The outer profile is not closed - check the DXF before cutting.");
            CheckOuterWalls(m, a, c.Tolerance, r);

            foreach (HoleInfo h in a.Holes)
            {
                string where = "at (" + Fmt.N(h.Center2.X * k, 2) + ", " + Fmt.N(h.Center2.Y * k, 2) + ")";
                if (!h.Through)
                {
                    r.SkippedLoops++;
                    r.Warnings.Add("Inner loop #" + h.Index + " " + where + " does not go through the plate (blind hole or pocket) - not cut.");
                    continue;
                }
                var ch = new CutHole { Hole = h, Center = h.Center2 * k };
                if (h.IsCircle)
                {
                    double dia = double.IsNaN(h.MinDiameter) ? h.TopDiameter : h.MinDiameter;
                    ch.CutDiameter = dia * k;
                    ch.Circle = new DxfCircle { Center = ch.Center, Radius = ch.CutDiameter / 2 };
                    Style(ch.Circle, c.CutLayer, c);
                    r.Doc.Entities.Add(ch.Circle);
                    if (h.TopDiameter - dia > c.Tolerance)
                        r.Warnings.Add("Hole #" + h.Index + " " + where + ": cut at the bore dia " + Fmt.N(dia * k, 3) + " (dia " +
                                       Fmt.N(h.TopDiameter * k, 3) + " at the face - " + (h.HasCone ? "countersink" : "counterbore") + ").");
                }
                else
                {
                    AddLoop(m, a, h.Loop, c, k, r);
                    if (h.HasStep)
                        r.Warnings.Add("Cut-out #" + h.Index + " " + where + " has a step part-way through - cut to its outline on the selected face.");
                    if (!h.Loop.Closed)
                        r.Warnings.Add("Cut-out #" + h.Index + " " + where + " is not closed - check the DXF.");
                }
                r.Holes.Add(ch);
            }

            r.Width = a.Bounds.Width * k;
            r.Height = a.Bounds.Height * k;
            return r;
        }

        static void Style(DxfEntity e, DxfLayer layer, LaserConfig c)
        {
            e.Layer = layer.Name;
            e.Color = c.EntityColors ? layer.Color : 256;
        }

        static void AddLoop(BodyModel m, FaceAnalysis a, FaceLoop loop, LaserConfig c, double k, ProfileResult r)
        {
            foreach (var oe in loop.Edges)
            {
                TopoEdge e = m.Edge(oe.EdgeId);
                DxfEntity ent;
                Arc2 arc;
                if (e.Kind == EdgeKind.Line)
                {
                    ent = new DxfLine { A = a.Frame.ToLocal(e.Start) * k, B = a.Frame.ToLocal(e.End) * k };
                }
                else if (EdgeGeom2.TryArc(e, a.Frame, c.Tolerance, out arc))
                {
                    if (arc.Full) ent = new DxfCircle { Center = arc.Center * k, Radius = arc.Radius * k };
                    else ent = new DxfArc { Center = arc.Center * k, Radius = arc.Radius * k, StartDeg = arc.StartDeg, EndDeg = arc.EndDeg };
                }
                else
                {
                    var pl = new DxfPolyline();
                    List<Vec3> pts = e.Points;
                    for (int i = 0; i < pts.Count; i++)
                        pl.Points.Add(a.Frame.ToLocal(pts[oe.Reversed ? pts.Count - 1 - i : i]) * k);
                    pl.Closed = e.IsClosed(c.Tolerance);
                    if (pl.Closed) pl.Points.RemoveAt(pl.Points.Count - 1);
                    ent = pl;
                }
                Style(ent, c.CutLayer, c);
                r.Doc.Entities.Add(ent);
            }
        }

        /// <summary>Warns when the outer side walls don't run the full plate thickness
        /// (a chamfer or edge break on the selected side makes the face outline smaller than the cut).</summary>
        static void CheckOuterWalls(BodyModel m, FaceAnalysis a, double tol, ProfileResult r)
        {
            double depthTol = Math.Max(tol * 5, 0.02);
            foreach (var oe in a.Outer.Edges)
            {
                foreach (int fid in m.Edge(oe.EdgeId).FaceIds)
                {
                    if (fid == a.FaceId) continue;
                    double dmax = 0;
                    foreach (int eid in m.Face(fid).EdgeIds)
                        foreach (var p in m.Edge(eid).Points)
                            dmax = Math.Max(dmax, -a.Frame.Height(p));
                    if (dmax < a.Thickness - depthTol)
                    {
                        r.Warnings.Add("The outer profile has a chamfer or edge break - the DXF follows the outline of the selected face.");
                        return;
                    }
                }
            }
        }
    }
}
