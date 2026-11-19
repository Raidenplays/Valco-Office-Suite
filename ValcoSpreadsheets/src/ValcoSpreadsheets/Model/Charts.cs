using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;

namespace ValcoSpreadsheets.Model
{
    public enum ChartKind { Column, Bar, Line, Pie, Area, Scatter, Doughnut }

    public class ChartSeries
    {
        public string NameRef, Name, CatRef, ValRef;
        public ChartSeries Clone() => (ChartSeries)MemberwiseClone();
    }

    /// <summary>A chart on a sheet: its type, data references, title and where it sits.</summary>
    public class ChartModel
    {
        public string Sheet;
        public ChartKind Kind = ChartKind.Column;
        public bool Stacked, Percent;
        public string Title = "";
        public bool ShowLegend = true;
        public string LegendPos = "r";
        public bool ShowDataLabels;
        public List<ChartSeries> Series = new();
        // anchor: top-left cell and offset in pixels, size in pixels
        public int Row = 2, Col = 2;
        public double OffsetX, OffsetY, Width = 480, Height = 288;
        public int StyleIndex = 2;
        // when the file gives the bottom-right cell instead of a size, the size is worked out against real widths
        public int ToCol, ToRow;
        public double ToOffX, ToOffY;

        public ChartModel Clone()
        {
            var c = (ChartModel)MemberwiseClone();
            c.Series = Series.Select(s => s.Clone()).ToList();
            return c;
        }
    }

    /// <summary>Charts kept alongside each open book (ClosedXML has no chart support, so they're read and written here).</summary>
    public static class ChartStore
    {
        static readonly ConditionalWeakTable<Book, List<ChartModel>> Table = new();
        public static List<ChartModel> For(Book b) => Table.GetValue(b, _ => new List<ChartModel>());
        public static void Set(Book b, List<ChartModel> charts) { var l = For(b); l.Clear(); l.AddRange(charts); }

        public static void RenameSheet(Book b, string oldName, string newName)
        {
            foreach (var c in For(b))
            {
                if (c.Sheet == oldName) c.Sheet = newName;
                foreach (var s in c.Series)
                {
                    s.NameRef = Rename(s.NameRef, oldName, newName); s.CatRef = Rename(s.CatRef, oldName, newName); s.ValRef = Rename(s.ValRef, oldName, newName);
                }
            }
        }

        static string Rename(string r, string o, string n)
        {
            if (string.IsNullOrEmpty(r)) return r;
            return r.Replace("'" + o.Replace("'", "''") + "'!", ChartIO.Quote(n) + "!").Replace(o + "!", ChartIO.Quote(n) + "!");
        }
    }

    /// <summary>Reading and writing DrawingML charts in the .xlsx package.</summary>
    public static class ChartIO
    {
        static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        static readonly XNamespace XDR = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const double EmuPerPx = 9525;

        public static string Quote(string sheet) => sheet.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_') ? "'" + sheet.Replace("'", "''") + "'" : sheet;

