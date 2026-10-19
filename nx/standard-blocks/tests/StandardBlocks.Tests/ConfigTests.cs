using StandardBlocks.Core;

namespace StandardBlocks.Tests;

public static class JsonTests
{
    public static void ParsesNestedValues()
    {
        var root = (Dictionary<string, object>)MiniJson.Parse(
            "{ \"a\": 1.5, \"b\": [true, false, null], \"c\": { \"d\": \"x\\\"y\\\\z\\u00D8\" }, \"e\": -2e3 }");
        Assert.Equal(1.5, (double)root["a"]);
        var b = (List<object>)root["b"];
        Assert.Equal(true, (bool)b[0]);
        Assert.Equal(false, (bool)b[1]);
        Assert.True(b[2] == null, "null element");
        Assert.Equal("x\"y\\zØ", (string)((Dictionary<string, object>)root["c"])["d"]);
        Assert.Equal(-2000.0, (double)root["e"]);
    }

    public static void ReportsLineAndColumn()
    {
        var ex = Assert.Throws<JsonException>(() => MiniJson.Parse("{\n  \"a\": 1,\n  \"b\": oops\n}"));
        Assert.Contains("line 3, column 8", ex.Message);
    }

    public static void RejectsTrailingCommaAndDuplicates()
    {
        Assert.Contains("comma", Assert.Throws<JsonException>(() => MiniJson.Parse("{ \"a\": 1, }")).Message);
        Assert.Contains("comma", Assert.Throws<JsonException>(() => MiniJson.Parse("[1, 2, ]")).Message);
        Assert.Contains("appears twice", Assert.Throws<JsonException>(() => MiniJson.Parse("{ \"a\": 1, \"a\": 2 }")).Message);
    }

    public static void RejectsSingleBackslashInPath()
    {
        // "C:\temp" - \t is a valid escape, but \x isn't; either way the user is told.
        var ex = Assert.Throws<JsonException>(() => MiniJson.Parse("{ \"p\": \"C:\\xyz\" }"));
        Assert.Contains("in paths, use /", ex.Message);
    }

    public static void AcceptsByteOrderMark()
    {
        var root = (Dictionary<string, object>)MiniJson.Parse("\uFEFF{ \"a\": 1 }");
        Assert.Equal(1.0, (double)root["a"]);
    }
}

