// Jig part request list (Excel) and the ZIP layer underneath it.
namespace LaserDxf.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using LaserDxf.Core;

    public static class RequestListTests
    {
        public static void TestZipRoundTrip()
        {
            var entries = new List<ZipEntryData>
            {
                new ZipEntryData { Name = "a.txt", Data = Encoding.UTF8.GetBytes("hello hello hello hello") },
                new ZipEntryData { Name = "dir/b.bin", Data = new byte[] { 0, 1, 2, 255 } },
                new ZipEntryData { Name = "empty", Data = new byte[0] },
            };
            List<ZipEntryData> back = Zip.Read(Zip.Write(entries));
            TestMain.Check(back.Count == 3 && back[0].Name == "a.txt" && Encoding.UTF8.GetString(back[0].Data) == "hello hello hello hello", "text entry");
            TestMain.Check(back[1].Data.Length == 4 && back[1].Data[3] == 255 && back[2].Data.Length == 0, "binary and empty entries");
            TestMain.Check(Zip.Crc32(Encoding.ASCII.GetBytes("123456789")) == 0xCBF43926u, "CRC-32 check value");
        }

        static List<List<string>> Table(int n, out List<string> header)
        {
            string csv = "ID,Revision,Name,Qty,Material,Thickness,DXF path\r\n";
            for (int i = 0; i < n; i++)
                csv += "S180-06068-470" + (i + 2).ToString("00") + "-1,1,PLATE " + i + " & CO <X>,"
                     + (i % 3 + 1) + ",MILD STEEL," + (i < n / 2 ? "6mm" : "3mm") + ",C:\\Laser DXF\\S180-06068-37003-1\\S180-06068-470" + (i + 2).ToString("00") + "-1.dxf\r\n";
            List<List<string>> rows = Batch.ParseCsv(csv);
            header = rows[0];
            rows.RemoveAt(0);
            return rows;
        }

        static byte[] Fill(int n, string outName, string dataSheet)
        {
            byte[] template = File.ReadAllBytes(Path.Combine("tests", "fixtures", "request_template.xlsx"));
            List<string> header;
            var rows = Table(n, out header);
            var w = new List<string>();
            RequestListSpec spec = LaserConfig.DefaultRequestList();
            spec.TitleValue = "S180-06068-37005-1";
            spec.DataSheetName = dataSheet;
            byte[] xlsx = Xlsx.FillTemplate(template, spec, header, rows, w);
            TestMain.Check(w.Count == 0, "no warnings: " + string.Join(" | ", w.ToArray()));
            File.WriteAllBytes(Path.Combine(TestMain.OutDir, outName), xlsx);
            return xlsx;
        }

        public static void TestFillTemplate()
        {
            byte[] xlsx = Fill(6, "request_list_6.xlsx", LaserConfig.DefaultRequestList().DataSheetName);
            var names = new List<string>();
            string sheet1 = null, workbook = null;
            foreach (var e in Zip.Read(xlsx))
            {
                names.Add(e.Name);
                if (e.Name == "xl/worksheets/sheet1.xml") sheet1 = Encoding.UTF8.GetString(e.Data);
                if (e.Name == "xl/workbook.xml") workbook = Encoding.UTF8.GetString(e.Data);
            }
            TestMain.Check(names.Contains("xl/media/image1.png") && names.Contains("xl/drawings/drawing1.xml"), "logo image kept");
            TestMain.Check(names.Contains("xl/worksheets/sheet2.xml") && workbook.Contains("name=\"DXF list\""), "CSV data on a second sheet by default");
            TestMain.Check(workbook.Contains("fullCalcOnLoad=\"1\""), "recalc on open");
            TestMain.Check(sheet1.Contains("<c r=\"A2\"") && sheet1.Contains(">S180-06068-37005-1</t>"), "assembly number in A2");
            TestMain.Check(sheet1.Contains("<f>COUNTA(A4:A37)</f>"), "total formula untouched");
            TestMain.Check(sheet1.Contains("PLATE 0 &amp; CO &lt;X&gt;") == false && sheet1.Contains("S180-06068-47002-1"), "part number written (name not mapped)");
        }

        public static void TestMoreRowsThanTemplate()
        {
            Fill(40, "request_list_40.xlsx", "DXF list"); // template has rows 4..37 = 34 rows; optional data sheet
        }
    }
}
