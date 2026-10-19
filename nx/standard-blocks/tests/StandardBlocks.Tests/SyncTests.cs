using StandardBlocks.Core;

namespace StandardBlocks.Tests;

public static class SyncPlannerTests
{
    private const string Assy = "0003984 rev A";   // as stored on features
    private const string AssyNumber = "0003984";    // as passed to the planner

    private static SyncGroup Group(string instance, string part = "001-4032-CAD2072", string type = "R6",
        string assembly = Assy, params string[] bodies) => new SyncGroup
    {
        Id = Guid.NewGuid().ToString("N"),
        PlateLabel = "0003985 rev A",
        Instance = instance,
        SourcePart = part,
        HoleType = type,
        Assembly = assembly,
        LinkBodies = bodies.Length > 0 ? bodies.ToList() : new List<string> { "EXTRUDE(3)", "EXTRUDE(3)2" },
    };

    private static SyncBlock Block(string instance, string part = "001-4032-CAD2072") => new SyncBlock
    {
        Instance = instance,
        Label = part + " rev A",
        Path = "0003984/A > " + part + "/A",
        PartNumber = part,
        ToolBodies = new List<string> { "EXTRUDE(3)", "EXTRUDE(3)1", "EXTRUDE(3)2" },
        ToolTypes = new List<string> { "R6", "M8", "R6" },
    };

    public static void BlockStillThereIsKept()
    {
        var d = SyncPlanner.Plan(new[] { Group("a") }, new[] { Block("a") }, AssyNumber);
        Assert.Equal(SyncAction.Keep, d[0].Action);
    }

    public static void DeletedBlocksHolesAreRemoved()
    {
        var d = SyncPlanner.Plan(new[] { Group("gone") }, new[] { Block("a") }, AssyNumber);
        Assert.Equal(SyncAction.RemoveDeleted, d[0].Action);
        Assert.Contains("no longer in the assembly", d[0].Reason);
    }

    public static void ReplacedVariantIsRebuilt()
    {
        var d = SyncPlanner.Plan(new[] { Group("a") }, new[] { Block("a", "001-4032-CAD2080") }, AssyNumber);
        Assert.Equal(SyncAction.Rebuild, d[0].Action);
        Assert.Contains("replaced by 001-4032-CAD2080 rev A", d[0].Reason);
    }

    public static void MissingToolBodyMeansRebuild()
    {
        var d = SyncPlanner.Plan(new[] { Group("a", bodies: new[] { "EXTRUDE(9)" }) }, new[] { Block("a") }, AssyNumber);
        Assert.Equal(SyncAction.Rebuild, d[0].Action);
        Assert.Contains("no longer has tool body EXTRUDE(9)", d[0].Reason);
    }

    public static void OtherAssemblysHolesAreLeftAlone()
    {
        var d = SyncPlanner.Plan(new[] { Group("gone", assembly: "0004000 rev A") }, new[] { Block("a") }, AssyNumber);
        Assert.Equal(SyncAction.LeaveOtherAssembly, d[0].Action);
    }

    public static void NewAssemblyRevisionIsStillTheSameAssembly()
    {
        var d = SyncPlanner.Plan(new[] { Group("a", assembly: "0003984 rev A") }, new[] { Block("a") }, "0003984");
        Assert.Equal(SyncAction.Keep, d[0].Action);
        Assert.True(SyncPlanner.SameAssembly("0003984 rev B", "0003984"), "rev B");
        Assert.False(SyncPlanner.SameAssembly("00039840 rev A", "0003984"), "a different number that starts the same");
        Assert.True(SyncPlanner.SameAssembly(null, "0003984"), "no attribute: assume this assembly");
    }

    public static void MisalignedHolesAreWarnedNotRemoved()
    {
        var g = Group("a");
        g.BodiesWithoutHole.Add("EXTRUDE(3)2");
        var d = SyncPlanner.Plan(new[] { g }, new[] { Block("a") }, AssyNumber);
        Assert.Equal(SyncAction.Keep, d[0].Action);
        var w = SyncPlanner.Misaligned(d);
        Assert.Equal(1, w.Count);
        Assert.Contains("tool body EXTRUDE(3)2 is no longer in a hole in 0003985 rev A", w[0]);
    }

    public static void BlockWithoutHolesIsReported()
    {
        var blocks = new[] { Block("a"), Block(null) };
        var d = SyncPlanner.Plan(new[] { Group("a", bodies: new[] { "EXTRUDE(3)", "EXTRUDE(3)1", "EXTRUDE(3)2" }) }, blocks, AssyNumber);
        var w = SyncPlanner.WithoutHoles(blocks, d);
        Assert.Equal(1, w.Count);
        Assert.Contains("has no holes yet", w[0]);
    }

    public static void ToolBodyWithoutHoleIsReported()
    {
        var blocks = new[] { Block("a") };
        var d = SyncPlanner.Plan(new[] { Group("a") }, blocks, AssyNumber);   // links EXTRUDE(3) and EXTRUDE(3)2 only
        var w = SyncPlanner.WithoutHoles(blocks, d);
        Assert.Equal(1, w.Count);
        Assert.Contains("1 tool body has no hole: EXTRUDE(3)1 (M8)", w[0]);
    }

    public static void HolesBeingRemovedDontCount()
    {
        var blocks = new[] { Block("a", "001-4032-CAD2080") };
        var d = SyncPlanner.Plan(new[] { Group("a", bodies: new[] { "EXTRUDE(3)", "EXTRUDE(3)1", "EXTRUDE(3)2" }) }, blocks, AssyNumber);
        Assert.Equal(SyncAction.Rebuild, d[0].Action);
        Assert.Contains("has no holes yet", SyncPlanner.WithoutHoles(blocks, d)[0]);
    }
}
