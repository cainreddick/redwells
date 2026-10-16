// Compile-only stand-ins for the NX Open members the journals use, so the
// merged journal can be syntax/type-checked as C# 5 outside NX. Signatures
// follow the NX Open .NET reference; behaviour is irrelevant (never run).
using System;

namespace NXOpen
{
    public enum Tag : uint { Null = 0 }

    public struct Point3d
    {
        public double X, Y, Z;
        public Point3d(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    public class TaggedObject { public Tag Tag { get { return Tag.Null; } } }

    public class NXObject : TaggedObject
    {
        public struct AttributeInformation
        {
            public AttributeType Type;
            public string Title;
            public string Category;
            public string StringValue;
        }

        public enum AttributeType { Invalid, Null, Boolean, Integer, Real, String, Time, Reference, Any }

        public bool IsOccurrence { get { return false; } }
        public NXObject Prototype { get { return null; } }
        public BasePart OwningPart { get { return null; } }
        public string JournalIdentifier { get { return ""; } }
        public string Name { get { return ""; } }
        public AttributeInformation[] GetUserAttributes() { return null; }
    }

    public class DisplayableObject : NXObject { }

    public class BasePart : NXObject
    {
        public enum Units { Inches, Millimeters }
        public Units PartUnits { get { return Units.Millimeters; } }
        public string Leaf { get { return ""; } }
        public string FullPath { get { return ""; } }
    }

    public class Part : BasePart
    {
        public NXOpen.Features.FeatureCollection Features { get { return null; } }
    }

    public class PartCollection { public Part Work { get { return null; } } public Part Display { get { return null; } } }

    public class ListingWindow
    {
        public void Open() { }
        public void WriteLine(string s) { }
    }

    public class Session
    {
        public enum LibraryUnloadOption { Explicitly, Immediately, AtTermination }
        public static Session GetSession() { return null; }
        public PartCollection Parts { get { return null; } }
        public ListingWindow ListingWindow { get { return null; } }
    }

    public class Body : DisplayableObject
    {
        public bool IsSolidBody { get { return true; } }
        public bool IsSheetBody { get { return false; } }
        public Face[] GetFaces() { return null; }
        public Edge[] GetEdges() { return null; }
        public NXOpen.Features.Feature[] GetFeatures() { return null; }
    }

    public class Face : DisplayableObject
    {
        public enum FaceType { Undefined, Planar, Cylindrical, Conical, Spherical, SurfaceOfRevolution, Parametric, Blending, Offset, Swept, Convergent }
        public FaceType SolidFaceType { get { return FaceType.Planar; } }
        public Edge[] GetEdges() { return null; }
        public Body GetBody() { return null; }
    }

    public class Edge : DisplayableObject
    {
        public enum EdgeType { Undefined, Linear, Circular, Elliptical, Intersection, Spline, SpCurve, Foreign, ConstantParameter, TrimmedCurve, Convergent }
        public EdgeType SolidEdgeType { get { return EdgeType.Linear; } }
        public Face[] GetFaces() { return null; }
        public double GetLength() { return 0; }
        public void GetVertices(out Point3d vertex1, out Point3d vertex2) { vertex1 = new Point3d(); vertex2 = new Point3d(); }
    }

    public class Selection
    {
        public enum SelectionScope { WorkPart, AnyInAssembly, WorkPartAndOccurrence }
        public enum SelectionAction { EnableAll, EnableSpecific, DisableSpecific, ClearAndEnableSpecific, AllAndDisableSpecific }
        public enum Response { Cancel = 1, Back = 2, Ok = 3, ObjectSelectedByName = 4, ObjectSelected = 5 }
        public struct MaskTriple
        {
            public int Type, Subtype, SolidBodySubtype;
            public MaskTriple(int type, int subtype, int solidBodySubtype) { Type = type; Subtype = subtype; SolidBodySubtype = solidBodySubtype; }
        }
        public Response SelectTaggedObject(string message, string title, SelectionScope scope, SelectionAction action,
            bool includeFeatures, bool keepHighlighted, MaskTriple[] maskArray, out TaggedObject selectedObject, out Point3d cursor)
        { selectedObject = null; cursor = new Point3d(); return Response.Cancel; }
    }

    public class NXMessageBox
    {
        public enum DialogType { Error, Information, Question, Warning }
        public int Show(string title, DialogType type, string message) { return 0; }
    }

    public class UI
    {
        public static UI GetUI() { return null; }
        public Selection SelectionManager { get { return null; } }
        public NXMessageBox NXMessageBox { get { return null; } }
    }
}

namespace NXOpen.Features
{
    public class Feature : NXOpen.NXObject
    {
        public string FeatureType { get { return ""; } }
        public string GetFeatureName() { return ""; }
    }

    public class BodyFeature : Feature
    {
        public NXOpen.Face[] GetFaces() { return null; }
    }

    public class HolePackage : BodyFeature { }

    public class FeatureCollection { }
}

namespace NXOpen.UF
{
    public static class UFConstants
    {
        public const int UF_solid_type = 70;
        public const int UF_UI_SEL_FEATURE_ANY_FACE = 20;
        public const int UF_UI_SEL_FEATURE_BODY = 1;
    }

    public class UFModl
    {
        public void AskCurveProps(NXOpen.Tag curve, double parm, double[] point, double[] tangent, double[] p_norm,
            double[] b_norm, out double torsion, out double rad_of_cur)
        { torsion = 0; rad_of_cur = 0; }
    }

    public class UFSession
    {
        public static UFSession GetUFSession() { return null; }
        public UFModl Modl { get { return null; } }
    }
}
