// Works out each round hole's label: NX Hole feature data first, then countersink
// geometry, then the diameter lookup table. Applies optional laser cut diameters.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;

    public sealed class HoleFeatureData
    {
        public string FeatureName = "";
        public string HoleType = "";
        public string Form = "";
        public string ThreadSize = "";
        public bool IsThreaded;
        public bool IsCountersunk;
        public bool IsCounterbored;
    }

    public static class HoleClassifier
    {
        static string Unquote(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            return s.Trim();
        }

        static bool Has(string s, string what)
        {
            return s.IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Reads the HolePackageBuilder properties the NX layer recorded for a Hole feature.
        /// The builder exposes a form for every hole type, so only the form of the active type counts.</summary>
        public static HoleFeatureData ParseHoleFeature(FeatureInfo f)
        {
            var d = new HoleFeatureData { FeatureName = f.Name };
            d.HoleType = Unquote(f.Prop("Types") ?? f.Prop("Type") ?? f.Prop("HoleType"));
            if (d.HoleType.Length == 0)
            {
                // Different NX versions name it differently: take any *Type(s) property whose value is a hole type.
                foreach (var kv in f.Props)
                    if ((kv.Key.EndsWith("Type", StringComparison.Ordinal) || kv.Key.EndsWith("Types", StringComparison.Ordinal)) && Unquote(kv.Value).EndsWith("Hole", StringComparison.OrdinalIgnoreCase))
                    {
                        d.HoleType = Unquote(kv.Value);
                        break;
                    }
            }
            if (d.HoleType.Length > 0)
            {
                d.Form = Unquote(f.Prop(d.HoleType + "Form"));
                if (d.Form.Length == 0)
                {
                    // e.g. type "ThreadedHole" -> "ThreadedHoleForm"/"ThreadForm"; take the first matching *Form
                    string stem = d.HoleType.EndsWith("Hole", StringComparison.OrdinalIgnoreCase) ? d.HoleType.Substring(0, d.HoleType.Length - 4) : d.HoleType;
                    foreach (var kv in f.Props)
                        if (kv.Key.EndsWith("Form", StringComparison.Ordinal) && kv.Key.StartsWith(stem, StringComparison.OrdinalIgnoreCase)) { d.Form = Unquote(kv.Value); break; }
                }
            }
            d.IsThreaded = Has(d.HoleType, "Thread");
            if (d.IsThreaded)
            {
                d.ThreadSize = Unquote(f.Prop("ThreadSize"));
                if (d.ThreadSize.Length == 0)
                    foreach (var kv in f.Props)
                        if (kv.Key.EndsWith("ThreadSize", StringComparison.Ordinal)) { d.ThreadSize = Unquote(kv.Value); break; }
            }
            d.IsCountersunk = Has(d.Form, "Countersunk") || Has(d.Form, "Countersink");
            d.IsCounterbored = Has(d.Form, "Counterbore");
            return d;
        }

        /// <summary>"M8 x 1.25" -> "M8", "M10x1.5" -> "M10", "1/4-20 UNC" -> "1/4-20".</summary>
        public static string ShortThread(string full)
        {
            string s = (full ?? "").Trim();
            int sp = s.IndexOf(' ');
            if (sp > 0) s = s.Substring(0, sp);
            for (int i = 1; i < s.Length - 1; i++)
                if ((s[i] == 'x' || s[i] == 'X') && char.IsDigit(s[i - 1]) && char.IsDigit(s[i + 1])) return s.Substring(0, i);
            return s;
        }

        /// <summary>Hole features behind a hole: the features that made its wall faces, plus their
        /// ancestors (a Pattern or Mirror instance points back to the Hole feature it copies).</summary>
        static List<FeatureInfo> HoleFeaturesFor(CutHole ch, Dictionary<int, FeatureInfo> featById)
        {
            var result = new List<FeatureInfo>();
            var seen = new IntSet();
            var queue = new Queue<KeyValuePair<int, int>>();
            foreach (int fid in ch.Hole.FeatureIds) queue.Enqueue(new KeyValuePair<int, int>(fid, 0));
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                FeatureInfo f;
                if (!seen.Add(item.Key) || !featById.TryGetValue(item.Key, out f)) continue;
                if (f.ClassName == "HolePackage") result.Add(f);
                if (item.Value < 4)
                    foreach (int pid in f.ParentIds) queue.Enqueue(new KeyValuePair<int, int>(pid, item.Value + 1));
            }
            return result;
        }

        public static void Classify(BodyModel m, ProfileResult profile, LaserConfig c, List<string> warnings)
        {
            var featById = new Dictionary<int, FeatureInfo>();
            foreach (var f in m.Features) featById[f.Id] = f;

            foreach (CutHole ch in profile.Holes)
            {
                if (ch.Circle == null) continue; // labels are for round holes only
                var why = new List<string>();

                // 1. NX Hole feature (directly, or behind a pattern/mirror)
                List<FeatureInfo> holeFeats = HoleFeaturesFor(ch, featById);
                if (holeFeats.Count == 0)
                {
                    var names = new List<string>();
                    foreach (int fid in ch.Hole.FeatureIds) { FeatureInfo f; if (featById.TryGetValue(fid, out f)) names.Add(f.Name); }
                    why.Add(names.Count > 0 ? "made by " + string.Join(", ", names.ToArray()) + " (no Hole feature)" : "no feature found for its faces");
                }
                else if (!c.UseHoleFeatures) why.Add("Hole feature data switched off in the config");
                else
                {
                    foreach (FeatureInfo f in holeFeats)
                    {
                        if (f.Props.Count == 0)
                        {
                            why.Add("Hole feature " + f.Name + ": its settings could not be read (see Notes)");
                            continue;
                        }
                        HoleFeatureData d = ParseHoleFeature(f);
                        if (d.IsThreaded && d.ThreadSize.Length > 0)
                        {
                            var t = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            t["THREAD"] = ShortThread(d.ThreadSize);
                            t["THREADFULL"] = d.ThreadSize;
                            ch.Label = Identity.Format(c.ThreadLabelFormat, t, warnings);
                            ch.LabelSource = "hole feature " + f.Name + " (threaded " + d.ThreadSize + ")";
                        }
                        else if (d.IsCountersunk)
                        {
                            ch.Label = c.CountersinkLabel;
                            ch.LabelSource = "hole feature " + f.Name + " (countersunk)";
                        }
                        else
                            why.Add("Hole feature " + f.Name + ": type \"" + d.HoleType + "\", form \"" + d.Form + "\"" +
                                    (d.IsThreaded ? ", thread size not found" : ", not threaded or countersunk"));
                        if (ch.Label.Length > 0) break;
                    }
                }

                // 2. Countersink found from the geometry (cone in the hole walls)
                if (ch.Label.Length == 0 && ch.Hole.HasCone && c.CountersinkLabel.Length > 0)
                {
                    ch.Label = c.CountersinkLabel;
                    ch.LabelSource = "countersink geometry";
                }

                // 3. Diameter lookup table (modelled bore diameter)
                if (ch.Label.Length == 0)
                {
                    LabelRule best = null;
                    double bestErr = double.MaxValue;
                    foreach (var r in c.LabelTable)
                    {
                        double err = Math.Abs(ch.CutDiameter - r.Diameter);
                        if (err <= r.Tolerance + 1e-9 && err < bestErr) { best = r; bestErr = err; }
                    }
                    if (best != null)
                    {
                        ch.Label = best.Label;
                        ch.LabelSource = "table dia " + Fmt.N(best.Diameter, 3) + " +/- " + Fmt.N(best.Tolerance, 3);
                    }
                    else
                    {
                        LabelRule nearest = null;
                        foreach (var r in c.LabelTable)
                            if (nearest == null || Math.Abs(ch.CutDiameter - r.Diameter) < Math.Abs(ch.CutDiameter - nearest.Diameter)) nearest = r;
                        why.Add("dia " + Fmt.N(ch.CutDiameter, 3) + " is not in the label table" +
                                (nearest == null ? "" : " (nearest: " + nearest.Label + " dia " + Fmt.N(nearest.Diameter, 3) + " +/- " + Fmt.N(nearest.Tolerance, 3) + ")"));
                    }
                    if (ch.Label.Length == 0) ch.Note = string.Join("; ", why.ToArray());
                }

                // Optional laser cut diameter for this label.
                if (ch.Label.Length > 0)
                {
                    foreach (var r in c.LabelTable)
                    {
                        if (!string.Equals(r.Label, ch.Label, StringComparison.OrdinalIgnoreCase) || double.IsNaN(r.CutDiameter)) continue;
                        ch.LabelSource += "; cut at dia " + Fmt.N(r.CutDiameter, 3) + " instead of " + Fmt.N(ch.CutDiameter, 3);
                        ch.CutDiameter = r.CutDiameter;
                        ch.Circle.Radius = r.CutDiameter / 2;
                        break;
                    }
                }
            }
        }
    }
}
