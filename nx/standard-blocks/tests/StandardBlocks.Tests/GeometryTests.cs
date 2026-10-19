using StandardBlocks.Core;

namespace StandardBlocks.Tests;

public static class VectorTests
{
    public static void ParallelIgnoresSign()
    {
        Assert.True(Geo.AreParallel(new Vec3(0, 0, 1), new Vec3(0, 0, -5), 0.5), "anti-parallel counts as parallel");
        Assert.False(Geo.AreParallel(new Vec3(0, 0, 1), new Vec3(1, 0, 0), 0.5), "perpendicular");
    }

    public static void ParallelRespectsAngleTolerance()
    {
        double r = Math.PI / 180;
        Assert.True(Geo.AreParallel(new Vec3(0, 0, 1), new Vec3(Math.Sin(0.4 * r), 0, Math.Cos(0.4 * r)), 0.5), "0.4 deg");
        Assert.False(Geo.AreParallel(new Vec3(0, 0, 1), new Vec3(Math.Sin(1.0 * r), 0, Math.Cos(1.0 * r)), 0.5), "1 deg");
    }

    public static void DistanceToLine()
    {
        Assert.Near(3, Geo.DistanceToLine(new Vec3(3, 0, 7), new Vec3(0, 0, 0), new Vec3(0, 0, 2)));
    }

    public static void UfBoxOrder()
    {
        var b = Aabb.FromUfBox(new double[] { 1, 2, 3, 4, 5, 6 });
        Assert.Near(new Vec3(1, 2, 3), b.Min);
        Assert.Near(new Vec3(4, 5, 6), b.Max);
    }

    public static void NumberFormatting()
    {
        Assert.Equal("0", Text.Num(-0.0001));
        Assert.Equal("5.8", Text.Num(5.8));
        Assert.Equal("5.800", Text.Num3(5.8));
        Assert.Equal("(1.5, 0, -10)", new Vec3(1.5, -0.0001, -10).ToString());
    }
}

public static class ToolGeometryTests
{
    public static void BuildsPlainCylinder()
    {
        var res = ToolGeometry.Build(TestData.CylinderFaces(new Vec3(10, 15, 5), new Vec3(10, 15, -10), 2.9), 0.01, 0.5);
        Assert.True(res.Ok, string.Join("; ", res.Problems));
        var c = res.Cylinder;
        Assert.Near(15, c.Length);
        Assert.Near(5.8, c.Diameter);
        Assert.False(c.DirectionKnown, "direction not known until oriented");
        // Start/End are the two end-face centres, in some order.
        bool ok = ((c.Start - new Vec3(10, 15, 5)).Length < 1e-9 && (c.End - new Vec3(10, 15, -10)).Length < 1e-9)
               || ((c.End - new Vec3(10, 15, 5)).Length < 1e-9 && (c.Start - new Vec3(10, 15, -10)).Length < 1e-9);
        Assert.True(ok, "end centres on the axis");
    }

    public static void AcceptsCylinderSplitIntoTwoFaces()
    {
        var faces = TestData.CylinderFaces(new Vec3(0, 0, 0), new Vec3(0, 0, 15), 2.75);
        faces.Add(new FaceSample(FaceKind.Cylinder, new Vec3(0, 0, 9), new Vec3(0, 0, 1), 2.75));
        Assert.True(ToolGeometry.Build(faces, 0.01, 0.5).Ok, "two coaxial cylindrical faces");
    }

    public static void RejectsNonCoaxialCylinders()
    {
        var faces = TestData.CylinderFaces(new Vec3(0, 0, 0), new Vec3(0, 0, 15), 2.75);
        faces.Add(new FaceSample(FaceKind.Cylinder, new Vec3(1, 0, 9), new Vec3(0, 0, 1), 2.75));
        var res = ToolGeometry.Build(faces, 0.01, 0.5);
        Assert.False(res.Ok, "offset second cylinder");
        Assert.Contains("not one straight cylinder", res.Problems[0]);
    }

    public static void RejectsWrongNumberOfEndFaces()
    {
        var faces = TestData.CylinderFaces(new Vec3(0, 0, 0), new Vec3(0, 0, 15), 2.75);
        faces.Add(new FaceSample(FaceKind.Plane, new Vec3(0, 0, 7), new Vec3(0, 0, 1), 0));
        Assert.Contains("exactly 2 flat end faces but has 3", ToolGeometry.Build(faces, 0.01, 0.5).Problems[0]);
    }

