// Steps 3-5 tests: stroke font, layout geometry, hole classification, label and etch placement.
namespace LaserDxf.Tests
{
    using System;
    using System.Collections.Generic;
    using LaserDxf.Core;

    public static class FontTests
    {
        public static void TestMeasureAndBounds()
        {
            double w = StrokeFont.Measure("M8", 3);
            TestMain.Near(w, (4 + 1.5 + 4) * 0.5, 1e-12, "width of M8 at 3 mm");
            foreach (string text in new[] { "S180-06068-47020-A", "R6", "CS", "0123456789", "ABCDEFGHIJKLMNOPQRSTUVWXYZ-_./()+=#:" })
            {
                double tw = StrokeFont.Measure(text, 5);
                foreach (var stroke in StrokeFont.Render(text, new Vec2(10, 20), 5, 0))
                    foreach (var p in stroke)
                        TestMain.Check(p.X >= 10 - 1e-9 && p.X <= 10 + tw + 1e-9 && p.Y >= 20 - 5.0 / 6 - 1e-9 && p.Y <= 25 + 1e-9,
                                       "\"" + text + "\" stays in its box: " + p);
            }
            TestMain.Check(StrokeFont.Unsupported("ab-1") == "", "lower case maps to capitals");
            TestMain.Check(StrokeFont.Unsupported("A&B") == "&", "unsupported reported");
        }

        public static void TestRotation90()
        {
            var strokes = StrokeFont.Render("HI", new Vec2(0, 0), 6, 90);
            foreach (var st in strokes)
                foreach (var p in st)
                    TestMain.Check(p.X <= 1e-9 && p.X >= -6 - 1e-9 && p.Y >= -1e-9, "rotated text goes up, glyph tops point -X: " + p);
        }
    }

