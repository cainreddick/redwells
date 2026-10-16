// Minimal JSON reader/writer. NX journals can only reference a fixed set of
// assemblies, so we don't rely on System.Web / System.Runtime.Serialization.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>JSON object that keeps key order (so dumps stay readable and diffable).</summary>
    public sealed class JObj
    {
        public readonly List<KeyValuePair<string, object>> Items = new List<KeyValuePair<string, object>>();

        public JObj Add(string key, object value)
        {
            Items.Add(new KeyValuePair<string, object>(key, value));
            return this;
        }

        public bool Has(string key)
        {
            foreach (var kv in Items) if (kv.Key == key) return true;
            return false;
        }

        public object Get(string key)
        {
            foreach (var kv in Items) if (kv.Key == key) return kv.Value;
            return null;
        }

        public string Str(string key, string def)
        {
            object v = Get(key);
            if (v == null) return def;
            if (v is string) return (string)v;
            if (v is double) return ((double)v).ToString("R", CultureInfo.InvariantCulture);
            if (v is bool) return (bool)v ? "true" : "false";
            return def;
        }

        public double Num(string key, double def)
        {
            object v = Get(key);
            if (v is double) return (double)v;
            double d;
            if (v is string && double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return def;
        }

        public bool Bool(string key, bool def)
        {
            object v = Get(key);
            if (v is bool) return (bool)v;
            return def;
        }

        public JObj Obj(string key) { return Get(key) as JObj; }
        public List<object> Arr(string key) { return Get(key) as List<object>; }
    }

    public static class Json
    {
        // ---------------- writer ----------------

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        static void Indent(StringBuilder sb, int level) { sb.Append(' ', level * 2); }

        static bool IsScalarArray(List<object> list)
        {
            foreach (object o in list)
                if (o is JObj || o is List<object>) return false;
            return true;
        }

        static void WriteValue(StringBuilder sb, object v, int level)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double || v is float || v is int || v is long)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var obj = v as JObj;
            if (obj != null)
            {
                if (obj.Items.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                for (int i = 0; i < obj.Items.Count; i++)
                {
                    Indent(sb, level + 1);
                    WriteString(sb, obj.Items[i].Key);
                    sb.Append(": ");
                    WriteValue(sb, obj.Items[i].Value, level + 1);
                    if (i < obj.Items.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                Indent(sb, level);
                sb.Append('}');
                return;
            }
            var list = v as List<object>;
            if (list != null)
            {
                if (list.Count == 0) { sb.Append("[]"); return; }
                if (IsScalarArray(list))
                {
                    sb.Append('[');
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        WriteValue(sb, list[i], level + 1);
                    }
                    sb.Append(']');
                    return;
                }
                sb.Append("[\n");
                for (int i = 0; i < list.Count; i++)
                {
                    Indent(sb, level + 1);
                    WriteValue(sb, list[i], level + 1);
                    if (i < list.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                Indent(sb, level);
                sb.Append(']');
                return;
            }
            throw new ArgumentException("Unsupported JSON value type: " + v.GetType().Name);
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------------- reader ----------------

        /// <summary>Parses JSON. Returns JObj, List&lt;object&gt;, string, double, bool or null.
        /// Allows // line comments so the config file can be annotated.</summary>
        public static object Parse(string text)
        {
            int pos = 0;
            object v = ParseValue(text, ref pos);
            SkipWs(text, ref pos);
            if (pos < text.Length) throw Error(text, pos, "Unexpected trailing characters");
            return v;
        }

        static FormatException Error(string t, int pos, string msg)
        {
            int line = 1, col = 1;
            for (int i = 0; i < pos && i < t.Length; i++)
            {
                if (t[i] == '\n') { line++; col = 1; } else col++;
            }
            return new FormatException("JSON error at line " + line + ", column " + col + ": " + msg);
        }

        static void SkipWs(string t, ref int pos)
        {
            while (pos < t.Length)
            {
                char c = t[pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\uFEFF') { pos++; continue; }
                if (c == '/' && pos + 1 < t.Length && t[pos + 1] == '/')
                {
                    while (pos < t.Length && t[pos] != '\n') pos++;
                    continue;
                }
                break;
            }
        }

        static object ParseValue(string t, ref int pos)
        {
            SkipWs(t, ref pos);
            if (pos >= t.Length) throw Error(t, pos, "Unexpected end of input");
            char c = t[pos];
            if (c == '{') return ParseObject(t, ref pos);
            if (c == '[') return ParseArray(t, ref pos);
            if (c == '"') return ParseString(t, ref pos);
            if (Match(t, ref pos, "true")) return true;
            if (Match(t, ref pos, "false")) return false;
            if (Match(t, ref pos, "null")) return null;
            return ParseNumber(t, ref pos);
        }

        static bool Match(string t, ref int pos, string word)
        {
            if (string.CompareOrdinal(t, pos, word, 0, word.Length) == 0)
            {
                pos += word.Length;
                return true;
            }
            return false;
        }

        static JObj ParseObject(string t, ref int pos)
        {
            var obj = new JObj();
            pos++; // {
            SkipWs(t, ref pos);
            if (pos < t.Length && t[pos] == '}') { pos++; return obj; }
            while (true)
            {
                SkipWs(t, ref pos);
                if (pos >= t.Length || t[pos] != '"') throw Error(t, pos, "Expected property name in quotes");
                string key = ParseString(t, ref pos);
                SkipWs(t, ref pos);
                if (pos >= t.Length || t[pos] != ':') throw Error(t, pos, "Expected ':' after property name");
                pos++;
                obj.Add(key, ParseValue(t, ref pos));
                SkipWs(t, ref pos);
                if (pos < t.Length && t[pos] == ',')
                {
                    pos++;
                    SkipWs(t, ref pos);
                    if (pos < t.Length && t[pos] == '}') { pos++; return obj; } // tolerate trailing comma
                    continue;
                }
                if (pos < t.Length && t[pos] == '}') { pos++; return obj; }
                throw Error(t, pos, "Expected ',' or '}'");
            }
        }

        static List<object> ParseArray(string t, ref int pos)
        {
            var list = new List<object>();
            pos++; // [
            SkipWs(t, ref pos);
            if (pos < t.Length && t[pos] == ']') { pos++; return list; }
            while (true)
            {
                list.Add(ParseValue(t, ref pos));
                SkipWs(t, ref pos);
                if (pos < t.Length && t[pos] == ',')
                {
                    pos++;
                    SkipWs(t, ref pos);
                    if (pos < t.Length && t[pos] == ']') { pos++; return list; }
                    continue;
                }
                if (pos < t.Length && t[pos] == ']') { pos++; return list; }
                throw Error(t, pos, "Expected ',' or ']'");
            }
        }

        static string ParseString(string t, ref int pos)
        {
            var sb = new StringBuilder();
            pos++; // opening quote
            while (pos < t.Length)
            {
                char c = t[pos++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (pos >= t.Length) break;
                char e = t[pos++];
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
                        if (pos + 4 > t.Length) throw Error(t, pos, "Bad \\u escape");
                        sb.Append((char)int.Parse(t.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        pos += 4;
                        break;
                    default:
                        // Be forgiving with Windows paths typed as "C:\Laser DXF\" - keep the backslash.
                        sb.Append('\\').Append(e);
                        break;
                }
            }
            throw Error(t, pos, "Unterminated string");
        }

        static double ParseNumber(string t, ref int pos)
        {
            int start = pos;
            while (pos < t.Length && "+-0123456789.eE".IndexOf(t[pos]) >= 0) pos++;
            double d;
            if (pos == start || !double.TryParse(t.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw Error(t, start, "Invalid value");
            return d;
        }
    }
}
