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
using System.Windows.Media.Imaging;

namespace ValcoWord
{
    public partial class MainWindow
    {
        // =====================================================================================
        //  Change case / shading / borders
        // =====================================================================================
        void ChangeCaseTo(string mode) => ChangeCase_Click(new FrameworkElement { Tag = mode }, null);

        void ApplyShading(Color? c)
        {
            Editor.BeginChange();
            var cell = Fmt.AncestorAt<TableCell>(Editor.CaretPosition);
            if (cell != null && Fmt.SelectedParagraphs(Editor).All(p => Fmt.Ancestor<TableCell>(p) != null))
                foreach (var tc in SelectedCells()) { if (c.HasValue) tc.Background = new SolidColorBrush(c.Value); else tc.ClearValue(TableCell.BackgroundProperty); }
            else if (!Editor.Selection.IsEmpty && Editor.Selection.Start.Paragraph == Editor.Selection.End.Paragraph)
                Editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent);
            else
                foreach (var p in Fmt.SelectedParagraphs(Editor)) { if (c.HasValue) p.Background = new SolidColorBrush(c.Value); else p.ClearValue(TextElement.BackgroundProperty); }
            Editor.EndChange();
            MarkDirty();
        }

        ContextMenu BordersMenu()
        {
            void Set(Func<Thickness, Thickness> f)
            {
                Editor.BeginChange();
                foreach (var p in Fmt.SelectedParagraphs(Editor))
                {
                    var t = f(p.BorderThickness);
                    p.BorderThickness = t;
                    p.BorderBrush = Brushes.Black;
                    p.Padding = new Thickness(t.Left > 0 ? 4 : 0, t.Top > 0 ? 1 : 0, t.Right > 0 ? 4 : 0, t.Bottom > 0 ? 1 : 0);
                    if (t == new Thickness(0)) { p.ClearValue(Block.BorderBrushProperty); p.ClearValue(Block.PaddingProperty); }
                }
                Editor.EndChange();
                MarkDirty(); ScheduleLayout();
            }
            const double w = 0.75;
            return RB.Menu(
                RB.MI("Bottom Border", () => Set(t => new Thickness(t.Left, t.Top, t.Right, w)), "IconBorders"),
                RB.MI("Top Border", () => Set(t => new Thickness(t.Left, w, t.Right, t.Bottom))),
                RB.MI("Left Border", () => Set(t => new Thickness(w, t.Top, t.Right, t.Bottom))),
                RB.MI("Right Border", () => Set(t => new Thickness(t.Left, t.Top, w, t.Bottom))),
                null,
                RB.MI("No Border", () => Set(t => new Thickness(0))),
                RB.MI("Outside Borders", () => Set(t => new Thickness(w))),
                null,
                RB.MI("Horizontal Line", () => InsertHLine_Click(null, null), "IconHLine"),
                RB.MI("Borders and Shading...", BordersAndShadingDialog));
        }

