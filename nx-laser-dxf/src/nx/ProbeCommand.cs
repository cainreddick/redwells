// Step 1: select a plate face (or body) and dump what the journal sees.
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
        public const string Version = "probe 1.0 (step 1)";
        const string Title = "Laser DXF";
        const string DebugFolder = @"C:\Laser DXF\_debug";

        public static void Run()
        {
            Session s = Session.GetSession();
            ListingWindow lw = s.ListingWindow;
            lw.Open();
            var log = new List<string>();
            try
            {
                RunInner(s, lw, log);
            }
            catch (PlateException ex)
            {
                lw.WriteLine("ABORTED: " + ex.Message);
                NxUtil.Message(Title, NXMessageBox.DialogType.Error, ex.Message);
            }
            catch (Exception ex)
            {
                lw.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
                lw.WriteLine(ex.StackTrace ?? "");
                foreach (string l in log) lw.WriteLine("  log: " + l);
                NxUtil.Message(Title, NXMessageBox.DialogType.Error, "Unexpected error:\n" + ex.Message + "\n\nDetails are in the listing window.");
            }
        }

        static void RunInner(Session s, ListingWindow lw, List<string> log)
        {
            UFSession uf = UFSession.GetUFSession();
            UI ui = UI.GetUI();

            if (s.Parts.Work == null) throw new PlateException("Open the plate part first.");

            Selection.MaskTriple[] mask =
            {
                new Selection.MaskTriple(UFConstants.UF_solid_type, 0, UFConstants.UF_UI_SEL_FEATURE_ANY_FACE),
                new Selection.MaskTriple(UFConstants.UF_solid_type, 0, UFConstants.UF_UI_SEL_FEATURE_BODY)
            };
            TaggedObject picked;
            Point3d cursor;
            Selection.Response resp = ui.SelectionManager.SelectTaggedObject(
                "Select the plate face to view from (or the plate body)", "Laser DXF - select plate",
                Selection.SelectionScope.AnyInAssembly, Selection.SelectionAction.ClearAndEnableSpecific,
                false, false, mask, out picked, out cursor);
            if (resp != Selection.Response.ObjectSelected && resp != Selection.Response.ObjectSelectedByName) return;

            Face face = picked as Face;
            Body body = picked as Body;
            string note;

            // Work on the part itself, not an assembly occurrence: coordinates are then the part's own.
            if (face != null && face.IsOccurrence)
            {
                face = (Face)face.Prototype;
                log.Add("Selection was an assembly occurrence; using the component part's face.");
            }
            if (body != null && body.IsOccurrence)
            {
                body = (Body)body.Prototype;
                log.Add("Selection was an assembly occurrence; using the component part's body.");
            }
            if (face != null)
            {
                if (face.SolidFaceType != Face.FaceType.Planar)
                    throw new PlateException("The selected face is not planar (it is " + face.SolidFaceType + "). Select the flat top or bottom face of the plate.");
                body = face.GetBody();
            }
            if (body == null) throw new PlateException("Nothing usable was selected.");
            if (!body.IsSolidBody) throw new PlateException("The selected body is not a solid body.");

            Part part = body.OwningPart as Part;
            if (part == null) throw new PlateException("Could not find the part that owns the selected body.");

            var opt = new AnalysisOptions();
            var ex = new NxBodyExtractor(uf, log);
            BodyModel m = ex.Extract(body, part);

            int faceId;
            if (face != null)
            {
                faceId = ex.FaceIdByTag[face.Tag];
                note = "selected directly";
            }
            else
            {
                faceId = PlateAnalyzer.PickPlateFace(m, opt, Vec3.UnitZ, log);
                note = "picked automatically from the body";
            }

            FaceAnalysis a = PlateAnalyzer.Analyze(m, faceId, opt);

            // ---------- output ----------
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string folder = NxUtil.EnsureFolder(DebugFolder, log);
            string baseName = NxUtil.SafeFileName(m.PartName) + "_" + stamp;
            string dumpPath = Path.Combine(folder, baseName + "_probe.txt");
            string jsonPath = Path.Combine(folder, baseName + "_body.json");

            var head = new List<string>();
            head.Add("=== Laser DXF " + Version + " ===   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            head.Add("NX part      : Leaf=\"" + SafeGet(delegate { return part.Leaf; }) + "\"  Name=\"" + SafeGet(delegate { return part.Name; }) +
                     "\"  FullPath=\"" + SafeGet(delegate { return part.FullPath; }) + "\"");
            head.Add("Journal path : " + (NxReflect.ExecutingJournal(s) ?? "(Session.ExecutingJournal not available)"));
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