        /// <summary>Reads the supported charts of a workbook. Returns false in <paramref name="allKept"/> when some couldn't be read.</summary>
        public static List<ChartModel> Read(string path, out bool allKept)
        {
            var list = new List<ChartModel>();
            allKept = true;
            try
            {
                using var doc = SpreadsheetDocument.Open(path, false);
                var wbPart = doc.WorkbookPart;
                foreach (var sheet in wbPart.Workbook.Sheets.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>())
                {
                    if (!(wbPart.GetPartById(sheet.Id) is WorksheetPart wsPart) || wsPart.DrawingsPart == null) continue;
                    var dp = wsPart.DrawingsPart;
                    XDocument dx;
                    using (var s = dp.GetStream()) dx = XDocument.Load(s);
                    foreach (var anchor in dx.Root.Elements().Where(e => e.Name == XDR + "twoCellAnchor" || e.Name == XDR + "oneCellAnchor"))
                    {
                        var chartRef = anchor.Descendants(C + "chart").FirstOrDefault();
                        if (chartRef == null) continue;
                        var rid = (string)chartRef.Attribute(R + "id");
                        if (!(dp.GetPartById(rid) is ChartPart cp)) { allKept = false; continue; }
                        XDocument cx;
                        using (var s = cp.GetStream()) cx = XDocument.Load(s);
                        var model = FromChartXml(cx);
                        if (model == null) { allKept = false; continue; }
                        model.Sheet = sheet.Name;
                        var from = anchor.Element(XDR + "from");
                        model.Col = (int)from.Element(XDR + "col") + 1; model.Row = (int)from.Element(XDR + "row") + 1;
                        model.OffsetX = (long)from.Element(XDR + "colOff") / EmuPerPx; model.OffsetY = (long)from.Element(XDR + "rowOff") / EmuPerPx;
                        var to = anchor.Element(XDR + "to");
                        var ext = anchor.Element(XDR + "ext");
                        if (ext != null) { model.Width = (long)ext.Attribute("cx") / EmuPerPx; model.Height = (long)ext.Attribute("cy") / EmuPerPx; }
                        else if (to != null)
                        {
                            // size from the cell span: worked out later against real widths; approximate with defaults
                            int c2 = (int)to.Element(XDR + "col") + 1, r2 = (int)to.Element(XDR + "row") + 1;
                            model.Width = Math.Max(120, (c2 - model.Col) * 64 + (long)to.Element(XDR + "colOff") / EmuPerPx - model.OffsetX);
                            model.Height = Math.Max(80, (r2 - model.Row) * 20 + (long)to.Element(XDR + "rowOff") / EmuPerPx - model.OffsetY);
                            model.ToCol = c2; model.ToRow = r2; model.ToOffX = (long)to.Element(XDR + "colOff") / EmuPerPx; model.ToOffY = (long)to.Element(XDR + "rowOff") / EmuPerPx;
                        }
                        list.Add(model);
                    }
                }
            }
            catch { allKept = false; }
            return list;
        }

        static ChartModel FromChartXml(XDocument cx)
        {
            var plot = cx.Descendants(C + "plotArea").FirstOrDefault();
            if (plot == null) return null;
            var types = plot.Elements().Where(e => e.Name.LocalName.EndsWith("Chart")).ToList();
            if (types.Count != 1) return null; // combination charts aren't supported
            var t = types[0];
            var m = new ChartModel();
            switch (t.Name.LocalName)
            {
                case "barChart":
                    m.Kind = (string)t.Element(C + "barDir")?.Attribute("val") == "bar" ? ChartKind.Bar : ChartKind.Column; break;
                case "lineChart": m.Kind = ChartKind.Line; break;
                case "pieChart": m.Kind = ChartKind.Pie; break;
                case "doughnutChart": m.Kind = ChartKind.Doughnut; break;
                case "areaChart": m.Kind = ChartKind.Area; break;
                case "scatterChart": m.Kind = ChartKind.Scatter; break;
                default: return null;
            }
            var grouping = (string)t.Element(C + "grouping")?.Attribute("val");
            m.Stacked = grouping == "stacked" || grouping == "percentStacked";
            m.Percent = grouping == "percentStacked";
            foreach (var ser in t.Elements(C + "ser"))
            {
                var s = new ChartSeries
                {
                    NameRef = (string)ser.Element(C + "tx")?.Descendants(C + "f").FirstOrDefault(),
                    Name = (string)ser.Element(C + "tx")?.Descendants(C + "v").FirstOrDefault(),
                    CatRef = (string)(ser.Element(C + "cat") ?? ser.Element(C + "xVal"))?.Descendants(C + "f").FirstOrDefault(),
                    ValRef = (string)(ser.Element(C + "val") ?? ser.Element(C + "yVal"))?.Descendants(C + "f").FirstOrDefault(),
                };
                if (s.ValRef == null) return null;
                m.Series.Add(s);
            }
            var title = cx.Root.Descendants(C + "title").FirstOrDefault(x => x.Parent?.Name == C + "chart");
            if (title != null) m.Title = string.Concat(title.Descendants(A + "t").Select(x => x.Value));
            else if ((string)cx.Descendants(C + "autoTitleDeleted").FirstOrDefault()?.Attribute("val") == "0" && m.Series.Count == 1) m.Title = "";
            var legend = cx.Descendants(C + "legend").FirstOrDefault();
            m.ShowLegend = legend != null;
            m.LegendPos = (string)legend?.Element(C + "legendPos")?.Attribute("val") ?? "r";
            m.ShowDataLabels = (string)t.Element(C + "dLbls")?.Element(C + "showVal")?.Attribute("val") == "1";
            return m;
        }

