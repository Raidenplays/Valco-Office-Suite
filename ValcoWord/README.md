# Valco Word

A native Windows word processor styled after Windows Vista-era apps — part of the Valco suite.
C# / WPF on .NET 8. No third-party libraries.

## Features
- Opens and saves Word documents (.docx), Rich Text (.rtf) and plain text; reads the text of Word 97-2003 (.doc) files
- Exports PDF (via Microsoft Print to PDF), XPS, HTML and plain text
- Page view with ruler (drag margins and indents), page boundaries, page breaks, zoom
- Fonts, sizes, colours, highlight, bold/italic/underline/strikethrough, super/subscript
- Quick Styles (Normal, Title, Headings, Quote, Code...), bullets and numbering, line spacing, Paragraph dialog
- Tables (grid picker, insert/delete rows & columns, shading, borders, column width)
- Pictures (insert, paste, drag in, resize with the handle), hyperlinks, symbols, date & time
- Find & Replace, spell check with suggestions and a custom dictionary, word count
- Page Setup (paper, orientation, margins, header text, page numbers), Print Preview, printing
- AutoRecover, recent documents, Full Screen Reading

## Build
Double-click `build.cmd` (or run `.\build.ps1`).
Produces `publish\ValcoWord.exe` (self-contained) and `dist\ValcoWord-Setup.msi`.

## Layout
- `src/ValcoWord` – the app
- `installer` – WiX v5 MSI project
- `assets` – icon and installer art (`tools/gen_assets.py`)
- `tools/gen_icons.py` – regenerates the vector icon set (`Themes/Icons.xaml`)

Settings live in `%APPDATA%\Valco Word`; AutoRecover copies in `%LOCALAPPDATA%\Valco Word\AutoRecover`.
