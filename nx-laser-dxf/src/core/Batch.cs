// Batch export (several plates from an assembly in one go): thickness check,
// per-plate results, the CSV summary for the laser shop and the batch report.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    public enum BatchStatus { Exported, Skipped, Failed, Cancelled }

    public sealed class BatchItem
    {
        public string PartLabel = "";
        public ExportRun Run;
        public BatchStatus Status = BatchStatus.Exported;
        public string Reason = "";
        /// <summary>Thickness written to the CSV: the attribute if present, otherwise the measured value.</summary>
        public string Thickness = "";
    }

    public static class Batch
    {
        /// <summary>Leading number of an attribute value such as "6", "6 mm", "6.0mm"; NaN if none.</summary>
        public static double ParseThickness(string s)
        {
            s = (s ?? "").Trim();
            int n = 0;
            while (n < s.Length && (char.IsDigit(s[n]) || s[n] == '.' || s[n] == ',')) n++;
            double d;
            if (n == 0 || !double.TryParse(s.Substring(0, n).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return double.NaN;
            return d;
        }

        /// <summary>Applies the thickness rule: the plate is exported only if its thickness attribute
        /// equals batch.requiredThickness (0 = no check). A missing attribute, or one that disagrees
        /// with the measured thickness, only gives a warning. Returns false with a reason to skip.</summary>
        public static bool CheckThickness(ExportRun run, LaserConfig c, out string reason, out string csvThickness)
        {
            reason = "";
            double measured = run.Analysis.Thickness * run.Profile.UnitScale;
            string attr = run.Identity.Thickness;
            double value = ParseThickness(attr);
            csvThickness = double.IsNaN(value) ? Fmt.N(measured, 2) : attr;
            double tol = c.ThicknessTolerance;

            if (double.IsNaN(value))
            {
                run.Warnings.Add("Thickness attribute " + c.ThicknessAttr + " is " + (attr.Length == 0 ? "missing" : "\"" + attr + "\" (not a number)") +
                                 " - measured " + Fmt.N(measured, 2) + " mm.");
                if (c.RequiredThickness > 0 && Math.Abs(measured - c.RequiredThickness) > tol)
                    run.Warnings.Add("Measured thickness " + Fmt.N(measured, 2) + " mm is not the required " + Fmt.N(c.RequiredThickness, 2) + " mm.");
                return true;
            }
            if (Math.Abs(value - measured) > tol)
                run.Warnings.Add("Thickness attribute says " + Fmt.N(value, 2) + " mm but the plate measures " + Fmt.N(measured, 2) + " mm.");
            if (c.RequiredThickness > 0 && Math.Abs(value - c.RequiredThickness) > tol)
            {
                reason = "thickness " + Fmt.N(value, 2) + " mm (" + c.ThicknessAttr.Title + "), batch is for " + Fmt.N(c.RequiredThickness, 2) + " mm plates";
                return false;
            }
            return true;
        }

        static string Csv(string s)
        {
            s = s ?? "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>CSV for the laser shop: one row per exported plate.</summary>
        public static string CsvSummary(List<BatchItem> items)
        {
            var sb = new StringBuilder();
            sb.Append("ID,Revision,Name,Material,Thickness,DXF path\r\n");
            foreach (var it in items)
            {
                if (it.Status != BatchStatus.Exported) continue;
                PartIdentity id = it.Run.Identity;
                sb.Append(Csv(id.Id)).Append(',').Append(Csv(id.RevisionRaw)).Append(',').Append(Csv(id.Name)).Append(',')
                  .Append(Csv(id.Material)).Append(',').Append(Csv(it.Thickness)).Append(',').Append(Csv(it.Run.OutputPath)).Append("\r\n");
            }
            return sb.ToString();
        }

        public static List<string> Summary(List<BatchItem> items, string version, string configPath, string csvPath)
        {
            var L = new List<string>();
            int exported = 0, skipped = 0, failed = 0, cancelled = 0;
            foreach (var it in items)
            {
                if (it.Status == BatchStatus.Exported) exported++;
                else if (it.Status == BatchStatus.Skipped) skipped++;
                else if (it.Status == BatchStatus.Failed) failed++;
                else cancelled++;
            }
            L.Add("=== Laser DXF batch " + version + " ===   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            L.Add("Config       : " + configPath);
            L.Add("Plates       : " + items.Count + " selected - " + exported + " exported, " + skipped + " skipped" +
                  (failed > 0 ? ", " + failed + " failed" : "") + (cancelled > 0 ? ", " + cancelled + " not done (batch cancelled)" : ""));
            if (csvPath != null) L.Add("CSV summary  : " + csvPath);
            L.Add("");
            foreach (var it in items)
            {
                ExportRun r = it.Run;
                string id = r != null && r.Identity != null ? r.Identity.Id + (r.Identity.RevisionRaw.Length > 0 ? "-" + r.Identity.RevisionRaw : "") : it.PartLabel;
                if (it.Status == BatchStatus.Exported)
                {
                    int round = 0, labelled = 0;
                    foreach (var h in r.Profile.Holes)
                    {
                        if (h.Circle == null) continue;
                        round++;
                        if (h.Label.Length > 0) labelled++;
                    }
                    L.Add("  OK       " + id + "   " + Fmt.N(r.Profile.Width, 1) + " x " + Fmt.N(r.Profile.Height, 1) + " mm, " +
                          round + " holes (" + labelled + " labelled)" + (r.EditSummary.Length > 0 ? ", edited" : "") +
                          (r.Warnings.Count > 0 ? ", " + r.Warnings.Count + " warning(s)" : "") + "  ->  " + r.OutputPath);
                    foreach (string w in r.Warnings) L.Add("             - " + w);
                }
                else
                {
                    string tag = it.Status == BatchStatus.Skipped ? "SKIPPED " : it.Status == BatchStatus.Failed ? "FAILED  " : "NOT DONE";
                    L.Add("  " + tag + " " + id + "   " + it.Reason);
                }
            }
            return L;
        }
    }
}
