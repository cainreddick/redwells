// One plate's export, shared by the single-plate and batch journals:
// extract -> analyse/layout (ExportPipeline) -> write the files, debug copy and log.
namespace LaserDxf.Nx
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using NXOpen;
    using NXOpen.UF;
    using LaserDxf.Core;

    public sealed class PlateJob
    {
        public PlateSelection Selection;
        public BodyModel Model;
        public int FaceId;
        public string Note = "";
        public ExportRun Run;
        public List<string> Log = new List<string>();
    }

    public static class PlateJobs
    {
        /// <summary>Reads the plate from NX and lays it out (no files written).</summary>
        public static PlateJob Prepare(Session s, LaserConfig cfg, PlateSelection sel, Vec3? etchPick, List<string> warnings)
        {
            var job = new PlateJob { Selection = sel };
            AnalysisOptions opt = cfg.ToAnalysisOptions();
            if (cfg.XAlign == XAlignMode.Hint)
            {
                Vec3 x;
                if (NxPlate.TryWcsX(s, sel.Part, out x)) opt.XHint = x;
                else warnings.Add("WCS alignment only works when the plate part is the displayed part - used the part's own X axis.");
            }

            var ex = new NxBodyExtractor(UFSession.GetUFSession(), job.Log) { ChordTol = cfg.ChordTolerance };
            ex.WantedAttributeTitles.AddRange(cfg.AttributeTitles());
            job.Model = ex.Extract(sel.Body, sel.Part);
            job.FaceId = NxPlate.PlateFaceId(sel, ex, job.Model, opt, job.Log, out job.Note);
            job.Run = ExportPipeline.Prepare(job.Model, job.FaceId, cfg, opt, job.Note, etchPick);
            job.Run.Warnings.InsertRange(0, warnings);
            return job;
        }

        /// <summary>Output files of the job that already exist in the folder.</summary>
        public static List<string> Existing(PlateJob job, string folder)
        {
            var l = new List<string>();
            if (File.Exists(Path.Combine(folder, job.Run.FileName))) l.Add(job.Run.FileName);
            if (job.Run.PdfBytes != null && File.Exists(Path.Combine(folder, job.Run.PdfFileName))) l.Add(job.Run.PdfFileName);
            return l;
        }

        /// <summary>Writes the DXF (and the PDF sheet if the run has one) to folder, and the debug body
        /// model under supportFolder\_debug. write = false records that overwriting was declined.</summary>
        public static void Write(PlateJob job, LaserConfig cfg, string folder, bool write)
        {
            Write(job, cfg, folder, write, folder);
        }

        public static void Write(PlateJob job, LaserConfig cfg, string folder, bool write, string supportFolder)
        {
            ExportRun run = job.Run;
            run.Notes.AddRange(job.Log);
            string path = Path.Combine(folder, run.FileName);
            run.OutputPath = path;
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

            if (run.PdfBytes != null)
            {
                string pdfPath = Path.Combine(folder, run.PdfFileName);
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

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            if (cfg.SaveBodyJson)
            {
                try
                {
                    string dbg = NxUtil.EnsureFolder(Path.Combine(supportFolder, "_debug"), run.Notes);
                    JObj json = job.Model.ToJson();
                    json.Add("selectedFaceId", (double)job.FaceId);
                    string jp = Path.Combine(dbg, Path.GetFileNameWithoutExtension(run.FileName) + "_" + stamp + "_body.json");
                    NxUtil.WriteText(jp, Json.Write(json));
                    run.Notes.Add("Body model saved: " + jp);
                }
                catch (Exception jex) { run.Notes.Add("Could not save the body model: " + jex.Message); }
            }
        }

        /// <summary>Appends the job's summary plus the geometry detail to the daily log. Returns the log path or null.</summary>
        public static string AppendLog(PlateJob job, LaserConfig cfg, string folder, List<string> summary)
        {
            if (!cfg.WriteLog) return null;
            try
            {
                string logDir = NxUtil.EnsureFolder(Path.Combine(folder, "_logs"), job.Run.Notes);
                string logPath = Path.Combine(logDir, "LaserDxf_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                var detail = new List<string>(summary);
                detail.Add("");
                detail.AddRange(ProbeReport.Summary(job.Model, job.Run.Analysis, job.Note));
                detail.Add("");
                detail.AddRange(ProbeReport.FaceEdges(job.Model, job.Run.Analysis));
                detail.Add("");
                detail.Add(new string('-', 100));
                detail.Add("");
                File.AppendAllText(logPath, ProbeReport.Join(detail), new UTF8Encoding(false));
                return logPath;
            }
            catch (Exception lex)
            {
                summary.Add("Could not write the log file: " + lex.Message);
                return null;
            }
        }
    }
}