        /// <summary>Adds the charts to a saved package (ClosedXML wrote everything else).</summary>
        public static void Write(string path, IEnumerable<ChartModel> charts, XLWorkbook wb)
        {
            var bySheet = charts.GroupBy(c => c.Sheet).ToList();
            using var doc = SpreadsheetDocument.Open(path, true);
            var wbPart = doc.WorkbookPart;
            // ClosedXML can carry chart frames over from the file it loaded; they're replaced by the charts we know about
            foreach (var wsp in wbPart.WorksheetParts)
            {
                var dpOld = wsp.DrawingsPart;
                if (dpOld == null) continue;
                XDocument ox;
                using (var s = dpOld.GetStream()) ox = XDocument.Load(s);
                var frames = ox.Root.Elements().Where(e => e.Descendants(C + "chart").Any()).ToList();
                if (frames.Count == 0) continue;
                foreach (var f in frames)
                {
                    var rid = (string)f.Descendants(C + "chart").First().Attribute(R + "id");
                    try { if (rid != null && dpOld.GetPartById(rid) is ChartPart old) dpOld.DeletePart(old); } catch { }
                    f.Remove();
                }
                using (var s = dpOld.GetStream(FileMode.Create)) ox.Save(s);
            }
            if (bySheet.Count == 0) return;
            foreach (var group in bySheet)
            {
                var sheet = wbPart.Workbook.Sheets.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().FirstOrDefault(s => s.Name == group.Key);
                if (sheet == null) continue;
                var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id);
                var ws = wb.Worksheets.FirstOrDefault(w => w.Name == group.Key);
                DrawingsPart dp = wsPart.DrawingsPart;
                XDocument dx;
                if (dp == null)
                {
                    dp = wsPart.AddNewPart<DrawingsPart>();
                    dx = new XDocument(new XElement(XDR + "wsDr", new XAttribute(XNamespace.Xmlns + "xdr", XDR), new XAttribute(XNamespace.Xmlns + "a", A)));
                    var drawing = new DocumentFormat.OpenXml.Spreadsheet.Drawing { Id = wsPart.GetIdOfPart(dp) };
                    var wsEl = wsPart.Worksheet;
                    // <drawing> goes after pageSetup/headerFooter/rowBreaks/colBreaks and before legacyDrawing/tableParts/extLst
                    var before = wsEl.Elements().FirstOrDefault(e => e.LocalName is "legacyDrawing" or "legacyDrawingHF" or "picture" or "oleObjects" or "controls" or "webPublishItems" or "tableParts" or "extLst");
                    if (before != null) wsEl.InsertBefore(drawing, before); else wsEl.Append(drawing);
                    wsEl.Save();
                }
                else using (var s = dp.GetStream()) dx = XDocument.Load(s);
                int id = 1000;
                foreach (var ch in group)
                {
                    var cp = dp.AddNewPart<ChartPart>();
                    var xml = ToChartXml(ch);
                    using (var s = cp.GetStream(FileMode.Create)) xml.Save(s);
                    var rid = dp.GetIdOfPart(cp);
                    dx.Root.Add(Anchor(ch, rid, ++id, ws));
                }
                using (var s = dp.GetStream(FileMode.Create)) dx.Save(s);
            }
        }

