// Unit tests for the pure-logic core. Run with tests/run_tests.sh (Mono).
namespace LaserDxf.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using LaserDxf.Core;

    public static class TestMain
    {
        static int failures, checks;
        static string current;

        public static void Check(bool ok, string what)
        {
            checks++;
            if (ok) return;
            failures++;
            Console.WriteLine("  FAIL [" + current + "] " + what);
        }

        public static void Near(double actual, double expected, double tol, string what)
        {
            Check(Math.Abs(actual - expected) <= tol, what + ": expected " + expected + ", got " + actual);
        }

        public static string OutDir;

        public static int Main(string[] args)
        {
            OutDir = args.Length > 0 ? args[0] : "build/test-output";
            Directory.CreateDirectory(OutDir);
            foreach (Type t in typeof(TestMain).Assembly.GetTypes())
            {
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!mi.Name.StartsWith("Test") || mi.GetParameters().Length != 0) continue;
                    current = t.Name + "." + mi.Name;
                    int before = failures;
                    try { mi.Invoke(null, null); }
                    catch (TargetInvocationException ex)
                    {
                        failures++;
                        Console.WriteLine("  FAIL [" + current + "] threw " + ex.InnerException);
                    }
                    Console.WriteLine((failures == before ? "ok   " : "FAIL ") + current);
                }
            }
            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL PASSED (" + checks + " checks)" : failures + " FAILURE(S) of " + checks + " checks");
            return failures == 0 ? 0 : 1;
        }
    }

    public static class GeomTests
    {
        public static void TestCircleFrom3Points()
        {
            Vec3 c, n;
            double r;
            bool ok = GeomUtil.CircleFrom3Points(new Vec3(15, 2, 3), new Vec3(10, 7, 3), new Vec3(5, 2, 3), out c, out r, out n);
            TestMain.Check(ok, "fit succeeds");
            TestMain.Near(c.X, 10, 1e-9, "cx");
            TestMain.Near(c.Y, 2, 1e-9, "cy");
            TestMain.Near(r, 5, 1e-9, "r");
            TestMain.Near(Math.Abs(n.Z), 1, 1e-9, "normal along Z");
            TestMain.Check(!GeomUtil.CircleFrom3Points(new Vec3(0, 0, 0), new Vec3(1, 1, 1), new Vec3(2, 2, 2), out c, out r, out n), "collinear rejected");
        }

        public static void TestSamplerTolerance()
        {
            Func<double, Vec3> f = delegate (double t) { return new Vec3(t * 100, 20 * Math.Sin(t * Math.PI * 3), 0); };
            List<Vec3> pts = Sampler.Sample(f, 0.01, 8, 14);
            TestMain.Check(pts.Count > 20, "enough samples (" + pts.Count + ")");
            TestMain.Near(pts[0].X, 0, 1e-12, "starts at t=0");
            TestMain.Near(pts[pts.Count - 1].X, 100, 1e-12, "ends at t=1");
            // check chord error against dense evaluation
            double worst = 0;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                double t0 = pts[i].X / 100, t1 = pts[i + 1].X / 100;
                for (int k = 1; k < 10; k++)
                {
                    Vec3 p = f(t0 + (t1 - t0) * k / 10.0);
                    worst = Math.Max(worst, GeomUtil.DistanceToLine(p, pts[i], pts[i + 1]));
                }
            }
            TestMain.Check(worst <= 0.011, "chord error " + worst);
        }

        public static void TestFmtInvariant()
        {
            TestMain.Check(Fmt.N(1234.5, 3) == "1234.5", "no thousands separator, dot decimal");
            TestMain.Check(Fmt.N(-0.00001, 3) == "0", "negative zero dropped");
        }
    }

    public static class JsonTests
    {
        public static void TestRoundTrip()
        {
            var o = new JObj().Add("a", 1.5).Add("s", "x\"y\\z").Add("l", new List<object> { 1.0, "two", true, null })
                .Add("o", new JObj().Add("k", false));
            string text = Json.Write(o);
            var back = (JObj)Json.Parse(text);
            TestMain.Near(back.Num("a", 0), 1.5, 0, "number");
            TestMain.Check(back.Str("s", "") == "x\"y\\z", "escaped string");
            TestMain.Check(back.Arr("l").Count == 4 && back.Arr("l")[3] == null, "array with null");
            TestMain.Check(back.Obj("o").Bool("k", true) == false, "nested object");
        }

        public static void TestCommentsAndWindowsPaths()
        {
            string text = "{\n // comment line\n \"outputFolder\": \"C:\\Laser DXF\\\\\",\n \"n\": 3, // trailing\n}";
            var o = (JObj)Json.Parse(text);
            TestMain.Check(o.Str("outputFolder", "") == "C:\\Laser DXF\\", "single backslash before a letter is kept: " + o.Str("outputFolder", ""));
            TestMain.Near(o.Num("n", 0), 3, 0, "value after comment");
        }

        public static void TestErrorHasLineNumber()
        {
            try
            {
                Json.Parse("{\n\"a\": 1,\n\"b\" 2\n}");
                TestMain.Check(false, "should throw");
            }
            catch (FormatException ex)
            {
                TestMain.Check(ex.Message.Contains("line 3"), "line number in message: " + ex.Message);
            }
        }
    }

    public static class AnalyzerTests
    {
        static Synth TestPlate()
        {
            var s = new Synth(6);
            s.Rectangle(200, 100);
            s.SimpleHole(20, 80, 9);              // 1
            s.Countersunk(60, 50, 6.6, 13, true); // 2: CS opening on top
            s.Counterbore(100, 50, 6.6, 11, 4);   // 3
            s.BlindHole(140, 50, 5, 3);           // 4: does not go through
            s.RectCutout(160, 10, 190, 30);       // 5: slot
            s.SplitHole(30, 20, 5.8);             // 6: circle made of two arcs
            return s;
        }

        static HoleInfo HoleAt(FaceAnalysis a, double x, double y)
        {
            HoleInfo best = null;
            double bd = double.MaxValue;
            foreach (var h in a.Holes)
            {
                double d = h.Center2.DistanceTo(new Vec2(x, y));
                if (d < bd) { bd = d; best = h; }
            }
            return bd < 0.01 ? best : null;
        }

        public static void TestTopFaceBasics()
        {
            Synth s = TestPlate();
            FaceAnalysis a = PlateAnalyzer.Analyze(s.M, s.TopFace, new AnalysisOptions());
            TestMain.Near(a.Thickness, 6, 1e-9, "thickness");
            TestMain.Near(a.Normal.Z, 1, 1e-9, "outward normal is +Z");
            TestMain.Near(a.Frame.Handedness, 1, 1e-9, "right-handed frame");
            TestMain.Near(a.Bounds.MinX, 0, 1e-9, "origin x");
            TestMain.Near(a.Bounds.MinY, 0, 1e-9, "origin y");
            TestMain.Near(a.Bounds.Width, 200, 1e-9, "width along longest edge");
            TestMain.Near(a.Bounds.Height, 100, 1e-9, "height");
            TestMain.Check(a.Outer.Edges.Count == 4, "outer loop has 4 edges");
            TestMain.Check(a.Holes.Count == 6, "6 inner loops, got " + a.Holes.Count);
            TestMain.Check(a.Warnings.Count == 0, "no warnings: " + string.Join(" | ", a.Warnings.ToArray()));
            TestMain.Near(a.NetArea, 200 * 100 - Math.PI * (4.5 * 4.5 + 6.5 * 6.5 + 5.5 * 5.5 + 2.5 * 2.5 + 2.9 * 2.9) - 600, 1.5, "net area");
        }

        /// <summary>Viewed from the top the holes must be where they were modelled,
        /// or rotated 180 degrees (both rectangle long edges tie) - never mirrored.</summary>
        public static void TestTopViewNotMirrored()
        {
            Synth s = TestPlate();
            FaceAnalysis a = PlateAnalyzer.Analyze(s.M, s.TopFace, new AnalysisOptions());
            bool asModelled = HoleAt(a, 20, 80) != null && HoleAt(a, 140, 50) != null;
            bool rotated = HoleAt(a, 180, 20) != null && HoleAt(a, 60, 50) != null && HoleAt(a, 140, 50) != null;
            TestMain.Check(asModelled || rotated, "hole positions match the top view (or its 180 degree rotation)");
        }

        public static void TestBottomViewIsMirrorOfTop()
        {
            Synth s = TestPlate();
            FaceAnalysis a = PlateAnalyzer.Analyze(s.M, s.BottomFace, new AnalysisOptions());
            TestMain.Near(a.Normal.Z, -1, 1e-9, "outward normal is -Z");
            TestMain.Near(a.Frame.Handedness, 1, 1e-9, "still right-handed");
            // Seen from underneath, the hole at modelled (20,80) is at x = 200-20.
            bool v1 = HoleAt(a, 180, 80) != null && HoleAt(a, 170, 20) != null;
            bool v2 = HoleAt(a, 20, 20) != null && HoleAt(a, 30, 80) != null; // same view rotated 180
            TestMain.Check(v1 || v2, "bottom view is the mirror image of the top view");
            TestMain.Check(a.Holes.Count == 5, "blind hole from the top does not appear on the bottom face, got " + a.Holes.Count);
        }

        public static void TestHoleClassification()
        {
            Synth s = TestPlate();
            FaceAnalysis a = PlateAnalyzer.Analyze(s.M, s.TopFace, new AnalysisOptions());
            Func<double, double, HoleInfo> at = delegate (double x, double y)
            {
                return HoleAt(a, x, y) ?? HoleAt(a, 200 - x, 100 - y);
            };

            HoleInfo plain = at(20, 80);
            TestMain.Check(plain != null && plain.IsCircle && plain.Through && !plain.HasCone && !plain.HasStep, "plain through hole");
            TestMain.Near(plain.MinDiameter, 9, 1e-9, "plain bore");

            HoleInfo cs = at(60, 50);
            TestMain.Check(cs != null && cs.Through && cs.HasCone && cs.ConeAtSurface, "countersink detected on this side");
            TestMain.Near(cs.TopDiameter, 13, 1e-9, "CS top diameter");
            TestMain.Near(cs.MinDiameter, 6.6, 1e-9, "CS cut diameter is the bore");

            HoleInfo cb = at(100, 50);
            TestMain.Check(cb != null && cb.Through && cb.HasStep && !cb.HasCone, "counterbore: through with a step");
            TestMain.Near(cb.MinDiameter, 6.6, 1e-9, "CB cut diameter is the bore");

            HoleInfo blind = at(140, 50);
            TestMain.Check(blind != null && !blind.Through, "blind hole is not through");

            HoleInfo slot = at(175, 20);
            TestMain.Check(slot != null && !slot.IsCircle && slot.Through, "rectangular cut-out is through, not a circle");
            TestMain.Near(slot.Bounds.Width, 30, 1e-9, "slot width");

            HoleInfo split = at(30, 20);
            TestMain.Check(split != null && split.IsCircle && split.Through, "two half arcs make a circle");
            TestMain.Near(split.MinDiameter, 5.8, 1e-9, "split hole diameter");
        }

        public static void TestCountersinkFromOtherSide()
        {
            var s = new Synth(6);
            s.Rectangle(100, 50);
            s.Countersunk(50, 25, 9, 18, false); // opens on the bottom
            FaceAnalysis top = PlateAnalyzer.Analyze(s.M, s.TopFace, new AnalysisOptions());
            HoleInfo h = top.Holes[0];
            TestMain.Check(h.Through && h.HasCone && !h.ConeAtSurface, "CS seen from the plain side");
            TestMain.Near(h.TopDiameter, 9, 1e-9, "loop on top is the bore");
            TestMain.Near(h.MinDiameter, 9, 1e-9, "bore");
        }

        public static void TestPickFacePrefersCountersinkSide()
        {
            var s = new Synth(6);
            s.Rectangle(100, 50);
            s.Countersunk(50, 25, 9, 18, false);
            var log = new List<string>();
            int pick = PlateAnalyzer.PickPlateFace(s.M, new AnalysisOptions(), Vec3.UnitZ, log);
            TestMain.Check(pick == s.BottomFace, "picks the face the countersink opens onto");

            var s2 = new Synth(6);
            s2.Rectangle(100, 50);
            s2.SimpleHole(50, 25, 9);
            int pick2 = PlateAnalyzer.PickPlateFace(s2.M, new AnalysisOptions(), Vec3.UnitZ, log);
            TestMain.Check(pick2 == s2.TopFace, "no countersinks: picks the face looking up +Z");
        }

        public static void TestNonPlanarFaceRejected()
        {
            Synth s = TestPlate();
            int cyl = -1;
            foreach (var f in s.M.Faces) if (f.Kind == FaceKind.Cylindrical) { cyl = f.Id; break; }
            try
            {
                PlateAnalyzer.Analyze(s.M, cyl, new AnalysisOptions());
                TestMain.Check(false, "should throw");
            }
            catch (PlateException ex)
            {
                TestMain.Check(ex.Message.Contains("not planar"), "clear message: " + ex.Message);
            }
        }

        public static void TestIrregularPlate()
        {
            Synth s = Synth.Irregular();
            FaceAnalysis a = PlateAnalyzer.Analyze(s.M, s.TopFace, new AnalysisOptions());
            TestMain.Check(a.Outer.Edges.Count == 9, "9 outer edges");
            TestMain.Check(a.Holes.Count == 24, "24 holes, got " + a.Holes.Count);
            TestMain.Near(a.Frame.Handedness, 1, 1e-9, "right-handed");

            // Longest straight edge (the 725 mm left side) now runs along the bottom at y = 0.
            TopoEdge le = s.M.Edge(a.LongestEdgeId);
            TestMain.Near(le.Length, 725, 1e-6, "longest edge picked");
            TestMain.Near(a.Frame.ToLocal(le.Start).Y, 0, 1e-6, "longest edge on y=0 (start)");
            TestMain.Near(a.Frame.ToLocal(le.End).Y, 0, 1e-6, "longest edge on y=0 (end)");
            // Exact bounds include the arc extremes: 805 along X, and the rounded tip reaches 759.64 in Y.
            TestMain.Near(a.Bounds.Width, 805, 1e-6, "bbox along X (incl. corner arcs)");
            TestMain.Near(a.Bounds.Height, 734.6447 + 25, 1e-3, "bbox along Y reaches the tip arc");
            foreach (var h in a.Holes)
            {
                TestMain.Check(h.Through, "hole " + h.Index + " through");
                TestMain.Check(h.Center2.X > 0 && h.Center2.X < a.Bounds.Width && h.Center2.Y > 0 && h.Center2.Y < a.Bounds.Height, "hole " + h.Index + " inside the plate");
            }
            File.WriteAllText(Path.Combine(TestMain.OutDir, "irregular_probe.txt"), ProbeReport.Join(ProbeReport.Summary(s.M, a, "synthetic")) +
                ProbeReport.Join(ProbeReport.FaceEdges(s.M, a)));
        }

        public static void TestWcsHintAlignment()
        {
            Synth s = Synth.Irregular();
            var opt = new AnalysisOptions { XAlign = XAlignMode.Hint, XHint = Vec3.UnitX };
            FaceAnalysis a = PlateAnalyzer.Analyze(s.M, s.TopFace, opt);
            TestMain.Near(a.Frame.XAxis.X, 1, 1e-12, "X follows the hint");
            TestMain.Near(a.Bounds.Width, 759.6447, 1e-3, "width as modelled");
            TestMain.Near(a.Bounds.Height, 805, 1e-6, "height as modelled");
        }

        public static void TestBodyJsonRoundTrip()
        {
            Synth s = TestPlate();
            string text = Json.Write(s.M.ToJson());
            BodyModel back = BodyModel.FromJson((JObj)Json.Parse(text));
            FaceAnalysis a1 = PlateAnalyzer.Analyze(s.M, s.TopFace, new AnalysisOptions());
            FaceAnalysis a2 = PlateAnalyzer.Analyze(back, s.TopFace, new AnalysisOptions());
            TestMain.Check(a1.Holes.Count == a2.Holes.Count, "same hole count");
            TestMain.Near(a2.Bounds.Width, a1.Bounds.Width, 1e-12, "same width");
            TestMain.Near(a2.Holes[1].MinDiameter, a1.Holes[1].MinDiameter, 1e-12, "same bore");
            File.WriteAllText(Path.Combine(TestMain.OutDir, "testplate_body.json"), text);
        }
    }
}
