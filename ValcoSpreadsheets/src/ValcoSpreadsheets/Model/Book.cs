using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ClosedXML.Excel;

namespace ValcoSpreadsheets.Model
{
    public enum BookFormat { Xlsx, Xlsm, Xltx, Xltm, Csv, Text, Xls }

    /// <summary>
    /// An open workbook: the ClosedXML workbook plus what ClosedXML itself doesn't keep
    /// (the VBA project of an .xlsm, the file format, the undo history).
    /// Kept free of WPF so it can be tested on its own.
    /// </summary>
    public class Book
    {
        public XLWorkbook Wb { get; private set; }
        public string FilePath { get; private set; }
        public BookFormat Format { get; private set; } = BookFormat.Xlsx;
        public string Title { get; private set; }
        public bool Dirty { get; set; }
        /// <summary>vbaProject.bin from an .xlsm, written back when it's saved as .xlsm again.</summary>
        public byte[] VbaProject { get; private set; }
        /// <summary>Parts of the original file that ClosedXML drops on save (charts, pivot tables...).</summary>
        public List<string> UnkeptParts { get; } = new List<string>();
        public readonly UndoStack Undo;
        public event Action Changed;

        static int _untitled;

        public Book()
        {
            Wb = NewWorkbook();
            Title = "Book" + (++_untitled);
            Undo = new UndoStack(this);
        }

        public static XLWorkbook NewWorkbook(int sheets = 3)
        {
            var wb = new XLWorkbook();
            for (int i = 1; i <= sheets; i++) wb.AddWorksheet("Sheet" + i);
            return wb;
        }

        public bool HasMacros => VbaProject != null;
        public string DisplayName => FilePath != null ? Path.GetFileName(FilePath) : Title;
        public bool IsTemplate => Format == BookFormat.Xltx || Format == BookFormat.Xltm;

        public void RaiseChanged() { Dirty = true; Changed?.Invoke(); }

        /// <summary>Replaces the whole workbook (undo of structural changes, reload).</summary>
        public void ReplaceWorkbook(XLWorkbook wb) { var old = Wb; Wb = wb; if (!ReferenceEquals(old, wb)) try { old.Dispose(); } catch { } }

        public static BookFormat FormatFromPath(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".xlsm": return BookFormat.Xlsm;
                case ".xltx": return BookFormat.Xltx;
                case ".xltm": return BookFormat.Xltm;
                case ".csv": return BookFormat.Csv;
                case ".txt": case ".tsv": case ".tab": case ".prn": return BookFormat.Text;
                case ".xls": case ".xlt": return BookFormat.Xls;
                default: return BookFormat.Xlsx;
            }
        }

        // ============================================================ opening
        public static Book Open(string path)
        {
            var b = new Book();
            _untitled--;
            b.Load(path);
            return b;
        }

