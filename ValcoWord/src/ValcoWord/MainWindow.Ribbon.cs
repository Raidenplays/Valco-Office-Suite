using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Shell;
using Path = System.Windows.Shapes.Path;

namespace ValcoWord
{
    public partial class MainWindow
    {
        RibbonBar Ribbon;
        RibbonTab TabHome, TabInsert, TabLayout, TabRefs, TabMail, TabReview, TabView, TabTableDesign, TabTableLayout, TabPicture, TabOutline;
        ComboBox CbFont, CbSize, CbBibStyle, CbMarkup;
        ToggleButton TbBold, TbItalic, TbUnderline, TbStrike, TbSub, TbSup, TbLeft, TbCenter, TbRight, TbJustify, TbBullets, TbNumbering, TbSpell, TbMarks, TbTrack, TbPreviewMerge, TbHighlightFields;
        ToggleButton TbPrintLayout, TbFullScreen, TbWebLayout, TbOutline, TbDraft;
        CheckBox CkRuler, CkGridlines, CkDocMap, CkThumbs, CkHeaderRow, CkTotalRow, CkBandedRows, CkFirstCol, CkLastCol, CkBandedCols;
        Button BtnUndo, BtnRedo, BtnFontColor, BtnHighlight;
        FrameworkElement BtnLineSpacing, BtnTable;
        Rectangle FontColorSwatch, HighlightSwatch, ShadingSwatch;
        WrapPanel StyleGallery;
        TextBox TxtIndentL, TxtIndentR, TxtSpaceBefore, TxtSpaceAfter, TxtRecord, TxtPicW, TxtPicH, TxtCellW;
        StackPanel QatPanel;
        Color _shading = Color.FromRgb(0xDB, 0xE5, 0xF1);
        readonly Dictionary<string, Action> _commands = new Dictionary<string, Action>();

        /// <summary>Wraps a ribbon command so macros can record it.</summary>
        Action C(string name, Action a)
        {
            _commands[name] = a;
            return () => { RecordCommand(name); a(); };
        }

