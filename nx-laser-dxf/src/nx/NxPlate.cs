// Shared NX plumbing for the journals: selecting the plate, error handling,
// locating the config file, the WCS.
namespace LaserDxf.Nx
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NXOpen;
    using NXOpen.UF;
    using LaserDxf.Core;

    public sealed class PlateSelection
    {
        public Body Body;
        /// <summary>The face the user picked, or null if they picked the body.</summary>
        public Face Face;
        public Part Part;
        /// <summary>The user picked inside an assembly (geometry was mapped to the component part).</summary>
        public bool FromOccurrence;
    }

    public static class NxPlate
    {
        public const string Title = "Laser DXF";

        public delegate void Command(Session s, ListingWindow lw, List<string> log);

        /// <summary>Runs a command with consistent error reporting (message box + listing window).</summary>
        public static void Run(Command command)
        {
            Session s = Session.GetSession();
            ListingWindow lw = s.ListingWindow;
            lw.Open();
            var log = new List<string>();
            try
            {
                command(s, lw, log);
            }
            catch (PlateException ex)
            {
                lw.WriteLine("ABORTED: " + ex.Message);
                foreach (string l in log) lw.WriteLine("  " + l);
                NxUtil.Message(Title, NXMessageBox.DialogType.Error, ex.Message);
            }
            catch (Exception ex)
            {
                lw.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
                lw.WriteLine(ex.StackTrace ?? "");
                foreach (string l in log) lw.WriteLine("  log: " + l);
                NxUtil.Message(Title, NXMessageBox.DialogType.Error, "Unexpected error:\n" + ex.Message + "\n\nDetails are in the listing window.");
            }
        }

        /// <summary>Asks for a planar face (or a body). Returns null if the user cancels.</summary>
        public static PlateSelection Select(Session s, List<string> log)
        {
            if (s.Parts.Work == null) throw new PlateException("Open the plate part first.");

            Selection.MaskTriple[] mask =
            {
                new Selection.MaskTriple(UFConstants.UF_solid_type, 0, UFConstants.UF_UI_SEL_FEATURE_ANY_FACE),
                new Selection.MaskTriple(UFConstants.UF_solid_type, 0, UFConstants.UF_UI_SEL_FEATURE_BODY)
            };
            TaggedObject picked;
            Point3d cursor;
            Selection.Response resp = UI.GetUI().SelectionManager.SelectTaggedObject(
                "Select the plate face to view from (or the plate body)", "Laser DXF - select plate",
                Selection.SelectionScope.AnyInAssembly, Selection.SelectionAction.ClearAndEnableSpecific,
                false, false, mask, out picked, out cursor);
            if (resp != Selection.Response.ObjectSelected && resp != Selection.Response.ObjectSelectedByName) return null;

            return FromPicked(picked, log);
        }

        /// <summary>Turns a picked face or body (possibly an assembly occurrence) into a plate selection
        /// on the component part itself.</summary>
        public static PlateSelection FromPicked(TaggedObject picked, List<string> log)
        {
            var sel = new PlateSelection { Face = picked as Face, Body = picked as Body };

            // Work on the part itself, not an assembly occurrence: coordinates are then the part's own.
            if (sel.Face != null && sel.Face.IsOccurrence)
            {
                sel.Face = (Face)sel.Face.Prototype;
                sel.FromOccurrence = true;
                log.Add("Selection was an assembly occurrence; using the component part's face.");
            }
            if (sel.Body != null && sel.Body.IsOccurrence)
            {
                sel.Body = (Body)sel.Body.Prototype;
                sel.FromOccurrence = true;
                log.Add("Selection was an assembly occurrence; using the component part's body.");
            }
            if (sel.Face != null)
            {
                if (sel.Face.SolidFaceType != Face.FaceType.Planar)
                    throw new PlateException("The selected face is not planar (it is " + sel.Face.SolidFaceType + "). Select the flat top or bottom face of the plate.");
                sel.Body = sel.Face.GetBody();
            }
            if (sel.Body == null) throw new PlateException("Nothing usable was selected.");
            if (!sel.Body.IsSolidBody) throw new PlateException("The selected body is not a solid body.");

            sel.Part = sel.Body.OwningPart as Part;
            if (sel.Part == null) throw new PlateException("Could not find the part that owns the selected body.");
            return sel;
        }

        /// <summary>Batch selection: any number of faces (or bodies) anywhere in the assembly.
        /// Returns null if the user cancels. Several picks on the same part give one plate (the first).</summary>
        public static List<PlateSelection> SelectMany(Session s, List<string> log)
        {
            if (s.Parts.Work == null) throw new PlateException("Open the assembly (or a plate part) first.");
            Selection.MaskTriple[] mask =
            {
                new Selection.MaskTriple(UFConstants.UF_solid_type, 0, UFConstants.UF_UI_SEL_FEATURE_ANY_FACE),
                new Selection.MaskTriple(UFConstants.UF_solid_type, 0, UFConstants.UF_UI_SEL_FEATURE_BODY)
            };
            TaggedObject[] picked;
            Selection.Response resp = UI.GetUI().SelectionManager.SelectTaggedObjects(
                "Select one face (the face to view from) on each plate to export - or the plate bodies", "Laser DXF batch - select plates",
                Selection.SelectionScope.AnyInAssembly, Selection.SelectionAction.ClearAndEnableSpecific,
                false, false, mask, out picked);
            if (resp != Selection.Response.Ok && resp != Selection.Response.ObjectSelected && resp != Selection.Response.ObjectSelectedByName) return null;
            if (picked == null || picked.Length == 0) throw new PlateException("Nothing was selected.");

            var result = new List<PlateSelection>();
            var seenParts = new Dictionary<Tag, string>();
            foreach (TaggedObject o in picked)
            {
                PlateSelection sel;
                try { sel = FromPicked(o, new List<string>()); }
                catch (PlateException ex)
                {
                    log.Add("Ignored one selection: " + ex.Message);
                    continue;
                }
                string label = NxUtil.PartLabel(sel.Part);
                if (seenParts.ContainsKey(sel.Part.Tag))
                {
                    log.Add(label + ": more than one face/body picked (or the part is used more than once) - exported once, from the first pick.");
                    continue;
                }
                seenParts[sel.Part.Tag] = label;
                result.Add(sel);
            }
            if (result.Count == 0) throw new PlateException("None of the selected objects is a planar face or solid body of a plate.");
            return result;
        }

        /// <summary>Asks the user to click on the plate where the part number should go.
        /// Returns false if they cancel. The point is NX's cursor position on the picked face.</summary>
        public static bool PickPoint(string message, out Vec3 point)
        {
            point = Vec3.Zero;
            Selection.MaskTriple[] mask =
            {
                new Selection.MaskTriple(UFConstants.UF_solid_type, 0, UFConstants.UF_UI_SEL_FEATURE_ANY_FACE)
            };
            TaggedObject picked;
            Point3d cursor;
            Selection.Response resp = UI.GetUI().SelectionManager.SelectTaggedObject(
                message, "Laser DXF - part number position",
                Selection.SelectionScope.AnyInAssembly, Selection.SelectionAction.ClearAndEnableSpecific,
                false, false, mask, out picked, out cursor);
            if (resp != Selection.Response.ObjectSelected && resp != Selection.Response.ObjectSelectedByName) return false;
            point = new Vec3(cursor.X, cursor.Y, cursor.Z);
            return true;
        }

        /// <summary>Picks the face to export: the selected one, or the plate face of the selected body.</summary>
        public static int PlateFaceId(PlateSelection sel, NxBodyExtractor ex, BodyModel m, AnalysisOptions opt, List<string> log, out string note)
        {
            if (sel.Face != null)
            {
                note = "selected directly";
                return ex.FaceIdByTag[sel.Face.Tag];
            }
            note = "picked automatically from the body";
            return PlateAnalyzer.PickPlateFace(m, opt, Vec3.UnitZ, log);
        }

        /// <summary>WCS X axis, if the plate part is the displayed part (otherwise the WCS is in
        /// assembly space and doesn't apply to the component's own coordinates).</summary>
        public static bool TryWcsX(Session s, Part part, out Vec3 x)
        {
            x = Vec3.UnitX;
            if (s.Parts.Display == null || s.Parts.Display.Tag != part.Tag) return false;
            Matrix3x3 mx = part.WCS.CoordinateSystem.Orientation.Element;
            x = new Vec3(mx.Xx, mx.Xy, mx.Xz);
            return true;
        }

        // ------------------------------------------------------------ config

        public const string ConfigFileName = "LaserDxf.config.json";
        public const string ConfigEnvVar = "LASER_DXF_CONFIG";

        /// <summary>Loads the config: %LASER_DXF_CONFIG%, else LaserDxf.config.json next to the
        /// journal, else built-in defaults (with a warning).</summary>
        public static LaserConfig LoadConfig(Session s, List<string> warnings)
        {
            var tried = new List<string>();
            string env = Environment.GetEnvironmentVariable(ConfigEnvVar);
            if (!string.IsNullOrEmpty(env)) tried.Add(env);
            string journal = NxReflect.ExecutingJournal(s);
            if (!string.IsNullOrEmpty(journal))
            {
                string dir = Path.GetDirectoryName(journal);
                if (!string.IsNullOrEmpty(dir)) tried.Add(Path.Combine(dir, ConfigFileName));
            }
            foreach (string path in tried)
            {
                if (!File.Exists(path)) continue;
                return LaserConfig.Parse(File.ReadAllText(path), path, warnings);
            }
            warnings.Add("No " + ConfigFileName + " found (looked in: " + (tried.Count == 0 ? "nowhere - journal path unknown" : string.Join("; ", tried.ToArray())) +
                         ") - using built-in defaults. Put the config file next to the journal.");
            return new LaserConfig();
        }
    }
}
