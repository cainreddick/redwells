// Human-readable report of a face analysis (step 1 probe). Pure text, no NX.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public static class ProbeReport
    {
        static string Pad(string s, int w) { return s.Length >= w ? s : s + new string(' ', w - s.Length); }
        static string PadL(string s, int w) { return s.Length >= w ? s : new string(' ', w - s.Length) + s; }

        public static List<string> Summary(BodyModel m, FaceAnalysis a, string selectionNote)
        {
            var L = new List<string>();
            PlateFrame fr = a.Frame;
            L.Add("Part         : " + m.PartName + "   (units: " + m.Units + ")");
            L.Add("Face         : #" + a.FaceId + "  " + selectionNote);
            L.Add("Normal (out) : " + a.Normal);
            L.Add("Thickness    : " + Fmt.N(a.Thickness, 3) + " mm");
            L.Add("Plate size   : " + Fmt.N(a.Bounds.Width, 2) + " x " + Fmt.N(a.Bounds.Height, 2) + " mm (DXF X x Y)");
            if (a.LongestEdgeId >= 0)
                L.Add("X axis       : along edge #" + a.LongestEdgeId + " (longest straight edge, " + Fmt.N(m.Edge(a.LongestEdgeId).Length, 2) + " mm), plate above it");
            else
                L.Add("X axis       : fallback direction");
            L.Add("Frame        : origin " + fr.Origin + "  X " + fr.XAxis + "  Y " + fr.YAxis + "  Z " + fr.ZAxis + "  handedness " + Fmt.N(fr.Handedness, 3));

            int lines = 0, arcs = 0, other = 0;
            foreach (var oe in a.Outer.Edges)
            {
                TopoEdge e = m.Edge(oe.EdgeId);
                if (e.Kind == EdgeKind.Line) lines++;
                else if (e.HasCircle) arcs++;
                else other++;
            }
            L.Add("Outer loop   : " + a.Outer.Edges.Count + " edges (" + lines + " lines, " + arcs + " arcs, " + other + " other), " +
                  "net plate area " + Fmt.N(a.NetArea, 1) + " mm2" + (a.Outer.Closed ? "" : "  ** NOT CLOSED **"));

            int circ = 0, through = 0;
            foreach (var h in a.Holes)
            {
                if (h.IsCircle) circ++;
                if (h.Through) through++;
            }
            L.Add("Inner loops  : " + a.Holes.Count + " (" + circ + " circular, " + (a.Holes.Count - circ) + " other; " +
                  through + " go right through, " + (a.Holes.Count - through) + " do not)");

            L.Add("");
            L.Add("Holes  (centre in DXF coordinates; dia at face / narrowest dia through the plate)");
            L.Add("   #  " + Pad("centre x, y", 22) + PadL("dia face", 9) + PadL("dia bore", 9) + "  through  " + Pad("walls", 28) + "features");
            foreach (var h in a.Holes)
            {
                var feats = new List<string>();
                foreach (int fid in h.FeatureIds)
                {
                    foreach (var f in m.Features)
                        if (f.Id == fid) feats.Add(f.Name);
                }
                string centre = Fmt.N(h.Center2.X, 3) + ", " + Fmt.N(h.Center2.Y, 3);
                string kind = h.IsCircle ? "" : "  [non-circular " + Fmt.N(h.Bounds.Width, 2) + " x " + Fmt.N(h.Bounds.Height, 2) + "]";
                string flags = (h.ConeAtSurface ? " CS-this-side" : "") + (h.HasStep ? " step" : "");
                L.Add(PadL(h.Index.ToString(), 4) + "  " + Pad(centre, 22) +
                      PadL(double.IsNaN(h.TopDiameter) ? "-" : Fmt.N(h.TopDiameter, 3), 9) +
                      PadL(double.IsNaN(h.MinDiameter) ? "-" : Fmt.N(h.MinDiameter, 3), 9) + "  " +
                      Pad(h.Through ? "yes" : "NO", 7) + "  " + Pad(h.WallSummary + flags, 28) +
                      string.Join(", ", feats.ToArray()) + kind);
            }

            // Group by bore diameter - this is what the label lookup table will key on.
            var groups = new SortedDictionary<double, int>();
            foreach (var h in a.Holes)
            {
                if (!h.IsCircle || double.IsNaN(h.MinDiameter)) continue;
                double key = Math.Round(h.MinDiameter, 3);
                int c;
                groups.TryGetValue(key, out c);
                groups[key] = c + 1;
            }
            if (groups.Count > 0)
            {
                L.Add("");
                var parts = new List<string>();
                foreach (var kv in groups) parts.Add("dia " + Fmt.N(kv.Key, 3) + " x" + kv.Value);
                L.Add("Bore diameters: " + string.Join(";  ", parts.ToArray()));
            }
            return L;
        }

        public static List<string> FaceEdges(BodyModel m, FaceAnalysis a)
        {
            var L = new List<string>();
            PlateFrame fr = a.Frame;
            L.Add("Edges of the selected face (DXF coordinates):");
            L.Add("  loop   edge  " + Pad("NX type", 14) + PadL("length", 10) + "  " + Pad("start", 24) + Pad("end", 24) + "circle");
            var loops = new List<FaceLoop> { a.Outer };
            loops.AddRange(a.Inner);
            for (int li = 0; li < loops.Count; li++)
            {
                string loopName = li == 0 ? "outer" : "in" + li;
                foreach (var oe in loops[li].Edges)
                {
                    TopoEdge e = m.Edge(oe.EdgeId);
                    Vec2 s = fr.ToLocal(oe.Reversed ? e.End : e.Start);
                    Vec2 en = fr.ToLocal(oe.Reversed ? e.Start : e.End);
                    string circle = "";
                    Arc2 arc;
                    if (EdgeGeom2.TryArc(e, fr, 0.01, out arc))
                        circle = "c " + arc.Center + " r " + Fmt.N(arc.Radius, 4) + (arc.Full ? " full" : " " + Fmt.N(arc.StartDeg, 2) + "->" + Fmt.N(arc.EndDeg, 2) + " deg");
                    else if (e.HasCircle) circle = "circle not parallel to face";
                    else if (e.Kind == EdgeKind.Other) circle = e.Points.Count + " samples";
                    L.Add("  " + Pad(loopName, 6) + PadL(e.Id.ToString(), 5) + "  " + Pad(e.NxType, 14) + PadL(Fmt.N(e.Length, 3), 10) + "  " +
                          Pad(s.ToString(), 24) + Pad(en.ToString(), 24) + circle);
                }
            }
            return L;
        }

        public static List<string> Features(BodyModel m, bool withProps)
        {
            var L = new List<string>();
            L.Add("Features on the body: " + m.Features.Count);
            foreach (var f in m.Features)
            {
                var pids = new List<string>();
                foreach (int p in f.ParentIds) pids.Add(p.ToString());
                L.Add("  [" + f.Id + "] " + f.Name + "  type=" + f.FeatureType + "  class=" + f.ClassName + "  faces=" + f.FaceIds.Count +
                      (pids.Count > 0 ? "  parents=" + string.Join(",", pids.ToArray()) : ""));
                if (!withProps) continue;
                foreach (var kv in f.Props) L.Add("        " + kv.Key + " = " + kv.Value);
            }
            return L;
        }

        public static List<string> Attributes(BodyModel m)
        {
            var L = new List<string>();
            L.Add("Part attributes as NX Open reports them: " + m.Attributes.Count + "   (category | title | alias = value  [type, flags])");
            foreach (var a in m.Attributes)
            {
                L.Add("  " + Pad(a.Category, 24) + " | " + Pad(a.Title, 26) + " | " + Pad(a.Alias, 14) + " = \"" + a.Value + "\"" +
                      "  [" + a.Type + (a.Flags.Length > 0 ? ", " + a.Flags : "") + "]");
            }
            return L;
        }

        public static string Join(List<string> lines)
        {
            var sb = new StringBuilder();
            foreach (var l in lines) sb.Append(l).Append("\r\n");
            return sb.ToString();
        }
    }
}
