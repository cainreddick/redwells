// NX layer: reads a solid body's faces, edges, features and the part's attributes
// into the neutral BodyModel. This is the only place that touches NX geometry.
namespace LaserDxf.Nx
{
    using System;
    using System.Collections.Generic;
    using NXOpen;
    using NXOpen.UF;
    using LaserDxf.Core;

    public sealed class NxBodyExtractor
    {
        readonly UFSession uf;
        readonly List<string> log;

        /// <summary>Chord tolerance (mm) when sampling edges that are not lines or arcs.</summary>
        public double ChordTol = 0.01;

        public readonly Dictionary<Tag, int> FaceIdByTag = new Dictionary<Tag, int>();
        public readonly Dictionary<Tag, int> EdgeIdByTag = new Dictionary<Tag, int>();
        public readonly Dictionary<int, Face> FaceById = new Dictionary<int, Face>();

        int vertexMismatches;

        public NxBodyExtractor(UFSession uf, List<string> log)
        {
            this.uf = uf;
            this.log = log;
        }

        public BodyModel Extract(Body body, Part part)
        {
            var m = new BodyModel();
            m.PartName = NxUtil.PartLabel(part);
            m.Units = part.PartUnits.ToString();

            Face[] faces = body.GetFaces();
            Edge[] edges = body.GetEdges();
            foreach (Face f in faces)
            {
                int id = FaceIdByTag.Count + 1;
                FaceIdByTag[f.Tag] = id;
                FaceById[id] = f;
            }
            foreach (Edge e in edges) EdgeIdByTag[e.Tag] = EdgeIdByTag.Count + 1;

            foreach (Edge e in edges) m.Edges.Add(ExtractEdge(e));

            foreach (Face f in faces)
            {
                var tf = new TopoFace
                {
                    Id = FaceIdByTag[f.Tag],
                    NxType = f.SolidFaceType.ToString(),
                    Kind = MapFace(f.SolidFaceType)
                };
                foreach (Edge e in f.GetEdges())
                {
                    int eid;
                    if (EdgeIdByTag.TryGetValue(e.Tag, out eid)) tf.EdgeIds.Add(eid);
                }
                m.Faces.Add(tf);
            }

            if (vertexMismatches > 0)
                log.Add("WARNING: " + vertexMismatches + " edge(s) whose sampled end points do not match their NX vertices. Please send me the debug file.");

            ExtractFeatures(body, part, m);
            NxAttributes.Read(part, m, log);
            return m;
        }

        static FaceKind MapFace(Face.FaceType t)
        {
            if (t == Face.FaceType.Planar) return FaceKind.Planar;
            if (t == Face.FaceType.Cylindrical) return FaceKind.Cylindrical;
            if (t == Face.FaceType.Conical) return FaceKind.Conical;
            return FaceKind.Other;
        }

        Vec3 Eval(Edge e, double param, out double radiusOfCurvature)
        {
            double[] p = new double[3];
            double[] tangent = new double[3];
            double[] pNorm = new double[3];
            double[] bNorm = new double[3];
            double torsion;
            // UF_MODL_ask_curve_props: parameter is normalised 0..1 over the edge.
            uf.Modl.AskCurveProps(e.Tag, param, p, tangent, pNorm, bNorm, out torsion, out radiusOfCurvature);
            return new Vec3(p[0], p[1], p[2]);
        }

        Vec3 Eval(Edge e, double param)
        {
            double r;
            return Eval(e, param, out r);
        }

