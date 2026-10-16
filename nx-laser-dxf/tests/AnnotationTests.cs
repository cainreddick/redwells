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

namespace LaserDxf.Tests
{
    using System.Collections.Generic;
    using LaserDxf.Core;

    public static class PickTests
    {
        public static void TestEtchAtPickedPoint()
        {
            var s = new Synth(6);
            s.Rectangle(200, 100);
            s.SimpleHole(30, 50, 5.8);
            s.M.Attributes = IdentityTests.Attrs();
            var c = new LaserConfig { EtchPlacement = "Pick" };
            // model point (150, 30) on the top face -> same in DXF coordinates for this plate
            ExportRun r = ExportPipeline.Prepare(s.M, s.TopFace, c, c.ToAnalysisOptions(), "t", new Vec3(150, 30, 6));
            Vec2 centre = r.Etch.Box.Center;
            bool asModelled = centre.DistanceTo(new Vec2(150, 30)) < 1e-6;
            bool rotated = centre.DistanceTo(new Vec2(50, 70)) < 1e-6; // both long edges tie: may be rotated 180
            TestMain.Check(asModelled || rotated, "etch centred on the picked point: " + centre);

            ExportRun off = ExportPipeline.Prepare(s.M, s.TopFace, c, c.ToAnalysisOptions(), "t", new Vec3(500, 30, 6));
            TestMain.Check(off.Etch != null && off.Etch.Clear, "off-plate pick falls back to automatic");
            bool warned = false;
            foreach (string w in off.Warnings) if (w.Contains("not on the plate")) warned = true;
            TestMain.Check(warned, "warning for an off-plate pick");
        }

        public static void TestPlacementValidated()
        {
            var w = new List<string>();
            LaserConfig c = LaserConfig.Parse("{ \"etch\": { \"placement\": \"Manual\" } }", "t", w);
            TestMain.Check(c.EtchPlacement == "Auto" && w.Count == 1, "bad placement reported");
        }
    }
}

namespace LaserDxf.Tests
{
    using System.Collections.Generic;
    using LaserDxf.Core;

    public static class HoleFeatureTests
    {
        static FeatureInfo ThreadedHole(int id)
        {
            var f = new FeatureInfo { Id = id, Name = "Hole(12)", ClassName = "HolePackage" };
            f.Props.Add(new KeyValuePair<string, string>("Types", "ThreadedHole"));
            f.Props.Add(new KeyValuePair<string, string>("ThreadSize", "\"M8 x 1.25\""));
            return f;
        }

        /// <summary>The bug from the field: holes patterned from a Hole feature belong to the
        /// Pattern feature, so the Hole data was never consulted and Ø6.8 matched no table row.</summary>
        public static void TestPatternedHoleFeatureIsFollowed()
        {
            var s = new Synth(6);
            s.Rectangle(200, 100);
            s.SimpleHole(30, 50, 6.8);   // seed, made by the Hole feature
            int seedCyl = s.M.Faces.Count;
            s.SimpleHole(60, 50, 6.8);   // instance, made by the pattern
            int instCyl = s.M.Faces.Count;
            s.SimpleHole(90, 50, 9);     // plain clearance hole from an extrude
            int extrCyl = s.M.Faces.Count;

            FeatureInfo hole = ThreadedHole(1);
            hole.FaceIds.Add(seedCyl);
            var pattern = new FeatureInfo { Id = 2, Name = "Pattern Feature [Linear](13)", ClassName = "PatternFeature" };
            pattern.FaceIds.Add(instCyl);
            pattern.ParentIds.Add(1);
            var extrude = new FeatureInfo { Id = 3, Name = "Extrude(5)", ClassName = "Extrude" };
            extrude.FaceIds.Add(extrCyl);
            s.M.Features.AddRange(new[] { hole, pattern, extrude });

            s.M.Attributes = IdentityTests.Attrs();
            var c = new LaserConfig();
            ExportRun r = ExportPipeline.Prepare(s.M, s.TopFace, c, c.ToAnalysisOptions(), "t");
            var labels = new Dictionary<double, string>();
            foreach (var h in r.Profile.Holes) labels[System.Math.Round(h.Hole.Center.X)] = h.Label;
            TestMain.Check(labels[30] == "M8", "seed hole labelled from the Hole feature");
            TestMain.Check(labels[60] == "M8", "patterned instance labelled through its parent Hole feature");
            TestMain.Check(labels[90] == "", "plain hole unlabelled");

            string report = string.Join("\n", ExportReport.Summary(r).ToArray());
            TestMain.Check(report.Contains("Not labelled") && report.Contains("made by Extrude(5)") && report.Contains("dia 9 is not in the label table"),
                           "report says why the plain hole has no label:\n" + report);
        }

