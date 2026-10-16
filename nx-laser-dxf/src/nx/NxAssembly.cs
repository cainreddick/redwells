// Counts how many times each part occurs in the displayed assembly (the same tree as the
// Assembly Navigator), for the Qty column of the batch CSV.
namespace LaserDxf.Nx
{
    using System;
    using System.Collections.Generic;
    using NXOpen;
    using NXOpen.Assemblies;
    using LaserDxf.Core;

    public sealed class PartCounts
    {
        readonly Dictionary<Tag, int> byTag = new Dictionary<Tag, int>();
        readonly Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public int Excluded;
        public bool IsAssembly;

        public void Add(Tag tag, string name)
        {
            int n;
            if (tag != Tag.Null) { byTag.TryGetValue(tag, out n); byTag[tag] = n + 1; }
            if (!string.IsNullOrEmpty(name)) { byName.TryGetValue(name, out n); byName[name] = n + 1; }
        }

        /// <summary>Occurrences of the part (by its tag, else by its name, for unloaded components).</summary>
        public int CountOf(Part part)
        {
            int n;
            if (byTag.TryGetValue(part.Tag, out n)) return n;
            if (byName.TryGetValue(NxUtil.PartLabel(part), out n)) return n;
            return 0;
        }
    }

    public static class NxAssembly
    {
        /// <summary>Walks the displayed part's component tree. Suppressed components (and everything
        /// under them) are left out, as are components carrying one of the configured "not in the
        /// parts list" attributes with a yes-like value (e.g. REFERENCE_COMPONENT, PLIST_IGNORE_MEMBER).</summary>
        public static PartCounts Count(Part display, LaserConfig cfg, List<string> log)
        {
            var counts = new PartCounts();
            Component root = null;
            try { root = display.ComponentAssembly.RootComponent; }
            catch (Exception ex) { log.Add("Could not read the assembly structure: " + ex.Message); }
            if (root == null) return counts; // a single part, not an assembly
            counts.IsAssembly = true;
            Walk(root, cfg, counts, log, 0);
            return counts;
        }

        static void Walk(Component parent, LaserConfig cfg, PartCounts counts, List<string> log, int depth)
        {
            Component[] children;
            try { children = parent.GetChildren(); }
            catch { return; }
            foreach (Component c in children)
            {
                object suppressed = NxReflect.Member(c, "IsSuppressed");
                if (suppressed is bool && (bool)suppressed) { counts.Excluded++; continue; }
                string why = ExcludedBy(c, cfg);
                if (why != null)
                {
                    counts.Excluded++;
                    if (counts.Excluded <= 10) log.Add("Qty: " + c.DisplayName + " not counted (" + why + ").");
                    continue;
                }
                Part proto = c.Prototype as Part;
                counts.Add(proto != null ? proto.Tag : Tag.Null, proto != null ? NxUtil.PartLabel(proto) : c.DisplayName);
                if (depth < 50) Walk(c, cfg, counts, log, depth + 1);
            }
        }

        static string ExcludedBy(Component c, LaserConfig cfg)
        {
            foreach (string title in cfg.QtyExcludeAttributes)
            {
                object v = NxReflect.CallByTitle(c, "GetUserAttributeAsString", title);
                string s = v as string;
                if (s == null) continue;
                s = s.Trim();
                if (s.Length == 0 || s == "0" || s.Equals("N", StringComparison.OrdinalIgnoreCase) || s.Equals("No", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("False", StringComparison.OrdinalIgnoreCase) || s.Equals("<No Value>", StringComparison.OrdinalIgnoreCase)) continue;
                return title + " = " + s;
            }
            return null;
        }
    }
}