        TopoEdge ExtractEdge(Edge e)
        {
            var te = new TopoEdge { Id = EdgeIdByTag[e.Tag], NxType = e.SolidEdgeType.ToString() };
            foreach (Face f in e.GetFaces())
            {
                int fid;
                if (FaceIdByTag.TryGetValue(f.Tag, out fid)) te.FaceIds.Add(fid);
            }
            try { te.Length = e.GetLength(); }
            catch (Exception ex) { log.Add("Edge " + te.Id + ": GetLength failed: " + ex.Message); }

            Edge.EdgeType et = e.SolidEdgeType;
            if (et == Edge.EdgeType.Linear)
            {
                te.Kind = EdgeKind.Line;
                te.Points.Add(Eval(e, 0.0));
                te.Points.Add(Eval(e, 1.0));
            }
            else if (et == Edge.EdgeType.Circular)
            {
                te.Kind = EdgeKind.Circular;
                for (int i = 0; i <= 4; i++) te.Points.Add(Eval(e, i / 4.0));
                Vec3 c, axis;
                double r;
                if (GeomUtil.CircleFrom3Points(te.Points[0], te.Points[1], te.Points[2], out c, out r, out axis))
                {
                    te.HasCircle = true;
                    te.Center = c;
                    te.Radius = r;
                    te.Axis = axis;
                    double rc;
                    Eval(e, 0.5, out rc);
                    if (Math.Abs(rc - r) > 1e-4)
                        log.Add("Edge " + te.Id + ": 3-point radius " + Fmt.N(r, 5) + " differs from NX curvature radius " + Fmt.N(rc, 5));
                }
                else log.Add("Edge " + te.Id + ": circular edge but the sample points are collinear?");
            }
            else
            {
                te.Kind = EdgeKind.Other;
                Edge edge = e;
                te.Points = Sampler.Sample(delegate (double s) { return Eval(edge, s); }, ChordTol, 8, 12);
            }

            CheckVertices(e, te);
            return te;
        }

        void CheckVertices(Edge e, TopoEdge te)
        {
            try
            {
                Point3d v1, v2;
                e.GetVertices(out v1, out v2);
                Vec3 a = new Vec3(v1.X, v1.Y, v1.Z);
                Vec3 b = new Vec3(v2.X, v2.Y, v2.Z);
                const double tol = 1e-3;
                bool startOk = te.Start.DistanceTo(a) < tol || te.Start.DistanceTo(b) < tol;
                bool endOk = te.End.DistanceTo(a) < tol || te.End.DistanceTo(b) < tol;
                if (!startOk || !endOk)
                {
                    vertexMismatches++;
                    if (vertexMismatches <= 5)
                        log.Add("Edge " + te.Id + " (" + te.NxType + "): vertices " + a + " " + b + " but sampled ends " + te.Start + " " + te.End);
                }
            }
            catch (Exception ex)
            {
                log.Add("Edge " + te.Id + ": GetVertices failed: " + ex.Message);
            }
        }

        void ExtractFeatures(Body body, Part part, BodyModel m)
        {
            NXOpen.Features.Feature[] feats;
            try { feats = body.GetFeatures(); }
            catch (Exception ex)
            {
                log.Add("Body.GetFeatures failed: " + ex.Message);
                return;
            }

            int id = 1;
            foreach (NXOpen.Features.Feature feat in feats)
            {
                var fi = new FeatureInfo { Id = id++, ClassName = feat.GetType().Name };
                try { fi.Name = feat.GetFeatureName(); } catch { fi.Name = feat.JournalIdentifier; }
                try { fi.FeatureType = feat.FeatureType; } catch { }

                var bf = feat as NXOpen.Features.BodyFeature;
                if (bf != null)
                {
                    try
                    {
                        foreach (Face f in bf.GetFaces())
                        {
                            int fid;
                            if (FaceIdByTag.TryGetValue(f.Tag, out fid)) fi.FaceIds.Add(fid);
                        }
                    }
                    catch (Exception ex) { log.Add("Feature " + fi.Name + ": GetFaces failed: " + ex.Message); }
                }

                if (fi.ClassName == "HolePackage")
                    NxReflect.ReadHoleBuilder(part, feat, fi.Props, log);

                m.Features.Add(fi);
            }
        }
    }
}
