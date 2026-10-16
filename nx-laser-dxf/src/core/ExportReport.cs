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
        public List<PlacedText> Labels = new List<PlacedText>();
        public string EtchText = "";
        public PlacedText Etch;
        /// <summary>Holes, cut-outs and plate outline (no text) - used to check hand edits.</summary>
        public Obstacles Geometry;
        public string EditSummary = "";
        /// <summary>Attribute table shown when the ID or revision can't be found.</summary>
        public List<string> AttributeDump = new List<string>();
        public string FileName = "";
        public string Date = DateTime.Now.ToString("dd/MM/yyyy");
        public string PdfFileName = "";
        public byte[] PdfBytes;
        public string PdfPath = "";
        public string PdfOutcome = "";
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
                var byLabel = new SortedDictionary<string, int>();
                var sources = new SortedDictionary<string, bool>();
                foreach (var h in r.Profile.Holes)
                {
                    if (h.Label.Length == 0) continue;
                    labelled++;
                    int n;
                    byLabel.TryGetValue(h.Label, out n);
                    byLabel[h.Label] = n + 1;
                    sources[h.Label + ": " + h.LabelSource] = true;
                }
                var lp = new List<string>();
                foreach (var kv in byLabel) lp.Add(kv.Key + " x" + kv.Value);
                L.Add("Labelled     : " + labelled + " of " + circles + " round holes" + (lp.Count > 0 ? "  (" + string.Join(", ", lp.ToArray()) + ")" : ""));
                foreach (var kv in sources) L.Add("               " + kv.Key);
                var unlabelled = new SortedDictionary<string, List<string>>();
                foreach (var h in r.Profile.Holes)
                {
                    if (h.Circle == null || h.Label.Length > 0) continue;
                    List<string> at;
                    if (!unlabelled.TryGetValue(h.Note, out at)) unlabelled[h.Note] = at = new List<string>();
                    at.Add("(" + Fmt.N(h.Center.X, 1) + ", " + Fmt.N(h.Center.Y, 1) + ")");
                }
                if (unlabelled.Count > 0)
                {
                    L.Add("Not labelled : (correct for plain clearance/fixing holes)");
                    foreach (var kv in unlabelled)
                        L.Add("               " + kv.Value.Count + " x " + kv.Key + "   at " +
                              string.Join(" ", kv.Value.GetRange(0, Math.Min(4, kv.Value.Count)).ToArray()) + (kv.Value.Count > 4 ? " ..." : ""));
                }
                if (r.Etch != null)
                    L.Add("Etch         : \"" + r.Etch.Text + "\"  " + Fmt.N(r.Etch.Height, 1) + " mm high, " + r.Etch.Position);
                L.Add("Entities     : " + r.Profile.Doc.Entities.Count + " (" + r.Profile.OuterEntityCount + " in the outer profile)");
            }
            if (r.Config != null)
                L.Add("DXF format   : " + r.Config.DxfVersion + ", text as " + (r.Config.TextMode == TextMode.Stroke ? "single-stroke vectors" : "DXF TEXT") + ", mm, layers " + r.Config.CutLayer.Name + " (colour " + r.Config.CutLayer.Color + ") / " +
                      r.Config.EtchLayer.Name + " (colour " + r.Config.EtchLayer.Color + ")");
            if (r.EditSummary.Length > 0) L.Add("Hand edits   : " + r.EditSummary);
            L.Add("Output       : " + (r.OutputPath.Length > 0 ? r.OutputPath : "-") + (r.Outcome.Length > 0 ? "   [" + r.Outcome + "]" : ""));
            if (r.PdfPath.Length > 0) L.Add("PDF sheet    : " + r.PdfPath + "   [" + r.PdfOutcome + "]");
            if (r.Warnings.Count > 0)
            {
                L.Add("");
                L.Add("Warnings (" + r.Warnings.Count + "):");
                foreach (string w in r.Warnings) L.Add("  - " + w);
            }
            if (r.AttributeDump.Count > 0)
            {
                L.Add("");
                L.AddRange(r.AttributeDump);
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