        static XElement Anchor(ChartModel ch, string rid, int id, IXLWorksheet ws)
        {
            // work out the bottom-right cell from real column widths/row heights
            double ColPx(int c) => ws == null ? 64 : Grid.Units.ColWidthToPx(ws.Column(c).IsHidden ? 0 : ws.Column(c).Width);
            double RowPx(int r) => ws == null ? 20 : Grid.Units.PtToPx(ws.Row(r).IsHidden ? 0 : ws.Row(r).Height);
            int c2 = ch.Col; double x = ch.OffsetX + ch.Width;
            while (x > ColPx(c2) && c2 < 16384) { x -= ColPx(c2); c2++; }
            int r2 = ch.Row; double y = ch.OffsetY + ch.Height;
            while (y > RowPx(r2) && r2 < 1048576) { y -= RowPx(r2); r2++; }
            XElement Pos(string name, int col, double colOff, int row, double rowOff) => new XElement(XDR + name,
                new XElement(XDR + "col", col - 1), new XElement(XDR + "colOff", (long)(colOff * EmuPerPx)),
                new XElement(XDR + "row", row - 1), new XElement(XDR + "rowOff", (long)(rowOff * EmuPerPx)));
            return new XElement(XDR + "twoCellAnchor",
                Pos("from", ch.Col, ch.OffsetX, ch.Row, ch.OffsetY),
                Pos("to", c2, x, r2, y),
                new XElement(XDR + "graphicFrame", new XAttribute("macro", ""),
                    new XElement(XDR + "nvGraphicFramePr",
                        new XElement(XDR + "cNvPr", new XAttribute("id", id), new XAttribute("name", "Chart " + (id - 1000))),
                        new XElement(XDR + "cNvGraphicFramePr")),
                    new XElement(XDR + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(A + "ext", new XAttribute("cx", 0), new XAttribute("cy", 0))),
                    new XElement(A + "graphic",
                        new XElement(A + "graphicData", new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/chart"),
                            new XElement(C + "chart", new XAttribute(XNamespace.Xmlns + "c", C), new XAttribute(XNamespace.Xmlns + "r", R), new XAttribute(R + "id", rid))))),
                new XElement(XDR + "clientData"));
        }

        static XElement Val(string name, object v) => new XElement(C + name, new XAttribute("val", v));