    public static void RejectsChamfersAndSlantedEnds()
    {
        var faces = TestData.CylinderFaces(new Vec3(0, 0, 0), new Vec3(0, 0, 15), 2.75);
        faces.Add(new FaceSample(FaceKind.Other, new Vec3(0, 0, 15), new Vec3(0, 0, 0), 0));
        Assert.Contains("neither flat nor cylindrical", ToolGeometry.Build(faces, 0.01, 0.5).Problems[0]);

        faces = TestData.CylinderFaces(new Vec3(0, 0, 0), new Vec3(0, 0, 15), 2.75);
        faces[2] = new FaceSample(FaceKind.Plane, new Vec3(0, 0, 15), new Vec3(0, 0.2, 1), 0);
        Assert.Contains("not square", ToolGeometry.Build(faces, 0.01, 0.5).Problems[0]);
    }

    public static void RejectsBodyWithoutCylinder()
    {
        var faces = new List<FaceSample> { new FaceSample(FaceKind.Plane, new Vec3(0, 0, 0), new Vec3(0, 0, 1), 0) };
        Assert.Contains("no cylindrical face", ToolGeometry.Build(faces, 0.01, 0.5).Problems[0]);
    }

    public static void OrientsInsideEndAsStart()
    {
        // Listed either way round, Start ends up at z = 5 (inside the block).
        foreach (bool reversed in new[] { false, true })
        {
            var res = ToolGeometry.Build(TestData.CylinderFaces(new Vec3(10, 15, -10), new Vec3(10, 15, 5), 2.9, reversed), 0.01, 0.5);
            var c = ToolGeometry.OrientFromBlock(res.Cylinder, TestData.BlockBox, 0.01);
            Assert.True(c.DirectionKnown, "direction known");
            Assert.Near(new Vec3(10, 15, 5), c.Start, 1e-9, "start inside the block");
            Assert.Near(new Vec3(0, 0, -1), c.Direction, 1e-9, "points out of the block");
        }
    }

    public static void DirectionUnknownWhenBothEndsInsideBlock()
    {
        var res = ToolGeometry.Build(TestData.CylinderFaces(new Vec3(10, 15, 5), new Vec3(10, 15, 20), 2.9), 0.01, 0.5);
        Assert.False(ToolGeometry.OrientFromBlock(res.Cylinder, TestData.BlockBox, 0.01).DirectionKnown, "both inside");
        Assert.False(ToolGeometry.OrientFromBlock(res.Cylinder, null, 0.01).DirectionKnown, "no block box");
    }

    public static void OrientLikeFollowsMatchingFaces()
    {
        // Block coordinates: caps in one order. Assembly coordinates: the same
        // faces (same indices), moved; orientation must carry across.
        var blockFaces = TestData.CylinderFaces(new Vec3(10, 15, -10), new Vec3(10, 15, 5), 2.9);
        var block = ToolGeometry.OrientFromBlock(ToolGeometry.Build(blockFaces, 0.01, 0.5).Cylinder, TestData.BlockBox, 0.01);

        // Assembly: block rotated 180 deg about X and moved, so z flips.
        var assyFaces = blockFaces.Select(f => new FaceSample(f.Kind,
            new Vec3(f.Point.X + 100, -f.Point.Y, -f.Point.Z + 50), new Vec3(f.Direction.X, -f.Direction.Y, -f.Direction.Z), f.Radius)).ToList();
        var assy = ToolGeometry.OrientLike(ToolGeometry.Build(assyFaces, 0.01, 0.5).Cylinder, block);

        Assert.True(assy.DirectionKnown, "direction carried across");
        Assert.Near(new Vec3(110, -15, 45), assy.Start, 1e-9, "inside end, moved");
        Assert.Near(new Vec3(0, 0, 1), assy.Direction, 1e-9, "direction, rotated");
    }

    public static void LengthCheck()
    {
        var cfg = TestData.Config();
        Assert.True(ToolGeometry.CheckLength(TestData.VerticalR6(), cfg) == null, "15 mm is standard");
        var shortTool = TestData.Tool(new Vec3(0, 0, 5), new Vec3(0, 0, -9), 5.8);
        Assert.Contains("is 14.000 mm long; the standard is 15 mm", ToolGeometry.CheckLength(shortTool, cfg));
    }
}