        void BordersAndShadingDialog()
        {
            var d = new DialogBase(this, "Borders and Shading", 380);
            var style = new ComboBox { Width = 160, ItemsSource = new[] { "None", "Box", "Bottom only", "Top and bottom" }, SelectedIndex = 1 };
            var width = new ComboBox { Width = 160, ItemsSource = new[] { "½ pt", "1 pt", "1½ pt", "2¼ pt", "3 pt" }, SelectedIndex = 1 };
            Color border = Colors.Black, fill = Colors.Transparent;
            var bBtn = UI.Btn("Border color...", null); bBtn.Click += (s, e) => { var c = ColorDialog.Pick(d, border); if (c.HasValue) border = c.Value; };
            var fBtn = UI.Btn("Shading...", null); fBtn.Click += (s, e) => { var c = ColorDialog.Pick(d, Colors.White); if (c.HasValue) fill = c.Value; };
            var sp = new StackPanel();
            sp.Children.Add(D.Group("Borders", D.Form(("Setting:", style), ("Width:", width), (null, bBtn))));
            sp.Children.Add(D.Group("Shading", fBtn));
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            double w = new[] { 0.67, 1.33, 2, 3, 4 }[width.SelectedIndex];
            Editor.BeginChange();
            foreach (var p in Fmt.SelectedParagraphs(Editor))
            {
                var t = style.SelectedIndex switch { 0 => new Thickness(0), 2 => new Thickness(0, 0, 0, w), 3 => new Thickness(0, w, 0, w), _ => new Thickness(w) };
                p.BorderThickness = t; p.BorderBrush = new SolidColorBrush(border);
                p.Padding = new Thickness(t.Left > 0 ? 4 : 0, t.Top > 0 ? 2 : 0, t.Right > 0 ? 4 : 0, t.Bottom > 0 ? 2 : 0);
                if (fill.A > 0) p.Background = new SolidColorBrush(fill);
            }
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        // =====================================================================================
        //  Lists
        // =====================================================================================
        ContextMenu BulletLibraryMenu() => RB.Menu(
            RB.MI("● Filled circle", () => SetListStyle("Disc")), RB.MI("○ Hollow circle", () => SetListStyle("Circle")),
            RB.MI("■ Square", () => SetListStyle("Square")), RB.MI("□ Box", () => SetListStyle("Box")), null,
            RB.MI("None", () => SetListStyle("None")));

        ContextMenu NumberingLibraryMenu() => RB.Menu(
            RB.MI("1.  2.  3.", () => SetListStyle("Decimal")), RB.MI("a.  b.  c.", () => SetListStyle("LowerLatin")),
            RB.MI("A.  B.  C.", () => SetListStyle("UpperLatin")), RB.MI("i.  ii.  iii.", () => SetListStyle("LowerRoman")),
            RB.MI("I.  II.  III.", () => SetListStyle("UpperRoman")), null,
            RB.MI("Set Numbering Value...", SetNumberingValue), RB.MI("None", () => SetListStyle("None")));

        ContextMenu MultilevelMenu() => RB.Menu(
            RB.MI("1.  a.  i.", () => Multilevel(new[] { TextMarkerStyle.Decimal, TextMarkerStyle.LowerLatin, TextMarkerStyle.LowerRoman })),
            RB.MI("I.  A.  1.", () => Multilevel(new[] { TextMarkerStyle.UpperRoman, TextMarkerStyle.UpperLatin, TextMarkerStyle.Decimal })),
            RB.MI("●  ○  ■", () => Multilevel(new[] { TextMarkerStyle.Disc, TextMarkerStyle.Circle, TextMarkerStyle.Square })),
            RB.MI("1.  1.  1.", () => Multilevel(new[] { TextMarkerStyle.Decimal, TextMarkerStyle.Decimal, TextMarkerStyle.Decimal })), null,
            RB.MI("Change List Level ▸ use Increase / Decrease Indent", null));

        void SetListStyle(string tag) => ListStyle_Click(new FrameworkElement { Tag = tag }, null);

        void Multilevel(TextMarkerStyle[] levels)
        {
            var list = Fmt.AncestorAt<List>(Editor.CaretPosition);
            if (list == null) { EditingCommands.ToggleNumbering.Execute(null, Editor); list = Fmt.AncestorAt<List>(Editor.CaretPosition); }
            if (list == null) return;
            var top = list;
            while (Fmt.Ancestor<List>(top.Parent as TextElement) is List up) top = up;
            void Walk(List l, int depth)
            {
                l.MarkerStyle = levels[depth % levels.Length];
                foreach (var li in l.ListItems) foreach (var nested in li.Blocks.OfType<List>()) Walk(nested, depth + 1);
            }
            Walk(top, 0);
            AfterList();
        }

        void SetNumberingValue()
        {
            var list = Fmt.AncestorAt<List>(Editor.CaretPosition);
            if (list == null || Fmt.IsBullet(list.MarkerStyle)) { Flash("Put the cursor in a numbered list first."); return; }
            var ans = InputDialog.Ask(this, "Set Numbering Value", "Start new list at:", list.StartIndex.ToString());
            if (int.TryParse(ans, out var n) && n > 0) { list.StartIndex = n; MarkDirty(); }
        }

        // =====================================================================================
        //  Sort paragraphs
        // =====================================================================================
        void SortParagraphs()
        {
            var paras = Fmt.SelectedParagraphs(Editor).Where(p => p.Parent is FlowDocument || p.Parent is Section || p.Parent is ListItem || p.Parent is TableCell).ToList();
            if (paras.Count < 2) { Flash("Select two or more paragraphs to sort."); return; }
            var d = new DialogBase(this, "Sort Text", 330);
            var asc = new RadioButton { Content = "_Ascending (A to Z)", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
            var desc = new RadioButton { Content = "_Descending (Z to A)", Margin = new Thickness(0, 2, 0, 2) };
            var type = new ComboBox { Width = 120, ItemsSource = new[] { "Text", "Number", "Date" }, SelectedIndex = 0 };
            var sp = new StackPanel();
            sp.Children.Add(D.Form(("Sort by Paragraphs, type:", type)));
            sp.Children.Add(asc); sp.Children.Add(desc);
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            // only sort paragraphs that share a parent (lines in the same list, cell or body)
            var groups = paras.GroupBy(p => p.SiblingBlocks).ToList();
            Editor.BeginChange();
            foreach (var g in groups)
            {
                var items = g.ToList();
                if (items.Count < 2) continue;
                Func<Paragraph, IComparable> key = type.SelectedIndex switch
                {
                    1 => p => double.TryParse(new string(Spans.Text(p).Trim().TakeWhile(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray()), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.MaxValue,
                    2 => p => DateTime.TryParse(Spans.Text(p).Trim(), out var dt) ? dt : DateTime.MaxValue,
                    _ => p => Spans.Text(p).Trim().ToLowerInvariant()
                };
                var sorted = desc.IsChecked == true ? items.OrderByDescending(key).ToList() : items.OrderBy(key).ToList();
                var blocks = g.Key;
                var anchor = items[0].PreviousBlock;
                var parentList = items[0].Parent is ListItem;
                if (parentList) continue;
                foreach (var p in items) blocks.Remove(p);
                Block last = anchor;
                foreach (var p in sorted)
                {
                    if (last == null) { if (blocks.FirstBlock == null) blocks.Add(p); else blocks.InsertBefore(blocks.FirstBlock, p); }
                    else blocks.InsertAfter(last, p);
                    last = p;
                }
            }
            // list items: sort ListItems of the containing list
            var lists = paras.Select(p => p.Parent as ListItem).Where(li => li != null).Select(li => li.List).Distinct().ToList();
            foreach (var l in lists)
            {
                var items = l.ListItems.ToList();
                var sorted = (desc.IsChecked == true ? items.OrderByDescending(li => new TextRange(li.ContentStart, li.ContentEnd).Text.Trim()) : items.OrderBy(li => new TextRange(li.ContentStart, li.ContentEnd).Text.Trim())).ToList();
                foreach (var li in items) l.ListItems.Remove(li);
                foreach (var li in sorted) l.ListItems.Add(li);
            }
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        // =====================================================================================
        //  Format Painter
        // =====================================================================================
        Dictionary<DependencyProperty, object> _painterChar, _painterPara;
        bool _painterSticky;

        void StartFormatPainter(bool sticky)
        {
            var sel = Editor.Selection;
            var at = sel.IsEmpty ? new TextRange(Editor.CaretPosition, Editor.CaretPosition) : sel;
            _painterChar = new Dictionary<DependencyProperty, object>();
            foreach (var dp in new[] { TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.FontWeightProperty, TextElement.FontStyleProperty, TextElement.ForegroundProperty, Inline.TextDecorationsProperty, Inline.BaselineAlignmentProperty, TextElement.BackgroundProperty })
            {
                var v = at.GetPropertyValue(dp);
                if (v != DependencyProperty.UnsetValue) _painterChar[dp] = v;
            }
            var p = Editor.CaretPosition.Paragraph;
            _painterPara = new Dictionary<DependencyProperty, object>();
            if (p != null)
            {
                _painterPara[Block.TextAlignmentProperty] = p.TextAlignment;
                _painterPara[Block.MarginProperty] = p.Margin;
                _painterPara[Paragraph.TextIndentProperty] = p.TextIndent;
                _painterPara[Block.LineHeightProperty] = p.LineHeight;
                _painterPara[FrameworkContentElement.TagProperty] = p.Tag;
            }
            _painterSticky = sticky;
            Editor.Cursor = Cursors.Pen;
            Flash(sticky ? "Format Painter is on. Select text to paint; press Esc to stop." : "Select the text to apply the formatting to.");
        }

        bool ApplyFormatPainter()
        {
            if (_painterChar == null) return false;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_painterChar == null) return;
                Editor.BeginChange();
                var range = Editor.Selection.IsEmpty ? TargetRange() : Editor.Selection;
                foreach (var kv in _painterChar) try { range.ApplyPropertyValue(kv.Key, kv.Value); } catch { }
                bool wholeParas = Editor.Selection.IsEmpty || Editor.Selection.Text.Contains("\n");
                if (wholeParas)
                    foreach (var p in Fmt.SelectedParagraphs(Editor))
                        foreach (var kv in _painterPara) try { p.SetValue(kv.Key, kv.Value); } catch { }
                Editor.EndChange();
                MarkDirty();
                if (!_painterSticky) StopFormatPainter();
            }), System.Windows.Threading.DispatcherPriority.Input);
            return true;
        }

        void StopFormatPainter()
        {
            _painterChar = null; _painterPara = null;
            Editor.ClearValue(CursorProperty);
        }

        // =====================================================================================
        //  Clipboard pane, Paste Special
        // =====================================================================================
        static readonly List<string> ClipHistory = new List<string>();

        void RememberClipboard()
        {
            try
            {
                var t = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(t)) return;
                ClipHistory.Remove(t);
                ClipHistory.Insert(0, t);
                while (ClipHistory.Count > 24) ClipHistory.RemoveAt(ClipHistory.Count - 1);
                if (_rightPaneKind == "clipboard") ShowClipboardPane();
            }
            catch { }
        }

