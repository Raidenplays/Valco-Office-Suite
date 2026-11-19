# Valco Spreadsheets

A spreadsheet program for Windows in the Excel 2007 style, from the **Valco Office** suite. C#/WPF on .NET 8.

- Opens and saves Excel workbooks: **.xlsx**, **.xlsm** (the VBA project is kept when you save as .xlsm; macros don't run), **.xltx/.xltm** templates (open as a new workbook), **.csv** and tab-delimited text. Opens and saves Excel 97-2003 **.xls** (values, formulas, formatting, widths and heights, merges, frozen panes, comments, links, pictures, names, print setup, conditional formatting, data validation; the VBA project of an .xls is kept when you save it as .xlsm). Saving as .xls runs a Compatibility Checker first, and Convert on the Valco menu switches a Compatibility Mode workbook to .xlsx. Functions newer than Excel 2003 (IFERROR, SUMIFS...) are stored the way Excel stores them (`_xlfn.`).
- Formulas through ClosedXML's calculation engine (hundreds of Excel functions), Formula AutoComplete and argument ScreenTips, coloured reference outlines, click-to-reference while typing a formula, F4 to cycle `$` references, Insert Function and Function Arguments dialogs.
- The Excel 2007 grid: blue headers that turn gold for the selection, fill handle (number/date/month/"Item 1" series, formulas copied with adjusted references), drag a selection's border to move it (Ctrl to copy), freeze panes, zoom 10–400%, hide/unhide rows and columns, AutoFit, merged cells, text overflow into empty neighbours, `####` for numbers that don't fit, comments, hyperlinks, pictures.
- Ribbon: **Home** (clipboard with Paste Special, font, borders, fill, alignment, wrap, merge, number formats, Conditional Formatting, Format as Table, Cell Styles, insert/delete, Format menu, AutoSum, Fill, Clear, Sort & Filter, Find & Select), **Insert** (table, picture, column/bar/line/pie/area/scatter/doughnut charts, hyperlink, header & footer, symbol), **Page Layout**, **Formulas** (function library, names, Show Formulas, error checking, Calculate Now), **Data** (import text, sort, AutoFilter with value checklists and custom filters, Text to Columns, Remove Duplicates, Data Validation, Group, Subtotal), **Review** (spelling with the Windows dictionaries, comments, Protect Sheet/Workbook), **View** (gridlines, headings, formula bar, zoom, freeze panes, full screen).
- Charts are read from and written to the workbook as real Excel charts (DrawingML), so Excel opens them. Move/resize them on the sheet; right-click for chart type, data, title, legend, data labels and Switch Row/Column.
- Print, Quick Print, Print Preview, Page Setup (orientation, paper, margins, fit to pages, print area, repeated titles, gridlines/headings, header/footer codes), PDF/XPS export.
- Undo/Redo for everything (cell edits store just the changed cells; structural changes store the workbook), AutoRecover, Valco update checks.

## Build

`build.cmd` (or `build.ps1`) publishes a self-contained single-file `publish\ValcoSpreadsheets.exe`; Valco Office Setup packages that folder. Icons: `tools\gen_assets.py` (app icon), `tools\gen_icons.py` (ribbon icons → `Themes\SheetIcons.xaml`).

Workbook engine: [ClosedXML](https://github.com/ClosedXML/ClosedXML) (MIT), .xls files: [NPOI](https://github.com/nissl-lab/npoi) 2.7.6 (Apache 2.0, the last release without the binary maintenance-fee EULA); Excel 5.0/95 files: ExcelDataReader (MIT).

© 2026 ItsRaiden
