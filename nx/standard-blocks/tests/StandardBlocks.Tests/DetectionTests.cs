using StandardBlocks.Core;

namespace StandardBlocks.Tests;

// The synthetic jig in TestData: a block on a horizontal base plate and
// against a perpendicular upright plate.
public static class DetectionTests
{
    private static ToolHit HitOn(List<ToolHit> hits, PlateModel plate) => hits.Single(h => h.Plate == plate);

    public static void VerticalToolsCutOnlyTheBasePlate()
    {
        var basePlate = TestData.BasePlate();
        var upright = TestData.UprightPlate();
        foreach (var tool in new[] { TestData.VerticalR6(), TestData.VerticalClr() })
        {
            var hits = TestData.Evaluate(tool, basePlate, upright);
            Assert.Equal(HitStatus.Selected, HitOn(hits, basePlate).Status, "base plate");
            Assert.Equal(HitStatus.NotParallel, HitOn(hits, upright).Status, "upright plate");
            Assert.True(HitOn(hits, basePlate).FullyThrough, "passes fully through the base plate");
            Assert.Near(5, HitOn(hits, basePlate).Distance, 1e-9, "contact face is 5 mm from the inside end");
        }
    }

    public static void HorizontalToolsCutOnlyTheUpright()
    {
        var basePlate = TestData.BasePlate();
        var upright = TestData.UprightPlate();
        foreach (var tool in new[] { TestData.HorizontalM8a(), TestData.HorizontalM8b() })
        {
            var hits = TestData.Evaluate(tool, basePlate, upright);
            Assert.Equal(HitStatus.Selected, HitOn(hits, upright).Status, "upright plate");
            Assert.Equal(HitStatus.NotParallel, HitOn(hits, basePlate).Status, "base plate");
            Assert.True(HitOn(hits, upright).FullyThrough, "passes fully through the upright");
        }
    }

    public static void SummaryPerPlate()
    {
        var cfg = TestData.Config();
        var basePlate = TestData.BasePlate();
        var upright = TestData.UprightPlate();
        var tools = new (ToolCylinder Tool, string Type)[]
        {
            (TestData.VerticalR6(), "R6"),
            (TestData.VerticalClr(), "CLR8.2"),
            (TestData.HorizontalM8a(), "M8"),
            (TestData.HorizontalM8b(), "M8"),
        };

        var perPlate = new Dictionary<PlateModel, List<string>>();
        foreach (var (tool, type) in tools)
        {
            foreach (var hit in TestData.Evaluate(tool, basePlate, upright).Where(h => h.Status == HitStatus.Selected))
            {
                if (!perPlate.TryGetValue(hit.Plate, out var list)) perPlate[hit.Plate] = list = new List<string>();
                list.Add(type);
            }
        }

        Assert.Equal("BASE-001 rev A: 2 holes (1x R6, 1x CLR8.2)", basePlate.Label + ": " + Summary.HoleCounts(perPlate[basePlate], cfg));
        Assert.Equal("UPRIGHT-002 rev A: 2 holes (2x M8)", upright.Label + ": " + Summary.HoleCounts(perPlate[upright], cfg));
    }

    public static void ToolLyingInsideAPlateIsNotCutIntoIt()
    {
        // A horizontal tool whose axis runs through the base plate's thickness
        // touches it, but its axis isn't normal to the plate, so no hole.
        var basePlate = TestData.BasePlate();
        var tool = TestData.Tool(new Vec3(35, 10, -3), new Vec3(50, 10, -3), 5.5,
            new Aabb(new Vec3(0, 0, -10), new Vec3(40, 30, 40)));
        Assert.True(TestData.Intersects(tool, basePlate), "it does touch the plate");
        Assert.Equal(HitStatus.NotParallel, TestData.Evaluate(tool, basePlate)[0].Status);
    }

    public static void OnlyTheFirstOfStackedPlatesIsCut()
    {
        var basePlate = TestData.BasePlate();
        var spacer = TestData.Plate("SPACER-003 rev A", new Vec3(0, 0, 1), new Aabb(new Vec3(-100, -100, -12), new Vec3(100, 100, -6)));
        var hits = TestData.Evaluate(TestData.VerticalR6(), spacer, basePlate);
        Assert.Equal(HitStatus.Selected, HitOn(hits, basePlate).Status, "base plate (touching the block)");
        Assert.Equal(HitStatus.BehindAnotherPlate, HitOn(hits, spacer).Status, "spacer under it");
    }

    public static void ThickPlateIsSkipped()
    {
        var thick = TestData.Plate("THICK-004 rev A", new Vec3(0, 0, 1), new Aabb(new Vec3(-100, -100, -10), new Vec3(100, 100, 0)));
        Assert.Equal(HitStatus.TooThick, TestData.Evaluate(TestData.VerticalR6(), thick)[0].Status);
    }