        void ShowClipboardPane()
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = ClipHistory.Count == 0 ? "Items you cut or copy will appear here." : "Click an item to paste:", Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray });
            foreach (var item in ClipHistory)
            {
                var text = item;
                var b = new Button { Style = RB.BtnStyle, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 1, 4, 1), Padding = new Thickness(4),
                    Content = new TextBlock { Text = text.Length > 160 ? text.Substring(0, 160) + "…" : text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black, MaxHeight = 64 } };
                b.Click += (s, e) => { Editor.Selection.Text = text; Editor.Selection.Select(Editor.Selection.End, Editor.Selection.End); MarkDirty(); Editor.Focus(); };
                sp.Children.Add(b);
            }
            var clear = RB.MenuButton("Clear All", () => { ClipHistory.Clear(); ShowClipboardPane(); }, "IconDelete");
            ShowRightPane("clipboard", "Clipboard", sp, clear);
        }

        void PasteSpecial()
        {
            var d = new DialogBase(this, "Paste Special", 340);
            var list = new ListBox { Height = 110 };
            var opts = new List<(string, Action)>();
            if (Clipboard.ContainsData(DataFormats.Rtf)) opts.Add(("Formatted Text (RTF)", () => Editor.Paste()));
            if (Clipboard.ContainsText()) opts.Add(("Unformatted Text", () => PastePlain_Click(null, null)));
            if (Clipboard.ContainsImage()) opts.Add(("Picture (PNG)", () => InsertImage(Clipboard.GetImage(), null)));
            if (Clipboard.ContainsText(TextDataFormat.Html)) opts.Add(("HTML Format (as text)", () => PastePlain_Click(null, null)));
            if (opts.Count == 0) { Flash("The Clipboard is empty."); return; }
            foreach (var o in opts) list.Items.Add(o.Item1);
            list.SelectedIndex = 0;
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "_As:", Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(list);
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            list.MouseDoubleClick += (s, e) => d.DialogResult = true;
            if (d.ShowDialog() == true && list.SelectedIndex >= 0) { opts[list.SelectedIndex].Item2(); MarkDirty(); }
        }

        void PasteAsHyperlink()
        {
            string t;
            try { t = Clipboard.GetText()?.Trim(); } catch { return; }
            if (string.IsNullOrEmpty(t)) return;
            if (!Uri.TryCreate(t.Contains("://") ? t : "https://" + t, UriKind.Absolute, out var uri)) { Flash("The Clipboard doesn't contain a web address."); return; }
            Editor.BeginChange();
            if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
            var h = new Hyperlink(new Run(t), Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward)) { NavigateUri = uri };
            Editor.EndChange();
            Editor.CaretPosition = h.ElementEnd;
            MarkDirty();
        }

        // =====================================================================================
        //  Select
        // =====================================================================================
        void SelectParagraph()
        {
            var p = Editor.CaretPosition.Paragraph;
            if (p != null) Editor.Selection.Select(p.ContentStart, p.ContentEnd);
            Editor.Focus();
        }

        List<TextRange> _similar;

        /// <summary>Finds every run formatted like the selection; the next formatting command applies to all of them.</summary>
        void SelectSimilarFormatting()
        {
            var at = Editor.Selection.IsEmpty ? TargetRange() : Editor.Selection;
            var fam = at.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
            var size = at.GetPropertyValue(TextElement.FontSizeProperty);
            var weight = at.GetPropertyValue(TextElement.FontWeightProperty);
            var style = at.GetPropertyValue(TextElement.FontStyleProperty);
            var fg = (at.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush)?.Color;
            _similar = new List<TextRange>();
            foreach (var r in Fmt.AllParagraphs(Editor.Document.Blocks).SelectMany(Fmt.Descendants).OfType<Run>())
            {
                if (fam != null && r.FontFamily.Source != fam.Source) continue;
                if (size is double sz && Math.Abs(r.FontSize - sz) > 0.1) continue;
                if (weight is FontWeight w && r.FontWeight != w) continue;
                if (style is FontStyle st && r.FontStyle != st) continue;
                if (fg.HasValue && !(r.Foreground is SolidColorBrush b && b.Color == fg.Value)) continue;
                _similar.Add(new TextRange(r.ContentStart, r.ContentEnd));
            }
            foreach (var r in _similar) r.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x60, 0x33, 0x99, 0xFF)));
            Flash($"{_similar.Count} matching pieces of text are selected. Your next font or style change applies to all of them.");
        }

        bool ApplyToSimilar(DependencyProperty dp, object value)
        {
            if (_similar == null) return false;
            Editor.BeginChange();
            foreach (var r in _similar) { try { r.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent); r.ApplyPropertyValue(dp, value); } catch { } }
            Editor.EndChange();
            _similar = null;
            MarkDirty();
            return true;
        }

        void ClearSimilar()
        {
            if (_similar == null) return;
            foreach (var r in _similar) try { r.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent); } catch { }
            _similar = null;
        }

        // =====================================================================================
        //  Styles: style sets, themes, quick styles, styles pane
        // =====================================================================================
        ContextMenu ChangeStylesMenu()
        {
            var sets = new MenuItem { Header = "Style Set", Icon = UI.Icon("IconStyleSet", 16) };
            foreach (var s in StyleDef.StyleSets) { var name = s; sets.Items.Add(RB.MI(name, () => ApplyThemeAndSet(null, name), null, null, _ex.StyleSet == name)); }
            sets.Items.Add(new Separator());
            sets.Items.Add(RB.MI("Reset Document Quick Styles", () => ApplyThemeAndSet(null, "Word 2007")));
            var colors = ThemePartMenu("colors"); var colorsMi = new MenuItem { Header = "Colors", Icon = UI.Icon("IconPageColor", 16) };
            foreach (var i in colors.Items.Cast<object>().ToList()) { colors.Items.Remove(i); colorsMi.Items.Add(i); }
            var fonts = ThemePartMenu("fonts"); var fontsMi = new MenuItem { Header = "Fonts", Icon = UI.Icon("IconFont", 16) };
            foreach (var i in fonts.Items.Cast<object>().ToList()) { fonts.Items.Remove(i); fontsMi.Items.Add(i); }
            return RB.Menu(sets, colorsMi, fontsMi, null, RB.MI("Set as Default", () =>
            {
                Settings.Current.Custom["theme"] = _ex.Theme; Settings.Current.Custom["styleSet"] = _ex.StyleSet; Settings.Save();
                Flash("New documents will use this theme and style set.");
            }));
        }

        ContextMenu ThemePartMenu(string part)
        {
            var m = new ContextMenu();
            foreach (var t in StyleDef.Themes)
            {
                var th = t;
                var mi = new MenuItem { Header = part == "fonts" ? $"{th.Name}   ({th.Major} / {th.Minor})" : th.Name };
                if (part == "colors")
                {
                    var sw = new StackPanel { Orientation = Orientation.Horizontal };
                    foreach (var c in new[] { th.Dark2, th.Accent1, th.Accent2, th.Accent3, th.Accent4, th.Accent5, th.Accent6 })
                        sw.Children.Add(new Border { Width = 10, Height = 12, Background = new SolidColorBrush(c), Margin = new Thickness(0, 0, 1, 0) });
                    mi.Icon = sw;
                }
                mi.Click += (s, e) => ApplyThemeAndSet(th.Name, null);
                m.Items.Add(mi);
            }
            return m;
        }

        void ShowThemesGallery()
        {
            var tiles = StyleDef.Themes.Select(th =>
            {
                var name = th.Name;
                var sp = new StackPanel { Width = 74 };
                sp.Children.Add(new TextBlock { Text = "Aa", FontFamily = new FontFamily(th.Major), FontSize = 20, Foreground = new SolidColorBrush(th.Dark2), HorizontalAlignment = HorizontalAlignment.Center });
                var bars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                foreach (var c in new[] { th.Accent1, th.Accent2, th.Accent3, th.Accent4, th.Accent5, th.Accent6 }) bars.Children.Add(new Border { Width = 9, Height = 9, Background = new SolidColorBrush(c), Margin = new Thickness(0.5) });
                sp.Children.Add(bars);
                sp.Children.Add(new TextBlock { Text = name, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Black });
                return (UIElement)RB.Tile(sp, name, () => ApplyThemeAndSet(name, null), 82, 70);
            }).ToList();
            RB.ShowGallery(Ribbon, "Built-In Themes", tiles, 440, new UIElement[] { RB.MenuButton("Reset to Theme from Template", () => ApplyThemeAndSet("Office", null), "IconRefresh") });
        }

        /// <summary>Switches theme and/or style set and restyles the document's paragraphs.</summary>
        void ApplyThemeAndSet(string theme, string set)
        {
            if (theme != null) _ex.Theme = theme;
            if (set != null) _ex.StyleSet = set;
            var oldDefs = StyleDef.All.ToDictionary(s => s.Id, s => (s.Font, s.SizePt, s.Color));
            StyleDef.Init(_ex.Theme, _ex.StyleSet);
            Editor.BeginChange();
            foreach (var p in Fmt.AllParagraphs(Editor.Document.Blocks))
            {
                var id = p.Tag as string ?? "Normal";
                if (!StyleDef.All.Any(s => s.Id == id)) continue;
                var def = StyleDef.Get(id);
                bool wasDefaultFont = oldDefs.TryGetValue(id, out var old) && p.FontFamily.Source == old.Font;
                if (p.Tag is string) { var align = p.TextAlignment; def.Apply(p, false); if (id == "Normal" || id == "NoSpacing") p.TextAlignment = align; }
                else if (wasDefaultFont) { p.FontFamily = def.Family; }
            }
            Editor.EndChange();
            BuildStyleUI();
            UpdateToolbar();
            MarkDirty(); ScheduleLayout();
        }

        void SaveQuickStyle()
        {
            var p = Editor.CaretPosition.Paragraph;
            if (p == null) return;
            var name = InputDialog.Ask(this, "Create New Style from Formatting", "Name:", "Style" + (StyleDef.Custom.Count + 1));
            if (string.IsNullOrWhiteSpace(name)) return;
            var sel = Editor.Selection;
            var def = new StyleDef
            {
                Id = "Custom" + new string(name.Where(char.IsLetterOrDigit).ToArray()), Name = name, Font = p.FontFamily.Source, SizePt = Math.Round(p.FontSize * 0.75 * 2) / 2,
                Bold = sel.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w.ToOpenTypeWeight() >= 600,
                Italic = sel.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle st && st != FontStyles.Normal,
                Color = (sel.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush)?.Color ?? Colors.Black,
                BeforePt = Safe(p.Margin.Top) * 0.75, AfterPt = Safe(p.Margin.Bottom) * 0.75, Line = Fmt.GetLineSpacing(p), IndentIn = Safe(p.Margin.Left) / 96, Align = p.TextAlignment
            };
            if (sel.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily ff) def.Font = ff.Source;
            if (sel.GetPropertyValue(TextElement.FontSizeProperty) is double sz) def.SizePt = Math.Round(sz * 0.75 * 2) / 2;
            StyleDef.Custom.RemoveAll(c => c.Id == def.Id);
            StyleDef.Custom.Add(def);
            StyleDef.Init();
            BuildStyleUI();
            def.Apply(p, false);
            HighlightStyle(def.Id);
        }

        void ShowStylesPane()
        {
            var sp = new StackPanel();
            foreach (var st in StyleDef.All)
            {
                var id = st.Id;
                var b = new Button { Style = RB.BtnStyle, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(4, 1, 4, 1), Padding = new Thickness(4, 3, 4, 3) };
                var dp = new DockPanel();
                var mark = new TextBlock { Text = id.StartsWith("Heading") || id == "Title" ? "¶a" : "¶", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(mark, Dock.Right);
                dp.Children.Add(mark);
                dp.Children.Add(new TextBlock { Text = st.Name, FontFamily = st.Family, FontSize = Math.Min(st.SizePx, 16), FontWeight = st.Bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = st.Italic ? FontStyles.Italic : FontStyles.Normal, Foreground = new SolidColorBrush(st.Color) });
                b.Content = dp;
                b.Click += (s, e) => ApplyStyle(id);
                sp.Children.Add(b);
            }
            ShowRightPane("styles", "Styles", sp, RB.MenuButton("New Style from Selection...", SaveQuickStyle, "IconChangeStyles"));
        }

        // =====================================================================================
        //  Side panes
        // =====================================================================================
        string _rightPaneKind, _leftPaneKind;

        void ShowRightPane(string kind, string title, UIElement content, UIElement footer = null, double width = 260)
        {
            _rightPaneKind = kind;
            var header = new DockPanel { Background = (Brush)FindResource("PaneHeaderBrush") };
            var close = new Button { Style = RB.BtnStyle, Width = 20, Height = 20, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, Content = new TextBlock { Text = "✕", Foreground = Brushes.DimGray }, ToolTip = "Close" };
            close.Click += (s, e) => HideRightPane();
            DockPanel.SetDock(close, Dock.Right);
            header.Children.Add(close);
            header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 4, 4, 4), Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) });
            var dp = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            dp.Children.Add(header);
            if (footer != null) { var f = new Border { Child = footer, Padding = new Thickness(4), BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xD3, 0xE6)), BorderThickness = new Thickness(0, 1, 0, 0) }; DockPanel.SetDock(f, Dock.Bottom); dp.Children.Add(f); }
            dp.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            RightPane.Child = dp;
            RightPane.Visibility = Visibility.Visible;
            RightSplitter.Visibility = Visibility.Visible;
            if (RightCol.Width.Value < 120) RightCol.Width = new GridLength(width);
        }

        void HideRightPane()
        {
            _rightPaneKind = null;
            RightPane.Child = null;
            RightPane.Visibility = Visibility.Collapsed;
            RightSplitter.Visibility = Visibility.Collapsed;
            RightCol.Width = new GridLength(0);
        }

        void ShowLeftPaneContent(string kind, string title, UIElement content)
        {
            _leftPaneKind = kind;
            var header = new DockPanel { Background = (Brush)FindResource("PaneHeaderBrush") };
            var close = new Button { Style = RB.BtnStyle, Width = 20, Height = 20, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, Content = new TextBlock { Text = "✕", Foreground = Brushes.DimGray } };
            close.Click += (s, e) => ShowLeftPane(null);
            DockPanel.SetDock(close, Dock.Right);
            header.Children.Add(close);
            header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 4, 4, 4), Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) });
            var dp = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            dp.Children.Add(header);
            dp.Children.Add(content);
            LeftPane.Child = dp;
            LeftPane.Visibility = Visibility.Visible;
            LeftSplitter.Visibility = Visibility.Visible;
            if (LeftCol.Width.Value < 120) LeftCol.Width = new GridLength(220);
        }
    }
}
