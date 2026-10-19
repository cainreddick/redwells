// StandardBlocks.cs - NX Open journal: associative holes from jig standard blocks
// (the NX version of SolidWorks Smart Components / Smart Features).
//
// Run with Tools > Journal > Play, or through one of the SB_*.cs launcher journals.
// Settings are read from StandardBlocks.config.json in the same folder.
//
// This is a single-file journal, so it runs without an NX Open Author licence.
// Keep it to C# 5 syntax (no $"...", ?., =>-bodied members, nameof, out var):
// NX may compile journals with an older compiler. The test projects in tests/
// enforce this.
//
// The file has two halves:
//   1. StandardBlocks.Core - pure logic with no NX types: maths, config, hole
//      rules, detection decisions, summaries. Unit-tested outside NX by
//      tests/, which compiles this file with SB_TEST defined.
//   2. StandardBlocks.Nx and the journal entry point - everything that talks
//      to NX. Left out when SB_TEST is defined.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace StandardBlocks.Core
{
    // ------------------------------------------------------------------
    // Text helpers. Everything is formatted with the invariant culture so
    // listings and logs look the same on every PC.
    // ------------------------------------------------------------------
    public static class Text
    {
        public static string F(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }

        // Short number: 5.8, -10, 0 (tiny values print as 0, never -0).
        public static string Num(double v)
        {
            if (Math.Abs(v) < 0.0005) v = 0.0;
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // Fixed three decimals: 5.800
        public static string Num3(double v)
        {
            if (Math.Abs(v) < 0.0005) v = 0.0;
            return v.ToString("0.000", CultureInfo.InvariantCulture);
        }

        public static string Count(int n, string singular, string plural)
        {
            return n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? singular : plural);
        }
    }

    // ------------------------------------------------------------------
    // Geometry
    // ------------------------------------------------------------------
    public struct Vec3
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 FromArray(double[] a)
        {
            if (a == null || a.Length < 3) throw new ArgumentException("Expected an array of 3 numbers.");
            return new Vec3(a[0], a[1], a[2]);
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vec3 operator -(Vec3 a) { return new Vec3(-a.X, -a.Y, -a.Z); }
        public static Vec3 operator *(Vec3 a, double k) { return new Vec3(a.X * k, a.Y * k, a.Z * k); }
        public static Vec3 operator *(double k, Vec3 a) { return new Vec3(a.X * k, a.Y * k, a.Z * k); }

        public double Dot(Vec3 b) { return X * b.X + Y * b.Y + Z * b.Z; }

        public Vec3 Cross(Vec3 b)
        {
            return new Vec3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        }

        public double Length { get { return Math.Sqrt(Dot(this)); } }

        public bool IsZero { get { return Length < 1e-12; } }

        public Vec3 Normalized()
        {
            double len = Length;
            if (len < 1e-12) throw new InvalidOperationException("Cannot normalise a zero-length vector.");
            return this * (1.0 / len);
        }

        public override string ToString()
        {
            return "(" + Text.Num(X) + ", " + Text.Num(Y) + ", " + Text.Num(Z) + ")";
        }
    }

    public static class Geo
    {
        // Small allowance for floating-point noise when comparing against a tolerance.
        public const double Eps = 1e-9;

        // True when the two directions are parallel or anti-parallel within angleTolDeg.
        public static bool AreParallel(Vec3 a, Vec3 b, double angleTolDeg)
        {
            double c = Math.Abs(a.Normalized().Dot(b.Normalized()));
            return c >= Math.Cos(angleTolDeg * Math.PI / 180.0) - Eps;
        }

        public static double DistanceToLine(Vec3 p, Vec3 linePoint, Vec3 lineDir)
        {
            Vec3 u = lineDir.Normalized();
            Vec3 d = p - linePoint;
            return (d - u * d.Dot(u)).Length;
        }
    }

    // Axis-aligned bounding box.
    public sealed class Aabb
    {
        public readonly Vec3 Min;
        public readonly Vec3 Max;

        public Aabb(Vec3 min, Vec3 max)
        {
            Min = new Vec3(Math.Min(min.X, max.X), Math.Min(min.Y, max.Y), Math.Min(min.Z, max.Z));
            Max = new Vec3(Math.Max(min.X, max.X), Math.Max(min.Y, max.Y), Math.Max(min.Z, max.Z));
        }

        // NX order: xmin, ymin, zmin, xmax, ymax, zmax
        public static Aabb FromUfBox(double[] b)
        {
            if (b == null || b.Length < 6) throw new ArgumentException("Expected 6 numbers.");
            return new Aabb(new Vec3(b[0], b[1], b[2]), new Vec3(b[3], b[4], b[5]));
        }

        public static Aabb FromPoints(params Vec3[] points)
        {
            if (points == null || points.Length == 0) throw new ArgumentException("No points.");
            Aabb box = new Aabb(points[0], points[0]);
            for (int i = 1; i < points.Length; i++) box = box.Union(new Aabb(points[i], points[i]));
            return box;
        }

        public Aabb Union(Aabb o)
        {
            if (o == null) return this;
            return new Aabb(
                new Vec3(Math.Min(Min.X, o.Min.X), Math.Min(Min.Y, o.Min.Y), Math.Min(Min.Z, o.Min.Z)),
                new Vec3(Math.Max(Max.X, o.Max.X), Math.Max(Max.Y, o.Max.Y), Math.Max(Max.Z, o.Max.Z)));
        }

        public Aabb Expanded(double t)
        {
            Vec3 d = new Vec3(t, t, t);
            return new Aabb(Min - d, Max + d);
        }

        public bool Overlaps(Aabb o)
        {
            return Min.X <= o.Max.X + Geo.Eps && o.Min.X <= Max.X + Geo.Eps
                && Min.Y <= o.Max.Y + Geo.Eps && o.Min.Y <= Max.Y + Geo.Eps
                && Min.Z <= o.Max.Z + Geo.Eps && o.Min.Z <= Max.Z + Geo.Eps;
        }

        public bool Contains(Vec3 p, double tol)
        {
            return p.X >= Min.X - tol - Geo.Eps && p.X <= Max.X + tol + Geo.Eps
                && p.Y >= Min.Y - tol - Geo.Eps && p.Y <= Max.Y + tol + Geo.Eps
                && p.Z >= Min.Z - tol - Geo.Eps && p.Z <= Max.Z + tol + Geo.Eps;
        }

        public override string ToString()
        {
            return Min + " to " + Max;
        }
    }

    // ------------------------------------------------------------------
    // Minimal JSON reader (objects, arrays, strings, numbers, true/false/null).
    // Written in here because a single-file journal can't count on
    // System.Text.Json or other JSON libraries being available.
    // ------------------------------------------------------------------
    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    public static class MiniJson
    {
        // Returns Dictionary<string, object>, List<object>, string, double, bool or null.
        public static object Parse(string text)
        {
            if (text == null) throw new JsonException("There is no text to read.");
            JsonParser p = new JsonParser(text);
            p.SkipWhitespace();
            object value = p.ParseValue();
            p.SkipWhitespace();
            if (!p.AtEnd) throw p.Fail("Unexpected text after the end of the JSON");
            return value;
        }

        private sealed class JsonParser
        {
            private readonly string s;
            private int pos;

            public JsonParser(string text)
            {
                s = text;
                pos = (s.Length > 0 && s[0] == '\uFEFF') ? 1 : 0;
            }

            public bool AtEnd { get { return pos >= s.Length; } }

            public void SkipWhitespace()
            {
                while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\r' || s[pos] == '\n')) pos++;
            }

            public JsonException Fail(string message)
            {
                int line = 1, col = 1;
                for (int k = 0; k < pos && k < s.Length; k++)
                {
                    if (s[k] == '\n') { line++; col = 1; }
                    else col++;
                }
                return new JsonException(Text.F("{0} at line {1}, column {2}.", message, line, col));
            }

            public object ParseValue()
            {
                if (AtEnd) throw Fail("Unexpected end of file");
                char c = s[pos];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == '-' || IsDigit(c)) return ParseNumber();
                if (Match("true")) return true;
                if (Match("false")) return false;
                if (Match("null")) return null;
                throw Fail(Text.F("Unexpected character '{0}'", c));
            }

            private static bool IsDigit(char c) { return c >= '0' && c <= '9'; }

            private bool Peek(char c) { return pos < s.Length && s[pos] == c; }

            private void Expect(char c)
            {
                if (!Peek(c)) throw Fail(Text.F("Expected '{0}'", c));
                pos++;
            }

            private bool Match(string word)
            {
                if (pos + word.Length <= s.Length && string.CompareOrdinal(s, pos, word, 0, word.Length) == 0)
                {
                    pos += word.Length;
                    return true;
                }
                return false;
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
                pos++;
                SkipWhitespace();
                if (Peek('}')) { pos++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    if (!Peek('"')) throw Fail("Expected a property name in double quotes");
                    int keyPos = pos;
                    string key = ParseString();
                    if (result.ContainsKey(key))
                    {
                        pos = keyPos;
                        throw Fail(Text.F("Property \"{0}\" appears twice", key));
                    }
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    result[key] = ParseValue();
                    SkipWhitespace();
                    if (Peek(','))
                    {
                        pos++;
                        SkipWhitespace();
                        if (Peek('}')) throw Fail("Remove the comma before '}'");
                        continue;
                    }
                    if (Peek('}')) { pos++; return result; }
                    throw Fail("Expected ',' or '}'");
                }
            }

            private List<object> ParseArray()
            {
                List<object> result = new List<object>();
                pos++;
                SkipWhitespace();
                if (Peek(']')) { pos++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (Peek(','))
                    {
                        pos++;
                        SkipWhitespace();
                        if (Peek(']')) throw Fail("Remove the comma before ']'");
                        continue;
                    }
                    if (Peek(']')) { pos++; return result; }
                    throw Fail("Expected ',' or ']'");
                }
            }

            private string ParseString()
            {
                pos++;
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Fail("Unterminated string");
                    char c = s[pos++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (AtEnd) throw Fail("Unterminated string");
                        char e = s[pos++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                int code;
                                if (pos + 4 > s.Length
                                    || !int.TryParse(s.Substring(pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code))
                                    throw Fail("Invalid \\u escape");
                                sb.Append((char)code);
                                pos += 4;
                                break;
                            default:
                                pos -= 2;
                                throw Fail(Text.F("Unknown escape '\\{0}' (in paths, use / or \\\\)", e));
                        }
                    }
                    else if (c < ' ')
                    {
                        pos--;
                        throw Fail("Line break or control character inside a string");
                    }
                    else sb.Append(c);
                }
            }

            private double ParseNumber()
            {
                int start = pos;
                if (Peek('-')) pos++;
                if (AtEnd || !IsDigit(s[pos])) throw Fail("Invalid number");
                while (!AtEnd && IsDigit(s[pos])) pos++;
                if (Peek('.'))
                {
                    pos++;
                    if (AtEnd || !IsDigit(s[pos])) throw Fail("Invalid number: digits expected after '.'");
                    while (!AtEnd && IsDigit(s[pos])) pos++;
                }
                if (Peek('e') || Peek('E'))
                {
                    pos++;
                    if (Peek('+') || Peek('-')) pos++;
                    if (AtEnd || !IsDigit(s[pos])) throw Fail("Invalid number: digits expected in exponent");
                    while (!AtEnd && IsDigit(s[pos])) pos++;
                }
                return double.Parse(s.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }

    // ------------------------------------------------------------------
    // Configuration (StandardBlocks.config.json)
    // ------------------------------------------------------------------
    public sealed class ConfigException : Exception
    {
        public ConfigException(string message) : base(message) { }
    }

    public sealed class HoleType
    {
        public string Name;       // value of SB_Hole_Type, e.g. "R6"
        public string Purpose;    // free text for reports
        public double Diameter;   // plate cut diameter, mm

        public HoleType(string name, string purpose, double diameter)
        {
            Name = name;
            Purpose = purpose;
            Diameter = diameter;
        }
    }

    public sealed class SbConfig
    {
        public List<HoleType> HoleTypes = new List<HoleType>();

        // Tolerances (mm and degrees)
        public double DiameterTolerance = 0.01;
        public double DistanceTolerance = 0.01;
        public double AngleToleranceDeg = 0.5;
        public double BoundingBoxTolerance = 1.0;

        // Plates
        public double MaxPlateThickness = 6.0;
        public double MinPlateAspectRatio = 3.0;   // plate width / thickness, at least

        // Tool bodies
        public int ToolLayer = 250;
        public string ToolReferenceSet = "SB_TOOLS";
        public string ModelReferenceSet = "MODEL";
        public double ToolInsideDepth = 5.0;
        public double ToolProjection = 10.0;

        // Attribute names
        public string BlockAttribute = "SB_BLOCK";
        public string BlockAttributeValue = "true";
        public string HoleTypeAttribute = "SB_Hole_Type";
        public string PartNumberAttribute = "DB_PART_NO";
        public string PartRevisionAttribute = "DB_PART_REV";

        // Features
        public string FeaturePrefix = "SB_";

        // Log folder: absolute, or relative to the config file's folder. Empty = no log file.
        public string LogFolder = "logs";

        // Non-fatal problems found while reading the file (e.g. misspelt settings).
        public List<string> Warnings = new List<string>();

        public double ExpectedToolLength { get { return ToolInsideDepth + ToolProjection; } }

        // Exact match first, then ignoring case.
        public HoleType FindHoleType(string name)
        {
            if (name == null) return null;
            string n = name.Trim();
            foreach (HoleType h in HoleTypes) if (string.Equals(h.Name, n, StringComparison.Ordinal)) return h;
            foreach (HoleType h in HoleTypes) if (string.Equals(h.Name, n, StringComparison.OrdinalIgnoreCase)) return h;
            return null;
        }

        public List<HoleType> FindHoleTypesByDiameter(double diameter)
        {
            List<HoleType> result = new List<HoleType>();
            foreach (HoleType h in HoleTypes)
            {
                if (Math.Abs(h.Diameter - diameter) <= DiameterTolerance + Geo.Eps) result.Add(h);
            }
            return result;
        }

        public string HoleTypeNames()
        {
            List<string> names = new List<string>();
            foreach (HoleType h in HoleTypes) names.Add(h.Name);
            return string.Join(", ", names.ToArray());
        }

        public string Describe()
        {
            List<string> parts = new List<string>();
            foreach (HoleType h in HoleTypes) parts.Add(h.Name + " = dia " + Text.Num(h.Diameter));
            return Text.Count(HoleTypes.Count, "hole type", "hole types") + ": " + string.Join(", ", parts.ToArray());
        }

        public static SbConfig Parse(string json)
        {
            object root;
            try { root = MiniJson.Parse(json); }
            catch (JsonException ex) { throw new ConfigException("The config file is not valid JSON. " + ex.Message); }

            Dictionary<string, object> top = root as Dictionary<string, object>;
            if (top == null) throw new ConfigException("The config file must contain one JSON object: { ... }.");

            SbConfig c = new SbConfig();
            ConfigReader r = new ConfigReader(c.Warnings);
            r.CheckKeys(top, "", "holeTypes", "tolerances", "plates", "toolBodies", "attributes", "features", "log");

            c.HoleTypes = ParseHoleTypes(top);

            Dictionary<string, object> tol = r.Section(top, "", "tolerances");
            if (tol != null)
            {
                r.CheckKeys(tol, "tolerances", "diameter", "distance", "angleDegrees", "boundingBox");
                c.DiameterTolerance = r.Number(tol, "tolerances", "diameter", c.DiameterTolerance, 0, 1);
                c.DistanceTolerance = r.Number(tol, "tolerances", "distance", c.DistanceTolerance, 0, 1);
                c.AngleToleranceDeg = r.Number(tol, "tolerances", "angleDegrees", c.AngleToleranceDeg, 0, 10);
                c.BoundingBoxTolerance = r.Number(tol, "tolerances", "boundingBox", c.BoundingBoxTolerance, 0, 100);
            }

            Dictionary<string, object> plates = r.Section(top, "", "plates");
            if (plates != null)
            {
                r.CheckKeys(plates, "plates", "maxThickness", "minAspectRatio");
                c.MaxPlateThickness = r.Number(plates, "plates", "maxThickness", c.MaxPlateThickness, 0.1, 100);
                c.MinPlateAspectRatio = r.Number(plates, "plates", "minAspectRatio", c.MinPlateAspectRatio, 1, 100);
            }

            Dictionary<string, object> tools = r.Section(top, "", "toolBodies");
            if (tools != null)
            {
                r.CheckKeys(tools, "toolBodies", "layer", "referenceSet", "modelReferenceSet", "insideDepth", "projection");
                c.ToolLayer = r.Integer(tools, "toolBodies", "layer", c.ToolLayer, 1, 256);
                c.ToolReferenceSet = r.Str(tools, "toolBodies", "referenceSet", c.ToolReferenceSet, false);
                c.ModelReferenceSet = r.Str(tools, "toolBodies", "modelReferenceSet", c.ModelReferenceSet, false);
                c.ToolInsideDepth = r.Number(tools, "toolBodies", "insideDepth", c.ToolInsideDepth, 0, 100);
                c.ToolProjection = r.Number(tools, "toolBodies", "projection", c.ToolProjection, 0, 100);
            }

            Dictionary<string, object> attrs = r.Section(top, "", "attributes");
            if (attrs != null)
            {
                r.CheckKeys(attrs, "attributes", "block", "blockValue", "holeType", "partNumber", "partRevision");
                c.BlockAttribute = r.Str(attrs, "attributes", "block", c.BlockAttribute, false);
                c.BlockAttributeValue = r.Str(attrs, "attributes", "blockValue", c.BlockAttributeValue, false);
                c.HoleTypeAttribute = r.Str(attrs, "attributes", "holeType", c.HoleTypeAttribute, false);
                c.PartNumberAttribute = r.Str(attrs, "attributes", "partNumber", c.PartNumberAttribute, false);
                c.PartRevisionAttribute = r.Str(attrs, "attributes", "partRevision", c.PartRevisionAttribute, false);
            }

            Dictionary<string, object> features = r.Section(top, "", "features");
            if (features != null)
            {
                r.CheckKeys(features, "features", "prefix");
                c.FeaturePrefix = r.Str(features, "features", "prefix", c.FeaturePrefix, false);
                foreach (char ch in c.FeaturePrefix)
                {
                    if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                        throw new ConfigException("\"features.prefix\" may only contain letters, digits and underscores.");
                }
            }

            Dictionary<string, object> log = r.Section(top, "", "log");
            if (log != null)
            {
                r.CheckKeys(log, "log", "folder");
                c.LogFolder = r.Str(log, "log", "folder", c.LogFolder, true);
                foreach (char ch in c.LogFolder)
                {
                    if (ch < ' ')
                        throw new ConfigException("\"log.folder\" contains a control character. In JSON paths, write C:/nx_settings/logs or C:\\\\nx_settings\\\\logs, not single backslashes.");
                }
            }

            return c;
        }

        private static List<HoleType> ParseHoleTypes(Dictionary<string, object> top)
        {
            object v;
            if (!top.TryGetValue("holeTypes", out v) || v == null)
                throw new ConfigException("The config file has no \"holeTypes\" list. It must list every SB_Hole_Type with its plate cut diameter.");
            List<object> list = v as List<object>;
            if (list == null || list.Count == 0)
                throw new ConfigException("\"holeTypes\" must be a non-empty list: [ { \"type\": \"R6\", \"diameter\": 5.8 }, ... ].");

            List<HoleType> result = new List<HoleType>();
            for (int i = 0; i < list.Count; i++)
            {
                string where = Text.F("holeTypes[{0}]", i);
                Dictionary<string, object> o = list[i] as Dictionary<string, object>;
                if (o == null) throw new ConfigException(where + " must be { \"type\": ..., \"diameter\": ... }.");

                object t;
                string name = (o.TryGetValue("type", out t) ? t as string : null);
                if (string.IsNullOrEmpty(name) || name.Trim().Length == 0)
                    throw new ConfigException(where + " needs a \"type\", e.g. \"R6\".");
                name = name.Trim();

                object d;
                if (!o.TryGetValue("diameter", out d) || !(d is double))
                    throw new ConfigException(Text.F("Hole type {0} needs a numeric \"diameter\" (the plate cut diameter in mm).", name));
                double dia = (double)d;
                if (dia <= 0 || dia > 100)
                    throw new ConfigException(Text.F("Hole type {0} has diameter {1}; it must be between 0 and 100 mm.", name, Text.Num(dia)));

                object p;
                string purpose = (o.TryGetValue("purpose", out p) ? p as string : null) ?? "";

                foreach (string key in o.Keys)
                {
                    if (key != "type" && key != "diameter" && key != "purpose" && !key.StartsWith("_", StringComparison.Ordinal))
                        throw new ConfigException(Text.F("Hole type {0} has an unknown setting \"{1}\". Allowed: type, diameter, purpose.", name, key));
                }

                foreach (HoleType existing in result)
                {
                    if (string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
                        throw new ConfigException(Text.F("Hole type {0} is listed twice in \"holeTypes\".", name));
                }
                result.Add(new HoleType(name, purpose, dia));
            }
            return result;
        }
    }

    internal sealed class ConfigReader
    {
        private readonly List<string> warnings;

        public ConfigReader(List<string> warnings)
        {
            this.warnings = warnings;
        }

        private static string Join(string path, string key)
        {
            return path.Length == 0 ? key : path + "." + key;
        }

        // Keys starting with "_" are treated as comments.
        public void CheckKeys(Dictionary<string, object> obj, string path, params string[] known)
        {
            foreach (string key in obj.Keys)
            {
                if (key.StartsWith("_", StringComparison.Ordinal)) continue;
                if (Array.IndexOf(known, key) < 0)
                    warnings.Add(Text.F("Unknown setting \"{0}\" was ignored. Check the spelling.", Join(path, key)));
            }
        }

        public Dictionary<string, object> Section(Dictionary<string, object> obj, string path, string key)
        {
            object v;
            if (!obj.TryGetValue(key, out v) || v == null) return null;
            Dictionary<string, object> d = v as Dictionary<string, object>;
            if (d == null) throw new ConfigException(Text.F("\"{0}\" must be a {{ ... }} section.", Join(path, key)));
            return d;
        }

        public double Number(Dictionary<string, object> obj, string path, string key, double def, double min, double max)
        {
            object v;
            if (!obj.TryGetValue(key, out v) || v == null) return def;
            if (!(v is double)) throw new ConfigException(Text.F("\"{0}\" must be a number.", Join(path, key)));
            double d = (double)v;
            if (d < min || d > max)
                throw new ConfigException(Text.F("\"{0}\" is {1}; it must be between {2} and {3}.", Join(path, key), Text.Num(d), Text.Num(min), Text.Num(max)));
            return d;
        }

        public int Integer(Dictionary<string, object> obj, string path, string key, int def, int min, int max)
        {
            double d = Number(obj, path, key, def, min, max);
            if (Math.Abs(d - Math.Round(d)) > Geo.Eps) throw new ConfigException(Text.F("\"{0}\" must be a whole number.", Join(path, key)));
            return (int)Math.Round(d);
        }

        public string Str(Dictionary<string, object> obj, string path, string key, string def, bool allowEmpty)
        {
            object v;
            if (!obj.TryGetValue(key, out v) || v == null) return def;
            string s = v as string;
            if (s == null) throw new ConfigException(Text.F("\"{0}\" must be text in double quotes.", Join(path, key)));
            s = s.Trim();
            if (!allowEmpty && s.Length == 0) throw new ConfigException(Text.F("\"{0}\" must not be empty.", Join(path, key)));
            return s;
        }
    }

    // ------------------------------------------------------------------
    // Hole rules: tool body diameter against the hole table
    // ------------------------------------------------------------------
    public enum HoleCheckStatus { Ok, MissingType, UnknownType, DiameterMismatch }

    public sealed class HoleCheck
    {
        public HoleCheckStatus Status;
        public HoleType Expected;       // null for MissingType / UnknownType
        public double MeasuredDiameter;
        public string Message;          // problem, or a note when Ok (e.g. letter case)

        public bool IsOk { get { return Status == HoleCheckStatus.Ok; } }
    }

    public static class HoleRules
    {
        public static HoleCheck Check(SbConfig cfg, string holeType, double measuredDiameter)
        {
            HoleCheck r = new HoleCheck();
            r.MeasuredDiameter = measuredDiameter;

            if (holeType == null || holeType.Trim().Length == 0)
            {
                r.Status = HoleCheckStatus.MissingType;
                r.Message = Text.F("has no {0} value", cfg.HoleTypeAttribute);
                return r;
            }

            string value = holeType.Trim();
            string note = null;
            HoleType ht = cfg.FindHoleType(value);
            if (ht == null)
            {
                // Some blocks hold the cut diameter (e.g. 5.8) instead of the type name.
                double dia;
                if (!TryParseNumber(value, out dia))
                {
                    r.Status = HoleCheckStatus.UnknownType;
                    r.Message = Text.F("{0} \"{1}\" is not in the hole table (known types: {2})",
                        cfg.HoleTypeAttribute, value, cfg.HoleTypeNames());
                    return r;
                }

                List<HoleType> matches = cfg.FindHoleTypesByDiameter(dia);
                if (matches.Count != 1)
                {
                    r.Status = HoleCheckStatus.UnknownType;
                    r.Message = Text.F(matches.Count == 0
                            ? "{0} is {1}, which looks like a diameter, but no hole type has that diameter ({2})"
                            : "{0} is {1}, which looks like a diameter, but more than one hole type has that diameter ({2})",
                        cfg.HoleTypeAttribute, value, cfg.Describe());
                    return r;
                }
                ht = matches[0];
                note = Text.F("{0} holds a diameter ({1}) instead of a type name, so it was read as {2}. Better: a String attribute set to \"{2}\"",
                    cfg.HoleTypeAttribute, value, ht.Name);
            }
            else if (!string.Equals(ht.Name, value, StringComparison.Ordinal))
            {
                note = Text.F("{0} is written \"{1}\"; the hole table spells it \"{2}\"", cfg.HoleTypeAttribute, value, ht.Name);
            }

            r.Expected = ht;
            if (Math.Abs(measuredDiameter - ht.Diameter) > cfg.DiameterTolerance + Geo.Eps)
            {
                r.Status = HoleCheckStatus.DiameterMismatch;
                r.Message = Text.F("is dia {0} but {1} needs dia {2} (tolerance {3})",
                    Text.Num3(measuredDiameter), ht.Name, Text.Num3(ht.Diameter), Text.Num(cfg.DiameterTolerance));
                if (note != null) r.Message += ". " + note;
                return r;
            }

            r.Status = HoleCheckStatus.Ok;
            r.Message = note;
            return r;
        }

        // Accepts "5.8", "5.800000" and "5,8".
        public static bool TryParseNumber(string s, out double value)
        {
            string t = (s ?? "").Trim();
            if (t.IndexOf('.') < 0) t = t.Replace(',', '.');
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    // ------------------------------------------------------------------
    // Tool body geometry: turn a tool body's faces into a cylinder segment
    // ------------------------------------------------------------------
    public enum FaceKind { Plane, Cylinder, Other }

    // One face as NX reports it: for a plane, Point is on the plane and
    // Direction is its normal; for a cylinder, Point is on the axis and
    // Direction is the axis.
    public sealed class FaceSample
    {
        public FaceKind Kind;
        public Vec3 Point;
        public Vec3 Direction;
        public double Radius;

        public FaceSample(FaceKind kind, Vec3 point, Vec3 direction, double radius)
        {
            Kind = kind;
            Point = point;
            Direction = direction;
            Radius = radius;
        }
    }

    public sealed class ToolCylinder
    {
        public Vec3 Start;            // centre of one end face; inside the block when DirectionKnown
        public Vec3 End;              // centre of the other end face; past the block's contact face
        public double Radius;
        public int StartFaceIndex;    // index into the face list the cylinder was built from
        public int EndFaceIndex;
        public bool DirectionKnown;   // true once we know which end is inside the block

        public Vec3 Direction { get { return (End - Start).Normalized(); } }
        public double Length { get { return (End - Start).Length; } }
        public double Diameter { get { return 2.0 * Radius; } }

        public ToolCylinder Copy()
        {
            ToolCylinder c = new ToolCylinder();
            c.Start = Start;
            c.End = End;
            c.Radius = Radius;
            c.StartFaceIndex = StartFaceIndex;
            c.EndFaceIndex = EndFaceIndex;
            c.DirectionKnown = DirectionKnown;
            return c;
        }

        public ToolCylinder Reversed()
        {
            ToolCylinder c = Copy();
            c.Start = End;
            c.End = Start;
            c.StartFaceIndex = EndFaceIndex;
            c.EndFaceIndex = StartFaceIndex;
            return c;
        }
    }

    public sealed class CylinderResult
    {
        public ToolCylinder Cylinder;                       // null if the body isn't a plain cylinder
        public List<string> Problems = new List<string>();

        public bool Ok { get { return Cylinder != null; } }
    }

    public static class ToolGeometry
    {
        // A tool body must be one straight cylinder (possibly split into several
        // coaxial cylindrical faces) with two flat end faces square to the axis.
        public static CylinderResult Build(IList<FaceSample> faces, double distTol, double angleTolDeg)
        {
            CylinderResult result = new CylinderResult();
            List<int> cyls = new List<int>();
            List<int> planes = new List<int>();
            int others = 0;
            for (int i = 0; i < faces.Count; i++)
            {
                if (faces[i].Kind == FaceKind.Cylinder) cyls.Add(i);
                else if (faces[i].Kind == FaceKind.Plane) planes.Add(i);
                else others++;
            }

            if (cyls.Count == 0)
            {
                result.Problems.Add("has no cylindrical face");
                return result;
            }
            if (others > 0)
            {
                result.Problems.Add(Text.F("has {0} that are neither flat nor cylindrical (chamfer or fillet?). Model tool bodies as plain cylinders",
                    Text.Count(others, "face", "faces")));
                return result;
            }

            FaceSample first = faces[cyls[0]];
            if (first.Direction.IsZero)
            {
                result.Problems.Add("NX returned no axis direction for its cylindrical face");
                return result;
            }
            Vec3 axisPoint = first.Point;
            Vec3 axis = first.Direction.Normalized();
            double radius = first.Radius;

            for (int k = 1; k < cyls.Count; k++)
            {
                FaceSample f = faces[cyls[k]];
                if (f.Direction.IsZero
                    || !Geo.AreParallel(f.Direction, axis, angleTolDeg)
                    || Geo.DistanceToLine(f.Point, axisPoint, axis) > distTol + Geo.Eps
                    || Math.Abs(f.Radius - radius) > distTol + Geo.Eps)
                {
                    result.Problems.Add("has cylindrical faces that are not one straight cylinder");
                    return result;
                }
            }

            if (planes.Count != 2)
            {
                result.Problems.Add(Text.F("should have exactly 2 flat end faces but has {0}", planes.Count));
                return result;
            }

            double[] t = new double[2];
            for (int k = 0; k < 2; k++)
            {
                FaceSample p = faces[planes[k]];
                if (p.Direction.IsZero || !Geo.AreParallel(p.Direction, axis, angleTolDeg))
                {
                    result.Problems.Add("has an end face that is not square to the cylinder axis");
                    return result;
                }
                t[k] = (p.Point - axisPoint).Dot(axis);
            }

            if (Math.Abs(t[1] - t[0]) <= distTol)
            {
                result.Problems.Add("has zero length");
                return result;
            }

            int lo = t[0] <= t[1] ? 0 : 1;
            int hi = 1 - lo;
            ToolCylinder c = new ToolCylinder();
            c.Start = axisPoint + axis * t[lo];
            c.End = axisPoint + axis * t[hi];
            c.Radius = radius;
            c.StartFaceIndex = planes[lo];
            c.EndFaceIndex = planes[hi];
            c.DirectionKnown = false;
            result.Cylinder = c;
            return result;
        }

        // Each tool body starts inside the block and ends past its contact face,
        // so exactly one end should lie inside the block's bounding box (in the
        // block part's own coordinates). Returns a copy with Start = inside end.
        public static ToolCylinder OrientFromBlock(ToolCylinder c, Aabb blockBox, double tol)
        {
            ToolCylinder r = c.Copy();
            r.DirectionKnown = false;
            if (blockBox == null) return r;
            bool startIn = blockBox.Contains(c.Start, tol);
            bool endIn = blockBox.Contains(c.End, tol);
            if (startIn && !endIn)
            {
                r.DirectionKnown = true;
                return r;
            }
            if (endIn && !startIn)
            {
                r = c.Reversed();
                r.DirectionKnown = true;
                return r;
            }
            return r;
        }

        // Applies an orientation worked out elsewhere (e.g. in block coordinates)
        // to a cylinder built from the matching faces (e.g. in assembly coordinates).
        public static ToolCylinder OrientLike(ToolCylinder c, ToolCylinder reference)
        {
            ToolCylinder r = (c.StartFaceIndex == reference.StartFaceIndex) ? c.Copy() : c.Reversed();
            r.DirectionKnown = reference.DirectionKnown;
            return r;
        }

        // Null if the length is as expected, otherwise a description of the problem.
        public static string CheckLength(ToolCylinder c, SbConfig cfg)
        {
            double expected = cfg.ExpectedToolLength;
            if (Math.Abs(c.Length - expected) <= cfg.DistanceTolerance + Geo.Eps) return null;
            return Text.F("is {0} mm long; the standard is {1} mm ({2} inside the block + {3} past the contact face)",
                Text.Num3(c.Length), Text.Num(expected), Text.Num(cfg.ToolInsideDepth), Text.Num(cfg.ToolProjection));
        }
    }

    // ------------------------------------------------------------------
    // Plates and detection (used from step 2)
    // ------------------------------------------------------------------
    public sealed class PlanarFace
    {
        public Vec3 Normal;
        public Vec3 Point;

        public PlanarFace(Vec3 normal, Vec3 point)
        {
            Normal = normal;
            Point = point;
        }
    }

    // A flat plate: unit Normal, and the range [Low, High] of Normal.Dot(x)
    // covered by its material.
    public sealed class PlateFrame
    {
        public Vec3 Normal;
        public double Low;
        public double High;

        public double Thickness { get { return High - Low; } }
    }

    public static class PlateGeometry
    {
        // Groups the flat faces by direction (a face and its opposite count as
        // one direction). For each direction that has faces on both sides, the
        // spread of the faces along it is the solid's size in that direction.
        // For a flat plate the smallest spread is the thickness: top and bottom
        // faces. Side faces, slots and tabs spread across the whole plate.
        public static PlateFrame FromPlanarFaces(IList<PlanarFace> faces, double angleTolDeg, out string problem)
        {
            problem = null;
            List<Vec3> dirs = new List<Vec3>();
            List<double> lows = new List<double>();
            List<double> highs = new List<double>();
            List<bool> plus = new List<bool>();
            List<bool> minus = new List<bool>();

            if (faces != null)
            {
                foreach (PlanarFace f in faces)
                {
                    if (f.Normal.IsZero) continue;
                    int g = -1;
                    for (int i = 0; i < dirs.Count; i++)
                    {
                        if (Geo.AreParallel(f.Normal, dirs[i], angleTolDeg)) { g = i; break; }
                    }
                    if (g < 0)
                    {
                        dirs.Add(f.Normal.Normalized());
                        lows.Add(double.MaxValue);
                        highs.Add(double.MinValue);
                        plus.Add(false);
                        minus.Add(false);
                        g = dirs.Count - 1;
                    }
                    double o = f.Point.Dot(dirs[g]);
                    if (o < lows[g]) lows[g] = o;
                    if (o > highs[g]) highs[g] = o;
                    if (f.Normal.Dot(dirs[g]) > 0) plus[g] = true;
                    else minus[g] = true;
                }
            }

            int best = -1;
            for (int i = 0; i < dirs.Count; i++)
            {
                if (!plus[i] || !minus[i] || highs[i] - lows[i] <= Geo.Eps) continue;
                if (best < 0 || highs[i] - lows[i] < highs[best] - lows[best]) best = i;
            }
            if (best < 0)
            {
                problem = "has no pair of opposite flat faces, so it is not a flat plate";
                return null;
            }

            PlateFrame frame = new PlateFrame();
            frame.Normal = dirs[best];
            frame.Low = lows[best];
            frame.High = highs[best];
            return frame;
        }

        // A plate is much wider than it is thick. Keeps blocks, bolts and
        // dowels from being treated as plates. box is the solid's bounding box.
        public static bool IsPlateLike(PlateFrame frame, Aabb box, double minAspectRatio)
        {
            if (frame == null || box == null) return false;
            Vec3 size = box.Max - box.Min;
            double largest = Math.Max(size.X, Math.Max(size.Y, size.Z));
            return largest >= minAspectRatio * frame.Thickness - Geo.Eps;
        }
    }

    public sealed class PlateModel
    {
        public string Id;        // unique key for this plate body in the assembly
        public string Label;     // e.g. "PART-123 rev A"
        public PlateFrame Frame;
        public Aabb Box;         // assembly coordinates
    }

    public enum HitStatus
    {
        Selected,             // this plate gets the hole
        NotParallel,          // tool axis isn't normal to the plate
        NoIntersection,       // tool body doesn't touch the plate
        TooThick,             // plate is thicker than plates.maxThickness
        BehindAnotherPlate,   // only the first plate the tool reaches is cut
        AmbiguousDirection    // several candidates and we can't tell which is first
    }

    public sealed class ToolHit
    {
        public PlateModel Plate;
        public HitStatus Status;
        public bool FullyThrough;   // tool covers the whole plate thickness
        public double Distance;     // from the tool's inside end to the plate's near face (mm)
    }

    public static class Detection
    {
        // Broad phase: bounding boxes overlap within tol.
        public static bool BoxesOverlap(Aabb a, Aabb b, double tol)
        {
            return a.Expanded(tol).Overlaps(b);
        }

        // Narrow phase for one tool body against candidate plates. "intersects"
        // answers whether the tool body actually touches the plate body. In NX
        // that is a solid interference check; in tests it's a stand-in.
        public static List<ToolHit> EvaluateTool(ToolCylinder tool, IList<PlateModel> plates, Func<PlateModel, bool> intersects, SbConfig cfg)
        {
            List<ToolHit> hits = new List<ToolHit>();
            List<ToolHit> candidates = new List<ToolHit>();
            Vec3 dir = tool.Direction;

            foreach (PlateModel plate in plates)
            {
                ToolHit h = new ToolHit();
                h.Plate = plate;
                hits.Add(h);

                if (!Geo.AreParallel(dir, plate.Frame.Normal, cfg.AngleToleranceDeg)) { h.Status = HitStatus.NotParallel; continue; }
                if (!intersects(plate)) { h.Status = HitStatus.NoIntersection; continue; }
                if (plate.Frame.Thickness > cfg.MaxPlateThickness + cfg.DistanceTolerance + Geo.Eps) { h.Status = HitStatus.TooThick; continue; }

                // Measure along the plate normal, flipped to point the same way as the tool.
                double sign = dir.Dot(plate.Frame.Normal) >= 0 ? 1.0 : -1.0;
                Vec3 n = plate.Frame.Normal * sign;
                double near = sign > 0 ? plate.Frame.Low : -plate.Frame.High;
                double far = sign > 0 ? plate.Frame.High : -plate.Frame.Low;
                double toolStart = tool.Start.Dot(n);
                double toolEnd = tool.End.Dot(n);
                double tol = cfg.DistanceTolerance + Geo.Eps;

                h.Distance = near - toolStart;
                h.FullyThrough = toolStart <= near + tol && toolEnd >= far - tol;
                candidates.Add(h);
            }

            if (candidates.Count == 1)
            {
                candidates[0].Status = HitStatus.Selected;
            }
            else if (candidates.Count > 1)
            {
                if (!tool.DirectionKnown)
                {
                    foreach (ToolHit h in candidates) h.Status = HitStatus.AmbiguousDirection;
                }
                else
                {
                    ToolHit first = candidates[0];
                    foreach (ToolHit h in candidates) if (h.Distance < first.Distance) first = h;
                    foreach (ToolHit h in candidates) h.Status = (h == first) ? HitStatus.Selected : HitStatus.BehindAnotherPlate;
                }
            }
            return hits;
        }
    }

    // One tool body and what it found.
    public sealed class ToolEvaluation
    {
        public string BlockLabel;   // "001-4032-CAD2072 rev A"
        public string ToolName;     // "EXTRUDE(3)"
        public string HoleType;     // "R6"
        public ToolCylinder Tool;
        public List<ToolHit> Hits = new List<ToolHit>();

        public ToolHit Selected
        {
            get
            {
                foreach (ToolHit h in Hits) if (h.Status == HitStatus.Selected) return h;
                return null;
            }
        }

        public string Describe()
        {
            return Text.F("{0}, tool body {1} ({2})", BlockLabel, ToolName, HoleType);
        }
    }

    // The holes planned for one plate.
    public sealed class PlatePlan
    {
        public PlateModel Plate;
        public List<ToolEvaluation> Tools = new List<ToolEvaluation>();

        public List<string> HoleTypes()
        {
            List<string> types = new List<string>();
            foreach (ToolEvaluation t in Tools) types.Add(t.HoleType);
            return types;
        }

        public string Summary(SbConfig cfg)
        {
            return Plate.Label + ": " + Core.Summary.HoleCounts(HoleTypes(), cfg);
        }
    }

    public static class Planner
    {
        // Selected holes grouped by plate, plates in order of first appearance.
        public static List<PlatePlan> ByPlate(IList<ToolEvaluation> evals)
        {
            List<PlatePlan> plans = new List<PlatePlan>();
            foreach (ToolEvaluation e in evals)
            {
                ToolHit sel = e.Selected;
                if (sel == null) continue;
                PlatePlan plan = null;
                foreach (PlatePlan p in plans) if (p.Plate.Id == sel.Plate.Id) plan = p;
                if (plan == null)
                {
                    plan = new PlatePlan();
                    plan.Plate = sel.Plate;
                    plans.Add(plan);
                }
                plan.Tools.Add(e);
            }
            return plans;
        }

        // One plain-English problem per tool body, or null if all is well.
        public static string Problem(ToolEvaluation e, SbConfig cfg)
        {
            ToolHit sel = e.Selected;
            if (sel != null)
            {
                if (sel.FullyThrough) return null;
                return Text.F("{0} doesn't pass fully through {1} ({2} mm thick), so the hole would not go all the way through",
                    e.Describe(), sel.Plate.Label, Text.Num(sel.Plate.Frame.Thickness));
            }

            List<string> ambiguous = new List<string>();
            ToolHit thick = null;
            foreach (ToolHit h in e.Hits)
            {
                if (h.Status == HitStatus.AmbiguousDirection) ambiguous.Add(h.Plate.Label);
                if (h.Status == HitStatus.TooThick && thick == null) thick = h;
            }
            if (ambiguous.Count > 0)
                return Text.F("{0} passes through {1} and it can't tell which plate is against the block, so no hole is planned. Check that one end of the tool body is inside the block",
                    e.Describe(), string.Join(" and ", ambiguous.ToArray()));
            if (thick != null)
                return Text.F("{0} only reaches {1}, which is {2} mm thick (the maximum is {3}), so no hole is planned",
                    e.Describe(), thick.Plate.Label, Text.Num(thick.Plate.Frame.Thickness), Text.Num(cfg.MaxPlateThickness));
            return Text.F("{0} doesn't pass through any plate, so it will make no hole. Is the block against its plate?", e.Describe());
        }

        // Short reason for one candidate plate, for detail listings.
        public static string Describe(ToolHit h)
        {
            switch (h.Status)
            {
                case HitStatus.Selected:
                    return Text.F("HOLE, {0} mm from the block's inside end{1}", Text.Num(h.Distance), h.FullyThrough ? "" : ", NOT fully through");
                case HitStatus.NotParallel: return "not cut: the tool body isn't square to this plate";
                case HitStatus.NoIntersection: return "not cut: the tool body doesn't touch it";
                case HitStatus.TooThick: return Text.F("not cut: {0} mm thick", Text.Num(h.Plate.Frame.Thickness));
                case HitStatus.BehindAnotherPlate: return "not cut: behind the plate nearer the block";
                case HitStatus.AmbiguousDirection: return "not cut: can't tell which plate is first";
                default: return h.Status.ToString();
            }
        }
    }

    // Feature names: SB_<block>_<type> for a subtract and SB_<block>_<type>_LINK
    // for its WAVE links, with _2, _3... for further copies of the same block
    // cutting the same plate.
    public static class FeatureNames
    {
        // Letters, digits and underscores only: "001-4032-CAD2072" -> "001_4032_CAD2072".
        public static string Sanitize(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in (s ?? "").Trim())
                sb.Append((c < 128 && char.IsLetterOrDigit(c)) || c == '_' ? c : '_');
            return sb.ToString();
        }

        public static string Subtract(string prefix, string blockId, string holeType, int copy)
        {
            string name = prefix + Sanitize(blockId) + "_" + Sanitize(holeType);
            return copy > 1 ? name + "_" + copy.ToString(CultureInfo.InvariantCulture) : name;
        }

        public static string Link(string prefix, string blockId, string holeType, int copy)
        {
            return Subtract(prefix, blockId, holeType, copy) + "_LINK";
        }

        // 1 for the first copy of a block on a plate, 2 for the next, and so on.
        // existingInstances: instance IDs of this block part already on the plate.
        public static int CopyNumber(IList<string> existingInstances, string instance)
        {
            int i = existingInstances.IndexOf(instance);
            return i >= 0 ? i + 1 : existingInstances.Count + 1;
        }
    }

    // A part the macro would change, and whether it can.
    public sealed class PartStatus
    {
        public string Label;        // "0003985 rev A"
        public string Role;         // "plate", "top assembly"
        public bool FullyLoaded;
        public bool ReadOnly;
        public string Error;        // NX couldn't say; null normally

        public string Describe()
        {
            if (Error != null) return "UNKNOWN (" + Error + ")";
            if (ReadOnly) return "READ-ONLY";
            if (!FullyLoaded) return "writable, but only partly loaded";
            return "writable";
        }
    }

    public static class WriteCheck
    {
        // Plain-English problems that must stop the macro before it changes anything.
        public static List<string> Problems(IList<PartStatus> parts)
        {
            List<string> problems = new List<string>();
            foreach (PartStatus p in parts)
            {
                if (p.Error != null)
                    problems.Add(Text.F("{0} ({1}): NX couldn't say whether it is writable ({2})", p.Label, p.Role, p.Error));
                else if (p.ReadOnly)
                    problems.Add(Text.F("{0} ({1}) is read-only. Check it out in Teamcenter, or get write access, and try again", p.Label, p.Role));
                else if (!p.FullyLoaded)
                    problems.Add(Text.F("{0} ({1}) is only partly loaded. Load it fully and try again", p.Label, p.Role));
            }
            return problems;
        }
    }

    public static class ExistingHoles
    {
        // Index of the first cylindrical face that is coaxial with the tool
        // body, or -1. Used to warn (and still cut) when a plate already has a
        // hole where a block wants one, e.g. a reused plate. The caller must
        // leave out faces made by our own SB_ features.
        public static int FindCoaxial(ToolCylinder tool, IList<FaceSample> plateFaces, double distTol, double angleTolDeg)
        {
            Vec3 dir = tool.Direction;
            for (int i = 0; i < plateFaces.Count; i++)
            {
                FaceSample f = plateFaces[i];
                if (f.Kind != FaceKind.Cylinder || f.Direction.IsZero) continue;
                if (!Geo.AreParallel(f.Direction, dir, angleTolDeg)) continue;
                if (Geo.DistanceToLine(f.Point, tool.Start, dir) > distTol + Geo.Eps) continue;
                return i;
            }
            return -1;
        }
    }

    // ------------------------------------------------------------------
    // Summaries
    // ------------------------------------------------------------------
    public static class Summary
    {
        // "4 holes (2x R6, 2x CLR8.2)": hole types in hole-table order, then
        // any unknown types alphabetically.
        public static string HoleCounts(IEnumerable<string> holeTypes, SbConfig cfg)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            int total = 0;
            foreach (string t in holeTypes)
            {
                HoleType ht = cfg.FindHoleType(t);
                string key = ht != null ? ht.Name : (t ?? "?");
                int n;
                counts.TryGetValue(key, out n);
                counts[key] = n + 1;
                total++;
            }

            List<string> order = new List<string>();
            foreach (HoleType ht in cfg.HoleTypes) if (counts.ContainsKey(ht.Name)) order.Add(ht.Name);
            List<string> unknown = new List<string>();
            foreach (string k in counts.Keys) if (!order.Contains(k)) unknown.Add(k);
            unknown.Sort(StringComparer.Ordinal);
            order.AddRange(unknown);

            List<string> parts = new List<string>();
            foreach (string k in order) parts.Add(counts[k].ToString(CultureInfo.InvariantCulture) + "x " + k);
            string head = Text.Count(total, "hole", "holes");
            return parts.Count == 0 ? head : head + " (" + string.Join(", ", parts.ToArray()) + ")";
        }
    }

    // ------------------------------------------------------------------
    // Command names
    // ------------------------------------------------------------------
    public static class Commands
    {
        public const string Default = "inspect";

        // NX can't run one source journal from another (Session.Execute only
        // takes compiled DLLs), so each ribbon button plays an identical copy
        // of this file named after its command: SB_Inspect.cs runs "inspect",
        // SB_Sync.cs runs "sync". Any other name gives null.
        public static string FromJournalName(string journalPath)
        {
            if (string.IsNullOrEmpty(journalPath)) return null;
            // Split by hand so \ and / both work wherever this runs.
            string name = journalPath.Trim();
            name = name.Substring(Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/')) + 1);
            int dot = name.LastIndexOf('.');
            if (dot > 0) name = name.Substring(0, dot);
            if (!name.StartsWith("SB_", StringComparison.OrdinalIgnoreCase) || name.Length <= 3) return null;
            return Normalize(name.Substring(3));
        }

        // Arguments win; then the journal's file name; then the default.
        // NX passes one empty argument when a journal is played directly, so
        // blank arguments are skipped.
        public static string Resolve(string[] args, string journalPath)
        {
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (a != null && a.Trim().Length > 0) return Normalize(a);
                }
            }
            return FromJournalName(journalPath) ?? Default;
        }

        public static string Normalize(string command)
        {
            string c = (command ?? "").Trim().ToLowerInvariant();
            return c.Length == 0 ? Default : c;
        }
    }

    // ------------------------------------------------------------------
    // Report: one stream of lines to the Listing window and the log file.
    // ------------------------------------------------------------------
    public sealed class Report
    {
        private readonly List<Action<string>> sinks = new List<Action<string>>();
        private readonly List<string> lines = new List<string>();
        private int indent;

        public int WarningCount;
        public int ErrorCount;

        // replay = also send the lines written so far.
        public void AddSink(Action<string> sink, bool replay)
        {
            sinks.Add(sink);
            if (replay) foreach (string l in lines) sink(l);
        }

        public List<string> Lines { get { return lines; } }

        public void Indent() { indent++; }
        public void Outdent() { if (indent > 0) indent--; }

        public void Line(string text)
        {
            string pad = new string(' ', indent * 2);
            string[] parts = (text ?? "").Replace("\r\n", "\n").Split('\n');
            foreach (string p in parts)
            {
                string l = pad + p;
                lines.Add(l);
                foreach (Action<string> s in sinks) s(l);
            }
        }

        public void Blank() { Line(""); }

        public void Warn(string text)
        {
            WarningCount++;
            Line("WARNING: " + text);
        }

        public void Error(string text)
        {
            ErrorCount++;
            Line("ERROR: " + text);
        }
    }
}

#if !SB_TEST
namespace StandardBlocks.Nx
{
    using NXOpen;
    using NXOpen.Assemblies;
    using NXOpen.UF;
    using StandardBlocks.Core;
    // Aliases win over same-named types NXOpen might have.
    using Aabb = StandardBlocks.Core.Aabb;
    using FaceKind = StandardBlocks.Core.FaceKind;
    using Report = StandardBlocks.Core.Report;
    using Summary = StandardBlocks.Core.Summary;
    using Text = StandardBlocks.Core.Text;
    using Vec3 = StandardBlocks.Core.Vec3;

    public sealed class NxContext
    {
        public Session Session;
        public UFSession Uf;
        public UI Ui;
        public SbConfig Config;
        public Report Report;
        public string ConfigPath;
    }

    public static class NxAttr
    {
        // Value of a string user attribute, or null if the object doesn't have one.
        public static string GetString(NXObject obj, string title)
        {
            try
            {
                if (!obj.HasUserAttribute(title, NXObject.AttributeType.String, -1)) return null;
                return obj.GetStringUserAttribute(title, -1);
            }
            catch (NXException)
            {
                return null;
            }
        }

        public static void Set(NXObject obj, string title, string value)
        {
            obj.SetUserAttribute(title, -1, value ?? "", Update.Option.Now);
        }

        // True if the object has an attribute with this title of any type.
        public static bool HasAny(NXObject obj, string title)
        {
            try { return obj.HasUserAttribute(title, NXObject.AttributeType.Any, -1); }
            catch (NXException) { return false; }
        }

        // Like GetString, but also accepts other attribute types and titles
        // differing in letter case or spaces. note says what differed (or null).
        public static string GetLoose(NXObject obj, string title, out string note)
        {
            note = null;
            string exact = GetString(obj, title);
            if (exact != null) return exact;

            NXObject.AttributeInformation[] infos;
            try { infos = obj.GetUserAttributes(); }
            catch (NXException) { return null; }

            foreach (NXObject.AttributeInformation info in infos)
            {
                if (info.Title == null || !string.Equals(info.Title.Trim(), title, StringComparison.OrdinalIgnoreCase)) continue;
                string value;
                try { value = obj.GetUserAttributeAsString(info.Title, info.Type, -1); }
                catch (NXException) { continue; }
                List<string> diffs = new List<string>();
                if (info.Title != title) diffs.Add("its title is written \"" + info.Title + "\"");
                if (info.Type != NXObject.AttributeType.String) diffs.Add("it is a " + info.Type + " attribute, not String");
                if (diffs.Count > 0) note = string.Join(" and ", diffs.ToArray());
                return value;
            }
            return null;
        }

        // "TITLE (Type) = value; ..." for diagnostics.
        public static string Describe(NXObject obj)
        {
            NXObject.AttributeInformation[] infos;
            try { infos = obj.GetUserAttributes(); }
            catch (NXException ex) { return "(could not read attributes: " + ex.Message + ")"; }
            if (infos == null || infos.Length == 0) return "none";

            List<string> parts = new List<string>();
            foreach (NXObject.AttributeInformation info in infos)
            {
                string value;
                try { value = obj.GetUserAttributeAsString(info.Title, info.Type, -1); }
                catch (NXException) { value = "?"; }
                parts.Add(Text.F("\"{0}\" ({1}) = \"{2}\"", info.Title, info.Type, value));
            }
            return string.Join("; ", parts.ToArray());
        }
    }

    // Where a tool body's SB_Hole_Type was found.
    public sealed class HoleTypeLookup
    {
        public string Value;    // null if not found anywhere
        public string Source;   // "body" or "feature Extrude(3)"
        public string Note;     // what wasn't standard about it, or null

        // Looks on the body, then on the feature(s) that made it.
        public static HoleTypeLookup Find(Body body, string title)
        {
            HoleTypeLookup r = new HoleTypeLookup();
            string note;
            r.Value = NxAttr.GetLoose(body, title, out note);
            if (r.Value != null)
            {
                r.Source = "body";
                r.Note = note;
                return r;
            }
            foreach (NXOpen.Features.Feature f in OwningFeatures(body))
            {
                r.Value = NxAttr.GetLoose(f, title, out note);
                if (r.Value != null)
                {
                    r.Source = "feature " + f.GetFeatureName();
                    r.Note = note;
                    return r;
                }
            }
            return r;
        }

        public static NXOpen.Features.Feature[] OwningFeatures(Body body)
        {
            try { return body.GetFeatures(); }
            catch (NXException) { return new NXOpen.Features.Feature[0]; }
        }
    }

    public static class NxNames
    {
        public static string PartLabel(SbConfig cfg, Part part)
        {
            string number = NxAttr.GetString(part, cfg.PartNumberAttribute);
            if (string.IsNullOrEmpty(number)) number = part.Leaf;
            string rev = NxAttr.GetString(part, cfg.PartRevisionAttribute);
            return string.IsNullOrEmpty(rev) ? number : number + " rev " + rev;
        }

        // "TOP-ASSY > SUB-ASSY > 001-4032-CAD2072"
        public static string ComponentPath(Component comp)
        {
            List<string> names = new List<string>();
            for (Component c = comp; c != null; c = c.Parent) names.Insert(0, c.DisplayName);
            return string.Join(" > ", names.ToArray());
        }

        public static string BodyName(Body body)
        {
            if (!string.IsNullOrEmpty(body.Name)) return body.Name;
            return body.JournalIdentifier;
        }
    }

    public static class NxGeom
    {
        // Face types returned by UF_MODL_ask_face_data
        private const int UfCylinder = 16;
        private const int UfPlane = 22;

        public static FaceSample Sample(UFSession uf, Face face)
        {
            int type;
            double radius;
            double radData;
            int normDir;
            double[] point = new double[3];
            double[] dir = new double[3];
            double[] box = new double[6];
            uf.Modl.AskFaceData(face.Tag, out type, point, dir, box, out radius, out radData, out normDir);
            FaceKind kind = type == UfCylinder ? FaceKind.Cylinder : (type == UfPlane ? FaceKind.Plane : FaceKind.Other);
            return new FaceSample(kind, Vec3.FromArray(point), Vec3.FromArray(dir), radius);
        }

        public static List<FaceSample> SampleAll(UFSession uf, IList<Face> faces)
        {
            List<FaceSample> result = new List<FaceSample>();
            foreach (Face f in faces) result.Add(Sample(uf, f));
            return result;
        }

        public static Aabb Box(UFSession uf, NXObject obj)
        {
            double[] b = new double[6];
            uf.Modl.AskBoundingBox(obj.Tag, b);
            return Aabb.FromUfBox(b);
        }

        // Step 1 diagnostic: does the assembly-space point we got from NX match
        // the block-space point moved by the component's position? Tells us
        // whether NX gives occurrence data in assembly coordinates.
        public static string PositionCheck(Component comp, Vec3 blockPoint, Vec3 assyPoint, double tol)
        {
            Point3d o;
            Matrix3x3 m;
            comp.GetPosition(out o, out m);
            Vec3 p = blockPoint;
            Vec3 rows = new Vec3(
                o.X + p.X * m.Xx + p.Y * m.Yx + p.Z * m.Zx,
                o.Y + p.X * m.Xy + p.Y * m.Yy + p.Z * m.Zy,
                o.Z + p.X * m.Xz + p.Y * m.Yz + p.Z * m.Zz);
            Vec3 cols = new Vec3(
                o.X + p.X * m.Xx + p.Y * m.Xy + p.Z * m.Xz,
                o.Y + p.X * m.Yx + p.Y * m.Yy + p.Z * m.Yz,
                o.Z + p.X * m.Zx + p.Y * m.Zy + p.Z * m.Zz);
            bool r = (rows - assyPoint).Length <= tol;
            bool c = (cols - assyPoint).Length <= tol;
            if (r && c) return "matches the component position";
            if (r) return "matches the component position (row convention)";
            if (c) return "matches the component position (column convention)";
            return "does NOT match the component position: expected " + rows + " or " + cols;
        }
    }

    public sealed class ToolInfo
    {
        public Body ProtoBody;         // in the block part
        public Body OccBody;           // the same body as seen in the assembly
        public string Name;
        public string HoleType;
        public HoleCheck Check;
        public ToolCylinder BlockCyl;  // block part coordinates
        public ToolCylinder AssyCyl;   // assembly coordinates
        public bool OnToolLayer;
        public bool InToolRefSet;
        public bool InModelRefSet;
        public string PositionCheck;
        public List<string> Problems = new List<string>();
        public List<string> Notes = new List<string>();

        public bool Usable { get { return OccBody != null && AssyCyl != null && Check != null && Check.IsOk; } }
    }

    public sealed class BlockInfo
    {
        public Component Component;
        public Part Prototype;
        public string Label;       // "001-4032-CAD2072 rev A"
        public string Path;        // component path in the assembly
        public Aabb BlockBox;      // the block's own solid bodies, block part coordinates
        public List<ToolInfo> Tools = new List<ToolInfo>();
        public List<string> Problems = new List<string>();
    }

    public static class BlockReader
    {
        // Reads a selected component as a standard block. Returns false with a
        // plain-English reason if it isn't one.
        public static bool TryRead(NxContext ctx, Component comp, out BlockInfo block, out string reason)
        {
            SbConfig cfg = ctx.Config;
            block = null;
            reason = null;

            if (comp.IsSuppressed)
            {
                reason = "the component is suppressed";
                return false;
            }

            Part proto = PrototypeOf(comp);
            if (proto == null)
            {
                reason = "its part isn't loaded. Load the assembly fully and try again";
                return false;
            }
            if (!proto.IsFullyLoaded)
            {
                try
                {
                    PartLoadStatus status = proto.LoadThisPartFully();
                    status.Dispose();
                }
                catch (NXException ex)
                {
                    reason = "its part could not be fully loaded: " + ex.Message;
                    return false;
                }
            }

            string flag = NxAttr.GetString(proto, cfg.BlockAttribute);
            if (flag == null)
            {
                reason = NxAttr.HasAny(proto, cfg.BlockAttribute)
                    ? Text.F("part attribute {0} exists but isn't a String attribute", cfg.BlockAttribute)
                    : Text.F("it isn't a standard block (part attribute {0} is missing)", cfg.BlockAttribute);
                return false;
            }
            if (!string.Equals(flag.Trim(), cfg.BlockAttributeValue, StringComparison.OrdinalIgnoreCase))
            {
                reason = Text.F("part attribute {0} is \"{1}\", not \"{2}\"", cfg.BlockAttribute, flag, cfg.BlockAttributeValue);
                return false;
            }

            BlockInfo b = new BlockInfo();
            b.Component = comp;
            b.Prototype = proto;
            b.Label = NxNames.PartLabel(cfg, proto);
            b.Path = NxNames.ComponentPath(comp);

            if (proto.PartUnits != BasePart.Units.Millimeters)
                b.Problems.Add("the block part is not in millimetres; all sizes below will be wrong");

            bool toolRsFound, modelRsFound;
            Dictionary<Tag, bool> toolRs = ReferenceSetMembers(proto, cfg.ToolReferenceSet, out toolRsFound);
            Dictionary<Tag, bool> modelRs = ReferenceSetMembers(proto, cfg.ModelReferenceSet, out modelRsFound);
            if (!toolRsFound) b.Problems.Add(Text.F("the block part has no reference set called {0}", cfg.ToolReferenceSet));
            if (!modelRsFound) b.Problems.Add(Text.F("the block part has no reference set called {0}", cfg.ModelReferenceSet));

            // Sort the block's bodies into tool bodies and the block's own bodies.
            List<Body> toolBodies = new List<Body>();
            List<HoleTypeLookup> lookups = new List<HoleTypeLookup>();
            foreach (Body body in proto.Bodies)
            {
                if (!body.IsSolidBody) continue;
                HoleTypeLookup lookup = HoleTypeLookup.Find(body, cfg.HoleTypeAttribute);
                bool onLayer = body.Layer == cfg.ToolLayer;
                if (lookup.Value != null)
                {
                    toolBodies.Add(body);
                    lookups.Add(lookup);
                }
                else if (onLayer)
                {
                    List<string> seen = new List<string>();
                    seen.Add("body attributes: " + NxAttr.Describe(body));
                    foreach (NXOpen.Features.Feature f in HoleTypeLookup.OwningFeatures(body))
                        seen.Add("feature " + f.GetFeatureName() + " attributes: " + NxAttr.Describe(f));
                    b.Problems.Add(Text.F("body {0} is on layer {1} but has no {2} attribute on the body or its feature, so it was ignored. Found {3}",
                        NxNames.BodyName(body), cfg.ToolLayer, cfg.HoleTypeAttribute, string.Join("; ", seen.ToArray())));
                }
                else
                {
                    b.BlockBox = b.BlockBox == null ? NxGeom.Box(ctx.Uf, body) : b.BlockBox.Union(NxGeom.Box(ctx.Uf, body));
                }
            }

            if (b.BlockBox == null)
                b.Problems.Add("the block part has no solid body apart from its tool bodies, so the inside end of each tool body can't be worked out");
            if (toolBodies.Count == 0)
                b.Problems.Add(Text.F("the block has no tool bodies (solid bodies with a {0} attribute)", cfg.HoleTypeAttribute));

            for (int i = 0; i < toolBodies.Count; i++)
                b.Tools.Add(ReadTool(ctx, comp, b, toolBodies[i], lookups[i], toolRs, modelRs));

            block = b;
            return true;
        }

        private static ToolInfo ReadTool(NxContext ctx, Component comp, BlockInfo block, Body body, HoleTypeLookup lookup,
            Dictionary<Tag, bool> toolRs, Dictionary<Tag, bool> modelRs)
        {
            SbConfig cfg = ctx.Config;
            ToolInfo t = new ToolInfo();
            t.ProtoBody = body;
            t.Name = NxNames.BodyName(body);
            t.HoleType = lookup.Value;
            if (lookup.Source != "body") t.Notes.Add(Text.F("{0} was read from {1}, not from the body", cfg.HoleTypeAttribute, lookup.Source));
            if (lookup.Note != null) t.Notes.Add(Text.F("{0} found, but {1}", cfg.HoleTypeAttribute, lookup.Note));
            t.OnToolLayer = body.Layer == cfg.ToolLayer;
            t.InToolRefSet = toolRs.ContainsKey(body.Tag);
            t.InModelRefSet = modelRs.ContainsKey(body.Tag);

            if (!t.OnToolLayer) t.Problems.Add(Text.F("is on layer {0}, not layer {1}", body.Layer, cfg.ToolLayer));
            if (!t.InToolRefSet) t.Problems.Add(Text.F("is not in reference set {0}", cfg.ToolReferenceSet));
            if (!t.InModelRefSet) t.Problems.Add(Text.F("is not in reference set {0}, so it won't exist in assemblies", cfg.ModelReferenceSet));

            Face[] protoFaces = body.GetFaces();
            CylinderResult blockRes = ToolGeometry.Build(NxGeom.SampleAll(ctx.Uf, protoFaces), cfg.DistanceTolerance, cfg.AngleToleranceDeg);
            if (!blockRes.Ok)
            {
                t.Problems.AddRange(blockRes.Problems);
                t.Check = HoleRules.Check(cfg, t.HoleType, 0);
                return t;
            }

            t.BlockCyl = ToolGeometry.OrientFromBlock(blockRes.Cylinder, block.BlockBox, cfg.DistanceTolerance);
            if (!t.BlockCyl.DirectionKnown && block.BlockBox != null)
                t.Problems.Add("can't tell which end is inside the block (both ends or neither end lie inside the block's bounding box)");

            t.Check = HoleRules.Check(cfg, t.HoleType, t.BlockCyl.Diameter);
            if (!t.Check.IsOk) t.Problems.Add(t.Check.Message);
            else if (t.Check.Message != null) t.Notes.Add(t.Check.Message);

            string len = ToolGeometry.CheckLength(t.BlockCyl, cfg);
            if (len != null) t.Notes.Add(len);

            // The same body and faces as seen in the assembly.
            t.OccBody = comp.FindOccurrence(body) as Body;
            if (t.OccBody == null)
            {
                t.Problems.Add(Text.F("isn't visible in the assembly. The component uses reference set \"{0}\", which doesn't include it", comp.ReferenceSet));
                return t;
            }

            Face[] occFaces = new Face[protoFaces.Length];
            for (int i = 0; i < protoFaces.Length; i++)
            {
                occFaces[i] = comp.FindOccurrence(protoFaces[i]) as Face;
                if (occFaces[i] == null)
                {
                    t.Problems.Add("NX didn't return the assembly occurrence of one of its faces");
                    return t;
                }
            }

            CylinderResult assyRes = ToolGeometry.Build(NxGeom.SampleAll(ctx.Uf, occFaces), cfg.DistanceTolerance, cfg.AngleToleranceDeg);
            if (!assyRes.Ok)
            {
                foreach (string p in assyRes.Problems) t.Problems.Add("in the assembly it " + p);
                return t;
            }
            t.AssyCyl = ToolGeometry.OrientLike(assyRes.Cylinder, t.BlockCyl);
            t.PositionCheck = NxGeom.PositionCheck(comp, t.BlockCyl.Start, t.AssyCyl.Start, Math.Max(cfg.DistanceTolerance, 0.001));
            return t;
        }

        public static bool IsBlock(SbConfig cfg, Part part)
        {
            string flag = NxAttr.GetString(part, cfg.BlockAttribute);
            return flag != null && string.Equals(flag.Trim(), cfg.BlockAttributeValue, StringComparison.OrdinalIgnoreCase);
        }

        public static Part PrototypeOf(Component comp)
        {
            object p = comp.Prototype;
            Part part = p as Part;
            if (part == null)
            {
                NXObject o = p as NXObject;
                if (o != null) part = o.OwningPart as Part;
            }
            return part;
        }

        private static Dictionary<Tag, bool> ReferenceSetMembers(Part part, string name, out bool found)
        {
            Dictionary<Tag, bool> result = new Dictionary<Tag, bool>();
            found = false;
            foreach (ReferenceSet rs in part.GetAllReferenceSets())
            {
                if (!string.Equals(rs.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                foreach (NXObject m in rs.AskMembersInReferenceSet()) result[m.Tag] = true;
            }
            return result;
        }
    }

    public static class NxSelect
    {
        // Lets the user pick one or more components. Null if cancelled.
        public static List<Component> Components(NxContext ctx, string prompt)
        {
            Selection.MaskTriple[] mask = new Selection.MaskTriple[1];
            mask[0].Type = UFConstants.UF_component_type;
            mask[0].Subtype = UFConstants.UF_component_subtype;
            mask[0].SolidBodySubtype = 0;

            TaggedObject[] picked;
            Selection.Response resp = ctx.Ui.SelectionManager.SelectTaggedObjects(
                prompt, "Standard Blocks",
                Selection.SelectionScope.AnyInAssembly,
                Selection.SelectionAction.ClearAndEnableSpecific,
                false, false, mask, out picked);
            if (resp != Selection.Response.Ok || picked == null) return null;

            List<Component> result = new List<Component>();
            foreach (TaggedObject t in picked)
            {
                Component c = t as Component;
                if (c != null && !result.Contains(c)) result.Add(c);
            }
            return result;
        }
    }

    public static class NxCommon
    {
        // The displayed part, if it is an assembly; otherwise reports an error and returns null.
        public static Part RequireAssembly(NxContext ctx)
        {
            Report r = ctx.Report;
            Part display = ctx.Session.Parts.Display;
            if (display == null)
            {
                r.Error("No part is displayed. Open the jig assembly first.");
                return null;
            }
            if (display.ComponentAssembly == null || display.ComponentAssembly.RootComponent == null)
            {
                r.Error(Text.F("The displayed part {0} is not an assembly. Open the jig assembly first.", NxNames.PartLabel(ctx.Config, display)));
                return null;
            }
            r.Line("Assembly: " + NxNames.PartLabel(ctx.Config, display));
            if (display.PartUnits != BasePart.Units.Millimeters) r.Warn("The assembly is not in millimetres.");
            r.Blank();
            return display;
        }

        // Asks for block components and reads them. Null if cancelled.
        public static List<BlockInfo> SelectBlocks(NxContext ctx)
        {
            Report r = ctx.Report;
            List<Component> picked = NxSelect.Components(ctx, "Select standard block components, then OK");
            if (picked == null || picked.Count == 0)
            {
                r.Line("Cancelled: nothing was selected.");
                return null;
            }

            List<BlockInfo> blocks = new List<BlockInfo>();
            foreach (Component comp in picked)
            {
                BlockInfo block;
                string reason;
                if (BlockReader.TryRead(ctx, comp, out block, out reason)) blocks.Add(block);
                else r.Warn(Text.F("Skipped {0}: {1}.", NxNames.ComponentPath(comp), reason));
            }
            if (blocks.Count < picked.Count) r.Blank();
            return blocks;
        }
    }

    // Step 1: select blocks and list their tool bodies (read-only).
    public static class InspectCommand
    {
        public static void Run(NxContext ctx)
        {
            Report r = ctx.Report;
            r.Line("Command: Inspect standard blocks (read-only, changes nothing)");
            if (NxCommon.RequireAssembly(ctx) == null) return;

            List<BlockInfo> blocks = NxCommon.SelectBlocks(ctx);
            if (blocks == null) return;
            for (int i = 0; i < blocks.Count; i++) PrintBlock(ctx, blocks[i], i + 1);
            r.Line(Text.F("Done: {0} read.", Text.Count(blocks.Count, "standard block", "standard blocks")));
        }

        private static void PrintBlock(NxContext ctx, BlockInfo b, int number)
        {
            Report r = ctx.Report;
            r.Line(Text.F("Block {0}: {1}", number, b.Label));
            r.Indent();
            r.Line(Text.F("Component: {0} (reference set {1})", b.Path, b.Component.ReferenceSet));
            if (b.BlockBox != null) r.Line("Block body extent (block coordinates): " + b.BlockBox);
            foreach (string p in b.Problems) r.Warn(Text.F("{0}: {1}.", b.Label, p));

            List<string> types = new List<string>();
            foreach (ToolInfo t in b.Tools) if (t.Check != null && t.Check.Expected != null) types.Add(t.Check.Expected.Name);
            r.Line(Text.F("Tool bodies: {0} - {1}", b.Tools.Count, Summary.HoleCounts(types, ctx.Config)));

            int i = 0;
            foreach (ToolInfo t in b.Tools)
            {
                i++;
                string type = t.HoleType ?? "(no type)";
                if (t.Check != null && t.Check.Expected != null && t.Check.Expected.Name != t.HoleType)
                    type = Text.F("{0} ({1} = {2})", t.Check.Expected.Name, ctx.Config.HoleTypeAttribute, t.HoleType);
                r.Line(Text.F("[{0}] {1}  {2}", i, type, t.Name));
                r.Indent();
                if (t.BlockCyl != null)
                {
                    r.Line(Text.F("dia {0} {1}, length {2}",
                        Text.Num3(t.BlockCyl.Diameter), t.Check != null && t.Check.IsOk ? "OK" : "MISMATCH", Text.Num3(t.BlockCyl.Length)));
                    r.Line(Text.F("In block:    start {0}  direction {1}{2}",
                        t.BlockCyl.Start, t.BlockCyl.Direction, t.BlockCyl.DirectionKnown ? "  (start is inside the block)" : "  (direction unknown)"));
                }
                if (t.AssyCyl != null)
                {
                    r.Line(Text.F("In assembly: start {0}  direction {1}", t.AssyCyl.Start, t.AssyCyl.Direction));
                    r.Line("Assembly position check: " + t.PositionCheck);
                }
                r.Line(Text.F("Layer {0}; in {1}: {2}; in {3}: {4}",
                    t.ProtoBody.Layer, ctx.Config.ToolReferenceSet, t.InToolRefSet ? "yes" : "no",
                    ctx.Config.ModelReferenceSet, t.InModelRefSet ? "yes" : "no"));
                foreach (string n in t.Notes) r.Line("Note: " + n + ".");
                foreach (string p in t.Problems) r.Warn(Text.F("{0}, tool body {1} ({2}) {3}.", b.Label, t.Name, t.HoleType ?? "no type", p));
                r.Outdent();
            }
            r.Outdent();
            r.Blank();
        }
    }

    // A solid body in the assembly that isn't part of a standard block.
    public sealed class SolidRef
    {
        public Component Component;
        public Part Part;
        public Body ProtoBody;   // the same body in the plate part
        public Body Body;        // occurrence: assembly coordinates
        public Aabb Box;
    }

    public sealed class AssemblyIndex
    {
        public List<SolidRef> Solids = new List<SolidRef>();
        public Dictionary<Tag, int> PartUses = new Dictionary<Tag, int>();
        public List<string> NotLoaded = new List<string>();

        public static AssemblyIndex Build(NxContext ctx, Component root)
        {
            AssemblyIndex index = new AssemblyIndex();
            index.Walk(ctx, root);
            return index;
        }

        public int UsesOf(Part part)
        {
            int n;
            PartUses.TryGetValue(part.Tag, out n);
            return n;
        }

        private void Walk(NxContext ctx, Component parent)
        {
            SbConfig cfg = ctx.Config;
            foreach (Component c in parent.GetChildren())
            {
                if (c.IsSuppressed) continue;
                Part p = BlockReader.PrototypeOf(c);
                if (p == null)
                {
                    NotLoaded.Add(NxNames.ComponentPath(c));
                    continue;
                }
                if (!p.IsFullyLoaded)
                {
                    try { p.LoadThisPartFully().Dispose(); }
                    catch (NXException)
                    {
                        NotLoaded.Add(NxNames.ComponentPath(c));
                        continue;
                    }
                }
                PartUses[p.Tag] = UsesOf(p) + 1;

                if (!BlockReader.IsBlock(cfg, p))
                {
                    foreach (Body b in p.Bodies)
                    {
                        if (!b.IsSolidBody || b.Layer == cfg.ToolLayer) continue;
                        Body occ = c.FindOccurrence(b) as Body;
                        if (occ == null) continue;   // not in the component's reference set
                        SolidRef s = new SolidRef();
                        s.Component = c;
                        s.Part = p;
                        s.ProtoBody = b;
                        s.Body = occ;
                        s.Box = NxGeom.Box(ctx.Uf, occ);
                        Solids.Add(s);
                    }
                }
                Walk(ctx, c);
            }
        }
    }

    // A solid near a block, read as a plate if it is one.
    public sealed class PlateInfo
    {
        public SolidRef Solid;
        public PlateModel Model;          // null if it isn't a plate
        public string NotPlateReason;
        public List<FaceSample> Cylinders = new List<FaceSample>();   // existing holes etc.
    }

    public static class PlateReader
    {
        public static PlateInfo Read(NxContext ctx, SolidRef s, int uses)
        {
            SbConfig cfg = ctx.Config;
            PlateInfo info = new PlateInfo();
            info.Solid = s;

            List<PlanarFace> planar = new List<PlanarFace>();
            foreach (FaceSample f in NxGeom.SampleAll(ctx.Uf, s.Body.GetFaces()))
            {
                if (f.Kind == FaceKind.Plane) planar.Add(new PlanarFace(f.Direction, f.Point));
                else if (f.Kind == FaceKind.Cylinder) info.Cylinders.Add(f);
            }

            string problem;
            PlateFrame frame = PlateGeometry.FromPlanarFaces(planar, cfg.AngleToleranceDeg, out problem);
            if (frame == null)
            {
                info.NotPlateReason = problem;
                return info;
            }
            if (!PlateGeometry.IsPlateLike(frame, s.Box, cfg.MinPlateAspectRatio))
            {
                info.NotPlateReason = Text.F("is {0} mm thick and not much wider, so it isn't a plate", Text.Num(frame.Thickness));
                return info;
            }

            PlateModel m = new PlateModel();
            m.Id = s.Body.Tag.ToString();
            m.Frame = frame;
            m.Box = s.Box;
            m.Label = NxNames.PartLabel(cfg, s.Part);
            if (uses > 1) m.Label += " [" + NxNames.ComponentPath(s.Component) + "]";
            info.Model = m;
            return info;
        }

        // True if the tool body overlaps the plate body (touching doesn't count).
        public static bool Interferes(UFSession uf, Body plate, Body tool)
        {
            int[] results = new int[1];
            uf.Modl.CheckInterference(plate.Tag, 1, new Tag[] { tool.Tag }, results);
            return results[0] == 1;
        }
    }

    // Step 3: can every part the macro would change actually be changed?
    public static class NxTeamcenter
    {
        public static PartStatus Status(SbConfig cfg, Part part, string role)
        {
            PartStatus s = new PartStatus();
            s.Label = NxNames.PartLabel(cfg, part);
            s.Role = role;
            try
            {
                s.FullyLoaded = part.IsFullyLoaded;
                s.ReadOnly = part.IsReadOnly;
            }
            catch (NXException ex)
            {
                s.Error = ex.Message;
            }
            return s;
        }

        // Lists each part's state and returns the problems (empty = all writable).
        public static List<string> Report(NxContext ctx, Part assembly, IList<Part> plates)
        {
            Report r = ctx.Report;
            List<PartStatus> parts = new List<PartStatus>();
            parts.Add(Status(ctx.Config, assembly, "top assembly"));
            foreach (Part p in plates) parts.Add(Status(ctx.Config, p, "plate"));

            r.Line("Teamcenter check: parts that adding holes would change");
            r.Indent();
            foreach (PartStatus s in parts) r.Line(Text.F("{0} ({1}): {2}", s.Label, s.Role, s.Describe()));
            r.Outdent();

            List<string> problems = WriteCheck.Problems(parts);
            foreach (string p in problems) r.Warn(p + ".");
            if (problems.Count == 0) r.Line("All writable: adding holes would be allowed.");
            else r.Line("Adding holes would stop here, before changing anything.");
            r.Blank();
            return problems;
        }
    }

    // What detection found, for the commands that act on it.
    public sealed class DetectionResult
    {
        public Part Assembly;
        public AssemblyIndex Index;
        public List<PlatePlan> Plans = new List<PlatePlan>();
        public Dictionary<string, PlateInfo> Plates = new Dictionary<string, PlateInfo>();   // by PlateModel.Id
        public Dictionary<ToolEvaluation, ToolInfo> Tools = new Dictionary<ToolEvaluation, ToolInfo>();
        public Dictionary<ToolEvaluation, BlockInfo> Blocks = new Dictionary<ToolEvaluation, BlockInfo>();

        public List<Part> PlateParts()
        {
            List<Part> parts = new List<Part>();
            foreach (PlatePlan plan in Plans)
            {
                Part part = Plates[plan.Plate.Id].Solid.Part;
                if (!parts.Contains(part)) parts.Add(part);
            }
            return parts;
        }
    }

    // Step 2: find the plates each block's tool bodies would cut, and list them.
    public static class Detector
    {
        public static DetectionResult Run(NxContext ctx, Part display, List<BlockInfo> blocks)
        {
            Report r = ctx.Report;
            SbConfig cfg = ctx.Config;
            DetectionResult result = new DetectionResult();
            result.Assembly = display;

            AssemblyIndex index = AssemblyIndex.Build(ctx, display.ComponentAssembly.RootComponent);
            result.Index = index;
            r.Line(Text.F("Assembly scanned: {0} outside standard blocks.", Text.Count(index.Solids.Count, "solid body", "solid bodies")));
            if (index.NotLoaded.Count > 0)
                r.Warn(Text.F("{0} not loaded, so any plates in them were not checked: {1}.",
                    Text.Count(index.NotLoaded.Count, "component is", "components are"), string.Join(", ", index.NotLoaded.ToArray())));
            r.Blank();

            Dictionary<Tag, PlateInfo> cache = new Dictionary<Tag, PlateInfo>();
            Dictionary<string, PlateInfo> byId = result.Plates;
            List<ToolEvaluation> evals = new List<ToolEvaluation>();

            for (int bi = 0; bi < blocks.Count; bi++)
            {
                BlockInfo b = blocks[bi];
                r.Line(Text.F("Block {0}: {1}  ({2})", bi + 1, b.Label, b.Path));
                r.Indent();
                foreach (string p in b.Problems) r.Warn(Text.F("{0}: {1}.", b.Label, p));

                List<ToolInfo> tools = new List<ToolInfo>();
                foreach (ToolInfo t in b.Tools)
                {
                    if (t.Usable) tools.Add(t);
                    else r.Warn(Text.F("{0}, tool body {1} ({2}) can't be used: {3}.", b.Label, t.Name, t.HoleType ?? "no type",
                        t.Problems.Count > 0 ? t.Problems[0] : "its geometry could not be read"));
                }
                if (tools.Count == 0)
                {
                    r.Outdent();
                    r.Blank();
                    continue;
                }

                // Broad phase: solids whose boxes overlap the tool bodies' box.
                Aabb toolBox = null;
                foreach (ToolInfo t in tools) toolBox = NxGeom.Box(ctx.Uf, t.OccBody).Union(toolBox);
                List<PlateModel> plates = new List<PlateModel>();
                List<string> ignored = new List<string>();
                foreach (SolidRef s in index.Solids)
                {
                    if (!Detection.BoxesOverlap(toolBox, s.Box, cfg.BoundingBoxTolerance)) continue;
                    PlateInfo info;
                    if (!cache.TryGetValue(s.Body.Tag, out info))
                    {
                        info = PlateReader.Read(ctx, s, index.UsesOf(s.Part));
                        cache[s.Body.Tag] = info;
                        if (info.Model != null) byId[info.Model.Id] = info;
                    }
                    if (info.Model != null) plates.Add(info.Model);
                    else ignored.Add(NxNames.PartLabel(cfg, s.Part) + " " + info.NotPlateReason);
                }

                r.Line(Text.F("Near the block: {0}; {1} ignored.", Text.Count(plates.Count, "plate", "plates"),
                    Text.Count(ignored.Count, "other solid", "other solids")));
                r.Indent();
                foreach (PlateModel m in plates)
                    r.Line(Text.F("{0}: {1} mm thick, normal {2}", m.Label, Text.Num(m.Frame.Thickness), m.Frame.Normal));
                foreach (string g in ignored) r.Line("Ignored: " + g);
                r.Outdent();

                // Narrow phase, one tool body at a time.
                for (int ti = 0; ti < tools.Count; ti++)
                {
                    ToolInfo t = tools[ti];
                    Body toolBody = t.OccBody;
                    ToolEvaluation e = new ToolEvaluation();
                    e.BlockLabel = b.Label;
                    e.ToolName = t.Name;
                    e.HoleType = t.Check.Expected.Name;
                    e.Tool = t.AssyCyl;
                    e.Hits = Detection.EvaluateTool(t.AssyCyl, plates,
                        delegate(PlateModel m) { return PlateReader.Interferes(ctx.Uf, byId[m.Id].Solid.Body, toolBody); }, cfg);
                    evals.Add(e);
                    result.Tools[e] = t;
                    result.Blocks[e] = b;

                    ToolHit sel = e.Selected;
                    r.Line(Text.F("[{0}] {1}  {2}  ->  {3}", ti + 1, e.HoleType, t.Name, sel != null ? sel.Plate.Label : "no hole"));
                    r.Indent();
                    foreach (ToolHit h in e.Hits) r.Line(h.Plate.Label + ": " + Planner.Describe(h));
                    string problem = Planner.Problem(e, cfg);
                    if (problem != null) r.Warn(problem + ".");
                    if (sel != null)
                    {
                        PlateInfo pi = byId[sel.Plate.Id];
                        int k = ExistingHoles.FindCoaxial(t.AssyCyl, pi.Cylinders, cfg.DistanceTolerance, cfg.AngleToleranceDeg);
                        if (k >= 0)
                            r.Warn(Text.F("{0} already has a dia {1} hole where {2} goes. It will be cut anyway.",
                                sel.Plate.Label, Text.Num3(2 * pi.Cylinders[k].Radius), e.Describe()));
                    }
                    r.Outdent();
                }
                r.Outdent();
                r.Blank();
            }

            result.Plans = Planner.ByPlate(evals);
            r.Line("Plates to cut:");
            r.Indent();
            if (result.Plans.Count == 0) r.Line("none");
            foreach (PlatePlan plan in result.Plans) r.Line(plan.Summary(cfg));
            r.Outdent();
            foreach (Part part in result.PlateParts())
            {
                int uses = index.UsesOf(part);
                if (uses > 1)
                    r.Warn(Text.F("{0} is used {1} times in this assembly. Holes are cut into the part, so every copy will get them.",
                        NxNames.PartLabel(cfg, part), uses));
            }
            r.Blank();
            return result;
        }
    }

    // Read-only: detection, the Teamcenter check, then highlight the plates.
    public static class DetectCommand
    {
        public static void Run(NxContext ctx)
        {
            ctx.Report.Line("Command: Detect target plates (read-only, changes nothing)");
            Part display = NxCommon.RequireAssembly(ctx);
            if (display == null) return;
            List<BlockInfo> blocks = NxCommon.SelectBlocks(ctx);
            if (blocks == null || blocks.Count == 0) return;

            DetectionResult det = Detector.Run(ctx, display, blocks);
            if (det.Plans.Count == 0) return;
            NxTeamcenter.Report(ctx, display, det.PlateParts());
            Highlight(ctx, det);
        }

        // Shows the plates that would be cut until the user presses OK.
        private static void Highlight(NxContext ctx, DetectionResult det)
        {
            List<Body> bodies = new List<Body>();
            foreach (PlatePlan plan in det.Plans) bodies.Add(det.Plates[plan.Plate.Id].Solid.Body);
            foreach (Body b in bodies) b.Highlight();
            try
            {
                ctx.Ui.NXMessageBox.Show("Standard Blocks", NXMessageBox.DialogType.Information,
                    Text.F("{0} highlighted. Details are in the Listing window.\n\nOK to clear the highlight.",
                        Text.Count(bodies.Count, "plate to cut is", "plates to cut are")));
            }
            finally
            {
                foreach (Body b in bodies) b.Unhighlight();
            }
        }
    }

    // A failure while changing the model, already worded for the user.
    public sealed class SbException : Exception
    {
        public SbException(string message) : base(message) { }
    }

    // Attribute titles written by the macro (prefix from features.prefix).
    public sealed class SbAttrs
    {
        public readonly string InstanceId;     // on the block component
        public readonly string Role;           // LINK or SUBTRACT
        public readonly string Group;          // ties a block's links to their subtract on one plate
        public readonly string SourcePart;
        public readonly string SourceRev;
        public readonly string SourceInstance;
        public readonly string SourcePath;
        public readonly string SourceBody;
        public readonly string HoleType;
        public readonly string HoleDiameter;
        public readonly string Assembly;
        public readonly string Version;

        public SbAttrs(string prefix)
        {
            InstanceId = prefix + "INSTANCE_ID";
            Role = prefix + "ROLE";
            Group = prefix + "GROUP";
            SourcePart = prefix + "SOURCE_PART";
            SourceRev = prefix + "SOURCE_REV";
            SourceInstance = prefix + "SOURCE_INSTANCE";
            SourcePath = prefix + "SOURCE_PATH";
            SourceBody = prefix + "SOURCE_BODY";
            HoleType = prefix + "HOLE_TYPE";
            HoleDiameter = prefix + "HOLE_DIA";
            Assembly = prefix + "ASSEMBLY";
            Version = prefix + "VERSION";
        }
    }

    // Step 4: WAVE link the tool bodies into each plate and subtract them.
    public static class AddHolesCommand
    {
        public static void Run(NxContext ctx)
        {
            Report r = ctx.Report;
            SbConfig cfg = ctx.Config;
            r.Line("Command: Add standard block holes");
            Part display = NxCommon.RequireAssembly(ctx);
            if (display == null) return;
            List<BlockInfo> blocks = NxCommon.SelectBlocks(ctx);
            if (blocks == null || blocks.Count == 0) return;

            DetectionResult det = Detector.Run(ctx, display, blocks);
            if (det.Plans.Count == 0)
            {
                r.Line("Nothing to add.");
                return;
            }

            // Leave out plates this block copy has already cut.
            SbAttrs attrs = new SbAttrs(cfg.FeaturePrefix);
            List<PlatePlan> plans = new List<PlatePlan>();
            foreach (PlatePlan plan in det.Plans)
            {
                PlatePlan todo = new PlatePlan();
                todo.Plate = plan.Plate;
                Part platePart = det.Plates[plan.Plate.Id].Solid.Part;
                foreach (ToolEvaluation e in plan.Tools)
                {
                    string id = NxAttr.GetString(det.Blocks[e].Component, attrs.InstanceId);
                    if (id != null && HoleBuilder.HasFeaturesFrom(platePart, attrs, id))
                        r.Line(Text.F("Skipped: {0} already has holes from this copy of {1}.", plan.Plate.Label, e.BlockLabel));
                    else todo.Tools.Add(e);
                }
                if (todo.Tools.Count > 0) plans.Add(todo);
            }
            if (plans.Count == 0)
            {
                r.Line("Nothing to add: every plate already has its holes.");
                return;
            }

            List<string> problems = NxTeamcenter.Report(ctx, display, det.PlateParts());
            if (problems.Count > 0)
            {
                r.Error("Nothing was changed, because not every part can be changed. Fix the problems above and run it again.");
                return;
            }

            List<PlatePlan> chosen = Confirm(ctx, det, plans);
            if (chosen.Count == 0)
            {
                r.Line("Nothing chosen: nothing was changed.");
                return;
            }

            Session.UndoMarkId mark = ctx.Session.SetUndoMark(Session.MarkVisibility.Visible, "Add Standard Block Holes");
            Component originalWork = ctx.Session.Parts.WorkComponent;
            bool ok = false;
            try
            {
                foreach (BlockInfo b in BlocksIn(det, chosen)) EnsureInstanceId(b, attrs);
                foreach (PlatePlan plan in chosen) HoleBuilder.AddToPlate(ctx, det, plan, attrs);
                ok = true;
            }
            catch (Exception ex)
            {
                r.Error(ex.Message);
                try
                {
                    ctx.Session.UndoToMark(mark, null);
                    r.Line("Everything this run did has been undone. Nothing was changed.");
                }
                catch (Exception undoEx)
                {
                    r.Error("Undoing it also failed (" + undoEx.Message + "). Use Edit > Undo to go back to \"Add Standard Block Holes\".");
                }
            }
            finally
            {
                try
                {
                    PartLoadStatus status;
                    ctx.Session.Parts.SetWorkComponent(originalWork, PartCollection.RefsetOption.Current,
                        PartCollection.WorkComponentOption.Visible, out status);
                    status.Dispose();
                }
                catch (Exception ex)
                {
                    r.Warn("Could not restore the work part: " + ex.Message + ".");
                }
            }

            if (ok)
            {
                r.Blank();
                r.Line("Done. Edit > Undo \"Add Standard Block Holes\" removes everything this run added.");
                r.Line("To check the holes follow the block: move the block, then update (Tools > Update > Interpart Update if they don't follow straight away).");
            }
        }

        // One Yes/No per plate, with the plate highlighted.
        private static List<PlatePlan> Confirm(NxContext ctx, DetectionResult det, List<PlatePlan> plans)
        {
            Report r = ctx.Report;
            List<PlatePlan> chosen = new List<PlatePlan>();
            r.Line("Your choices:");
            r.Indent();
            foreach (PlatePlan plan in plans)
            {
                Body body = det.Plates[plan.Plate.Id].Solid.Body;
                body.Highlight();
                int answer;
                try
                {
                    answer = ctx.Ui.NXMessageBox.Show("Standard Blocks", NXMessageBox.DialogType.Question,
                        Text.F("Add {0} to the highlighted plate?\n\n{1}", Summary.HoleCounts(plan.HoleTypes(), ctx.Config), plan.Plate.Label));
                }
                finally
                {
                    body.Unhighlight();
                }
                bool yes = answer == 1;
                r.Line(Text.F("{0}: {1} (answer {2})", plan.Summary(ctx.Config), yes ? "add" : "skip", answer));
                if (yes) chosen.Add(plan);
            }
            r.Outdent();
            r.Blank();
            return chosen;
        }

        private static List<BlockInfo> BlocksIn(DetectionResult det, List<PlatePlan> plans)
        {
            List<BlockInfo> blocks = new List<BlockInfo>();
            foreach (PlatePlan plan in plans)
                foreach (ToolEvaluation e in plan.Tools)
                    if (!blocks.Contains(det.Blocks[e])) blocks.Add(det.Blocks[e]);
            return blocks;
        }

        // Gives the block component a permanent ID so Sync can find its holes.
        private static void EnsureInstanceId(BlockInfo b, SbAttrs attrs)
        {
            if (NxAttr.GetString(b.Component, attrs.InstanceId) != null) return;
            try
            {
                b.Component.SetUserAttribute(attrs.InstanceId, -1, Guid.NewGuid().ToString("N"), Update.Option.Now);
            }
            catch (NXException ex)
            {
                throw new SbException(Text.F("Couldn't give {0} its {1} attribute ({2}). The assembly that holds it must be writable.",
                    b.Path, attrs.InstanceId, ex.Message));
            }
        }
    }

    public static class HoleBuilder
    {
        public static bool HasFeaturesFrom(Part plate, SbAttrs attrs, string instanceId)
        {
            foreach (NXOpen.Features.Feature f in plate.Features)
            {
                if (NxAttr.GetString(f, attrs.SourceInstance) == instanceId) return true;
            }
            return false;
        }

        // Instance IDs of copies of blockPart that already cut this plate, in feature order.
        private static List<string> CopiesOf(Part plate, SbAttrs attrs, string blockPart)
        {
            List<string> ids = new List<string>();
            foreach (NXOpen.Features.Feature f in plate.Features)
            {
                if (NxAttr.GetString(f, attrs.SourcePart) != blockPart) continue;
                string id = NxAttr.GetString(f, attrs.SourceInstance);
                if (id != null && !ids.Contains(id)) ids.Add(id);
            }
            return ids;
        }

        public static void AddToPlate(NxContext ctx, DetectionResult det, PlatePlan plan, SbAttrs attrs)
        {
            Report r = ctx.Report;
            SbConfig cfg = ctx.Config;
            PlateInfo plate = det.Plates[plan.Plate.Id];
            string plateLabel = plan.Plate.Label;
            string assemblyLabel = NxNames.PartLabel(cfg, det.Assembly);

            PartLoadStatus status;
            try
            {
                ctx.Session.Parts.SetWorkComponent(plate.Solid.Component, PartCollection.RefsetOption.Current,
                    PartCollection.WorkComponentOption.Visible, out status);
                status.Dispose();
            }
            catch (NXException ex)
            {
                throw new SbException(Text.F("Couldn't make {0} the work part: {1}.", plateLabel, ex.Message));
            }
            Part work = ctx.Session.Parts.Work;

            r.Line("Adding to " + plateLabel + ":");
            r.Indent();

            // One group per block copy and hole type: its links, then one subtract.
            List<string> keys = new List<string>();
            Dictionary<string, List<ToolEvaluation>> groups = new Dictionary<string, List<ToolEvaluation>>();
            foreach (ToolEvaluation e in plan.Tools)
            {
                string key = NxAttr.GetString(det.Blocks[e].Component, attrs.InstanceId) + "|" + e.HoleType;
                if (!groups.ContainsKey(key))
                {
                    keys.Add(key);
                    groups[key] = new List<ToolEvaluation>();
                }
                groups[key].Add(e);
            }

            foreach (string key in keys)
            {
                List<ToolEvaluation> group = groups[key];
                BlockInfo block = det.Blocks[group[0]];
                string blockPart = NxAttr.GetString(block.Prototype, cfg.PartNumberAttribute) ?? block.Prototype.Leaf;
                string blockRev = NxAttr.GetString(block.Prototype, cfg.PartRevisionAttribute) ?? "";
                string instance = NxAttr.GetString(block.Component, attrs.InstanceId);
                string holeType = group[0].HoleType;
                string groupId = Guid.NewGuid().ToString("N");

                List<string> copies = CopiesOf(work, attrs, blockPart);
                int copy = FeatureNames.CopyNumber(copies, instance);
                string subtractName = FeatureNames.Subtract(cfg.FeaturePrefix, blockPart, holeType, copy);
                string linkName = FeatureNames.Link(cfg.FeaturePrefix, blockPart, holeType, copy);

                List<NXOpen.Features.Feature> made = new List<NXOpen.Features.Feature>();
                List<Body> linkedBodies = new List<Body>();
                foreach (ToolEvaluation e in group)
                {
                    ToolInfo tool = det.Tools[e];
                    NXOpen.Features.Feature link;
                    try
                    {
                        link = CreateLink(work, tool.OccBody);
                        foreach (Body lb in ((NXOpen.Features.BodyFeature)link).GetBodies()) linkedBodies.Add(lb);
                    }
                    catch (NXException ex)
                    {
                        throw new SbException(Text.F("Creating the WAVE link for {0} in {1} failed: {2}.", e.Describe(), plateLabel, ex.Message));
                    }
                    link.SetName(linkName);
                    SetCommon(link, attrs, "LINK", groupId, blockPart, blockRev, instance, block.Path, holeType, tool, assemblyLabel);
                    NxAttr.Set(link, attrs.SourceBody, tool.ProtoBody.JournalIdentifier);
                    made.Add(link);
                }
                if (linkedBodies.Count == 0)
                    throw new SbException(Text.F("The WAVE links for {0} {1} in {2} produced no bodies.", block.Label, holeType, plateLabel));

                NXOpen.Features.Feature subtract;
                try
                {
                    subtract = CreateSubtract(work, plate.Solid.ProtoBody, linkedBodies);
                }
                catch (NXException ex)
                {
                    throw new SbException(Text.F("Subtracting the {0} holes of {1} from {2} failed: {3}.", holeType, block.Label, plateLabel, ex.Message));
                }
                subtract.SetName(subtractName);
                SetCommon(subtract, attrs, "SUBTRACT", groupId, blockPart, blockRev, instance, block.Path, holeType, det.Tools[group[0]], assemblyLabel);

                r.Line(Text.F("{0}: {1} + subtract {2}", Text.Count(group.Count, "link", "links"), linkName, subtractName));
            }
            r.Outdent();
        }

        private static void SetCommon(NXOpen.Features.Feature f, SbAttrs a, string role, string groupId, string blockPart, string blockRev,
            string instance, string path, string holeType, ToolInfo tool, string assemblyLabel)
        {
            NxAttr.Set(f, a.Role, role);
            NxAttr.Set(f, a.Group, groupId);
            NxAttr.Set(f, a.SourcePart, blockPart);
            NxAttr.Set(f, a.SourceRev, blockRev);
            NxAttr.Set(f, a.SourceInstance, instance);
            NxAttr.Set(f, a.SourcePath, path);
            NxAttr.Set(f, a.HoleType, holeType);
            NxAttr.Set(f, a.HoleDiameter, Text.Num(tool.Check.Expected.Diameter));
            NxAttr.Set(f, a.Assembly, assemblyLabel);
            NxAttr.Set(f, a.Version, StandardBlocksJournal.Version);
        }

        // WAVE Geometry Linker, body link, associative, not fixed at timestamp.
        private static NXOpen.Features.Feature CreateLink(Part work, Body toolOccurrence)
        {
            NXOpen.Features.WaveLinkBuilder builder = work.BaseFeatures.CreateWaveLinkBuilder(null);
            try
            {
                builder.Type = NXOpen.Features.WaveLinkBuilder.Types.BodyLink;
                NXOpen.Features.ExtractFaceBuilder extract = builder.ExtractFaceBuilder;
                extract.ParentPart = NXOpen.Features.ExtractFaceBuilder.ParentPartType.OtherPart;
                extract.Associative = true;
                extract.FixAtCurrentTimestamp = false;
                extract.HideOriginal = false;
                extract.InheritDisplayProperties = false;

                ScCollector collector = work.ScCollectors.CreateCollector();
                SelectionIntentRule[] rules = new SelectionIntentRule[] { work.ScRuleFactory.CreateRuleBodyDumb(new Body[] { toolOccurrence }, true) };
                collector.ReplaceRules(rules, false);
                extract.ExtractBodyCollector = collector;

                return (NXOpen.Features.Feature)builder.Commit();
            }
            finally
            {
                builder.Destroy();
            }
        }

        // Subtract, consuming the linked bodies.
        private static NXOpen.Features.Feature CreateSubtract(Part work, Body plate, List<Body> tools)
        {
            NXOpen.Features.BooleanBuilder builder = work.Features.CreateBooleanBuilderUsingCollector(null);
            try
            {
                builder.Operation = NXOpen.Features.Feature.BooleanType.Subtract;
                builder.RetainTool = false;

                ScCollector target = work.ScCollectors.CreateCollector();
                target.ReplaceRules(new SelectionIntentRule[] { work.ScRuleFactory.CreateRuleBodyDumb(new Body[] { plate }, true) }, false);
                builder.TargetBodyCollector = target;

                ScCollector tool = work.ScCollectors.CreateCollector();
                tool.ReplaceRules(new SelectionIntentRule[] { work.ScRuleFactory.CreateRuleBodyDumb(tools.ToArray(), true) }, false);
                builder.ToolBodyCollector = tool;

                return (NXOpen.Features.Feature)builder.Commit();
            }
            finally
            {
                builder.Destroy();
            }
        }
    }
}

// Journal entry point. Tools > Journal > Play runs Main. Copies of this file
// named SB_<command>.cs run that command (see Commands.FromJournalName).
public class StandardBlocksJournal
{
    public const string ConfigFileName = "StandardBlocks.config.json";

    // Printed in the header so a run shows which build it is. Bump on every change.
    public const string Version = "0.4.0 (step 4: add holes)";

    public static void Main(string[] args)
    {
        string journal = null;
        try { journal = NXOpen.Session.GetSession().ExecutingJournal; }
        catch (Exception) { }
        Run(StandardBlocks.Core.Commands.Resolve(args, journal));
    }

    public static void Run(string command)
    {
        command = StandardBlocks.Core.Commands.Normalize(command);
        NXOpen.Session session = NXOpen.Session.GetSession();
        NXOpen.ListingWindow lw = session.ListingWindow;
        lw.Open();

        StandardBlocks.Core.Report report = new StandardBlocks.Core.Report();
        report.AddSink(delegate(string line) { lw.WriteLine(line); }, false);

        StreamWriter log = null;
        string logPath = null;
        try
        {
            report.Line("=== Standard Blocks " + Version + " ===");
            report.Line(StandardBlocks.Core.Text.F("{0:yyyy-MM-dd HH:mm:ss}, user {1}, .NET {2}",
                DateTime.Now, Environment.UserName, Environment.Version));

            StandardBlocks.Nx.NxContext ctx = new StandardBlocks.Nx.NxContext();
            ctx.Session = session;
            ctx.Uf = NXOpen.UF.UFSession.GetUFSession();
            ctx.Ui = NXOpen.UI.GetUI();
            ctx.Report = report;

            ctx.ConfigPath = FindConfig(session);
            if (ctx.ConfigPath == null)
            {
                report.Error("Can't find " + ConfigFileName + ". Put it in the same folder as StandardBlocks.cs, or set the SB_CONFIG environment variable to its full path.");
                return;
            }
            report.Line("Config: " + ctx.ConfigPath);
            try
            {
                ctx.Config = StandardBlocks.Core.SbConfig.Parse(File.ReadAllText(ctx.ConfigPath));
            }
            catch (StandardBlocks.Core.ConfigException ex)
            {
                report.Error("Problem in " + ctx.ConfigPath + ": " + ex.Message);
                return;
            }
            foreach (string w in ctx.Config.Warnings) report.Warn("Config: " + w);
            report.Line(ctx.Config.Describe());

            logPath = OpenLog(ctx, command, out log);
            if (log != null)
            {
                StreamWriter writer = log;
                report.AddSink(delegate(string line) { writer.WriteLine(line); }, true);
            }
            report.Blank();

            switch (command)
            {
                case "inspect":
                    StandardBlocks.Nx.InspectCommand.Run(ctx);
                    break;
                case "detect":
                    StandardBlocks.Nx.DetectCommand.Run(ctx);
                    break;
                case "addholes":
                    StandardBlocks.Nx.AddHolesCommand.Run(ctx);
                    break;
                default:
                    report.Error("Unknown command \"" + command + "\". Available: inspect, detect, addholes.");
                    break;
            }
        }
        catch (Exception ex)
        {
            report.Error("Unexpected error: " + ex.Message);
            report.Line(ex.ToString());
        }
        finally
        {
            report.Line(StandardBlocks.Core.Text.F("Finished with {0}, {1}.",
                StandardBlocks.Core.Text.Count(report.ErrorCount, "error", "errors"),
                StandardBlocks.Core.Text.Count(report.WarningCount, "warning", "warnings")));
            if (logPath != null) report.Line("Log: " + logPath);
            if (log != null) log.Dispose();
        }

        if (report.ErrorCount > 0)
        {
            NXOpen.UI.GetUI().NXMessageBox.Show("Standard Blocks", NXOpen.NXMessageBox.DialogType.Error,
                StandardBlocks.Core.Text.Count(report.ErrorCount, "error", "errors") + ". See the Listing window for details.");
        }
    }

    // SB_CONFIG overrides; otherwise the config sits next to the running journal.
    private static string FindConfig(NXOpen.Session session)
    {
        string env = Environment.GetEnvironmentVariable("SB_CONFIG");
        if (!string.IsNullOrEmpty(env)) return File.Exists(env) ? env : null;

        string journal = null;
        try { journal = session.ExecutingJournal; }
        catch (Exception) { }
        if (string.IsNullOrEmpty(journal)) return null;

        string path = Path.Combine(Path.GetDirectoryName(journal), ConfigFileName);
        return File.Exists(path) ? path : null;
    }

    private static string OpenLog(StandardBlocks.Nx.NxContext ctx, string command, out StreamWriter log)
    {
        log = null;
        string folder = ctx.Config.LogFolder;
        if (string.IsNullOrEmpty(folder)) return null;
        if (!Path.IsPathRooted(folder)) folder = Path.Combine(Path.GetDirectoryName(ctx.ConfigPath), folder);
        try
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, StandardBlocks.Core.Text.F("StandardBlocks_{0:yyyyMMdd_HHmmss}_{1}.log", DateTime.Now, command));
            log = new StreamWriter(path, false, new UTF8Encoding(false));
            log.AutoFlush = true;
            return path;
        }
        catch (Exception ex)
        {
            ctx.Report.Warn("Could not create a log file in " + folder + ": " + ex.Message + ". Carrying on without one.");
            return null;
        }
    }

    public static int GetUnloadOption(string dummy)
    {
        return (int)NXOpen.Session.LibraryUnloadOption.Immediately;
    }
}
#endif
