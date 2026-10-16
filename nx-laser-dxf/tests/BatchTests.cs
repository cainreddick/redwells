// Batch export: thickness rule, CSV summary and report.
namespace LaserDxf.Tests
{
    using System;
    using System.Collections.Generic;
    using LaserDxf.Core;

    public static class BatchTests
    {
        static ExportRun Plate(double thickness, string thkAttr, string id, string name)
        {
            var s = new Synth(thickness);
            s.Rectangle(120, 60);
            s.SimpleHole(20, 30, 5.8);
            var attrs = IdentityTests.Attrs();
            attrs.RemoveAll(a => a.Title == "Matl_Thk" || a.Title == "DB_PART_NO" || a.Title == "DB_PART_NAME");
            if (thkAttr != null) attrs.Add(new AttributeEntry { Category = "Materials", Title = "Matl_Thk", Value = thkAttr });
            attrs.Add(new AttributeEntry { Category = "ONE4_MCAD", Title = "DB_PART_NO", Value = id });
            attrs.Add(new AttributeEntry { Category = "ONE4_MCAD", Title = "DB_PART_NAME", Value = name });
            s.M.Attributes = attrs;
            var c = new LaserConfig { WritePdf = false };
            return ExportPipeline.Prepare(s.M, s.TopFace, c, c.ToAnalysisOptions(), "batch");
        }

        public static void TestParseThickness()
        {
            TestMain.Near(Batch.ParseThickness("6"), 6, 0, "6");
            TestMain.Near(Batch.ParseThickness("6 mm"), 6, 0, "6 mm");
            TestMain.Near(Batch.ParseThickness("6,0mm"), 6, 0, "6,0mm");
            TestMain.Check(double.IsNaN(Batch.ParseThickness("")) && double.IsNaN(Batch.ParseThickness("thick")), "not a number");
        }

        public static void TestThicknessRule()
        {
            var c = new LaserConfig();
            string reason, thk;

            ExportRun ok = Plate(6, "6", "S180-1", "PLATE A");
            TestMain.Check(Batch.CheckThickness(ok, c, out reason, out thk) && thk == "6mm" && ok.Warnings.Count == 0, "6 mm plate with Matl_Thk 6: exported, no warning");

            ExportRun three = Plate(3, "3", "S180-2", "PLATE B");
            TestMain.Check(Batch.CheckThickness(three, c, out reason, out thk) && three.Warnings.Count == 0, "3 mm plate exported too");

            ExportRun ten = Plate(10, "10", "S180-2", "PLATE B");
            TestMain.Check(!Batch.CheckThickness(ten, c, out reason, out thk) && reason.Contains("10") && reason.Contains("6 or 3"), "10 mm plate skipped: " + reason);

            ExportRun missing = Plate(6, null, "S180-3", "PLATE C");
            TestMain.Check(Batch.CheckThickness(missing, c, out reason, out thk), "missing attribute: still exported");
            TestMain.Check(thk == "6mm" && missing.Warnings.Exists(w => w.Contains("missing")), "warned; CSV gets the measured 6");

            ExportRun lies = Plate(5, "6", "S180-4", "PLATE D");
            TestMain.Check(Batch.CheckThickness(lies, c, out reason, out thk), "attribute 6 matches the batch: exported");
            TestMain.Check(lies.Warnings.Exists(w => w.Contains("measures 5")), "but warned that it measures 5 mm");

            var noCheck = new LaserConfig();
            noCheck.AllowedThicknesses.Clear();
            TestMain.Check(Batch.CheckThickness(Plate(10, "10", "S180-5", "E"), noCheck, out reason, out thk), "allowedThicknesses [] = no thickness filter");
        }