        void Load(string path)
        {
            var fmt = FormatFromPath(path);
            XLWorkbook wb;
            switch (fmt)
            {
                case BookFormat.Csv:
                case BookFormat.Text:
                    wb = Delimited.Import(path, fmt == BookFormat.Csv ? ',' : '\t');
                    break;
                case BookFormat.Xls:
                    VbaProject = null;
                    UnkeptParts.Clear();
                    try
                    {
                        var x = XlsFile.Import(path);
                        wb = x.Workbook;
                        VbaProject = x.VbaProject;
                        if (x.HasCharts) UnkeptParts.Add("charts");
                    }
                    catch (NPOI.HSSF.OldExcelFormatException)
                    {
                        // Excel 5.0/95 files: values and number formats only
                        wb = LegacyXls.Import(path);
                    }
                    break;
                default:
                    ReadExtras(path);
                    var charts = ChartIO.Read(path, out bool allKept);
                    ChartStore.Set(this, charts);
                    if (allKept) UnkeptParts.Remove("charts");
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        var ms = new MemoryStream();
                        fs.CopyTo(ms);
                        ms.Position = 0;
                        wb = new XLWorkbook(ms);
                    }
                    break;
            }
            Wb = wb;
            NormalizeChartSizes();
            FilePath = path;
            Format = fmt;
            Title = Path.GetFileNameWithoutExtension(path);
            // a template opens as a new workbook based on it, like Excel
            if (IsTemplate) { FilePath = null; Title = Title + "1"; }
            Dirty = false;
        }

        /// <summary>Charts anchored by their bottom-right cell get their pixel size from the real column widths and row heights.</summary>
        void NormalizeChartSizes()
        {
            foreach (var c in ChartStore.For(this))
            {
                if (c.ToCol <= 0) continue;
                var ws = Wb.Worksheets.FirstOrDefault(w => w.Name == c.Sheet);
                if (ws == null) continue;
                double w = -c.OffsetX, h = -c.OffsetY;
                for (int col = c.Col; col < c.ToCol && col < c.Col + 200; col++) w += Grid.Units.ColWidthToPx(ws.Column(col).IsHidden ? 0 : ws.Column(col).Width);
                for (int row = c.Row; row < c.ToRow && row < c.Row + 500; row++) h += Grid.Units.PtToPx(ws.Row(row).IsHidden ? 0 : ws.Row(row).Height);
                w += c.ToOffX; h += c.ToOffY;
                if (w > 20) c.Width = w;
                if (h > 20) c.Height = h;
            }
        }

        /// <summary>Reads what ClosedXML won't keep: the VBA project, and notes charts / pivot tables that saving would drop.</summary>
        void ReadExtras(string path)
        {
            VbaProject = null;
            UnkeptParts.Clear();
            try
            {
                using var zip = ZipFile.OpenRead(path);
                foreach (var e in zip.Entries)
                {
                    var n = e.FullName.ToLowerInvariant();
                    if (n.EndsWith("vbaproject.bin"))
                    {
                        using var s = e.Open();
                        var ms = new MemoryStream();
                        s.CopyTo(ms);
                        VbaProject = ms.ToArray();
                    }
                    else if (n.StartsWith("xl/charts/chart") && !UnkeptParts.Contains("charts")) UnkeptParts.Add("charts");
                    else if (n.StartsWith("xl/pivottables/") && !UnkeptParts.Contains("PivotTables")) UnkeptParts.Add("PivotTables");
                    else if (n.StartsWith("xl/slicers/") && !UnkeptParts.Contains("slicers")) UnkeptParts.Add("slicers");
                    else if (n.StartsWith("xl/activex/") && !UnkeptParts.Contains("ActiveX controls")) UnkeptParts.Add("ActiveX controls");
                }
            }
            catch { }
        }

        // ============================================================ saving
        public void SaveAs(string path, BookFormat fmt)
        {
            string tmp = path + ".valco-tmp";
            try
            {
                switch (fmt)
                {
                    case BookFormat.Csv: Delimited.Export(ActiveSheetForExport, tmp, ','); break;
                    case BookFormat.Text: Delimited.Export(ActiveSheetForExport, tmp, '\t'); break;
                    case BookFormat.Xls: XlsFile.Export(this, tmp); break;
                    default:
                        // ClosedXML picks the package type from the extension, so save under a matching temporary name
                        var ext = fmt switch { BookFormat.Xlsm => ".xlsm", BookFormat.Xltx => ".xltx", BookFormat.Xltm => ".xltm", _ => ".xlsx" };
                        var t2 = tmp + ext;
                        Wb.SaveAs(t2, new SaveOptions { EvaluateFormulasBeforeSaving = false, ValidatePackage = false });
                        if (File.Exists(tmp)) File.Delete(tmp);
                        File.Move(t2, tmp);
                        var charts = ChartStore.For(this).Where(c => Wb.Worksheets.Any(w => w.Name == c.Sheet)).ToList();
                        ChartIO.Write(tmp, charts, Wb);
                        if ((fmt == BookFormat.Xlsm || fmt == BookFormat.Xltm) && VbaProject != null) Macros.Inject(tmp, VbaProject, fmt == BookFormat.Xltm);
                        break;
                }
                if (File.Exists(path)) File.Replace(tmp, path, null, true);
                else File.Move(tmp, path);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
            FilePath = path;
            Format = fmt;
            Title = Path.GetFileNameWithoutExtension(path);
            Dirty = false;
        }

        /// <summary>CSV/Text save only the active sheet (like Excel).</summary>
        public IXLWorksheet ActiveSheetForExport { get; set; }

        /// <summary>A workbook brought back by AutoRecover: no file yet, unsaved changes.</summary>
        public void MarkRecovered()
        {
            Title = (Title ?? "Book").Split(new[] { " - " }, 2, StringSplitOptions.None).Last() + " (Recovered)";
            FilePath = null;
            Dirty = true;
        }

        /// <summary>Drops the VBA project (Save As .xlsx of a macro workbook).</summary>
        public void DiscardMacros() => VbaProject = null;
    }

    /// <summary>Keeps an .xlsm's VBA project: ClosedXML doesn't, so it is put back into the saved package.</summary>
    public static class Macros
    {
        public static void Inject(string path, byte[] vba, bool template)
        {
            using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(path, true);
            doc.ChangeDocumentType(template ? DocumentFormat.OpenXml.SpreadsheetDocumentType.MacroEnabledTemplate : DocumentFormat.OpenXml.SpreadsheetDocumentType.MacroEnabledWorkbook);
            var wbPart = doc.WorkbookPart;
            foreach (var old in wbPart.GetPartsOfType<DocumentFormat.OpenXml.Packaging.VbaProjectPart>().ToList()) wbPart.DeletePart(old);
            var part = wbPart.AddNewPart<DocumentFormat.OpenXml.Packaging.VbaProjectPart>();
            using (var ms = new MemoryStream(vba)) part.FeedData(ms);
        }
    }
}
