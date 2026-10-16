// Neutral description of a solid body's topology and geometry.
// The NX layer fills this in; everything downstream works on it alone.
// It can also be saved to JSON so real parts can be replayed in tests.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;

    public enum FaceKind { Planar, Cylindrical, Conical, Other }

    public enum EdgeKind { Line, Circular, Other }

    public sealed class TopoEdge
    {
        public int Id;
        public EdgeKind Kind;
        public string NxType = "";
        public List<int> FaceIds = new List<int>();

        /// <summary>Sample points from start to end. Lines: 2 points. Circular: 5 points at
        /// t = 0, .25, .5, .75, 1. Other: adaptive samples within the chord tolerance.</summary>
        public List<Vec3> Points = new List<Vec3>();

        public bool HasCircle;
        public Vec3 Center;
        public double Radius;
        public Vec3 Axis;
        public double Length;

        public Vec3 Start { get { return Points[0]; } }
        public Vec3 End { get { return Points[Points.Count - 1]; } }

        public bool IsClosed(double tol) { return Start.DistanceTo(End) <= tol; }
    }

    public sealed class TopoFace
    {
        public int Id;
        public FaceKind Kind;
        public string NxType = "";
        public List<int> EdgeIds = new List<int>();
    }

    public sealed class FeatureInfo
    {
        public int Id;
        public string Name = "";
        public string FeatureType = "";
        public string ClassName = "";
        public List<int> FaceIds = new List<int>();
        public List<KeyValuePair<string, string>> Props = new List<KeyValuePair<string, string>>();

        public string Prop(string key)
        {
            foreach (var kv in Props) if (kv.Key == key) return kv.Value;
            return null;
        }
    }

    public sealed class AttributeEntry
    {
        public string Category = "";
        public string Title = "";
        public string Alias = "";
        public string Value = "";
        public string Type = "";
        public bool IsSet = true;
    }

    public sealed class BodyModel
    {
        public string PartName = "";
        public string Units = "Millimeters";
        public List<TopoFace> Faces = new List<TopoFace>();
        public List<TopoEdge> Edges = new List<TopoEdge>();
        public List<FeatureInfo> Features = new List<FeatureInfo>();
        public List<AttributeEntry> Attributes = new List<AttributeEntry>();

        Dictionary<int, TopoFace> faceIndex;
        Dictionary<int, TopoEdge> edgeIndex;

        public TopoFace Face(int id)
        {
            if (faceIndex == null || faceIndex.Count != Faces.Count)
            {
                faceIndex = new Dictionary<int, TopoFace>();
                foreach (var f in Faces) faceIndex[f.Id] = f;
            }
            return faceIndex[id];
        }

        public TopoEdge Edge(int id)
        {
            if (edgeIndex == null || edgeIndex.Count != Edges.Count)
            {
                edgeIndex = new Dictionary<int, TopoEdge>();
                foreach (var e in Edges) edgeIndex[e.Id] = e;
            }
            return edgeIndex[id];
        }

        public IEnumerable<Vec3> AllPoints()
        {
            foreach (var e in Edges)
                foreach (var p in e.Points)
                    yield return p;
        }

        // ---------------- JSON round trip ----------------

        static List<object> V(Vec3 v) { return new List<object> { v.X, v.Y, v.Z }; }

        static Vec3 ToVec(object o)
        {
            var l = (List<object>)o;
            return new Vec3((double)l[0], (double)l[1], (double)l[2]);
        }

        static List<object> Ints(List<int> ids)
        {
            var l = new List<object>();
            foreach (int i in ids) l.Add((double)i);
            return l;
        }

        static List<int> ToInts(object o)
        {
            var r = new List<int>();
            var l = o as List<object>;
            if (l != null) foreach (object x in l) r.Add((int)(double)x);
            return r;
        }

        public JObj ToJson()
        {
            var faces = new List<object>();
            foreach (var f in Faces)
                faces.Add(new JObj().Add("id", (double)f.Id).Add("kind", f.Kind.ToString()).Add("nxType", f.NxType).Add("edges", Ints(f.EdgeIds)));

            var edges = new List<object>();
            foreach (var e in Edges)
            {
                var pts = new List<object>();
                foreach (var p in e.Points) pts.Add(V(p));
                var o = new JObj().Add("id", (double)e.Id).Add("kind", e.Kind.ToString()).Add("nxType", e.NxType)
                    .Add("faces", Ints(e.FaceIds)).Add("length", e.Length).Add("points", pts);
                if (e.HasCircle) o.Add("center", V(e.Center)).Add("radius", e.Radius).Add("axis", V(e.Axis));
                edges.Add(o);
            }

            var feats = new List<object>();
            foreach (var f in Features)
            {
                var props = new JObj();
                foreach (var kv in f.Props) props.Add(kv.Key, kv.Value);
                feats.Add(new JObj().Add("id", (double)f.Id).Add("name", f.Name).Add("featureType", f.FeatureType)
                    .Add("className", f.ClassName).Add("faces", Ints(f.FaceIds)).Add("props", props));
            }

            var attrs = new List<object>();
            foreach (var a in Attributes)
                attrs.Add(new JObj().Add("category", a.Category).Add("title", a.Title).Add("alias", a.Alias)
                    .Add("value", a.Value).Add("type", a.Type).Add("isSet", a.IsSet));

            return new JObj().Add("format", "laserdxf-body-1").Add("partName", PartName).Add("units", Units)
                .Add("faces", faces).Add("edges", edges).Add("features", feats).Add("attributes", attrs);
        }

        public static BodyModel FromJson(JObj o)
        {
            var m = new BodyModel();
            m.PartName = o.Str("partName", "");
            m.Units = o.Str("units", "Millimeters");
            foreach (JObj f in o.Arr("faces"))
            {
                m.Faces.Add(new TopoFace
                {
                    Id = (int)f.Num("id", 0),
                    Kind = (FaceKind)Enum.Parse(typeof(FaceKind), f.Str("kind", "Other")),
                    NxType = f.Str("nxType", ""),
                    EdgeIds = ToInts(f.Get("edges"))
                });
            }
            foreach (JObj e in o.Arr("edges"))
            {
                var te = new TopoEdge
                {
                    Id = (int)e.Num("id", 0),
                    Kind = (EdgeKind)Enum.Parse(typeof(EdgeKind), e.Str("kind", "Other")),
                    NxType = e.Str("nxType", ""),
                    FaceIds = ToInts(e.Get("faces")),
                    Length = e.Num("length", 0)
                };
                foreach (object p in e.Arr("points")) te.Points.Add(ToVec(p));
                if (e.Has("center"))
                {
                    te.HasCircle = true;
                    te.Center = ToVec(e.Get("center"));
                    te.Radius = e.Num("radius", 0);
                    te.Axis = ToVec(e.Get("axis"));
                }
                m.Edges.Add(te);
            }
            var feats = o.Arr("features");
            if (feats != null)
            {
                foreach (JObj f in feats)
                {
                    var fi = new FeatureInfo
                    {
                        Id = (int)f.Num("id", 0),
                        Name = f.Str("name", ""),
                        FeatureType = f.Str("featureType", ""),
                        ClassName = f.Str("className", ""),
                        FaceIds = ToInts(f.Get("faces"))
                    };
                    var props = f.Obj("props");
                    if (props != null) foreach (var kv in props.Items) fi.Props.Add(new KeyValuePair<string, string>(kv.Key, Convert.ToString(kv.Value, Fmt.Inv)));
                    m.Features.Add(fi);
                }
            }
            var attrs = o.Arr("attributes");
            if (attrs != null)
            {
                foreach (JObj a in attrs)
                {
                    m.Attributes.Add(new AttributeEntry
                    {
                        Category = a.Str("category", ""),
                        Title = a.Str("title", ""),
                        Alias = a.Str("alias", ""),
                        Value = a.Str("value", ""),
                        Type = a.Str("type", ""),
                        IsSet = a.Bool("isSet", true)
                    });
                }
            }
            return m;
        }
    }
}
