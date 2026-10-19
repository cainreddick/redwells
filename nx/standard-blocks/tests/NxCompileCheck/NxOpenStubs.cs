// Compile-check stubs for the parts of the NX Open API that StandardBlocks.cs uses.
//
// These are NOT Siemens code and they do nothing. They only let the journal
// compile on a machine without NX, with C# 5 syntax enforced, so typos and
// syntax NX's compiler would reject get caught before a test run in NX.
// Signatures are my best understanding of NX Open 2414; if NX reports a
// compile error, fix the journal AND update the stub here to match NX.

using System;
using System.Collections;

namespace NXOpen
{
    public enum Tag : uint { Null = 0 }

    public class NXException : Exception
    {
        public NXException(string message) : base(message) { }
    }

    public class TaggedObject
    {
        public Tag Tag { get { throw new NotImplementedException(); } }
    }

    public class NXObject : TaggedObject
    {
        public enum AttributeType { Invalid, Null, Boolean, Integer, Real, String, Time, Reference, Any }

        public struct AttributeInformation
        {
            public string Title;
            public AttributeType Type;
        }

        public string Name { get { throw new NotImplementedException(); } }
        public string JournalIdentifier { get { throw new NotImplementedException(); } }
        public BasePart OwningPart { get { throw new NotImplementedException(); } }
        public bool HasUserAttribute(string title, AttributeType type, int index) { throw new NotImplementedException(); }
        public string GetStringUserAttribute(string title, int index) { throw new NotImplementedException(); }
        public AttributeInformation[] GetUserAttributes() { throw new NotImplementedException(); }
        public void SetUserAttribute(string title, int index, string value, Update.Option option) { throw new NotImplementedException(); }
        public void SetName(string name) { throw new NotImplementedException(); }
        public string GetUserAttributeAsString(string title, AttributeType type, int index) { throw new NotImplementedException(); }
    }

    public class DisplayableObject : NXObject
    {
        public int Layer { get { throw new NotImplementedException(); } set { } }
        public void Highlight() { }
        public void Unhighlight() { }
    }

    public class Face : DisplayableObject
    {
        public Body GetBody() { throw new NotImplementedException(); }
    }

    public class Body : DisplayableObject
    {
        public bool IsSolidBody { get { throw new NotImplementedException(); } }
        public Face[] GetFaces() { throw new NotImplementedException(); }
        public NXOpen.Features.Feature[] GetFeatures() { throw new NotImplementedException(); }
    }

    public class BodyCollection : IEnumerable
    {
        public IEnumerator GetEnumerator() { throw new NotImplementedException(); }
    }

    public class ReferenceSet : DisplayableObject
    {
        public NXObject[] AskMembersInReferenceSet() { throw new NotImplementedException(); }
    }

    public class PartLoadStatus : IDisposable
    {
        public void Dispose() { }
    }

    public class BasePart : NXObject
    {
        public enum Units { Inches, Millimeters }

        public Units PartUnits { get { throw new NotImplementedException(); } }
        public string Leaf { get { throw new NotImplementedException(); } }
        public bool IsFullyLoaded { get { throw new NotImplementedException(); } }
        public bool IsReadOnly { get { throw new NotImplementedException(); } }
        public PartLoadStatus LoadThisPartFully() { throw new NotImplementedException(); }
        public NXOpen.Assemblies.ComponentAssembly ComponentAssembly { get { throw new NotImplementedException(); } }
    }

    public class Part : BasePart
    {
        public NXOpen.Features.FeatureCollection Features { get { throw new NotImplementedException(); } }
        public NXOpen.Features.BaseFeatureCollection BaseFeatures { get { throw new NotImplementedException(); } }
        public ScCollectorCollection ScCollectors { get { throw new NotImplementedException(); } }
        public ScRuleFactory ScRuleFactory { get { throw new NotImplementedException(); } }
        public BodyCollection Bodies { get { throw new NotImplementedException(); } }
        public ReferenceSet[] GetAllReferenceSets() { throw new NotImplementedException(); }
    }

    public class PartCollection
    {
        public enum RefsetOption { Entire, Current }
        public enum WorkComponentOption { Given, Visible }
        public NXOpen.Assemblies.Component WorkComponent { get { throw new NotImplementedException(); } }
        public void SetWorkComponent(NXOpen.Assemblies.Component component, RefsetOption refsetOption,
            WorkComponentOption visibilityOption, out PartLoadStatus loadStatus) { throw new NotImplementedException(); }
        public Part Display { get { throw new NotImplementedException(); } }
        public Part Work { get { throw new NotImplementedException(); } }
    }

    public class ListingWindow
    {
        public void Open() { }
        public void WriteLine(string msg) { }
    }

    public class Session
    {
        public enum LibraryUnloadOption { AtTermination, Immediately, Explicitly }
        public enum MarkVisibility { Invisible, Visible, AnyVisibility }
        public struct UndoMarkId { public int Value; }
        public UndoMarkId SetUndoMark(MarkVisibility visibility, string name) { throw new NotImplementedException(); }
        public void UndoToMark(UndoMarkId markId, string markName) { throw new NotImplementedException(); }

        public static Session GetSession() { throw new NotImplementedException(); }
        public ListingWindow ListingWindow { get { throw new NotImplementedException(); } }
        public PartCollection Parts { get { throw new NotImplementedException(); } }
        public string ExecutingJournal { get { throw new NotImplementedException(); } }
    }

    public static class Update
    {
        public enum Option { Now, Later }
    }

    public class Builder
    {
        public NXObject Commit() { throw new NotImplementedException(); }
        public void Destroy() { }
    }

    public class SelectionIntentRule { }
    public class BodyDumbRule : SelectionIntentRule { }