public static class ConfigTests
{
    private static string ShippedConfigText()
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StandardBlocks.config.json"));
    }

    public static void ShippedConfigHasExactHoleTable()
    {
        // The DXF export macro labels holes by diameter, so these must not drift.
        var cfg = SbConfig.Parse(ShippedConfigText());
        Assert.Equal(0, cfg.Warnings.Count, "warnings: " + string.Join("; ", cfg.Warnings));
        Assert.Equal(3, cfg.HoleTypes.Count);
        Assert.Equal(5.8, cfg.FindHoleType("R6").Diameter);
        Assert.Equal(5.5, cfg.FindHoleType("M8").Diameter);
        Assert.Equal(8.2, cfg.FindHoleType("CLR8.2").Diameter);
    }

    public static void ShippedConfigMatchesDefaults()
    {
        var cfg = SbConfig.Parse(ShippedConfigText());
        Assert.Equal(250, cfg.ToolLayer);
        Assert.Equal("SB_TOOLS", cfg.ToolReferenceSet);
        Assert.Equal("MODEL", cfg.ModelReferenceSet);
        Assert.Equal("SB_BLOCK", cfg.BlockAttribute);
        Assert.Equal("SB_Hole_Type", cfg.HoleTypeAttribute);
        Assert.Equal(6.0, cfg.MaxPlateThickness);
        Assert.Equal(3.0, cfg.MinPlateAspectRatio);
        Assert.Equal(15.0, cfg.ExpectedToolLength);
        Assert.Equal("logs", cfg.LogFolder);
    }

    public static void MinimalConfigUsesDefaults()
    {
        var cfg = SbConfig.Parse("{ \"holeTypes\": [ { \"type\": \"R6\", \"diameter\": 5.8 } ] }");
        Assert.Equal(0.01, cfg.DiameterTolerance);
        Assert.Equal(250, cfg.ToolLayer);
        Assert.Equal("SB_", cfg.FeaturePrefix);
    }

    public static void UnknownSettingIsAWarningNotAnError()
    {
        var cfg = SbConfig.Parse("{ \"holeTypes\": [ { \"type\": \"R6\", \"diameter\": 5.8 } ], \"plates\": { \"maxThicknes\": 6 }, \"_note\": 1 }");
        Assert.Equal(1, cfg.Warnings.Count);
        Assert.Contains("plates.maxThicknes", cfg.Warnings[0]);
    }

    public static void MissingHoleTypesIsAnError()
    {
        Assert.Contains("holeTypes", Assert.Throws<ConfigException>(() => SbConfig.Parse("{ }")).Message);
        Assert.Contains("non-empty", Assert.Throws<ConfigException>(() => SbConfig.Parse("{ \"holeTypes\": [] }")).Message);
    }

    public static void BadHoleTypesAreErrors()
    {
        Assert.Contains("numeric \"diameter\"", Assert.Throws<ConfigException>(() =>
            SbConfig.Parse("{ \"holeTypes\": [ { \"type\": \"R6\", \"diameter\": \"5.8\" } ] }")).Message);
        Assert.Contains("between 0 and 100", Assert.Throws<ConfigException>(() =>
            SbConfig.Parse("{ \"holeTypes\": [ { \"type\": \"R6\", \"diameter\": -1 } ] }")).Message);
        Assert.Contains("listed twice", Assert.Throws<ConfigException>(() =>
            SbConfig.Parse("{ \"holeTypes\": [ { \"type\": \"R6\", \"diameter\": 5.8 }, { \"type\": \"r6\", \"diameter\": 6 } ] }")).Message);
        Assert.Contains("unknown setting \"dia\"", Assert.Throws<ConfigException>(() =>
            SbConfig.Parse("{ \"holeTypes\": [ { \"type\": \"R6\", \"dia\": 5.8, \"diameter\": 5.8 } ] }")).Message);
    }

    public static void OutOfRangeValuesAreErrors()
    {
        Assert.Contains("toolBodies.layer", Assert.Throws<ConfigException>(() =>
            SbConfig.Parse("{ \"holeTypes\": [ { \"type\": \"R6\", \"diameter\": 5.8 } ], \"toolBodies\": { \"layer\": 300 } }")).Message);
        Assert.Contains("whole number", Assert.Throws<ConfigException>(() =>
            SbConfig.Parse("{ \"holeTypes\": [ { \"type\": \"R6\", \"diameter\": 5.8 } ], \"toolBodies\": { \"layer\": 250.5 } }")).Message);
    }

    public static void BadPrefixIsAnError()
    {
        Assert.Contains("features.prefix", Assert.Throws<ConfigException>(() =>
            SbConfig.Parse("{ \"holeTypes\": [ { \"type\": \"R6\", \"diameter\": 5.8 } ], \"features\": { \"prefix\": \"SB-\" } }")).Message);
    }

    public static void LogFolderWithEscapedNewlineIsCaught()
    {
        // "C:\nx_settings" written with one backslash parses as C:<newline>x_settings.
        var ex = Assert.Throws<ConfigException>(() =>
            SbConfig.Parse("{ \"holeTypes\": [ { \"type\": \"R6\", \"diameter\": 5.8 } ], \"log\": { \"folder\": \"C:\\nx_settings\" } }"));
        Assert.Contains("C:/nx_settings/logs", ex.Message);
    }

    public static void InvalidJsonMessageSaysSo()
    {
        Assert.Contains("not valid JSON", Assert.Throws<ConfigException>(() => SbConfig.Parse("{ holeTypes: [] }")).Message);
    }
}

