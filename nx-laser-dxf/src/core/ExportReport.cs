// Text summary of an export run, for the listing window and the log file.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;

    public sealed class ExportRun
    {
        public string Version = "";
        public string ConfigPath = "";
        public string PartName = "";
        public string SelectionNote = "";
        public PartIdentity Identity;
        public FaceAnalysis Analysis;
        public ProfileResult Profile;
        public LaserConfig Config;
        public string FileName = "";
        public string DxfText = "";
        public string OutputPath = "";
        public string Outcome = "";
        public List<string> Warnings = new List<string>();
        public List<string> Notes = new List<string>();
    }

    public static class ExportReport
    {
        public static List<string> Summary(ExportRun r)
        {
            var L = new List<string>();
            L.Add("=== Laser DXF " + r.Version + " ===   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            L.Add("Config       : " + r.ConfigPath);
            L.Add("Part         : " + r.PartName);
            if (r.Identity != null)
            {
                string rev = r.Identity.RevisionRaw.Length == 0 ? "none"
                    : r.Identity.Revision == r.Identity.RevisionRaw ? "\"" + r.Identity.Revision + "\""
                    : "\"" + r.Identity.RevisionRaw + "\" -> " + r.Identity.Revision;
                L.Add("Part number  : " + r.Identity.Id + "   (from " + r.Identity.IdSource + "; revision " + rev + ")");
                if (r.Identity.Name.Length > 0) L.Add("Name         : " + r.Identity.Name);
            }
            if (r.Analysis != null && r.Profile != null)
            {
                FaceAnalysis a = r.Analysis;
                L.Add("Plate        : " + Fmt.N(r.Profile.Width, 2) + " x " + Fmt.N(r.Profile.Height, 2) + " mm, " +
                      Fmt.N(a.Thickness * r.Profile.UnitScale, 2) + " mm thick   (face #" + a.FaceId + ", " + r.SelectionNote + ")");
                L.Add("Orientation  : " + (a.LongestEdgeId >= 0 ? "X along the longest straight edge" : "X along the WCS / fallback direction") +
                      ", viewed from the selected face");

                int circles = 0, cutouts = 0;
                var groups = new SortedDictionary<double, int>();
                foreach (var h in r.Profile.Holes)
                {
                    if (double.IsNaN(h.CutDiameter)) { cutouts++; continue; }
                    circles++;
                    double key = Math.Round(h.CutDiameter, 3);
                    int c;
                    groups.TryGetValue(key, out c);
                    groups[key] = c + 1;
                }
                L.Add("Holes found  : " + circles + " round holes, " + cutouts + " other cut-outs" +
                      (r.Profile.SkippedLoops > 0 ? ", " + r.Profile.SkippedLoops + " not cut (do not go through)" : ""));
                if (groups.Count > 0)
                {
                    var parts = new List<string>();
                    foreach (var kv in groups) parts.Add("dia " + Fmt.N(kv.Key, 3) + " x" + kv.Value);
                    L.Add("Diameters    : " + string.Join(";  ", parts.ToArray()));
                }
                int labelled = 0;
                foreach (var h in r.Profile.Holes) if (h.Label.Length > 0) labelled++;
                L.Add("Labelled     : " + labelled);
                L.Add("Entities     : " + r.Profile.Doc.Entities.Count + " (" + r.Profile.OuterEntityCount + " in the outer profile)");
            }
            if (r.Config != null)
                L.Add("DXF format   : " + r.Config.DxfVersion + ", mm, layers " + r.Config.CutLayer.Name + " (colour " + r.Config.CutLayer.Color + ") / " +
                      r.Config.EtchLayer.Name + " (colour " + r.Config.EtchLayer.Color + ")");
            L.Add("Output       : " + (r.OutputPath.Length > 0 ? r.OutputPath : "-") + (r.Outcome.Length > 0 ? "   [" + r.Outcome + "]" : ""));
            if (r.Warnings.Count > 0)
            {
                L.Add("");
                L.Add("Warnings (" + r.Warnings.Count + "):");
                foreach (string w in r.Warnings) L.Add("  - " + w);
            }
            if (r.Notes.Count > 0)
            {
                L.Add("");
                L.Add("Notes:");
                foreach (string n in r.Notes) L.Add("  " + n);
            }
            return L;
        }
    }
}
