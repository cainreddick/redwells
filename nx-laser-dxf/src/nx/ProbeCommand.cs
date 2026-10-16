// Diagnostic journal: select a plate face (or body) and dump what the journal sees.
// Writes a summary to the listing window, plus a full text dump and a JSON
// model of the body to the debug folder.
namespace LaserDxf.Nx
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NXOpen;
    using NXOpen.UF;
    using LaserDxf.Core;

    public static class ProbeCommand
    {
        public const string Version = "probe 1.1";

        public static void Run()
        {
            NxPlate.Run(RunInner);
        }

        static void RunInner(Session s, ListingWindow lw, List<string> log)
        {
            var cfgWarnings = new List<string>();
            LaserConfig cfg = NxPlate.LoadConfig(s, cfgWarnings);
            log.AddRange(cfgWarnings);

            PlateSelection sel = NxPlate.Select(s, log);
            if (sel == null) return;
            Part part = sel.Part;

            AnalysisOptions opt = cfg.ToAnalysisOptions();
            var ex = new NxBodyExtractor(UFSession.GetUFSession(), log) { ChordTol = cfg.ChordTolerance };
            ex.WantedAttributeTitles.AddRange(cfg.AttributeTitles());
            BodyModel m = ex.Extract(sel.Body, part);
            string note;
            int faceId = NxPlate.PlateFaceId(sel, ex, m, opt, log, out note);
            FaceAnalysis a = PlateAnalyzer.Analyze(m, faceId, opt);

            // ---------- output ----------
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string folder = NxUtil.EnsureFolder(Path.Combine(cfg.OutputFolder, "_debug"), log);
            string baseName = NxUtil.SafeFileName(m.PartName) + "_" + stamp;
            string dumpPath = Path.Combine(folder, baseName + "_probe.txt");
            string jsonPath = Path.Combine(folder, baseName + "_body.json");

            var head = new List<string>();
            head.Add("=== Laser DXF " + Version + " ===   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            head.Add("NX part      : Leaf=\"" + SafeGet(delegate { return part.Leaf; }) + "\"  Name=\"" + SafeGet(delegate { return part.Name; }) +
                     "\"  FullPath=\"" + SafeGet(delegate { return part.FullPath; }) + "\"");
            head.Add("Journal path : " + (NxReflect.ExecutingJournal(s) ?? "(Session.ExecutingJournal not available)"));
            head.Add("Config       : " + cfg.SourcePath);
            head.Add("Body         : " + m.Faces.Count + " faces, " + m.Edges.Count + " edges, " + m.Features.Count + " features, " + m.Attributes.Count + " attributes");

            List<string> summary = ProbeReport.Summary(m, a, note);
            var tail = new List<string>();
            if (a.Warnings.Count + log.Count > 0)
            {
                tail.Add("");
                tail.Add("Notes / warnings:");
                foreach (string w in a.Warnings) tail.Add("  WARNING: " + w);
                foreach (string l in log) tail.Add("  " + l);
            }

            var listing = new List<string>(head);
            listing.AddRange(summary);
            listing.Add("");
            int holeFeats = 0;
            foreach (var f in m.Features) if (f.ClassName == "HolePackage") holeFeats++;
            listing.Add("Hole features : " + holeFeats + "   (full properties are in the debug file)");
            listing.AddRange(tail);
            listing.Add("");

            var dump = new List<string>(listing);
            dump.AddRange(ProbeReport.FaceEdges(m, a));
            dump.Add("");
            dump.AddRange(ProbeReport.Features(m, true));
            dump.Add("");
            dump.AddRange(ProbeReport.Attributes(m));

            JObj json = m.ToJson();
            json.Add("selectedFaceId", (double)faceId);

            string written;
            try
            {
                NxUtil.WriteText(dumpPath, ProbeReport.Join(dump));
                NxUtil.WriteText(jsonPath, Json.Write(json));
                written = "Debug files  : " + dumpPath + "\n               " + jsonPath;
            }
            catch (Exception wex)
            {
                written = "Could not write debug files: " + wex.Message;
            }

            foreach (string l in listing) lw.WriteLine(l);
            lw.WriteLine("Part attributes: see the debug file (" + m.Attributes.Count + " found)");
            lw.WriteLine(written);
        }

        delegate string StringGetter();

        static string SafeGet(StringGetter g)
        {
            try { return g() ?? ""; }
            catch (Exception ex) { return "<" + ex.Message + ">"; }
        }
    }
}
