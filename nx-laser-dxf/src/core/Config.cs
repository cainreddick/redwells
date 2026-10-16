// Settings from LaserDxf.config.json. Every value has a default, so a missing or
// partial file still works; unknown keys are reported (they are usually typos).
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;

    public enum TextMode { Stroke, Text }

    public sealed class AttrRef
    {
        public string Title = "";
        public string Category = "";
        public List<string> IgnoreValues = new List<string>();

        public AttrRef() { }
        public AttrRef(string title, string category) { Title = title; Category = category; }

        public override string ToString() { return "\"" + Title + "\" (category \"" + Category + "\")"; }
    }

    public sealed class LabelRule
    {
        public string Label = "";
        public double Diameter;
        public double Tolerance = 0.05;
        /// <summary>Optional laser cut diameter replacing the modelled one (NaN = keep).</summary>
        public double CutDiameter = double.NaN;
        /// <summary>What the label means, for the hole key on the PDF sheet.</summary>
        public string Description = "";
    }

    public sealed class LaserConfig
    {
        public string SourcePath = "(built-in defaults)";

        public string OutputFolder = @"C:\Laser DXF";
        public string FileNameFormat = "{ID}-{REV}.dxf";
        public bool AskBeforeOverwrite = true;
        public bool ShowOptionsDialog = true;
        public bool ShowPreview = true;

        // geometry
        public double Tolerance = 0.01;
        public double ChordTolerance = 0.02;
        public XAlignMode XAlign = XAlignMode.LongestEdge;

        // dxf
        public DxfVersion DxfVersion = DxfVersion.R2000;
        public TextMode TextMode = TextMode.Stroke;
        public DxfLayer CutLayer = new DxfLayer("CUT", 7);
        public DxfLayer EtchLayer = new DxfLayer("ETCH", 5);
        public bool EntityColors = true;

        // labels
        public double LabelHeight = 3;
        public double LabelGap = 1.5;
        public double HoleClearance = 1.0;
        public double EdgeClearance = 2.0;
        public bool UseHoleFeatures = true;
        public string ThreadLabelFormat = "{THREAD}";
        public string CountersinkLabel = "CS";
        public string CountersinkDescription = "countersunk hole - countersink after cutting";
        public List<LabelRule> LabelTable = new List<LabelRule>();

        // etch
        public string EtchFormat = "{ID}-{REV}";
        public bool RevisionAsLetter = false;
        public double EtchHeight = 5;
        public double EtchMinHeight = 3;
        public double EtchClearance = 3;
        public string EtchPlacement = "Auto";
        public bool EtchAllowRotate90 = true;

        // attributes
        public AttrRef IdAttr = new AttrRef("DB_PART_NO", "ONE4_MCAD");
        public AttrRef RevisionAttr = new AttrRef("DB_PART_REV", "ONE4_MCADRevision");
        public AttrRef FallbackIdAttr = new AttrRef("Part_Number", "Lentus Attributes");
        public AttrRef NameAttr = new AttrRef("DB_PART_NAME", "ONE4_MCAD");
        public AttrRef MaterialAttr = new AttrRef("Polar_Material", "ONE4_MCADRevision");
        public AttrRef ThicknessAttr = new AttrRef("Matl_Thk", "Materials");
        public AttrRef DrawnByAttr = new AttrRef("DRAWN_BY", "person");
        /// <summary>If the revision attribute is missing, take it from a Teamcenter part name "ID/REV".</summary>
        public bool RevisionFromPartName = true;

        // PDF instruction sheet for the laser shop
        public bool WritePdf = true;
        public string PdfFileNameFormat = "{ID}-{REV}.pdf";
        public string SheetSize = "Auto";
        public string Company = "";
        public string SheetTitle = "LASER CUTTING INSTRUCTION";
        public double SheetMinTextHeight = 1.8;
        public List<string> SheetNotes = new List<string>
        {
            "Cut the profile and all holes from the DXF file {DXF} (layer CUT).",
            "!ETCH ALL TEXT ON LAYER ETCH (shown blue on this sheet): the part number {ETCH} and every hole label. Do not omit any etching.",
            "Etch on the face shown - this view is from the etch side.",
            "Material: {MATERIAL} {THK} mm thick.",
            "Remove all burrs and sharp edges."
        };

        // batch (phase 2)
        /// <summary>Plate thicknesses (mm) the batch exports; empty = any thickness.</summary>
        public List<double> AllowedThicknesses = new List<double> { 6, 3 };
        /// <summary>CSV columns: header + {TOKEN} template ({ID} {REV} {REVRAW} {NAME} {MATERIAL} {THICKNESS} {DXF} {DXFNAME}).</summary>
        public List<KeyValuePair<string, string>> CsvColumns = DefaultCsvColumns();
        /// <summary>Batch sub-folder (inside OutputFolder) and CSV name, from the assembly's {ID}/{REV}. Empty folder = no sub-folder.</summary>
        public string BatchFolderFormat = "{ID}-{REV}";
        /// <summary>Component attributes that mean "not in the parts list": such components aren't counted in Qty.</summary>
        public List<string> QtyExcludeAttributes = new List<string> { "REFERENCE_COMPONENT", "PLIST_IGNORE_MEMBER" };

        // Jig part request list (Excel), filled from the batch CSV
        public bool RequestListEnabled = true;
        /// <summary>Template workbook; a relative path is relative to the config file's folder.</summary>
        public string RequestListTemplate = "Jig Part Request List.xlsx";
        public string RequestListFileFormat = "{ID}-{REV}.xlsx";
        public RequestListSpec RequestList = DefaultRequestList();
        /// <summary>Text for requestList.titleCell, from the assembly's tokens.</summary>
        public string RequestListTitleFormat = "{ID}-{REV}";

        public static RequestListSpec DefaultRequestList()
        {
            var r = new RequestListSpec { FirstRow = 4, TotalCell = "H1", TitleCell = "A2", DataSheetName = "" };
            r.Columns.Add(new KeyValuePair<string, string>("A", "ID"));
            r.Columns.Add(new KeyValuePair<string, string>("B", "Material"));
            r.Columns.Add(new KeyValuePair<string, string>("C", "Thickness"));
            r.Columns.Add(new KeyValuePair<string, string>("D", "Qty"));
            return r;
        }
        public string BatchCsvFileFormat = "{ID}-{REV}.csv";
        public double ThicknessTolerance = 0.1;
        public bool WriteCsv = true;

        // debug
        public bool SaveBodyJson = true;
        public bool WriteLog = true;

        public LaserConfig()
        {
            FallbackIdAttr.IgnoreValues.Add("model-plain-1");
            LabelTable.Add(new LabelRule { Label = "R6", Diameter = 5.8, Description = "6 mm reamed dowel hole - ream after cutting" });
            LabelTable.Add(new LabelRule { Label = "M8", Diameter = 5.5, Description = "M8 tapped hole - drill and tap after cutting" });
        }

        public static List<KeyValuePair<string, string>> DefaultCsvColumns()
        {
            var l = new List<KeyValuePair<string, string>>();
            l.Add(new KeyValuePair<string, string>("ID", "{ID}-{REV}"));
            l.Add(new KeyValuePair<string, string>("Revision", "{REVRAW}"));
            l.Add(new KeyValuePair<string, string>("Name", "{NAME}"));
            l.Add(new KeyValuePair<string, string>("Qty", "{QTY}"));
            l.Add(new KeyValuePair<string, string>("Material", "{MATERIAL}"));
            l.Add(new KeyValuePair<string, string>("Thickness", "{THICKNESS}"));
            l.Add(new KeyValuePair<string, string>("DXF path", "{DXF}"));
            return l;
        }

        public List<string> AttributeTitles()
        {
            var l = new List<string>();
            foreach (var a in new[] { IdAttr, RevisionAttr, FallbackIdAttr, NameAttr, MaterialAttr, ThicknessAttr, DrawnByAttr }) l.Add(a.Title);
            return l;
        }

        public AnalysisOptions ToAnalysisOptions()
        {
            return new AnalysisOptions { Tol = Tolerance, XAlign = XAlign };
        }

        // ------------------------------------------------------------ loading

        /// <summary>Wraps a JSON object, tracking which keys were read so leftovers can be reported.</summary>
        sealed class Section
        {
            readonly JObj obj;
            readonly string path;
            readonly List<string> warnings;
            readonly List<string> used = new List<string>();

            public Section(JObj obj, string path, List<string> warnings)
            {
                this.obj = obj ?? new JObj();
                this.path = path;
                this.warnings = warnings;
            }

            object Take(string key)
            {
                used.Add(key);
                return obj.Get(key);
            }

            string Where(string key) { return path.Length == 0 ? key : path + "." + key; }

            public double Num(string key, double def, double min, double max)
            {
                object v = Take(key);
                if (v == null) return def;
                if (!(v is double)) { warnings.Add("Config: " + Where(key) + " should be a number - using " + (double.IsNaN(def) ? "none" : Fmt.N(def))); return def; }
                double d = (double)v;
                if (d < min || d > max) { warnings.Add("Config: " + Where(key) + " = " + Fmt.N(d) + " is out of range (" + Fmt.N(min) + " to " + Fmt.N(max) + ") - using " + Fmt.N(def)); return def; }
                return d;
            }

            /// <summary>Optional number: absent or null gives NaN.</summary>
            public double OptNum(string key, double min, double max)
            {
                return Num(key, double.NaN, min, max);
            }

            public string Str(string key, string def)
            {
                object v = Take(key);
                if (v == null) return def;
                if (!(v is string)) { warnings.Add("Config: " + Where(key) + " should be text in quotes - using \"" + def + "\""); return def; }
                return (string)v;
            }

            public bool Bool(string key, bool def)
            {
                object v = Take(key);
                if (v == null) return def;
                if (!(v is bool)) { warnings.Add("Config: " + Where(key) + " should be true or false - using " + (def ? "true" : "false")); return def; }
                return (bool)v;
            }

            public T Choice<T>(string key, T def)
            {
                string s = Str(key, def.ToString());
                foreach (T value in Enum.GetValues(typeof(T)))
                    if (string.Equals(value.ToString(), s, StringComparison.OrdinalIgnoreCase)) return value;
                warnings.Add("Config: " + Where(key) + " = \"" + s + "\" is not one of " + string.Join(", ", Enum.GetNames(typeof(T))) + " - using " + def);
                return def;
            }

            /// <summary>The raw object under key (marked as used), or null.</summary>
            public JObj RawObj(string key)
            {
                object v = Take(key);
                if (v != null && !(v is JObj)) warnings.Add("Config: " + Where(key) + " should be an object { ... }");
                return v as JObj;
            }

            public Section Sub(string key)
            {
                object v = Take(key);
                if (v != null && !(v is JObj)) warnings.Add("Config: " + Where(key) + " should be an object { ... }");
                return new Section(v as JObj, Where(key), warnings);
            }

            public List<object> Arr(string key)
            {
                object v = Take(key);
                if (v != null && !(v is List<object>)) warnings.Add("Config: " + Where(key) + " should be a list [ ... ]");
                return v as List<object>;
            }

            public void Finish()
            {
                foreach (var kv in obj.Items)
                    if (!used.Contains(kv.Key) && !kv.Key.StartsWith("_"))
                        warnings.Add("Config: unknown setting \"" + Where(kv.Key) + "\" ignored (typo?)");
            }

            public DxfLayer Layer(string key, DxfLayer def)
            {
                Section s = Sub(key);
                var l = new DxfLayer(s.Str("name", def.Name), (int)s.Num("color", def.Color, 1, 255));
                if (l.Name.Trim().Length == 0) l.Name = def.Name;
                s.Finish();
                return l;
            }

            public AttrRef Attr(string key, AttrRef def)
            {
                Section s = Sub(key);
                var a = new AttrRef(s.Str("title", def.Title), s.Str("category", def.Category));
                List<object> ign = s.Arr("ignoreValues");
                if (ign == null) a.IgnoreValues.AddRange(def.IgnoreValues);
                else foreach (object o in ign) if (o is string) a.IgnoreValues.Add((string)o);
                s.Finish();
                return a;
            }
        }

        public static LaserConfig Parse(string json, string sourcePath, List<string> warnings)
        {
            var c = new LaserConfig();
            c.SourcePath = sourcePath;
            object root;
            try { root = Json.Parse(json); }
            catch (FormatException ex)
            {
                throw new PlateException("The config file " + sourcePath + " is not valid JSON.\n" + ex.Message);
            }
            if (!(root is JObj)) throw new PlateException("The config file " + sourcePath + " must contain a { ... } object.");
            var s = new Section((JObj)root, "", warnings);

            c.OutputFolder = s.Str("outputFolder", c.OutputFolder).Replace('/', '\\');
            c.FileNameFormat = s.Str("fileNameFormat", c.FileNameFormat);
            c.AskBeforeOverwrite = s.Bool("askBeforeOverwrite", c.AskBeforeOverwrite);
            c.ShowOptionsDialog = s.Bool("showOptionsDialog", c.ShowOptionsDialog);
            c.ShowPreview = s.Bool("showPreview", c.ShowPreview);

            Section g = s.Sub("geometry");
            c.Tolerance = g.Num("tolerance", c.Tolerance, 1e-5, 1);
            c.ChordTolerance = g.Num("chordTolerance", c.ChordTolerance, 1e-4, 2);
            string xa = g.Str("xAlign", "LongestEdge");
            if (string.Equals(xa, "WCS", StringComparison.OrdinalIgnoreCase)) c.XAlign = XAlignMode.Hint;
            else if (string.Equals(xa, "LongestEdge", StringComparison.OrdinalIgnoreCase)) c.XAlign = XAlignMode.LongestEdge;
            else warnings.Add("Config: geometry.xAlign = \"" + xa + "\" should be LongestEdge or WCS - using LongestEdge");
            g.Finish();

            Section d = s.Sub("dxf");
            c.DxfVersion = d.Choice("version", c.DxfVersion);
            c.TextMode = d.Choice("textMode", c.TextMode);
            c.CutLayer = d.Layer("cutLayer", c.CutLayer);
            c.EtchLayer = d.Layer("etchLayer", c.EtchLayer);
            c.EntityColors = d.Bool("entityColors", c.EntityColors);
            d.Finish();

            Section l = s.Sub("labels");
            c.LabelHeight = l.Num("textHeight", c.LabelHeight, 0.5, 50);
            c.LabelGap = l.Num("gap", c.LabelGap, 0, 50);
            c.HoleClearance = l.Num("holeClearance", c.HoleClearance, 0, 50);
            c.EdgeClearance = l.Num("edgeClearance", c.EdgeClearance, 0, 50);
            c.UseHoleFeatures = l.Bool("useHoleFeatures", c.UseHoleFeatures);
            c.ThreadLabelFormat = l.Str("threadLabelFormat", c.ThreadLabelFormat);
            c.CountersinkLabel = l.Str("countersinkLabel", c.CountersinkLabel);
            c.CountersinkDescription = l.Str("countersinkDescription", c.CountersinkDescription);
            List<object> table = l.Arr("table");
            if (table != null)
            {
                c.LabelTable.Clear();
                int i = 0;
                foreach (object o in table)
                {
                    var rs = new Section(o as JObj, "labels.table[" + i++ + "]", warnings);
                    var r = new LabelRule
                    {
                        Label = rs.Str("label", ""),
                        Diameter = rs.Num("diameter", 0, 0, 10000),
                        Tolerance = rs.Num("tolerance", 0.05, 0, 10),
                        CutDiameter = rs.OptNum("cutDiameter", 0.01, 10000),
                        Description = rs.Str("description", "")
                    };
                    rs.Finish();
                    if (r.Label.Length == 0 || r.Diameter <= 0)
                        warnings.Add("Config: labels.table entry " + (i - 1) + " needs a label and a diameter - skipped");
                    else c.LabelTable.Add(r);
                }
            }
            l.Finish();

            Section e = s.Sub("etch");
            c.EtchFormat = e.Str("format", c.EtchFormat);
            c.RevisionAsLetter = e.Bool("revisionAsLetter", c.RevisionAsLetter);
            c.EtchHeight = e.Num("textHeight", c.EtchHeight, 0.5, 100);
            c.EtchMinHeight = e.Num("minTextHeight", c.EtchMinHeight, 0.5, 100);
            c.EtchClearance = e.Num("clearance", c.EtchClearance, 0, 100);
            c.EtchPlacement = e.Str("placement", c.EtchPlacement);
            if (!string.Equals(c.EtchPlacement, "Auto", StringComparison.OrdinalIgnoreCase) && !string.Equals(c.EtchPlacement, "Pick", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("Config: etch.placement = \"" + c.EtchPlacement + "\" should be Auto or Pick - using Auto");
                c.EtchPlacement = "Auto";
            }
            c.EtchAllowRotate90 = e.Bool("allowRotate90", c.EtchAllowRotate90);
            e.Finish();

            Section a = s.Sub("attributes");
            c.IdAttr = a.Attr("id", c.IdAttr);
            c.RevisionAttr = a.Attr("revision", c.RevisionAttr);
            c.FallbackIdAttr = a.Attr("fallbackId", c.FallbackIdAttr);
            c.NameAttr = a.Attr("name", c.NameAttr);
            c.MaterialAttr = a.Attr("material", c.MaterialAttr);
            c.ThicknessAttr = a.Attr("thickness", c.ThicknessAttr);
            c.DrawnByAttr = a.Attr("drawnBy", c.DrawnByAttr);
            c.RevisionFromPartName = a.Bool("revisionFromPartName", c.RevisionFromPartName);
            a.Finish();

            Section pdf = s.Sub("pdf");
            c.WritePdf = pdf.Bool("enabled", c.WritePdf);
            c.PdfFileNameFormat = pdf.Str("fileNameFormat", c.PdfFileNameFormat);
            c.SheetSize = pdf.Str("sheet", c.SheetSize);
            if (c.SheetSize != "Auto" && c.SheetSize != "A4" && c.SheetSize != "A3")
            {
                warnings.Add("Config: pdf.sheet = \"" + c.SheetSize + "\" should be Auto, A4 or A3 - using Auto");
                c.SheetSize = "Auto";
            }
            c.Company = pdf.Str("company", c.Company);
            c.SheetTitle = pdf.Str("title", c.SheetTitle);
            c.SheetMinTextHeight = pdf.Num("minTextHeight", c.SheetMinTextHeight, 0.5, 10);
            List<object> notes = pdf.Arr("notes");
            if (notes != null)
            {
                c.SheetNotes.Clear();
                foreach (object o in notes) if (o is string) c.SheetNotes.Add((string)o);
            }
            pdf.Finish();

            Section b = s.Sub("batch");
            c.BatchFolderFormat = b.Str("folderFormat", c.BatchFolderFormat);
            c.BatchCsvFileFormat = b.Str("csvFileName", c.BatchCsvFileFormat);
            List<object> qx = b.Arr("qtyExcludeAttributes");
            if (qx != null)
            {
                c.QtyExcludeAttributes.Clear();
                foreach (object o in qx) if (o is string && ((string)o).Trim().Length > 0) c.QtyExcludeAttributes.Add(((string)o).Trim());
            }
            List<object> thk = b.Arr("allowedThicknesses");
            if (thk != null)
            {
                c.AllowedThicknesses.Clear();
                foreach (object o in thk)
                {
                    if (o is double && (double)o > 0) c.AllowedThicknesses.Add((double)o);
                    else warnings.Add("Config: batch.allowedThicknesses should be a list of numbers in mm, e.g. [6, 3]");
                }
            }
            List<object> cols = b.Arr("csvColumns");
            if (cols != null)
            {
                c.CsvColumns.Clear();
                int ci = 0;
                foreach (object o in cols)
                {
                    var cs = new Section(o as JObj, "batch.csvColumns[" + ci++ + "]", warnings);
                    string header = cs.Str("header", ""), value = cs.Str("value", "");
                    cs.Finish();
                    if (header.Length == 0) warnings.Add("Config: batch.csvColumns entry " + (ci - 1) + " needs a header - skipped");
                    else c.CsvColumns.Add(new KeyValuePair<string, string>(header, value));
                }
                if (c.CsvColumns.Count == 0) c.CsvColumns = DefaultCsvColumns();
            }
            c.ThicknessTolerance = b.Num("thicknessTolerance", c.ThicknessTolerance, 0, 100);
            c.WriteCsv = b.Bool("writeCsv", c.WriteCsv);
            b.Finish();

            Section rl = s.Sub("requestList");
            c.RequestListEnabled = rl.Bool("enabled", c.RequestListEnabled);
            c.RequestListTemplate = rl.Str("template", c.RequestListTemplate);
            c.RequestListFileFormat = rl.Str("fileName", c.RequestListFileFormat);
            c.RequestList.SheetName = rl.Str("sheet", c.RequestList.SheetName);
            c.RequestList.FirstRow = (int)rl.Num("firstRow", c.RequestList.FirstRow, 1, 100000);
            c.RequestList.TotalCell = rl.Str("totalCell", c.RequestList.TotalCell);
            c.RequestList.DataSheetName = rl.Str("dataSheet", c.RequestList.DataSheetName);
            c.RequestList.TitleCell = rl.Str("titleCell", c.RequestList.TitleCell);
            c.RequestListTitleFormat = rl.Str("title", c.RequestListTitleFormat);
            JObj rlCols = rl.RawObj("columns");
            if (rlCols != null)
            {
                c.RequestList.Columns.Clear();
                foreach (var kv in rlCols.Items)
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(kv.Key, "^[A-Za-z]{1,3}$") || !(kv.Value is string))
                        warnings.Add("Config: requestList.columns entry \"" + kv.Key + "\" should be a column letter with a CSV column name, e.g. \"A\": \"ID\"");
                    else c.RequestList.Columns.Add(new KeyValuePair<string, string>(kv.Key.ToUpperInvariant(), (string)kv.Value));
                }
            }
            if (c.RequestList.TotalCell.Trim().Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(c.RequestList.TotalCell.Trim(), "^[A-Za-z]{1,3}[0-9]+$"))
            {
                warnings.Add("Config: requestList.totalCell \"" + c.RequestList.TotalCell + "\" is not a cell like H1 - ignored");
                c.RequestList.TotalCell = "";
            }
            foreach (string cellKey in new[] { "titleCell" })
                if (c.RequestList.TitleCell.Trim().Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(c.RequestList.TitleCell.Trim(), "^[A-Za-z]{1,3}[0-9]+$"))
                {
                    warnings.Add("Config: requestList." + cellKey + " \"" + c.RequestList.TitleCell + "\" is not a cell like A2 - ignored");
                    c.RequestList.TitleCell = "";
                }
            rl.Finish();

            Section dbg = s.Sub("debug");
            c.SaveBodyJson = dbg.Bool("saveBodyJson", c.SaveBodyJson);
            c.WriteLog = dbg.Bool("writeLog", c.WriteLog);
            dbg.Finish();

            s.Finish();
            return c;
        }
    }
}