public static class PlateGeometryTests
{
    private static List<PlanarFace> BoxFaces(Vec3 min, Vec3 max)
    {
        return new List<PlanarFace>
        {
            new PlanarFace(new Vec3(0, 0, 1), max),
            new PlanarFace(new Vec3(0, 0, -1), min),
            new PlanarFace(new Vec3(1, 0, 0), max),
            new PlanarFace(new Vec3(-1, 0, 0), min),
            new PlanarFace(new Vec3(0, 1, 0), max),
            new PlanarFace(new Vec3(0, -1, 0), min),
        };
    }

    public static void HorizontalPlate()
    {
        var f = PlateGeometry.FromPlanarFaces(BoxFaces(new Vec3(-100, -100, -6), new Vec3(100, 100, 0)), 0.5, out string problem);
        Assert.True(f != null, "is a plate: " + problem);
        Assert.True(Geo.AreParallel(f.Normal, new Vec3(0, 0, 1), 0.01), "normal is Z");
        Assert.Near(6, f.Thickness);
    }

    public static void UprightPlate()
    {
        var f = PlateGeometry.FromPlanarFaces(BoxFaces(new Vec3(40, -100, 0), new Vec3(43, 100, 100)), 0.5, out _);
        Assert.True(Geo.AreParallel(f.Normal, new Vec3(1, 0, 0), 0.01), "normal is X");
        Assert.Near(3, f.Thickness);
    }

    public static void SlotsAndTabsDontConfuseThickness()
    {
        // 200 x 100 x 6 plate with a 6.2 mm slot (faces at x = 50 and 56.2)
        // and a 4 mm deep notch step (faces at y = 96).
        var faces = BoxFaces(new Vec3(0, 0, 0), new Vec3(200, 100, 6));
        faces.Add(new PlanarFace(new Vec3(1, 0, 0), new Vec3(50, 50, 3)));
        faces.Add(new PlanarFace(new Vec3(-1, 0, 0), new Vec3(56.2, 50, 3)));
        faces.Add(new PlanarFace(new Vec3(0, -1, 0), new Vec3(150, 96, 3)));
        var f = PlateGeometry.FromPlanarFaces(faces, 0.5, out _);
        Assert.True(Geo.AreParallel(f.Normal, new Vec3(0, 0, 1), 0.01), "normal is Z");
        Assert.Near(6, f.Thickness);
    }

    public static void AngledProfileEdgesAreFine()
    {
        var faces = BoxFaces(new Vec3(0, 0, 0), new Vec3(200, 100, 5));
        var d = new Vec3(1, 1, 0).Normalized();
        faces.Add(new PlanarFace(d, new Vec3(190, 100, 2)));
        var f = PlateGeometry.FromPlanarFaces(faces, 0.5, out _);
        Assert.Near(5, f.Thickness);
    }

    public static void SingleFaceIsNotAPlate()
    {
        var f = PlateGeometry.FromPlanarFaces(new List<PlanarFace> { new PlanarFace(new Vec3(0, 0, 1), new Vec3(0, 0, 0)) }, 0.5, out string problem);
        Assert.True(f == null, "not a plate");
        Assert.Contains("not a flat plate", problem);
    }

    public static void BlocksAndBoltsAreNotPlateLike()
    {
        var cube = new Aabb(new Vec3(0, 0, 0), new Vec3(40, 30, 40));
        var cubeFrame = PlateGeometry.FromPlanarFaces(BoxFaces(cube.Min, cube.Max), 0.5, out _);
        Assert.False(PlateGeometry.IsPlateLike(cubeFrame, cube, 3), "40 x 30 x 40 block");

        var plate = new Aabb(new Vec3(0, 0, 0), new Vec3(200, 100, 6));
        var plateFrame = PlateGeometry.FromPlanarFaces(BoxFaces(plate.Min, plate.Max), 0.5, out _);
        Assert.True(PlateGeometry.IsPlateLike(plateFrame, plate, 3), "200 x 100 x 6 plate");

        var thickBase = new Aabb(new Vec3(0, 0, 0), new Vec3(300, 300, 10));
        var thickFrame = PlateGeometry.FromPlanarFaces(BoxFaces(thickBase.Min, thickBase.Max), 0.5, out _);
        Assert.True(PlateGeometry.IsPlateLike(thickFrame, thickBase, 3), "10 mm base is still a plate (rejected later as too thick)");
    }
}
