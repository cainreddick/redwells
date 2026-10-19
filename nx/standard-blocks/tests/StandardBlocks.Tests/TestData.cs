using StandardBlocks.Core;

namespace StandardBlocks.Tests;

// Synthetic jig used by the geometry and detection tests (mm, assembly coordinates):
//
//   Base plate BASE-001     horizontal, 6 mm:  z = -6..0,   x, y = -100..100
//   Upright plate UPRIGHT-002 vertical, 3 mm:  x = 40..43,  y = -100..100, z = 0..100
//   Block                   x = 0..40, y = 0..30, z = 0..40
//                           sits on the base plate (contact face z = 0) and
//                           against the upright (contact face x = 40)
//
//   Vertical tool bodies (cut the base plate):     R6 at (10, 15), CLR8.2 at (30, 15)
//                                                  z = 5 (inside block) to z = -10
//   Horizontal tool bodies (cut the upright):      2x M8 at (y 10, z 20) and (y 20, z 20)
//                                                  x = 35 (inside block) to x = 50
public static class TestData
{
    public static SbConfig Config()
    {
        return SbConfig.Parse(@"{
            ""holeTypes"": [
                { ""type"": ""R6"", ""diameter"": 5.8 },
                { ""type"": ""M8"", ""diameter"": 5.5 },
                { ""type"": ""CLR8.2"", ""diameter"": 8.2 }
            ]
        }");
    }

    public static readonly Aabb BlockBox = new Aabb(new Vec3(0, 0, 0), new Vec3(40, 30, 40));

    public static PlateModel Plate(string label, Vec3 normal, Aabb box)
    {
        Vec3 n = normal.Normalized();
        // Range of n.x over the box corners.
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (double x in new[] { box.Min.X, box.Max.X })
        foreach (double y in new[] { box.Min.Y, box.Max.Y })
        foreach (double z in new[] { box.Min.Z, box.Max.Z })
        {
            double o = new Vec3(x, y, z).Dot(n);
            lo = Math.Min(lo, o);
            hi = Math.Max(hi, o);
        }
        return new PlateModel
        {
            Id = label,
            Label = label,
            Box = box,
            Frame = new PlateFrame { Normal = n, Low = lo, High = hi },
        };
    }

    public static PlateModel BasePlate() =>
        Plate("BASE-001 rev A", new Vec3(0, 0, 1), new Aabb(new Vec3(-100, -100, -6), new Vec3(100, 100, 0)));

    public static PlateModel UprightPlate() =>
        Plate("UPRIGHT-002 rev A", new Vec3(1, 0, 0), new Aabb(new Vec3(40, -100, 0), new Vec3(43, 100, 100)));

    // Faces of a plain cylinder from a to b, as NX would report them. With
    // capsReversed the end faces are listed in the opposite order.
    public static List<FaceSample> CylinderFaces(Vec3 a, Vec3 b, double radius, bool capsReversed = false)
    {
        Vec3 axis = (b - a).Normalized();
        var capA = new FaceSample(FaceKind.Plane, a + new Vec3(0.3, 0.2, 0.1).Cross(axis) * 0.5, -axis, 0);
        var capB = new FaceSample(FaceKind.Plane, b, axis, 0);
        return new List<FaceSample>
        {
            new FaceSample(FaceKind.Cylinder, a + axis * 3.7, -axis, radius), // axis point anywhere, sign arbitrary
            capsReversed ? capB : capA,
            capsReversed ? capA : capB,
        };
    }

    // Builds and orients a tool cylinder the same way the journal does.
    public static ToolCylinder Tool(Vec3 a, Vec3 b, double diameter, Aabb blockBox = null)
    {
        var res = ToolGeometry.Build(CylinderFaces(a, b, diameter / 2), 0.01, 0.5);
        Assert.True(res.Ok, "tool builds: " + string.Join("; ", res.Problems));
        return ToolGeometry.OrientFromBlock(res.Cylinder, blockBox ?? BlockBox, 0.01);
    }

    public static ToolCylinder VerticalR6() => Tool(new Vec3(10, 15, 5), new Vec3(10, 15, -10), 5.8);
    public static ToolCylinder VerticalClr() => Tool(new Vec3(30, 15, -10), new Vec3(30, 15, 5), 8.2); // listed outer end first
    public static ToolCylinder HorizontalM8a() => Tool(new Vec3(35, 10, 20), new Vec3(50, 10, 20), 5.5);
    public static ToolCylinder HorizontalM8b() => Tool(new Vec3(50, 20, 20), new Vec3(35, 20, 20), 5.5);

    // Stand-in for NX's solid interference check: does the tool's segment,
    // fattened by its radius, pass through the plate's box? (Slab clipping.)
    public static bool Intersects(ToolCylinder tool, PlateModel plate)
    {
        Aabb box = plate.Box.Expanded(tool.Radius);
        Vec3 p = tool.Start, d = tool.End - tool.Start;
        double t0 = 0, t1 = 1;
        double[] ps = { p.X, p.Y, p.Z }, ds = { d.X, d.Y, d.Z };
        double[] mins = { box.Min.X, box.Min.Y, box.Min.Z }, maxs = { box.Max.X, box.Max.Y, box.Max.Z };
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(ds[i]) < 1e-12)
            {
                if (ps[i] < mins[i] || ps[i] > maxs[i]) return false;
                continue;
            }
            double a = (mins[i] - ps[i]) / ds[i], b = (maxs[i] - ps[i]) / ds[i];
            if (a > b) (a, b) = (b, a);
            t0 = Math.Max(t0, a);
            t1 = Math.Min(t1, b);
            if (t0 > t1) return false;
        }
        return true;
    }

    public static List<ToolHit> Evaluate(ToolCylinder tool, params PlateModel[] plates) =>
        Detection.EvaluateTool(tool, plates, p => Intersects(tool, p), Config());
}
