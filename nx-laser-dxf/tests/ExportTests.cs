// Step 2 tests: config, identity/templates, profile building, DXF output.
// Also writes sample DXFs (+ expectations) that tests/validate_dxf.py checks with ezdxf.
namespace LaserDxf.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using LaserDxf.Core;

    public static class ConfigTests
    {
        public static void TestShippedConfigParsesCleanly()
        {
            string path = Path.Combine("config", "LaserDxf.config.json");
            var w = new List<string>();
            LaserConfig c = LaserConfig.Parse(File.ReadAllText(path), path, w);
            TestMain.Check(w.Count == 0, "no warnings: " + string.Join(" | ", w.ToArray()));
            TestMain.Check(c.OutputFolder == @"C:\Laser DXF", "output folder: " + c.OutputFolder);
            TestMain.Check(c.DxfVersion == DxfVersion.R2000 && c.CutLayer.Name == "CUT" && c.EtchLayer.Color == 5, "dxf section");
            TestMain.Check(c.LabelTable.Count == 2 && c.LabelTable[0].Label == "R6" && double.IsNaN(c.LabelTable[0].CutDiameter), "label table");
            TestMain.Check(c.FallbackIdAttr.IgnoreValues.Contains("model-plain-1"), "ignore values");
        }

        public static void TestDefaultsMatchShippedFile()
        {
            var w = new List<string>();
            LaserConfig file = LaserConfig.Parse(File.ReadAllText(Path.Combine("config", "LaserDxf.config.json")), "x", w);
            var def = new LaserConfig();
            TestMain.Check(Json.Write(Snapshot(file)) == Json.Write(Snapshot(def)), "built-in defaults equal the shipped config");
        }

        static JObj Snapshot(LaserConfig c)
        {
            var o = new JObj();
            foreach (var f in typeof(LaserConfig).GetFields())
            {
                if (f.Name == "SourcePath") continue;
                object v = f.GetValue(c);
                var layer = v as DxfLayer;
                var attr = v as AttrRef;
                var table = v as List<LabelRule>;
                var strings = v as List<string>;
                if (layer != null) v = layer.Name + "/" + layer.Color;
                else if (attr != null) v = attr + string.Join(",", attr.IgnoreValues.ToArray());
                else if (table != null)
                {
                    var sb = new StringBuilder();
                    foreach (var r in table) sb.Append(r.Label + r.Diameter + r.Tolerance + r.CutDiameter + r.Description + ";");
                    v = sb.ToString();
                }
                else if (strings != null) v = string.Join("|", strings.ToArray());
                o.Add(f.Name, Convert.ToString(v, Fmt.Inv));
            }
            return o;
        }

        public static void TestWarningsForBadValues()
        {
            var w = new List<string>();
            LaserConfig c = LaserConfig.Parse("{ \"outputFolder\": \"D:\\\\Out\", \"dxf\": { \"version\": \"R14\", \"cutLayr\": {} }, " +
                "\"labels\": { \"textHeight\": \"big\", \"table\": [ { \"label\": \"X\", \"diameter\": 4, \"cutDiameter\": 4.2 } ] }, \"misspelt\": 1 }", "t", w);
            string all = string.Join(" | ", w.ToArray());
            TestMain.Check(c.OutputFolder == @"D:\Out", "folder");
            TestMain.Check(all.Contains("dxf.version"), "bad enum reported: " + all);
            TestMain.Check(all.Contains("dxf.cutLayr"), "typo in nested key reported");
            TestMain.Check(all.Contains("labels.textHeight"), "wrong type reported");
            TestMain.Check(all.Contains("\"misspelt\""), "unknown top-level key reported");
            TestMain.Near(c.LabelTable[0].CutDiameter, 4.2, 0, "cut diameter override read");
            TestMain.Near(c.LabelHeight, 3, 0, "falls back to default");
        }

        public static void TestInvalidJsonIsAClearError()
        {
            try
            {
                LaserConfig.Parse("{ \"a\": 1 \n \"b\": 2 }", "C:\\x\\LaserDxf.config.json", new List<string>());
                TestMain.Check(false, "should throw");
            }
            catch (PlateException ex)
            {
                TestMain.Check(ex.Message.Contains("not valid JSON") && ex.Message.Contains("line 2"), ex.Message);
            }
        }
    }

    public static class IdentityTests
    {
        /// <summary>Attributes as NX Open reported them for a real Teamcenter part (step 3 run).</summary>
        public static List<AttributeEntry> Attrs()
        {
            return new List<AttributeEntry>
            {
                new AttributeEntry { Category = "Lentus Attributes", Title = "Part_Number", Value = "model-plain-1" },
                new AttributeEntry { Category = "ONE4_MCAD", Title = "DB_PART_NO", Alias = "Part Number", Value = "S180-06068-47009", Flags = "pdm" },
                new AttributeEntry { Category = "ONE4_MCAD", Title = "DB_PART_NAME", Alias = "Part Name", Value = "RING SUPPORT - JIG", Flags = "pdm" },
                new AttributeEntry { Category = "ONE4_MCADRevision", Title = "DB_PART_REV", Alias = "Part Revision", Value = "1", Flags = "pdm" },
                new AttributeEntry { Category = "ONE4_MCADRevision", Title = "Polar_Material", Value = "MILD STEEL", Flags = "pdm" },
                new AttributeEntry { Category = "ONE4_MCADRevision", Title = "Finish", Value = "", Flags = "pdm" },
                new AttributeEntry { Category = "Materials", Title = "Matl_Thk", Value = "6" },
            };
        }

        public static void TestRevisionLetters()
        {
            TestMain.Check(Identity.RevisionLetters("1") == "A", "1");
            TestMain.Check(Identity.RevisionLetters("2") == "B", "2");
            TestMain.Check(Identity.RevisionLetters("26") == "Z", "26");
            TestMain.Check(Identity.RevisionLetters("27") == "AA", "27");
            TestMain.Check(Identity.RevisionLetters("52") == "AZ", "52");
            TestMain.Check(Identity.RevisionLetters("C") == "C", "letters unchanged");
            TestMain.Check(Identity.RevisionLetters("0") == "0", "zero unchanged");
        }

        public static void TestResolveFromAttributes()
        {
            var m = new BodyModel { PartName = "S180-06068-47009/1", Attributes = Attrs() };
            PartIdentity p = Identity.Resolve(m, new LaserConfig());
            TestMain.Check(p.Id == "S180-06068-47009" && p.Revision == "1" && p.RevisionRaw == "1", "id + rev number (default)");
            var letters = new LaserConfig { RevisionAsLetter = true };
            TestMain.Check(Identity.Resolve(m, letters).Revision == "A", "rev letter when enabled");
            TestMain.Check(p.Name == "RING SUPPORT - JIG" && p.Material == "MILD STEEL" && p.Thickness == "6", "other attributes");
            TestMain.Check(p.Warnings.Count == 0, "no warnings");

            var byAlias = new LaserConfig();
            byAlias.NameAttr = new AttrRef("part name", "one4_mcad"); // alias, different case
            TestMain.Check(Identity.Resolve(m, byAlias).Name == "RING SUPPORT - JIG", "matches alias case-insensitively");

            var wrongCategory = new LaserConfig();
            wrongCategory.IdAttr = new AttrRef("DB_PART_NO", "MCAD"); // the dialog's display category
            PartIdentity w = Identity.Resolve(m, wrongCategory);
            TestMain.Check(w.Id == "S180-06068-47009" && w.Warnings.Count == 1 && w.Warnings[0].Contains("ONE4_MCAD"), "found in another category, with a note");
        }

        public static void TestFallbackChain()
        {
            var attrs = Attrs();
            attrs.RemoveAt(1); // no ID
            var m = new BodyModel { PartName = "S180-06068-47020/1", Attributes = attrs };
            PartIdentity p = Identity.Resolve(m, new LaserConfig());
            TestMain.Check(p.Id == "S180-06068-47020" && p.IdSource == "part name", "template value model-plain-1 ignored -> part name: " + p.Id);

            attrs[0].Value = "S180-11111-22222";
            p = Identity.Resolve(m, new LaserConfig());
            TestMain.Check(p.Id == "S180-11111-22222" && p.IdSource.StartsWith("fallback"), "real Part_Number used");
            TestMain.Check(p.Warnings.Count == 1, "one warning about the fallback");
        }

        public static void TestRevisionFromPartName()
        {
            TestMain.Check(Identity.RevisionFromPartName("S180-06068-47020/1") == "1", "ID/REV");
            TestMain.Check(Identity.RevisionFromPartName("S180-06068-47020/1-PIPE SUPPORT - JIG") == "1", "ID/REV-NAME");
            TestMain.Check(Identity.RevisionFromPartName("S180-06068-47020") == null, "no revision");

            var attrs = Attrs();
            attrs.RemoveAll(a => a.Title == "DB_PART_REV");
            var m = new BodyModel { PartName = "S180-06068-47009/2", Attributes = attrs };
            PartIdentity p = Identity.Resolve(m, new LaserConfig());
            TestMain.Check(p.Revision == "2" && p.RevisionRaw == "2", "revision from the part name: " + p.Revision);
            var t = p.Tokens();
            TestMain.Check(Identity.Format("{ID}-{REV}", t, null) == "S180-06068-47009-2", "etch format");

            var off = new LaserConfig { RevisionFromPartName = false };
            TestMain.Check(Identity.Resolve(m, off).Revision == "", "fallback can be switched off");
        }

        public static void TestFormatDropsSeparatorForMissingValue()
        {
            var t = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "ID", "S180-1" }, { "REV", "" } };
            TestMain.Check(Identity.Format("{ID}-{REV}.dxf", t, null) == "S180-1.dxf", "rev missing");
            t["REV"] = "B";
            TestMain.Check(Identity.Format("{ID}-{REV}.dxf", t, null) == "S180-1-B.dxf", "rev present");
            t["REV"] = "";
            TestMain.Check(Identity.Format("{REV}_{ID}", t, null) == "S180-1", "leading empty token");
            var w = new List<string>();
            TestMain.Check(Identity.Format("{ID}-{FOO}", t, w) == "S180-1-{FOO}" && w.Count == 1, "unknown token kept and reported");
        }

        public static void TestSafeFileName()
        {
            TestMain.Check(Identity.SafeFileName("A/B:C?.dxf") == "A_B_C_.dxf", Identity.SafeFileName("A/B:C?.dxf"));
        }
    }

    public static class ProfileTests
    {
        static ExportRun Run(Synth s, int face, LaserConfig c)
        {
            s.M.Attributes = IdentityTests.Attrs();
            return ExportPipeline.Prepare(s.M, face, c, c.ToAnalysisOptions(), "test");
        }

        static Synth TestPlate()
        {
            var s = new Synth(6);
            s.Rectangle(200, 100);
            s.SimpleHole(20, 80, 9);
            s.Countersunk(60, 50, 6.6, 13, true);
            s.Counterbore(100, 50, 6.6, 11, 4);
            s.BlindHole(140, 50, 5, 3);
            s.RectCutout(160, 10, 190, 30);
            s.SplitHole(30, 20, 5.8);
            return s;
        }

        static int Count<T>(DxfDocument d) where T : DxfEntity
        {
            int n = 0;
            foreach (var e in d.Entities) if (e is T) n++;
            return n;
        }

        public static void TestProfileEntities()
        {
            Synth s = TestPlate();
            ExportRun r = Run(s, s.TopFace, new LaserConfig());
            DxfDocument d = r.Profile.Doc;
            TestMain.Check(Count<DxfLine>(d) == 4 + 4, "4 outer + 4 slot lines, got " + Count<DxfLine>(d));
            TestMain.Check(r.Etch != null && r.Etch.Clear && r.Etch.Text == "S180-06068-47009-1", "part number etched");
            TestMain.Check(Count<DxfCircle>(d) == 4, "4 round through holes (blind one skipped), got " + Count<DxfCircle>(d));
            TestMain.Check(r.Profile.SkippedLoops == 1, "blind hole skipped");
            TestMain.Check(r.FileName == "S180-06068-47009-1.dxf", "file name " + r.FileName);
            bool csAtBore = false;
            foreach (var h in r.Profile.Holes) if (h.Hole.HasCone && Math.Abs(h.CutDiameter - 6.6) < 1e-9) csAtBore = true;
            TestMain.Check(csAtBore, "countersunk hole cut at the bore");
            string warn = string.Join(" | ", r.Warnings.ToArray());
            TestMain.Check(warn.Contains("countersink") && warn.Contains("counterbore") && warn.Contains("does not go through"), warn);
            foreach (var e in d.Entities)
            {
                bool cut = e.Layer == "CUT" && e.Color == 7 && !(e is DxfPolyline);
                bool etch = e.Layer == "ETCH" && e.Color == 5 && e is DxfPolyline;
                TestMain.Check(cut || etch, "geometry on CUT (7), text strokes on ETCH (5): " + e.GetType().Name + " on " + e.Layer);
            }
        }

        public static void TestInchPartIsScaled()
        {
            var s = new Synth(0.25);
            s.Rectangle(4, 2);
            s.SimpleHole(1, 1, 0.5);
            s.M.Units = "Inches";
            ExportRun r = Run(s, s.TopFace, new LaserConfig());
            TestMain.Near(r.Profile.Width, 101.6, 1e-9, "width in mm");
            TestMain.Near(r.Profile.Holes[0].CutDiameter, 12.7, 1e-9, "hole in mm");
        }

        /// <summary>Writes the sample DXFs that tests/validate_dxf.py checks with ezdxf.</summary>
        public static void TestWriteSamples()
        {
            var samples = new List<object>();
            foreach (DxfVersion v in new[] { DxfVersion.R2000, DxfVersion.R12 })
            {
                var c = new LaserConfig { DxfVersion = v };
                samples.Add(WriteSample("testplate_" + v, Run(TestPlate(), 1, c), 4, 4, 0));
                Synth irr = Synth.Irregular();
                samples.Add(WriteSample("irregular_" + v, Run(irr, irr.TopFace, c), 5, 24 + 0, 4));
                Synth bottom = TestPlate();
                samples.Add(WriteSample("testplate_bottomface_" + v, Run(bottom, bottom.BottomFace, c), 4, 4, 0));
            }
            var tc = new LaserConfig { TextMode = TextMode.Text };
            Synth irrText = Synth.Irregular();
            samples.Add(WriteSample("irregular_TEXT_R2000", Run(irrText, irrText.TopFace, tc), 5, 24, 4));
            File.WriteAllText(Path.Combine(TestMain.OutDir, "samples.json"), Json.Write(samples));
        }

        static JObj WriteSample(string name, ExportRun r, int outerLines, int circles, int arcs)
        {
            string file = name + ".dxf";
            File.WriteAllText(Path.Combine(TestMain.OutDir, file), r.DxfText, Encoding.GetEncoding(1252));
            TestMain.Check(r.DxfText.Contains("CUT") && r.DxfText.EndsWith("EOF\r\n"), name + " looks like a DXF");
            return new JObj().Add("file", file).Add("version", r.Config.DxfVersion.ToString())
                .Add("circles", (double)circles).Add("arcs", (double)arcs)
                .Add("width", r.Profile.Width).Add("height", r.Profile.Height)
                .Add("entities", (double)r.Profile.Doc.Entities.Count)
                .Add("minEtch", (double)(r.Labels.Count + (r.Etch != null ? 1 : 0)))
                .Add("texts", (double)(r.Config.TextMode == TextMode.Text ? r.Labels.Count + (r.Etch != null ? 1 : 0) : 0));
        }
    }
}