    public static class LayoutTests
    {
        public static void TestDistances()
        {
            var r = new Rect2(0, 0, 10, 5);
            TestMain.Near(Layout2.PointRect(new Vec2(13, 9), r), 5, 1e-12, "point to rect");
            TestMain.Near(Layout2.RectRect(r, new Rect2(12, 0, 20, 5)), 2, 1e-12, "rect to rect");
            TestMain.Near(Layout2.SegmentRect(new Vec2(-5, -5), new Vec2(15, 10), r), 0, 1e-12, "crossing segment");
            TestMain.Near(Layout2.SegmentRect(new Vec2(0, 8), new Vec2(10, 8), r), 3, 1e-12, "parallel segment");
            var square = new List<Vec2> { new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10) };
            TestMain.Check(Layout2.PointInPolygon(new Vec2(5, 5), square) && !Layout2.PointInPolygon(new Vec2(15, 5), square), "point in polygon");
        }
    }

    public static class ClassifyTests
    {
        static ExportRun Run(Synth s, LaserConfig c)
        {
            s.M.Attributes = IdentityTests.Attrs();
            return ExportPipeline.Prepare(s.M, s.TopFace, c, c.ToAnalysisOptions(), "test");
        }

        static CutHole At(ExportRun r, double x, double y)
        {
            foreach (var h in r.Profile.Holes)
                if (h.Hole.Center.DistanceTo(new Vec3(x, y, h.Hole.Center.Z)) < 1e-6) return h;
            return null;
        }

        public static void TestTableAndGeometry()
        {
            var s = new Synth(6);
            s.Rectangle(200, 100);
            s.SimpleHole(30, 50, 5.8);           // R6 by table
            s.SimpleHole(70, 50, 5.53);          // M8 by table (within 0.05)
            s.SimpleHole(110, 50, 9);            // plain clearance: no label
            s.Countersunk(150, 50, 9, 18, true); // CS by geometry
            ExportRun r = Run(s, new LaserConfig());
            TestMain.Check(At(r, 30, 50).Label == "R6", "R6");
            TestMain.Check(At(r, 70, 50).Label == "M8", "M8 within tolerance");
            TestMain.Check(At(r, 110, 50).Label == "", "plain hole unlabelled");
            TestMain.Check(At(r, 150, 50).Label == "CS" && At(r, 150, 50).LabelSource == "countersink geometry", "CS from geometry");
            TestMain.Check(r.Labels.Count == 3, "3 labels placed");
            foreach (var t in r.Labels) TestMain.Check(t.Clear && t.Position == "right", t.Text + " placed right by default: " + t.Position);
        }

        public static void TestCutDiameterOverride()
        {
            var s = new Synth(6);
            s.Rectangle(100, 50);
            s.SimpleHole(30, 25, 5.5);
            var c = new LaserConfig();
            c.LabelTable[1].CutDiameter = 6.0;
            ExportRun r = Run(s, c);
            CutHole h = At(r, 30, 25);
            TestMain.Check(h.Label == "M8", "M8");
            TestMain.Near(h.Circle.Radius, 3.0, 1e-12, "circle written at the laser cut diameter");
            TestMain.Check(r.DxfText.Contains("\r\n 40\r\n3.0\r\n"), "radius 3 in the DXF");
        }

        public static void TestHoleFeatureThread()
        {
            var f = new FeatureInfo { Id = 7, Name = "Hole(3)", ClassName = "HolePackage" };
            f.Props.Add(new KeyValuePair<string, string>("GeneralHoleForm", "Simple"));
            f.Props.Add(new KeyValuePair<string, string>("ScrewClearanceHoleForm", "Countersunk"));
            f.Props.Add(new KeyValuePair<string, string>("ThreadSize", "\"M8 x 1.25\""));
            f.Props.Add(new KeyValuePair<string, string>("Types", "ThreadedHole"));
            HoleFeatureData d = HoleClassifier.ParseHoleFeature(f);
            TestMain.Check(d.IsThreaded && d.ThreadSize == "M8 x 1.25" && !d.IsCountersunk, "threaded; inactive countersunk form ignored");

            f.Props[3] = new KeyValuePair<string, string>("Types", "ScrewClearanceHole");
            d = HoleClassifier.ParseHoleFeature(f);
            TestMain.Check(!d.IsThreaded && d.IsCountersunk, "screw clearance countersunk");

            TestMain.Check(HoleClassifier.ShortThread("M8 x 1.25") == "M8", "M8 x 1.25");
            TestMain.Check(HoleClassifier.ShortThread("M10x1.5") == "M10", "M10x1.5");
            TestMain.Check(HoleClassifier.ShortThread("1/4-20 UNC") == "1/4-20", "UNC");

            // Wire the feature to a hole: the feature wins over the table (5.5 would be "M8" anyway, use 6.8)
            var s = new Synth(6);
            s.Rectangle(100, 50);
            s.SimpleHole(30, 25, 6.8);
            f.Props[3] = new KeyValuePair<string, string>("Types", "ThreadedHole");
            f.FaceIds.Add(s.M.Faces[s.M.Faces.Count - 1].Id); // the hole's cylinder
            s.M.Features.Add(f);
            ExportRun r = Run(s, new LaserConfig());
            CutHole h = At(r, 30, 25);
            TestMain.Check(h.Label == "M8" && h.LabelSource.StartsWith("hole feature"), "label from the Hole feature: " + h.LabelSource);
            var c = new LaserConfig { UseHoleFeatures = false };
            TestMain.Check(At(Run(s, c), 30, 25).Label == "", "feature data can be switched off");
        }

        public static void TestLabelMovesAwayFromEdge()
        {
            var s = new Synth(6);
            s.Rectangle(100, 50);
            s.SimpleHole(95, 25, 5.8);   // right edge 5 mm away: label must not go right
            s.SimpleHole(50, 47, 5.8);   // near the top edge
            ExportRun r = Run(s, new LaserConfig());
            foreach (var t in r.Labels)
            {
                TestMain.Check(t.Clear, "clear spot found for hole at " + t.Hole.Center);
                TestMain.Check(t.Box.MinX >= 2 - 1e-9 && t.Box.MaxX <= 98 + 1e-9 && t.Box.MinY >= 2 - 1e-9 && t.Box.MaxY <= 48 + 1e-9, "label inside plate with edge clearance: " + t.Box);
            }
            TestMain.Check(r.Labels[1].Position != "right", "edge hole label does not go right (next in order: above): " + r.Labels[1].Position);
        }

        public static void TestCrowdedLabelsDoNotOverlap()
        {
            var s = Synth.Irregular();
            s.SimpleHole(100, 300, 5.8);
            s.SimpleHole(112, 300, 5.8); // right of the previous hole, where its label would go
            ExportRun r = Run(s, new LaserConfig());
            for (int i = 0; i < r.Labels.Count; i++)
                for (int j = i + 1; j < r.Labels.Count; j++)
                    TestMain.Check(Layout2.RectRect(r.Labels[i].Box, r.Labels[j].Box) > 0, "labels " + i + " and " + j + " don't overlap");
            foreach (var t in r.Labels)
                foreach (var h in r.Profile.Holes)
                    if (h.Circle != null)
                        TestMain.Check(Layout2.PointRect(h.Circle.Center, t.Box) >= h.Circle.Radius, "label " + t.Text + " clear of hole at " + h.Center);
        }

        public static void TestEtchPlacement()
        {
            Synth s = Synth.Irregular();
            ExportRun r = Run(s, new LaserConfig());
            TestMain.Check(r.Etch != null && r.Etch.Clear && r.Etch.RotationDeg == 0 && r.Etch.Height == 5, "horizontal, full size, clear");
            foreach (var t in r.Labels) TestMain.Check(Layout2.RectRect(t.Box, r.Etch.Box) >= 3 - 1e-6, "etch away from labels");
            TestMain.Check(r.Etch.Clearance >= 3, "clearance " + r.Etch.Clearance);
        }

        public static void TestEtchRotatesOnNarrowPlate()
        {
            var s = new Synth(6);
            s.Rectangle(300, 14); // 14 mm tall: 5 mm text fits horizontally? width ~ 95 mm: yes. Make holes block it.
            for (int x = 20; x <= 280; x += 20) s.SimpleHole(x, 7, 6);
            ExportRun r = Run(s, new LaserConfig());
            TestMain.Check(r.Etch != null, "etched");
            TestMain.Check(!r.Etch.Clear, "no clear spot on a strip full of holes -> warning");
            bool warned = false;
            foreach (string w in r.Warnings) if (w.Contains("part number")) warned = true;
            TestMain.Check(warned, "warning logged");

            var s2 = new Synth(6);
            s2.Rectangle(40, 200); // tall narrow plate: text only fits vertically
            ExportRun r2 = Run(s2, new LaserConfig());
            // The longest edge (200) becomes X, so the plate is 200 wide and 40 high: horizontal fits.
            TestMain.Check(r2.Etch.Clear && r2.Etch.RotationDeg == 0, "fits horizontally after alignment");
            var c = new LaserConfig { XAlign = XAlignMode.Hint };
            s2.M.Attributes = IdentityTests.Attrs();
            ExportRun r3 = ExportPipeline.Prepare(s2.M, s2.TopFace, c, c.ToAnalysisOptions(), "t");
            TestMain.Check(r3.Etch.Clear && r3.Etch.RotationDeg == 90, "40 mm wide (WCS): rotated 90 degrees, got " + r3.Etch.Position);
        }
    }
}
