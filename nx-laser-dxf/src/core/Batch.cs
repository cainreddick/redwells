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
        /// <summary>Thickness for the CSV, e.g. "6mm": the attribute if present, otherwise the measured value.</summary>
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

        static string List(List<double> l)
        {
            var parts = new List<string>();
            foreach (double d in l) parts.Add(Fmt.N(d, 2));
            return string.Join(" or ", parts.ToArray());
        }

        static bool Allowed(double t, LaserConfig c)
        {
            foreach (double a in c.AllowedThicknesses) if (Math.Abs(t - a) <= c.ThicknessTolerance) return true;
            return false;
        }

        /// <summary>Applies the thickness rule: the plate is exported only if its thickness attribute is one
        /// of batch.allowedThicknesses (empty = no check). A missing attribute, or one that disagrees with
        /// the measured thickness, only gives a warning. Returns false with a reason to skip.</summary>
        public static bool CheckThickness(ExportRun run, LaserConfig c, out string reason, out string csvThickness)
        {
            reason = "";
            double measured = run.Analysis.Thickness * run.Profile.UnitScale;
            string attr = run.Identity.Thickness;
            double value = ParseThickness(attr);
            csvThickness = Fmt.N(double.IsNaN(value) ? measured : value, 2) + "mm"; // e.g. "6mm"
            bool check = c.AllowedThicknesses.Count > 0;

            if (double.IsNaN(value))
            {
                run.Warnings.Add("Thickness attribute " + c.ThicknessAttr + " is " + (attr.Length == 0 ? "missing" : "\"" + attr + "\" (not a number)") +
                                 " - measured " + Fmt.N(measured, 2) + " mm.");
                if (check && !Allowed(measured, c))
                    run.Warnings.Add("Measured thickness " + Fmt.N(measured, 2) + " mm is not " + List(c.AllowedThicknesses) + " mm.");
                return true;
            }
            if (Math.Abs(value - measured) > c.ThicknessTolerance)
                run.Warnings.Add("Thickness attribute says " + Fmt.N(value, 2) + " mm but the plate measures " + Fmt.N(measured, 2) + " mm.");
            if (check && !Allowed(value, c))
            {
                reason = "thickness " + Fmt.N(value, 2) + " mm (" + c.ThicknessAttr.Title + "), batch exports " + List(c.AllowedThicknesses) + " mm plates";
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

        /// <summary>CSV for the laser shop: one row per exported plate, columns from batch.csvColumns.</summary>
        public static string CsvSummary(List<BatchItem> items, LaserConfig c)
        {
            var sb = new StringBuilder();
            var headers = new List<string>();
            foreach (var col in c.CsvColumns) headers.Add(Csv(col.Key));
            sb.Append(string.Join(",", headers.ToArray())).Append("\r\n");
            // Grouped by thickness (thickest first), then by part number - the order the laser shop works in.
            var rows = new List<BatchItem>();
            foreach (var it in items) if (it.Status == BatchStatus.Exported) rows.Add(it);
            rows.Sort(delegate (BatchItem x, BatchItem y)
            {
                double tx = ParseThickness(x.Thickness), ty = ParseThickness(y.Thickness);
                if (Math.Abs(tx - ty) > 1e-9) return ty.CompareTo(tx);
                return string.Compare(x.Run.Identity.Id, y.Run.Identity.Id, StringComparison.OrdinalIgnoreCase);
            });
            foreach (var it in rows)
            {
                Dictionary<string, string> t = it.Run.Identity.Tokens();
                t["THICKNESS"] = it.Thickness;
                t["DXF"] = it.Run.OutputPath;
                t["DXFNAME"] = it.Run.FileName;
                var cells = new List<string>();
                foreach (var col in c.CsvColumns) cells.Add(Csv(Identity.Format(col.Value, t, null)));
                sb.Append(string.Join(",", cells.ToArray())).Append("\r\n");
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
