// The whole export, minus NX: body model + chosen face + config -> DXF text and file name.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;

    public static class ExportPipeline
    {
        /// <param name="etchPick">Optional point (model coordinates) the user picked for the part number.</param>
        public static ExportRun Prepare(BodyModel m, int faceId, LaserConfig c, AnalysisOptions opt, string selectionNote, Vec3? etchPick = null)
        {
            var run = new ExportRun { Config = c, ConfigPath = c.SourcePath, PartName = m.PartName, SelectionNote = selectionNote };

            run.Analysis = PlateAnalyzer.Analyze(m, faceId, opt);
            run.Warnings.AddRange(run.Analysis.Warnings);

            run.Identity = Identity.Resolve(m, c);
            run.Warnings.AddRange(run.Identity.Warnings);
            if (run.Identity.AttributesMissing) run.AttributeDump = ProbeReport.Attributes(m);

            run.Profile = ProfileBuilder.Build(m, run.Analysis, c);
            run.Warnings.AddRange(run.Profile.Warnings);

            // Labels and part number.
            HoleClassifier.Classify(m, run.Profile, c, run.Warnings);
            Obstacles obs = Obstacles.From(m, run.Analysis, run.Profile);
            run.Geometry = Obstacles.From(m, run.Analysis, run.Profile);
            run.Labels = Annotator.PlaceLabels(run.Profile, obs, c, run.Warnings);

            run.EtchText = Identity.Format(c.EtchFormat, run.Identity.Tokens(), run.Warnings);
            string bad = StrokeFont.Unsupported(run.EtchText);
            if (bad.Length > 0 && c.TextMode == TextMode.Stroke)
                run.Warnings.Add("The etch font has no glyph for \"" + bad + "\" - drawn as '?'.");
            var plate = new Bounds2 { MinX = 0, MinY = 0, MaxX = run.Profile.Width, MaxY = run.Profile.Height };
            if (etchPick.HasValue)
            {
                Vec2 at = run.Analysis.Frame.ToLocal(etchPick.Value) * run.Profile.UnitScale;
                run.Etch = Annotator.PlaceEtchAt(run.EtchText, at, obs, c, run.Warnings);
            }
            if (run.Etch == null)
                run.Etch = Annotator.PlaceEtch(run.EtchText, obs, plate, c, run.Warnings);
            if (run.Etch != null) run.Etch.IsEtch = true;

            run.FileName = FileNameFor(run.Identity, c, run.Warnings);
            Finish(run);
            return run;
        }

        /// <summary>(Re)builds the DXF from the cut geometry and the current text items, so hand
        /// edits made in the preview end up in the file. Deleted items are left out.</summary>
        public static void Finish(ExportRun run)
        {
            DxfDocument doc = run.Profile.Doc;
            doc.Entities.Clear();
            doc.Entities.AddRange(run.Profile.CutEntities);
            foreach (var t in run.Labels) if (!t.Deleted) Annotator.Emit(doc, t, run.Config);
            if (run.Etch != null && !run.Etch.Deleted) Annotator.Emit(doc, run.Etch, run.Config);
            run.DxfText = DxfWriter.Write(doc, run.Config.DxfVersion);
        }

        public static string FileNameFor(PartIdentity id, LaserConfig c, List<string> warnings)
        {
            string name = Identity.SafeFileName(Identity.Format(c.FileNameFormat, id.Tokens(), warnings));
            if (name.Length == 0 || name.Equals(".dxf", StringComparison.OrdinalIgnoreCase)) name = "plate";
            if (!name.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)) name += ".dxf";
            return name;
        }
    }
}
