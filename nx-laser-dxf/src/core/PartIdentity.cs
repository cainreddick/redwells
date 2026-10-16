// Part number / revision from Teamcenter attributes, and {TOKEN} templates
// for the etch text and the DXF file name.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    public sealed class PartIdentity
    {
        public string Id = "";
        public string IdSource = "";
        public string RevisionRaw = "";
        public string Revision = "";
        public string Name = "";
        public string Material = "";
        public string Thickness = "";
        public string DrawnBy = "";
        public List<string> Warnings = new List<string>();
        /// <summary>True when the ID or revision attribute could not be found.</summary>
        public bool AttributesMissing;

        public Dictionary<string, string> Tokens()
        {
            var t = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            t["ID"] = Id;
            t["REV"] = Revision;
            t["REVRAW"] = RevisionRaw;
            t["NAME"] = Name;
            t["MATERIAL"] = Material;
            t["THK"] = Thickness;
            t["DRAWNBY"] = DrawnBy;
            return t;
        }
    }

    public static class Identity
    {
        /// <summary>Finds an attribute by title-or-alias (case-insensitive) in the configured category.
        /// If it isn't in that category, any category is accepted and a note says where it was found.
        /// Empty and ignored values count as missing.</summary>
        public static string Find(List<AttributeEntry> attrs, AttrRef r, List<string> notes)
        {
            if (r == null || r.Title.Trim().Length == 0) return null;
            bool anyCategory = r.Category.Trim().Length == 0;
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1 && anyCategory) break;
                foreach (var a in attrs)
                {
                    if (pass == 0 && !anyCategory && !Same(a.Category, r.Category)) continue;
                    if (!Same(a.Title, r.Title) && !Same(a.Alias, r.Title)) continue;
                    string v = (a.Value ?? "").Trim();
                    if (v.Length == 0) continue;
                    bool ignored = false;
                    foreach (string ig in r.IgnoreValues) if (Same(v, ig)) ignored = true;
                    if (ignored) continue;
                    if (pass == 1 && notes != null)
                        notes.Add("Attribute \"" + r.Title + "\" found in category \"" + a.Category + "\", not \"" + r.Category + "\" - update the config if that is right.");
                    return v;
                }
            }
            return null;
        }

        public static string Find(List<AttributeEntry> attrs, AttrRef r)
        {
            return Find(attrs, r, null);
        }

        static bool Same(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>1 -> A, 26 -> Z, 27 -> AA. Non-numeric revisions are returned unchanged.</summary>
        public static string RevisionLetters(string rev)
        {
            int n;
            if (!int.TryParse(rev.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out n) || n <= 0) return rev;
            var sb = new StringBuilder();
            while (n > 0)
            {
                n--;
                sb.Insert(0, (char)('A' + n % 26));
                n /= 26;
            }
            return sb.ToString();
        }

        /// <summary>Part name without the Teamcenter "/revision" suffix, e.g. "S180-06068-47020/1" -> "S180-06068-47020".</summary>
        public static string StripRevision(string partName)
        {
            string s = (partName ?? "").Trim();
            int slash = s.IndexOf('/');
            return slash > 0 ? s.Substring(0, slash) : s;
        }

        /// <summary>Teamcenter part names look like "ID/REV" (e.g. "S180-06068-47020/1"); returns REV or null.</summary>
        public static string RevisionFromPartName(string partName)
        {
            string s = (partName ?? "").Trim();
            int slash = s.LastIndexOf('/');
            if (slash <= 0 || slash == s.Length - 1) return null;
            string rev = s.Substring(slash + 1).Trim();
            int dash = rev.IndexOf('-');                 // "1-PIPE SUPPORT ..." style names
            if (dash > 0) rev = rev.Substring(0, dash).Trim();
            return rev.Length > 0 && rev.Length <= 4 ? rev : null;
        }

        public static PartIdentity Resolve(BodyModel m, LaserConfig c)
        {
            var p = new PartIdentity();
            string id = Find(m.Attributes, c.IdAttr, p.Warnings);
            if (id != null) p.IdSource = "attribute " + c.IdAttr;
            else
            {
                id = Find(m.Attributes, c.FallbackIdAttr, p.Warnings);
                if (id != null)
                {
                    p.IdSource = "fallback attribute " + c.FallbackIdAttr;
                    p.AttributesMissing = true;
                    p.Warnings.Add("Attribute " + c.IdAttr + " not found - used " + c.FallbackIdAttr + ".");
                }
                else
                {
                    id = StripRevision(m.PartName);
                    p.IdSource = "part name";
                    p.AttributesMissing = true;
                    p.Warnings.Add("Neither " + c.IdAttr + " nor " + c.FallbackIdAttr + " found - used the part name \"" + id + "\".");
                }
            }
            p.Id = id;

            string rev = Find(m.Attributes, c.RevisionAttr, p.Warnings);
            if (rev == null)
            {
                p.AttributesMissing = true;
                string fromName = c.RevisionFromPartName ? RevisionFromPartName(m.PartName) : null;
                if (fromName != null)
                {
                    rev = fromName;
                    p.Warnings.Add("Attribute " + c.RevisionAttr + " not found - used the revision \"" + rev + "\" from the part name \"" + m.PartName + "\".");
                }
                else p.Warnings.Add("Attribute " + c.RevisionAttr + " not found - revision left out.");
            }
            p.RevisionRaw = rev ?? "";
            p.Revision = rev == null ? "" : (c.RevisionAsLetter ? RevisionLetters(rev) : rev);

            p.Name = Find(m.Attributes, c.NameAttr) ?? "";
            p.Material = Find(m.Attributes, c.MaterialAttr) ?? "";
            p.Thickness = Find(m.Attributes, c.ThicknessAttr) ?? "";
            p.DrawnBy = Find(m.Attributes, c.DrawnByAttr) ?? "";
            return p;
        }

        const string Separators = "-_. ";

        /// <summary>Replaces {TOKEN}s. An empty token also drops the separator before it
        /// (or after it, at the start), so "{ID}-{REV}" with no revision gives "ID", not "ID-".</summary>
        public static string Format(string template, Dictionary<string, string> tokens, List<string> warnings)
        {
            var sb = new StringBuilder();
            int i = 0;
            bool dropNextSeparator = false;
            while (i < template.Length)
            {
                char ch = template[i];
                int close = ch == '{' ? template.IndexOf('}', i + 1) : -1;
                if (close < 0)
                {
                    if (dropNextSeparator && Separators.IndexOf(ch) >= 0) { dropNextSeparator = false; i++; continue; }
                    dropNextSeparator = false;
                    sb.Append(ch);
                    i++;
                    continue;
                }
                string key = template.Substring(i + 1, close - i - 1);
                string value;
                if (!tokens.TryGetValue(key, out value))
                {
                    if (warnings != null) warnings.Add("Unknown placeholder {" + key + "} in \"" + template + "\" - left as is.");
                    value = "{" + key + "}";
                }
                if (value.Length == 0)
                {
                    if (sb.Length > 0 && Separators.IndexOf(sb[sb.Length - 1]) >= 0) sb.Length--;
                    else if (sb.Length == 0) dropNextSeparator = true;
                }
                else
                {
                    sb.Append(value);
                    dropNextSeparator = false;
                }
                i = close + 1;
            }
            return sb.ToString();
        }

        public static string SafeFileName(string s)
        {
            const string bad = "<>:\"/\\|?*";
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(c < 32 || bad.IndexOf(c) >= 0 ? '_' : c);
            return sb.ToString().Trim().TrimEnd('.');
        }
    }
}
