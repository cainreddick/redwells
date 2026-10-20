// PNG and JPEG images for the PDF writer (the company logo on the instruction sheet).
// JPEGs go in as they are; PNGs are decoded (all colour types, 1-16 bit, not interlaced)
// and re-compressed as RGB or grey plus an alpha soft mask, using DeflateStream from System.dll.
namespace LaserDxf.Core
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Text;

    public sealed class PdfImage
    {
        public int Width, Height;
        public string ColorSpace = "/DeviceRGB";
        public string Filter = "/FlateDecode";
        public string Decode;          // e.g. inverted Adobe CMYK JPEGs
        public byte[] Data;            // the image stream, already encoded with Filter
        public byte[] Alpha;           // zlib-compressed 8-bit soft mask, or null when opaque

        /// <summary>Loads a PNG or JPEG. Throws FormatException with a readable reason otherwise.</summary>
        public static PdfImage Load(byte[] bytes)
        {
            if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G') return FromPng(bytes);
            if (bytes.Length > 4 && bytes[0] == 0xFF && bytes[1] == 0xD8) return FromJpeg(bytes);
            throw new FormatException("not a PNG or JPEG file");
        }

        public double AspectRatio { get { return Height == 0 ? 1 : (double)Width / Height; } }

        // ------------------------------------------------------------ JPEG

        static PdfImage FromJpeg(byte[] b)
        {
            int i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF) throw new FormatException("damaged JPEG (no marker at byte " + i + ")");
                int marker = b[i + 1];
                if (marker == 0xFF) { i++; continue; }
                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }
                bool sof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (sof)
                {
                    var img = new PdfImage { Filter = "/DCTDecode", Data = b };
                    img.Height = (b[i + 5] << 8) | b[i + 6];
                    img.Width = (b[i + 7] << 8) | b[i + 8];
                    int comps = b[i + 9];
                    if (comps == 1) img.ColorSpace = "/DeviceGray";
                    else if (comps == 3) img.ColorSpace = "/DeviceRGB";
                    else if (comps == 4) { img.ColorSpace = "/DeviceCMYK"; img.Decode = "[1 0 1 0 1 0 1 0]"; }
                    else throw new FormatException("JPEG with " + comps + " colour channels");
                    return img;
                }
                i += 2 + ((b[i + 2] << 8) | b[i + 3]);
            }
            throw new FormatException("JPEG has no image header");
        }

        // ------------------------------------------------------------ PNG

        static PdfImage FromPng(byte[] b)
        {
            int w, h, channels;
            byte[] colour, alpha;
            DecodePng(b, out w, out h, out channels, out colour, out alpha);
            var img = new PdfImage { Width = w, Height = h, ColorSpace = channels == 1 ? "/DeviceGray" : "/DeviceRGB" };
            img.Data = Zlib(colour);
            if (alpha != null)
            {
                bool opaque = true;
                foreach (byte a in alpha) if (a != 255) { opaque = false; break; }
                if (!opaque) img.Alpha = Zlib(alpha);
            }
            return img;
        }

        static int BE32(byte[] b, int i) { return (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]; }

        /// <summary>Decodes a PNG to 8-bit samples: colour has 1 (grey) or 3 (RGB) channels per pixel,
        /// alpha is one byte per pixel or null if the PNG has no transparency.</summary>
        public static void DecodePng(byte[] b, out int w, out int h, out int channels, out byte[] colour, out byte[] alpha)
        {
            w = h = 0;
            int depth = 0, type = -1, interlace = 0;
            byte[] plte = null, trns = null;
            var idat = new MemoryStream();
            int pos = 8;
            while (pos + 8 <= b.Length)
            {
                int len = BE32(b, pos);
                string chunk = Encoding.ASCII.GetString(b, pos + 4, 4);
                int d = pos + 8;
                if (len < 0 || d + len > b.Length) throw new FormatException("damaged PNG (chunk " + chunk + ")");
                if (chunk == "IHDR")
                {
                    w = BE32(b, d); h = BE32(b, d + 4);
                    depth = b[d + 8]; type = b[d + 9]; interlace = b[d + 12];
                }
                else if (chunk == "PLTE") { plte = new byte[len]; Array.Copy(b, d, plte, 0, len); }
                else if (chunk == "tRNS") { trns = new byte[len]; Array.Copy(b, d, trns, 0, len); }
                else if (chunk == "IDAT") idat.Write(b, d, len);
                else if (chunk == "IEND") break;
                pos = d + len + 4;
            }
            if (w <= 0 || h <= 0) throw new FormatException("PNG has no image header");
            if (interlace != 0) throw new FormatException("interlaced PNG - save it again without interlacing");
            int samples = type == 0 ? 1 : type == 2 ? 3 : type == 3 ? 1 : type == 4 ? 2 : type == 6 ? 4 : 0;
            if (samples == 0) throw new FormatException("PNG colour type " + type);
            if (type == 3 && plte == null) throw new FormatException("palette PNG without a palette");

            byte[] raw = Inflate(idat.ToArray());
            int bitsPerPixel = samples * depth;
            int stride = (w * bitsPerPixel + 7) / 8;
            int unit = Math.Max(1, bitsPerPixel / 8);
            if (raw.Length < h * (stride + 1)) throw new FormatException("PNG image data is short");
            var pixels = new byte[h * stride];
            var prev = new byte[stride];
            var cur = new byte[stride];
            for (int y = 0; y < h; y++)
            {
                int f = raw[y * (stride + 1)];
                Array.Copy(raw, y * (stride + 1) + 1, cur, 0, stride);
                for (int x = 0; x < stride; x++)
                {
                    int left = x >= unit ? cur[x - unit] : 0, up = prev[x], ul = x >= unit ? prev[x - unit] : 0;
                    int v = cur[x];
                    if (f == 1) v += left;
                    else if (f == 2) v += up;
                    else if (f == 3) v += (left + up) >> 1;
                    else if (f == 4)
                    {
                        int p = left + up - ul, pa = Math.Abs(p - left), pb = Math.Abs(p - up), pc = Math.Abs(p - ul);
                        v += pa <= pb && pa <= pc ? left : pb <= pc ? up : ul;
                    }
                    else if (f != 0) throw new FormatException("damaged PNG (filter " + f + ")");
                    cur[x] = (byte)v;
                }
                Array.Copy(cur, 0, pixels, y * stride, stride);
                var t = prev; prev = cur; cur = t;
            }

            channels = type == 0 || type == 4 ? 1 : 3;
            colour = new byte[w * h * channels];
            bool hasAlpha = type == 4 || type == 6 || trns != null;
            alpha = hasAlpha ? new byte[w * h] : null;
            int maxv = (1 << depth) - 1;
            int keyGrey = type == 0 && trns != null && trns.Length >= 2 ? (trns[0] << 8) | trns[1] : -1;
            int keyR = -1, keyG = -1, keyB = -1;
            if (type == 2 && trns != null && trns.Length >= 6)
            {
                keyR = (trns[0] << 8) | trns[1]; keyG = (trns[2] << 8) | trns[3]; keyB = (trns[4] << 8) | trns[5];
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    int s0 = Sample(pixels, y * stride, x * samples, depth);
                    if (type == 3)
                    {
                        if (s0 * 3 + 2 >= plte.Length) throw new FormatException("PNG palette index out of range");
                        colour[i * 3] = plte[s0 * 3]; colour[i * 3 + 1] = plte[s0 * 3 + 1]; colour[i * 3 + 2] = plte[s0 * 3 + 2];
                        if (alpha != null) alpha[i] = s0 < trns.Length ? trns[s0] : (byte)255;
                        continue;
                    }
                    if (channels == 1)
                    {
                        colour[i] = To8(s0, depth, maxv);
                        if (type == 4) alpha[i] = To8(Sample(pixels, y * stride, x * samples + 1, depth), depth, maxv);
                        else if (alpha != null) alpha[i] = s0 == keyGrey ? (byte)0 : (byte)255;
                    }
                    else
                    {
                        int s1 = Sample(pixels, y * stride, x * samples + 1, depth), s2 = Sample(pixels, y * stride, x * samples + 2, depth);
                        colour[i * 3] = To8(s0, depth, maxv); colour[i * 3 + 1] = To8(s1, depth, maxv); colour[i * 3 + 2] = To8(s2, depth, maxv);
                        if (type == 6) alpha[i] = To8(Sample(pixels, y * stride, x * samples + 3, depth), depth, maxv);
                        else if (alpha != null) alpha[i] = s0 == keyR && s1 == keyG && s2 == keyB ? (byte)0 : (byte)255;
                    }
                }
        }

        /// <summary>The n-th sample of a row (depth 1, 2, 4, 8 or 16 bits).</summary>
        static int Sample(byte[] px, int row, int n, int depth)
        {
            if (depth == 8) return px[row + n];
            if (depth == 16) return (px[row + n * 2] << 8) | px[row + n * 2 + 1];
            int bit = n * depth;
            return (px[row + bit / 8] >> (8 - depth - bit % 8)) & ((1 << depth) - 1);
        }

        static byte To8(int v, int depth, int maxv)
        {
            if (depth == 8) return (byte)v;
            if (depth == 16) return (byte)(v >> 8);
            return (byte)(v * 255 / maxv);
        }

        static byte[] Inflate(byte[] zlib)
        {
            if (zlib.Length < 2) throw new FormatException("PNG has no image data");
            using (var src = new MemoryStream(zlib, 2, zlib.Length - 2)) // skip the 2-byte zlib header
            using (var ds = new DeflateStream(src, CompressionMode.Decompress))
            using (var dst = new MemoryStream())
            {
                var buf = new byte[65536];
                int n;
                while ((n = ds.Read(buf, 0, buf.Length)) > 0) dst.Write(buf, 0, n);
                return dst.ToArray();
            }
        }

        /// <summary>zlib stream (what PDF's FlateDecode expects): header, deflate data, Adler-32.</summary>
        static byte[] Zlib(byte[] data)
        {
            var ms = new MemoryStream();
            ms.WriteByte(0x78);
            ms.WriteByte(0x9C);
            using (var ds = new DeflateStream(ms, CompressionMode.Compress, true)) ds.Write(data, 0, data.Length);
            uint a = 1, s = 0;
            foreach (byte x in data) { a = (a + x) % 65521; s = (s + a) % 65521; }
            uint adler = (s << 16) | a;
            ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16)); ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
            return ms.ToArray();
        }
    }
}
