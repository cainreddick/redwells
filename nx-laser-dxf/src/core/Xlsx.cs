// Fills an Excel (.xlsx) template - e.g. the "Jig Part Request List" - with rows of data,
// and adds a plain data sheet. Only the cells written are touched: logos, borders, merged
// cells, print setup etc. of the template are kept as they are.
namespace LaserDxf.Core
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Text.RegularExpressions;

    public sealed class RequestListSpec
    {
        /// <summary>Sheet to fill ("" = the first sheet).</summary>
        public string SheetName = "";
        /// <summary>First data row (1-based), e.g. 4 below a header in row 3.</summary>
        public int FirstRow = 4;
        /// <summary>Column letter -> CSV column header, e.g. A -> "ID".</summary>
        public List<KeyValuePair<string, string>> Columns = new List<KeyValuePair<string, string>>();
        /// <summary>Cell for the number of part numbers (e.g. "H1"); left alone if it holds a formula. "" = none.</summary>
        public string TotalCell = "";
        /// <summary>Name of the extra sheet with the full CSV table ("" = none).</summary>
        public string DataSheetName = "DXF list";
    }

    public static class Xlsx
    {
        const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string WorksheetRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";
        const string WorksheetContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";

        // ------------------------------------------------------------ helpers

        public static string Esc(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        static string Attr(string tag, string name)
        {
            Match m = Regex.Match(tag, @"(?:^|\s)" + Regex.Escape(name) + @"\s*=\s*""([^""]*)""");
            return m.Success ? m.Groups[1].Value : null;
        }

        static string SetAttr(string attrs, string name, string value)
        {
            var re = new Regex(@"(\s)" + Regex.Escape(name) + @"\s*=\s*""[^""]*""");
            if (re.IsMatch(attrs)) return re.Replace(attrs, "$1" + name + "=\"" + value + "\"", 1);
            return attrs + " " + name + "=\"" + value + "\"";
        }

        static string RemoveAttr(string attrs, string name)
        {
            return Regex.Replace(attrs, @"\s" + Regex.Escape(name) + @"\s*=\s*""[^""]*""", "");
        }

        public static int ColumnIndex(string letters)
        {
            int n = 0;
            foreach (char ch in letters.ToUpperInvariant())
            {
                if (ch < 'A' || ch > 'Z') break;
                n = n * 26 + (ch - 'A' + 1);
            }
            return n;
        }

        public static string ColumnLetters(int index)
        {
            var sb = new StringBuilder();
            while (index > 0)
            {
                index--;
                sb.Insert(0, (char)('A' + index % 26));
                index /= 26;
            }
            return sb.ToString();
        }

        static void SplitRef(string cellRef, out string col, out int row)
        {
            Match m = Regex.Match(cellRef.Trim().ToUpperInvariant(), @"^([A-Z]+)(\d+)$");
            if (!m.Success) throw new FormatException("Not a cell reference: " + cellRef);
            col = m.Groups[1].Value;
            row = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        static bool IsPlainNumber(string s)
        {
            return Regex.IsMatch(s ?? "", @"^-?\d+(\.\d+)?$");
        }

        static string CellXml(string colRow, string style, string value)
        {
            string s = style == null ? "" : " s=\"" + style + "\"";
            if (value == null || value.Length == 0) return "<c r=\"" + colRow + "\"" + s + "/>";
            if (IsPlainNumber(value)) return "<c r=\"" + colRow + "\"" + s + "><v>" + value + "</v></c>";
            return "<c r=\"" + colRow + "\"" + s + " t=\"inlineStr\"><is><t xml:space=\"preserve\">" + Esc(value) + "</t></is></c>";
        }

        // ------------------------------------------------------------ sheet model

        sealed class Cell
        {
            public int Col;
            public string Xml;
            public string Style { get { return Attr(Regex.Match(Xml, @"^<c\b[^>]*").Value, "s"); } }
            public bool HasFormula { get { return Xml.Contains("<f"); } }
        }

        sealed class Row
        {
            public int R;
            public string Attrs;   // attributes of <row ...> (without r)
            public List<Cell> Cells = new List<Cell>();

            public Cell Get(int col)
            {
                foreach (var c in Cells) if (c.Col == col) return c;
                return null;
            }

            public void Put(Cell cell)
            {
                for (int i = 0; i < Cells.Count; i++)
                {
                    if (Cells[i].Col == cell.Col) { Cells[i] = cell; return; }
                    if (Cells[i].Col > cell.Col) { Cells.Insert(i, cell); return; }
                }
                Cells.Add(cell);
            }

            public string ToXml()
            {
                var sb = new StringBuilder("<row r=\"" + R + "\"" + Attrs);
                if (Cells.Count == 0) return sb.Append("/>").ToString();
                sb.Append('>');
                foreach (var c in Cells) sb.Append(c.Xml);
                return sb.Append("</row>").ToString();
            }
        }

        static List<Row> ParseRows(string sheetData)
        {
            var rows = new List<Row>();
            foreach (Match m in Regex.Matches(sheetData, @"<row\b([^>]*?)(?:/>|>(.*?)</row>)", RegexOptions.Singleline))
            {
                string attrs = m.Groups[1].Value;
                var row = new Row { R = int.Parse(Attr(attrs, "r"), CultureInfo.InvariantCulture), Attrs = RemoveAttr(" " + attrs.Trim(), "r") };
                if (row.Attrs.Trim().Length == 0) row.Attrs = "";
                foreach (Match cm in Regex.Matches(m.Groups[2].Value, @"<c\b[^>]*?(?:/>|>.*?</c>)", RegexOptions.Singleline))
                {
                    string r = Attr(Regex.Match(cm.Value, @"^<c\b[^>]*").Value, "r");
                    string col; int rr;
                    SplitRef(r, out col, out rr);
                    row.Cells.Add(new Cell { Col = ColumnIndex(col), Xml = cm.Value });
                }
                rows.Add(row);
            }
            rows.Sort(delegate (Row a, Row b) { return a.R.CompareTo(b.R); });
            return rows;
        }

        static Row FindRow(List<Row> rows, int r)
        {
            foreach (var row in rows) if (row.R == r) return row;
            return null;
        }

        // ------------------------------------------------------------ fill

        /// <summary>Copies the template and writes rows (from the CSV table: header + rows) into the
        /// request-list sheet, then adds the data sheet. Returns the new .xlsx file.</summary>
        public static byte[] FillTemplate(byte[] template, RequestListSpec spec, List<string> header, List<List<string>> rows, List<string> warnings)
        {
            List<ZipEntryData> entries = Zip.Read(template);
            var byName = new Dictionary<string, ZipEntryData>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries) byName[e.Name] = e;
            Func<string, string> text = delegate (string name) { return Encoding.UTF8.GetString(byName[name].Data); };
            Action<string, string> put = delegate (string name, string value)
            {
                ZipEntryData e;
                if (!byName.TryGetValue(name, out e)) { e = new ZipEntryData { Name = name }; entries.Add(e); byName[name] = e; }
                e.Data = new UTF8Encoding(false).GetBytes(value);
            };
            if (!byName.ContainsKey("xl/workbook.xml") || !byName.ContainsKey("xl/_rels/workbook.xml.rels"))
                throw new FormatException("The template is not an Excel .xlsx workbook.");

            string workbook = text("xl/workbook.xml");
            string rels = text("xl/_rels/workbook.xml.rels");

            // Which sheet? (the first one unless named)
            MatchCollection sheets = Regex.Matches(workbook, @"<sheet\b[^>]*/?>");
            if (sheets.Count == 0) throw new FormatException("The template has no sheets.");
            string sheetTag = sheets[0].Value;
            if (spec.SheetName.Length > 0)
            {
                sheetTag = null;
                foreach (Match sm in sheets)
                    if (string.Equals(Attr(sm.Value, "name"), spec.SheetName, StringComparison.OrdinalIgnoreCase)) sheetTag = sm.Value;
                if (sheetTag == null)
                {
                    warnings.Add("Request list: the template has no sheet called \"" + spec.SheetName + "\" - used the first sheet.");
                    sheetTag = sheets[0].Value;
                }
            }
            string relPrefix = Regex.Match(sheetTag, @"\s(\w+):id\s*=").Groups[1].Value;
            if (relPrefix.Length == 0) relPrefix = "r";
            string rid = Attr(sheetTag, relPrefix + ":id");
            string target = null;
            foreach (Match rm in Regex.Matches(rels, @"<Relationship\b[^>]*/?>"))
                if (Attr(rm.Value, "Id") == rid) target = Attr(rm.Value, "Target");
            if (target == null) throw new FormatException("Could not find the request-list sheet in the template.");
            string sheetPath = target.StartsWith("/") ? target.Substring(1) : "xl/" + target;

            // Fill the sheet.
            string sheet = text(sheetPath);
            sheet = FillSheet(sheet, spec, header, rows, warnings);
            put(sheetPath, sheet);

            // If the list now runs past the print area, extend it so every row prints.
            int sheetIndex = 0;
            for (int i = 0; i < sheets.Count; i++) if (sheets[i].Value == sheetTag) sheetIndex = i;
            int lastRow = spec.FirstRow + rows.Count - 1;
            workbook = Regex.Replace(workbook,
                @"(<definedName\b[^>]*name=""_xlnm\.Print_Area""[^>]*localSheetId=""" + sheetIndex + @"""[^>]*>[^<]*?![$]?[A-Z]+[$]?\d+:[$]?[A-Z]+[$]?)(\d+)(</definedName>)",
                delegate (Match pm)
                {
                    int end = int.Parse(pm.Groups[2].Value, CultureInfo.InvariantCulture);
                    return end >= lastRow ? pm.Value : pm.Groups[1].Value + lastRow + pm.Groups[3].Value;
                });

            // Extra sheet with the whole CSV table.
            if (spec.DataSheetName.Trim().Length > 0)
            {
                string name = spec.DataSheetName.Trim();
                bool taken = false;
                foreach (Match sm in sheets) if (string.Equals(Attr(sm.Value, "name"), name, StringComparison.OrdinalIgnoreCase)) taken = true;
                if (taken) warnings.Add("Request list: the template already has a sheet called \"" + name + "\" - data sheet not added.");
                else
                {
                    int n = 1;
                    while (byName.ContainsKey("xl/worksheets/sheet" + n + ".xml")) n++;
                    string path = "xl/worksheets/sheet" + n + ".xml";
                    put(path, DataSheet(header, rows));

                    int maxRid = 0, maxSheetId = 0;
                    foreach (Match rm in Regex.Matches(rels, @"\bId=""rId(\d+)""")) maxRid = Math.Max(maxRid, int.Parse(rm.Groups[1].Value, CultureInfo.InvariantCulture));
                    foreach (Match sm in sheets) { int id; if (int.TryParse(Attr(sm.Value, "sheetId"), out id)) maxSheetId = Math.Max(maxSheetId, id); }
                    string newRid = "rId" + (maxRid + 1);
                    rels = rels.Replace("</Relationships>", "<Relationship Id=\"" + newRid + "\" Type=\"" + WorksheetRelType + "\" Target=\"worksheets/sheet" + n + ".xml\"/></Relationships>");
                    // Declare the relationships prefix on the element itself: some writers declare it per <sheet>, not on <workbook>.
                    workbook = workbook.Replace("</sheets>", "<sheet xmlns:" + relPrefix + "=\"" + RelNs + "\" name=\"" + Esc(name) + "\" sheetId=\"" + (maxSheetId + 1) + "\" " + relPrefix + ":id=\"" + newRid + "\"/></sheets>");
                    string ct = text("[Content_Types].xml");
                    put("[Content_Types].xml", ct.Replace("</Types>", "<Override PartName=\"/" + path + "\" ContentType=\"" + WorksheetContentType + "\"/></Types>"));
                }
            }

            // Let Excel recalculate on open (e.g. the "Total Part No.'s" formula), and drop the
            // calculation chain, which Excel rebuilds.
            workbook = ForceRecalc(workbook);
            if (byName.ContainsKey("xl/calcChain.xml"))
            {
                entries.Remove(byName["xl/calcChain.xml"]);
                byName.Remove("xl/calcChain.xml");
                rels = Regex.Replace(rels, @"<Relationship\b[^>]*calcChain[^>]*/>", "");
                put("[Content_Types].xml", Regex.Replace(text("[Content_Types].xml"), @"<Override\b[^>]*calcChain[^>]*/>", ""));
            }
            put("xl/workbook.xml", workbook);
            put("xl/_rels/workbook.xml.rels", rels);
            return Zip.Write(entries);
        }

        static string ForceRecalc(string workbook)
        {
            Match calc = Regex.Match(workbook, @"<calcPr\b[^>]*?/?>");
            if (calc.Success)
            {
                string tag = calc.Value;
                bool selfClosing = tag.EndsWith("/>");
                string attrs = tag.Substring(7, tag.Length - 7 - (selfClosing ? 2 : 1));
                attrs = SetAttr(attrs, "fullCalcOnLoad", "1");
                return workbook.Substring(0, calc.Index) + "<calcPr" + attrs + (selfClosing ? "/>" : ">") + workbook.Substring(calc.Index + calc.Length);
            }
            // calcPr comes after sheets / externalReferences / definedNames in the schema order.
            foreach (string after in new[] { "</definedNames>", "</externalReferences>", "</sheets>" })
            {
                int i = workbook.IndexOf(after, StringComparison.Ordinal);
                if (i >= 0) return workbook.Insert(i + after.Length, "<calcPr fullCalcOnLoad=\"1\"/>");
            }
            return workbook;
        }

        static string FillSheet(string sheet, RequestListSpec spec, List<string> header, List<List<string>> rows, List<string> warnings)
        {
            Match sd = Regex.Match(sheet, @"<sheetData\s*/>|<sheetData\b[^>]*>(.*?)</sheetData>", RegexOptions.Singleline);
            if (!sd.Success) throw new FormatException("The request-list sheet has no cell data.");
            List<Row> sheetRows = ParseRows(sd.Groups[1].Value);

            // Columns to fill: letter -> index in the CSV row.
            var cols = new List<KeyValuePair<int, int>>();
            foreach (var kv in spec.Columns)
            {
                int idx = header.FindIndex(delegate (string h) { return string.Equals(h, kv.Value, StringComparison.OrdinalIgnoreCase); });
                if (idx < 0) { warnings.Add("Request list: no CSV column \"" + kv.Value + "\" for column " + kv.Key + " - left empty."); continue; }
                cols.Add(new KeyValuePair<int, int>(ColumnIndex(kv.Key), idx));
            }

            // The last template row of the list is the pattern for any extra rows needed.
            Row pattern = null;
            foreach (var r in sheetRows) if (r.R >= spec.FirstRow) pattern = r;

            for (int i = 0; i < rows.Count; i++)
            {
                int rr = spec.FirstRow + i;
                Row row = FindRow(sheetRows, rr);
                if (row == null)
                {
                    row = new Row { R = rr, Attrs = pattern != null ? pattern.Attrs : "" };
                    if (pattern != null)
                        foreach (var c in pattern.Cells)
                            row.Cells.Add(new Cell { Col = c.Col, Xml = CellXml(ColumnLetters(c.Col) + rr, c.Style, null) });
                    sheetRows.Add(row);
                }
                foreach (var kv in cols)
                {
                    Cell existing = row.Get(kv.Key);
                    string style = existing != null ? existing.Style : (pattern != null && pattern.Get(kv.Key) != null ? pattern.Get(kv.Key).Style : null);
                    string value = kv.Value < rows[i].Count ? rows[i][kv.Value] : "";
                    row.Put(new Cell { Col = kv.Key, Xml = CellXml(ColumnLetters(kv.Key) + rr, style, value) });
                }
            }

            if (spec.TotalCell.Trim().Length > 0)
            {
                string tc; int tr;
                SplitRef(spec.TotalCell, out tc, out tr);
                Row row = FindRow(sheetRows, tr);
                if (row == null) { row = new Row { R = tr, Attrs = "" }; sheetRows.Add(row); }
                Cell existing = row.Get(ColumnIndex(tc));
                if (existing == null || !existing.HasFormula)
                    row.Put(new Cell { Col = ColumnIndex(tc), Xml = CellXml(tc + tr, existing != null ? existing.Style : null, rows.Count.ToString(CultureInfo.InvariantCulture)) });
            }

            sheetRows.Sort(delegate (Row a, Row b) { return a.R.CompareTo(b.R); });
            var sb = new StringBuilder();
            foreach (var r in sheetRows) sb.Append(r.ToXml());
            string open = sd.Value.StartsWith("<sheetData/") || sd.Value.StartsWith("<sheetData /") ? "<sheetData>" : Regex.Match(sd.Value, @"^<sheetData\b[^>]*>").Value;
            sheet = sheet.Substring(0, sd.Index) + open + sb + "</sheetData>" + sheet.Substring(sd.Index + sd.Length);

            // Keep the declared used range in step with the rows written.
            int lastRow = spec.FirstRow + rows.Count - 1;
            Match dim = Regex.Match(sheet, @"<dimension ref=""([A-Z]+\d+):([A-Z]+)(\d+)""");
            if (dim.Success && lastRow > int.Parse(dim.Groups[3].Value, CultureInfo.InvariantCulture))
                sheet = sheet.Substring(0, dim.Index) + "<dimension ref=\"" + dim.Groups[1].Value + ":" + dim.Groups[2].Value + lastRow + "\"" + sheet.Substring(dim.Index + dim.Length);
            return sheet;
        }

        static string DataSheet(List<string> header, List<List<string>> rows)
        {
            var widths = new List<int>();
            foreach (string h in header) widths.Add(Math.Max(8, h.Length + 2));
            foreach (var r in rows)
                for (int i = 0; i < r.Count && i < widths.Count; i++) widths[i] = Math.Min(80, Math.Max(widths[i], r[i].Length + 2));
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n");
            sb.Append("<worksheet xmlns=\"" + MainNs + "\" xmlns:r=\"" + RelNs + "\"><cols>");
            for (int i = 0; i < widths.Count; i++)
                sb.Append("<col min=\"" + (i + 1) + "\" max=\"" + (i + 1) + "\" width=\"" + widths[i] + "\" customWidth=\"1\"/>");
            sb.Append("</cols><sheetData>");
            var all = new List<List<string>> { header };
            all.AddRange(rows);
            for (int r = 0; r < all.Count; r++)
            {
                sb.Append("<row r=\"" + (r + 1) + "\">");
                for (int c = 0; c < all[r].Count; c++) sb.Append(CellXml(ColumnLetters(c + 1) + (r + 1), null, all[r][c]));
                sb.Append("</row>");
            }
            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }
    }
}
