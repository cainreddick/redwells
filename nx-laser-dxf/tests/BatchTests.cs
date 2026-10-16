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
            TestMain.Check(Batch.CheckThickness(ok, c, out reason, out thk) && thk == "6" && ok.Warnings.Count == 0, "6 mm plate with Matl_Thk 6: exported, no warning");

            ExportRun ten = Plate(10, "10", "S180-2", "PLATE B");
            TestMain.Check(!Batch.CheckThickness(ten, c, out reason, out thk) && reason.Contains("10") && reason.Contains("6"), "10 mm plate skipped: " + reason);

            ExportRun missing = Plate(6, null, "S180-3", "PLATE C");
            TestMain.Check(Batch.CheckThickness(missing, c, out reason, out thk), "missing attribute: still exported");
            TestMain.Check(thk == "6" && missing.Warnings.Exists(w => w.Contains("missing")), "warned; CSV gets the measured 6");

            ExportRun lies = Plate(5, "6", "S180-4", "PLATE D");
            TestMain.Check(Batch.CheckThickness(lies, c, out reason, out thk), "attribute 6 matches the batch: exported");
            TestMain.Check(lies.Warnings.Exists(w => w.Contains("measures 5")), "but warned that it measures 5 mm");

            var noCheck = new LaserConfig { RequiredThickness = 0 };
            TestMain.Check(Batch.CheckThickness(Plate(10, "10", "S180-5", "E"), noCheck, out reason, out thk), "requiredThickness 0 = no thickness filter");
        }

        public static void TestCsvAndSummary()
        {
            var items = new List<BatchItem>();
            ExportRun a = Plate(6, "6", "S180-06068-47005", "BASE PLATE, LH \"MAIN\"");
            a.OutputPath = @"C:\Laser DXF\S180-06068-47005-1.dxf";
            items.Add(new BatchItem { PartLabel = "S180-06068-47005/1", Run = a, Thickness = "6" });
            ExportRun b = Plate(10, "10", "S180-06068-47006", "SIDE PLATE");
            items.Add(new BatchItem { PartLabel = "S180-06068-47006/1", Run = b, Status = BatchStatus.Skipped, Reason = "thickness 10 mm" });
            items.Add(new BatchItem { PartLabel = "S180-06068-47007/1", Status = BatchStatus.Failed, Reason = "not planar" });

            string csv = Batch.CsvSummary(items);
            string[] rows = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            TestMain.Check(rows.Length == 2, "header + 1 exported plate, got " + rows.Length);
            TestMain.Check(rows[0] == "ID,Revision,Name,Material,Thickness,DXF path", rows[0]);
            TestMain.Check(rows[1] == "S180-06068-47005,1,\"BASE PLATE, LH \"\"MAIN\"\"\",MILD STEEL,6,C:\\Laser DXF\\S180-06068-47005-1.dxf", rows[1]);

            string report = string.Join("\n", Batch.Summary(items, "0.6", "cfg", @"C:\Laser DXF\batch.csv").ToArray());
            TestMain.Check(report.Contains("3 selected - 1 exported, 1 skipped, 1 failed"), report);
            TestMain.Check(report.Contains("SKIPPED  S180-06068-47006-1   thickness 10 mm") && report.Contains("FAILED   S180-06068-47007/1   not planar"), report);
        }
    }
}
