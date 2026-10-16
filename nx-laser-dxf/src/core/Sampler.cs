// Adaptive curve sampling used for edges that are neither lines nor arcs.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;

    public static class Sampler
    {
        /// <summary>Samples eval(t), t in [0,1], so that no chord deviates from the curve's
        /// midpoint by more than tol. Start and end points are always included.</summary>
        public static List<Vec3> Sample(Func<double, Vec3> eval, double tol, int initialSegments, int maxDepth)
        {
            var pts = new List<Vec3>();
            if (initialSegments < 1) initialSegments = 1;
            Vec3 prev = eval(0.0);
            pts.Add(prev);
            for (int i = 1; i <= initialSegments; i++)
            {
                double t0 = (double)(i - 1) / initialSegments;
                double t1 = (double)i / initialSegments;
                Vec3 p1 = eval(t1);
                Refine(eval, t0, prev, t1, p1, tol, maxDepth, pts);
                pts.Add(p1);
                prev = p1;
            }
            return pts;
        }

        static void Refine(Func<double, Vec3> eval, double t0, Vec3 p0, double t1, Vec3 p1, double tol, int depth, List<Vec3> pts)
        {
            if (depth <= 0) return;
            double tm = (t0 + t1) * 0.5;
            Vec3 pm = eval(tm);
            double dev = p0.DistanceTo(p1) < 1e-12 ? pm.DistanceTo(p0) : GeomUtil.DistanceToLine(pm, p0, p1);
            // Also check the quarter points so an S-bend whose midpoint lies on the chord is not missed.
            if (dev <= tol)
            {
                Vec3 pq = eval((t0 + tm) * 0.5);
                Vec3 pr = eval((tm + t1) * 0.5);
                if (GeomUtil.DistanceToLine(pq, p0, p1) <= tol && GeomUtil.DistanceToLine(pr, p0, p1) <= tol) return;
            }
            Refine(eval, t0, p0, tm, pm, tol, depth - 1, pts);
            pts.Add(pm);
            Refine(eval, tm, pm, t1, p1, tol, depth - 1, pts);
        }
    }
}
