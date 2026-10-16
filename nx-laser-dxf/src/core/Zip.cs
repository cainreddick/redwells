// Minimal ZIP reader/writer (enough for .xlsx files): stored and deflated entries,
// no ZIP64, no encryption. Uses DeflateStream from System.dll; System.IO.Compression's
// ZipArchive lives in an assembly an NX journal may not reference.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Text;

    public sealed class ZipEntryData
    {
        public string Name;
        public byte[] Data;
    }

    public static class Zip
    {
        static uint[] crcTable;

        public static uint Crc32(byte[] data)
        {
            if (crcTable == null)
            {
                var t = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    t[n] = c;
                }
                crcTable = t;
            }
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        static ushort U16(byte[] b, int o) { return (ushort)(b[o] | b[o + 1] << 8); }
        static uint U32(byte[] b, int o) { return (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24); }

        /// <summary>Reads all entries, in archive order.</summary>
        public static List<ZipEntryData> Read(byte[] zip)
        {
            int eocd = -1;
            for (int i = zip.Length - 22; i >= Math.Max(0, zip.Length - 22 - 65535); i--)
                if (U32(zip, i) == 0x06054b50) { eocd = i; break; }
            if (eocd < 0) throw new InvalidDataException("Not a ZIP file (no end-of-central-directory record).");
            int count = U16(zip, eocd + 10);
            int pos = (int)U32(zip, eocd + 16);
            var entries = new List<ZipEntryData>();
            for (int i = 0; i < count; i++)
            {
                if (U32(zip, pos) != 0x02014b50) throw new InvalidDataException("Bad ZIP central directory.");
                int method = U16(zip, pos + 10);
                int csize = (int)U32(zip, pos + 20);
                int usize = (int)U32(zip, pos + 24);
                int nameLen = U16(zip, pos + 28), extraLen = U16(zip, pos + 30), commentLen = U16(zip, pos + 32);
                int local = (int)U32(zip, pos + 42);
                string name = Encoding.UTF8.GetString(zip, pos + 46, nameLen);
                pos += 46 + nameLen + extraLen + commentLen;

                if (U32(zip, local) != 0x04034b50) throw new InvalidDataException("Bad ZIP local header for " + name);
                int dataStart = local + 30 + U16(zip, local + 26) + U16(zip, local + 28);
                byte[] data;
                if (method == 0)
                {
                    data = new byte[usize];
                    Buffer.BlockCopy(zip, dataStart, data, 0, usize);
                }
                else if (method == 8)
                {
                    using (var ms = new MemoryStream(zip, dataStart, csize))
                    using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                    using (var outMs = new MemoryStream(usize > 0 ? usize : 1024))
                    {
                        var buf = new byte[65536];
                        int r;
                        while ((r = ds.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, r);
                        data = outMs.ToArray();
                    }
                }
                else throw new InvalidDataException("Unsupported ZIP compression method " + method + " for " + name);
                entries.Add(new ZipEntryData { Name = name, Data = data });
            }
            return entries;
        }

        static void W16(Stream s, int v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); }
        static void W32(Stream s, uint v) { W16(s, (int)(v & 0xFFFF)); W16(s, (int)(v >> 16)); }

        /// <summary>Writes the entries (deflated) to a new ZIP archive.</summary>
        public static byte[] Write(List<ZipEntryData> entries)
        {
            var ms = new MemoryStream();
            var central = new MemoryStream();
            // DOS date/time: 1980-01-01 00:00 (fixed, so output is reproducible)
            const int dosTime = 0, dosDate = (0 << 9) | (1 << 5) | 1;
            foreach (var e in entries)
            {
                byte[] name = Encoding.UTF8.GetBytes(e.Name);
                byte[] comp;
                using (var cms = new MemoryStream())
                {
                    using (var ds = new DeflateStream(cms, CompressionMode.Compress, true)) ds.Write(e.Data, 0, e.Data.Length);
                    comp = cms.ToArray();
                }
                uint crc = Crc32(e.Data);
                uint offset = (uint)ms.Position;

                W32(ms, 0x04034b50); W16(ms, 20); W16(ms, 0x0800); W16(ms, 8); W16(ms, dosTime); W16(ms, dosDate);
                W32(ms, crc); W32(ms, (uint)comp.Length); W32(ms, (uint)e.Data.Length); W16(ms, name.Length); W16(ms, 0);
                ms.Write(name, 0, name.Length);
                ms.Write(comp, 0, comp.Length);

                W32(central, 0x02014b50); W16(central, 20); W16(central, 20); W16(central, 0x0800); W16(central, 8);
                W16(central, dosTime); W16(central, dosDate); W32(central, crc); W32(central, (uint)comp.Length); W32(central, (uint)e.Data.Length);
                W16(central, name.Length); W16(central, 0); W16(central, 0); W16(central, 0); W16(central, 0); W32(central, 0); W32(central, offset);
                central.Write(name, 0, name.Length);
            }
            uint cdOffset = (uint)ms.Position;
            byte[] cd = central.ToArray();
            ms.Write(cd, 0, cd.Length);
            W32(ms, 0x06054b50); W16(ms, 0); W16(ms, 0); W16(ms, entries.Count); W16(ms, entries.Count);
            W32(ms, (uint)cd.Length); W32(ms, cdOffset); W16(ms, 0);
            return ms.ToArray();
        }
    }
}