public static class HoleRulesTests
{
    private static readonly SbConfig Cfg = TestData.Config();

    public static void ExactDiameterIsOk()
    {
        var r = HoleRules.Check(Cfg, "R6", 5.8);
        Assert.Equal(HoleCheckStatus.Ok, r.Status);
        Assert.True(r.Message == null, "no note");
    }

    public static void WithinToleranceIsOk()
    {
        Assert.Equal(HoleCheckStatus.Ok, HoleRules.Check(Cfg, "R6", 5.81).Status, "exactly on the 0.01 tolerance");
        Assert.Equal(HoleCheckStatus.Ok, HoleRules.Check(Cfg, "CLR8.2", 8.195).Status);
    }

    public static void OutsideToleranceIsMismatch()
    {
        var r = HoleRules.Check(Cfg, "M8", 6.8);
        Assert.Equal(HoleCheckStatus.DiameterMismatch, r.Status);
        Assert.Contains("is dia 6.800 but M8 needs dia 5.500", r.Message);
        Assert.Equal(HoleCheckStatus.DiameterMismatch, HoleRules.Check(Cfg, "R6", 5.812).Status);
    }

    public static void UnknownTypeListsKnownTypes()
    {
        var r = HoleRules.Check(Cfg, "M10", 8.5);
        Assert.Equal(HoleCheckStatus.UnknownType, r.Status);
        Assert.Contains("R6, M8, CLR8.2", r.Message);
    }

    public static void MissingType()
    {
        Assert.Equal(HoleCheckStatus.MissingType, HoleRules.Check(Cfg, "  ", 5.8).Status);
        Assert.Equal(HoleCheckStatus.MissingType, HoleRules.Check(Cfg, null, 5.8).Status);
    }

    public static void WrongLetterCaseIsOkWithNote()
    {
        var r = HoleRules.Check(Cfg, "clr8.2", 8.2);
        Assert.Equal(HoleCheckStatus.Ok, r.Status);
        Assert.Equal("CLR8.2", r.Expected.Name);
        Assert.Contains("spells it \"CLR8.2\"", r.Message);
    }
}

public static class HoleRulesDiameterValueTests
{
    private static readonly SbConfig Cfg = TestData.Config();

    // 001-4032-CAD2072 stores SB_Hole_Type as a Real diameter: 5.8, 5.5, 8.2.
    public static void DiameterValueIsReadAsTheMatchingType()
    {
        var r = HoleRules.Check(Cfg, "5.8", 5.8);
        Assert.Equal(HoleCheckStatus.Ok, r.Status);
        Assert.Equal("R6", r.Expected.Name);
        Assert.Contains("holds a diameter (5.8) instead of a type name, so it was read as R6", r.Message);

        Assert.Equal("M8", HoleRules.Check(Cfg, "5.500000", 5.5).Expected.Name);
        Assert.Equal("CLR8.2", HoleRules.Check(Cfg, "8,2", 8.2).Expected.Name);
    }

    public static void DiameterValueStillCheckedAgainstTheBody()
    {
        var r = HoleRules.Check(Cfg, "5.8", 5.5);
        Assert.Equal(HoleCheckStatus.DiameterMismatch, r.Status);
        Assert.Contains("is dia 5.500 but R6 needs dia 5.800", r.Message);
    }

    public static void DiameterNotInTable()
    {
        var r = HoleRules.Check(Cfg, "6.8", 6.8);
        Assert.Equal(HoleCheckStatus.UnknownType, r.Status);
        Assert.Contains("no hole type has that diameter", r.Message);
    }

    public static void DiameterSharedByTwoTypesIsAmbiguous()
    {
        var cfg = SbConfig.Parse("{ \"holeTypes\": [ { \"type\": \"A\", \"diameter\": 5 }, { \"type\": \"B\", \"diameter\": 5 } ] }");
        Assert.Contains("more than one hole type", HoleRules.Check(cfg, "5", 5).Message);
    }
}

