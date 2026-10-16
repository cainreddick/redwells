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
    }

    public sealed class LaserConfig
    {
        public string SourcePath = "(built-in defaults)";

        public string OutputFolder = @"C:\Laser DXF";
        public string FileNameFormat = "{ID}-{REV}.dxf";
        public bool AskBeforeOverwrite = true;
        public bool ShowOptionsDialog = true;

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
        public List<LabelRule> LabelTable = new List<LabelRule>();

        // etch
        public string EtchFormat = "{ID}-{REV}";
        public bool RevisionAsLetter = true;
        public double EtchHeight = 5;
        public double EtchMinHeight = 3;
        public double EtchClearance = 3;
        public string EtchPlacement = "Auto";
        public bool EtchAllowRotate90 = true;

        // attributes
        public AttrRef IdAttr = new AttrRef("ID", "MCAD");
        public AttrRef RevisionAttr = new AttrRef("Revision", "MCAD Revision");
        public AttrRef FallbackIdAttr = new AttrRef("Part_Number", "Lentus Attributes");
        public AttrRef NameAttr = new AttrRef("DB_PART_NAME", "MCAD");
        public AttrRef MaterialAttr = new AttrRef("Polar_Material", "MCAD Revision");
        public AttrRef ThicknessAttr = new AttrRef("Matl_Thk", "Materials");

        // batch (phase 2)
        public double RequiredThickness = 6;
        public double ThicknessTolerance = 0.1;
        public bool WriteCsv = true;

        // debug
        public bool SaveBodyJson = true;
        public bool WriteLog = true;

        public LaserConfig()
        {
            FallbackIdAttr.IgnoreValues.Add("model-plain-1");
            LabelTable.Add(new LabelRule { Label = "R6", Diameter = 5.8 });
            LabelTable.Add(new LabelRule { Label = "M8", Diameter = 5.5 });
        }

        public List<string> AttributeTitles()
        {
            var l = new List<string>();
            foreach (var a in new[] { IdAttr, RevisionAttr, FallbackIdAttr, NameAttr, MaterialAttr, ThicknessAttr }) l.Add(a.Title);
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
                        CutDiameter = rs.OptNum("cutDiameter", 0.01, 10000)
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
            c.EtchAllowRotate90 = e.Bool("allowRotate90", c.EtchAllowRotate90);
            e.Finish();

            Section a = s.Sub("attributes");
            c.IdAttr = a.Attr("id", c.IdAttr);
            c.RevisionAttr = a.Attr("revision", c.RevisionAttr);
            c.FallbackIdAttr = a.Attr("fallbackId", c.FallbackIdAttr);
            c.NameAttr = a.Attr("name", c.NameAttr);
            c.MaterialAttr = a.Attr("material", c.MaterialAttr);
            c.ThicknessAttr = a.Attr("thickness", c.ThicknessAttr);
            a.Finish();

            Section b = s.Sub("batch");
            c.RequiredThickness = b.Num("requiredThickness", c.RequiredThickness, 0, 1000);
            c.ThicknessTolerance = b.Num("thicknessTolerance", c.ThicknessTolerance, 0, 100);
            c.WriteCsv = b.Bool("writeCsv", c.WriteCsv);
            b.Finish();

            Section dbg = s.Sub("debug");
            c.SaveBodyJson = dbg.Bool("saveBodyJson", c.SaveBodyJson);
            c.WriteLog = dbg.Bool("writeLog", c.WriteLog);
            dbg.Finish();

            s.Finish();
            return c;
        }
    }
}
