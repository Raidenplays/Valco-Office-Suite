using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ClosedXML.Excel;
using ValcoSpreadsheets.Grid;
using ValcoSpreadsheets.Model;
using Path = System.Windows.Shapes.Path;

namespace ValcoSpreadsheets
{
    /// <summary>The Excel 2007 Ribbon: Home, Insert, Page Layout, Formulas, Data, Review, View.</summary>
    public partial class MainWindow
    {
        RibbonBar Ribbon;
        ValcoTitleBar _title;
        ComboBox CbFont, CbSize, CbNumFmt;
        ToggleButton TbBold, TbItalic, TbUnderline, TbLeft, TbCenter, TbRight, TbTop, TbMiddle, TbBottom, TbWrap;
        CheckBox CkGridView, CkGridPrint, CkHeadView, CkHeadPrint, CkGridlines2, CkFormulaBar, CkHeadings2;
        Rectangle FontColorSwatch, FillColorSwatch;
        Color _fontColor = Color.FromRgb(0xFF, 0x00, 0x00), _fillColor = Color.FromRgb(0xFF, 0xFF, 0x00);
        FrameworkElement _fontColorBtn, _fillColorBtn, _bordersBtn;
        Button BtnUndoQat => _title?.QatButtons.TryGetValue("Undo", out var b) == true ? b : null;
        Button BtnRedoQat => _title?.QatButtons.TryGetValue("Redo", out var b) == true ? b : null;
        BorderKind _lastBorder = BorderKind.Bottom;
        bool _ribbonSync;

