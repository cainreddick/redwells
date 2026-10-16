// Built-in single-stroke font for laser etching (vectors, not filled glyphs).
// Glyphs are drawn on a grid where the cap height is 6 units; most glyphs are
// 4 units wide. Each glyph is a list of polylines: "x,y x,y ...|x,y x,y ...".
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    public static class StrokeFont
    {
        const double CapUnits = 6.0;
        const double SpacingUnits = 1.5;

        sealed class Glyph
        {
            public double Width;
            public List<List<Vec2>> Strokes = new List<List<Vec2>>();
        }

        static Dictionary<char, Glyph> glyphs;

        static readonly string[] Defs =
        {
            "0|4|1,0 0,1 0,5 1,6 3,6 4,5 4,1 3,0 1,0|0.6,1 3.4,5",
            "1|4|1,5 2,6 2,0|1,0 3,0",
            "2|4|0,5 1,6 3,6 4,5 4,4 0,0 4,0",
            "3|4|0,5 1,6 3,6 4,5 4,4 3,3 4,2 4,1 3,0 1,0 0,1|1.5,3 3,3",
            "4|4|3,0 3,6 0,2 4,2",
            "5|4|4,6 0,6 0,3.5 3,3.5 4,2.5 4,1 3,0 1,0 0,1",
            "6|4|3.5,6 1,6 0,5 0,1 1,0 3,0 4,1 4,2.5 3,3.5 0,3.5",
            "7|4|0,6 4,6 1.5,0",
            "8|4|1,3 0,4 0,5 1,6 3,6 4,5 4,4 3,3 1,3 0,2 0,1 1,0 3,0 4,1 4,2 3,3",
            "9|4|4,3 1,3 0,4 0,5 1,6 3,6 4,5 4,1 3,0 0.5,0",
            "A|4|0,0 0,4 2,6 4,4 4,0|0,2.5 4,2.5",
            "B|4|0,0 0,6 3,6 4,5 4,4 3,3 0,3|3,3 4,2 4,1 3,0 0,0",
            "C|4|4,5 3,6 1,6 0,5 0,1 1,0 3,0 4,1",
            "D|4|0,0 0,6 3,6 4,5 4,1 3,0 0,0",
            "E|4|4,6 0,6 0,0 4,0|0,3 3,3",
            "F|4|4,6 0,6 0,0|0,3 3,3",
            "G|4|4,5 3,6 1,6 0,5 0,1 1,0 3,0 4,1 4,3 2,3",
            "H|4|0,0 0,6|4,0 4,6|0,3 4,3",
            "I|2|0,6 2,6|1,6 1,0|0,0 2,0",
            "J|4|4,6 4,1 3,0 1,0 0,1",
            "K|4|0,0 0,6|4,6 0,2|1.33,3.33 4,0",
            "L|4|0,6 0,0 4,0",
            "M|4|0,0 0,6 2,3 4,6 4,0",
            "N|4|0,0 0,6 4,0 4,6",
            "O|4|1,0 0,1 0,5 1,6 3,6 4,5 4,1 3,0 1,0",
            "P|4|0,0 0,6 3,6 4,5 4,4 3,3 0,3",
            "Q|4|1,0 0,1 0,5 1,6 3,6 4,5 4,1 3,0 1,0|2.5,1.5 4,0",
            "R|4|0,0 0,6 3,6 4,5 4,4 3,3 0,3|2,3 4,0",
            "S|4|4,5 3,6 1,6 0,5 0,4 1,3 3,3 4,2 4,1 3,0 1,0 0,1",
            "T|4|0,6 4,6|2,6 2,0",
            "U|4|0,6 0,1 1,0 3,0 4,1 4,6",
            "V|4|0,6 2,0 4,6",
            "W|4|0,6 1,0 2,4 3,0 4,6",
            "X|4|0,0 4,6|0,6 4,0",
            "Y|4|0,6 2,3 4,6|2,3 2,0",
            "Z|4|0,6 4,6 0,0 4,0",
            "-|3|0,3 3,3",
            "_|4|0,0 4,0",
            ".|1|0,0 1,0 1,1 0,1 0,0",
            ",|1|1,1 0,-1",
            ":|1|0,1 1,1 1,2 0,2 0,1|0,4 1,4 1,5 0,5 0,4",
            "/|4|0,0 4,6",
            "(|2|2,6 1,5 1,1 2,0",
            ")|2|0,6 1,5 1,1 0,0",
            "+|4|2,1 2,5|0,3 4,3",
            "=|4|0,2 4,2|0,4 4,4",
            "#|4|1,0 1,6|3,0 3,6|0,2 4,2|0,4 4,4",
            "Ø|4|1,0 0,1 0,5 1,6 3,6 4,5 4,1 3,0 1,0|0,0 4,6",
            "?|4|0,5 1,6 3,6 4,5 4,4 2,2.5 2,1.5|2,0.5 2,0",
            " |3|",
        };

        static void Init()
        {
            if (glyphs != null) return;
            var g = new Dictionary<char, Glyph>();
            foreach (string def in Defs)
            {
                // "<char>|<width>|<strokes>" - the char itself may be '|' free; split from the left.
                char ch = def[0];
                string rest = def.Substring(2);
                int bar = rest.IndexOf('|');
                var glyph = new Glyph { Width = double.Parse(rest.Substring(0, bar), CultureInfo.InvariantCulture) };
                foreach (string stroke in rest.Substring(bar + 1).Split('|'))
                {
                    var pts = new List<Vec2>();
                    foreach (string xy in stroke.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] p = xy.Split(',');
                        pts.Add(new Vec2(double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture)));
                    }
                    if (pts.Count >= 2) glyph.Strokes.Add(pts);
                }
                g[ch] = glyph;
            }
            glyphs = g;
        }

        static Glyph Get(char c)
        {
            Init();
            Glyph g;
            if (glyphs.TryGetValue(char.ToUpperInvariant(c), out g)) return g;
            return glyphs['?'];
        }

        /// <summary>Characters the font can't draw (they come out as '?').</summary>
        public static string Unsupported(string text)
        {
            Init();
            string bad = "";
            foreach (char c in text)
                if (!glyphs.ContainsKey(char.ToUpperInvariant(c)) && bad.IndexOf(c) < 0) bad += c;
            return bad;
        }

        /// <summary>Width of the text at the given cap height.</summary>
        public static double Measure(string text, double height)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            double units = 0;
            foreach (char c in text) units += Get(c).Width + SpacingUnits;
            units -= SpacingUnits;
            return units * height / CapUnits;
        }

        /// <summary>Strokes for the text. origin = left end of the baseline; rotation in degrees CCW.</summary>
        public static List<List<Vec2>> Render(string text, Vec2 origin, double height, double rotationDeg)
        {
            var result = new List<List<Vec2>>();
            double s = height / CapUnits;
            double r = rotationDeg * Math.PI / 180;
            double cos = Math.Cos(r), sin = Math.Sin(r);
            double x = 0;
            foreach (char c in text)
            {
                Glyph g = Get(c);
                foreach (var stroke in g.Strokes)
                {
                    var pts = new List<Vec2>(stroke.Count);
                    foreach (var p in stroke)
                    {
                        double lx = (x + p.X) * s, ly = p.Y * s;
                        pts.Add(new Vec2(origin.X + lx * cos - ly * sin, origin.Y + lx * sin + ly * cos));
                    }
                    result.Add(pts);
                }
                x += g.Width + SpacingUnits;
            }
            return result;
        }
    }
}
