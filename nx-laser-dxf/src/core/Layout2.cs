// 2D layout geometry for placing text: rectangles, clearances, point-in-polygon.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;

    public struct Rect2
    {
        public double MinX, MinY, MaxX, MaxY;

        public Rect2(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
        }

        public static Rect2 FromCenter(Vec2 c, double w, double h)
        {
            return new Rect2(c.X - w / 2, c.Y - h / 2, c.X + w / 2, c.Y + h / 2);
        }

        public double Width { get { return MaxX - MinX; } }
        public double Height { get { return MaxY - MinY; } }
        public Vec2 Center { get { return new Vec2((MinX + MaxX) / 2, (MinY + MaxY) / 2); } }

        public override string ToString()
        {
            return "[" + Fmt.N(MinX, 2) + ", " + Fmt.N(MinY, 2) + " .. " + Fmt.N(MaxX, 2) + ", " + Fmt.N(MaxY, 2) + "]";
        }
    }

    public static class Layout2
    {
        /// <summary>Distance from a point to a rectangle (0 inside).</summary>
        public static double PointRect(Vec2 p, Rect2 r)
        {
            double dx = Math.Max(Math.Max(r.MinX - p.X, 0), p.X - r.MaxX);
            double dy = Math.Max(Math.Max(r.MinY - p.Y, 0), p.Y - r.MaxY);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static double RectRect(Rect2 a, Rect2 b)
        {
            double dx = Math.Max(Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX), 0);
            double dy = Math.Max(Math.Max(a.MinY - b.MaxY, b.MinY - a.MaxY), 0);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static double PointSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 d = b - a;
            double len2 = d.X * d.X + d.Y * d.Y;
            double t = len2 < 1e-24 ? 0 : ((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / len2;
            t = Math.Max(0, Math.Min(1, t));
            return p.DistanceTo(new Vec2(a.X + d.X * t, a.Y + d.Y * t));
        }

        static double Cross(Vec2 o, Vec2 a, Vec2 b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        static bool SegmentsIntersect(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
        {
            double d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0));
        }

        /// <summary>Distance from a segment to a rectangle (0 if they touch or cross).</summary>
        public static double SegmentRect(Vec2 a, Vec2 b, Rect2 r)
        {
            if (PointRect(a, r) == 0 || PointRect(b, r) == 0) return 0;
            var c = new[] { new Vec2(r.MinX, r.MinY), new Vec2(r.MaxX, r.MinY), new Vec2(r.MaxX, r.MaxY), new Vec2(r.MinX, r.MaxY) };
            for (int i = 0; i < 4; i++)
                if (SegmentsIntersect(a, b, c[i], c[(i + 1) % 4])) return 0;
            double d = Math.Min(PointRect(a, r), PointRect(b, r));
            for (int i = 0; i < 4; i++) d = Math.Min(d, PointSegment(c[i], a, b));
            return d;
        }

        /// <summary>Distance from a closed polyline's edges to a rectangle.</summary>
        public static double PolylineRect(List<Vec2> poly, Rect2 r)
        {
            double d = double.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                Vec2 a = poly[i], b = poly[(i + 1) % poly.Count];
                // quick reject: segment bounding box far away
                double bx = Math.Max(Math.Max(r.MinX - Math.Max(a.X, b.X), Math.Min(a.X, b.X) - r.MaxX), 0);
                double by = Math.Max(Math.Max(r.MinY - Math.Max(a.Y, b.Y), Math.Min(a.Y, b.Y) - r.MaxY), 0);
                if (bx * bx + by * by >= d * d) continue;
                d = Math.Min(d, SegmentRect(a, b, r));
                if (d == 0) return 0;
            }
            return d;
        }

        public static bool PointInPolygon(Vec2 p, List<Vec2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vec2 a = poly[i], b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        public static bool RectInPolygon(Rect2 r, List<Vec2> poly)
        {
            return PointInPolygon(r.Center, poly) && PolylineRect(poly, r) > 0;
        }
    }
}