        static UIElement G(string text, FontWeight? w = null, FontStyle? s = null, TextDecorationCollection d = null, double size = 15, string family = "Times New Roman")
        {
            var tb = new TextBlock { Text = text, FontFamily = new FontFamily(family), FontSize = size, Margin = new Thickness(0, -2, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Rb.Text");
            if (w.HasValue) tb.FontWeight = w.Value;
            if (s.HasValue) tb.FontStyle = s.Value;
            if (d != null) tb.TextDecorations = d;
            return tb;
        }

        static UIElement Script(bool sup)
        {
            var tb = new TextBlock { FontFamily = new FontFamily("Times New Roman"), FontSize = 13, Margin = new Thickness(0, -2, 0, 0) };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Rb.Text");
            tb.Inlines.Add(new Run("x"));
            tb.Inlines.Add(new Run("2") { FontSize = 8, BaselineAlignment = sup ? BaselineAlignment.Superscript : BaselineAlignment.Subscript });
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

        static TextBox Spin(double width, Action<double> commit, string tip)
        {
            var tb = new TextBox { Width = width, Height = 20, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 11, ToolTip = tip, Margin = new Thickness(2, 0, 0, 0) };
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) { var v = D.ParseNum(tb.Text); if (v.HasValue) commit(v.Value); e.Handled = true; } };
            tb.LostKeyboardFocus += (s, e) => { var v = D.ParseNum(tb.Text); if (v.HasValue && tb.Tag as string != "sync") commit(v.Value); };
            return tb;
        }

        static StackPanel Labeled(string icon, string label, UIElement control)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 4, 2) };
            if (icon != null) sp.Children.Add(new Image { Source = UI.Img(icon), Width = 16, Height = 16, Margin = new Thickness(0, 0, 3, 0) });
            var tb = new TextBlock { Text = label, Width = 50, VerticalAlignment = VerticalAlignment.Center };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Rb.Text");
            sp.Children.Add(tb);
            sp.Children.Add(control);
            return sp;
        }

        CheckBox Check(string label, Action<bool> changed)
        {
            var c = new CheckBox { Content = label, Margin = new Thickness(2, 2, 6, 3), VerticalAlignment = VerticalAlignment.Center };
            c.SetResourceReference(Control.ForegroundProperty, "Rb.Text");
            c.Click += (s, e) => changed(c.IsChecked == true);
            return c;
        }

        // =====================================================================================
        void BuildRibbon()
        {
            OfficeTheme.Apply(Settings.Current.ColorScheme);
            Ribbon = new RibbonBar();
            RibbonHost.Children.Add(Ribbon);
            BuildTitleBar();

            // ---------------- Home ----------------
            CbFont = RB.Combo(150, true);
            CbFont.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(f => f).ToList();
            CbFont.ItemTemplate = FontDialog.FontItemTemplate();
            CbFont.IsTextSearchEnabled = true;
            CbFont.SelectionChanged += CbFont_SelectionChanged;
            CbFont.KeyDown += CbFont_KeyDown;
            CbFont.DropDownClosed += Combo_DropDownClosed;
            CbFont.ToolTip = RB.Tip("Font", "Change the font face.");
            CbSize = RB.Combo(44, true);
            CbSize.ItemsSource = Sizes.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList();
            CbSize.SelectionChanged += CbSize_SelectionChanged;
            CbSize.KeyDown += CbSize_KeyDown;
            CbSize.DropDownClosed += Combo_DropDownClosed;
            CbSize.ToolTip = RB.Tip("Font Size", "Change the font size.");

            TbBold = RB.Glyph(G("B", FontWeights.Bold), C("Bold", () => Bold_Click(null, null)), "Bold (Ctrl+B)", "Make the selected text bold.");
            TbItalic = RB.Glyph(G("I", null, FontStyles.Italic), C("Italic", () => Italic_Click(null, null)), "Italic (Ctrl+I)", "Italicize the selected text.");
            TbUnderline = RB.Glyph(G("U", null, null, TextDecorations.Underline), C("Underline", () => Underline_Click(null, null)), "Underline (Ctrl+U)", "Underline the selected text.");
            TbStrike = RB.Glyph(G("abc", null, null, TextDecorations.Strikethrough, 12), C("Strikethrough", () => Strike_Click(null, null)), "Strikethrough", "Draw a line through the middle of the selected text.");
            TbSub = RB.Glyph(Script(false), C("Subscript", () => Sub_Click(null, null)), "Subscript (Ctrl+=)", "Create small letters below the text baseline.");
            TbSup = RB.Glyph(Script(true), C("Superscript", () => Sup_Click(null, null)), "Superscript (Ctrl+Shift++)", "Create small letters above the line of text.");
            BtnHighlight = new Button();
            var hl = RB.Split(null, null, C("Highlight", () => Highlight_Click(null, null)), () => { HighlightDrop_Click(null, null); return null; }, "Text Highlight Color", false, SwatchIcon("IconHighlight", out HighlightSwatch, _highlight));
            BtnFontColor = new Button();
            var fc = RB.Split(null, null, C("FontColor", () => FontColor_Click(null, null)), () => { FontColorDrop_Click(null, null); return null; }, "Font Color", false, SwatchIcon("IconFontColor", out FontColorSwatch, _fontColor));
            _fontColorTarget = (FrameworkElement)fc; _highlightTarget = (FrameworkElement)hl;

            var fontGroup = RB.Group("Font", () => FontDialog_Click(null, null),
                RB.Stack(
                    RB.Row(CbFont, CbSize,
                        RB.Small("IconGrow", null, C("GrowFont", () => Grow_Click(null, null)), "Grow Font (Ctrl+])"),
                        RB.Small("IconShrink", null, C("ShrinkFont", () => Shrink_Click(null, null)), "Shrink Font (Ctrl+[)"),
                        RB.Small("IconClearFormat", null, C("ClearFormatting", () => ClearFormat_Click(null, null)), "Clear Formatting")),
                    RB.Row(TbBold, TbItalic, TbUnderline, TbStrike, TbSub, TbSup,
                        RB.Small("IconChangeCase", null, null, "Change Case", () => RB.Menu(
                            RB.MI("Sentence case.", () => ChangeCaseTo("sentence")), RB.MI("lowercase", () => ChangeCaseTo("lower")),
                            RB.MI("UPPERCASE", () => ChangeCaseTo("upper")), RB.MI("Capitalize Each Word", () => ChangeCaseTo("title")), RB.MI("tOGGLE cASE", () => ChangeCaseTo("toggle")))),
                        RB.Sep(), hl, fc)));

            TbBullets = RB.Toggle("IconBullets", null, b => Bullets_Click(null, null), "Bullets");
            TbNumbering = RB.Toggle("IconNumbering", null, b => Numbering_Click(null, null), "Numbering");
            TbLeft = RB.Glyph(new Image { Source = UI.Img("IconAlignLeft"), Width = 16, Height = 16 }, C("AlignLeft", () => AlignLeft_Click(null, null)), "Align Text Left (Ctrl+L)");
            TbCenter = RB.Glyph(new Image { Source = UI.Img("IconAlignCenter"), Width = 16, Height = 16 }, C("Center", () => AlignCenter_Click(null, null)), "Center (Ctrl+E)");
            TbRight = RB.Glyph(new Image { Source = UI.Img("IconAlignRight"), Width = 16, Height = 16 }, C("AlignRight", () => AlignRight_Click(null, null)), "Align Text Right (Ctrl+R)");
            TbJustify = RB.Glyph(new Image { Source = UI.Img("IconAlignJustify"), Width = 16, Height = 16 }, C("Justify", () => AlignJustify_Click(null, null)), "Justify (Ctrl+J)");
            TbMarks = RB.Toggle("IconParagraph", null, on => { Settings.Current.ShowMarks = on; Settings.Save(); DrawOverlay(); }, "Show/Hide ¶ (Ctrl+*)");
            BtnLineSpacing = RB.Small("IconLineSpacing", null, () => LineSpacing_Click(null, null), "Line Spacing", () => null);
            var shading = RB.Split(null, null, C("Shading", () => ApplyShading(_shading)), () => { ColorPalette.ShowText(_shadingTarget, "No Color", c => { if (c.HasValue) { _shading = c.Value; ShadingSwatch.Fill = new SolidColorBrush(_shading); } ApplyShading(c); }); return null; },
                "Shading", false, SwatchIcon("IconShading", out ShadingSwatch, _shading));
            _shadingTarget = (FrameworkElement)shading;
            var paraGroup = RB.Group("Paragraph", () => ParagraphDialog_Click(null, null),
                RB.Stack(
                    RB.Row(
                        RB.Split("IconBullets", null, C("Bullets", () => Bullets_Click(null, null)), BulletLibraryMenu, "Bullets"),
                        RB.Split("IconNumbering", null, C("Numbering", () => Numbering_Click(null, null)), NumberingLibraryMenu, "Numbering"),
                        RB.Small("IconMultilevel", null, null, "Multilevel List", MultilevelMenu),
                        RB.Small("IconOutdent", null, C("DecreaseIndent", () => Outdent_Click(null, null)), "Decrease Indent"),
                        RB.Small("IconIndent", null, C("IncreaseIndent", () => Indent_Click(null, null)), "Increase Indent"),
                        RB.Sep(),
                        RB.Small("IconSort", null, C("Sort", SortParagraphs), "Sort", null),
                        TbMarks),
                    RB.Row(TbLeft, TbCenter, TbRight, TbJustify, RB.Sep(), BtnLineSpacing, RB.Sep(), shading,
                        RB.Small("IconBorders", null, null, "Borders", BordersMenu))));

            StyleGallery = new WrapPanel { Width = 300, Margin = new Thickness(0, 1, 0, 0) };
            BuildStyleUI();
            var styleMore = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            var up = RB.Small(null, null, () => StyleGalleryScroll(-1), "Previous row"); up.Content = new Path { Data = Geometry.Parse("M0,4 L3,0 L6,4 Z"), Fill = Brushes.DimGray };
            var dn = RB.Small(null, null, () => StyleGalleryScroll(1), "Next row"); dn.Content = new Path { Data = Geometry.Parse("M0,0 L6,0 L3,4 Z"), Fill = Brushes.DimGray };
            var more = RB.Small(null, null, null, "More styles"); more.Content = new Path { Data = Geometry.Parse("M0,0 H6 M0,3 L6,3 L3,7 Z"), Stroke = Brushes.DimGray, Fill = Brushes.DimGray };
            more.Click += (s, e) => ShowAllStyles(more);
            up.Height = dn.Height = more.Height = 23;
            styleMore.Children.Add(up); styleMore.Children.Add(dn); styleMore.Children.Add(more);
            var galleryFrame = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xA9, 0xC3, 0xE6)), BorderThickness = new Thickness(1), Background = Brushes.White, Child = new ScrollViewer { Content = StyleGallery, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Height = 70, Focusable = false }, Margin = new Thickness(0, 2, 0, 0) };
            _styleScroll = (ScrollViewer)galleryFrame.Child;
            var stylesGroup = RB.Group("Styles", ShowStylesPane, galleryFrame, styleMore,
                RB.Large("IconChangeStyles", "Change\nStyles", null, "Change the set of styles, colors and fonts used in this document.", ChangeStylesMenu));

            TabHome = Ribbon.AddTab(new RibbonTab("Home")
                .Add(RB.Group("Clipboard", ShowClipboardPane,
                    RB.Split("IconPaste", "Paste", C("Paste", () => Paste_Click(null, null)), () => RB.Menu(
                        RB.MI("Paste", () => Paste_Click(null, null), "IconPaste", "Ctrl+V"),
                        RB.MI("Paste Special...", PasteSpecial),
                        RB.MI("Paste as Plain Text", () => PastePlain_Click(null, null), null, "Ctrl+Shift+V"),
                        RB.MI("Paste as Hyperlink", PasteAsHyperlink)), "Paste the contents of the Clipboard.", true),
                    RB.Stack(
                        RB.Small("IconCut", "Cut", C("Cut", () => Cut_Click(null, null)), "Cut the selection and put it on the Clipboard."),
                        RB.Small("IconCopy", "Copy", C("Copy", () => Copy_Click(null, null)), "Copy the selection and put it on the Clipboard."),
                        FormatPainterButton())))
                .Add(fontGroup)
                .Add(paraGroup)
                .Add(stylesGroup)
                .Add(RB.Group("Editing", null, RB.Stack(
                    RB.Split("IconFind", "Find", C("Find", () => Find_Click(null, null)), () => RB.Menu(RB.MI("Find...", () => Find_Click(null, null), "IconFind", "Ctrl+F"), RB.MI("Go To...", () => GoTo_Click(null, null), null, "Ctrl+G")), "Find text in the document."),
                    RB.Small("IconReplace", "Replace", C("Replace", () => Replace_Click(null, null)), "Replace text in the document."),
                    RB.Small("IconSelect", "Select", null, "Select text or objects in the document.", () => RB.Menu(
                        RB.MI("Select All", () => SelectAll_Click(null, null), "IconSelect", "Ctrl+A"),
                        RB.MI("Select Current Paragraph", SelectParagraph),
                        RB.MI("Select Text with Similar Formatting", SelectSimilarFormatting)))))));

            // ---------------- Insert ----------------
            BtnTable = RB.Large("IconTable", "Table", null, "Insert or draw a table into the document.", TableMenu);
            TabInsert = Ribbon.AddTab(new RibbonTab("Insert")
                .Add(RB.Group("Pages", null, RB.Stack(
                    RB.Small("IconCoverPage", "Cover Page", null, "Insert a fully formatted cover page.", CoverPageMenu),
                    RB.Small("IconBlankPage", "Blank Page", C("BlankPage", InsertBlankPage), "Insert a new blank page at the cursor."),
                    RB.Small("IconPageBreak", "Page Break", C("PageBreak", () => InsertPageBreak_Click(null, null)), "Start the next page at the current position (Ctrl+Enter)."))))
                .Add(RB.Group("Tables", null, BtnTable))
                .Add(RB.Group("Illustrations", null,
                    RB.Large("IconPicture", "Picture", C("Picture", () => InsertPicture_Click(null, null)), "Insert a picture from a file."),
                    RB.Large("IconClipArt", "Clip Art", C("ClipArt", ShowClipArtPane), "Insert clip art into the document."),
                    RB.Large("IconShapes", "Shapes", null, "Insert ready-made shapes.", () => { ShowShapesGallery(); return null; }),
                    RB.Large("IconSmartArt", "SmartArt", C("SmartArt", InsertSmartArt), "Insert a SmartArt graphic to visually communicate information."),
                    RB.Large("IconChart", "Chart", C("Chart", InsertChart), "Insert a chart to illustrate and compare data.")))
                .Add(RB.Group("Links", null, RB.Stack(
                    RB.Small("IconLink", "Hyperlink", C("Hyperlink", () => InsertLink_Click(null, null)), "Create a link to a web page or a place in the document (Ctrl+K)."),
                    RB.Small("IconBookmark", "Bookmark", C("Bookmark", BookmarkDialog), "Create a bookmark to assign a name to a specific point in the document."),
                    RB.Small("IconCrossRef", "Cross-reference", C("CrossReference", CrossReference), "Refer to headings, figures and bookmarks."))))
                .Add(RB.Group("Header & Footer", null,
                    RB.Large("IconHeader", "Header", null, "Edit the header of the document.", () => HeaderFooterMenu(true)),
                    RB.Large("IconFooter", "Footer", null, "Edit the footer of the document.", () => HeaderFooterMenu(false)),
                    RB.Large("IconPageNumber", "Page\nNumber", null, "Insert page numbers into the document.", PageNumberMenu)))
                .Add(RB.Group("Text", null,
                    RB.Large("IconTextBox", "Text\nBox", null, "Insert preformatted text boxes.", TextBoxMenu),
                    RB.Large("IconQuickParts", "Quick\nParts", null, "Insert pieces of reusable content, document properties and fields.", QuickPartsMenu),
                    RB.Large("IconWordArt", "WordArt", null, "Insert decorative text in your document.", () => { ShowWordArtGallery(); return null; }),
                    RB.Large("IconDropCap", "Drop\nCap", null, "Create a large capital letter at the beginning of a paragraph.", DropCapMenu),
                    RB.Stack(
                        RB.Small("IconSignature", "Signature Line", C("SignatureLine", InsertSignatureLine), "Insert a signature line."),
                        RB.Small("IconDate", "Date & Time", C("DateTime", () => InsertDate_Click(null, null)), "Insert the current date or time."),
                        RB.Small("IconObject", "Object", null, "Insert an embedded object.", () => RB.Menu(RB.MI("Text from File...", () => InsertFile_Click(null, null)))))))
                .Add(RB.Group("Symbols", null,
                    RB.Large("IconEquation", "Equation", null, "Insert common mathematical equations or build your own.", EquationMenu),
                    RB.Large("IconSymbol", "Symbol", null, "Insert symbols that are not on your keyboard.", SymbolMenu))));

            // ---------------- Page Layout ----------------
            TxtIndentL = Spin(52, v => SetIndentFromRibbon(v, null), "Left indent (inches)");
            TxtIndentR = Spin(52, v => SetIndentFromRibbon(null, v), "Right indent (inches)");
            TxtSpaceBefore = Spin(52, v => SetSpacingFromRibbon(v, null), "Spacing before (points)");
            TxtSpaceAfter = Spin(52, v => SetSpacingFromRibbon(null, v), "Spacing after (points)");
            TabLayout = Ribbon.AddTab(new RibbonTab("Page Layout")
                .Add(RB.Group("Themes", null,
                    RB.Large("IconThemes", "Themes", null, "Change the overall design of the document, including colors, fonts and effects.", () => { ShowThemesGallery(); return null; }),
                    RB.Stack(
                        RB.Small("IconPageColor", "Colors", null, "Change the colors for the current theme.", () => ThemePartMenu("colors")),
                        RB.Small("IconFont", "Fonts", null, "Change the fonts for the current theme.", () => ThemePartMenu("fonts")))))
                .Add(RB.Group("Page Setup", () => PageSetup_Click(null, null),
                    RB.Large("IconMargins", "Margins", null, "Select the margin sizes for the document.", MarginsMenu),
                    RB.Large("IconOrientation", "Orientation", null, "Switch the pages between portrait and landscape layouts.", () => RB.Menu(
                        RB.MI("Portrait", () => SetOrientation(false), null, null, !_page.Landscape), RB.MI("Landscape", () => SetOrientation(true), null, null, _page.Landscape))),
                    RB.Large("IconSize", "Size", null, "Choose a paper size for the document.", SizeMenu),
                    RB.Large("IconColumns", "Columns", null, "Split text into two or more columns.", ColumnsMenu),
                    RB.Stack(
                        RB.Small("IconBreaks", "Breaks", null, "Add page, section or column breaks to the document.", BreaksMenu),
                        RB.Small("IconLineNumbers", "Line Numbers", null, "Add line numbers in the margin alongside each line of the document.", () => RB.Menu(
                            RB.MI("None", () => SetLineNumbers(false), null, null, !_page.LineNumbers), RB.MI("Continuous", () => SetLineNumbers(true), null, null, _page.LineNumbers))),
                        RB.Small("IconHyphenation", "Hyphenation", null, "Turn on hyphenation, which allows Valco Word to break lines between the syllables of words.", () => RB.Menu(
                            RB.MI("None", () => SetHyphenation(false), null, null, !_page.Hyphenation), RB.MI("Automatic", () => SetHyphenation(true), null, null, _page.Hyphenation))))))
                .Add(RB.Group("Page Background", null,
                    RB.Large("IconWatermark", "Watermark", null, "Insert ghosted text behind the content on the page.", () => { ShowWatermarkGallery(); return null; }),
                    RB.Large("IconPageColor", "Page\nColor", null, "Choose a color for the background of the page.", () => { ColorPalette.ShowText(_pageColorTarget, "No Color", SetPageColor); return null; }),
                    RB.Large("IconPageBorders", "Page\nBorders", C("PageBorders", PageBordersDialog), "Add or change the border around the page.")))
                .Add(RB.Group("Paragraph", () => ParagraphDialog_Click(null, null),
                    RB.Stack(new TextBlock { Text = "Indent", FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)), Margin = new Thickness(2, 1, 0, 2) },
                        Labeled("IconIndent", "Left:", TxtIndentL), Labeled("IconOutdent", "Right:", TxtIndentR)),
                    RB.Stack(new TextBlock { Text = "Spacing", FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)), Margin = new Thickness(2, 1, 0, 2) },
                        Labeled("IconLineSpacing", "Before:", TxtSpaceBefore), Labeled("IconLineSpacing", "After:", TxtSpaceAfter))))
                .Add(RB.Group("Arrange", null,
                    RB.Large("IconWrap", "Position", null, "Position the selected picture on the page.", PositionMenu),
                    RB.Stack(
                        RB.Small("IconWrap", "Text Wrapping", null, "Change the way text wraps around the selected picture.", PositionMenu),
                        RB.Small("IconAlignCenter", "Align", null, "Align the selected picture.", () => RB.Menu(RB.MI("Align Left", () => AlignPicture(TextAlignment.Left)), RB.MI("Align Center", () => AlignPicture(TextAlignment.Center)), RB.MI("Align Right", () => AlignPicture(TextAlignment.Right)))),
                        RB.Small("IconRotate", "Rotate", null, "Rotate or flip the selected picture.", RotateMenu)))));
            _pageColorTarget = (FrameworkElement)((Border)TabLayout.Groups[2]).Child;

            // ---------------- References ----------------
            CbBibStyle = RB.Combo(70);
            CbBibStyle.ItemsSource = new[] { "APA", "MLA", "Chicago" };
            CbBibStyle.SelectedItem = "APA";
            CbBibStyle.SelectionChanged += (s, e) => { if (!_updatingUI && CbBibStyle.SelectedItem is string st) { _ex.BibStyle = st; RefreshCitations(); } };
            TabRefs = Ribbon.AddTab(new RibbonTab("References")
                .Add(RB.Group("Table of Contents", null,
                    RB.Large("IconTOC", "Table of\nContents", null, "Add a table of contents to the document.", TocMenu),
                    RB.Stack(
                        RB.Small("IconAddText", "Add Text", null, "Add the current paragraph as an entry in the table of contents.", () => RB.Menu(
                            RB.MI("Do Not Show in Table of Contents", () => ApplyStyle("Normal")), RB.MI("Level 1", () => ApplyStyle("Heading1")), RB.MI("Level 2", () => ApplyStyle("Heading2")), RB.MI("Level 3", () => ApplyStyle("Heading3")))),
                        RB.Small("IconUpdate", "Update Table", C("UpdateTOC", () => UpdateGenerated(SectionKind.TOC)), "Update the table of contents so all entries refer to the correct page numbers."))))
                .Add(RB.Group("Footnotes", null,
                    RB.Large("IconFootnote", "Insert\nFootnote", C("InsertFootnote", () => InsertNote(false)), "Add a footnote to the document (Alt+Ctrl+F)."),
                    RB.Stack(
                        RB.Small("IconEndnote", "Insert Endnote", C("InsertEndnote", () => InsertNote(true)), "Add an endnote to the document (Alt+Ctrl+D)."),
                        RB.Small("IconNextChange", "Next Footnote", null, "Navigate to the next footnote in the document.", () => RB.Menu(RB.MI("Next Footnote", () => GoToNote(1)), RB.MI("Previous Footnote", () => GoToNote(-1)))),
                        RB.Small("IconFootnote", "Show Notes", C("ShowNotes", ShowNotes), "Scroll the document to show where footnotes and endnotes are located."))))
                .Add(RB.Group("Citations & Bibliography", null,
                    RB.Large("IconCitation", "Insert\nCitation", null, "Cite a book, journal article or other source.", CitationMenu),
                    RB.Stack(
                        RB.Small("IconManageSources", "Manage Sources", C("ManageSources", ManageSources), "Organize the sources cited in the document."),
                        Labeled(null, "Style:", CbBibStyle),
                        RB.Small("IconBibliography", "Bibliography", null, "Add a bibliography listing all sources cited in the document.", () => RB.Menu(
                            RB.MI("Bibliography", () => InsertBibliography("Bibliography")), RB.MI("Works Cited", () => InsertBibliography("Works Cited")), RB.MI("References", () => InsertBibliography("References")))))))
                .Add(RB.Group("Captions", null,
                    RB.Large("IconCaption", "Insert\nCaption", C("InsertCaption", InsertCaption), "Add a caption to a picture or other image."),
                    RB.Stack(
                        RB.Small("IconTOC", "Insert Table of Figures", C("TableOfFigures", () => InsertGenerated(SectionKind.Figures)), "Insert a table of figures into the document."),
                        RB.Small("IconUpdate", "Update Table", C("UpdateFigures", () => UpdateGenerated(SectionKind.Figures)), "Update the table of figures."),
                        RB.Small("IconCrossRef", "Cross-reference", C("CrossReference2", CrossReference), "Refer to items such as headings, figures and tables."))))
                .Add(RB.Group("Index", null,
                    RB.Large("IconMarkEntry", "Mark\nEntry", C("MarkEntry", () => MarkEntry("XE")), "Include the selected text in the index (Alt+Shift+X)."),
                    RB.Stack(
                        RB.Small("IconIndex", "Insert Index", C("InsertIndex", () => InsertGenerated(SectionKind.Index)), "Insert an index into the document."),
                        RB.Small("IconUpdate", "Update Index", C("UpdateIndex", () => UpdateGenerated(SectionKind.Index)), "Update the index so all entries refer to the correct page numbers."))))
                .Add(RB.Group("Table of Authorities", null,
                    RB.Large("IconMarkEntry", "Mark\nCitation", C("MarkCitation", () => MarkEntry("TA")), "Add the selected text as an entry in the table of authorities (Alt+Shift+I)."),
                    RB.Stack(
                        RB.Small("IconTOA", "Insert Table of Authorities", C("InsertTOA", () => InsertGenerated(SectionKind.TOA)), "Insert a table of authorities."),
                        RB.Small("IconUpdate", "Update Table", C("UpdateTOA", () => UpdateGenerated(SectionKind.TOA)), "Update the table of authorities.")))));

            // ---------------- Mailings ----------------
            TbPreviewMerge = RB.Toggle("IconPreviewResults", "Preview Results", on => SetMergePreview(on), "Replace the merge fields in your document with actual data from your recipient list.");
            TbHighlightFields = RB.Toggle("IconMergeField", "Highlight Merge Fields", on => HighlightMergeFields(on), "Highlight the fields you have inserted into the document.");
            TxtRecord = new TextBox { Width = 34, Height = 20, Text = "1", TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };
            TxtRecord.KeyDown += (s, e) => { if (e.Key == Key.Enter && int.TryParse(TxtRecord.Text, out var n)) GoToRecord(n - 1); };
            TabMail = Ribbon.AddTab(new RibbonTab("Mailings")
                .Add(RB.Group("Create", null,
                    RB.Large("IconEnvelope", "Envelopes", C("Envelopes", EnvelopesDialog), "Create and print envelopes."),
                    RB.Large("IconLabels", "Labels", C("Labels", LabelsDialog), "Create and print labels.")))
                .Add(RB.Group("Start Mail Merge", null,
                    RB.Large("IconMailMerge", "Start Mail\nMerge", null, "Start a mail merge to create a form letter that you print or e-mail many times.", () => RB.Menu(
                        RB.MI("Letters", () => StartMerge("Letters")), RB.MI("Envelopes...", () => { StartMerge("Envelopes"); EnvelopesDialog(); }), RB.MI("Labels...", () => { StartMerge("Labels"); LabelsDialog(); }),
                        RB.MI("Directory", () => StartMerge("Directory")), null, RB.MI("Normal Word Document", () => StartMerge(null)))),
                    RB.Large("IconRecipients", "Select\nRecipients", null, "Choose the list of people you intend to send the letter to.", () => RB.Menu(
                        RB.MI("Type New List...", TypeNewList), RB.MI("Use Existing List...", UseExistingList))),
                    RB.Large("IconRecipients", "Edit\nRecipient List", C("EditRecipients", EditRecipientList), "Make changes to the list of recipients.")))
                .Add(RB.Group("Write & Insert Fields", null,
                    RB.Stack(TbHighlightFields),
                    RB.Large("IconEnvelope", "Address\nBlock", C("AddressBlock", InsertAddressBlock), "Add an address to your letter."),
                    RB.Large("IconComment", "Greeting\nLine", C("GreetingLine", InsertGreetingLine), "Add a greeting line, such as \"Dear «First Name»,\" to the document."),
                    RB.Large("IconMergeField", "Insert Merge\nField", null, "Add a field from the recipient list to the document.", MergeFieldMenu)))
                .Add(RB.Group("Preview Results", null,
                    RB.Large("IconPreviewResults", "Preview\nResults", C("PreviewResults", () => { TbPreviewMerge.IsChecked = !(TbPreviewMerge.IsChecked == true); SetMergePreview(TbPreviewMerge.IsChecked == true); }), "Replace the merge fields with actual data from your recipient list."),
                    RB.Stack(
                        RB.Row(RB.Small("IconFirstRecord", null, () => GoToRecord(0), "First Record"), RB.Small("IconPrevRecord", null, () => GoToRecord(_ex.MergeIndex - 1), "Previous Record"),
                               TxtRecord, RB.Small("IconNextRecord", null, () => GoToRecord(_ex.MergeIndex + 1), "Next Record"), RB.Small("IconLastRecord", null, () => GoToRecord(_ex.MergeRecords.Count - 1), "Last Record")),
                        RB.Small("IconFind", "Find Recipient", C("FindRecipient", FindRecipient), "Find and preview a specific recipient."))))
                .Add(RB.Group("Finish", null,
                    RB.Large("IconFinishMerge", "Finish &\nMerge", null, "Complete the mail merge.", () => RB.Menu(
                        RB.MI("Edit Individual Documents...", () => FinishMerge(false), "IconFinishMerge"), RB.MI("Print Documents...", () => FinishMerge(true), "IconPrint"), RB.MI("Send E-mail Messages...", () => FinishMerge("email"), "IconEnvelope"))))));

            // ---------------- Review ----------------
            TbTrack = RB.Toggle("IconTrack", null, on => SetTrackChanges(on), "Track Changes (Ctrl+Shift+E)");
            var trackBig = new ToggleButton { Style = RB.TglStyle, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(3, 2, 3, 2), ToolTip = RB.Tip("Track Changes (Ctrl+Shift+E)", "Track all changes made to the document, including insertions and deletions.") };
            var tsp = new StackPanel(); tsp.Children.Add(new Image { Source = UI.Img("IconTrack"), Width = 32, Height = 32 }); tsp.Children.Add(new TextBlock { Text = "Track\nChanges", TextAlignment = TextAlignment.Center });
            trackBig.Content = tsp;
            trackBig.Click += (s, e) => SetTrackChanges(trackBig.IsChecked == true);
            TbTrack = trackBig;
            CbMarkup = RB.Combo(150);
            CbMarkup.ItemsSource = new[] { "Final Showing Markup", "Final", "Original Showing Markup", "Original" };
            CbMarkup.SelectedIndex = 0;
            CbMarkup.SelectionChanged += (s, e) => { if (!_updatingUI) ApplyMarkupView(CbMarkup.SelectedIndex); };
            TabReview = Ribbon.AddTab(new RibbonTab("Review")
                .Add(RB.Group("Proofing", null,
                    RB.Large("IconSpellGrammar", "Spelling &\nGrammar", C("Spelling", () => SpellingDialog_Click(null, null)), "Check the spelling and grammar of text in the document (F7)."),
                    RB.Stack(
                        RB.Small("IconResearch", "Research", C("Research", () => ShowResearchPane("Research")), "Open the Research task pane to search through reference materials."),
                        RB.Small("IconThesaurus", "Thesaurus", C("Thesaurus", () => ShowResearchPane("Thesaurus")), "Suggest other words with a similar meaning (Shift+F7)."),
                        RB.Small("IconTranslate", "Translate", C("Translate", () => ShowResearchPane("Translate")), "Translate the selected text into a different language.")),
                    RB.Stack(RB.Small("IconWordCount", "Word Count", C("WordCount", () => WordCount_Click(null, null)), "Find out the number of words, characters, paragraphs and lines in the document."))))
                .Add(RB.Group("Comments", null,
                    RB.Large("IconComment", "New\nComment", C("NewComment", NewComment), "Add a comment about the selection."),
                    RB.Split("IconDeleteComment", "Delete", C("DeleteComment", DeleteComment), () => RB.Menu(RB.MI("Delete", DeleteComment), RB.MI("Delete All Comments in Document", DeleteAllComments)), "Delete the selected comment.", true),
                    RB.Stack(
                        RB.Small("IconPrevComment", "Previous", C("PrevComment", () => GoToComment(-1)), "Navigate to the previous comment."),
                        RB.Small("IconNextComment", "Next", C("NextComment", () => GoToComment(1)), "Navigate to the next comment."))))
                .Add(RB.Group("Tracking", null,
                    TbTrack,
                    RB.Stack(CbMarkup,
                        RB.Small("IconComment", "Show Markup", null, "Choose what kind of markup to show in the document.", ShowMarkupMenu),
                        RB.Small("IconDocMap", "Reviewing Pane", C("ReviewingPane", ShowReviewingPane), "Show revisions and comments in a separate pane."))))
                .Add(RB.Group("Changes", null,
                    RB.Split("IconAccept", "Accept", C("Accept", () => AcceptReject(true, false)), () => RB.Menu(RB.MI("Accept and Move to Next", () => AcceptReject(true, false)), RB.MI("Accept Change", () => AcceptReject(true, false, false)), RB.MI("Accept All Changes in Document", () => AcceptReject(true, true))), "Accept this change and move to the next proposed change.", true),
                    RB.Split("IconReject", "Reject", C("Reject", () => AcceptReject(false, false)), () => RB.Menu(RB.MI("Reject and Move to Next", () => AcceptReject(false, false)), RB.MI("Reject Change", () => AcceptReject(false, false, false)), RB.MI("Reject All Changes in Document", () => AcceptReject(false, true))), "Reject this change and move to the next proposed change.", true),
                    RB.Stack(
                        RB.Small("IconPrevChange", "Previous", C("PrevChange", () => GoToChange(-1)), "Navigate to the previous revision."),
                        RB.Small("IconNextChange", "Next", C("NextChange", () => GoToChange(1)), "Navigate to the next revision."))))
                .Add(RB.Group("Compare", null, RB.Large("IconCompare", "Compare", C("Compare", CompareDocuments), "Compare two versions of a document.")))
                .Add(RB.Group("Protect", null, RB.Large("IconProtect", "Protect\nDocument", C("Protect", ProtectDocument), "Restrict how people can edit the document."))));

            // ---------------- View ----------------
            TbPrintLayout = ViewToggle("IconPrintLayout", "Print\nLayout", "print");
            TbFullScreen = ViewToggle("IconFullScreen", "Full Screen\nReading", "full");
            TbWebLayout = ViewToggle("IconWebLayout", "Web\nLayout", "web");
            TbOutline = ViewToggle("IconOutline", "Outline", "outline");
            TbDraft = ViewToggle("IconDraft", "Draft", "draft");
            CkRuler = Check("Ruler", on => { Settings.Current.ShowRuler = on; Settings.Save(); ApplyViewSettings(); });
            CkGridlines = Check("Gridlines", on => { _gridlines = on; DrawOverlay(); });
            CkDocMap = Check("Document Map", on => ShowLeftPane(on ? "map" : null));
            CkThumbs = Check("Thumbnails", on => ShowLeftPane(on ? "thumbs" : null));
            TabView = Ribbon.AddTab(new RibbonTab("View")
                .Add(RB.Group("Document Views", null, TbPrintLayout, TbFullScreen, TbWebLayout, TbOutline, TbDraft))
                .Add(RB.Group("Show/Hide", null, RB.Stack(CkRuler, CkGridlines), RB.Stack(CkDocMap, CkThumbs)))
                .Add(RB.Group("Zoom", null,
                    RB.Large("IconZoom", "Zoom", C("Zoom", () => ZoomDialog_Click(null, null)), "Open the Zoom dialog box to specify the zoom level of the document."),
                    RB.Large("IconOnePage", "100%", C("Zoom100", () => SetZoom(1)), "Zoom the document to 100% of the normal size."),
                    RB.Stack(
                        RB.Small("IconOnePage", "One Page", C("OnePage", () => ZoomToFit("page")), "Zoom the document so that an entire page fits in the window."),
                        RB.Small("IconTwoPages", "Two Pages", C("TwoPages", () => ZoomToFit("two")), "Zoom the document so that two pages fit in the window."),
                        RB.Small("IconPageWidth", "Page Width", C("PageWidth", () => ZoomToFit("width")), "Zoom the document so that the width of the page matches the width of the window."))))
                .Add(RB.Group("Window", null,
                    RB.Large("IconNewWindow", "New\nWindow", C("NewWindow", OpenMirrorWindow), "Open a new window containing a view of the current document."),
                    RB.Large("IconArrange", "Arrange\nAll", C("ArrangeAll", ArrangeAll), "Tile all open windows side by side on the screen."),
                    RB.Large("IconSplitWindow", "Split", C("Split", ToggleSplit), "Split the current window into two parts."),
                    RB.Stack(
                        RB.Small("IconSideBySide", "View Side by Side", C("SideBySide", ViewSideBySide), "View two documents side by side to compare their contents."),
                        RB.Small("IconRefresh", "Synchronous Scrolling", () => { _syncScroll = !_syncScroll; Flash(_syncScroll ? "Synchronous scrolling is on." : "Synchronous scrolling is off."); }, "Synchronize the scrolling of two documents."),
                        RB.Small("IconArrange", "Reset Window Position", C("ResetWindows", ViewSideBySide), "Reset the window position of the documents being compared side by side.")),
                    RB.Large("IconSwitchWindows", "Switch\nWindows", null, "Switch to another currently open window.", SwitchWindowsMenu)))
                .Add(RB.Group("Macros", null, RB.Large("IconMacro", "Macros", null, "View the list of macros, or record a new macro.", MacrosMenu))));

            // ---------------- Contextual tabs ----------------
            CkHeaderRow = Check("Header Row", on => ApplyTableStyle(null));
            CkTotalRow = Check("Total Row", on => ApplyTableStyle(null));
            CkBandedRows = Check("Banded Rows", on => ApplyTableStyle(null));
            CkFirstCol = Check("First Column", on => ApplyTableStyle(null));
            CkLastCol = Check("Last Column", on => ApplyTableStyle(null));
            CkBandedCols = Check("Banded Columns", on => ApplyTableStyle(null));
            CkHeaderRow.IsChecked = CkBandedRows.IsChecked = CkFirstCol.IsChecked = true;
            var tableTools = Color.FromRgb(0xF2, 0xC2, 0x3D);
            TabTableDesign = Ribbon.AddTab(new RibbonTab("Design") { ContextGroup = "Table Tools", ContextColor = tableTools, Visible = false }
                .Add(RB.Group("Table Style Options", null, RB.Stack(CkHeaderRow, CkTotalRow, CkBandedRows), RB.Stack(CkFirstCol, CkLastCol, CkBandedCols)))
                .Add(RB.Group("Table Styles", null, TableStylesStrip(),
                    RB.Stack(
                        RB.Small("IconShading", "Shading", null, "Color the background behind the selected cells.", () => { CellShading_Click(null, null); return null; }),
                        RB.Small("IconBorders", "Borders", null, "Customize the borders of the selected cells.", TableBordersMenu)))));
            TxtCellW = Spin(52, v => SetColumnWidth(v), "Column width (inches)");
            TabTableLayout = Ribbon.AddTab(new RibbonTab("Layout") { ContextGroup = "Table Tools", ContextColor = tableTools, Visible = false }
                .Add(RB.Group("Table", null, RB.Stack(
                    RB.Small("IconSelect", "Select", null, "Select the current cell, row, column or entire table.", () => RB.Menu(
                        RB.MI("Select Cell", () => SelectTablePart("cell")), RB.MI("Select Column", () => SelectTablePart("col")), RB.MI("Select Row", () => SelectTablePart("row")), RB.MI("Select Table", () => SelectTablePart("table")))),
                    RB.Small("IconGridlines", "View Gridlines", () => TableBorders_Click(null, null), "Show or hide the gridlines within the table."),
                    RB.Small("IconProperties", "Properties", () => ColWidth_Click(null, null), "Show the Table Properties dialog box."))))
                .Add(RB.Group("Rows & Columns", null,
                    RB.Large("IconDelete", "Delete", null, "Delete rows, columns, cells or the table.", () => RB.Menu(
                        RB.MI("Delete Columns", () => DelCol_Click(null, null)), RB.MI("Delete Rows", () => DelRow_Click(null, null)), RB.MI("Delete Table", () => DelTable_Click(null, null)))),
                    RB.Large("IconMoveUp", "Insert\nAbove", () => RowAbove_Click(null, null), "Add a new row directly above the selected row."),
                    RB.Stack(
                        RB.Small("IconMoveDown", "Insert Below", () => RowBelow_Click(null, null), "Add a new row directly below the selected row."),
                        RB.Small("IconPromote", "Insert Left", () => ColLeft_Click(null, null), "Add a new column directly to the left."),
                        RB.Small("IconDemote", "Insert Right", () => ColRight_Click(null, null), "Add a new column directly to the right."))))
                .Add(RB.Group("Merge", null, RB.Stack(
                    RB.Small("IconMerge", "Merge Cells", C("MergeCells", MergeCells), "Merge the selected cells into one cell."),
                    RB.Small("IconSplit", "Split Cells", C("SplitCells", SplitCell), "Split the selected cell into multiple cells."),
                    RB.Small("IconSplitWindow", "Split Table", C("SplitTable", SplitTable), "Split the table into two tables at the current row."))))
                .Add(RB.Group("Cell Size", null,
                    RB.Large("IconPageWidth", "AutoFit", null, "Automatically resize the column widths.", () => RB.Menu(
                        RB.MI("AutoFit Contents", () => AutoFitTable("contents")), RB.MI("AutoFit Window", () => AutoFitTable("window")))),
                    RB.Stack(Labeled(null, "Width:", TxtCellW),
                        RB.Small("IconColumns", "Distribute Columns", () => AutoFitTable("window"), "Distribute the width of the selected columns equally."))))
                .Add(RB.Group("Alignment", null,
                    RB.Stack(
                        RB.Row(RB.Small("IconAlignLeft", null, () => CellAlign(TextAlignment.Left), "Align Left"), RB.Small("IconAlignCenter", null, () => CellAlign(TextAlignment.Center), "Align Center"), RB.Small("IconAlignRight", null, () => CellAlign(TextAlignment.Right), "Align Right")),
                        RB.Small("IconMargins", "Cell Margins", C("CellMargins", CellMargins), "Customize cell margins and the spacing between cells."))))
                .Add(RB.Group("Data", null,
                    RB.Large("IconSort", "Sort", C("SortTable", SortTable), "Sort the selected rows."),
                    RB.Stack(
                        RB.Small("IconTableStyle", "Repeat Header Rows", C("RepeatHeader", RepeatHeaderRows), "Mark the selected rows as header rows that repeat at the top of each page."),
                        RB.Small("IconConvert", "Convert to Text", C("ConvertToText", ConvertTableToText), "Convert the table into regular text."),
                        RB.Small("IconFormula", "Formula", C("Formula", TableFormula), "Add a formula to the cell to do a simple calculation, such as SUM or AVERAGE.")))));

            TxtPicW = Spin(52, v => SetPictureSize(v, null), "Shape width (inches)");
            TxtPicH = Spin(52, v => SetPictureSize(null, v), "Shape height (inches)");
            TabPicture = Ribbon.AddTab(new RibbonTab("Format") { ContextGroup = "Picture Tools", ContextColor = Color.FromRgb(0xB0, 0x8E, 0xD8), Visible = false }
                .Add(RB.Group("Adjust", null, RB.Stack(
                        RB.Small("IconBrightness", "Brightness", null, "Adjust the brightness of the picture.", () => AdjustMenu("brightness")),
                        RB.Small("IconContrast", "Contrast", null, "Increase or decrease the contrast of the picture.", () => AdjustMenu("contrast")),
                        RB.Small("IconRecolor", "Recolor", null, "Recolor the picture to give it a stylized effect.", RecolorMenu)),
                    RB.Stack(
                        RB.Small("IconPicture", "Compress Pictures", C("CompressPictures", CompressPictures), "Compress pictures in the document to reduce its size."),
                        RB.Small("IconPicture", "Change Picture", C("ChangePicture", ChangePicture), "Change to a different picture, keeping the size."),
                        RB.Small("IconResetPicture", "Reset Picture", C("ResetPicture", ResetPicture), "Discard all of the formatting changes made to this picture."))))
                .Add(RB.Group("Picture Styles", null, PictureStylesStrip(), RB.Stack(
                    RB.Small("IconPageBorders", "Picture Border", null, "Specify the color of the outline for the selected picture.", () => { ColorPalette.ShowText(Ribbon, "No Outline", c => ApplyPictureEffect("border:" + (c.HasValue ? Fmt.Hex(c.Value) : ""))); return null; }),
                    RB.Small("IconPictureStyle", "Picture Effects", null, "Apply a visual effect to the picture, such as shadow, glow, reflection or soft edges.", PictureEffectsMenu))))
                .Add(RB.Group("Arrange", null, RB.Stack(
                    RB.Small("IconWrap", "Position", null, "Position the selected picture on the page.", PositionMenu),
                    RB.Small("IconWrap", "Text Wrapping", null, "Change the way text wraps around the selected picture.", PositionMenu),
                    RB.Small("IconRotate", "Rotate", null, "Rotate or flip the selected picture.", RotateMenu))))
                .Add(RB.Group("Size", () => { if (_selectedImage != null) PictureSize(_selectedImage); },
                    RB.Large("IconCrop", "Crop", C("Crop", CropPicture), "Crop the picture to remove unwanted parts."),
                    RB.Stack(Labeled(null, "Height:", TxtPicH), Labeled(null, "Width:", TxtPicW)))));

            TabOutline = Ribbon.AddTab(new RibbonTab("Outlining") { ContextGroup = "Outline", ContextColor = Color.FromRgb(0x7F, 0xB8, 0x5E), Visible = false }
                .Add(RB.Group("Outline Tools", null,
                    RB.Large("IconPromote", "Promote to\nHeading 1", () => SetOutlineLevel(1), "Promote to Heading 1."),
                    RB.Stack(
                        RB.Row(RB.Small("IconPromote", null, () => ChangeOutlineLevel(-1), "Promote"), RB.Small("IconDemote", null, () => ChangeOutlineLevel(1), "Demote"),
                               RB.Small("IconDemote", null, () => SetOutlineLevel(0), "Demote to Body Text")),
                        RB.Row(RB.Small("IconMoveUp", null, () => MoveParagraph(-1), "Move Up"), RB.Small("IconMoveDown", null, () => MoveParagraph(1), "Move Down")))))
                .Add(RB.Group("Close", null, RB.Large("IconDelete", "Close\nOutline View", () => SetView("print"), "Close Outline view and return to Print Layout."))));

            Ribbon.TabChanged += t => { if (t != TabOutline && _view == "outline" && t.ContextGroup == null) { } };
            if (Settings.Current.RibbonMinimized) Ribbon.ToggleMinimize();
            BuildViewButtons();
            ApplyViewSettings();
        }

        FrameworkElement _fontColorTarget, _highlightTarget, _shadingTarget, _pageColorTarget;
        ScrollViewer _styleScroll;

        ToggleButton ViewToggle(string icon, string label, string view)
        {
            var b = new ToggleButton { Style = RB.TglStyle, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(3, 2, 3, 2), Tag = view, MinWidth = 44 };
            var sp = new StackPanel();
            sp.Children.Add(new Image { Source = UI.Img(icon), Width = 32, Height = 32 });
            sp.Children.Add(new TextBlock { Text = label, TextAlignment = TextAlignment.Center });
            b.Content = sp;
            b.ToolTip = RB.Tip(label.Replace("\n", " "), null);
            b.Click += (s, e) => SetView(view);
            return b;
        }

        void BuildViewButtons()
        {
            ViewButtons.Children.Clear();
            foreach (var (icon, tip, view) in new[] { ("IconPrintLayout", "Print Layout", "print"), ("IconFullScreen", "Full Screen Reading", "full"), ("IconWebLayout", "Web Layout", "web"), ("IconOutline", "Outline", "outline"), ("IconDraft", "Draft", "draft") })
            {
                var b = new ToggleButton { Style = (Style)FindResource("StatusButton"), Tag = view, ToolTip = tip, Content = new Image { Source = UI.Img(icon), Width = 16, Height = 16 } };
                b.Click += (s, e) => SetView(view);
                ViewButtons.Children.Add(b);
            }
        }

        Button FormatPainterButton()
        {
            var b = RB.Small("IconFormatPainter", "Format Painter", null, "Copy formatting from one place and apply it to another. Double-click to apply the same formatting to multiple places.");
            b.Click += (s, e) => StartFormatPainter(false);
            b.MouseDoubleClick += (s, e) => StartFormatPainter(true);
            _formatPainterButton = b;
            return b;
        }

        // =====================================================================================
        //  Title bar: Office button + Quick Access Toolbar
        // =====================================================================================
        Button _orb;
        void BuildTitleBar()
        {
            // A Canvas has no desired size, so the window's 27px title row never clips the orb;
            // the orb deliberately hangs down over the tab row like Office 2007's.
            var canvas = new Canvas { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = false };
            _orb = new Button { Width = 40, Height = 40, Focusable = false, Cursor = Cursors.Hand, ToolTip = RB.Tip("Valco Button", "Click here to open, save or print, and to see everything else you can do with your document.") };
            _orb.Template = OrbTemplate();
            _orb.Click += (s, e) => ShowOfficeMenu();
            WindowChrome.SetIsHitTestVisibleInChrome(_orb, true);
            Canvas.SetLeft(_orb, 5); Canvas.SetTop(_orb, 4);
            QatPanel = new StackPanel { Orientation = Orientation.Horizontal, Height = 22 };
            WindowChrome.SetIsHitTestVisibleInChrome(QatPanel, true);
            Canvas.SetLeft(QatPanel, 52); Canvas.SetTop(QatPanel, 3);
            BuildQat();
            canvas.Children.Add(QatPanel);
            canvas.Children.Add(_orb);
            Panel.SetZIndex(_orb, 5);
            Tag = canvas;   // shown by the window template in the title bar
            StateChanged += (s, e) =>
            {
                // when maximized the frame pushes content up; keep the orb fully on screen
                double top = WindowState == WindowState.Maximized ? 6 : 4;
                Canvas.SetTop(_orb, top); Canvas.SetTop(QatPanel, top - 1);
            };
        }

        static ControlTemplate OrbTemplate() => ValcoTitleBar.OrbTemplate();

        static readonly (string Key, string Icon, string Label)[] QatOptions =
        {
            ("New", "IconNew", "New"), ("Open", "IconOpen", "Open"), ("Save", "IconSave", "Save"), ("QuickPrint", "IconPrint", "Quick Print"),
            ("PrintPreview", "IconPreview", "Print Preview"), ("Spelling", "IconSpellGrammar", "Spelling & Grammar"), ("Undo", "IconUndo", "Undo"), ("Redo", "IconRedo", "Redo"),
            ("Email", "IconEnvelope", "E-mail")
        };

        List<string> QatItems
        {
            get
            {
                if (!Settings.Current.Custom.TryGetValue("qat", out var v)) v = "Save,Undo,Redo";
                return v.Split(',').Where(x => x.Length > 0).ToList();
            }
            set { Settings.Current.Custom["qat"] = string.Join(",", value); Settings.Save(); }
        }

        void BuildQat()
        {
            QatPanel.Children.Clear();
            foreach (var key in QatItems)
            {
                var opt = QatOptions.FirstOrDefault(o => o.Key == key);
                if (opt.Key == null) continue;
                var b = new Button { Style = RB.BtnStyle, Width = 22, Height = 22, Padding = new Thickness(2), HorizontalContentAlignment = HorizontalAlignment.Center, Content = new Image { Source = UI.Img(opt.Icon), Width = 16, Height = 16 }, ToolTip = opt.Label };
                b.Click += (s, e) => RunQat(key);
                if (key == "Undo") BtnUndo = b;
                if (key == "Redo") BtnRedo = b;
                QatPanel.Children.Add(b);
            }
            BtnUndo ??= new Button(); BtnRedo ??= new Button();
            var more = new Button { Style = RB.BtnStyle, Width = 14, Height = 22, Padding = new Thickness(1), HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = "Customize Quick Access Toolbar",
                Content = new Path { Data = Geometry.Parse("M0,0 H6 M0,3 L6,3 L3,6 Z"), Stroke = new SolidColorBrush(Color.FromRgb(0x2B, 0x3B, 0x50)), Fill = new SolidColorBrush(Color.FromRgb(0x2B, 0x3B, 0x50)) } };
            more.Click += (s, e) =>
            {
                var m = new ContextMenu();
                m.Items.Add(new MenuItem { Header = "Customize Quick Access Toolbar", IsEnabled = false, FontWeight = FontWeights.Bold });
                foreach (var o in QatOptions)
                {
                    var key = o.Key;
                    m.Items.Add(RB.MI(o.Label, () => { var l = QatItems; if (l.Contains(key)) l.Remove(key); else l.Add(key); QatItems = l; foreach (var w in Application.Current.Windows.OfType<MainWindow>()) w.BuildQat(); }, null, null, QatItems.Contains(key)));
                }
                m.Items.Add(new Separator());
                m.Items.Add(RB.MI(Ribbon.Minimized ? "Restore the Ribbon" : "Minimize the Ribbon", () => { Ribbon.ToggleMinimize(); Settings.Current.RibbonMinimized = Ribbon.Minimized; Settings.Save(); }));
                RB.OpenMenu(more, m);
            };
            QatPanel.Children.Add(more);
        }

        void RunQat(string key)
        {
            switch (key)
            {
                case "New": New_Click(null, null); break;
                case "Open": Open_Click(null, null); break;
                case "Save": Save(); break;
                case "QuickPrint": QuickPrint(); break;
                case "PrintPreview": Preview_Click(null, null); break;
                case "Spelling": SpellingDialog_Click(null, null); break;
                case "Undo": Undo_Click(null, null); break;
                case "Redo": Redo_Click(null, null); break;
                case "Email": EmailDocument(); break;
            }
        }

        // =====================================================================================
        //  Office menu
        // =====================================================================================
        void ShowOfficeMenu()
        {
            var popup = new Popup { PlacementTarget = _orb, Placement = PlacementMode.Bottom, VerticalOffset = -18, HorizontalOffset = -4, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
            var right = new ContentControl { Width = 330 };
            Brush rightBg = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFD));

            UIElement RecentList()
            {
                var sp = new StackPanel();
                sp.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xD3, 0xE6)), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(6, 4, 6, 4), Padding = new Thickness(0, 0, 0, 3),
                    Child = new TextBlock { Text = "Recent Documents", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) } });
                var recent = Settings.Current.Pinned.Concat(Settings.Current.Recent.Where(r => !Settings.Current.Pinned.Contains(r))).Where(File.Exists).Take(17).ToList();
                int i = 1;
                foreach (var r in recent)
                {
                    var path = r;
                    var row = new DockPanel();
                    bool pinned = Settings.Current.Pinned.Contains(path);
                    var pin = new Button { Style = RB.BtnStyle, Width = 20, Height = 20, Padding = new Thickness(1), HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = pinned ? "Unpin from the Recent Documents list" : "Pin to the Recent Documents list",
                        Content = new Path { Data = Geometry.Parse(pinned ? "M4,0 H8 V6 L10,8 H7 V12 H5 V8 H2 L4,6 Z" : "M0,4 V8 H6 L8,10 V7 H12 V5 H8 V2 L6,4 Z"), Fill = pinned ? new SolidColorBrush(Color.FromRgb(0x2B, 0x56, 0x97)) : Brushes.Gray } };
                    pin.Click += (s, e) => { var p = Settings.Current.Pinned; if (p.Contains(path)) p.Remove(path); else p.Add(path); Settings.Save(); right.Content = RecentList(); };
                    DockPanel.SetDock(pin, Dock.Right);
                    row.Children.Add(pin);
                    var b = new Button { Style = RB.BtnStyle, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(4, 2, 4, 2), ToolTip = path,
                        Content = new TextBlock { Text = $"{i++}  {System.IO.Path.GetFileName(path)}", TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brushes.Black } };
                    b.Click += (s, e) => { popup.IsOpen = false; OpenFile(path); };
                    row.Children.Add(b);
                    sp.Children.Add(row);
                }
                return sp;
            }

            (string Icon, string Title, string Desc, Action Act) I(string a, string b, string c, Action d) => (a, b, c, d);
            UIElement SubPanel(string title, params (string Icon, string Title, string Desc, Action Act)[] items)
            {
                var sp = new StackPanel();
                sp.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xD3, 0xE6)), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(6, 4, 6, 4), Padding = new Thickness(0, 0, 0, 3),
                    Child = new TextBlock { Text = title, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) } });
                foreach (var it in items)
                {
                    var b = new Button { Style = RB.BtnStyle, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(4), Margin = new Thickness(2, 0, 2, 1) };
                    var g = new DockPanel();
                    var img = new Image { Source = UI.Img(it.Icon), Width = 32, Height = 32, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
                    DockPanel.SetDock(img, Dock.Left);
                    g.Children.Add(img);
                    var t = new StackPanel();
                    t.Children.Add(new TextBlock { Text = it.Title, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
                    t.Children.Add(new TextBlock { Text = it.Desc, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), MaxWidth = 250 });
                    g.Children.Add(t);
                    b.Content = g;
                    var act = it.Act;
                    b.Click += (s, e) => { popup.IsOpen = false; act(); };
                    sp.Children.Add(b);
                }
                return sp;
            }

            var left = new StackPanel { Width = 180, Margin = new Thickness(2) };
            void Item(string icon, string text, Action act, Func<UIElement> sub = null)
            {
                var b = new Button { Style = RB.BtnStyle, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 0, 1) };
                var dp = new DockPanel();
                if (sub != null) { var arr = new Path { Data = Geometry.Parse("M0,0 L4,4 L0,8 Z"), Fill = Brushes.Black, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(arr, Dock.Right); dp.Children.Add(arr); }
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new Image { Source = UI.Img(icon), Width = 32, Height = 32, Margin = new Thickness(0, 0, 8, 0) });
                sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Black });
                dp.Children.Add(sp);
                b.Content = dp;
                if (sub != null)
                {
                    b.MouseEnter += (s, e) => right.Content = sub();
                    b.Click += (s, e) => { if (act != null) { popup.IsOpen = false; act(); } else right.Content = sub(); };
                }
                else
                {
                    b.MouseEnter += (s, e) => right.Content = RecentList();
                    b.Click += (s, e) => { popup.IsOpen = false; act(); };
                }
                left.Children.Add(b);
            }
            void Sep() => left.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xC5, 0xD3, 0xE6)), Margin = new Thickness(40, 2, 2, 2) });

            Item("IconNew", "New", () => NewFromTemplate());
            Item("IconOpen", "Open", () => Open_Click(null, null));
            if (_convertible) Item("IconConvert", "Convert", ConvertDocument);
            Item("IconSave", "Save", () => Save());
            Item("IconSaveAs", "Save As", () => SaveAs(), () => SubPanel("Save a copy of the document",
                I("IconDoc", "Word Document", "Save the document in the default file format.", () => SaveAsFormat(".docx")),
                I("IconNew", "Word Template", "Save the document as a template that can be used to format future documents.", () => SaveAsFormat(".dotx")),
                I("IconPdf", "PDF or XPS", "Publish a copy of the document as a PDF or XPS file.", () => ExportPdf_Click(null, null)),
                I("IconWebLayout", "Web Page", "Save a copy of the document as a web page.", () => ExportHtml_Click(null, null)),
                I("IconDoc", "OpenDocument Text", "Save the document in the OpenDocument format used by LibreOffice and others.", () => SaveAsFormat(".odt")),
                I("IconDraft", "Other Formats", "Open the Save As dialog box to select from all possible file types.", () => SaveAs())));
            Sep();
            Item("IconPrint", "Print", () => Print_Click(null, null), () => SubPanel("Preview and print the document",
                I("IconPrint", "Print", "Select a printer, number of copies and other printing options before printing.", () => Print_Click(null, null)),
                I("IconPrint", "Quick Print", "Send the document directly to the default printer without making changes.", QuickPrint),
                I("IconPreview", "Print Preview", "Preview and make changes to pages before printing.", () => Preview_Click(null, null))));
            Item("IconProperties", "Prepare", null, () => SubPanel("Prepare the document for distribution",
                I("IconProperties", "Properties", "View and edit document properties, such as Title, Author and Keywords.", () => Properties_Click(null, null)),
                I("IconInspect", "Inspect Document", "Check the document for hidden metadata or personal information.", InspectDocument),
                I("IconProtect", "Restrict Editing", "Make the document read-only, with an optional password.", ProtectDocument),
                I("IconMarkFinal", "Mark as Final", "Let readers know the document is final and make it read-only.", MarkAsFinal)));
            Item("IconEnvelope", "Send", null, () => SubPanel("Send a copy of the document to other people",
                I("IconEnvelope", "E-mail", "Send a copy of the document in an e-mail as an attachment.", () => EmailDocument()),
                I("IconPdf", "E-mail as PDF Attachment", "Send a copy of the document in an e-mail as a PDF attachment.", () => EmailDocument(true))));
            Sep();
            Item("IconExit", "Close", () => Close());

            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(4) };
            var opts = RB.MenuButton("Valco Word Options", () => { popup.IsOpen = false; WordOptions(); }, "IconOptions");
            var exit = RB.MenuButton("Exit Valco Word", () => { popup.IsOpen = false; Exit_Click(null, null); }, "IconExit");
            foreach (var b in new[] { opts, exit }) { b.Background = Brushes.White; b.BorderBrush = Brushes.Gray; b.Margin = new Thickness(4, 0, 0, 0); bottom.Children.Add(b); }

            right.Content = RecentList();
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.Children.Add(left);
            var rb = new Border { Background = rightBg, BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xD3, 0xE6)), BorderThickness = new Thickness(1, 0, 0, 0), Child = right, MinHeight = 380 };
            Grid.SetColumn(rb, 1);
            body.Children.Add(rb);
            var dp2 = new DockPanel();
            DockPanel.SetDock(bottom, Dock.Bottom);
            dp2.Children.Add(bottom);
            var bodyBorder = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x9B, 0xB4, 0xD6)), BorderThickness = new Thickness(1), Child = body, Margin = new Thickness(4, 0, 4, 0) };
            dp2.Children.Add(bodyBorder);
            popup.Child = new Border
            {
                CornerRadius = new CornerRadius(4), BorderBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0x8F, 0xBD)), BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(Color.FromRgb(0xDF, 0xEA, 0xF8), Color.FromRgb(0xC2, 0xD5, 0xEE), 90),
                Padding = new Thickness(0, 22, 0, 0), Child = dp2, Effect = new DropShadowEffect { ShadowDepth = 3, Opacity = 0.4, BlurRadius = 8 }, Margin = new Thickness(0, 0, 8, 8)
            };
            popup.IsOpen = true;
        }

        // =====================================================================================
        //  Style gallery, view settings, contextual tabs
        // =====================================================================================
        void BuildStyleUI()
        {
            if (StyleGallery == null) return;
            StyleGallery.Children.Clear();
            foreach (var st in StyleDef.All)
            {
                var id = st.Id;
                var sample = new StackPanel { Width = 64 };
                sample.Children.Add(new TextBlock
                {
                    Text = "AaBbCcDd", FontFamily = st.Family, FontSize = Math.Min(st.SizePx, 15), FontWeight = st.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = st.Italic ? FontStyles.Italic : FontStyles.Normal, Foreground = new SolidColorBrush(st.Color), Height = 18, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center
                });
                sample.Children.Add(new TextBlock { Text = (id == "Normal" ? "¶ " : "") + st.Name, FontSize = 10.5, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                var t = new ToggleButton { Style = RB.TglStyle, Content = sample, Width = 72, Height = 35, Tag = id, Padding = new Thickness(1), HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = st.Name };
                t.Click += (s, e) => { RecordCommand("Style:" + id); ApplyStyle(id); };
                StyleGallery.Children.Add(t);
            }
        }

        void HighlightStyle(string id)
        {
            if (StyleGallery == null) return;
            foreach (var t in StyleGallery.Children.OfType<ToggleButton>()) t.IsChecked = (string)t.Tag == id;
        }

        void StyleGalleryScroll(int dir) => _styleScroll?.ScrollToVerticalOffset(Math.Max(0, _styleScroll.VerticalOffset + dir * 35));

        void ShowAllStyles(FrameworkElement target)
        {
            var tiles = StyleDef.All.Select(st =>
            {
                var id = st.Id;
                var tb = new TextBlock { Text = st.Name, FontFamily = st.Family, FontSize = Math.Min(st.SizePx, 16), FontWeight = st.Bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = st.Italic ? FontStyles.Italic : FontStyles.Normal, Foreground = new SolidColorBrush(st.Color) };
                return (UIElement)RB.Tile(tb, st.Name, () => ApplyStyle(id), 120, 40);
            }).ToList();
            RB.ShowGallery(target, "Quick Styles", tiles, 390, new UIElement[]
            {
                RB.MenuButton("Save Selection as a New Quick Style...", SaveQuickStyle, "IconChangeStyles"),
                RB.MenuButton("Clear Formatting", () => ClearFormat_Click(null, null), "IconClearFormat"),
                RB.MenuButton("Apply Styles...", ShowStylesPane, "IconStyleSet")
            });
        }

        void ApplyViewSettings()
        {
            var s = Settings.Current;
            if (CkRuler != null) CkRuler.IsChecked = s.ShowRuler;
            TheRuler.Visibility = s.ShowRuler && (_view == "print" || _view == "draft" || _view == null) ? Visibility.Visible : Visibility.Collapsed;
            if (TbMarks != null) TbMarks.IsChecked = s.ShowMarks;
            SpellCheck.SetIsEnabled(Editor, s.SpellCheck);
        }

        void UpdateContextualTabs()
        {
            if (Ribbon == null) return;
            bool inTable = Fmt.AncestorAt<TableCell>(Editor.CaretPosition) != null;
            Ribbon.SetContextVisible("Table Tools", inTable);
            Ribbon.SetContextVisible("Picture Tools", _selectedImage != null);
            Ribbon.SetContextVisible("Outline", _view == "outline");
            if (_selectedImage != null && TxtPicW != null && !TxtPicW.IsKeyboardFocusWithin && !TxtPicH.IsKeyboardFocusWithin)
            {
                double w = double.IsNaN(_selectedImage.Width) ? _selectedImage.ActualWidth : _selectedImage.Width;
                double h = double.IsNaN(_selectedImage.Height) ? _selectedImage.ActualHeight : _selectedImage.Height;
                TxtPicW.Tag = "sync"; TxtPicH.Tag = "sync";
                TxtPicW.Text = D.In(w / 96); TxtPicH.Text = D.In(h / 96);
                TxtPicW.Tag = null; TxtPicH.Tag = null;
            }
            if (inTable && TxtCellW != null && !TxtCellW.IsKeyboardFocusWithin)
            {
                var cur = CurrentCell();
                if (cur != null && cur.Value.Col < cur.Value.T.Columns.Count && cur.Value.T.Columns[cur.Value.Col].Width.IsAbsolute)
                { TxtCellW.Tag = "sync"; TxtCellW.Text = D.In(cur.Value.T.Columns[cur.Value.Col].Width.Value / 96); TxtCellW.Tag = null; }
            }
            var p = Editor.CaretPosition.Paragraph;
            if (p != null && TxtIndentL != null)
            {
                foreach (var tb in new[] { TxtIndentL, TxtIndentR, TxtSpaceBefore, TxtSpaceAfter }) tb.Tag = "sync";
                if (!TxtIndentL.IsKeyboardFocusWithin) TxtIndentL.Text = D.In(Safe(p.Margin.Left) / 96);
                if (!TxtIndentR.IsKeyboardFocusWithin) TxtIndentR.Text = D.In(Safe(p.Margin.Right) / 96);
                if (!TxtSpaceBefore.IsKeyboardFocusWithin) TxtSpaceBefore.Text = D.Pt(Safe(p.Margin.Top) * 0.75);
                if (!TxtSpaceAfter.IsKeyboardFocusWithin) TxtSpaceAfter.Text = D.Pt(Safe(p.Margin.Bottom) * 0.75);
                foreach (var tb in new[] { TxtIndentL, TxtIndentR, TxtSpaceBefore, TxtSpaceAfter }) tb.Tag = null;
            }
        }

        void SetIndentFromRibbon(double? leftIn, double? rightIn)
        {
            Editor.BeginChange();
            foreach (var p in Fmt.SelectedParagraphs(Editor))
                p.Margin = new Thickness(leftIn.HasValue ? Math.Max(0, leftIn.Value * 96) : Safe(p.Margin.Left), Safe(p.Margin.Top), rightIn.HasValue ? Math.Max(0, rightIn.Value * 96) : Safe(p.Margin.Right), Safe(p.Margin.Bottom));
            Editor.EndChange();
            MarkDirty(); UpdateRuler(); ScheduleLayout();
        }

        void SetSpacingFromRibbon(double? beforePt, double? afterPt)
        {
            Editor.BeginChange();
            foreach (var p in Fmt.SelectedParagraphs(Editor))
                p.Margin = new Thickness(Safe(p.Margin.Left), beforePt.HasValue ? Math.Max(0, beforePt.Value) * Fmt.PxPerPt : Safe(p.Margin.Top), Safe(p.Margin.Right), afterPt.HasValue ? Math.Max(0, afterPt.Value) * Fmt.PxPerPt : Safe(p.Margin.Bottom));
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        // =====================================================================================
        //  Mini toolbar (appears near a mouse selection)
        // =====================================================================================
        Popup _mini;
        void ShowMiniToolbar()
        {
            if (!Settings.Current.MiniToolbar || Editor.Selection.IsEmpty || _view == "full") return;
            _mini?.SetValue(Popup.IsOpenProperty, false);
            var font = RB.Combo(110, true);
            font.ItemsSource = CbFont.ItemsSource; font.ItemTemplate = CbFont.ItemTemplate; font.Text = CbFont.Text;
            font.SelectionChanged += (s, e) => { if (font.IsDropDownOpen && font.SelectedItem is string f) ApplyChar(TextElement.FontFamilyProperty, new FontFamily(f)); };
            var size = RB.Combo(40, true);
            size.ItemsSource = CbSize.ItemsSource; size.Text = CbSize.Text;
            size.SelectionChanged += (s, e) => { if (size.IsDropDownOpen && size.SelectedItem is string v && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var pt)) ApplyChar(TextElement.FontSizeProperty, pt * Fmt.PxPerPt); };
            Button B(string icon, Action a, string tip) => RB.Small(icon, null, a, tip);
            Button T(UIElement g, Action a, string tip) { var b = new Button { Style = RB.BtnStyle, Height = 22, MinWidth = 22, HorizontalContentAlignment = HorizontalAlignment.Center, Content = g, ToolTip = tip }; b.Click += (s, e) => a(); return b; }
            var row1 = RB.Row(font, size, B("IconGrow", () => Grow_Click(null, null), "Grow Font"), B("IconShrink", () => Shrink_Click(null, null), "Shrink Font"), B("IconFormatPainter", () => StartFormatPainter(false), "Format Painter"));
            var row2 = RB.Row(T(G("B", FontWeights.Bold), () => Bold_Click(null, null), "Bold"), T(G("I", null, FontStyles.Italic), () => Italic_Click(null, null), "Italic"),
                B("IconAlignCenter", () => AlignCenter_Click(null, null), "Center"), B("IconHighlight", () => Highlight_Click(null, null), "Text Highlight Color"),
                B("IconFontColor", () => FontColor_Click(null, null), "Font Color"), B("IconOutdent", () => Outdent_Click(null, null), "Decrease Indent"),
                B("IconIndent", () => Indent_Click(null, null), "Increase Indent"), B("IconBullets", () => Bullets_Click(null, null), "Bullets"));
            var sp = new StackPanel(); sp.Children.Add(row1); sp.Children.Add(row2);
            var border = new Border
            {
                Child = sp, Padding = new Thickness(3), CornerRadius = new CornerRadius(3), BorderBrush = new SolidColorBrush(Color.FromRgb(0x9B, 0xB4, 0xD6)), BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(Color.FromRgb(0xF8, 0xFB, 0xFF), Color.FromRgb(0xDB, 0xE7, 0xF6), 90), Effect = new DropShadowEffect { ShadowDepth = 2, Opacity = 0.3, BlurRadius = 4 },
                Opacity = 0.35, Margin = new Thickness(0, 0, 4, 4)
            };
            border.MouseEnter += (s, e) => border.Opacity = 1;
            var mouse = Mouse.GetPosition(Editor);
            _mini = new Popup { PlacementTarget = Editor, Placement = PlacementMode.Relative, HorizontalOffset = mouse.X * Zoom + 4, VerticalOffset = mouse.Y * Zoom - 70, AllowsTransparency = true, StaysOpen = true, Child = border };
            Editor.PreviewMouseMove += MiniMouseMove;
            _mini.Closed += (s, e) => Editor.PreviewMouseMove -= MiniMouseMove;
            _mini.IsOpen = true;
            _miniOrigin = mouse;
        }

        Point _miniOrigin;
        void MiniMouseMove(object sender, MouseEventArgs e)
        {
            if (_mini == null || !_mini.IsOpen) return;
            var p = e.GetPosition(Editor);
            double d = (p - _miniOrigin).Length;
            if (_mini.Child is Border b && !b.IsMouseOver) b.Opacity = Math.Max(0, 1 - d / 140) * 0.95;
            if (d > 160) _mini.IsOpen = false;
        }

        void HideMiniToolbar() { if (_mini != null) _mini.IsOpen = false; }
    }
}
