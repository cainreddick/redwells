// Batch journal: pick one face (or the body) on each plate in an assembly and
// export a DXF per plate. DXF only - no PDF sheets in batch mode.
namespace LaserDxf.Nx
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using NXOpen;
    using LaserDxf.Core;

    public static class BatchCommand
    {
        public const string Version = ExportCommand.Version;

        public static void Run()
        {
            NxPlate.Run(RunInner);
        }

        static void RunInner(Session s, ListingWindow lw, List<string> log)
        {
            var cfgWarnings = new List<string>();
            LaserConfig cfg = NxPlate.LoadConfig(s, cfgWarnings);

            List<PlateSelection> plates = NxPlate.SelectMany(s, log);
            if (plates == null) return;

            if (cfg.ShowOptionsDialog && !OptionsDialog.Show(cfg, plates.Count + " plate(s)"))
            {
                lw.WriteLine("Laser DXF batch: cancelled.");
                return;
            }
            cfg.WritePdf = false; // batch writes DXFs only
            if (string.Equals(cfg.EtchPlacement, "Pick", StringComparison.OrdinalIgnoreCase))
            {
                cfgWarnings.Add("Picking the part-number position isn't available in batch mode - placed automatically (move it in the preview if needed).");
                cfg.EtchPlacement = "Auto";
            }
            string root = NxUtil.EnsureFolder(cfg.OutputFolder, cfgWarnings);

            // The batch goes in a folder named after the assembly (the displayed part): <root>\S180-...-1\
            Part assemblyPart = s.Parts.Display ?? s.Parts.Work;
            var asmModel = new BodyModel { PartName = NxUtil.PartLabel(assemblyPart) };
            var asmLog = new List<string>();
            NxAttributes.Read(assemblyPart, asmModel, asmLog, cfg.AttributeTitles());
            PartIdentity assembly = Identity.Resolve(asmModel, cfg);
            foreach (string w in assembly.Warnings) cfgWarnings.Add("Assembly: " + w);
            string assemblyLabel = Identity.Format("{ID}-{REV}", assembly.Tokens(), null) + (assembly.Name.Length > 0 ? "  " + assembly.Name : "");
            string sub = Batch.FolderName(assembly, cfg, cfgWarnings);
            string folder = sub.Length == 0 ? root : NxUtil.EnsureFolder(Path.Combine(root, sub), cfgWarnings);

            // Quantity per jig = occurrences in the assembly structure (Assembly Navigator tree).
            PartCounts counts = NxAssembly.Count(assemblyPart, cfg, log);

            // 1. Read and lay out every plate first, so file names (and clashes) are known up front.
            var items = new List<BatchItem>();
            var jobs = new Dictionary<BatchItem, PlateJob>();
            foreach (PlateSelection sel in plates)
            {
                var item = new BatchItem { PartLabel = NxUtil.PartLabel(sel.Part) };
                items.Add(item);
                try
                {
                    NxUtil.EnsureFullyLoaded(sel.Part, log);
                    PlateJob job = PlateJobs.Prepare(s, cfg, sel, null, new List<string>());
                    job.Run.Version = Version;
                    item.Run = job.Run;
                    jobs[item] = job;
                    string reason, thickness;
                    if (!Batch.CheckThickness(job.Run, cfg, out reason, out thickness))
                    {
                        item.Status = BatchStatus.Skipped;
                        item.Reason = reason;
                    }
                    item.Thickness = thickness;
                    item.QtyCounted = item.Qty = counts.IsAssembly ? counts.CountOf(sel.Part) : 1;
                    if (item.Qty == 0)
                        job.Run.Warnings.Add("Qty: not found in the assembly structure (suppressed or reference-only?) - set it in the preview.");
                }
                catch (Exception ex)
                {
                    item.Status = BatchStatus.Failed;
                    item.Reason = ex.Message.Replace("\n", " ");
                }
            }

            // Two plates with the same file name would overwrite each other.
            var byName = new Dictionary<string, BatchItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in items)
            {
                if (it.Status != BatchStatus.Exported) continue;
                BatchItem first;
                if (byName.TryGetValue(it.Run.FileName, out first))
                {
                    it.Status = BatchStatus.Skipped;
                    it.Reason = "same file name as " + first.PartLabel + " (" + it.Run.FileName + ")";
                }
                else byName[it.Run.FileName] = it;
            }

            // 2. One overwrite question for all existing files.
            var existing = new List<string>();
            foreach (var it in items)
                if (it.Status == BatchStatus.Exported) existing.AddRange(PlateJobs.Existing(jobs[it], folder));
            bool overwrite = true;
            if (existing.Count > 0 && cfg.AskBeforeOverwrite)
            {
                string list = string.Join("\n", existing.GetRange(0, Math.Min(existing.Count, 12)).ToArray()) + (existing.Count > 12 ? "\n..." : "");
                int answer = UI.GetUI().NXMessageBox.Show(NxPlate.Title, NXMessageBox.DialogType.Question,
                    existing.Count + " file(s) already exist in " + folder + ":\n" + list + "\n\nOverwrite them?\n(No = skip those plates, export the others)");
                overwrite = answer == 1;
            }
            if (!overwrite)
                foreach (var it in items)
                    if (it.Status == BatchStatus.Exported && PlateJobs.Existing(jobs[it], folder).Count > 0)
                    {
                        it.Status = BatchStatus.Skipped;
                        it.Reason = it.Run.FileName + " already exists (overwrite declined)";
                    }

            // 3. Preview and write each plate in turn.
            int todo = 0;
            foreach (var it in items) if (it.Status == BatchStatus.Exported) todo++;
            int n = 0;
            bool cancelled = false;
            foreach (var it in items)
            {
                if (it.Status != BatchStatus.Exported) continue;
                if (cancelled)
                {
                    it.Status = BatchStatus.Cancelled;
                    it.Reason = "batch cancelled before this plate";
                    continue;
                }
                n++;
                PlateJob job = jobs[it];
                if (cfg.ShowPreview)
                {
                    var editor = new LayoutEditor(job.Run);
                    int qty = it.Qty;
                    PreviewResult r = PreviewWindow.ShowBatch(editor, job.Run.Identity.Id + "  (" + job.Run.FileName + ")", n, todo, ref qty);
                    it.Qty = qty;
                    if (r == PreviewResult.Skip)
                    {
                        it.Status = BatchStatus.Skipped;
                        it.Reason = "skipped in the preview";
                        continue;
                    }
                    if (r == PreviewResult.Cancel)
                    {
                        cancelled = true;
                        it.Status = BatchStatus.Cancelled;
                        it.Reason = "batch cancelled in the preview";
                        continue;
                    }
                    editor.Apply();
                }
                try
                {
                    PlateJobs.Write(job, cfg, folder, true, root);
                    List<string> summary = ExportReport.Summary(job.Run);
                    PlateJobs.AppendLog(job, cfg, root, summary);
                }
                catch (Exception ex)
                {
                    it.Status = BatchStatus.Failed;
                    it.Reason = ex.Message.Replace("\n", " ");
                }
            }

            // 4. CSV for the laser shop + listing.
            string csvPath = null;
            int exported = 0;
            foreach (var it in items) if (it.Status == BatchStatus.Exported) exported++;
            if (cfg.WriteCsv && exported > 0)
            {
                try
                {
                    csvPath = Path.Combine(folder, Batch.CsvFileName(assembly, cfg, cfgWarnings));
                    string csv = Batch.CsvSummary(items, cfg);
                    if (File.Exists(csvPath))
                    {
                        int kept;
                        csv = Batch.MergeCsv(File.ReadAllText(csvPath), csv, cfg, out kept);
                        if (kept > 0) log.Add("CSV: kept " + kept + " row(s) from the earlier batch for plates not exported this time.");
                    }
                    File.WriteAllText(csvPath, csv, new UTF8Encoding(true)); // BOM so Excel reads UTF-8
                }
                catch (Exception ex)
                {
                    log.Add("Could not write the CSV summary " + csvPath + ": " + ex.Message + " (is it open in Excel?)");
                    csvPath = null;
                }
            }

            List<string> lines = Batch.Summary(items, Version, cfg.SourcePath, csvPath, assemblyLabel, folder);
            var notes = new List<string>(cfgWarnings);
            notes.AddRange(log);
            if (notes.Count > 0)
            {
                lines.Add("");
                lines.Add("Notes:");
                foreach (string l in notes) lines.Add("  " + l);
            }
            lines.Add("");
            lines.Add("Per-plate details are in " + Path.Combine(Path.Combine(root, "_logs"), "LaserDxf_" + DateTime.Now.ToString("yyyyMMdd") + ".log"));
            foreach (string l in lines) lw.WriteLine(l);
        }
    }
}