        static UIElement GL(string text, FontWeight? w = null, FontStyle? s = null, TextDecorationCollection d = null, double size = 15)
        {
            var tb = new TextBlock { Text = text, FontFamily = new FontFamily("Times New Roman"), FontSize = size, Margin = new Thickness(0, -2, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Rb.Text");
            if (w.HasValue) tb.FontWeight = w.Value;
            if (s.HasValue) tb.FontStyle = s.Value;
            if (d != null) tb.TextDecorations = d;
            return tb;
        }

        static FrameworkElement SwatchIcon(string icon, out Rectangle swatch, Color c)
        {
            var sp = new StackPanel();
            sp.Children.Add(new Image { Source = UI.Img(icon), Width = 16, Height = 13 });
            swatch = new Rectangle { Height = 3, Width = 16, Fill = new SolidColorBrush(c) };
            sp.Children.Add(swatch);
            return sp;
        }

        static Image Ico(string key) => new Image { Source = UI.Img(key), Width = 16, Height = 16 };

        CheckBox Check(string label, Action<bool> changed)
        {
            var c = new CheckBox { Content = label, Margin = new Thickness(2, 2, 6, 3), VerticalAlignment = VerticalAlignment.Center };
            c.SetResourceReference(Control.ForegroundProperty, "Rb.Text");
            c.Click += (s, e) => changed(c.IsChecked == true);
            return c;
        }

        static TextBlock Lbl(string text)
        {
            var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Rb.Text");
            return t;
        }

        // =====================================================================================
        void BuildRibbon()
        {
            OfficeTheme.Apply(Settings.Current.ColorScheme);
            Ribbon = new RibbonBar();
            RibbonHost.Children.Add(Ribbon);
            FormulaBar.Visibility = Settings.Current.ShowFormulaBar ? Visibility.Visible : Visibility.Collapsed;
            StatusBar.Visibility = Settings.Current.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;

            // ---------------- Home ----------------
            CbFont = RB.Combo(130, true);
            CbFont.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(f => f).ToList();
            CbFont.ItemTemplate = FontItemTemplate();
            CbFont.IsTextSearchEnabled = true;
            CbFont.ToolTip = RB.Tip("Font", "Change the font face.");
            CbFont.DropDownClosed += (s, e) => { if (CbFont.SelectedItem is string f && !_ribbonSync) { SetFontName(f); G.Focus(); } };
            CbFont.KeyDown += (s, e) => { if (e.Key == Key.Enter) { SetFontName(CbFont.Text); G.Focus(); e.Handled = true; } };
            CbSize = RB.Combo(44, true);
            CbSize.ItemsSource = FontSizes.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList();
            CbSize.ToolTip = RB.Tip("Font Size", "Change the font size.");
            CbSize.DropDownClosed += (s, e) => { if (CbSize.SelectedItem is string v && !_ribbonSync && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { SetFontSize(d); G.Focus(); } };
            CbSize.KeyDown += (s, e) => { if (e.Key == Key.Enter) { if (double.TryParse(CbSize.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var d)) SetFontSize(d); G.Focus(); e.Handled = true; } };

            TbBold = RB.Glyph(GL("B", FontWeights.Bold), () => ToggleFont("Bold"), "Bold (Ctrl+B)", "Make the selected text bold.");
            TbItalic = RB.Glyph(GL("I", null, FontStyles.Italic), () => ToggleFont("Italic"), "Italic (Ctrl+I)", "Italicize the selected text.");
            TbUnderline = RB.Glyph(GL("U", null, null, TextDecorations.Underline), () => ToggleFont("Underline"), "Underline (Ctrl+U)", "Underline the selected text.");
            _bordersBtn = RB.Split("IconBorders", null, () => ApplyBorder(_lastBorder, _borderColor, _borderStyle), BordersMenu, "Borders");
            _fillColorBtn = RB.Split(null, null, () => SetFillColor(_fillColor), () => { ColorPicker.Show(_fillColorBtn, "No Fill", c => { if (c.HasValue) { _fillColor = c.Value; FillColorSwatch.Fill = new SolidColorBrush(c.Value); } SetFillColor(c); }); return null; }, "Fill Color", false, SwatchIcon("IconFillColor", out FillColorSwatch, _fillColor));
            _fontColorBtn = RB.Split(null, null, () => SetFontColor(_fontColor), () => { ColorPicker.Show(_fontColorBtn, "Automatic", c => { if (c.HasValue) { _fontColor = c.Value; FontColorSwatch.Fill = new SolidColorBrush(c.Value); } SetFontColor(c); }); return null; }, "Font Color", false, SwatchIcon("IconFontColor", out FontColorSwatch, _fontColor));
            var fontGroup = RB.Group("Font", () => FormatCells("Font"),
                RB.Stack(
                    RB.Row(CbFont, CbSize, RB.Small("IconGrow", null, () => GrowFont(1), "Increase Font Size"), RB.Small("IconShrink", null, () => GrowFont(-1), "Decrease Font Size")),
                    RB.Row(TbBold, TbItalic, TbUnderline, RB.Sep(), _bordersBtn, RB.Sep(), _fillColorBtn, _fontColorBtn)));

            TbTop = RB.Glyph(Ico("IconVTop"), () => SetVAlign(XLAlignmentVerticalValues.Top), "Top Align", "Align text to the top of the cell.");
            TbMiddle = RB.Glyph(Ico("IconVMiddle"), () => SetVAlign(XLAlignmentVerticalValues.Center), "Middle Align", "Align text so that it is centered between the top and bottom of the cell.");
            TbBottom = RB.Glyph(Ico("IconVBottom"), () => SetVAlign(XLAlignmentVerticalValues.Bottom), "Bottom Align", "Align text to the bottom of the cell.");
            TbLeft = RB.Glyph(Ico("IconAlignLeft"), () => SetHAlign(XLAlignmentHorizontalValues.Left), "Align Text Left", "Align text to the left.");
            TbCenter = RB.Glyph(Ico("IconAlignCenter"), () => SetHAlign(XLAlignmentHorizontalValues.Center), "Center", "Center text.");
            TbRight = RB.Glyph(Ico("IconAlignRight"), () => SetHAlign(XLAlignmentHorizontalValues.Right), "Align Text Right", "Align text to the right.");
            TbWrap = RB.Toggle("IconWrapText", "Wrap Text", b => ToggleWrap(), "Wrap Text");
            TbWrap.ToolTip = RB.Tip("Wrap Text", "Make all content visible within a cell by displaying it on multiple lines.");
            var merge = RB.Split("IconMergeCenter", "Merge & Center", () => MergeCells(G.ActiveCell.IsMerged() ? "Unmerge" : "Center"), () => RB.Menu(
                RB.MI("Merge & Center", () => MergeCells("Center"), "IconMergeCenter"),
                RB.MI("Merge Across", () => MergeCells("Across")),
                RB.MI("Merge Cells", () => MergeCells("Merge")),
                RB.MI("Unmerge Cells", () => MergeCells("Unmerge"))), "Merge & Center");
            var orient = RB.Small("IconOrientation", null, null, "Orientation", () => RB.Menu(
                RB.MI("Angle Counterclockwise", () => SetRotation(45)), RB.MI("Angle Clockwise", () => SetRotation(135)),
                RB.MI("Vertical Text", () => SetRotation(255)), RB.MI("Rotate Text Up", () => SetRotation(90)), RB.MI("Rotate Text Down", () => SetRotation(180)),
                new Separator(), RB.MI("Format Cell Alignment", () => FormatCells("Alignment"))));
            var alignGroup = RB.Group("Alignment", () => FormatCells("Alignment"),
                RB.Stack(
                    RB.Row(TbTop, TbMiddle, TbBottom, RB.Sep(), orient, RB.Sep(), TbWrap),
                    RB.Row(TbLeft, TbCenter, TbRight, RB.Sep(), RB.Small("IconOutdent", null, () => Indent(-1), "Decrease Indent"), RB.Small("IconIndent", null, () => Indent(1), "Increase Indent"), RB.Sep(), merge)));

            CbNumFmt = RB.Combo(110, false);
            CbNumFmt.ItemsSource = NumberFormats.Select(n => n.Name).ToList();
            CbNumFmt.ToolTip = RB.Tip("Number Format", "Choose how the values in a cell are displayed: as a percentage, as currency, as a date or time, and so on.");
            CbNumFmt.SelectionChanged += (s, e) =>
            {
                if (_ribbonSync || CbNumFmt.SelectedIndex < 0) return;
                var nf = NumberFormats[CbNumFmt.SelectedIndex];
                if (nf.Format == null) { FormatCells("Number"); return; }
                ApplyNumberFormat(nf.Format, "Number Format");
                G.Focus();
            };
            var numGroup = RB.Group("Number", () => FormatCells("Number"),
                RB.Stack(
                    RB.Row(CbNumFmt),
                    RB.Row(
                        RB.Split("IconCurrency", null, () => ApplyNumberFormat("_(\"$\"* #,##0.00_);_(\"$\"* \\(#,##0.00\\);_(\"$\"* \"-\"??_);_(@_)", "Accounting"), () => RB.Menu(
                            RB.MI("$ English (Canada)", () => ApplyNumberFormat("_(\"$\"* #,##0.00_);_(\"$\"* \\(#,##0.00\\);_(\"$\"* \"-\"??_);_(@_)", "Accounting")),
                            RB.MI("£ English (United Kingdom)", () => ApplyNumberFormat("_-[$£-809]* #,##0.00_-;-[$£-809]* #,##0.00_-;_-[$£-809]* \"-\"??_-;_-@_-", "Accounting")),
                            RB.MI("€ Euro (€ 123)", () => ApplyNumberFormat("_-[$€-2] * #,##0.00_-;-[$€-2] * #,##0.00_-;_-[$€-2] * \"-\"??_-;_-@_-", "Accounting")),
                            new Separator(), RB.MI("More Accounting Formats...", () => FormatCells("Number"))), "Accounting Number Format"),
                        RB.Small("IconPercent", null, () => ApplyNumberFormat("0%", "Percent Style"), "Percent Style (Ctrl+Shift+%)"),
                        RB.Small("IconComma", null, () => ApplyNumberFormat("_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)", "Comma Style"), "Comma Style"),
                        RB.Sep(),
                        RB.Small("IconIncDecimal", null, () => ChangeDecimals(1), "Increase Decimal"),
                        RB.Small("IconDecDecimal", null, () => ChangeDecimals(-1), "Decrease Decimal"))));

            var stylesGroup = RB.Group("Styles", null,
                RB.Large("IconCondFormat", "Conditional\nFormatting", null, "Highlight interesting cells, emphasize unusual values, and visualize data using Data Bars and Color Scales.", CondFormatMenu),
                RB.Large("IconFormatTable", "Format\nas Table", () => FormatAsTable(null), "Quickly format a range of cells and convert it to a Table by choosing a pre-defined Table Style.", null),
                RB.Large("IconCellStyles", "Cell\nStyles", () => CellStylesGallery(), "Quickly format a cell by choosing from pre-defined styles."));

            var cellsGroup = RB.Group("Cells", null,
                RB.Large("IconInsertCells", "Insert", null, "Insert cells, rows, or columns into the sheet or table.", () => RB.Menu(
                    RB.MI("Insert Cells...", InsertCellsDialog, "IconInsertCells"),
                    RB.MI("Insert Sheet Rows", InsertRows, "IconInsertRows"),
                    RB.MI("Insert Sheet Columns", InsertCols, "IconInsertCols"),
                    new Separator(), RB.MI("Insert Sheet", () => InsertSheet(SheetIdx), "IconNewSheet"))),
                RB.Large("IconDeleteCells", "Delete", null, "Delete rows or columns from the table or sheet.", () => RB.Menu(
                    RB.MI("Delete Cells...", DeleteCellsDialog, "IconDeleteCells"),
                    RB.MI("Delete Sheet Rows", () => { var s = G.ActiveRect; G.SelectRect(new CellRect(s.R1, 1, s.R2, SheetLimits.Cols)); DeleteRows(); }, "IconDeleteRows"),
                    RB.MI("Delete Sheet Columns", () => { var s = G.ActiveRect; G.SelectRect(new CellRect(1, s.C1, SheetLimits.Rows, s.C2)); DeleteCols(); }, "IconDeleteCols"),
                    new Separator(), RB.MI("Delete Sheet", () => DeleteSheet(SheetIdx), "IconDelete"))),
                RB.Large("IconFormatCells", "Format", null, "Change the row height or column width, organize sheets, or protect or hide cells.", FormatMenu));

            var editGroup = RB.Group("Editing", null, RB.Stack(
                RB.Split("IconAutoSum", "AutoSum", () => AutoSum("SUM"), () => RB.Menu(
                    RB.MI("Sum", () => AutoSum("SUM")), RB.MI("Average", () => AutoSum("AVERAGE")), RB.MI("Count Numbers", () => AutoSum("COUNT")),
                    RB.MI("Max", () => AutoSum("MAX")), RB.MI("Min", () => AutoSum("MIN")), new Separator(), RB.MI("More Functions...", () => InsertFunction_Click(null, null))), "AutoSum (Alt+=)"),
                RB.Small("IconFillDown", "Fill", null, "Fill", () => RB.Menu(
                    RB.MI("Down", () => FillDirection("Down"), null, "Ctrl+D"), RB.MI("Right", () => FillDirection("Right"), null, "Ctrl+R"),
                    RB.MI("Up", () => FillDirection("Up")), RB.MI("Left", () => FillDirection("Left")), new Separator(),
                    RB.MI("Series...", SeriesDialogShow))),
                RB.Small("IconClear", "Clear", null, "Clear", () => RB.Menu(
                    RB.MI("Clear All", () => ClearSelection(XLClearOptions.All, "Clear All")),
                    RB.MI("Clear Formats", () => ClearSelection(XLClearOptions.NormalFormats | XLClearOptions.ConditionalFormats, "Clear Formats")),
                    RB.MI("Clear Contents", () => ClearSelection(XLClearOptions.Contents, "Clear Contents"), null, "Delete"),
                    RB.MI("Clear Comments", () => ClearSelection(XLClearOptions.Comments, "Clear Comments"))))),
                RB.Large("IconSortAZ", "Sort &\nFilter", null, "Arrange data so that it is easier to analyze.", () => RB.Menu(
                    RB.MI("Sort A to Z", () => SortQuick(false), "IconSortAZ"), RB.MI("Sort Z to A", () => SortQuick(true), "IconSortZA"),
                    RB.MI("Custom Sort...", SortDialogShow, "IconSort"), new Separator(),
                    RB.MI("Filter", ToggleFilter, "IconFilter", "Ctrl+Shift+L", G_FilterOn()), RB.MI("Clear", ClearFilters, "IconClearFilter"), RB.MI("Reapply", ReapplyFilter, "IconReapply"))),
                RB.Large("IconFind", "Find &\nSelect", null, "Find and select specific text, formatting, or type of information within the workbook.", () => RB.Menu(
                    RB.MI("Find...", () => ShowFind(false), "IconFind", "Ctrl+F"), RB.MI("Replace...", () => ShowFind(true), "IconReplace", "Ctrl+H"),
                    RB.MI("Go To...", GoToDialogShow, "IconGoTo", "Ctrl+G"), RB.MI("Go To Special...", GoToSpecialDialog), new Separator(),
                    RB.MI("Formulas", () => SelectSpecial("Formulas")), RB.MI("Comments", () => SelectSpecial("Comments")),
                    RB.MI("Constants", () => SelectSpecial("Constants")), RB.MI("Data Validation", () => SelectSpecial("Validation")))));

            var home = new RibbonTab("Home")
                .Add(RB.Group("Clipboard", null,
                    RB.Split("IconPaste", "Paste", Paste, () => RB.Menu(
                        RB.MI("Paste", Paste, "IconPaste", "Ctrl+V"),
                        RB.MI("Formulas", () => PasteAs(Ops.PasteWhat.Formulas, false, false)),
                        RB.MI("Paste Values", () => PasteAs(Ops.PasteWhat.Values, false, false)),
                        RB.MI("No Borders", () => PasteAs(Ops.PasteWhat.AllExceptBorders, false, false)),
                        RB.MI("Transpose", () => PasteAs(Ops.PasteWhat.All, true, false)),
                        new Separator(),
                        RB.MI("Paste Special...", PasteSpecial, "IconSpecialPaste", "Ctrl+Alt+V")), "Paste the contents of the Clipboard.", true),
                    RB.Stack(
                        RB.Small("IconCut", "Cut", () => CopySelection(true), "Cut (Ctrl+X)"),
                        RB.Small("IconCopy", "Copy", () => CopySelection(false), "Copy (Ctrl+C)"),
                        RB.Small("IconFormatPainter", "Format Painter", FormatPainter, "Format Painter"))))
                .Add(fontGroup).Add(alignGroup).Add(numGroup).Add(stylesGroup).Add(cellsGroup).Add(editGroup);

            // ---------------- Insert ----------------
            var insert = new RibbonTab("Insert")
                .Add(RB.Group("Tables", null,
                    RB.Large("IconTable", "Table", () => FormatAsTable("TableStyleMedium2"), "Create a table to manage and analyze related data. (Ctrl+T)")))
                .Add(RB.Group("Illustrations", null,
                    RB.Large("IconPicture", "Picture", InsertPicture, "Insert a picture from a file.")))
                .Add(RB.Group("Charts", null,
                    RB.Large("IconColumnChart", "Column", () => InsertChart(ChartKind.Column), "Insert a column chart."),
                    RB.Large("IconLineChart", "Line", () => InsertChart(ChartKind.Line), "Insert a line chart."),
                    RB.Large("IconPieChart", "Pie", () => InsertChart(ChartKind.Pie), "Insert a pie chart."),
                    RB.Large("IconBarChart", "Bar", () => InsertChart(ChartKind.Bar), "Insert a bar chart."),
                    RB.Large("IconAreaChart", "Area", () => InsertChart(ChartKind.Area), "Insert an area chart."),
                    RB.Large("IconScatterChart", "Scatter", () => InsertChart(ChartKind.Scatter), "Insert a scatter chart."),
                    RB.Large("IconDoughnutChart", "Doughnut", () => InsertChart(ChartKind.Doughnut), "Insert a doughnut chart.")))
                .Add(RB.Group("Links", null,
                    RB.Large("IconLink", "Hyperlink", InsertHyperlink, "Create a link to a Web page, a picture, an e-mail address, or a program. (Ctrl+K)")))
                .Add(RB.Group("Text", null,
                    RB.Large("IconHeader", "Header\n& Footer", () => PageSetupDialogShow(), "Edit the page header or footer."),
                    RB.Large("IconSymbol", "Symbol", InsertSymbol, "Insert characters that are not on your keyboard.")));

            // ---------------- Page Layout ----------------
            CkGridView = Check("View", b => { WS.ShowGridLines = b; _book.RaiseChanged(); G.Redraw(); SyncSheetToggles(); });
            CkGridPrint = Check("Print", b => { WS.PageSetup.ShowGridlines = b; _book.RaiseChanged(); });
            CkHeadView = Check("View", b => { G.ShowHeadings = b; WS.ShowRowColHeaders = b; G.Relayout(); SyncSheetToggles(); });
            CkHeadPrint = Check("Print", b => { WS.PageSetup.ShowRowAndColumnHeadings = b; _book.RaiseChanged(); });
            var layout = new RibbonTab("Page Layout")
                .Add(RB.Group("Page Setup", PageSetupDialogShow,
                    RB.Large("IconMargins", "Margins", null, "Select the margin sizes for the entire document.", () => RB.Menu(
                        RB.MI("Normal", () => SetMargins(0.75, 0.7, 0.75, 0.7)), RB.MI("Wide", () => SetMargins(1, 1, 1, 1)), RB.MI("Narrow", () => SetMargins(0.75, 0.25, 0.75, 0.25)),
                        new Separator(), RB.MI("Custom Margins...", PageSetupDialogShow))),
                    RB.Large("IconOrientation", "Orientation", null, "Switch the pages between portrait and landscape layouts.", () => RB.Menu(
                        RB.MI("Portrait", () => PageChange(p => p.PageOrientation = XLPageOrientation.Portrait), null, null, WS.PageSetup.PageOrientation != XLPageOrientation.Landscape),
                        RB.MI("Landscape", () => PageChange(p => p.PageOrientation = XLPageOrientation.Landscape), null, null, WS.PageSetup.PageOrientation == XLPageOrientation.Landscape))),
                    RB.Large("IconSize", "Size", null, "Choose a paper size for the current section.", () => RB.Menu(
                        RB.MI("Letter  8.5\" x 11\"", () => PageChange(p => p.PaperSize = XLPaperSize.LetterPaper)),
                        RB.MI("Legal  8.5\" x 14\"", () => PageChange(p => p.PaperSize = XLPaperSize.LegalPaper)),
                        RB.MI("A4  8.27\" x 11.69\"", () => PageChange(p => p.PaperSize = XLPaperSize.A4Paper)),
                        RB.MI("A5  5.83\" x 8.27\"", () => PageChange(p => p.PaperSize = XLPaperSize.A5Paper)),
                        RB.MI("Executive  7.25\" x 10.5\"", () => PageChange(p => p.PaperSize = XLPaperSize.ExecutivePaper)))),
                    RB.Large("IconPrintArea", "Print\nArea", null, "Mark a specific area of the sheet for printing.", () => RB.Menu(
                        RB.MI("Set Print Area", SetPrintArea), RB.MI("Clear Print Area", () => PageChange(p => p.PrintAreas.Clear())))),
                    RB.Large("IconBreaks", "Breaks", null, "Specify where a new page will begin in the printed copy.", () => RB.Menu(
                        RB.MI("Insert Page Break", InsertPageBreak), RB.MI("Remove Page Break", RemovePageBreak), RB.MI("Reset All Page Breaks", () => PageChange(p => { p.RowBreaks.Clear(); p.ColumnBreaks.Clear(); })))),
                    RB.Large("IconPrintTitles", "Print\nTitles", PageSetupDialogShow, "Specify rows and columns to repeat on each printed page.")))
                .Add(RB.Group("Scale to Fit", PageSetupDialogShow,
                    RB.Stack(
                        RB.Small(null, "Width: Automatic / 1 page", null, "Shrink the width of printout to fit a maximum number of pages.", () => RB.Menu(
                            RB.MI("Automatic", () => PageChange(p => { p.PagesWide = 0; if (p.PagesTall == 0) p.Scale = 100; })),
                            RB.MI("1 page", () => PageChange(p => p.FitToPages(1, p.PagesTall))), RB.MI("2 pages", () => PageChange(p => p.FitToPages(2, p.PagesTall))))),
                        RB.Small(null, "Height: Automatic / 1 page", null, "Shrink the height of printout to fit a maximum number of pages.", () => RB.Menu(
                            RB.MI("Automatic", () => PageChange(p => { p.PagesTall = 0; if (p.PagesWide == 0) p.Scale = 100; })),
                            RB.MI("1 page", () => PageChange(p => p.FitToPages(p.PagesWide, 1))), RB.MI("2 pages", () => PageChange(p => p.FitToPages(p.PagesWide, 2))))),
                        RB.Small(null, "Scale...", PageSetupDialogShow, "Stretch or shrink the printed output to a percentage of its actual size."))))
                .Add(RB.Group("Sheet Options", PageSetupDialogShow,
                    RB.Stack(Lbl("Gridlines"), CkGridView, CkGridPrint),
                    RB.Stack(Lbl("Headings"), CkHeadView, CkHeadPrint)));

            // ---------------- Formulas ----------------
            Func<ContextMenu> FnMenu(string cat) => () =>
            {
                var items = (cat == "Most Recently Used" ? Functions.Recent.Select(n => Functions.ByName.TryGetValue(n, out var f) ? f : null).Where(f => f != null) : Functions.All.Where(f => f.Category == cat).OrderBy(f => f.Name))
                    .Select(f => (object)RB.MI(f.Name, () => InsertFunctionNamed(f.Name))).ToList();
                items.Add(new Separator());
                items.Add(RB.MI("Insert Function...", () => InsertFunction_Click(null, null), "IconFunction"));
                return RB.Menu(items.ToArray());
            };
            var formulas = new RibbonTab("Formulas")
                .Add(RB.Group("Function Library", null,
                    RB.Large("IconFunction", "Insert\nFunction", () => InsertFunction_Click(null, null), "Edit the formula in the current cell by choosing functions and editing the arguments. (Shift+F3)"),
                    RB.Split("IconAutoSum", "AutoSum", () => AutoSum("SUM"), () => RB.Menu(
                        RB.MI("Sum", () => AutoSum("SUM")), RB.MI("Average", () => AutoSum("AVERAGE")), RB.MI("Count Numbers", () => AutoSum("COUNT")),
                        RB.MI("Max", () => AutoSum("MAX")), RB.MI("Min", () => AutoSum("MIN"))), "AutoSum", true),
                    RB.Large("IconFunction", "Recently\nUsed", null, "Browse and select from a list of recently used functions.", FnMenu("Most Recently Used")),
                    RB.Stack(
                        RB.Small("IconCurrency", "Financial", null, "Financial functions", FnMenu("Financial")),
                        RB.Small("IconQuestion", "Logical", null, "Logical functions", FnMenu("Logical")),
                        RB.Small("IconFont", "Text", null, "Text functions", FnMenu("Text"))),
                    RB.Stack(
                        RB.Small("IconDate", "Date & Time", null, "Date & Time functions", FnMenu("Date & Time")),
                        RB.Small("IconFind", "Lookup & Reference", null, "Lookup & Reference functions", FnMenu("Lookup & Reference")),
                        RB.Small("IconCalcNow", "Math & Trig", null, "Math & Trig functions", FnMenu("Math & Trig"))),
                    RB.Large("IconFunction", "More\nFunctions", null, "Insert statistical, information and database functions.", () => RB.Menu(
                        RB.MI("Statistical", () => RB.OpenMenu(G, FnMenu("Statistical")())), RB.MI("Information", () => RB.OpenMenu(G, FnMenu("Information")())), RB.MI("Database", () => RB.OpenMenu(G, FnMenu("Database")()))))))
                .Add(RB.Group("Defined Names", null,
                    RB.Large("IconNameManager", "Name\nManager", NameManager, "Create, edit, delete, and find all the names used in the workbook."),
                    RB.Stack(
                        RB.Small("IconNameManager", "Define Name", NewNameDialog, "Name cells so that you can refer to them in formulas by that name."),
                        RB.Small("IconFunction", "Use in Formula", null, "Choose a name used in this workbook and insert it into the current formula.", UseNameMenu),
                        RB.Small("IconTable", "Create from Selection", CreateNamesFromSelection, "Automatically generate names from the selected cells."))))
                .Add(RB.Group("Formula Auditing", null,
                    RB.Stack(
                        RB.Toggle("IconFormula", "Show Formulas", b => { G.ShowFormulas = b; G.Redraw(); }, "Display the formula in each cell instead of the resulting value. (Ctrl+`)"),
                        RB.Small("IconWarning", "Error Checking", ErrorChecking, "Check for common errors that occur in formulas."),
                        RB.Small("IconPreviewResults", "Evaluate Formula", EvaluateFormulaDialog, "Evaluate each part of the formula to see its value."))))
                .Add(RB.Group("Calculation", null,
                    RB.Large("IconCalcNow", "Calculate\nNow", CalculateNow, "Calculate the entire workbook now. (F9)")));

            // ---------------- Data ----------------
            var data = new RibbonTab("Data")
                .Add(RB.Group("Get External Data", null,
                    RB.Large("IconOpen", "From\nText", ImportText, "Import data from a text or CSV file into this sheet.")))
                .Add(RB.Group("Connections", null,
                    RB.Large("IconRefresh", "Refresh\nAll", CalculateNow, "Recalculate every formula in the workbook.")))
                .Add(RB.Group("Sort & Filter", null,
                    RB.Stack(RB.Small("IconSortAZ", null, () => SortQuick(false), "Sort A to Z"), RB.Small("IconSortZA", null, () => SortQuick(true), "Sort Z to A")),
                    RB.Large("IconSort", "Sort", SortDialogShow, "Launch the Sort dialog box to sort data based on several criteria at once."),
                    RB.Large("IconFilter", "Filter", ToggleFilter, "Enable filtering of the selected cells. (Ctrl+Shift+L)"),
                    RB.Stack(RB.Small("IconClearFilter", "Clear", ClearFilters, "Clear the filter and sort state for the current range of data."), RB.Small("IconReapply", "Reapply", ReapplyFilter, "Reapply the filter and sort in the current range."))))
                .Add(RB.Group("Data Tools", null,
                    RB.Large("IconTextToColumns", "Text to\nColumns", TextToColumns, "Separate the contents of one cell into separate columns."),
                    RB.Large("IconRemoveDup", "Remove\nDuplicates", RemoveDuplicates, "Delete duplicate rows from a sheet."),
                    RB.Large("IconValidation", "Data\nValidation", DataValidationDialog, "Prevent invalid data from being entered into a cell.")))
                .Add(RB.Group("Outline", null,
                    RB.Large("IconGroup", "Group", GroupRows, "Tie a range of cells together so that they can be collapsed or expanded."),
                    RB.Large("IconGroup", "Ungroup", UngroupRows, "Ungroup a range of cells that were previously grouped."),
                    RB.Large("IconInsertRows", "Subtotal", SubtotalDialog, "Total several rows of related data together by automatically inserting subtotals and totals.")));

            // ---------------- Review ----------------
            var review = new RibbonTab("Review")
                .Add(RB.Group("Proofing", null,
                    RB.Large("IconSpelling", "Spelling", SpellCheckSheet, "Check the spelling of text. (F7)")))
                .Add(RB.Group("Comments", null,
                    RB.Large("IconComment", "New\nComment", EditComment, "Add a comment about the selection."),
                    RB.Large("IconDeleteComment", "Delete", DeleteComment, "Delete the selected comment."),
                    RB.Large("IconPrevComment", "Previous", () => NextComment(-1), "Navigate to the previous comment in the document."),
                    RB.Large("IconNextComment", "Next", () => NextComment(1), "Navigate to the next comment in the document.")))
                .Add(RB.Group("Changes", null,
                    RB.Large("IconProtectSheet", "Protect\nSheet", ProtectSheetToggle, "Prevent unwanted changes to the data in a sheet by specifying what information can be changed."),
                    RB.Large("IconProtect", "Protect\nWorkbook", ProtectWorkbookToggle, "Restrict access to the workbook by preventing new sheets from being created.")));

            // ---------------- View ----------------
            CkGridlines2 = Check("Gridlines", b => { WS.ShowGridLines = b; _book.RaiseChanged(); G.Redraw(); SyncSheetToggles(); });
            CkFormulaBar = Check("Formula Bar", b => { Settings.Current.ShowFormulaBar = b; Settings.Save(); FormulaBar.Visibility = b ? Visibility.Visible : Visibility.Collapsed; });
            CkHeadings2 = Check("Headings", b => { G.ShowHeadings = b; WS.ShowRowColHeaders = b; G.Relayout(); SyncSheetToggles(); });
            var view = new RibbonTab("View")
                .Add(RB.Group("Workbook Views", null,
                    RB.Large("IconNormalView", "Normal", () => { }, "View the document in Normal view."),
                    RB.Large("IconPreview", "Print\nPreview", PrintPreview, "See how the sheet will look when printed."),
                    RB.Large("IconFullScreen", "Full\nScreen", ToggleFullScreen, "View the document in Full Screen Mode.")))
                .Add(RB.Group("Show/Hide", null, RB.Stack(CkGridlines2, CkFormulaBar, CkHeadings2)))
                .Add(RB.Group("Zoom", null,
                    RB.Large("IconZoom", "Zoom", ZoomDialog, "Open the Zoom dialog box to specify the zoom level of the document."),
                    RB.Large("IconOnePage", "100%", () => SetZoom(1), "Zoom the document to 100% of the normal size."),
                    RB.Large("IconPageWidth", "Zoom to\nSelection", ZoomToSelection, "Zoom the worksheet so that the currently selected range of cells fills the entire window.")))
                .Add(RB.Group("Window", null,
                    RB.Large("IconNewWindow", "New\nWindow", NewWorkbook, "Open a new window with a new workbook."),
                    RB.Large("IconFreeze", "Freeze\nPanes", null, "Keep a portion of the sheet visible while the rest of the sheet scrolls.", () => RB.Menu(
                        RB.MI(G.FrozenRows > 0 || G.FrozenCols > 0 ? "Unfreeze Panes" : "Freeze Panes", FreezePanesAtSelection),
                        RB.MI("Freeze Top Row", () => Freeze(1, 0)),
                        RB.MI("Freeze First Column", () => Freeze(0, 1)))),
                    RB.Large("IconSwitchWindows", "Switch\nWindows", null, "Switch to a different currently open window.", () => RB.Menu(
                        Application.Current.Windows.OfType<MainWindow>().Select(w => (object)RB.MI(w._book.DisplayName, () => w.Activate(), null, null, w == this)).ToArray()))))
                .Add(RB.Group("Macros", null,
                    RB.Large("IconMacro", "Macros", ShowMacros, "View the macros in this workbook.")));

            foreach (var t in new[] { home, insert, layout, formulas, data, review, view }) Ribbon.AddTab(t);
            if (Settings.Current.RibbonMinimized) Ribbon.ToggleMinimize();

            _title = new ValcoTitleBar(this, ShowValcoMenu, "Click here to open, save or print, and to see everything else you can do with your workbook.")
                .QatOption("New", "IconNew", "New", NewWorkbook)
                .QatOption("Open", "IconOpen", "Open", OpenFile)
                .QatOption("Save", "IconSave", "Save", () => Cmd(() => Save()))
                .QatOption("QuickPrint", "IconPrint", "Quick Print", () => Cmd(QuickPrint))
                .QatOption("PrintPreview", "IconPreview", "Print Preview", () => Cmd(PrintPreview))
                .QatOption("Spelling", "IconSpelling", "Spelling", () => Cmd(SpellCheckSheet))
                .QatOption("Undo", "IconUndo", "Undo", UndoCmd)
                .QatOption("Redo", "IconRedo", "Redo", RedoCmd)
                .QatOption("SortAZ", "IconSortAZ", "Sort Ascending", () => Cmd(() => SortQuick(false)))
                .QatOption("SortZA", "IconSortZA", "Sort Descending", () => Cmd(() => SortQuick(true)));
            _title.InitQat(() => Settings.Current.QuickAccess.ToList(), l => { Settings.Current.QuickAccess = l; Settings.Save(); UpdateUndoButtons(); }, Ribbon,
                m => { Settings.Current.RibbonMinimized = m; Settings.Save(); });
            UpdateUndoButtons();

            BuildViewButtons();
        }

        bool G_FilterOn() { try { return G.FilterRange.HasValue; } catch { return false; } }

        void BuildViewButtons()
        {
            ToggleButton V(string icon, string tip, bool on, Action a)
            {
                var b = new ToggleButton { Style = (Style)FindResource("StatusButton"), IsChecked = on, ToolTip = tip, Content = new Image { Source = UI.Img(icon), Width = 16, Height = 16 } };
                b.Click += (s, e) => a();
                return b;
            }
            ViewButtons.Children.Add(V("IconNormalView", "Normal", true, () => { }));
            ViewButtons.Children.Add(V("IconPageLayoutView", "Print Preview", false, PrintPreview));
            ViewButtons.Children.Add(V("IconPageBreakPreview", "Page Setup", false, PageSetupDialogShow));
            foreach (ToggleButton b in ViewButtons.Children) b.Click += (s, e) => { foreach (ToggleButton x in ViewButtons.Children) x.IsChecked = x == ViewButtons.Children[0]; };
        }

        static DataTemplate FontItemTemplate()
        {
            var t = new DataTemplate();
            var f = new FrameworkElementFactory(typeof(TextBlock));
            f.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            f.SetBinding(TextBlock.FontFamilyProperty, new System.Windows.Data.Binding());
            f.SetValue(TextBlock.FontSizeProperty, 13.0);
            t.VisualTree = f;
            return t;
        }

        public static readonly (string Name, string Format)[] NumberFormats =
        {
            ("General", ""), ("Number", "0.00"), ("Currency", "\"$\"#,##0.00"), ("Accounting", "_(\"$\"* #,##0.00_);_(\"$\"* \\(#,##0.00\\);_(\"$\"* \"-\"??_);_(@_)"),
            ("Short Date", "m/d/yyyy"), ("Long Date", "[$-F800]dddd, mmmm dd, yyyy"), ("Time", "[$-F400]h:mm:ss AM/PM"), ("Percentage", "0.00%"),
            ("Fraction", "# ?/?"), ("Scientific", "0.00E+00"), ("Text", "@"), ("More Number Formats...", null)
        };

        void SyncRibbon()
        {
            if (Ribbon == null) return;
            _ribbonSync = true;
            try
            {
                var st = G.ActiveCell.Style;
                var f = st.Font;
                if (!CbFont.IsKeyboardFocusWithin) CbFont.Text = f.FontName;
                if (!CbSize.IsKeyboardFocusWithin) CbSize.Text = f.FontSize.ToString(CultureInfo.CurrentCulture);
                TbBold.IsChecked = f.Bold; TbItalic.IsChecked = f.Italic; TbUnderline.IsChecked = f.Underline != XLFontUnderlineValues.None;
                var h = st.Alignment.Horizontal; var v = st.Alignment.Vertical;
                TbLeft.IsChecked = h == XLAlignmentHorizontalValues.Left; TbCenter.IsChecked = h == XLAlignmentHorizontalValues.Center; TbRight.IsChecked = h == XLAlignmentHorizontalValues.Right;
                TbTop.IsChecked = v == XLAlignmentVerticalValues.Top; TbMiddle.IsChecked = v == XLAlignmentVerticalValues.Center; TbBottom.IsChecked = v == XLAlignmentVerticalValues.Bottom;
                TbWrap.IsChecked = st.Alignment.WrapText;
                var nf = st.NumberFormat.Format ?? "";
                int id = st.NumberFormat.NumberFormatId;
                int idx = Array.FindIndex(NumberFormats, n => n.Format == nf && n.Format != null);
                if (idx < 0)
                {
                    idx = id switch { 0 => 0, 1 or 2 or 3 or 4 => 1, 14 or 15 or 16 or 17 or 22 => 4, 18 or 19 or 20 or 21 or 45 or 46 or 47 => 6, 9 or 10 => 7, 12 or 13 => 8, 11 or 48 => 9, 49 => 10, 44 or 42 or 41 or 43 => 3, 5 or 6 or 7 or 8 => 2, _ => -1 };
                    if (idx < 0 && nf.Length > 0) idx = nf.Contains("%") ? 7 : nf.Contains("$") ? 2 : nf.Contains("y") || nf.Contains("d") && nf.Contains("m") ? 4 : nf.Contains("h") ? 6 : nf.Contains("E+") ? 9 : 1;
                }
                CbNumFmt.SelectedIndex = idx < 0 ? 0 : idx;
            }
            catch { }
            finally { _ribbonSync = false; }
            UpdateUndoButtons();
        }

        void SyncSheetToggles()
        {
            if (CkGridView == null) return;
            bool grid = WS.ShowGridLines;
            CkGridView.IsChecked = CkGridlines2.IsChecked = grid;
            CkGridPrint.IsChecked = WS.PageSetup.ShowGridlines;
            bool heads = WS.ShowRowColHeaders;
            G.ShowHeadings = heads;
            CkHeadView.IsChecked = CkHeadings2.IsChecked = heads;
            CkHeadPrint.IsChecked = WS.PageSetup.ShowRowAndColumnHeadings;
            CkFormulaBar.IsChecked = Settings.Current.ShowFormulaBar;
        }

        // ===================================================================== Valco menu
        void ShowValcoMenu()
        {
            if (G.IsEditing) CommitEdit(0, 0);
            var menu = new AppMenu()
                .Item("IconNew", "New", NewWorkbook, "New",
                    new AppMenuEntry("IconSheet", "Blank Workbook", "Create a new, empty workbook.", NewWorkbook),
                    new AppMenuEntry("IconOpen", "New from Existing...", "Create a new workbook from a template or an existing workbook.", NewFromTemplate))
                .Item("IconOpen", "Open", OpenFile)
                .Item("IconSave", "Save", () => Save())
                .Item("IconSaveAs", "Save As", () => SaveAs(), "Save a copy of the document",
                    new AppMenuEntry("IconSheet", "Excel Workbook", "Save the workbook in the default file format (.xlsx).", () => SaveAsFormat(BookFormat.Xlsx)),
                    new AppMenuEntry("IconMacro", "Excel Macro-Enabled Workbook", "Save the workbook in the XML-based, macro-enabled file format (.xlsm).", () => SaveAsFormat(BookFormat.Xlsm)),
                    new AppMenuEntry("IconDoc", "Excel 97-2003 Workbook", "Save a copy of the workbook that is fully compatible with Excel 97-2003.", () => SaveAsFormat(BookFormat.Xls)),
                    new AppMenuEntry("IconTextToColumns", "CSV (Comma delimited)", "Save the active sheet as comma-separated values (.csv).", () => SaveAsFormat(BookFormat.Csv)),
                    new AppMenuEntry("IconPdf", "PDF or XPS", "Publish a copy of the workbook as a PDF or XPS file.", SaveAsPdf),
                    new AppMenuEntry("IconSaveAs", "Other Formats", "Open the Save As dialog box to select from all possible file types.", () => SaveAs()))
                .Sep()
                .Item("IconPrint", "Print", PrintDialogShow, "Preview and print the document",
                    new AppMenuEntry("IconPrint", "Print", "Select a printer, number of copies, and other printing options before printing.", PrintDialogShow),
                    new AppMenuEntry("IconPrint", "Quick Print", "Send the workbook directly to the default printer without making changes.", QuickPrint),
                    new AppMenuEntry("IconPreview", "Print Preview", "Preview and make changes to pages before printing.", PrintPreview))
                .Item("IconProperties", "Prepare", null, "Prepare the document for distribution",
                    new AppMenuEntry("IconProperties", "Properties", "View and edit workbook properties, such as Title, Author, and Keywords.", PropertiesDialog),
                    new AppMenuEntry("IconProtect", "Encrypt Document", "Protect the structure of the workbook from changes (Review > Protect Workbook).", ProtectWorkbookToggle),
                    new AppMenuEntry("IconInspect", "Run Compatibility Checker", "Check for features not supported by earlier versions of Excel.", RunCompatibilityChecker));
            if (_book.Format == BookFormat.Xls) menu.Item("IconConvert", "Convert", ConvertWorkbook, "Convert the workbook to the newest file format",
                    new AppMenuEntry("IconConvert", "Convert", "Convert this Excel 97-2003 workbook to the Excel Workbook format and use all the new features.", ConvertWorkbook));
            menu.Sep()
                .Item("IconExit", "Close", CloseWorkbook)
                .Recent("Recent Documents", () => Settings.Current.Recent.Where(File.Exists).Take(17).Select(p => (System.IO.Path.GetFileName(p), p, (Action)(() => OpenPath(p)))))
                .Footer("Valco Spreadsheets Options", "IconOptions", ShowOptions)
                .Footer("Exit Valco Spreadsheets", "IconExit", ExitApp)
                .Show(_title.Orb);
        }

        void SaveAsFormat(BookFormat f)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = SaveTypes.First(t => t.Format == f).Filter,
                FileName = System.IO.Path.GetFileNameWithoutExtension(_book.DisplayName),
                AddExtension = true,
            };
            if (_book.FilePath != null) dlg.InitialDirectory = System.IO.Path.GetDirectoryName(_book.FilePath);
            if (dlg.ShowDialog(this) != true) return;
            SaveTo(System.IO.Path.ChangeExtension(dlg.FileName, SaveTypes.First(t => t.Format == f).Ext), f);
        }

        // ===================================================================== menus
        ContextMenu BordersMenu()
        {
            MenuItem B(string text, BorderKind k, string icon = null) => RB.MI(text, () => { _lastBorder = k; ApplyBorder(k, _borderColor, _borderStyle); }, icon);
            var color = new MenuItem { Header = "Line Color" };
            color.Click += (s, e) => ColorPicker.Show(_bordersBtn, "Automatic", c => _borderColor = c);
            var style = new MenuItem { Header = "Line Style" };
            foreach (var (n, v) in new[] { ("Thin", XLBorderStyleValues.Thin), ("Medium", XLBorderStyleValues.Medium), ("Thick", XLBorderStyleValues.Thick), ("Dashed", XLBorderStyleValues.Dashed), ("Dotted", XLBorderStyleValues.Dotted), ("Double", XLBorderStyleValues.Double) })
                style.Items.Add(RB.MI(n, () => _borderStyle = v, null, null, _borderStyle == v));
            return RB.Menu(
                new MenuItem { Header = "Borders", IsEnabled = false, FontWeight = FontWeights.Bold },
                B("Bottom Border", BorderKind.Bottom), B("Top Border", BorderKind.Top), B("Left Border", BorderKind.Left), B("Right Border", BorderKind.Right),
                new Separator(),
                B("No Border", BorderKind.None), B("All Borders", BorderKind.All, "IconBorders"), B("Outside Borders", BorderKind.Outside), B("Thick Box Border", BorderKind.ThickBox),
                new Separator(),
                B("Bottom Double Border", BorderKind.BottomDouble), B("Thick Bottom Border", BorderKind.ThickBottom), B("Top and Bottom Border", BorderKind.TopBottom),
                B("Top and Thick Bottom Border", BorderKind.TopThickBottom), B("Top and Double Bottom Border", BorderKind.TopDoubleBottom),
                new Separator(),
                color, style,
                RB.MI("More Borders...", () => FormatCells("Border")));
        }

        Color? _borderColor;
        XLBorderStyleValues _borderStyle = XLBorderStyleValues.Thin;

        ContextMenu FormatMenu()
        {
            var hide = new MenuItem { Header = "Hide & Unhide" };
            hide.Items.Add(RB.MI("Hide Rows", () => HideAndRefresh(HideRows), null, "Ctrl+9"));
            hide.Items.Add(RB.MI("Hide Columns", () => HideAndRefresh(HideCols), null, "Ctrl+0"));
            hide.Items.Add(RB.MI("Hide Sheet", () => HideSheet(SheetIdx)));
            hide.Items.Add(new Separator());
            hide.Items.Add(RB.MI("Unhide Rows", UnhideRows, null, "Ctrl+Shift+("));
            hide.Items.Add(RB.MI("Unhide Columns", UnhideCols, null, "Ctrl+Shift+)"));
            hide.Items.Add(RB.MI("Unhide Sheet...", UnhideSheetDialog));
            var tab = new MenuItem { Header = "Tab Color" };
            tab.Click += (s, e) => { if (e.OriginalSource == tab) ColorPicker.Show(G, "No Color", SetTabColor); };
            bool locked = true; try { locked = G.ActiveCell.Style.Protection.Locked; } catch { }
            return RB.Menu(
                new MenuItem { Header = "Cell Size", IsEnabled = false, FontWeight = FontWeights.Bold },
                RB.MI("Row Height...", SetRowHeightDialog, "IconRowHeight"),
                RB.MI("AutoFit Row Height", () => AutoFitRows(SelRows(), true)),
                RB.MI("Column Width...", SetColumnWidthDialog, "IconColWidth"),
                RB.MI("AutoFit Column Width", () => AutoFitColumns(SelCols(), G.ActiveRect.IsWholeColumns ? (CellRect?)null : G.ActiveRect)),
                RB.MI("Default Width...", SetDefaultWidthDialog),
                new Separator(),
                new MenuItem { Header = "Visibility", IsEnabled = false, FontWeight = FontWeights.Bold },
                hide,
                new Separator(),
                new MenuItem { Header = "Organize Sheets", IsEnabled = false, FontWeight = FontWeights.Bold },
                RB.MI("Rename Sheet", () => RenameSheet(SheetIdx)),
                RB.MI("Move or Copy Sheet...", MoveCopyDialog),
                tab,
                new Separator(),
                new MenuItem { Header = "Protection", IsEnabled = false, FontWeight = FontWeights.Bold },
                RB.MI(WS.IsProtected ? "Unprotect Sheet..." : "Protect Sheet...", ProtectSheetToggle, "IconProtectSheet"),
                RB.MI("Lock Cell", () => StyleSelection("Lock Cell", s => s.Protection.Locked = !locked), null, null, locked),
                RB.MI("Format Cells...", () => FormatCells(null), "IconFormatCells", "Ctrl+1"));
        }

        ContextMenu UseNameMenu()
        {
            var names = _book.Wb.DefinedNames.Select(n => n.Name).OrderBy(n => n).ToList();
            if (names.Count == 0) return RB.Menu(new MenuItem { Header = "(No names defined)", IsEnabled = false });
            return RB.Menu(names.Select(n => (object)RB.MI(n, () =>
            {
                if (!G.IsEditing) { G.BeginEdit("=" + n, EditMode.Enter); _editSheet = SheetIdx; }
                else InsertAtCaret(G.EditorBox, n);
            })).ToArray());
        }

        // ===================================================================== page layout helpers
        void PageChange(Action<IXLPageSetup> a)
        {
            var ws = WS;
            Change("Page Setup", () => a(ws.PageSetup), structure: true);
        }

        void SetMargins(double top, double left, double bottom, double right) => PageChange(p => { p.Margins.Top = top; p.Margins.Left = left; p.Margins.Bottom = bottom; p.Margins.Right = right; });

        void SetPrintArea()
        {
            var r = G.ActiveRect;
            PageChange(p => { p.PrintAreas.Clear(); p.PrintAreas.Add(r.R1, r.C1, r.R2, r.C2); });
        }

        void InsertPageBreak()
        {
            int r = G.View.ActiveRow, c = G.View.ActiveCol;
            PageChange(p => { if (r > 1) p.AddHorizontalPageBreak(r - 1); if (c > 1 && G.ActiveRect.IsWholeColumns) p.AddVerticalPageBreak(c - 1); });
        }

        void RemovePageBreak()
        {
            int r = G.View.ActiveRow;
            PageChange(p => p.RowBreaks.Remove(r - 1));
        }

        // ===================================================================== view helpers
        void Freeze(int rows, int cols)
        {
            var ws = WS;
            if (G.FrozenRows == rows && G.FrozenCols == cols) { rows = 0; cols = 0; }
            Change("Freeze Panes", () => ws.SheetView.Freeze(rows, cols), structure: true);
            G.ScrollTo(rows + 1, cols + 1);
        }

        void FreezePanesAtSelection()
        {
            var ws = WS;
            if (G.FrozenRows > 0 || G.FrozenCols > 0) { Change("Unfreeze Panes", () => { ws.SheetView.SplitRow = 0; ws.SheetView.SplitColumn = 0; }, structure: true); return; }
            // rows above and columns left of the active cell stay put
            int r = G.View.ActiveRow - 1, c = G.View.ActiveCol - 1;
            if (r == 0 && c == 0) { r = G.VisibleRowCount / 2; }
            Change("Freeze Panes", () => ws.SheetView.Freeze(r, c), structure: true);
        }

        void ZoomToSelection()
        {
            var s = G.ActiveRect;
            if (s.IsWholeColumns) s = new CellRect(G.View.TopRow, s.C1, G.View.TopRow + 30, s.C2);
            if (s.IsWholeRows) s = new CellRect(s.R1, G.View.LeftCol, s.R2, G.View.LeftCol + 10);
            double w = G.Layout.Cols.Pos(s.C2 + 1) - G.Layout.Cols.Pos(s.C1), h = G.Layout.Rows.Pos(s.R2 + 1) - G.Layout.Rows.Pos(s.R1);
            double availW = G.ActualWidth - G.HeaderW - 20, availH = G.ActualHeight - G.HeaderH - 20;
            if (w <= 0 || h <= 0) return;
            SetZoom(Math.Clamp(Math.Min(availW / w, availH / h), 0.1, 4));
            G.ScrollTo(s.R1, s.C1);
        }

        WindowState _preFullState;
        bool _fullScreen;
        void ToggleFullScreen()
        {
            _fullScreen = !_fullScreen;
            RibbonHost.Visibility = FormulaBar.Visibility = StatusBar.Visibility = _fullScreen ? Visibility.Collapsed : Visibility.Visible;
            if (_fullScreen) { _preFullState = WindowState; WindowState = WindowState.Maximized; }
            else
            {
                WindowState = _preFullState;
                FormulaBar.Visibility = Settings.Current.ShowFormulaBar ? Visibility.Visible : Visibility.Collapsed;
                StatusBar.Visibility = Settings.Current.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        void ZoomDialog()
        {
            var opts = new[] { "200%", "100%", "75%", "50%", "25%", "Fit selection" };
            int cur = Array.IndexOf(opts, Math.Round(G.Zoom * 100) + "%");
            var pick = ChoiceDialog.Ask(this, "Zoom", opts, cur < 0 ? 1 : cur, "Custom:", Math.Round(G.Zoom * 100).ToString(CultureInfo.InvariantCulture));
            if (pick < 0) return;
            if (pick == opts.Length - 1) { ZoomToSelection(); return; }
            if (pick < opts.Length) { SetZoom(double.Parse(opts[pick].TrimEnd('%'), CultureInfo.InvariantCulture) / 100); return; }
            if (double.TryParse(ChoiceDialog.LastCustom, NumberStyles.Float, CultureInfo.CurrentCulture, out var z) && z >= 10 && z <= 400) SetZoom(z / 100);
        }

        void ShowMacros()
        {
            if (!_book.HasMacros) { VistaDialog.Show(this, "This workbook doesn't contain any macros.", "Macros"); return; }
            var names = VbaModules.List(_book.VbaProject);
            VistaDialog.Show(this, "This workbook contains a VBA project" + (names.Count > 0 ? " with these modules:\n\n" + string.Join("\n", names.Select(n => "   •  " + n)) : ".") +
                "\n\nValco Spreadsheets keeps the macros when you save the workbook as an Excel Macro-Enabled Workbook (.xlsm), but doesn't run them, so they can't change your data or your computer.", "Macros");
        }

        // ===================================================================== format painter
        IXLCell _painterHolder;

        /// <summary>Format Painter: the next selection gets the active cell's formatting.</summary>
        void FormatPainter()
        {
            var holder = new XLWorkbook().AddWorksheet("p").Cell(1, 1);
            holder.Style = G.ActiveCell.Style;
            _painterHolder = holder;
            StatusMsg.Text = "Select the cells to copy the formatting to.";
            G.Cursor = Cursors.Pen;
            void Once(object s, MouseButtonEventArgs e)
            {
                G.PreviewMouseLeftButtonUp -= Once;
                G.Cursor = null;
                var h = _painterHolder;
                _painterHolder = null;
                StatusMsg.Text = "";
                if (h == null) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var sel = G.View.Selection.ToList();
                    Change("Format Painter", () =>
                    {
                        foreach (var r in sel)
                            foreach (var cell in WS.Range(r.R1, r.C1, Math.Min(r.R2, r.R1 + 5000), Math.Min(r.C2, r.C1 + 500)).Cells()) cell.Style = h.Style;
                    });
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            G.PreviewMouseLeftButtonUp += Once;
        }

        // ===================================================================== workbook protection / properties
        void ProtectWorkbookToggle()
        {
            if (_book.Wb.IsProtected)
            {
                string pw = null;
                if (_book.Wb.IsPasswordProtected) { pw = PasswordDialog.Ask(this, "Unprotect Workbook", "Password:"); if (pw == null) return; }
                try { var wb = _book.Wb; if (pw == null) wb.Unprotect(); else wb.Unprotect(pw); _book.RaiseChanged(); }
                catch { VistaDialog.Show(this, "The password you supplied is not correct.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning); }
                return;
            }
            var p = PasswordDialog.Ask(this, "Protect Structure and Windows", "Password (optional):", allowEmpty: true);
            if (p == null) return;
            if (p.Length == 0) _book.Wb.Protect(); else _book.Wb.Protect(p);
            _book.RaiseChanged();
        }

        void PropertiesDialog()
        {
            var d = new PropertiesDialog(this, _book.Wb.Properties);
            if (d.ShowDialog() == true) _book.RaiseChanged();
        }
    }
}