public static class SummaryTests
{
    private static readonly SbConfig Cfg = TestData.Config();

    public static void CountsInHoleTableOrder()
    {
        Assert.Equal("4 holes (2x R6, 2x CLR8.2)", Summary.HoleCounts(new[] { "CLR8.2", "R6", "CLR8.2", "R6" }, Cfg));
    }

    public static void SingularAndEmpty()
    {
        Assert.Equal("1 hole (1x M8)", Summary.HoleCounts(new[] { "M8" }, Cfg));
        Assert.Equal("0 holes", Summary.HoleCounts(Array.Empty<string>(), Cfg));
    }

    public static void UnknownTypesGoLast()
    {
        Assert.Equal("3 holes (1x M8, 1x A1, 1x Z9)", Summary.HoleCounts(new[] { "Z9", "M8", "A1" }, Cfg));
    }

    public static void CaseVariantsCountTogether()
    {
        Assert.Equal("2 holes (2x R6)", Summary.HoleCounts(new[] { "R6", "r6" }, Cfg));
    }
}

public static class CommandTests
{
    public static void BlankArgumentsMeanDefault()
    {
        // NX passes [""] when a journal is played directly.
        Assert.Equal("inspect", Commands.Resolve(new[] { "" }, null));
        Assert.Equal("inspect", Commands.Resolve(new[] { "  ", null }, null));
        Assert.Equal("inspect", Commands.Resolve(null, null));
        Assert.Equal("inspect", Commands.Resolve(Array.Empty<string>(), @"C:\nx_settings\StandardBlocks.cs"));
    }

    public static void JournalFileNameChoosesCommand()
    {
        Assert.Equal("inspect", Commands.Resolve(new[] { "" }, @"C:\nx_settings\SB_Inspect.cs"));
        Assert.Equal("sync", Commands.Resolve(new[] { "" }, "C:/nx_settings/sb_sync.cs"));
        Assert.Equal("addholes", Commands.Resolve(null, @"C:\nx_settings\SB_AddHoles.cs"));
        Assert.Equal("addholesmanual", Commands.Resolve(null, @"C:\nx_settings\SB_AddHolesManual.cs"));
        Assert.True(Commands.FromJournalName(@"C:\nx_settings\SB_.cs") == null, "SB_ alone");
        Assert.True(Commands.FromJournalName(@"C:\nx_settings\StandardBlocks.cs") == null, "main file");
    }

    public static void ArgumentBeatsFileName()
    {
        Assert.Equal("sync", Commands.Resolve(new[] { "", " Sync " }, @"C:\nx_settings\SB_Inspect.cs"));
    }

    public static void NormalizeTrimsAndLowercases()
    {
        Assert.Equal("inspect", Commands.Normalize(" INSPECT "));
        Assert.Equal("inspect", Commands.Normalize(""));
        Assert.Equal("inspect", Commands.Normalize(null));
    }
}

public static class JournalCopyTests
{
    // Each SB_<command>.cs must be an exact copy of StandardBlocks.cs: the
    // journal picks its command from its file name. If this fails, copy
    // StandardBlocks.cs over the SB_ file named in the message.
    public static void CommandCopiesMatchTheMainJournal()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "journals");
        byte[] main = File.ReadAllBytes(Path.Combine(dir, "StandardBlocks.cs"));
        string[] copies = Directory.GetFiles(dir, "SB_*.cs");
        Assert.True(copies.Length >= 2, "SB_Inspect.cs and SB_Detect.cs exist");
        foreach (string copy in copies)
            Assert.True(File.ReadAllBytes(copy).AsSpan().SequenceEqual(main), Path.GetFileName(copy) + " is identical to StandardBlocks.cs");
    }

    public static void EveryCommandHasACopy()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "journals");
        foreach (string command in new[] { "Inspect", "Detect", "AddHoles", "AddHolesManual" })
            Assert.True(File.Exists(Path.Combine(dir, "SB_" + command + ".cs")), "SB_" + command + ".cs exists");
    }
}