    public static void MaxThicknessPlateIsAccepted()
    {
        var sixMm = TestData.Plate("SIX-005 rev A", new Vec3(0, 0, 1), new Aabb(new Vec3(-100, -100, -6.005), new Vec3(100, 100, 0)));
        Assert.Equal(HitStatus.Selected, TestData.Evaluate(TestData.VerticalR6(), sixMm)[0].Status, "6.005 is within tolerance of 6");
    }

    public static void ShortToolIsSelectedButFlaggedNotThrough()
    {
        var tool = TestData.Tool(new Vec3(10, 15, 5), new Vec3(10, 15, -4), 5.8);
        var hit = TestData.Evaluate(tool, TestData.BasePlate())[0];
        Assert.Equal(HitStatus.Selected, hit.Status);
        Assert.False(hit.FullyThrough, "ends 2 mm short of the plate's underside");
    }

    public static void NonIntersectingPlateIsIgnored()
    {
        var farPlate = TestData.Plate("FAR-006 rev A", new Vec3(0, 0, 1), new Aabb(new Vec3(200, 200, -6), new Vec3(300, 300, 0)));
        Assert.Equal(HitStatus.NoIntersection, TestData.Evaluate(TestData.VerticalR6(), farPlate)[0].Status);
    }

    public static void ReversedPlateNormalGivesSameAnswer()
    {
        var p = TestData.Plate("BASE-001 rev A", new Vec3(0, 0, -1), new Aabb(new Vec3(-100, -100, -6), new Vec3(100, 100, 0)));
        var hit = TestData.Evaluate(TestData.VerticalR6(), p)[0];
        Assert.Equal(HitStatus.Selected, hit.Status);
        Assert.True(hit.FullyThrough, "fully through");
        Assert.Near(5, hit.Distance);
    }

    public static void UnknownDirectionWithTwoCandidatesIsAmbiguous()
    {
        var basePlate = TestData.BasePlate();
        var spacer = TestData.Plate("SPACER-003 rev A", new Vec3(0, 0, 1), new Aabb(new Vec3(-100, -100, -12), new Vec3(100, 100, -6)));
        var tool = TestData.VerticalR6().Copy();
        tool.DirectionKnown = false;

        var hits = TestData.Evaluate(tool, basePlate, spacer);
        Assert.True(hits.All(h => h.Status == HitStatus.AmbiguousDirection), "neither is chosen");

        Assert.Equal(HitStatus.Selected, TestData.Evaluate(tool, basePlate)[0].Status, "one candidate needs no direction");
    }

    public static void BroadPhaseUsesTolerance()
    {
        var block = TestData.BlockBox;
        Assert.True(Detection.BoxesOverlap(block, TestData.UprightPlate().Box, 0), "touching at x = 40");
        var gap = new Aabb(new Vec3(40.5, 0, 0), new Vec3(43.5, 10, 10));
        Assert.True(Detection.BoxesOverlap(block, gap, 1.0), "0.5 gap within 1.0 tolerance");
        Assert.False(Detection.BoxesOverlap(block, gap, 0.2), "0.5 gap outside 0.2 tolerance");
    }
}

public static class ExistingHoleTests
{
    private static FaceSample Cyl(Vec3 p, Vec3 d, double r) => new FaceSample(FaceKind.Cylinder, p, d, r);

    public static void FindsCoaxialHoleOfAnySize()
    {
        var tool = TestData.VerticalR6(); // axis x = 10, y = 15
        var faces = new List<FaceSample>
        {
            new FaceSample(FaceKind.Plane, new Vec3(0, 0, 0), new Vec3(0, 0, 1), 0),
            Cyl(new Vec3(50, 50, -3), new Vec3(0, 0, 1), 3),     // another hole elsewhere
            Cyl(new Vec3(10, 15, -6), new Vec3(0, 0, -1), 3.25), // dia 6.5 on the same axis
        };
        Assert.Equal(2, ExistingHoles.FindCoaxial(tool, faces, 0.01, 0.5));
    }

    public static void SameRadiusOptionIgnoresOtherSizes()
    {
        var tool = TestData.VerticalR6(); // dia 5.8
        var faces = new List<FaceSample> { Cyl(new Vec3(10, 15, -6), new Vec3(0, 0, 1), 4.1) }; // dia 8.2 on the axis
        Assert.Equal(0, ExistingHoles.FindCoaxial(tool, faces, 0.01, 0.5));
        Assert.Equal(-1, ExistingHoles.FindCoaxial(tool, faces, 0.01, 0.5, true));
        faces.Add(Cyl(new Vec3(10, 15, -3), new Vec3(0, 0, -1), 2.9));
        Assert.Equal(1, ExistingHoles.FindCoaxial(tool, faces, 0.01, 0.5, true), "the dia 5.8 hole");
    }

