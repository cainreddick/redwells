// NX layer helpers: attributes, reflection-based dumps, output files, messages.
// Calls whose exact NX Open signature I could not verify are made through
// reflection, so a wrong guess shows up as a logged warning, not a compile error.
namespace LaserDxf.Nx
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using NXOpen;
    using LaserDxf.Core;

    public static class NxUtil
    {
        public static string PartLabel(Part part)
        {
            string s = null;
            try { s = part.Leaf; } catch { }
            if (string.IsNullOrEmpty(s)) try { s = part.Name; } catch { }
            return s ?? "unknown";
        }

        public static string SafeFileName(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString().Trim();
        }

        /// <summary>Creates the folder, falling back to %TEMP%\LaserDxf if that fails.</summary>
        public static string EnsureFolder(string folder, List<string> log)
        {
            try
            {
                Directory.CreateDirectory(folder);
                return folder;
            }
            catch (Exception ex)
            {
                string fb = Path.Combine(Path.GetTempPath(), "LaserDxf");
                Directory.CreateDirectory(fb);
                log.Add("WARNING: cannot create " + folder + " (" + ex.Message + "), using " + fb);
                return fb;
            }
        }

        public static void WriteText(string path, string text)
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        public static void Message(string title, NXMessageBox.DialogType type, string message)
        {
            UI.GetUI().NXMessageBox.Show(title, type, message);
        }
    }

    public static class NxAttributes
    {
        static readonly string[] AliasMembers = { "TitleAlias", "Alias" };

        /// <summary>Reads every part attribute with its category. Teamcenter-synced attributes
        /// can behave differently from plain NX ones, so: values missing from the struct are
        /// re-read with GetUserAttributeAsString, and each configured title not in the list is
        /// looked up directly with GetUserAttribute.</summary>
        public static void Read(Part part, BodyModel m, List<string> log, IEnumerable<string> wantedTitles)
        {
            NXObject.AttributeInformation[] infos;
            try { infos = part.GetUserAttributes(); }
            catch (Exception ex)
            {
                log.Add("GetUserAttributes failed: " + ex.Message);
                infos = new NXObject.AttributeInformation[0];
            }
            foreach (NXObject.AttributeInformation info in infos) m.Attributes.Add(ToEntry(part, info, ""));

            foreach (string title in wantedTitles)
            {
                if (string.IsNullOrEmpty(title)) continue;
                bool present = false;
                foreach (var a in m.Attributes)
                    if (string.Equals(a.Title, title, StringComparison.OrdinalIgnoreCase) || string.Equals(a.Alias, title, StringComparison.OrdinalIgnoreCase)) present = true;
                if (present) continue;
                object info = NxReflect.CallByTitle(part, "GetUserAttribute", title);
                if (info is NXObject.AttributeInformation)
                {
                    m.Attributes.Add(ToEntry(part, (NXObject.AttributeInformation)info, "direct"));
                    log.Add("Attribute \"" + title + "\" was not in GetUserAttributes() but was found by a direct lookup.");
                }
            }
        }

        static AttributeEntry ToEntry(Part part, NXObject.AttributeInformation info, string extraFlag)
        {
            object boxed = info;
            var a = new AttributeEntry
            {
                Title = info.Title ?? "",
                Category = info.Category ?? "",
                Type = info.Type.ToString()
            };
            foreach (string n in AliasMembers)
            {
                object v = NxReflect.Member(boxed, n);
                if (v != null && v.ToString().Length > 0) { a.Alias = v.ToString(); break; }
            }
            var flags = new List<string>();
            if (extraFlag.Length > 0) flags.Add(extraFlag);
            if (NxReflect.Member(boxed, "Unset") is bool && (bool)NxReflect.Member(boxed, "Unset")) { a.IsSet = false; flags.Add("unset"); }
            if (NxReflect.Member(boxed, "PdmBased") is bool && (bool)NxReflect.Member(boxed, "PdmBased")) flags.Add("pdm");
            if (NxReflect.Member(boxed, "Inherited") is bool && (bool)NxReflect.Member(boxed, "Inherited")) flags.Add("inherited");

            object val = NxReflect.Member(boxed, a.Type + "Value");
            if (val == null) val = NxReflect.Member(boxed, "StringValue");
            a.Value = val == null ? "" : Convert.ToString(val, Fmt.Inv);
            if (a.Value.Length == 0)
            {
                object s = NxReflect.CallByTitle(part, "GetUserAttributeAsString", a.Title);
                if (s is string && ((string)s).Length > 0)
                {
                    a.Value = (string)s;
                    flags.Add("asString");
                }
            }
            a.Flags = string.Join(",", flags.ToArray());
            return a;
        }
    }

    public static class NxReflect
    {
        public static object Member(object obj, string name)
        {
            if (obj == null) return null;
            Type t = obj.GetType();
            try
            {
                FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (f != null) return f.GetValue(obj);
                PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.CanRead && p.GetIndexParameters().Length == 0) return p.GetValue(obj, null);
            }
            catch { }
            return null;
        }

        public static string MemberNames(object obj)
        {
            var names = new List<string>();
            Type t = obj.GetType();
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) names.Add(f.Name);
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) names.Add(p.Name);
            return string.Join(", ", names.ToArray());
        }

        static string Describe(object v)
        {
            if (v == null) return "null";
            if (v is string) return "\"" + v + "\"";
            if (v is double) return Fmt.R((double)v);
            if (v is Enum || v.GetType().IsPrimitive) return Convert.ToString(v, Fmt.Inv);
            object rhs = Member(v, "RightHandSide");
            if (rhs != null) return "expr \"" + rhs + "\" = " + Convert.ToString(Member(v, "Value"), Fmt.Inv);
            var arr = v as Array;
            if (arr != null) return v.GetType().Name + "[" + arr.Length + "]";
            return v.GetType().Name;
        }

        static readonly string[] SkipProps = { "Tag", "Prototype", "OwningPart", "OwningComponent", "IsOccurrence", "JournalIdentifier", "IsBlanked" };

        public static void ReadProps(object obj, string prefix, List<KeyValuePair<string, string>> props, int depth)
        {
            if (obj == null || props.Count > 400) return;
            PropertyInfo[] ps = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(ps, delegate (PropertyInfo x, PropertyInfo y) { return string.CompareOrdinal(x.Name, y.Name); });
            foreach (PropertyInfo p in ps)
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0 || Array.IndexOf(SkipProps, p.Name) >= 0) continue;
                object v;
                try { v = p.GetValue(obj, null); }
                catch (Exception ex)
                {
                    Exception inner = ex.InnerException ?? ex;
                    props.Add(new KeyValuePair<string, string>(prefix + p.Name, "<error: " + inner.Message + ">"));
                    continue;
                }
                props.Add(new KeyValuePair<string, string>(prefix + p.Name, Describe(v)));
                if (v != null && depth > 0 && !(v is string) && !(v is Enum) && !v.GetType().IsPrimitive && Member(v, "RightHandSide") == null)
                {
                    string tn = v.GetType().Name;
                    if (tn.EndsWith("Builder") || tn.EndsWith("Data") || tn.EndsWith("Settings"))
                        ReadProps(v, prefix + p.Name + ".", props, depth - 1);
                }
            }
        }

        /// <summary>Opens a HolePackageBuilder on the feature (read-only, never committed) and records its properties.</summary>
        public static void ReadHoleBuilder(Part part, NXOpen.Features.Feature feat, List<KeyValuePair<string, string>> props, List<string> log)
        {
            object coll = part.Features;
            MethodInfo create = null;
            foreach (MethodInfo mi in coll.GetType().GetMethods())
            {
                if (mi.Name == "CreateHolePackageBuilder" && mi.GetParameters().Length == 1) { create = mi; break; }
            }
            if (create == null)
            {
                log.Add("FeatureCollection.CreateHolePackageBuilder not found - hole feature data unavailable.");
                return;
            }
            object builder = null;
            try
            {
                builder = create.Invoke(coll, new object[] { feat });
                ReadProps(builder, "", props, 1);
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                log.Add("Reading hole feature " + feat.JournalIdentifier + " failed: " + inner.Message);
            }
            finally
            {
                if (builder != null)
                {
                    try { builder.GetType().GetMethod("Destroy", Type.EmptyTypes).Invoke(builder, null); }
                    catch (Exception ex) { log.Add("Builder.Destroy failed: " + ex.Message); }
                }
            }
        }

        /// <summary>Calls a parameterless method by reflection; null if missing or it throws.</summary>
        public static object Call(object obj, string method)
        {
            try
            {
                MethodInfo mi = obj.GetType().GetMethod(method, Type.EmptyTypes);
                return mi == null ? null : mi.Invoke(obj, null);
            }
            catch { return null; }
        }

        /// <summary>Calls obj.method(title, AttributeType, -1) by reflection, trying the String then
        /// Any attribute type. Returns null if the method is missing or the attribute isn't there.</summary>
        public static object CallByTitle(object obj, string method, string title)
        {
            foreach (MethodInfo mi in obj.GetType().GetMethods())
            {
                ParameterInfo[] ps = mi.GetParameters();
                if (mi.Name != method || ps.Length != 3 || ps[0].ParameterType != typeof(string) || !ps[1].ParameterType.IsEnum || ps[2].ParameterType != typeof(int)) continue;
                foreach (string typeName in new[] { "String", "Any" })
                {
                    try
                    {
                        object type = Enum.Parse(ps[1].ParameterType, typeName);
                        object r = mi.Invoke(obj, new object[] { title, type, -1 });
                        if (r != null) return r;
                    }
                    catch { }
                }
            }
            return null;
        }

        /// <summary>Path of the running journal, if this NX version exposes Session.ExecutingJournal.</summary>
        public static string ExecutingJournal(Session s)
        {
            object v = Member(s, "ExecutingJournal");
            return v == null ? null : v.ToString();
        }
    }
}