public static class WriteCheckTests
{
    private static PartStatus Part(string label, string role, bool fully = true, bool readOnly = false, string error = null) =>
        new PartStatus { Label = label, Role = role, FullyLoaded = fully, ReadOnly = readOnly, Error = error };

    public static void AllWritableMeansNoProblems()
    {
        Assert.Equal(0, WriteCheck.Problems(new[] { Part("0003984 rev A", "top assembly"), Part("0003985 rev A", "plate") }).Count);
        Assert.Equal("writable", Part("x", "plate").Describe());
    }

    public static void EachBadPartIsNamed()
    {
        var problems = WriteCheck.Problems(new[]
        {
            Part("0003984 rev A", "top assembly"),
            Part("0003985 rev A", "plate", readOnly: true),
            Part("0003986 rev A", "plate", fully: false),
            Part("0003987 rev A", "plate", error: "no access"),
        });
        Assert.Equal(3, problems.Count);
        Assert.Contains("0003985 rev A (plate) is read-only. Check it out in Teamcenter", problems[0]);
        Assert.Contains("0003986 rev A (plate) is only partly loaded", problems[1]);
        Assert.Contains("0003987 rev A (plate): NX couldn't say", problems[2]);
    }
}

public static class FeatureNameTests
{
    public static void NamesFollowTheAgreedPattern()
    {
        Assert.Equal("SB_001_4032_CAD2072_R6", FeatureNames.Subtract("SB_", "001-4032-CAD2072", "R6", 1));
        Assert.Equal("SB_001_4032_CAD2072_R6_LINK", FeatureNames.Link("SB_", "001-4032-CAD2072", "R6", 1));
        Assert.Equal("SB_001_4032_CAD2072_CLR8p2", FeatureNames.Subtract("SB_", "001-4032-CAD2072", "CLR8.2", 1));
        Assert.Equal("SB_001_4032_CAD2072_CLR8p2_2_LINK", FeatureNames.Link("SB_", "001-4032-CAD2072", "CLR8.2", 2));
    }

    public static void SecondCopyOfABlockGetsASuffix()
    {
        Assert.Equal("SB_001_4032_CAD2072_M8_2", FeatureNames.Subtract("SB_", "001-4032-CAD2072", "M8", 2));
        Assert.Equal("SB_001_4032_CAD2072_M8_2_LINK", FeatureNames.Link("SB_", "001-4032-CAD2072", "M8", 2));
    }

    public static void CopyNumberFollowsExistingInstances()
    {
        var existing = new List<string> { "aaa", "bbb" };
        Assert.Equal(1, FeatureNames.CopyNumber(new List<string>(), "aaa"), "first copy on the plate");
        Assert.Equal(2, FeatureNames.CopyNumber(existing, "bbb"), "already there as the 2nd");
        Assert.Equal(3, FeatureNames.CopyNumber(existing, "ccc"), "new third copy");
    }

    public static void SanitizeKeepsOnlySafeCharacters()
    {
        Assert.Equal("A_BpC_9", FeatureNames.Sanitize(" A-B.C 9 "));
        Assert.Equal("_", FeatureNames.Sanitize("Ø"));
    }
}

public static class OwnershipTests
{
    public static void TheCopySittingInTheHolesOwnsThem()
    {
        Assert.Equal(1, Ownership.Decide(new[] { 0, 3 }), "second copy sits in 3 holes");
        Assert.Equal(0, Ownership.Decide(new[] { 6, 0, 0 }));
    }

    public static void NoHolesMeansNoOwnerYet()
    {
        Assert.Equal(Ownership.NoHoles, Ownership.Decide(new[] { 0, 0 }));
    }

    public static void TwoCopiesInHolesIsAmbiguous()
    {
        Assert.Equal(Ownership.Ambiguous, Ownership.Decide(new[] { 2, 3 }));
    }
}