        public static void TestUnreadableHoleFeatureIsReported()
        {
            var s = new Synth(6);
            s.Rectangle(100, 50);
            s.SimpleHole(30, 25, 6.8);
            var f = new FeatureInfo { Id = 1, Name = "Hole(3)", ClassName = "HolePackage" };
            f.FaceIds.Add(s.M.Faces.Count);
            s.M.Features.Add(f);
            s.M.Attributes = IdentityTests.Attrs();
            var c = new LaserConfig();
            ExportRun r = ExportPipeline.Prepare(s.M, s.TopFace, c, c.ToAnalysisOptions(), "t");
            TestMain.Check(r.Profile.Holes[0].Note.Contains("settings could not be read"), "note: " + r.Profile.Holes[0].Note);
        }

        public static void TestAlternativeTypePropertyName()
        {
            var f = new FeatureInfo { Name = "Hole(1)", ClassName = "HolePackage" };
            f.Props.Add(new KeyValuePair<string, string>("HolePackageType", "ThreadedHole"));
            f.Props.Add(new KeyValuePair<string, string>("ThreadSize", "\"M10 x 1.5\""));
            HoleFeatureData d = HoleClassifier.ParseHoleFeature(f);
            TestMain.Check(d.IsThreaded && d.ThreadSize == "M10 x 1.5", "type found under another property name");
        }
    }
}

namespace LaserDxf.Tests
{
    using System;
    using LaserDxf.Core;

    public static class EditorTests
    {
        static ExportRun Plate()
        {
            var s = new Synth(6);
            s.Rectangle(200, 100);
            s.SimpleHole(30, 50, 5.8);
            s.SimpleHole(70, 50, 5.5);
            s.M.Attributes = IdentityTests.Attrs();
            var c = new LaserConfig();
            return ExportPipeline.Prepare(s.M, s.TopFace, c, c.ToAnalysisOptions(), "t");
        }

        public static void TestHitMoveAndProblems()
        {
            ExportRun r = Plate();
            var ed = new LayoutEditor(r);
            TestMain.Check(ed.Items.Count == 3 && ed.Problems().Count == 0, "2 labels + part number, all clear initially");
            PlacedText etch = ed.HitTest(r.Etch.Box.Center, 0.1);
            TestMain.Check(etch == r.Etch, "hit test finds the part number");
            TestMain.Check(ed.HitTest(new Vec2(-50, -50), 0.1) == null, "empty space hits nothing");

            // Drag the part number across the plate edge: flagged red.
            Vec2 c = etch.Box.Center;
            ed.Move(etch, new Vec2(200 - etch.Box.MaxX + 5, 0));
            TestMain.Check(ed.Status(etch) == ItemStatus.Overlap, "part number over the cut line is flagged: " + ed.Describe(etch));
            TestMain.Check(ed.Problems().Count == 1, "one problem");

            // Back inside, near a hole: tight / overlap, then somewhere clear: OK.
            ed.Move(etch, c - etch.Box.Center);
            TestMain.Check(ed.Status(etch) == ItemStatus.Ok, "moved back: OK");

            // The edit reaches the DXF.
            ed.Move(etch, new Vec2(-10, 0));
            ed.Apply();
            TestMain.Check(r.EditSummary.Contains("1 item(s) moved"), r.EditSummary);
            TestMain.Near(r.Etch.Origin.X, etch.Origin.X, 0, "same object");
            int inNew = 0, leftBehind = 0;
            Rect2 vacated = new Rect2(etch.Box.MaxX + 0.01, etch.Box.MinY, etch.Box.MaxX + 10, etch.Box.MaxY);
            foreach (var e in r.Profile.Doc.Entities)
            {
                var pl = e as DxfPolyline;
                if (pl == null || pl.Layer != "ETCH") continue;
                bool all = true;
                foreach (var p in pl.Points)
                {
                    if (Layout2.PointRect(p, etch.Box) > 1e-6) all = false;
                    if (Layout2.PointRect(p, vacated) == 0) leftBehind++;
                }
                if (all) inNew++;
            }
            TestMain.Check(inNew == StrokeFont.Render(etch.Text, new Vec2(0, 0), etch.Height, 0).Count, "every part-number stroke at the new place (" + inNew + ")");
            TestMain.Check(leftBehind == 0, "nothing left at the old place");
        }

