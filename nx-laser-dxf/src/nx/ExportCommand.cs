// Main journal: select the plate, preview, write the DXF, report.
namespace LaserDxf.Nx
{
    using System;
    using System.Collections.Generic;
    using NXOpen;
    using LaserDxf.Core;

    public static class ExportCommand
    {
        public const string Version = "0.7";

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

            PlateJob job = PlateJobs.Prepare(s, cfg, sel, etchPick, cfgWarnings);
            job.Log.InsertRange(0, log);
            ExportRun run = job.Run;
            run.Version = Version;

            if (cfg.ShowPreview)
            {
                var editor = new LayoutEditor(run);
                PreviewResult r = PreviewWindow.Show(editor, run.Identity.Id + "  (" + run.FileName + ")", cfg.WriteCutPdf);
                if (r == PreviewResult.Cancel)
                {
                    lw.WriteLine("Laser DXF: cancelled in the preview - nothing written.");
                    return;
                }
                run.WriteCutPdf = r == PreviewResult.ExportWithPdf;
                editor.Apply();
            }

            string folder = NxUtil.EnsureFolder(cfg.OutputFolder, run.Warnings);
            bool write = true;
            List<string> existing = PlateJobs.Existing(job, folder);
            if (existing.Count > 0 && cfg.AskBeforeOverwrite)
            {
                int answer = UI.GetUI().NXMessageBox.Show(NxPlate.Title, NXMessageBox.DialogType.Question,
                    string.Join(" and ", existing.ToArray()) + (existing.Count > 1 ? " already exist in\n" : " already exists in\n") + folder + "\n\nOverwrite?");
                write = answer == 1; // NXMessageBox: 1 = Yes, 2 = No
                run.Notes.Add("Overwrite prompt answer code: " + answer);
            }
            PlateJobs.Write(job, cfg, folder, write);

            List<string> lines = ExportReport.Summary(run);
            string logPath = PlateJobs.AppendLog(job, cfg, folder, lines);
            if (logPath != null) lines.Add("Log          : " + logPath);
            foreach (string l in lines) lw.WriteLine(l);
        }
    }
}
