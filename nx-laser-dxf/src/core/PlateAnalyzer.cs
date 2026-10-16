// Turns a BodyModel + selected planar face into: outward normal, thickness,
// a right-handed 2D frame, the outer loop, inner loops and per-hole analysis.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;

    public class PlateException : Exception
    {
        public PlateException(string message) : base(message) { }
    }

    public sealed class OrientedEdge
    {
        public int EdgeId;
        public bool Reversed;
    }

    public sealed class FaceLoop
    {
        public List<OrientedEdge> Edges = new List<OrientedEdge>();
        /// <summary>Closed polyline through the edge samples (last point not repeated).</summary>
        public List<Vec3> Points = new List<Vec3>();
        public bool Closed = true;
        /// <summary>Signed area relative to the outward plate normal.</summary>
        public double Area;
        public bool IsOuter;
    }

    public sealed class HoleInfo
    {
        public int Index;
        public FaceLoop Loop;
        public bool IsCircle;
        public Vec3 Center;
        public Vec2 Center2;
        /// <summary>Diameter of the loop on the selected face.</summary>
        public double TopDiameter = double.NaN;
        /// <summary>Narrowest coaxial circular edge through the plate (the laser-cut bore).</summary>
        public double MinDiameter = double.NaN;
        public bool Through;
        public bool HasCone;
        /// <summary>A conical face meets the selected face (countersink opens on this side).</summary>
        public bool ConeAtSurface;
        /// <summary>A planar floor/step parallel to the face part-way through (counterbore or pocket).</summary>
        public bool HasStep;
        public List<int> WallFaceIds = new List<int>();
        public List<int> FeatureIds = new List<int>();
        public string WallSummary = "";
        public Bounds2 Bounds;
    }

    public enum XAlignMode { LongestEdge, Hint }

    public sealed class AnalysisOptions
    {
        /// <summary>Geometric tolerance in mm.</summary>
        public double Tol = 0.01;
        public XAlignMode XAlign = XAlignMode.LongestEdge;
        /// <summary>Used for XAlign = Hint (e.g. WCS X) and as fallback when there is no straight edge.</summary>
        public Vec3 XHint = Vec3.UnitX;
    }

    public sealed class FaceAnalysis
    {
        public int FaceId;
        public Vec3 Normal;
        public double Thickness;
        public PlateFrame Frame;
        public FaceLoop Outer;
        public List<FaceLoop> Inner = new List<FaceLoop>();
        public List<HoleInfo> Holes = new List<HoleInfo>();
        public int LongestEdgeId = -1;
        /// <summary>Outer profile bounds in frame coordinates (MinX = MinY = 0).</summary>
        public Bounds2 Bounds;
        public double NetArea;
        public List<string> Warnings = new List<string>();
    }

    /// <summary>An edge mapped onto the plate plane as a true arc (CCW from StartDeg to EndDeg).</summary>
    public sealed class Arc2
    {
        public Vec2 Center;
        public double Radius;
        public double StartDeg;
        public double EndDeg;
        public bool Full;
    }

    public static class EdgeGeom2
    {
        static double NormDeg(double a)
        {
            a %= 360.0;
            if (a < 0) a += 360.0;
            return a;
        }

        static double AngleDeg(Vec2 c, Vec2 p)
        {
            return NormDeg(Math.Atan2(p.Y - c.Y, p.X - c.X) * 180.0 / Math.PI);
        }

        /// <summary>True if the edge is a circle/arc whose axis is normal to the plate.</summary>
        public static bool TryArc(TopoEdge e, PlateFrame fr, double tol, out Arc2 arc)
        {
            arc = null;
            if (!e.HasCircle || Math.Abs(e.Axis.Dot(fr.ZAxis)) < 0.9999 || e.Points.Count < 3) return false;
            var a = new Arc2 { Center = fr.ToLocal(e.Center), Radius = e.Radius };
            if (e.IsClosed(tol))
            {
                a.Full = true;
                a.StartDeg = 0;
                a.EndDeg = 360;
                arc = a;
                return true;
            }
            double a0 = AngleDeg(a.Center, fr.ToLocal(e.Start));
            double a1 = AngleDeg(a.Center, fr.ToLocal(e.End));
            double am = AngleDeg(a.Center, fr.ToLocal(e.Points[e.Points.Count / 2]));
            double sweep = NormDeg(a1 - a0);
            if (sweep == 0) sweep = 360;
            if (NormDeg(am - a0) < sweep)
            {
                a.StartDeg = a0;
                a.EndDeg = a1;
            }
            else
            {
                a.StartDeg = a1;
                a.EndDeg = a0;
            }
            arc = a;
            return true;
        }

        public static bool AngleInCcwRange(double ang, double start, double end)
        {
            double s = NormDeg(end - start);
            if (s == 0) s = 360;
            return NormDeg(ang - start) <= s;
        }

        /// <summary>Grows b by the exact 2D extent of the edge (arcs include their quadrant points).</summary>
        public static void AddBounds(TopoEdge e, PlateFrame fr, double tol, ref Bounds2 b)
        {
            foreach (var p in e.Points) b.Add(fr.ToLocal(p));
            Arc2 arc;
            if (!TryArc(e, fr, tol, out arc)) return;
            for (int k = 0; k < 4; k++)
            {
                double ang = k * 90.0;
                if (arc.Full || AngleInCcwRange(ang, arc.StartDeg, arc.EndDeg))
                {
                    double r = ang * Math.PI / 180.0;
                    b.Add(new Vec2(arc.Center.X + arc.Radius * Math.Cos(r), arc.Center.Y + arc.Radius * Math.Sin(r)));
                }
            }
        }
    }

    public static class PlateAnalyzer
    {
        // ---------------- loops ----------------

        public static List<FaceLoop> BuildLoops(BodyModel m, TopoFace face, double tol, List<string> warnings)
        {
            var loops = new List<FaceLoop>();
            var remaining = new List<TopoEdge>();
            foreach (int id in face.EdgeIds)
            {
                TopoEdge e = m.Edge(id);
                if (e.Points.Count < 2) continue;
                if (e.IsClosed(tol))
                {
                    var single = new FaceLoop();
                    single.Edges.Add(new OrientedEdge { EdgeId = e.Id });
                    loops.Add(single);
                }
                else remaining.Add(e);
            }

            while (remaining.Count > 0)
            {
                var loop = new FaceLoop();
                TopoEdge first = remaining[0];
                remaining.RemoveAt(0);
                loop.Edges.Add(new OrientedEdge { EdgeId = first.Id });
                Vec3 loopStart = first.Start;
                Vec3 cur = first.End;
                while (cur.DistanceTo(loopStart) > tol)
                {
                    int bestIdx = -1;
                    bool bestRev = false;
                    double bestDist = tol;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        double ds = remaining[i].Start.DistanceTo(cur);
                        double de = remaining[i].End.DistanceTo(cur);
                        if (ds <= bestDist) { bestDist = ds; bestIdx = i; bestRev = false; }
                        if (de <= bestDist) { bestDist = de; bestIdx = i; bestRev = true; }
                    }
                    if (bestIdx < 0)
                    {
                        loop.Closed = false;
                        if (warnings != null)
                            warnings.Add("Face " + face.Id + ": a loop does not close (gap at " + cur + "). Its outline may be incomplete.");
                        break;
                    }
                    TopoEdge next = remaining[bestIdx];
                    remaining.RemoveAt(bestIdx);
                    loop.Edges.Add(new OrientedEdge { EdgeId = next.Id, Reversed = bestRev });
                    cur = bestRev ? next.Start : next.End;
                }
                loops.Add(loop);
            }

            foreach (var loop in loops) FillPoints(m, loop);
            return loops;
        }

        static void FillPoints(BodyModel m, FaceLoop loop)
        {
            loop.Points.Clear();
            foreach (var oe in loop.Edges)
            {
                List<Vec3> pts = EdgePolyline(m.Edge(oe.EdgeId));
                int n = pts.Count;
                // Skip each edge's last point: the next edge starts there (or the loop closes).
                for (int i = 0; i < n - 1; i++)
                    loop.Points.Add(oe.Reversed ? pts[n - 1 - i] : pts[i]);
            }
        }

        /// <summary>Edge as a polyline; arcs are densified (5 degree steps) so loop areas are accurate.</summary>
        public static List<Vec3> EdgePolyline(TopoEdge e)
        {
            if (!e.HasCircle || e.Radius <= 0) return e.Points;
            // Axis comes from a 3-point fit in traversal order, so the edge runs CCW about it.
            Vec3 u = (e.Start - e.Center).Normalized();
            Vec3 v = e.Axis.Cross(u);
            double sweep = 2 * Math.PI;
            if (e.Start.DistanceTo(e.End) > 1e-9 * Math.Max(1, e.Radius))
            {
                Vec3 d = e.End - e.Center;
                sweep = Math.Atan2(d.Dot(v), d.Dot(u));
                if (sweep <= 0) sweep += 2 * Math.PI;
            }
            int n = Math.Max(4, (int)Math.Ceiling(sweep / (Math.PI / 36)));
            var pts = new List<Vec3>(n + 1);
            for (int i = 0; i < n; i++)
            {
                double t = sweep * i / n;
                pts.Add(e.Center + (u * Math.Cos(t) + v * Math.Sin(t)) * e.Radius);
            }
            pts.Add(e.End);
            return pts;
        }

        // ---------------- face analysis ----------------

        public static FaceAnalysis Analyze(BodyModel m, int faceId, AnalysisOptions opt)
        {
            TopoFace face = m.Face(faceId);
            if (face.Kind != FaceKind.Planar)
                throw new PlateException("The selected face is not planar (NX face type: " + face.NxType + "). Select the flat top or bottom face of the plate.");

            var a = new FaceAnalysis { FaceId = faceId };
            List<FaceLoop> loops = BuildLoops(m, face, opt.Tol, a.Warnings);
            if (loops.Count == 0) throw new PlateException("The selected face has no edges.");

            // Plane normal from the biggest loop (that is the outer loop on a planar face).
            FaceLoop outer = null;
            Vec3 best = Vec3.Zero;
            foreach (var l in loops)
            {
                Vec3 nn = GeomUtil.NewellNormal(l.Points);
                if (nn.Length > best.Length) { best = nn; outer = l; }
            }
            if (outer == null || best.Length < 1e-9) throw new PlateException("Could not work out the plane of the selected face.");

            Vec3 n = best.Normalized();
            Vec3 p0 = outer.Points[0];

            // Outward normal: the body's material must sit behind the face.
            double maxD = 0, minD = 0;
            foreach (var p in m.AllPoints())
            {
                double d = (p - p0).Dot(n);
                if (d > maxD) maxD = d;
                if (d < minD) minD = d;
            }
            if (maxD > -minD)
            {
                n = -n;
                double t = maxD;
                maxD = -minD;
                minD = -t;
            }
            a.Normal = n;
            a.Thickness = -minD;
            if (a.Thickness < opt.Tol) throw new PlateException("The body has no measurable thickness behind the selected face.");
            if (maxD > Math.Max(opt.Tol * 5, 0.05))
                a.Warnings.Add("Some of the body lies " + Fmt.N(maxD, 3) + " mm in front of the selected face. Is it really the top or bottom face of a flat plate?");

            foreach (var l in loops) l.Area = GeomUtil.NewellNormal(l.Points).Dot(n) * 0.5;
            outer.IsOuter = true;
            a.Outer = outer;
            foreach (var l in loops) if (l != outer) a.Inner.Add(l);

            a.Frame = BuildFrame(m, a, p0, opt);

            a.NetArea = Math.Abs(outer.Area);
            foreach (var l in a.Inner) a.NetArea -= Math.Abs(l.Area);

            int index = 1;
            foreach (var l in a.Inner)
            {
                HoleInfo h = AnalyzeHole(m, face, l, a.Frame, a.Thickness, opt.Tol);
                h.Index = index++;
                a.Holes.Add(h);
            }
            return a;
        }

        static PlateFrame BuildFrame(BodyModel m, FaceAnalysis a, Vec3 p0, AnalysisOptions opt)
        {
            Vec3 n = a.Normal;
            Vec3 x = Vec3.Zero;
            Vec3 edgePoint = p0;

            if (opt.XAlign == XAlignMode.LongestEdge)
            {
                double bestLen = 0;
                foreach (var oe in a.Outer.Edges)
                {
                    TopoEdge e = m.Edge(oe.EdgeId);
                    if (e.Kind != EdgeKind.Line) continue;
                    Vec3 v = GeomUtil.ProjectOntoPlane(e.End - e.Start, n);
                    if (v.Length > bestLen + opt.Tol)
                    {
                        bestLen = v.Length;
                        x = v;
                        edgePoint = e.Start;
                        a.LongestEdgeId = e.Id;
                    }
                }
                if (a.LongestEdgeId < 0)
                    a.Warnings.Add("The outer profile has no straight edge, so X follows the fallback direction (WCS X).");
            }

            if (x.Length < opt.Tol)
            {
                x = GeomUtil.ProjectOntoPlane(opt.XHint, n);
                if (x.Length < 1e-6) x = GeomUtil.ProjectOntoPlane(Vec3.UnitX, n);
                if (x.Length < 1e-6) x = GeomUtil.ProjectOntoPlane(Vec3.UnitY, n);
            }
            x = x.Normalized();

            var fr = new PlateFrame { Origin = p0, XAxis = x, YAxis = n.Cross(x), ZAxis = n };

            // Longest edge goes along the bottom: the plate must lie above it.
            if (a.LongestEdgeId >= 0)
            {
                Bounds2 b0 = OuterBounds(m, a.Outer, fr, opt.Tol);
                double centreY = (b0.MinY + b0.MaxY) * 0.5;
                if (centreY < fr.ToLocal(edgePoint).Y)
                {
                    fr.XAxis = -fr.XAxis;
                    fr.YAxis = -fr.YAxis; // rotate 180 degrees about Z - never a mirror
                }
            }

            // Origin at the bottom-left of the outer profile's bounding box.
            Bounds2 b = OuterBounds(m, a.Outer, fr, opt.Tol);
            fr.Origin = fr.Origin + fr.XAxis * b.MinX + fr.YAxis * b.MinY;
            a.Bounds = OuterBounds(m, a.Outer, fr, opt.Tol);
            return fr;
        }

        public static Bounds2 OuterBounds(BodyModel m, FaceLoop loop, PlateFrame fr, double tol)
        {
            Bounds2 b = Bounds2.Empty;
            foreach (var oe in loop.Edges) EdgeGeom2.AddBounds(m.Edge(oe.EdgeId), fr, tol, ref b);
            return b;
        }

        // ---------------- holes ----------------

        static HoleInfo AnalyzeHole(BodyModel m, TopoFace top, FaceLoop loop, PlateFrame fr, double thickness, double tol)
        {
            var h = new HoleInfo { Loop = loop };
            double depthTol = Math.Max(tol * 5, 0.02);

            // Is the loop one circle (possibly split into several arcs)?
            bool allCirc = loop.Closed;
            TopoEdge firstEdge = m.Edge(loop.Edges[0].EdgeId);
            foreach (var oe in loop.Edges)
            {
                TopoEdge e = m.Edge(oe.EdgeId);
                if (!e.HasCircle || e.Center.DistanceTo(firstEdge.Center) > tol || Math.Abs(e.Radius - firstEdge.Radius) > tol)
                {
                    allCirc = false;
                    break;
                }
            }
            h.IsCircle = allCirc;
            if (allCirc)
            {
                h.Center = firstEdge.Center;
                h.Center2 = fr.ToLocal(firstEdge.Center);
                h.TopDiameter = 2 * firstEdge.Radius;
            }
            Bounds2 bb = Bounds2.Empty;
            foreach (var oe in loop.Edges) EdgeGeom2.AddBounds(m.Edge(oe.EdgeId), fr, tol, ref bb);
            h.Bounds = bb;
            if (!allCirc) h.Center2 = new Vec2((bb.MinX + bb.MaxX) * 0.5, (bb.MinY + bb.MaxY) * 0.5);

            // Flood-fill the hole's wall faces from the loop, stopping at the far face of the plate.
            var visited = new IntSet();
            visited.Add(top.Id);
            var queue = new Queue<int>();
            foreach (var oe in loop.Edges)
            {
                foreach (int fid in m.Edge(oe.EdgeId).FaceIds)
                {
                    if (visited.Add(fid))
                    {
                        queue.Enqueue(fid);
                        if (m.Face(fid).Kind == FaceKind.Conical) h.ConeAtSurface = true;
                    }
                }
            }

            var kinds = new List<string>();
            while (queue.Count > 0)
            {
                TopoFace g = m.Face(queue.Dequeue());
                double dmin = double.MaxValue, dmax = double.MinValue;
                foreach (int eid in g.EdgeIds)
                {
                    foreach (var p in m.Edge(eid).Points)
                    {
                        double d = -fr.Height(p);
                        if (d < dmin) dmin = d;
                        if (d > dmax) dmax = d;
                    }
                }
                bool parallelPlanar = g.Kind == FaceKind.Planar && dmax - dmin <= depthTol;
                if (parallelPlanar)
                {
                    double d = (dmin + dmax) * 0.5;
                    if (Math.Abs(d - thickness) <= depthTol) { h.Through = true; continue; }
                    if (d <= depthTol) continue; // another face level with the top - not part of this hole
                    h.HasStep = true;
                }
                h.WallFaceIds.Add(g.Id);
                if (g.Kind == FaceKind.Conical) h.HasCone = true;
                string k = parallelPlanar ? "Floor" : g.Kind.ToString();
                if (!kinds.Contains(k)) kinds.Add(k);

                if (h.WallFaceIds.Count > 500) break; // runaway guard
                foreach (int eid in g.EdgeIds)
                    foreach (int nf in m.Edge(eid).FaceIds)
                        if (visited.Add(nf)) queue.Enqueue(nf);
            }
            h.WallSummary = string.Join("+", kinds.ToArray());

            // Narrowest coaxial circle anywhere in the walls = the bore the laser must cut.
            var edgeIds = new IntSet();
            foreach (var oe in loop.Edges) edgeIds.Add(oe.EdgeId);
            foreach (int fid in h.WallFaceIds) foreach (int eid in m.Face(fid).EdgeIds) edgeIds.Add(eid);
            foreach (int eid in edgeIds.Items)
            {
                TopoEdge e = m.Edge(eid);
                if (!e.HasCircle || Math.Abs(e.Axis.Dot(fr.ZAxis)) < 0.9999) continue;
                if (h.IsCircle && fr.ToLocal(e.Center).DistanceTo(h.Center2) > tol * 10) continue;
                double dia = 2 * e.Radius;
                if (double.IsNaN(h.MinDiameter) || dia < h.MinDiameter) h.MinDiameter = dia;
            }

            var wallSet = new IntSet();
            foreach (int fid in h.WallFaceIds) wallSet.Add(fid);
            foreach (var f in m.Features)
            {
                foreach (int fid in f.FaceIds)
                {
                    if (wallSet.Contains(fid)) { h.FeatureIds.Add(f.Id); break; }
                }
            }
            return h;
        }

        // ---------------- body selection ----------------

        /// <summary>Largest planar face by outer-profile area. When the two plate faces tie (within 2%),
        /// prefer the face that countersinks open onto, then the one facing preferredUp.</summary>
        public static int PickPlateFace(BodyModel m, AnalysisOptions opt, Vec3 preferredUp, List<string> log)
        {
            var cands = new List<KeyValuePair<int, double>>();
            foreach (var f in m.Faces)
            {
                if (f.Kind != FaceKind.Planar) continue;
                List<FaceLoop> loops = BuildLoops(m, f, opt.Tol, null);
                double outer = 0;
                foreach (var l in loops) outer = Math.Max(outer, GeomUtil.NewellNormal(l.Points).Length * 0.5);
                cands.Add(new KeyValuePair<int, double>(f.Id, outer));
            }
            if (cands.Count == 0) throw new PlateException("The selected body has no planar faces.");
            cands.Sort((p, q) => q.Value.CompareTo(p.Value));

            int pick = cands[0].Key;
            if (cands.Count > 1 && cands[1].Value >= cands[0].Value * 0.98)
            {
                FaceAnalysis a0 = Analyze(m, cands[0].Key, opt);
                FaceAnalysis a1 = Analyze(m, cands[1].Key, opt);
                int cs0 = 0, cs1 = 0;
                foreach (var h in a0.Holes) if (h.ConeAtSurface) cs0++;
                foreach (var h in a1.Holes) if (h.ConeAtSurface) cs1++;
                if (cs0 != cs1)
                {
                    pick = cs0 > cs1 ? a0.FaceId : a1.FaceId;
                    if (log != null) log.Add("Two plate faces of equal size: picked face " + pick + " because the countersinks open onto it.");
                }
                else
                {
                    pick = a0.Normal.Dot(preferredUp) >= a1.Normal.Dot(preferredUp) ? a0.FaceId : a1.FaceId;
                    if (log != null) log.Add("Two plate faces of equal size: picked face " + pick + " (the one facing up).");
                }
            }
            else if (log != null) log.Add("Picked the largest planar face: face " + pick + ".");
            return pick;
        }
    }
}