    public class ScCollector : NXObject
    {
        public void ReplaceRules(SelectionIntentRule[] rules, bool createRulesWoUpdate) { throw new NotImplementedException(); }
    }

    public class ScCollectorCollection
    {
        public ScCollector CreateCollector() { throw new NotImplementedException(); }
    }

    public class ScRuleFactory
    {
        public BodyDumbRule CreateRuleBodyDumb(Body[] bodies, bool includeSheetBodies) { throw new NotImplementedException(); }
    }

    public struct Point3d
    {
        public double X, Y, Z;
    }

    public struct Matrix3x3
    {
        public double Xx, Xy, Xz, Yx, Yy, Yz, Zx, Zy, Zz;
    }

    public class Selection
    {
        public struct MaskTriple
        {
            public int Type;
            public int Subtype;
            public int SolidBodySubtype;
        }

        public enum Response { Back = 1, Cancel, Ok, ObjectSelected, ObjectSelectedByName }
        public enum SelectionScope { UseDefault, WorkPart, AnyInAssembly, WorkPartAndOccurrence }
        public enum SelectionAction { ClearAndEnableSpecific, EnableSpecific, DisableSpecific, EnableAll, AllAndDisableSpecific, UseDefault }

        public Response SelectTaggedObjects(string message, string title, SelectionScope scope, SelectionAction action,
            bool includeFeatures, bool keepHighlighted, MaskTriple[] maskArray, out TaggedObject[] objectArray)
        { throw new NotImplementedException(); }
    }

    public class NXMessageBox
    {
        public enum DialogType { Error, Warning, Information, Question }

        public int Show(string title, DialogType style, string message) { throw new NotImplementedException(); }
    }

    public class UI
    {
        public static UI GetUI() { throw new NotImplementedException(); }
        public Selection SelectionManager { get { throw new NotImplementedException(); } }
        public NXMessageBox NXMessageBox { get { throw new NotImplementedException(); } }
    }
}

namespace NXOpen.Assemblies
{
    public class Component : DisplayableObject
    {
        public BasePart Prototype { get { throw new NotImplementedException(); } }
        public Component Parent { get { throw new NotImplementedException(); } }
        public string DisplayName { get { throw new NotImplementedException(); } }
        public string ReferenceSet { get { throw new NotImplementedException(); } }
        public bool IsSuppressed { get { throw new NotImplementedException(); } }
        public NXObject FindOccurrence(NXObject proto) { throw new NotImplementedException(); }
        public Component[] GetChildren() { throw new NotImplementedException(); }
        public void GetPosition(out Point3d position, out Matrix3x3 orientation) { throw new NotImplementedException(); }
    }

    public class ComponentAssembly
    {
        public Component RootComponent { get { throw new NotImplementedException(); } }
    }
}

namespace NXOpen.Features
{
    public class Feature : NXObject
    {
        public enum BooleanType { Create, Unite, Subtract, Intersect }
        public string GetFeatureName() { throw new NotImplementedException(); }
    }

    public class BodyFeature : Feature
    {
        public Body[] GetBodies() { throw new NotImplementedException(); }
    }

    public class FeatureCollection : System.Collections.IEnumerable
    {
        public System.Collections.IEnumerator GetEnumerator() { throw new NotImplementedException(); }
        public BooleanBuilder CreateBooleanBuilderUsingCollector(BooleanFeature boolean) { throw new NotImplementedException(); }
    }

    public class BaseFeatureCollection
    {
        public WaveLinkBuilder CreateWaveLinkBuilder(Feature feature) { throw new NotImplementedException(); }
    }

    public class BooleanFeature : BodyFeature { }

    public class ExtractFaceBuilder : Builder
    {
        public enum ParentPartType { WorkPart, OtherPart }
        public ParentPartType ParentPart { get; set; }
        public bool Associative { get; set; }
        public bool FixAtCurrentTimestamp { get; set; }
        public bool HideOriginal { get; set; }
        public bool InheritDisplayProperties { get; set; }
        public ScCollector ExtractBodyCollector { get { throw new NotImplementedException(); } }   // read-only in NX 2414
    }

    public class WaveLinkBuilder : Builder
    {
        public enum Types { CurveLink, PointLink, DatumLink, FaceLink, RegionLink, BodyLink }
        public Types Type { get; set; }
        public ExtractFaceBuilder ExtractFaceBuilder { get { throw new NotImplementedException(); } }
    }

    public class BooleanBuilder : Builder
    {
        public Feature.BooleanType Operation { get; set; }
        public bool RetainTool { get; set; }
        public ScCollector TargetBodyCollector { get; set; }
        public ScCollector ToolBodyCollector { get; set; }
    }
}

namespace NXOpen.UF
{
    public class UFModl
    {
        public void AskFaceData(Tag face, out int type, double[] point, double[] dir, double[] box,
            out double radius, out double radData, out int normDir)
        { throw new NotImplementedException(); }

        public void AskBoundingBox(Tag obj, double[] boundingBox) { throw new NotImplementedException(); }

        // results[i]: 1 = interference, 2 = none, 3 = touching
        public void CheckInterference(Tag target, int numTools, Tag[] tools, int[] results) { throw new NotImplementedException(); }
    }

    public class UFSession
    {
        public static UFSession GetUFSession() { throw new NotImplementedException(); }
        public UFModl Modl { get { throw new NotImplementedException(); } }
    }

    public static class UFConstants
    {
        public const int UF_component_type = 63;
        public const int UF_component_subtype = 1;
        public const int UF_solid_type = 70;
        public const int UF_UI_SEL_FEATURE_ANY_FACE = 20;
    }
}