        static XDocument ToChartXml(ChartModel ch)
        {
            var colors = ChartPalette.Hex;
            XElement Ser(ChartSeries s, int i)
            {
                var e = new XElement(C + "ser", Val("idx", i), Val("order", i));
                if (!string.IsNullOrEmpty(s.NameRef)) e.Add(new XElement(C + "tx", new XElement(C + "strRef", new XElement(C + "f", s.NameRef))));
                else if (!string.IsNullOrEmpty(s.Name)) e.Add(new XElement(C + "tx", new XElement(C + "v", s.Name)));
                if (ch.Kind != ChartKind.Pie && ch.Kind != ChartKind.Doughnut)
                {
                    var fill = new XElement(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", colors[i % colors.Length])));
                    e.Add(new XElement(C + "spPr", ch.Kind == ChartKind.Line || ch.Kind == ChartKind.Scatter
                        ? new XElement(A + "ln", new XAttribute("w", 28575), fill)
                        : fill));
                }
                if (ch.Kind == ChartKind.Line) e.Add(new XElement(C + "marker", Val("symbol", "circle"), Val("size", 6)));
                if (ch.Kind == ChartKind.Scatter)
                {
                    e.Add(new XElement(C + "marker", Val("symbol", "circle"), Val("size", 6)));
                    if (!string.IsNullOrEmpty(s.CatRef)) e.Add(new XElement(C + "xVal", new XElement(C + "numRef", new XElement(C + "f", s.CatRef))));
                    e.Add(new XElement(C + "yVal", new XElement(C + "numRef", new XElement(C + "f", s.ValRef))));
                    e.Add(Val("smooth", 0));
                }
                else
                {
                    if (!string.IsNullOrEmpty(s.CatRef)) e.Add(new XElement(C + "cat", new XElement(C + "strRef", new XElement(C + "f", s.CatRef))));
                    e.Add(new XElement(C + "val", new XElement(C + "numRef", new XElement(C + "f", s.ValRef))));
                    if (ch.Kind == ChartKind.Line) e.Add(Val("smooth", 0));
                }
                return e;
            }
            XElement typeEl;
            string grouping = ch.Percent ? "percentStacked" : ch.Stacked ? "stacked" : (ch.Kind == ChartKind.Line || ch.Kind == ChartKind.Area ? "standard" : "clustered");
            var series = ch.Series.Select(Ser).ToList();
            var dl = new XElement(C + "dLbls", Val("showLegendKey", 0), Val("showVal", ch.ShowDataLabels ? 1 : 0), Val("showCatName", 0), Val("showSerName", 0), Val("showPercent", 0), Val("showBubbleSize", 0));
            var axIds = new object[] { Val("axId", 500000001), Val("axId", 500000002) };
            switch (ch.Kind)
            {
                case ChartKind.Bar:
                case ChartKind.Column:
                    typeEl = new XElement(C + "barChart", Val("barDir", ch.Kind == ChartKind.Bar ? "bar" : "col"), Val("grouping", grouping), Val("varyColors", 0), series, dl, Val("gapWidth", 150), ch.Stacked ? Val("overlap", 100) : null, axIds);
                    break;
                case ChartKind.Line:
                    typeEl = new XElement(C + "lineChart", Val("grouping", ch.Stacked ? grouping : "standard"), Val("varyColors", 0), series, dl, Val("marker", 1), axIds);
                    break;
                case ChartKind.Area:
                    typeEl = new XElement(C + "areaChart", Val("grouping", ch.Stacked ? grouping : "standard"), Val("varyColors", 0), series, dl, axIds);
                    break;
                case ChartKind.Scatter:
                    typeEl = new XElement(C + "scatterChart", Val("scatterStyle", "lineMarker"), Val("varyColors", 0), series, dl, axIds);
                    break;
                case ChartKind.Doughnut:
                    typeEl = new XElement(C + "doughnutChart", Val("varyColors", 1), series, dl, Val("firstSliceAng", 0), Val("holeSize", 50));
                    break;
                default:
                    typeEl = new XElement(C + "pieChart", Val("varyColors", 1), series, dl, Val("firstSliceAng", 0));
                    break;
            }
            var plot = new XElement(C + "plotArea", new XElement(C + "layout"), typeEl);
            if (ch.Kind != ChartKind.Pie && ch.Kind != ChartKind.Doughnut)
            {
                bool bar = ch.Kind == ChartKind.Bar;
                var catAx = ch.Kind == ChartKind.Scatter
                    ? new XElement(C + "valAx", Val("axId", 500000001), new XElement(C + "scaling", Val("orientation", "minMax")), Val("delete", 0), Val("axPos", "b"), new XElement(C + "numFmt", new XAttribute("formatCode", "General"), new XAttribute("sourceLinked", 1)), Val("tickLblPos", "nextTo"), Val("crossAx", 500000002), Val("crosses", "autoZero"), Val("crossBetween", "midCat"))
                    : new XElement(C + "catAx", Val("axId", 500000001), new XElement(C + "scaling", Val("orientation", "minMax")), Val("delete", 0), Val("axPos", bar ? "l" : "b"), Val("tickLblPos", "nextTo"), Val("crossAx", 500000002), Val("crosses", "autoZero"), Val("auto", 1), Val("lblAlgn", "ctr"), Val("lblOffset", 100));
                var valAx = new XElement(C + "valAx", Val("axId", 500000002), new XElement(C + "scaling", Val("orientation", "minMax")), Val("delete", 0), Val("axPos", bar ? "b" : "l"),
                    new XElement(C + "majorGridlines"), new XElement(C + "numFmt", new XAttribute("formatCode", ch.Percent ? "0%" : "General"), new XAttribute("sourceLinked", 1)),
                    Val("tickLblPos", "nextTo"), Val("crossAx", 500000001), Val("crosses", "autoZero"), Val("crossBetween", ch.Kind == ChartKind.Area ? "midCat" : "between"));
                plot.Add(catAx, valAx);
            }
            var chart = new XElement(C + "chart");
            if (!string.IsNullOrEmpty(ch.Title))
                chart.Add(new XElement(C + "title", new XElement(C + "tx", new XElement(C + "rich", new XElement(A + "bodyPr"), new XElement(A + "lstStyle"),
                    new XElement(A + "p", new XElement(A + "pPr", new XElement(A + "defRPr")), new XElement(A + "r", new XElement(A + "t", ch.Title))))), Val("overlay", 0)));
            chart.Add(Val("autoTitleDeleted", string.IsNullOrEmpty(ch.Title) ? 1 : 0));
            chart.Add(plot);
            if (ch.ShowLegend) chart.Add(new XElement(C + "legend", Val("legendPos", ch.LegendPos ?? "r"), Val("overlay", 0)));
            chart.Add(Val("plotVisOnly", 1));
            chart.Add(Val("dispBlanksAs", "gap"));
            var root = new XElement(C + "chartSpace", new XAttribute(XNamespace.Xmlns + "c", C), new XAttribute(XNamespace.Xmlns + "a", A), new XAttribute(XNamespace.Xmlns + "r", R),
                Val("roundedCorners", 0), Val("style", ch.StyleIndex), chart);
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);
        }

