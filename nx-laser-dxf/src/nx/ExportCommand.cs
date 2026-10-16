// Main journal: select the plate, write the DXF, report.
namespace LaserDxf.Nx
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using NXOpen;
    using NXOpen.UF;
    using LaserDxf.Core;

    public static class ExportCommand
    {
        public const string Version = "0.5";

        public static void Run()
        {
            NxPlate.Run(RunInner);
        }

        static void RunInner(Session s, ListingWindow lw, List<string> log)
        {
            var cfgWarnings = new List<string>();
            LaserConfig cfg = NxPlate.LoadConfig(s, cfgWarnings);

            PlateSelection sel = NxPlate.Select(s, log);
            if (sel == null) return;

            if (cfg.ShowOptionsDialog && !OptionsDialog.Show(cfg, NxUtil.PartLabel(sel.Part)))
            {
                lw.WriteLine("Laser DXF: cancelled.");
                return;
            }

            Vec3? etchPick = null;
            if (string.Equals(cfg.EtchPlacement, "Pick", StringComparison.OrdinalIgnoreCase))
            {
                Vec3 p;
                if (sel.FromOccurrence)
                    cfgWarnings.Add("Picking the part-number position only works with the plate part displayed on its own - placed automatically.");
                else if (NxPlate.PickPoint("Click on the plate where the part number should go (Cancel = automatic)", out p))
                    etchPick = p;
                else
                    cfgWarnings.Add("No point picked - part number placed automatically.");
            }

            AnalysisOptions opt = cfg.ToAnalysisOptions();
            if (cfg.XAlign == XAlignMode.Hint)
            {
                Vec3 x;
                if (NxPlate.TryWcsX(s, sel.Part, out x)) opt.XHint = x;
                else cfgWarnings.Add("WCS alignment only works when the plate part is the displayed part - used the part's own X axis.");
            }

            var ex = new NxBodyExtractor(UFSession.GetUFSession(), log) { ChordTol = cfg.ChordTolerance };
            ex.WantedAttributeTitles.AddRange(cfg.AttributeTitles());
            BodyModel m = ex.Extract(sel.Body, sel.Part);
            string note;
            int faceId = NxPlate.PlateFaceId(sel, ex, m, opt, log, out note);

            ExportRun run = ExportPipeline.Prepare(m, faceId, cfg, opt, note, etchPick);
            run.Version = Version;

            if (cfg.ShowPreview)
            {
                var editor = new LayoutEditor(run);
                if (!PreviewWindow.Show(editor, run.Identity.Id + "  (" + run.FileName + ")"))
                {
                    lw.WriteLine("Laser DXF: cancelled in the preview - nothing written.");
                    return;
                }
                editor.Apply();
            }
            run.Warnings.InsertRange(0, cfgWarnings);
            run.Notes.AddRange(log);

            // ---------- write the DXF ----------
            string folder = NxUtil.EnsureFolder(cfg.OutputFolder, run.Warnings);
            string path = Path.Combine(folder, run.FileName);
            string pdfPath = run.PdfBytes != null ? Path.Combine(folder, run.PdfFileName) : "";
            run.OutputPath = path;
            bool write = true;
            var existing = new List<string>();
            if (File.Exists(path)) existing.Add(run.FileName);
            if (pdfPath.Length > 0 && File.Exists(pdfPath)) existing.Add(run.PdfFileName);
            if (existing.Count > 0 && cfg.AskBeforeOverwrite)
            {
                int answer = UI.GetUI().NXMessageBox.Show(NxPlate.Title, NXMessageBox.DialogType.Question,
                    string.Join(" and ", existing.ToArray()) + (existing.Count > 1 ? " already exist in\n" : " already exists in\n") + folder + "\n\nOverwrite?");
                write = answer == 1; // NXMessageBox: 1 = Yes, 2 = No
                run.Notes.Add("Overwrite prompt answer code: " + answer);
            }
            if (write)
            {
                try
                {
                    File.WriteAllText(path, run.DxfText, Encoding.GetEncoding(1252));
                    run.Outcome = "written";
                }
                catch (Exception wex)
                {
                    throw new PlateException("Could not write " + path + "\n" + wex.Message + "\n\nIs the file open in another program?");
                }
            }
            else run.Outcome = "NOT written - overwrite declined";

            if (pdfPath.Length > 0)
            {
                run.PdfPath = pdfPath;
                if (!write) run.PdfOutcome = "NOT written - overwrite declined";
                else
                {
                    try
                    {
                        File.WriteAllBytes(pdfPath, run.PdfBytes);
                        run.PdfOutcome = "written";
                    }
                    catch (Exception pex)
                    {
                        run.PdfOutcome = "NOT written";
                        run.Warnings.Add("Could not write the PDF sheet " + pdfPath + ": " + pex.Message + " (is it open in a PDF viewer?)");
                    }
                }
            }

            // ---------- debug output ----------
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            if (cfg.SaveBodyJson)
            {
                try
                {
                    string dbg = NxUtil.EnsureFolder(Path.Combine(folder, "_debug"), run.Notes);
                    JObj json = m.ToJson();
                    json.Add("selectedFaceId", (double)faceId);
                    string jp = Path.Combine(dbg, Path.GetFileNameWithoutExtension(run.FileName) + "_" + stamp + "_body.json");
                    NxUtil.WriteText(jp, Json.Write(json));
                    run.Notes.Add("Body model saved: " + jp);
                }
                catch (Exception jex) { run.Notes.Add("Could not save the body model: " + jex.Message); }
            }

            List<string> lines = ExportReport.Summary(run);
            if (cfg.WriteLog)
            {
                try
                {
                    string logDir = NxUtil.EnsureFolder(Path.Combine(folder, "_logs"), run.Notes);
                    string logPath = Path.Combine(logDir, "LaserDxf_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    var detail = new List<string>(lines);
                    detail.Add("");
                    detail.AddRange(ProbeReport.Summary(m, run.Analysis, note));
                    detail.Add("");
                    detail.AddRange(ProbeReport.FaceEdges(m, run.Analysis));
                    detail.Add("");
                    detail.Add(new string('-', 100));
                    detail.Add("");
                    File.AppendAllText(logPath, ProbeReport.Join(detail), new UTF8Encoding(false));
                    lines.Add("Log          : " + logPath);
                }
                catch (Exception lex) { lines.Add("Could not write the log file: " + lex.Message); }
            }

            foreach (string l in lines) lw.WriteLine(l);
        }
    }
}
