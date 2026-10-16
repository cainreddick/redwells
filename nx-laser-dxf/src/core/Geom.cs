// Pure geometry: vectors, plate coordinate frame, small numeric helpers.
// No NX dependencies - compiled and unit-tested outside NX.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    public struct Vec3
    {
        public double X, Y, Z;

        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);
        public static readonly Vec3 UnitX = new Vec3(1, 0, 0);
        public static readonly Vec3 UnitY = new Vec3(0, 1, 0);
        public static readonly Vec3 UnitZ = new Vec3(0, 0, 1);

        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vec3 operator -(Vec3 a) { return new Vec3(-a.X, -a.Y, -a.Z); }
        public static Vec3 operator *(Vec3 a, double s) { return new Vec3(a.X * s, a.Y * s, a.Z * s); }
        public static Vec3 operator *(double s, Vec3 a) { return new Vec3(a.X * s, a.Y * s, a.Z * s); }
        public static Vec3 operator /(Vec3 a, double s) { return new Vec3(a.X / s, a.Y / s, a.Z / s); }

        public double Dot(Vec3 b) { return X * b.X + Y * b.Y + Z * b.Z; }
        public Vec3 Cross(Vec3 b) { return new Vec3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X); }
        public double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public double DistanceTo(Vec3 b) { return (this - b).Length; }

        public Vec3 Normalized()
        {
            double l = Length;
            return l > 1e-300 ? this / l : this;
        }

        public override string ToString()
        {
            return "(" + Fmt.N(X) + ", " + Fmt.N(Y) + ", " + Fmt.N(Z) + ")";
        }
    }

    public struct Vec2
    {
        public double X, Y;

        public Vec2(double x, double y) { X = x; Y = y; }

        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.X + b.X, a.Y + b.Y); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.X - b.X, a.Y - b.Y); }
        public static Vec2 operator *(Vec2 a, double s) { return new Vec2(a.X * s, a.Y * s); }

        public double Length { get { return Math.Sqrt(X * X + Y * Y); } }
        public double DistanceTo(Vec2 b) { return (this - b).Length; }

        public override string ToString()
        {
            return "(" + Fmt.N(X) + ", " + Fmt.N(Y) + ")";
        }
    }

    /// <summary>
    /// Right-handed frame on the selected face. Z is the outward face normal,
    /// so looking down -Z shows the plate exactly as seen from the selected side
    /// (never mirrored).
    /// </summary>
    public sealed class PlateFrame
    {
        public Vec3 Origin, XAxis, YAxis, ZAxis;

        public Vec2 ToLocal(Vec3 p)
        {
            Vec3 d = p - Origin;
            return new Vec2(d.Dot(XAxis), d.Dot(YAxis));
        }

        /// <summary>Signed height above the face plane (negative = into the material).</summary>
        public double Height(Vec3 p) { return (p - Origin).Dot(ZAxis); }

        public double Handedness { get { return XAxis.Cross(YAxis).Dot(ZAxis); } }
    }

    public struct Bounds2
    {
        public double MinX, MinY, MaxX, MaxY;

        public static Bounds2 Empty
        {
            get
            {
                Bounds2 b;
                b.MinX = b.MinY = double.MaxValue;
                b.MaxX = b.MaxY = double.MinValue;
                return b;
            }
        }

        public void Add(Vec2 p)
        {
            if (p.X < MinX) MinX = p.X;
            if (p.Y < MinY) MinY = p.Y;
            if (p.X > MaxX) MaxX = p.X;
            if (p.Y > MaxY) MaxY = p.Y;
        }

        public double Width { get { return MaxX - MinX; } }
        public double Height { get { return MaxY - MinY; } }
        public bool IsEmpty { get { return MaxX < MinX; } }
    }

    public static class GeomUtil
    {
        /// <summary>Newell's method: returns 2 * area * unit normal of a closed polygon.</summary>
        public static Vec3 NewellNormal(IList<Vec3> pts)
        {
            double x = 0, y = 0, z = 0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                Vec3 a = pts[i];
                Vec3 b = pts[(i + 1) % n];
                x += (a.Y - b.Y) * (a.Z + b.Z);
                y += (a.Z - b.Z) * (a.X + b.X);
                z += (a.X - b.X) * (a.Y + b.Y);
            }
            return new Vec3(x, y, z);
        }

        /// <summary>Signed area of a closed 2D polygon (CCW positive).</summary>
        public static double SignedArea(IList<Vec2> pts)
        {
            double a = 0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                Vec2 p = pts[i];
                Vec2 q = pts[(i + 1) % n];
                a += p.X * q.Y - q.X * p.Y;
            }
            return a * 0.5;
        }

        /// <summary>Circle through three 3D points. Returns false if they are collinear.</summary>
        public static bool CircleFrom3Points(Vec3 a, Vec3 b, Vec3 c, out Vec3 center, out double radius, out Vec3 normal)
        {
            Vec3 u = a - c;
            Vec3 v = b - c;
            Vec3 w = u.Cross(v);
            double w2 = w.Dot(w);
            double scale = Math.Max(u.Dot(u), v.Dot(v));
            if (scale <= 0 || w2 <= 1e-18 * scale * scale)
            {
                center = Vec3.Zero;
                radius = 0;
                normal = Vec3.Zero;
                return false;
            }
            Vec3 num = (v * u.Dot(u) - u * v.Dot(v)).Cross(w);
            center = c + num / (2 * w2);
            radius = center.DistanceTo(a);
            normal = w.Normalized();
            return true;
        }

        /// <summary>Distance from p to the infinite line through a and b.</summary>
        public static double DistanceToLine(Vec3 p, Vec3 a, Vec3 b)
        {
            Vec3 d = b - a;
            double len = d.Length;
            if (len < 1e-12) return p.DistanceTo(a);
            return (p - a).Cross(d).Length / len;
        }

        /// <summary>Projects v onto the plane with unit normal n.</summary>
        public static Vec3 ProjectOntoPlane(Vec3 v, Vec3 n)
        {
            return v - n * v.Dot(n);
        }
    }

    /// <summary>Invariant-culture number formatting (Windows locale must never leak into files).</summary>
    public static class Fmt
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string N(double v) { return N(v, 4); }

        public static string N(double v, int decimals)
        {
            if (double.IsNaN(v)) return "NaN";
            double r = Math.Round(v, decimals);
            if (r == 0) r = 0; // drop negative zero
            return r.ToString("0." + new string('#', decimals), Inv);
        }

        public static string R(double v)
        {
            return v.ToString("R", Inv);
        }
    }
}

namespace LaserDxf.Core
{
    using System.Collections.Generic;

    /// <summary>Insertion-ordered int set. (HashSet lives in System.Core, which an NX
    /// journal may not reference, so the shared code sticks to mscorlib + System.)</summary>
    public sealed class IntSet
    {
        readonly Dictionary<int, bool> map = new Dictionary<int, bool>();
        public readonly List<int> Items = new List<int>();

        public bool Add(int v)
        {
            if (map.ContainsKey(v)) return false;
            map[v] = true;
            Items.Add(v);
            return true;
        }

        public bool Contains(int v) { return map.ContainsKey(v); }
        public int Count { get { return Items.Count; } }
    }
}