        /// <summary>Builds a chart from a block of data, like Excel's Insert → Chart.</summary>
        public static ChartModel FromRange(IXLWorksheet ws, CellRect r, ChartKind kind)
        {
            var m = new ChartModel { Sheet = ws.Name, Kind = kind };
            bool TextCell(int row, int col) { var v = Ops.SafeValue(ws.Cell(row, col)); return v.IsText || v.IsBlank; }
            bool header = r.Rows > 1 && Enumerable.Range(r.C1, r.Cols).Any(c => TextCell(r.R1, c) && !ws.Cell(r.R1, c).IsEmpty()) && !Enumerable.Range(r.C1 + (r.Cols > 1 ? 1 : 0), Math.Max(1, r.Cols - 1)).All(c => TextCell(r.R1 + 1, c));
            bool cats = r.Cols > 1 && Enumerable.Range(r.R1 + (header ? 1 : 0), r.Rows - (header ? 1 : 0)).All(row => TextCell(row, r.C1)) || (r.Cols > 1 && kind != ChartKind.Scatter && header && TextCell(r.R1, r.C1) && ws.Cell(r.R1, r.C1).IsEmpty());
            if (kind == ChartKind.Scatter && r.Cols > 1) cats = true;
            int dr1 = r.R1 + (header ? 1 : 0), dc1 = r.C1 + (cats ? 1 : 0);
            bool byCols = (r.Rows - (header ? 1 : 0)) >= (r.Cols - (cats ? 1 : 0)) || kind == ChartKind.Scatter;
            string Ref(int r1, int c1, int r2, int c2) => Quote(ws.Name) + "!$" + A1.Col(c1) + "$" + r1 + (r1 == r2 && c1 == c2 ? "" : ":$" + A1.Col(c2) + "$" + r2);
            if (byCols)
            {
                for (int c = dc1; c <= r.C2; c++)
                    m.Series.Add(new ChartSeries { NameRef = header ? Ref(r.R1, c, r.R1, c) : null, Name = header ? null : "Series" + (c - dc1 + 1), CatRef = cats ? Ref(dr1, r.C1, r.R2, r.C1) : null, ValRef = Ref(dr1, c, r.R2, c) });
            }
            else
            {
                for (int row = dr1; row <= r.R2; row++)
                    m.Series.Add(new ChartSeries { NameRef = cats ? Ref(row, r.C1, row, r.C1) : null, Name = cats ? null : "Series" + (row - dr1 + 1), CatRef = header ? Ref(r.R1, dc1, r.R1, r.C2) : null, ValRef = Ref(row, dc1, row, r.C2) });
            }
            if (m.Series.Count == 1 && (kind == ChartKind.Pie || kind == ChartKind.Doughnut || kind == ChartKind.Column)) m.Title = "";
            if (kind == ChartKind.Pie || kind == ChartKind.Doughnut) { m.Series = m.Series.Take(1).ToList(); }
            m.Row = r.R1; m.Col = r.C2 + 2;
            return m;
        }
    }

    public static class ChartPalette
    {
        public static readonly string[] Hex = { "4F81BD", "C0504D", "9BBB59", "8064A2", "4BACC6", "F79646", "2C4D75", "772C2A", "5F7530", "4D3B62" };
    }

    /// <summary>The data a chart shows, read from the cells.</summary>
    public class ChartData
    {
        public List<string> Categories = new();
        public List<(string Name, List<double?> Values, List<double?> X)> Series = new();

        public static ChartData Read(ChartModel m, XLWorkbook wb)
        {
            var d = new ChartData();
            foreach (var s in m.Series)
            {
                var vals = Values(wb, s.ValRef);
                var xs = m.Kind == ChartKind.Scatter ? Values(wb, s.CatRef) : null;
                string name = s.Name;
                if (!string.IsNullOrEmpty(s.NameRef)) name = Texts(wb, s.NameRef).FirstOrDefault() ?? name;
                d.Series.Add((name ?? "Series" + (d.Series.Count + 1), vals, xs));
                if (d.Categories.Count == 0 && m.Kind != ChartKind.Scatter && !string.IsNullOrEmpty(s.CatRef)) d.Categories = Texts(wb, s.CatRef);
            }
            int n = d.Series.Count == 0 ? 0 : d.Series.Max(x => x.Values.Count);
            while (d.Categories.Count < n) d.Categories.Add((d.Categories.Count + 1).ToString(CultureInfo.CurrentCulture));
            return d;
        }

        static IEnumerable<IXLCell> Cells(XLWorkbook wb, string reference)
        {
            if (string.IsNullOrEmpty(reference)) return Enumerable.Empty<IXLCell>();
            try { return wb.Range(reference.Replace("$", "")).Cells(); }
            catch
            {
                try
                {
                    var i = reference.LastIndexOf('!');
                    var sheet = reference.Substring(0, i).Trim('\'').Replace("''", "'");
                    var ws = wb.Worksheet(sheet);
                    return ws.Range(reference.Substring(i + 1).Replace("$", "")).Cells();
                }
                catch { return Enumerable.Empty<IXLCell>(); }
            }
        }

        static List<double?> Values(XLWorkbook wb, string reference) => Cells(wb, reference).Select(c =>
        {
            var v = Ops.SafeValue(c);
            return v.IsNumber ? v.GetNumber() : v.IsDateTime ? v.GetDateTime().ToOADate() : (double?)null;
        }).ToList();

        static List<string> Texts(XLWorkbook wb, string reference) => Cells(wb, reference).Select(c => { try { return c.GetFormattedString(); } catch { return ""; } }).ToList();
    }

    /// <summary>Lists the module names in a VBA project (for the Macros dialog). vbaProject.bin is a compound file; the module streams sit under VBA/.</summary>
    public static class VbaModules
    {
        public static List<string> List(byte[] vba)
        {
            var names = new List<string>();
            if (vba == null) return names;
            try
            {
                // the dir stream is compressed; the directory entries of the compound file are not, so read names from there
                int sectorSize = 1 << BitConverter.ToUInt16(vba, 30);
                int dirStart = BitConverter.ToInt32(vba, 48);
                // walk the FAT chain of the directory
                var fat = ReadFat(vba, sectorSize);
                int sector = dirStart, guard = 0;
                while (sector >= 0 && sector < fat.Length && guard++ < 1000)
                {
                    int off = (sector + 1) * sectorSize;
                    for (int e = 0; e + 128 <= sectorSize && off + e + 128 <= vba.Length; e += 128)
                    {
                        int len = BitConverter.ToUInt16(vba, off + e + 64);
                        byte type = vba[off + e + 66];
                        if (len < 2 || type != 2) continue;
                        var name = Encoding.Unicode.GetString(vba, off + e, len - 2);
                        if (name == "dir" || name.StartsWith("_VBA_PROJECT") || name == "PROJECT" || name == "PROJECTwm" || name.StartsWith("__SRP")) continue;
                        names.Add(name);
                    }
                    sector = fat[sector];
                }
            }
            catch { }
            return names.Distinct().OrderBy(n => n).ToList();
        }

        static int[] ReadFat(byte[] b, int sectorSize)
        {
            int fatSectors = BitConverter.ToInt32(b, 44);
            var list = new List<int>();
            for (int i = 0; i < Math.Min(fatSectors, 109); i++)
            {
                int s = BitConverter.ToInt32(b, 76 + i * 4);
                int off = (s + 1) * sectorSize;
                for (int k = 0; k < sectorSize / 4 && off + k * 4 + 4 <= b.Length; k++) list.Add(BitConverter.ToInt32(b, off + k * 4));
            }
            return list.ToArray();
        }
    }
}
