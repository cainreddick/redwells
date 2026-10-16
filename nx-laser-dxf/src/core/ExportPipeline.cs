// The whole export, minus NX: body model + chosen face + config -> DXF text and file name.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;

    public static class ExportPipeline
    {
        public static ExportRun Prepare(BodyModel m, int faceId, LaserConfig c, AnalysisOptions opt, string selectionNote)
        {
            var run = new ExportRun { Config = c, ConfigPath = c.SourcePath, PartName = m.PartName, SelectionNote = selectionNote };

            run.Analysis = PlateAnalyzer.Analyze(m, faceId, opt);
            run.Warnings.AddRange(run.Analysis.Warnings);

            run.Identity = Identity.Resolve(m, c);
            run.Warnings.AddRange(run.Identity.Warnings);

            run.Profile = ProfileBuilder.Build(m, run.Analysis, c);
            run.Warnings.AddRange(run.Profile.Warnings);

            run.FileName = FileNameFor(run.Identity, c, run.Warnings);
            run.DxfText = DxfWriter.Write(run.Profile.Doc, c.DxfVersion);
            return run;
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