        public static void TestRotateEditDeleteReset()
        {
            ExportRun r = Plate();
            var ed = new LayoutEditor(r);
            PlacedText etch = r.Etch;
            Vec2 c = etch.Box.Center;
            double w = etch.Box.Width, h = etch.Box.Height;
            ed.Rotate90(etch);
            TestMain.Near(etch.Box.Center.X, c.X, 1e-9, "rotates about its centre (x)");
            TestMain.Near(etch.Box.Center.Y, c.Y, 1e-9, "rotates about its centre (y)");
            TestMain.Near(etch.Box.Width, h, 1e-9, "box turned: width = old height");
            TestMain.Near(etch.Box.Height, w, 1e-9, "box turned: height = old width");
            foreach (var st in StrokeFont.Render(etch.Text, etch.Origin, etch.Height, etch.RotationDeg))
                foreach (var p in st)
                    TestMain.Check(Layout2.PointRect(p, etch.Box) < 1e-6, "rendered strokes stay in the rotated box");
            ed.Rotate90(etch); ed.Rotate90(etch); ed.Rotate90(etch);
            TestMain.Near(etch.RotationDeg, 0, 1e-12, "four turns = back to horizontal");
            TestMain.Near(etch.Box.Center.X, c.X, 1e-9, "still centred");

            PlacedText label = r.Labels[0];
            ed.SetText(label, "R6.1");
            TestMain.Check(label.Text == "R6.1" && Math.Abs(label.Box.Width - StrokeFont.Measure("R6.1", label.Height)) < 1e-9, "text edited, box resized");
            ed.Delete(r.Labels[1]);
            ed.Apply();
            int etchEntities = 0;
            foreach (var e in r.Profile.Doc.Entities) if (e.Layer == "ETCH") etchEntities++;
            int expected = StrokeFont.Render("R6.1", new Vec2(0, 0), 3, 0).Count + StrokeFont.Render(etch.Text, new Vec2(0, 0), 5, 0).Count;
            TestMain.Check(etchEntities == expected, "deleted label left out of the DXF (" + etchEntities + " vs " + expected + ")");

            ed.Reset();
            TestMain.Check(!r.Labels[1].Deleted && label.Text == "R6" && Math.Abs(etch.Box.Center.X - c.X) < 1e-9, "reset restores everything");
        }
    }
}

namespace LaserDxf.Tests
{
    using System;
    using LaserDxf.Core;

    public static class AddLabelTests
    {
        public static void TestAddLabelsByHand()
        {
            var s = new Synth(6);
            s.Rectangle(200, 100);
            s.SimpleHole(30, 50, 5.8);   // R6 by table
            s.SimpleHole(80, 50, 6.0);   // the field case: real-size holes missing from the table
            s.SimpleHole(110, 50, 6.0);
            s.SimpleHole(140, 50, 6.8);
            s.M.Attributes = IdentityTests.Attrs();
            var c = new LaserConfig();
            c.LabelTable.Add(new LabelRule { Label = "CB", Diameter = 20, CutDiameter = double.NaN });
            c.LabelTable[1].CutDiameter = 6.2; // M8 row: laser cut dia 6.2
            ExportRun r = ExportPipeline.Prepare(s.M, s.TopFace, c, c.ToAnalysisOptions(), "t");
            var ed = new LayoutEditor(r);
            TestMain.Check(ed.Unlabelled().Count == 3, "3 unlabelled round holes");

            CutHole h80 = ed.HitHole(new Vec2(80 + 2.9, 50), 0.5) ?? ed.HitHole(new Vec2(200 - 80 - 2.9, 50), 0.5);
            TestMain.Check(h80 != null, "hit test finds the hole by clicking its edge");
            TestMain.Check(ed.DescribeHole(h80).Contains("no label") && ed.DescribeHole(h80).Contains("not in the label table"), ed.DescribeHole(h80));
            TestMain.Check(ed.SameSizeUnlabelled(h80).Count == 1, "one other dia 6 hole");
            TestMain.Check(ed.LabelChoices().Contains("R6") && ed.LabelChoices().Contains("M8") && ed.LabelChoices().Contains("CS"), "choices from the table");

            PlacedText t = ed.AddLabel(h80, "R6");
            foreach (CutHole o in ed.SameSizeUnlabelled(h80)) ed.AddLabel(o, "R6");
            TestMain.Check(t != null && t.Clear && t.Position == "right", "added label placed like the automatic ones");
            TestMain.Check(ed.Unlabelled().Count == 1 && ed.Problems().Count == 0, "only the 6.8 hole left, no clashes");

            CutHole h68 = ed.Unlabelled()[0];
            ed.AddLabel(h68, "M8");
            TestMain.Near(h68.Circle.Radius, 3.1, 1e-12, "M8's laser cut diameter applied to the hand-labelled hole");

            ed.Apply();
            TestMain.Check(r.EditSummary.StartsWith("3 label(s) added"), r.EditSummary);
            string report = string.Join("\n", ExportReport.Summary(r).ToArray());
            TestMain.Check(report.Contains("Labelled     : 4 of 4") && report.Contains("added by hand"), report);
            TestMain.Check(r.DxfText.Contains("\r\n 40\r\n3.1\r\n"), "cut diameter 6.2 written");

            ed.Reset();
            TestMain.Check(ed.Unlabelled().Count == 3 && Math.Abs(h68.Circle.Radius - 3.4) < 1e-12, "reset removes added labels and restores the diameter");
        }
    }
}