    public static void IgnoresOffsetOrTiltedHoles()
    {
        var tool = TestData.VerticalR6();
        var faces = new List<FaceSample>
        {
            Cyl(new Vec3(10.5, 15, -3), new Vec3(0, 0, 1), 2.9), // 0.5 mm off the axis
            Cyl(new Vec3(10, 15, -3), new Vec3(1, 0, 0), 2.9),   // through the axis, but horizontal
        };
        Assert.Equal(-1, ExistingHoles.FindCoaxial(tool, faces, 0.01, 0.5));
    }
}

public static class PlannerTests
{
    private static readonly SbConfig Cfg = TestData.Config();

    private static ToolEvaluation Eval(string name, string type, ToolCylinder tool, params PlateModel[] plates) => new ToolEvaluation
    {
        BlockLabel = "001-4032-CAD2072 rev A",
        ToolName = name,
        HoleType = type,
        Tool = tool,
        Hits = TestData.Evaluate(tool, plates),
    };

    public static void GroupsHolesByPlate()
    {
        var basePlate = TestData.BasePlate();
        var upright = TestData.UprightPlate();
        var evals = new List<ToolEvaluation>
        {
            Eval("EXTRUDE(3)", "R6", TestData.VerticalR6(), basePlate, upright),
            Eval("EXTRUDE(4)", "M8", TestData.HorizontalM8a(), basePlate, upright),
            Eval("EXTRUDE(3)1", "CLR8.2", TestData.VerticalClr(), basePlate, upright),
            Eval("EXTRUDE(4)1", "M8", TestData.HorizontalM8b(), basePlate, upright),
        };

        var plans = Planner.ByPlate(evals);
        Assert.Equal(2, plans.Count);
        Assert.Equal("BASE-001 rev A: 2 holes (1x R6, 1x CLR8.2)", plans[0].Summary(Cfg));
        Assert.Equal("UPRIGHT-002 rev A: 2 holes (2x M8)", plans[1].Summary(Cfg));
        Assert.True(evals.All(e => Planner.Problem(e, Cfg) == null), "no problems");
    }

    public static void ToolThatReachesNoPlateIsReported()
    {
        var far = TestData.Plate("FAR-006 rev A", new Vec3(0, 0, 1), new Aabb(new Vec3(200, 200, -6), new Vec3(300, 300, 0)));
        var e = Eval("EXTRUDE(3)", "R6", TestData.VerticalR6(), far);
        Assert.Equal(0, Planner.ByPlate(new[] { e }).Count);
        Assert.Contains("001-4032-CAD2072 rev A, tool body EXTRUDE(3) (R6) doesn't pass through any plate", Planner.Problem(e, Cfg));
    }

    public static void ThickPlateIsReported()
    {
        var thick = TestData.Plate("THICK-004 rev A", new Vec3(0, 0, 1), new Aabb(new Vec3(-100, -100, -10), new Vec3(100, 100, 0)));
        Assert.Contains("only reaches THICK-004 rev A, which is 10 mm thick (the maximum is 6)",
            Planner.Problem(Eval("EXTRUDE(3)", "R6", TestData.VerticalR6(), thick), Cfg));
    }

    public static void BlindHoleIsReported()
    {
        var shortTool = TestData.Tool(new Vec3(10, 15, 5), new Vec3(10, 15, -4), 5.8);
        Assert.Contains("doesn't pass fully through BASE-001 rev A (6 mm thick)",
            Planner.Problem(Eval("EXTRUDE(3)", "R6", shortTool, TestData.BasePlate()), Cfg));
    }

    public static void AmbiguousDirectionIsReported()
    {
        var spacer = TestData.Plate("SPACER-003 rev A", new Vec3(0, 0, 1), new Aabb(new Vec3(-100, -100, -12), new Vec3(100, 100, -6)));
        var tool = TestData.VerticalR6().Copy();
        tool.DirectionKnown = false;
        Assert.Contains("passes through BASE-001 rev A and SPACER-003 rev A and it can't tell",
            Planner.Problem(Eval("EXTRUDE(3)", "R6", tool, TestData.BasePlate(), spacer), Cfg));
    }

    public static void DescribesEachCandidate()
    {
        var hits = TestData.Evaluate(TestData.VerticalR6(), TestData.BasePlate(), TestData.UprightPlate());
        Assert.Equal("HOLE, 5 mm from the block's inside end", Planner.Describe(hits[0]));
        Assert.Contains("isn't square", Planner.Describe(hits[1]));
    }
}