namespace LaserDxf.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using LaserDxf.Core;

    public static class PdfTests
    {
        public static void TestXrefOffsetsPointAtObjects()
        {
            var doc = new PdfDocument { Title = "t (1)" };
            var p = new PdfPage(297, 210);
            p.Text(10, 10, 3, "Hole Ø5.8 (R6) \\ test", false, PdfAlign.Left);
            p.Circle(new Vec2(50, 50), 10);
            doc.Pages.Add(p);
            byte[] bytes = doc.Save();
            string s = Encoding.GetEncoding(1252).GetString(bytes);
            int sx = s.LastIndexOf("startxref");
            long xref = long.Parse(s.Substring(sx + 10).Split('\n')[0]);
            TestMain.Check(s.Substring((int)xref, 4) == "xref", "startxref points at the xref table");
            string[] rows = s.Substring((int)xref).Split('\n');
            int count = int.Parse(rows[1].Split(' ')[1]);
            for (int i = 1; i < count; i++)
            {
                long off = long.Parse(rows[2 + i].Substring(0, 10));
                TestMain.Check(s.Substring((int)off).StartsWith(i + " 0 obj"), "xref entry " + i + " points at its object");
            }
            TestMain.Check(s.Contains("(Hole Ø5.8 \\(R6\\) \\\\ test) Tj"), "text escaped, Ø kept in WinAnsi");
        }

        public static void TestFontTablesComplete()
        {
            TestMain.Near(PdfFonts.Width("A", 10, false), 6.67, 1e-9, "Helvetica A");
            TestMain.Near(PdfFonts.Width("~", 10, false), 5.84, 1e-9, "Helvetica table reaches ~ (95 entries)");
            TestMain.Near(PdfFonts.Width("~", 10, true), 5.84, 1e-9, "Bold table reaches ~");
            var lines = PdfFonts.Wrap("one two three four five six seven", 3, false, 20);
            foreach (string l in lines) TestMain.Check(PdfFonts.Width(l, 3, false) <= 20 || !l.Contains(" "), "wrapped line fits: " + l);
            TestMain.Check(string.Join(" ", lines.ToArray()) == "one two three four five six seven", "wrap keeps every word");
        }

        public static void TestSheetChoiceAndHoleKey()
        {
            TestMain.Check(LaserSheet.ScaleText(0.2) == "1:5" && LaserSheet.ScaleText(1) == "1:1" && LaserSheet.ScaleText(2) == "2:1", "scale text");
            TestMain.Near(LaserSheet.FitScale(800, 760, 270, 130), 0.1, 1e-12, "800 mm plate on A4 view area: 1:10");

            Synth s = Synth.Irregular();
            s.M.Attributes = IdentityTests.Attrs();
            var c = new LaserConfig();
            ExportRun r = ExportPipeline.Prepare(s.M, s.TopFace, c, c.ToAnalysisOptions(), "t");
            string name; double pw, ph, sc;
            LaserSheet.ChooseSheet(r, out name, out pw, out ph, out sc);
            TestMain.Check(name == "A3" && Math.Abs(sc - 0.2) < 1e-12, "805 mm plate: A3 at 1:5 (A4 would be 1:10), got " + name + " " + LaserSheet.ScaleText(sc));
            List<string> key = LaserSheet.HoleKey(r);
            string all = string.Join(" | ", key.ToArray());
            TestMain.Check(all.Contains("R6  x3  -  6 mm reamed dowel hole") && all.Contains("M8  x2") && all.Contains("CS  x1  -  countersunk"), all);
            TestMain.Check(all.Contains("laser cut Ø5.8"), "cut diameter in the key");
            TestMain.Check(r.PdfFileName == "S180-06068-47009-1.pdf" && r.PdfBytes != null && r.PdfBytes.Length > 1000, "PDF made alongside the DXF");
        }

        public static void TestWritePdfSamples()
        {
            var samples = new List<object>();
            // Large irregular plate (A3), a small plate (A4, 1:1) and one with hand edits.
            Synth irr = Synth.Irregular();
            irr.M.Attributes = IdentityTests.Attrs();
            var c = new LaserConfig { Company = "POLAR" };
            ExportRun big = ExportPipeline.Prepare(irr.M, irr.TopFace, c, c.ToAnalysisOptions(), "t");
            samples.Add(Write("sheet_irregular.pdf", big, "A3"));

            var small = new Synth(6);
            small.Rectangle(120, 60);
            small.SimpleHole(20, 30, 5.8);
            small.SimpleHole(40, 30, 5.5);
            small.SimpleHole(100, 30, 9);
            small.M.Attributes = IdentityTests.Attrs();
            ExportRun sm = ExportPipeline.Prepare(small.M, small.TopFace, c, c.ToAnalysisOptions(), "t");
            samples.Add(Write("sheet_small.pdf", sm, "A4"));

            var ed = new LayoutEditor(sm);
            ed.Delete(sm.Labels[0]);
            ed.SetHeight(sm.Etch, 3);
            ed.AddLabelAt("M8", new Vec2(100 + 4.5 + 6, 30));
            ed.Apply();
            samples.Add(Write("sheet_small_edited.pdf", sm, "A4"));
            File.WriteAllText(Path.Combine(TestMain.OutDir, "pdf_samples.json"), Json.Write(samples));
        }

        static JObj Write(string file, ExportRun r, string sheet)
        {
            File.WriteAllBytes(Path.Combine(TestMain.OutDir, file), r.PdfBytes);
            var labels = new List<object>();
            foreach (var t in r.Labels) if (!t.Deleted) labels.Add(t.Text);
            return new JObj().Add("file", file).Add("sheet", sheet).Add("etch", r.Etch.Text).Add("dxf", r.FileName)
                .Add("key", new List<object>(LaserSheet.HoleKey(r).ToArray()));
        }
    }
}