        /// <summary>The layout from the user's example sheet: 6 mm plates first, then 3 mm, each by ID.</summary>
        public static void TestCsvMatchesExampleSheet()
        {
            var plates = new[]
            {
                new[] { "S180-06068-47003", "RING SUPPORT - JIG VALVE ASSEMNLY", "3" },
                new[] { "S180-06068-47012", "VALVE SUPPORT LAMDA - JIG VALVE ASSEMBLY", "6" },
                new[] { "S180-06068-47002", "RING SUPPORT - JIG VALVE ASSEMBLY", "6" },
                new[] { "S180-06068-47008", "VALVE SUPPORT AXIS - JIG VALVE ASSEMBLY", "3" },
                new[] { "S180-06068-47005", "BASE PLATE - JIG VALVE ASSEMBLY", "6" },
                new[] { "S180-06068-47004", "VALVE SUPPORT - JIG VALVE ASSEMBLY", "3" },
            };
            var items = new List<BatchItem>();
            var c = new LaserConfig();
            foreach (var p in plates)
            {
                ExportRun r = Plate(double.Parse(p[2]), p[2], p[0], p[1]);
                r.Identity.Material = "";
                r.OutputPath = @"C:\Laser DXF\" + r.FileName;
                string reason, thk;
                TestMain.Check(Batch.CheckThickness(r, c, out reason, out thk), p[0] + " exported");
                items.Add(new BatchItem { Run = r, Thickness = thk });
            }
            string expected =
                "ID,Revision,Name,Material,Thickness,DXF path\r\n" +
                "S180-06068-47002-1,1,RING SUPPORT - JIG VALVE ASSEMBLY,,6mm,C:\\Laser DXF\\S180-06068-47002-1.dxf\r\n" +
                "S180-06068-47005-1,1,BASE PLATE - JIG VALVE ASSEMBLY,,6mm,C:\\Laser DXF\\S180-06068-47005-1.dxf\r\n" +
                "S180-06068-47012-1,1,VALVE SUPPORT LAMDA - JIG VALVE ASSEMBLY,,6mm,C:\\Laser DXF\\S180-06068-47012-1.dxf\r\n" +
                "S180-06068-47003-1,1,RING SUPPORT - JIG VALVE ASSEMNLY,,3mm,C:\\Laser DXF\\S180-06068-47003-1.dxf\r\n" +
                "S180-06068-47004-1,1,VALVE SUPPORT - JIG VALVE ASSEMBLY,,3mm,C:\\Laser DXF\\S180-06068-47004-1.dxf\r\n" +
                "S180-06068-47008-1,1,VALVE SUPPORT AXIS - JIG VALVE ASSEMBLY,,3mm,C:\\Laser DXF\\S180-06068-47008-1.dxf\r\n";
            string csv = Batch.CsvSummary(items, c);
            TestMain.Check(csv == expected, "CSV matches the example sheet:\n" + csv);
        }

        public static void TestCsvAndSummary()
        {
            var items = new List<BatchItem>();
            ExportRun a = Plate(6, "6", "S180-06068-47005", "BASE PLATE, LH \"MAIN\"");
            a.OutputPath = @"C:\Laser DXF\S180-06068-47005-1.dxf";
            items.Add(new BatchItem { PartLabel = "S180-06068-47005/1", Run = a, Thickness = "6mm" });
            ExportRun b = Plate(10, "10", "S180-06068-47006", "SIDE PLATE");
            items.Add(new BatchItem { PartLabel = "S180-06068-47006/1", Run = b, Status = BatchStatus.Skipped, Reason = "thickness 10 mm" });
            items.Add(new BatchItem { PartLabel = "S180-06068-47007/1", Status = BatchStatus.Failed, Reason = "not planar" });

            string csv = Batch.CsvSummary(items, new LaserConfig());
            string[] rows = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            TestMain.Check(rows.Length == 2, "header + 1 exported plate, got " + rows.Length);
            TestMain.Check(rows[0] == "ID,Revision,Name,Material,Thickness,DXF path", rows[0]);
            TestMain.Check(rows[1] == "S180-06068-47005-1,1,\"BASE PLATE, LH \"\"MAIN\"\"\",MILD STEEL,6mm,C:\\Laser DXF\\S180-06068-47005-1.dxf", rows[1]);

            // Custom columns from the config, and a missing revision drops the "-".
            var custom = LaserConfig.Parse("{ \"batch\": { \"csvColumns\": [ { \"header\": \"Part\", \"value\": \"{ID}-{REV}\" }, { \"header\": \"File\", \"value\": \"{DXFNAME}\" } ] } }", "t", new List<string>());
            a.Identity.RevisionRaw = a.Identity.Revision = "";
            a.FileName = "S180-06068-47005.dxf";
            string csv2 = Batch.CsvSummary(items, custom);
            TestMain.Check(csv2 == "Part,File\r\nS180-06068-47005,S180-06068-47005.dxf\r\n", csv2);

            string report = string.Join("\n", Batch.Summary(items, "0.6", "cfg", @"C:\Laser DXF\batch.csv").ToArray());
            TestMain.Check(report.Contains("3 selected - 1 exported, 1 skipped, 1 failed"), report);
            TestMain.Check(report.Contains("SKIPPED  S180-06068-47006-1   thickness 10 mm") && report.Contains("FAILED   S180-06068-47007/1   not planar"), report);
        }
    }
}
