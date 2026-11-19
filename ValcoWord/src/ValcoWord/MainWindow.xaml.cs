using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ValcoWord
{
    public partial class MainWindow : VistaWindow
    {
        public static readonly double[] Sizes = { 8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72 };
        static int _docCounter;

        string _path, _docTitle, _author, _displayName;
        PageSettings _page;
        bool _dirty, _loading, _updatingUI, _fullScreen;
        int _pageCount = 1, _currentPage = 1;
        readonly List<(double Y, bool Manual)> _boundaries = new List<(double, bool)>();
        readonly DispatcherTimer _layoutTimer, _autoTimer;
        string _recoverSlot;
        FileStream _recoverLock;
        Color _fontColor = Color.FromRgb(0xC0, 0x00, 0x00), _highlight = Color.FromRgb(255, 255, 0);
        FindReplaceDialog _findDlg;
        string _lastQuery; bool _lastCase, _lastWhole;
        ImageResizeAdorner _imgAdorner;
        Image _selectedImage;
        string _statusNote;

        public MainWindow()
        {
            InitializeComponent();
            var s = Settings.Current;
            Width = Math.Max(MinWidth, s.WinWidth);
            Height = Math.Max(MinHeight, s.WinHeight);
            if (s.WinMax) WindowState = WindowState.Maximized;

            BuildRibbon();
            _ex = new DocExtras();

            _layoutTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _layoutTimer.Tick += (o, e) => { _layoutTimer.Stop(); UpdatePages(); UpdateCounts(); };
            _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(Math.Max(1, s.AutoRecoverMinutes)) };
            _autoTimer.Tick += (o, e) => SaveRecovery();
            _autoTimer.Start();

            Editor.TextChanged += Editor_TextChanged;
            Editor.SelectionChanged += (o, e) => { UpdateToolbar(); UpdateRuler(); CheckImageSelection(); UpdatePageIndicator(); UpdateContextualTabs(); };
            Editor.PreviewMouseLeftButtonUp += (o, e) => { if (ApplyFormatPainter()) return; if (!Editor.Selection.IsEmpty) Dispatcher.BeginInvoke(new Action(ShowMiniToolbar), DispatcherPriority.Input); };
            Editor.PreviewKeyDown += (o, e) => HideMiniToolbar();
            Editor.SizeChanged += (o, e) => ScheduleLayout();
            Editor.PreviewKeyDown += Editor_PreviewKeyDown;
            Editor.PreviewMouseLeftButtonDown += Editor_PreviewMouseLeftButtonDown;
            Editor.ContextMenuOpening += Editor_ContextMenuOpening;
            Editor.PreviewDragOver += Editor_PreviewDragOver;
            Editor.PreviewDrop += Editor_PreviewDrop;
            Editor.IsDocumentEnabled = false;
            Editor.RequestBringIntoView += (o, e) => { if (e.TargetRect.IsEmpty && e.TargetObject == Editor) e.Handled = true; };
            DataObject.AddPastingHandler(Editor, OnPasting);
            SpellCheck.SetIsEnabled(Editor, s.SpellCheck);
            AddCustomDictionary();

            TheRuler.IndentsChanged += Ruler_IndentsChanged;
            TheRuler.MarginsChanged += Ruler_MarginsChanged;
            PreviewKeyDown += Window_PreviewKeyDown;
            Drop += Window_Drop;
            DragOver += (o, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Closing += Window_Closing;
            LayoutUpdated += (o, e) => SyncRulerPosition();
            Loaded += (o, e) => { SetZoom(s.Zoom); Editor.Focus(); };

            InitFeatures();
            NewDocument();
        }

        // =====================================================================================
        //  Document lifecycle
        // =====================================================================================
        string DisplayName => _displayName ?? (_path != null ? System.IO.Path.GetFileName(_path) : "Document");

        bool IsBlank => _path == null && !_dirty && DocFormats.PlainText(Editor.Document).Trim().Length == 0;

        void NewDocument()
        {
            var fd = new FlowDocument();
            var p = new Paragraph();
            StyleDef.Get("Normal").Apply(p, false);
            fd.Blocks.Add(p);
            SetDocument(fd, Settings.Current.Page.Clone(), null, null, null);
            _displayName = "Document" + (++_docCounter);
            UpdateTitle();
        }

        static void PrepareDocument(FlowDocument fd)
        {
            var s = Settings.Current;
            fd.PagePadding = new Thickness(0);
            fd.FontFamily = new FontFamily(s.DefaultFont);
            fd.FontSize = s.DefaultSizePt * Fmt.PxPerPt;
            fd.Foreground = Brushes.Black;
            fd.TextAlignment = TextAlignment.Left;
            fd.IsHyphenationEnabled = false;
            var ps = new Style(typeof(Paragraph));
            ps.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0, 0, 0, 10 * Fmt.PxPerPt)));
            fd.Resources[typeof(Paragraph)] = ps;
            var ls = new Style(typeof(List));
            ls.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
            ls.Setters.Add(new Setter(Block.PaddingProperty, new Thickness(36, 0, 0, 0)));
            fd.Resources[typeof(List)] = ls;
            var ts = new Style(typeof(Table));
            ts.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0, 0, 0, 10)));
            ts.Setters.Add(new Setter(Table.CellSpacingProperty, 0.0));
            fd.Resources[typeof(Table)] = ts;
            var hs = new Style(typeof(Hyperlink));
            hs.Setters.Add(new Setter(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0, 0, 0xFF))));
            hs.Setters.Add(new Setter(Inline.TextDecorationsProperty, TextDecorations.Underline));
            hs.Setters.Add(new Setter(FrameworkContentElement.ToolTipProperty, "Ctrl+Click to follow link"));
            fd.Resources[typeof(Hyperlink)] = hs;
        }

        void SetDocument(FlowDocument fd, PageSettings page, string path, string title, string author) => SetDocument(fd, page, path, title, author, null);
        void SetDocument(FlowDocument fd, PageSettings page, string path, string title, string author, DocExtras extras)
        {
            _loading = true;
            _ex = extras ?? new DocExtras();
            if (_ex.Theme != StyleDef.ThemeName || _ex.StyleSet != StyleDef.StyleSetName) { try { StyleDef.Init(_ex.Theme ?? "Office", _ex.StyleSet ?? "Word 2007"); BuildStyleUI(); } catch { } }
            PrepareDocument(fd);
            RemoveImageAdorner();
            Editor.Document = fd;
            _page = page ?? Settings.Current.Page.Clone();
            _path = path;
            _docTitle = title; _author = author;
            _displayName = null;
            ApplyPage();
            Editor.CaretPosition = fd.ContentStart.GetInsertionPosition(LogicalDirection.Forward) ?? fd.ContentStart;
            Scroller.ScrollToHome();
            _loading = false;
            _dirty = false;
            UpdateTitle();
            UpdateToolbar();
            ApplyExtras();
            if (_view != "print") ApplyViewLayout();
            ScheduleLayout();
        }

        public void OpenFile(string path)
        {
            if (!IsBlank && !(_path != null && string.Equals(_path, path, StringComparison.OrdinalIgnoreCase)))
            {
                var existing = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w._path != null && string.Equals(w._path, path, StringComparison.OrdinalIgnoreCase));
                if (existing != null) { existing.Activate(); return; }
                var w = new MainWindow();
                w.Show();
                w.OpenFile(path);
                return;
            }
            LoadedDocument doc;
            Mouse.OverrideCursor = Cursors.Wait;
            try { doc = DocFormats.Load(path); }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                VistaDialog.Error(this, ex.Message, $"Valco Word can't open \"{System.IO.Path.GetFileName(path)}\".");
                return;
            }
            finally { Mouse.OverrideCursor = null; }
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            bool keepPath = DocFormats.CanSave(path);
            SetDocument(doc.Document, doc.Page, keepPath ? path : null, doc.Title, doc.Author, doc.Extras);
            _convertible = !keepPath && (ext == ".doc" || ext == ".rtf" || ext == ".htm" || ext == ".html");
            _sourcePath = path;
            if (!keepPath)
            {
                _displayName = System.IO.Path.GetFileNameWithoutExtension(path);
                _statusNote = ext == ".doc" ? "Opened the text of a Word 97-2003 file. Save as .docx to keep your edits." : "Opened as a new document. Save to keep it as .docx.";
                StatusMsg.Text = _statusNote;
            }
            UpdateTitle();
            Settings.AddRecent(path);
        }

        void UpdateTitle() => Title = $"{DisplayName} - Valco Word";

        void MarkDirty()
        {
            if (_loading) return;
            _dirty = true;
        }

        bool Save()
        {
            if (_path == null || !DocFormats.CanSave(_path)) return SaveAs();
            return SaveTo(_path);
        }

        bool SaveAs()
        {
            var dlg = new SaveFileDialog
            {
                Filter = DocFormats.SaveFilter, AddExtension = true, DefaultExt = ".docx", OverwritePrompt = true,
                FileName = _path != null ? System.IO.Path.GetFileNameWithoutExtension(_path) : SuggestedName(),
                InitialDirectory = _path != null ? System.IO.Path.GetDirectoryName(_path) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (_path != null)
            {
                var ext = System.IO.Path.GetExtension(_path).ToLowerInvariant();
                dlg.FilterIndex = ext == ".rtf" ? 2 : ext == ".txt" ? 3 : 1;
            }
            if (dlg.ShowDialog(this) != true) return false;
            var target = dlg.FileName;
            if (System.IO.Path.GetExtension(target).ToLowerInvariant() == ".txt" && !HasOnlyText())
            {
                if (VistaDialog.Show(this, "Saving as a text document removes all formatting, pictures and tables. Do you want to continue?",
                        "Valco Word", MessageBoxButton.YesNo, DlgIcon.Warning, "You are about to save in a text-only format.") != MessageBoxResult.Yes) return false;
            }
            return SaveTo(target);
        }

        bool HasOnlyText() => !Fmt.AllBlocks(Editor.Document.Blocks).Any(b => b is Table || b is List || b is BlockUIContainer)
                              && !Fmt.AllParagraphs(Editor.Document.Blocks).Any(p => p.Inlines.Any(i => !(i is Run)));

        string SuggestedName()
        {
            var first = Fmt.AllParagraphs(Editor.Document.Blocks).Select(p => new TextRange(p.ContentStart, p.ContentEnd).Text.Trim()).FirstOrDefault(t => t.Length > 0);
            if (first == null) return DisplayName;
            first = new string(first.Take(40).Where(c => !System.IO.Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
            return first.Length > 0 ? first : DisplayName;
        }

        bool SaveTo(string path)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                _ex.Theme = StyleDef.ThemeName; _ex.StyleSet = StyleDef.StyleSetName;
                DocFormats.Save(path, Editor.Document, _page, _docTitle, string.IsNullOrEmpty(_author) ? (_ex.Custom.ContainsKey("removePersonal") ? "" : Environment.UserName) : _author, _ex);
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                VistaDialog.Error(this, ex.Message, "The document couldn't be saved.");
                return false;
            }
            finally { Mouse.OverrideCursor = null; }
            _path = path;
            _displayName = null;
            _dirty = false;
            _statusNote = null;
            UpdateTitle();
            ClearRecovery();
            Settings.AddRecent(path);
            Flash("Saved " + System.IO.Path.GetFileName(path));
            return true;
        }

        bool ConfirmClose()
        {
            if (!_dirty) return true;
            var r = VistaDialog.Show(this, "", "Valco Word", MessageBoxButton.YesNoCancel, DlgIcon.Warning, $"Do you want to save the changes to {DisplayName}?");
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return Save();
            return true;
        }

        void Window_Closing(object sender, CancelEventArgs e)
        {
            _findDlg?.Close();
            if (!ConfirmClose()) { e.Cancel = true; return; }
            ClearRecovery();
            var s = Settings.Current;
            s.WinMax = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal) { s.WinWidth = Width; s.WinHeight = Height; }
            s.Zoom = PageScale.ScaleX;
            Settings.Save();
        }

        void Flash(string msg)
        {
            StatusMsg.Text = msg;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            t.Tick += (o, e) => { t.Stop(); if (StatusMsg.Text == msg) StatusMsg.Text = _statusNote ?? ""; };
            t.Start();
        }

        // ---------------- AutoRecover ----------------
        void SaveRecovery()
        {
            if (!_dirty || Settings.Current.AutoRecoverMinutes <= 0) return;
            try
            {
                Directory.CreateDirectory(AutoRecover.Dir);
                if (_recoverSlot == null)
                {
                    _recoverSlot = AutoRecover.NewSlot();
                    _recoverLock = new FileStream(_recoverSlot + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                }
                using (var fs = new FileStream(_recoverSlot, FileMode.Create))
                    DocxWriter.Save(Editor.Document, _page, _docTitle, _author, fs);
                File.WriteAllText(_recoverSlot + ".name", (_path ?? "") + "\n" + DisplayName);
            }
            catch { }
        }

        void ClearRecovery()
        {
            if (_recoverSlot == null) return;
            try { _recoverLock?.Dispose(); } catch { }
            _recoverLock = null;
            AutoRecover.Delete(_recoverSlot);
            try { File.Delete(_recoverSlot + ".lock"); } catch { }
            _recoverSlot = null;
        }

        public void OfferRecovery(List<string> slots)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var names = slots.Select(sl => (AutoRecover.OriginalName(sl) ?? "").Split('\n').Last()).Where(n => n.Length > 0).ToList();
                var msg = "Valco Word closed before these documents were saved:\n\n" + string.Join("\n", names.Take(8).Select(n => "  • " + n)) +
                          "\n\nDo you want to open the recovered copies?";
                var r = VistaDialog.Show(this, msg, "Document Recovery", MessageBoxButton.YesNoCancel, DlgIcon.Question, "Valco Word recovered your work");
                if (r == MessageBoxResult.Cancel) return;
                foreach (var slot in slots)
                {
                    if (r == MessageBoxResult.Yes)
                    {
                        try
                        {
                            var doc = DocxReader.Load(slot);
                            var info = (AutoRecover.OriginalName(slot) ?? "\nDocument").Split('\n');
                            var target = IsBlank ? this : new MainWindow();
                            if (target != this) target.Show();
                            target.SetDocument(doc.Document, doc.Page, null, doc.Title, doc.Author);
                            target._displayName = info.Last() + " (Recovered)";
                            target._dirty = true;
                            target.UpdateTitle();
                        }
                        catch { continue; }
                    }
                    AutoRecover.Delete(slot);
                    try { File.Delete(slot + ".lock"); } catch { }
                }
            }), DispatcherPriority.ApplicationIdle);
        }

        // =====================================================================================
        //  Page layout, zoom, ruler
        // =====================================================================================
        void ApplyPage()
        {
            Page.Width = _page.WidthPx;
            Editor.Margin = new Thickness(_page.MarginLeft * 96, _page.MarginTop * 96, _page.MarginRight * 96, _page.MarginBottom * 96);
            Editor.Width = _page.BodyWidthPx;
            Editor.MinHeight = _page.BodyHeightPx;
            PageGrid.MinHeight = _page.HeightPx;
            UpdateRuler();
            ScheduleLayout();
        }

        void ScheduleLayout()
        {
            _layoutTimer.Stop();
            _layoutTimer.Start();
        }

        void UpdatePages()
        {
            var doc = Editor.Document;
            double bodyH = _page.BodyHeightPx;
            var endRect = doc.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            if (endRect.IsEmpty) return;
            double contentBottom = endRect.Bottom;
            var breaks = new List<double>();
            foreach (var b in doc.Blocks)
            {
                if (!b.BreakPageBefore) continue;
                var r = b.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                if (r.IsEmpty) continue;
                double y = r.Top - (double.IsNaN(b.Margin.Top) ? 0 : b.Margin.Top);
                if (y > 1) breaks.Add(y);
            }
            breaks.Sort();
            _boundaries.Clear();
            double start = 0;
            foreach (var y in breaks)
            {
                while (start + bodyH < y - 1) { start += bodyH; _boundaries.Add((start, false)); }
                _boundaries.Add((y, true));
                start = y;
            }
            while (start + bodyH < contentBottom - 1) { start += bodyH; _boundaries.Add((start, false)); }
            _pageCount = _boundaries.Count + 1;
            double minH = Math.Max(bodyH, start + bodyH);
            if (Math.Abs(Editor.MinHeight - minH) > 0.5) Editor.MinHeight = minH;
            DrawOverlay();
            UpdatePageIndicator();
        }

        void DrawOverlay()
        {
            Overlay.Children.Clear();
            DrawPageDecorations();
            if (Settings.Current.ShowMarks) DrawFormattingMarks();
            if (_view != "print") { DrawViewOverlay(); return; }
            double w = _page.WidthPx, top = Editor.Margin.Top, ml = Editor.Margin.Left, mr = Editor.Margin.Right;
            var autoBrush = new SolidColorBrush(Color.FromRgb(0x9D, 0xB2, 0xCC));
            var manualBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0x7F, 0xB0));
            int page = 1;
            foreach (var (y, manual) in _boundaries)
            {
                page++;
                double yy = Math.Round(top + y) + 0.5;
                if (!manual)
                {
                    var line = new Line { X1 = 0, X2 = w, Y1 = yy, Y2 = yy, Stroke = autoBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 6, 4 } };
                    Overlay.Children.Add(line);
                    var lbl = new TextBlock { Text = "Page " + page, FontSize = 9, Foreground = autoBrush };
                    Canvas.SetLeft(lbl, w - mr + 8); Canvas.SetTop(lbl, yy + 1);
                    Overlay.Children.Add(lbl);
                }
                else
                {
                    var line = new Line { X1 = ml, X2 = w - mr, Y1 = yy - 4, Y2 = yy - 4, Stroke = manualBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 1, 2 } };
                    Overlay.Children.Add(line);
                    var lbl = new Border
                    {
                        Background = Brushes.White, Padding = new Thickness(5, 0, 5, 0),
                        Child = new TextBlock { Text = "Page Break", FontSize = 10, Foreground = manualBrush }
                    };
                    lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(lbl, ml + (w - ml - mr - lbl.DesiredSize.Width) / 2); Canvas.SetTop(lbl, yy - 4 - lbl.DesiredSize.Height / 2);
                    Overlay.Children.Add(lbl);
                }
            }
        }

        void UpdatePageIndicator()
        {
            var r = Editor.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
            if (!r.IsEmpty) _currentPage = 1 + _boundaries.Count(b => b.Y <= r.Top + 1);
            _currentPage = Math.Min(_currentPage, _pageCount);
            PageText.Text = $"Page: {_currentPage} of {_pageCount}";
        }

        void UpdateCounts()
        {
            var text = DocFormats.PlainText(Editor.Document);
            int words = Regex.Matches(text, @"[\p{L}\p{N}][\p{L}\p{N}'’\-]*").Count;
            WordsText.Text = $"Words: {words:N0}";
            if (SpellCheck.GetIsEnabled(Editor))
            {
                bool errors = false;
                try { errors = Editor.GetNextSpellingErrorPosition(Editor.Document.ContentStart, LogicalDirection.Forward) != null; } catch { }
                SpellStatusIcon.Source = UI.Img(errors ? "IconWarning" : "IconSpelling");
                BtnSpellStatus.ToolTip = errors ? "Spelling errors found. Click to go to the next one (F7)." : "No spelling errors";
            }
            else { SpellStatusIcon.Source = UI.Img("IconSpelling"); BtnSpellStatus.ToolTip = "Spell checking is off"; }
        }

        double Zoom => PageScale.ScaleX;

        void SetZoom(double z)
        {
            z = Math.Max(0.1, Math.Min(5, z));
            PageScale.ScaleX = PageScale.ScaleY = z;
            _updatingUI = true;
            ZoomSlider.Value = z * 100;
            _updatingUI = false;
            ZoomText.Text = Math.Round(z * 100) + "%";
            UpdateRuler();
        }

        void ZoomToFit(string mode)
        {
            double vw = Math.Max(100, Scroller.ViewportWidth - 60), vh = Math.Max(100, Scroller.ViewportHeight - 60);
            double zw = vw / _page.WidthPx, zh = vh / _page.HeightPx;
            SetZoom(mode == "page" ? Math.Min(zw, zh) : zw);
        }

        void UpdateRuler()
        {
            if (_page == null) return;
            TheRuler.Zoom = Zoom;
            TheRuler.PageWidth = _page.WidthPx;
            TheRuler.MarginLeft = _page.MarginLeft * 96;
            TheRuler.MarginRight = _page.MarginRight * 96;
            var p = Editor.CaretPosition.Paragraph;
            if (p != null)
            {
                double listOff = ListOffset(p);
                TheRuler.LeftIndent = Safe(p.Margin.Left) + listOff;
                TheRuler.FirstIndent = Safe(p.TextIndent);
                TheRuler.RightIndent = Safe(p.Margin.Right);
                TheRuler.IndentsEnabled = Fmt.AncestorAt<TableCell>(Editor.CaretPosition) == null;
            }
            SyncRulerPosition(true);
        }

        void SyncRulerPosition(bool force = false)
        {
            if (!TheRuler.IsVisible || !Page.IsLoaded) return;
            try
            {
                double x = Page.TranslatePoint(new Point(0, 0), TheRuler).X;
                if (force || Math.Abs(x - TheRuler.PageLeft) > 0.5) { TheRuler.PageLeft = x; TheRuler.Refresh(); }
            }
            catch { }
        }

        static double Safe(double v) => double.IsNaN(v) || double.IsInfinity(v) ? 0 : v;

        static double ListOffset(Paragraph p)
        {
            double off = 0;
            DependencyObject d = p.Parent;
            while (d != null)
            {
                if (d is List l) off += Safe(l.Padding.Left) + Safe(l.Margin.Left);
                d = d is TextElement te ? te.Parent : null;
            }
            return off;
        }

        void Ruler_IndentsChanged(double left, double first, double right)
        {
            Editor.BeginChange();
            foreach (var p in Fmt.SelectedParagraphs(Editor))
            {
                double off = ListOffset(p);
                p.Margin = new Thickness(Math.Max(0, left - off), Safe(p.Margin.Top), Math.Max(0, right), Safe(p.Margin.Bottom));
                p.TextIndent = first;
            }
            Editor.EndChange();
            MarkDirty();
            UpdateRuler();
        }

        void Ruler_MarginsChanged(double left, double right)
        {
            _page.MarginLeft = Math.Round(left / 96 * 16) / 16;
            _page.MarginRight = Math.Round(right / 96 * 16) / 16;
            ApplyPage();
            MarkDirty();
        }

        void Scroller_ScrollChanged(object sender, ScrollChangedEventArgs e) => SyncRulerPosition();

        void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            SetZoom(Math.Round((Zoom + (e.Delta > 0 ? 0.1 : -0.1)) * 10) / 10);
            e.Handled = true;
        }

        void PageHost_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == PageHost)
            {
                var pt = e.GetPosition(Page);
                if (pt.Y > Page.ActualHeight * Zoom) Editor.CaretPosition = Editor.Document.ContentEnd;
                Editor.Focus();
            }
        }

        void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingUI || PageScale == null) return;
            SetZoom(Math.Round(e.NewValue / 5) * 5 / 100);
        }
        void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(Math.Round((Zoom + 0.1) * 10) / 10);
        void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(Math.Round((Zoom - 0.1) * 10) / 10);
        void ZoomPreset_Click(object sender, RoutedEventArgs e)
        {
            var tag = (string)((FrameworkElement)sender).Tag;
            if (tag == "width" || tag == "page") ZoomToFit(tag);
            else SetZoom(double.Parse(tag, CultureInfo.InvariantCulture));
        }
        void ZoomDialog_Click(object sender, RoutedEventArgs e)
        {
            var d = new ZoomDialog(this, Zoom);
            if (d.ShowDialog() != true) return;
            if (d.Mode != null) ZoomToFit(d.Mode); else if (d.Zoom.HasValue) SetZoom(d.Zoom.Value);
        }

        // =====================================================================================
        //  Editor events
        // =====================================================================================
        void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            MarkDirty();
            ScheduleLayout();
            BtnUndo.IsEnabled = Editor.CanUndo;
            BtnRedo.IsEnabled = Editor.CanRedo;
        }

        void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            if (e.Key == Key.Enter && mods == ModifierKeys.None)
            {
                var p = Editor.CaretPosition.Paragraph;
                var tag = p?.Tag as string;
                if (tag != null && tag != "Normal" && tag != "NoSpacing" && tag != "Quote" && tag != "Code"
                    && new TextRange(Editor.CaretPosition, p.ContentEnd).Text.Trim().Length == 0)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var np = Editor.CaretPosition.Paragraph;
                        if (np != null && np != p) StyleDef.Get("Normal").Apply(np);
                        UpdateToolbar();
                    }), DispatcherPriority.Input);
                }
            }
            else if (e.Key == Key.Tab && (mods == ModifierKeys.None || mods == ModifierKeys.Shift))
            {
                var cell = Fmt.AncestorAt<TableCell>(Editor.CaretPosition);
                if (cell != null) { MoveCell(cell, mods == ModifierKeys.Shift); e.Handled = true; }
            }
            else if (e.Key == Key.Back && mods == ModifierKeys.None && Editor.Selection.IsEmpty)
            {
                // Backspace at the start of a paragraph after a page break removes the break first
                var p = Editor.CaretPosition.Paragraph;
                if (p != null && p.BreakPageBefore && Editor.CaretPosition.CompareTo(p.ContentStart.GetInsertionPosition(LogicalDirection.Forward)) <= 0)
                { p.BreakPageBefore = false; MarkDirty(); ScheduleLayout(); e.Handled = true; }
            }
            else if ((e.Key == Key.Delete || e.Key == Key.Back) && _selectedImage != null)
            {
                RemoveImageAdorner();
            }
        }

        void MoveCell(TableCell cell, bool back)
        {
            var row = (TableRow)cell.Parent;
            var group = (TableRowGroup)row.Parent;
            var table = (Table)group.Parent;
            var cells = table.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells).ToList();
            int i = cells.IndexOf(cell);
            if (back) { if (i > 0) SelectCell(cells[i - 1]); return; }
            if (i < cells.Count - 1) { SelectCell(cells[i + 1]); return; }
            var nr = NewRowLike(row);
            group.Rows.Add(nr);
            SelectCell(nr.Cells[0]);
        }

        void SelectCell(TableCell c)
        {
            var first = Fmt.AllParagraphs(c.Blocks).FirstOrDefault();
            if (first == null) return;
            var start = first.ContentStart.GetPositionAtOffset(0, LogicalDirection.Forward);
            var last = Fmt.AllParagraphs(c.Blocks).Last();
            if (new TextRange(first.ContentStart, last.ContentEnd).Text.Trim().Length == 0)
                Editor.Selection.Select(start, start);
            else
                Editor.Selection.Select(start, last.ContentEnd.GetPositionAtOffset(0, LogicalDirection.Backward));
        }

        void Editor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_painterChar == null) ClearSimilar();
            var pt = e.GetPosition(Editor);
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                var pos = Editor.GetPositionFromPoint(pt, false);
                var link = pos == null ? null : Fmt.AncestorAt<Hyperlink>(pos);
                if (link?.NavigateUri != null) { FollowLink(link.NavigateUri); e.Handled = true; return; }
            }
            var img = ImageAt(pt);
            if (img != null)
            {
                SelectImage(img);
                if (e.ClickCount == 2) PictureSize(img);
                e.Handled = true;
                Editor.Focus();
            }
        }

        static void OpenLink(Uri uri)
        {
            try { Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); } catch { }
        }

        Image ImageAt(Point pt)
        {
            Image found = null;
            VisualTreeHelper.HitTest(Editor, null, r =>
            {
                DependencyObject d = r.VisualHit;
                while (d != null && d != Editor)
                {
                    if (d is Image i && IsDocImage(i)) { found = i; return HitTestResultBehavior.Stop; }
                    d = VisualTreeHelper.GetParent(d);
                }
                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(pt));
            return found;
        }

        bool IsDocImage(Image i) => i.Parent is InlineUIContainer || i.Parent is BlockUIContainer;

        void SelectImage(Image img)
        {
            if (img.Parent is InlineUIContainer c) Editor.Selection.Select(c.ElementStart, c.ElementEnd);
            else if (img.Parent is BlockUIContainer b) Editor.Selection.Select(b.ElementStart, b.ElementEnd);
            ShowImageAdorner(img);
        }

        void ShowImageAdorner(Image img)
        {
            if (_selectedImage == img && _imgAdorner != null) return;
            RemoveImageAdorner();
            var layer = AdornerLayer.GetAdornerLayer(img);
            if (layer == null) return;
            _imgAdorner = new ImageResizeAdorner(img);
            _imgAdorner.Resized += () => { MarkDirty(); ScheduleLayout(); };
            layer.Add(_imgAdorner);
            _selectedImage = img;
        }

        void RemoveImageAdorner()
        {
            if (_imgAdorner != null && _selectedImage != null)
            {
                var layer = AdornerLayer.GetAdornerLayer(_selectedImage);
                layer?.Remove(_imgAdorner);
            }
            _imgAdorner = null;
            _selectedImage = null;
        }

        void CheckImageSelection()
        {
            if (_selectedImage == null) return;
            var c = _selectedImage.Parent as TextElement;
            var sel = Editor.Selection;
            if (c == null || sel.Start.CompareTo(c.ElementStart) > 0 || sel.End.CompareTo(c.ElementEnd) < 0 ||
                new TextRange(sel.Start, sel.End).Text.Trim().Length > 0) RemoveImageAdorner();
        }

        void PictureSize(Image img)
        {
            var src = img.Source as BitmapSource;
            double w = double.IsNaN(img.Width) ? img.ActualWidth : img.Width, h = double.IsNaN(img.Height) ? img.ActualHeight : img.Height;
            var d = new PictureSizeDialog(this, w, h, src?.Width ?? w, src?.Height ?? h);
            if (d.ShowDialog() != true) return;
            img.Width = d.W; img.Height = d.H;
            MarkDirty(); ScheduleLayout();
        }

        void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            var data = e.DataObject;
            bool hasRich = data.GetDataPresent(DataFormats.Rtf) || data.GetDataPresent(DataFormats.Xaml) || data.GetDataPresent(DataFormats.XamlPackage);
            if (!hasRich && (data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent("PNG")))
            {
                e.CancelCommand();
                BitmapSource img = null;
                try
                {
                    if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream ms)
                    {
                        var bytes = ms.ToArray();
                        img = LoadBitmap(bytes);
                        Dispatcher.BeginInvoke(new Action(() => InsertImage(img, new ImageData { Bytes = bytes, Ext = "png" })));
                        return;
                    }
                    img = Clipboard.GetImage();
                }
                catch { }
                if (img != null) Dispatcher.BeginInvoke(new Action(() => InsertImage(img, null)));
                return;
            }
            if (!hasRich && data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
            {
                var pics = files.Where(IsImageFile).ToList();
                if (pics.Count > 0) { e.CancelCommand(); Dispatcher.BeginInvoke(new Action(() => pics.ForEach(InsertImageFile))); }
            }
        }

        static bool IsImageFile(string f) => new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".ico" }.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant());

        void Editor_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
        }

        void Editor_PreviewDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files)) return;
            e.Handled = true;
            var pos = Editor.GetPositionFromPoint(e.GetPosition(Editor), true);
            if (pos != null) Editor.CaretPosition = pos;
            foreach (var f in files)
            {
                if (IsImageFile(f)) InsertImageFile(f);
                else OpenFile(f);
            }
        }

        void Window_Drop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files)) return;
            foreach (var f in files) { if (IsImageFile(f)) InsertImageFile(f); else OpenFile(f); }
        }

        // =====================================================================================
        //  Keyboard shortcuts
        // =====================================================================================
        void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var m = Keyboard.Modifiers;
            bool ctrl = m == ModifierKeys.Control, ctrlShift = m == (ModifierKeys.Control | ModifierKeys.Shift), ctrlAlt = m == (ModifierKeys.Control | ModifierKeys.Alt);
            bool handled = true;
            if (ctrl && key == Key.N) New_Click(null, null);
            else if (ctrl && key == Key.O) Open_Click(null, null);
            else if (ctrl && key == Key.S) Save();
            else if (m == ModifierKeys.None && key == Key.F12) SaveAs();
            else if (ctrl && key == Key.P) Print_Click(null, null);
            else if (ctrl && key == Key.W) Close();
            else if (ctrl && key == Key.F) Find_Click(null, null);
            else if (ctrl && key == Key.H) Replace_Click(null, null);
            else if (ctrl && key == Key.G) GoTo_Click(null, null);
            else if (ctrl && key == Key.K) InsertLink_Click(null, null);
            else if (ctrl && key == Key.D) FontDialog_Click(null, null);
            else if (ctrl && key == Key.B) Bold_Click(null, null);
            else if (ctrl && key == Key.I) Italic_Click(null, null);
            else if (ctrl && key == Key.U) Underline_Click(null, null);
            else if (ctrl && key == Key.OemPlus) Sub_Click(null, null);
            else if (ctrlShift && key == Key.OemPlus) Sup_Click(null, null);
            else if (ctrl && key == Key.Space) ClearFormat_Click(null, null);
            else if (ctrl && key == Key.Enter) InsertPageBreak_Click(null, null);
            else if (ctrlShift && key == Key.V) PastePlain_Click(null, null);
            else if (ctrlShift && key == Key.N) ApplyStyle("Normal");
            else if (ctrlShift && key == Key.G) WordCount_Click(null, null);
            else if (ctrlShift && key == Key.L) Bullets_Click(null, null);
            else if (ctrlAlt && key == Key.D1) ApplyStyle("Heading1");
            else if (ctrlAlt && key == Key.D2) ApplyStyle("Heading2");
            else if (ctrlAlt && key == Key.D3) ApplyStyle("Heading3");
            else if (ctrl && key == Key.D1) SetLineSpacing(1);
            else if (ctrl && key == Key.D2) SetLineSpacing(2);
            else if (ctrl && key == Key.D5) SetLineSpacing(1.5);
            else if (m == ModifierKeys.None && key == Key.F3) FindAgain(false);
            else if (m == ModifierKeys.Shift && key == Key.F3) FindAgain(true);
            else if (m == ModifierKeys.None && key == Key.F7) NextSpelling_Click(null, null);
            else if (m == ModifierKeys.None && key == Key.F11) ToggleFullScreen();
            else if (m == ModifierKeys.None && key == Key.F1) InfoDialogs.Shortcuts(this);
            else if (m == ModifierKeys.None && key == Key.Escape && _painterChar != null) StopFormatPainter();
            else if (m == ModifierKeys.None && key == Key.Escape && _fullScreen) ToggleFullScreen();
            else if ((ctrl || ctrlShift) && (key == Key.L || key == Key.E || key == Key.R || key == Key.J || key == Key.OemCloseBrackets || key == Key.OemOpenBrackets))
            {
                handled = false;
                Dispatcher.BeginInvoke(new Action(UpdateToolbar), DispatcherPriority.Input);
            }
            else handled = false;
            if (handled) e.Handled = true;
        }

        // =====================================================================================
        //  Toolbar state
        // =====================================================================================

        void UpdateToolbar()
        {
            if (Editor.Document == null) return;
            _updatingUI = true;
            try
            {
                var sel = Editor.Selection;
                var fam = sel.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
                if (!CbFont.IsKeyboardFocusWithin) CbFont.Text = fam?.Source ?? "";
                var size = sel.GetPropertyValue(TextElement.FontSizeProperty);
                if (!CbSize.IsKeyboardFocusWithin) CbSize.Text = size is double d ? Math.Round(d * 0.75 * 2, MidpointRounding.AwayFromZero) / 2 + "" : "";
                TbBold.IsChecked = sel.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w.ToOpenTypeWeight() >= 600;
                TbItalic.IsChecked = sel.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle fs && fs != FontStyles.Normal;
                var deco = sel.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
                TbUnderline.IsChecked = Fmt.HasDecoration(deco, TextDecorationLocation.Underline);
                TbStrike.IsChecked = Fmt.HasDecoration(deco, TextDecorationLocation.Strikethrough);
                var ba = sel.GetPropertyValue(Inline.BaselineAlignmentProperty);
                TbSup.IsChecked = ba is BaselineAlignment b1 && b1 == BaselineAlignment.Superscript;
                TbSub.IsChecked = ba is BaselineAlignment b2 && b2 == BaselineAlignment.Subscript;
                var al = sel.GetPropertyValue(Block.TextAlignmentProperty);
                TbLeft.IsChecked = al is TextAlignment a1 && a1 == TextAlignment.Left;
                TbCenter.IsChecked = al is TextAlignment a2 && a2 == TextAlignment.Center;
                TbRight.IsChecked = al is TextAlignment a3 && a3 == TextAlignment.Right;
                TbJustify.IsChecked = al is TextAlignment a4 && a4 == TextAlignment.Justify;
                var list = Fmt.AncestorAt<List>(Editor.CaretPosition);
                TbBullets.IsChecked = list != null && Fmt.IsBullet(list.MarkerStyle);
                TbNumbering.IsChecked = list != null && !Fmt.IsBullet(list.MarkerStyle);
                var p = Editor.CaretPosition.Paragraph;
                var tag = p?.Tag as string ?? "Normal";
                HighlightStyle(tag);
                BtnUndo.IsEnabled = Editor.CanUndo;
                BtnRedo.IsEnabled = Editor.CanRedo;
            }
            finally { _updatingUI = false; }
        }

        // =====================================================================================
        //  Character formatting
        // =====================================================================================
        TextRange TargetRange()
        {
            var sel = Editor.Selection;
            if (!sel.IsEmpty) return sel;
            var caret = Editor.CaretPosition;
            string before = caret.GetTextInRun(LogicalDirection.Backward), after = caret.GetTextInRun(LogicalDirection.Forward);
            if (before.Length == 0 || after.Length == 0 || !char.IsLetterOrDigit(before[before.Length - 1]) || !char.IsLetterOrDigit(after[0])) return sel;
            int back = 0; while (back < before.Length && char.IsLetterOrDigit(before[before.Length - 1 - back])) back++;
            int fwd = 0; while (fwd < after.Length && char.IsLetterOrDigit(after[fwd])) fwd++;
            return new TextRange(caret.GetPositionAtOffset(-back), caret.GetPositionAtOffset(fwd));
        }

        void ApplyChar(DependencyProperty dp, object value)
        {
            if (ApplyToSimilar(dp, value)) { UpdateToolbar(); return; }
            var r = TargetRange();
            try { r.ApplyPropertyValue(dp, value); } catch { }
            MarkDirty();
            UpdateToolbar();
            Editor.Focus();
        }

        void Bold_Click(object sender, RoutedEventArgs e)
        {
            bool on = Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w.ToOpenTypeWeight() >= 600;
            ApplyChar(TextElement.FontWeightProperty, on ? FontWeights.Normal : FontWeights.Bold);
        }

        void Italic_Click(object sender, RoutedEventArgs e)
        {
            bool on = Editor.Selection.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle fs && fs != FontStyles.Normal;
            ApplyChar(TextElement.FontStyleProperty, on ? FontStyles.Normal : FontStyles.Italic);
        }

        void ToggleDecoration(TextDecorationLocation loc)
        {
            var cur = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            bool on = Fmt.HasDecoration(cur, loc);
            var result = new TextDecorationCollection();
            if (cur != null) foreach (var d in cur) if (d.Location != loc) result.Add(d);
            if (!on) result.Add(loc == TextDecorationLocation.Underline ? TextDecorations.Underline[0] : TextDecorations.Strikethrough[0]);
            ApplyChar(Inline.TextDecorationsProperty, result);
        }

        void Underline_Click(object sender, RoutedEventArgs e) => ToggleDecoration(TextDecorationLocation.Underline);
        void Strike_Click(object sender, RoutedEventArgs e) => ToggleDecoration(TextDecorationLocation.Strikethrough);

        void ToggleBaseline(BaselineAlignment target)
        {
            bool on = Editor.Selection.GetPropertyValue(Inline.BaselineAlignmentProperty) is BaselineAlignment b && b == target;
            ApplyChar(Inline.BaselineAlignmentProperty, on ? BaselineAlignment.Baseline : target);
        }
        void Sub_Click(object sender, RoutedEventArgs e) => ToggleBaseline(BaselineAlignment.Subscript);
        void Sup_Click(object sender, RoutedEventArgs e) => ToggleBaseline(BaselineAlignment.Superscript);

        void CbFont_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingUI || !CbFont.IsDropDownOpen || !(CbFont.SelectedItem is string f)) return;
            ApplyChar(TextElement.FontFamilyProperty, new FontFamily(f));
        }

        void CbFont_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var f = CbFont.Text.Trim();
                if (f.Length > 0) ApplyChar(TextElement.FontFamilyProperty, new FontFamily(f));
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) { UpdateToolbar(); Editor.Focus(); }
        }

        void CbSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingUI || !CbSize.IsDropDownOpen || !(CbSize.SelectedItem is string s)) return;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var pt)) ApplyChar(TextElement.FontSizeProperty, pt * Fmt.PxPerPt);
        }

        void CbSize_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var v = D.ParseNum(CbSize.Text);
                if (v.HasValue && v >= 1 && v <= 1638) ApplyChar(TextElement.FontSizeProperty, v.Value * Fmt.PxPerPt);
                else { VistaDialog.Show(this, "The number must be between 1 and 1638.", "Valco Word", MessageBoxButton.OK, DlgIcon.Warning); UpdateToolbar(); }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) { UpdateToolbar(); Editor.Focus(); }
        }

        void Combo_DropDownClosed(object sender, EventArgs e)
        {
            if (!_updatingUI) Editor.Focus();
        }

        void GrowShrink(bool grow)
        {
            var cur = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty);
            double pt = cur is double d ? d * 0.75 : Settings.Current.DefaultSizePt;
            double next = grow ? Sizes.FirstOrDefault(s => s > pt + 0.01) : Sizes.LastOrDefault(s => s < pt - 0.01);
            if (next == 0) next = grow ? Math.Min(1638, pt + 10) : Math.Max(1, pt - 1);
            ApplyChar(TextElement.FontSizeProperty, next * Fmt.PxPerPt);
        }
        void Grow_Click(object sender, RoutedEventArgs e) => GrowShrink(true);
        void Shrink_Click(object sender, RoutedEventArgs e) => GrowShrink(false);

        void FontColor_Click(object sender, RoutedEventArgs e) => ApplyChar(TextElement.ForegroundProperty, new SolidColorBrush(_fontColor));
        void FontColorDrop_Click(object sender, RoutedEventArgs e) =>
            ColorPalette.ShowText(BtnFontColor, "Automatic", c =>
            {
                _fontColor = c ?? Colors.Black;
                FontColorSwatch.Fill = new SolidColorBrush(_fontColor);
                ApplyChar(TextElement.ForegroundProperty, new SolidColorBrush(_fontColor));
            });

        void Highlight_Click(object sender, RoutedEventArgs e) => ApplyChar(TextElement.BackgroundProperty, new SolidColorBrush(_highlight));
        void HighlightDrop_Click(object sender, RoutedEventArgs e) =>
            ColorPalette.ShowHighlight(BtnHighlight, c =>
            {
                if (c.HasValue) { _highlight = c.Value; HighlightSwatch.Fill = new SolidColorBrush(_highlight); ApplyChar(TextElement.BackgroundProperty, new SolidColorBrush(_highlight)); }
                else ApplyChar(TextElement.BackgroundProperty, Brushes.Transparent);
            });

        void ClearFormat_Click(object sender, RoutedEventArgs e)
        {
            Editor.BeginChange();
            var paras = Fmt.SelectedParagraphs(Editor);
            if (!Editor.Selection.IsEmpty) Editor.Selection.ClearAllProperties();
            foreach (var p in paras) StyleDef.Get("Normal").Apply(p);
            Editor.EndChange();
            MarkDirty(); UpdateToolbar(); UpdateRuler(); Editor.Focus();
        }

        void FontDialog_Click(object sender, RoutedEventArgs e)
        {
            var sel = Editor.Selection;
            var deco = sel.GetPropertyValue(Inline.TextDecorationsProperty);
            var ba = sel.GetPropertyValue(Inline.BaselineAlignmentProperty);
            var cur = new FontResult
            {
                Family = (sel.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily)?.Source,
                SizePt = sel.GetPropertyValue(TextElement.FontSizeProperty) is double d ? Math.Round(d * 0.75 * 2) / 2 : (double?)null,
                Bold = sel.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w ? w.ToOpenTypeWeight() >= 600 : (bool?)null,
                Italic = sel.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle fs ? fs != FontStyles.Normal : (bool?)null,
                Underline = deco == DependencyProperty.UnsetValue ? (bool?)null : Fmt.HasDecoration(deco as TextDecorationCollection, TextDecorationLocation.Underline),
                Strike = deco == DependencyProperty.UnsetValue ? (bool?)null : Fmt.HasDecoration(deco as TextDecorationCollection, TextDecorationLocation.Strikethrough),
                Superscript = ba is BaselineAlignment b1 ? b1 == BaselineAlignment.Superscript : (bool?)null,
                Subscript = ba is BaselineAlignment b2 ? b2 == BaselineAlignment.Subscript : (bool?)null,
                Color = sel.GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush sc ? sc.Color : (Color?)null
            };
            var dlg = new FontDialog(this, cur);
            if (dlg.ShowDialog() != true) return;
            var r = dlg.Result;
            var range = TargetRange();
            Editor.BeginChange();
            try
            {
                if (r.Family != null) range.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(r.Family));
                if (r.SizePt.HasValue) range.ApplyPropertyValue(TextElement.FontSizeProperty, r.SizePt.Value * Fmt.PxPerPt);
                if (r.Bold.HasValue) range.ApplyPropertyValue(TextElement.FontWeightProperty, r.Bold.Value ? FontWeights.Bold : FontWeights.Normal);
                if (r.Italic.HasValue) range.ApplyPropertyValue(TextElement.FontStyleProperty, r.Italic.Value ? FontStyles.Italic : FontStyles.Normal);
                if (r.Underline.HasValue || r.Strike.HasValue)
                {
                    var dc = new TextDecorationCollection();
                    bool u = r.Underline ?? cur.Underline ?? false, s = r.Strike ?? cur.Strike ?? false;
                    if (u) dc.Add(TextDecorations.Underline[0]);
                    if (s) dc.Add(TextDecorations.Strikethrough[0]);
                    range.ApplyPropertyValue(Inline.TextDecorationsProperty, dc);
                }
                if (r.Superscript == true) range.ApplyPropertyValue(Inline.BaselineAlignmentProperty, BaselineAlignment.Superscript);
                else if (r.Subscript == true) range.ApplyPropertyValue(Inline.BaselineAlignmentProperty, BaselineAlignment.Subscript);
                else if (r.Superscript == false && r.Subscript == false) range.ApplyPropertyValue(Inline.BaselineAlignmentProperty, BaselineAlignment.Baseline);
                if (r.Color.HasValue && r.Color != cur.Color) range.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(r.Color.Value));
            }
            finally { Editor.EndChange(); }
            MarkDirty(); UpdateToolbar(); Editor.Focus();
        }

        void ChangeCase_Click(object sender, RoutedEventArgs e)
        {
            var mode = (string)((FrameworkElement)sender).Tag;
            var sel = Editor.Selection;
            var range = sel.IsEmpty ? TargetRange() : sel;
            if (range.IsEmpty) return;
            var segs = new List<(TextPointer, TextPointer)>();
            var p = range.Start;
            while (p != null && p.CompareTo(range.End) < 0)
            {
                if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var len = p.GetTextRunLength(LogicalDirection.Forward);
                    var end = p.GetPositionAtOffset(len);
                    if (end.CompareTo(range.End) > 0) end = range.End;
                    segs.Add((p.GetPositionAtOffset(0, LogicalDirection.Forward), end.GetPositionAtOffset(0, LogicalDirection.Backward)));
                    p = end;
                }
                else p = p.GetNextContextPosition(LogicalDirection.Forward);
            }
            char prev = ' ';
            bool sentenceStart = true;
            Editor.BeginChange();
            foreach (var (s, en) in segs)
            {
                var r = new TextRange(s, en);
                var t = r.Text;
                var chars = t.ToCharArray();
                for (int i = 0; i < chars.Length; i++)
                {
                    char c = chars[i];
                    switch (mode)
                    {
                        case "upper": chars[i] = char.ToUpper(c); break;
                        case "lower": chars[i] = char.ToLower(c); break;
                        case "toggle": chars[i] = char.IsUpper(c) ? char.ToLower(c) : char.ToUpper(c); break;
                        case "title": chars[i] = char.IsLetter(prev) || prev == '\'' || prev == '’' ? char.ToLower(c) : char.ToUpper(c); break;
                        case "sentence":
                            if (char.IsLetter(c)) { chars[i] = sentenceStart ? char.ToUpper(c) : char.ToLower(c); sentenceStart = false; }
                            else if (c == '.' || c == '!' || c == '?') sentenceStart = true;
                            break;
                    }
                    prev = c;
                }
                var nt = new string(chars);
                if (nt != t) r.Text = nt;
                prev = ' ';
                if (mode == "title") prev = t.Length > 0 ? t[t.Length - 1] : ' ';
            }
            Editor.EndChange();
            MarkDirty();
        }

        // =====================================================================================
        //  Paragraph formatting
        // =====================================================================================
        void Align(TextAlignment a)
        {
            Editor.BeginChange();
            foreach (var p in Fmt.SelectedParagraphs(Editor)) p.TextAlignment = a;
            Editor.EndChange();
            MarkDirty(); UpdateToolbar(); Editor.Focus();
        }
        void AlignLeft_Click(object sender, RoutedEventArgs e) => Align(TextAlignment.Left);
        void AlignCenter_Click(object sender, RoutedEventArgs e) => Align(TextAlignment.Center);
        void AlignRight_Click(object sender, RoutedEventArgs e) => Align(TextAlignment.Right);
        void AlignJustify_Click(object sender, RoutedEventArgs e) => Align(TextAlignment.Justify);

        void Bullets_Click(object sender, RoutedEventArgs e) { EditingCommands.ToggleBullets.Execute(null, Editor); AfterList(); }
        void Numbering_Click(object sender, RoutedEventArgs e) { EditingCommands.ToggleNumbering.Execute(null, Editor); AfterList(); }

        void AfterList()
        {
            var list = Fmt.AncestorAt<List>(Editor.CaretPosition);
            if (list != null)
            {
                var top = list;
                while (Fmt.Ancestor<List>(top.Parent as TextElement) is List up) top = up;
                foreach (var p in Fmt.AllParagraphs(new Block[] { top })) p.Margin = new Thickness(Safe(p.Margin.Left), 0, Safe(p.Margin.Right), 0);
                top.Margin = new Thickness(0, 0, 0, 10 * Fmt.PxPerPt);
            }
            MarkDirty(); UpdateToolbar(); UpdateRuler(); Editor.Focus();
        }

        void ListStyle_Click(object sender, RoutedEventArgs e)
        {
            var tag = (string)((FrameworkElement)sender).Tag;
            var list = Fmt.AncestorAt<List>(Editor.CaretPosition);
            if (tag == "None")
            {
                if (list != null) { if (Fmt.IsBullet(list.MarkerStyle)) EditingCommands.ToggleBullets.Execute(null, Editor); else EditingCommands.ToggleNumbering.Execute(null, Editor); }
                AfterList();
                return;
            }
            var style = (TextMarkerStyle)Enum.Parse(typeof(TextMarkerStyle), tag);
            if (list == null)
            {
                if (Fmt.IsBullet(style)) EditingCommands.ToggleBullets.Execute(null, Editor); else EditingCommands.ToggleNumbering.Execute(null, Editor);
                list = Fmt.AncestorAt<List>(Editor.CaretPosition);
            }
            if (list != null) list.MarkerStyle = style;
            AfterList();
        }

        void Indent_Click(object sender, RoutedEventArgs e) => ChangeIndent(true);
        void Outdent_Click(object sender, RoutedEventArgs e) => ChangeIndent(false);

        void ChangeIndent(bool increase)
        {
            if (Fmt.AncestorAt<List>(Editor.CaretPosition) != null)
            {
                (increase ? EditingCommands.IncreaseIndentation : EditingCommands.DecreaseIndentation).Execute(null, Editor);
                // nested lists continue the bullet/number cycle like Word
                var l = Fmt.AncestorAt<List>(Editor.CaretPosition);
                if (l != null && increase)
                {
                    int depth = Fmt.ListDepth(l);
                    var parent = l.Parent is ListItem li ? li.List : null;
                    if (parent != null && l.MarkerStyle == parent.MarkerStyle)
                        l.MarkerStyle = Fmt.IsBullet(parent.MarkerStyle) ? Fmt.BulletCycle[depth % 3] : Fmt.NumberCycle[depth % 3];
                }
            }
            else
            {
                Editor.BeginChange();
                foreach (var p in Fmt.SelectedParagraphs(Editor))
                {
                    double left = Safe(p.Margin.Left);
                    left = increase ? (Math.Floor(left / 48 + 0.001) + 1) * 48 : Math.Max(0, (Math.Ceiling(left / 48 - 0.001) - 1) * 48);
                    p.Margin = new Thickness(left, Safe(p.Margin.Top), Safe(p.Margin.Right), Safe(p.Margin.Bottom));
                }
                Editor.EndChange();
            }
            MarkDirty(); UpdateRuler(); UpdateToolbar(); Editor.Focus();
        }

        void SetLineSpacing(double f)
        {
            Editor.BeginChange();
            foreach (var p in Fmt.SelectedParagraphs(Editor)) Fmt.SetLineSpacing(p, f);
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        void LineSpacing_Click(object sender, RoutedEventArgs e)
        {
            var cur = Editor.CaretPosition.Paragraph;
            double f = cur != null ? Fmt.GetLineSpacing(cur) : 1;
            var menu = new ContextMenu { PlacementTarget = BtnLineSpacing, Placement = PlacementMode.Bottom };
            foreach (var v in new[] { 1.0, 1.15, 1.5, 2.0, 2.5, 3.0 })
            {
                var mi = new MenuItem { Header = v.ToString("0.0#", CultureInfo.InvariantCulture), IsCheckable = true, IsChecked = Math.Abs(v - f) < 0.01 };
                var val = v;
                mi.Click += (o, a) => SetLineSpacing(val);
                menu.Items.Add(mi);
            }
            menu.Items.Add(new Separator());
            bool hasBefore = cur != null && Safe(cur.Margin.Top) > 0.5, hasAfter = cur != null && Safe(cur.Margin.Bottom) > 0.5;
            var before = new MenuItem { Header = hasBefore ? "Remove Space _Before Paragraph" : "Add Space _Before Paragraph" };
            before.Click += (o, a) => { foreach (var p in Fmt.SelectedParagraphs(Editor)) p.Margin = new Thickness(Safe(p.Margin.Left), hasBefore ? 0 : 12 * Fmt.PxPerPt, Safe(p.Margin.Right), Safe(p.Margin.Bottom)); MarkDirty(); ScheduleLayout(); };
            var after = new MenuItem { Header = hasAfter ? "Remove Space _After Paragraph" : "Add Space _After Paragraph" };
            after.Click += (o, a) => { foreach (var p in Fmt.SelectedParagraphs(Editor)) p.Margin = new Thickness(Safe(p.Margin.Left), Safe(p.Margin.Top), Safe(p.Margin.Right), hasAfter ? 0 : 10 * Fmt.PxPerPt); MarkDirty(); ScheduleLayout(); };
            menu.Items.Add(before); menu.Items.Add(after);
            menu.Items.Add(new Separator());
            var opts = new MenuItem { Header = "Line Spacing _Options..." };
            opts.Click += ParagraphDialog_Click;
            menu.Items.Add(opts);
            menu.IsOpen = true;
        }

        void ParagraphDialog_Click(object sender, RoutedEventArgs e)
        {
            var p = Editor.CaretPosition.Paragraph;
            if (p == null) return;
            var cur = new ParaResult
            {
                Align = p.TextAlignment, LeftPx = Safe(p.Margin.Left), RightPx = Safe(p.Margin.Right), FirstPx = Safe(p.TextIndent),
                BeforePt = Safe(p.Margin.Top) * 0.75, AfterPt = Safe(p.Margin.Bottom) * 0.75, LineFactor = Fmt.GetLineSpacing(p), PageBreakBefore = p.BreakPageBefore
            };
            var d = new ParagraphDialog(this, cur);
            if (d.ShowDialog() != true) return;
            var r = d.Result;
            Editor.BeginChange();
            var paras = Fmt.SelectedParagraphs(Editor);
            foreach (var x in paras)
            {
                x.TextAlignment = r.Align;
                x.Margin = new Thickness(r.LeftPx, r.BeforePt * Fmt.PxPerPt, r.RightPx, r.AfterPt * Fmt.PxPerPt);
                x.TextIndent = r.FirstPx;
                if (r.ExactPt.HasValue) { x.LineHeight = r.ExactPt.Value * Fmt.PxPerPt; x.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; }
                else Fmt.SetLineSpacing(x, r.LineFactor);
            }
            if (paras.Count > 0) paras[0].BreakPageBefore = r.PageBreakBefore;
            Editor.EndChange();
            MarkDirty(); UpdateToolbar(); UpdateRuler(); ScheduleLayout(); Editor.Focus();
        }

        void ApplyStyle(string id)
        {
            var def = StyleDef.Get(id);
            Editor.BeginChange();
            foreach (var p in Fmt.SelectedParagraphs(Editor)) def.Apply(p);
            Editor.EndChange();
            MarkDirty(); UpdateToolbar(); UpdateRuler(); ScheduleLayout(); Editor.Focus();
        }


        // =====================================================================================
        //  Insert
        // =====================================================================================
        static BitmapSource LoadBitmap(byte[] bytes)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        void InsertImageFile(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var src = LoadBitmap(bytes);
                var ext = System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                if (ext == "jpg") ext = "jpeg";
                var keep = ext == "png" || ext == "jpeg" || ext == "gif" || ext == "bmp";
                InsertImage(src, keep ? new ImageData { Bytes = bytes, Ext = ext } : null);
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "This picture couldn't be inserted."); }
        }

        void InsertImage(BitmapSource src, ImageData data)
        {
            if (src == null) return;
            double w = src.Width, h = src.Height;
            double max = _page.BodyWidthPx;
            var cell = Fmt.AncestorAt<TableCell>(Editor.CaretPosition);
            if (cell != null) max = Math.Min(max, 300);
            if (w > max) { h = h * max / w; w = max; }
            var img = new Image { Source = src, Width = Math.Round(w), Height = Math.Round(h), Stretch = Stretch.Fill, Tag = data };
            Editor.BeginChange();
            if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
            var pos = Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var c = new InlineUIContainer(img, pos);
            Editor.EndChange();
            Editor.CaretPosition = c.ElementEnd;
            MarkDirty(); ScheduleLayout(); Editor.Focus();
        }

        void InsertPicture_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Insert Picture", Multiselect = true,
                Filter = "All Pictures|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.ico|All Files|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };
            if (dlg.ShowDialog(this) != true) return;
            foreach (var f in dlg.FileNames) InsertImageFile(f);
        }

        TableCell NewCell(bool borders) => new TableCell(NewCellParagraph())
        {
            BorderBrush = Brushes.Black, BorderThickness = new Thickness(borders ? 0.5 : 0), Padding = new Thickness(7, 2, 7, 2)
        };

        static Paragraph NewCellParagraph()
        {
            var p = new Paragraph();
            StyleDef.Get("Normal").Apply(p, false);
            p.Margin = new Thickness(0);
            p.Tag = null;
            return p;
        }

        TableRow NewRowLike(TableRow like)
        {
            var r = new TableRow();
            foreach (var c in like.Cells)
                r.Cells.Add(new TableCell(NewCellParagraph())
                {
                    BorderBrush = c.BorderBrush, BorderThickness = c.BorderThickness, Padding = c.Padding, ColumnSpan = c.ColumnSpan, Background = c.Background
                });
            return r;
        }

        void InsertTable(int rows, int cols, bool borders = true)
        {
            var t = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 10) };
            double colW = Math.Floor((_page.BodyWidthPx - 2) / cols);
            for (int c = 0; c < cols; c++) t.Columns.Add(new TableColumn { Width = new GridLength(colW) });
            var g = new TableRowGroup();
            for (int r = 0; r < rows; r++)
            {
                var row = new TableRow();
                for (int c = 0; c < cols; c++) row.Cells.Add(NewCell(borders));
                g.Rows.Add(row);
            }
            t.RowGroups.Add(g);

            Editor.BeginChange();
            var para = Editor.CaretPosition.Paragraph;
            if (para == null) Editor.Document.Blocks.Add(t);
            else
            {
                bool empty = new TextRange(para.ContentStart, para.ContentEnd).Text.Length == 0 && !para.Inlines.Any(i => i is InlineUIContainer);
                if (empty) para.SiblingBlocks.InsertBefore(para, t);
                else
                {
                    var atEnd = Editor.CaretPosition.CompareTo(para.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)) >= 0;
                    if (atEnd)
                    {
                        var after = new Paragraph();
                        StyleDef.Get("Normal").Apply(after, false);
                        para.SiblingBlocks.InsertAfter(para, t);
                        t.SiblingBlocks.InsertAfter(t, after);
                    }
                    else
                    {
                        var np = Editor.CaretPosition.InsertParagraphBreak();
                        np.Paragraph?.SiblingBlocks.InsertBefore(np.Paragraph, t);
                    }
                }
            }
            Editor.EndChange();
            Editor.Focus();
            SelectCell(g.Rows[0].Cells[0]);
            MarkDirty(); ScheduleLayout();
        }

        void TableGrid_Click(object sender, RoutedEventArgs e) =>
            TableGridPicker.Show(BtnTable, (r, c) => InsertTable(r, c), () => InsertTableDialog_Click(null, null));

        void InsertTableDialog_Click(object sender, RoutedEventArgs e)
        {
            var d = new InsertTableDialog(this);
            if (d.ShowDialog() == true) InsertTable(d.Rows, d.Cols, d.Borders);
        }

        void InsertLink_Click(object sender, RoutedEventArgs e)
        {
            var existing = Fmt.AncestorAt<Hyperlink>(Editor.CaretPosition) ?? Fmt.AncestorAt<Hyperlink>(Editor.Selection.Start);
            if (existing != null)
            {
                var text = new TextRange(existing.ContentStart, existing.ContentEnd).Text;
                var d = new HyperlinkDialog(this, text, existing.NavigateUri?.ToString(), true, false);
                if (d.ShowDialog() != true) return;
                if (d.Remove) RemoveLink(existing);
                else existing.NavigateUri = new Uri(d.Url);
                MarkDirty();
                return;
            }
            var sel = Editor.Selection;
            bool sameParagraph = !sel.IsEmpty && sel.Start.Paragraph != null && sel.Start.Paragraph == sel.End.Paragraph;
            var dlg = new HyperlinkDialog(this, sel.IsEmpty ? "" : sel.Text, null, false, sel.IsEmpty || !sameParagraph);
            if (dlg.ShowDialog() != true) return;
            Editor.BeginChange();
            try
            {
                if (sameParagraph) new Hyperlink(sel.Start, sel.End) { NavigateUri = new Uri(dlg.Url) };
                else
                {
                    if (!sel.IsEmpty) sel.Text = "";
                    var h = new Hyperlink(new Run(dlg.LinkText), Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward)) { NavigateUri = new Uri(dlg.Url) };
                    Editor.CaretPosition = h.ElementEnd;
                }
            }
            finally { Editor.EndChange(); }
            MarkDirty(); Editor.Focus();
        }

        void RemoveLink(Hyperlink h)
        {
            Editor.BeginChange();
            var inlines = h.Inlines.ToList();
            foreach (var i in inlines)
            {
                h.Inlines.Remove(i);
                h.SiblingInlines.InsertBefore(h, i);
            }
            h.SiblingInlines.Remove(h);
            Editor.EndChange();
            MarkDirty();
        }

        void InsertText(string text)
        {
            Editor.BeginChange();
            if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
            Editor.CaretPosition.InsertTextInRun(text);
            Editor.EndChange();
            MarkDirty(); Editor.Focus();
        }

        void InsertDate_Click(object sender, RoutedEventArgs e)
        {
            var d = new DateTimeDialog(this);
            if (d.ShowDialog() == true && d.Value != null) InsertText(d.Value);
        }

        void InsertSymbol_Click(object sender, RoutedEventArgs e)
        {
            var d = new SymbolDialog(this);
            d.Insert += s => InsertText(s);
            d.ShowDialog();
        }

        void InsertPageBreak_Click(object sender, RoutedEventArgs e)
        {
            Editor.BeginChange();
            if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
            var pos = Editor.CaretPosition;
            var para = pos.Paragraph;
            if (para != null && pos.CompareTo(para.ContentStart.GetInsertionPosition(LogicalDirection.Forward)) <= 0 && para.PreviousBlock != null)
            {
                para.BreakPageBefore = true;
            }
            else
            {
                var np = pos.InsertParagraphBreak();
                if (np.Paragraph != null) np.Paragraph.BreakPageBefore = true;
                Editor.CaretPosition = np;
            }
            Editor.EndChange();
            MarkDirty(); ScheduleLayout(); Editor.Focus();
        }

        void InsertHLine_Click(object sender, RoutedEventArgs e)
        {
            Editor.BeginChange();
            var pos = Editor.CaretPosition;
            var para = pos.Paragraph;
            var rule = new Paragraph { Tag = "HRule", BorderBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), BorderThickness = new Thickness(0, 0, 0, 1.5), Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(0), FontSize = 4 };
            var after = new Paragraph();
            StyleDef.Get("Normal").Apply(after, false);
            if (para == null) { Editor.Document.Blocks.Add(rule); Editor.Document.Blocks.Add(after); }
            else
            {
                para.SiblingBlocks.InsertAfter(para, rule);
                rule.SiblingBlocks.InsertAfter(rule, after);
            }
            Editor.EndChange();
            Editor.CaretPosition = after.ContentStart;
            MarkDirty(); ScheduleLayout(); Editor.Focus();
        }

        void InsertFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Insert File", Filter = DocFormats.OpenFilter };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var doc = DocFormats.Load(dlg.FileName);
                var ms = new MemoryStream();
                new TextRange(doc.Document.ContentStart, doc.Document.ContentEnd).Save(ms, DataFormats.XamlPackage);
                ms.Position = 0;
                Editor.BeginChange();
                Editor.Selection.Load(ms, DataFormats.XamlPackage);
                Editor.EndChange();
                MarkDirty(); ScheduleLayout();
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "That file couldn't be inserted."); }
        }

        // =====================================================================================
        //  Tables
        // =====================================================================================
        (Table T, TableRowGroup G, TableRow R, TableCell C, int Col)? CurrentCell()
        {
            var c = Fmt.AncestorAt<TableCell>(Editor.CaretPosition);
            if (c == null) return null;
            var r = (TableRow)c.Parent; var g = (TableRowGroup)r.Parent; var t = (Table)g.Parent;
            int col = 0;
            foreach (var x in r.Cells) { if (x == c) break; col += Math.Max(1, x.ColumnSpan); }
            return (t, g, r, c, col);
        }


        void RowInsert(bool below)
        {
            var cur = CurrentCell(); if (cur == null) return;
            var (t, g, r, c, col) = cur.Value;
            var nr = NewRowLike(r);
            int idx = g.Rows.IndexOf(r);
            g.Rows.Insert(below ? idx + 1 : idx, nr);
            SelectCell(nr.Cells[Math.Min(r.Cells.IndexOf(c), nr.Cells.Count - 1)]);
            Editor.Selection.Select(Editor.Selection.Start, Editor.Selection.Start);
            MarkDirty(); ScheduleLayout();
        }
        void RowAbove_Click(object sender, RoutedEventArgs e) => RowInsert(false);
        void RowBelow_Click(object sender, RoutedEventArgs e) => RowInsert(true);

        void ColInsert(bool right)
        {
            var cur = CurrentCell(); if (cur == null) return;
            var (t, g, r, c, col) = cur.Value;
            int target = right ? col + Math.Max(1, c.ColumnSpan) : col;
            double width = t.Columns.Count > col && t.Columns[col].Width.IsAbsolute ? t.Columns[col].Width.Value : 96;
            // keep the table inside the page: shrink all columns proportionally
            double total = t.Columns.Sum(x => x.Width.IsAbsolute ? x.Width.Value : 96) + width;
            double scale = total > _page.BodyWidthPx ? _page.BodyWidthPx / total : 1;
            foreach (var x in t.Columns) x.Width = new GridLength(Math.Max(20, (x.Width.IsAbsolute ? x.Width.Value : 96) * scale));
            t.Columns.Insert(Math.Min(target, t.Columns.Count), new TableColumn { Width = new GridLength(Math.Max(20, width * scale)) });
            foreach (var row in t.RowGroups.SelectMany(x => x.Rows))
            {
                int pos = 0, idx = 0;
                foreach (var cell in row.Cells) { if (pos >= target) break; pos += Math.Max(1, cell.ColumnSpan); idx++; }
                var like = row.Cells.Count > 0 ? row.Cells[Math.Min(idx, row.Cells.Count - 1)] : null;
                row.Cells.Insert(Math.Min(idx, row.Cells.Count), new TableCell(NewCellParagraph())
                {
                    BorderBrush = like?.BorderBrush ?? Brushes.Black, BorderThickness = like?.BorderThickness ?? new Thickness(0.5), Padding = like?.Padding ?? new Thickness(7, 2, 7, 2)
                });
            }
            MarkDirty(); ScheduleLayout();
        }
        void ColLeft_Click(object sender, RoutedEventArgs e) => ColInsert(false);
        void ColRight_Click(object sender, RoutedEventArgs e) => ColInsert(true);

        void DelRow_Click(object sender, RoutedEventArgs e)
        {
            var cur = CurrentCell(); if (cur == null) return;
            var (t, g, r, c, col) = cur.Value;
            if (t.RowGroups.Sum(x => x.Rows.Count) <= 1) { DelTable_Click(null, null); return; }
            int idx = g.Rows.IndexOf(r);
            g.Rows.Remove(r);
            if (g.Rows.Count > 0) { SelectCell(g.Rows[Math.Min(idx, g.Rows.Count - 1)].Cells[0]); Editor.Selection.Select(Editor.Selection.Start, Editor.Selection.Start); }
            MarkDirty(); ScheduleLayout();
        }

        void DelCol_Click(object sender, RoutedEventArgs e)
        {
            var cur = CurrentCell(); if (cur == null) return;
            var (t, g, r, c, col) = cur.Value;
            int cols = Math.Max(t.Columns.Count, r.Cells.Sum(x => Math.Max(1, x.ColumnSpan)));
            if (cols <= 1) { DelTable_Click(null, null); return; }
            foreach (var row in t.RowGroups.SelectMany(x => x.Rows))
            {
                int pos = 0;
                foreach (var cell in row.Cells.ToList())
                {
                    int span = Math.Max(1, cell.ColumnSpan);
                    if (col >= pos && col < pos + span)
                    {
                        if (span > 1) cell.ColumnSpan = span - 1; else row.Cells.Remove(cell);
                        break;
                    }
                    pos += span;
                }
            }
            if (col < t.Columns.Count) t.Columns.RemoveAt(col);
            var first = t.RowGroups.SelectMany(x => x.Rows).SelectMany(x => x.Cells).FirstOrDefault();
            if (first != null) { SelectCell(first); Editor.Selection.Select(Editor.Selection.Start, Editor.Selection.Start); }
            MarkDirty(); ScheduleLayout();
        }

        void DelTable_Click(object sender, RoutedEventArgs e)
        {
            var cur = CurrentCell(); if (cur == null) return;
            var t = cur.Value.T;
            var blocks = t.SiblingBlocks;
            var next = t.NextBlock;
            if (next == null && t.PreviousBlock == null)
            {
                var p = new Paragraph(); StyleDef.Get("Normal").Apply(p, false);
                blocks.InsertAfter(t, p); next = p;
            }
            blocks.Remove(t);
            if (next != null) Editor.CaretPosition = next.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            MarkDirty(); ScheduleLayout(); Editor.Focus();
        }

        IEnumerable<TableCell> SelectedCells()
        {
            var cur = CurrentCell();
            if (cur == null) return Enumerable.Empty<TableCell>();
            var sel = Editor.Selection;
            var cells = cur.Value.T.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells)
                .Where(c => c.ContentEnd.CompareTo(sel.Start) >= 0 && c.ContentStart.CompareTo(sel.End) <= 0).ToList();
            return cells.Count > 0 ? cells : new List<TableCell> { cur.Value.C };
        }

        void CellShading_Click(object sender, RoutedEventArgs e)
        {
            var cells = SelectedCells().ToList();
            if (cells.Count == 0) return;
            var start = cells[0].Background is SolidColorBrush b ? b.Color : Colors.White;
            var c = ColorDialog.Pick(this, start);
            if (c == null) return;
            foreach (var cell in cells) { if (c.Value == Colors.White) cell.ClearValue(TableCell.BackgroundProperty); else cell.Background = new SolidColorBrush(c.Value); }
            MarkDirty();
        }

        void TableBorders_Click(object sender, RoutedEventArgs e)
        {
            var cur = CurrentCell(); if (cur == null) return;
            bool on = !(cur.Value.C.BorderThickness.Left > 0);
            foreach (var cell in cur.Value.T.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells))
            { cell.BorderBrush = Brushes.Black; cell.BorderThickness = new Thickness(on ? 0.5 : 0); }
            MarkDirty();
        }

        void ColWidth_Click(object sender, RoutedEventArgs e)
        {
            var cur = CurrentCell(); if (cur == null) return;
            var (t, g, r, c, col) = cur.Value;
            while (t.Columns.Count <= col) t.Columns.Add(new TableColumn { Width = new GridLength(96) });
            var w = t.Columns[col].Width;
            var ans = InputDialog.Ask(this, "Column Width", "Width of this column in inches:", D.In((w.IsAbsolute ? w.Value : 96) / 96));
            var v = D.ParseNum(ans);
            if (v == null || v <= 0.1 || v > 20) return;
            t.Columns[col].Width = new GridLength(v.Value * 96);
            MarkDirty(); ScheduleLayout();
        }

        // =====================================================================================
        //  Context menu
        // =====================================================================================
        void Editor_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var menu = new ContextMenu();
            MenuItem Item(string header, RoutedEventHandler click, string icon = null, bool bold = false)
            {
                var mi = new MenuItem { Header = header, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };
                if (icon != null) mi.Icon = UI.Icon(icon, 16);
                mi.Click += click;
                return mi;
            }
            var pt = Mouse.GetPosition(Editor);
            var img = ImageAt(pt);
            if (img != null)
            {
                SelectImage(img);
                menu.Items.Add(Item("Cu_t", Cut_Click, "IconCut"));
                menu.Items.Add(Item("_Copy", Copy_Click, "IconCopy"));
                menu.Items.Add(Item("_Paste", Paste_Click, "IconPaste"));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Picture _Size...", (o, a) => PictureSize(img), "IconPicture"));
                menu.Items.Add(Item("_Reset Picture Size", (o, a) =>
                {
                    if (img.Source is BitmapSource bs)
                    {
                        double w = bs.Width, h = bs.Height;
                        if (w > _page.BodyWidthPx) { h = h * _page.BodyWidthPx / w; w = _page.BodyWidthPx; }
                        img.Width = w; img.Height = h; MarkDirty(); ScheduleLayout();
                    }
                }));
                menu.Items.Add(Item("Save Picture _As...", (o, a) => SavePicture(img)));
                Editor.ContextMenu = menu;
                return;
            }

            var pos = Editor.GetPositionFromPoint(pt, true) ?? Editor.CaretPosition;
            var err = SpellCheck.GetIsEnabled(Editor) ? Editor.GetSpellingError(pos) : null;
            if (err != null)
            {
                var errRange = Editor.GetSpellingErrorRange(pos);
                var sugg = err.Suggestions.Take(6).ToList();
                if (sugg.Count == 0) menu.Items.Add(new MenuItem { Header = "(No Spelling Suggestions)", IsEnabled = false });
                foreach (var s in sugg)
                {
                    var word = s;
                    menu.Items.Add(Item(word, (o, a) => { Editor.Selection.Select(errRange.Start, errRange.End); err.Correct(word); MarkDirty(); }, null, true));
                }
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("_Ignore All", (o, a) => { err.IgnoreAll(); ScheduleLayout(); }));
                menu.Items.Add(Item("_Add to Dictionary", (o, a) => AddToDictionary(errRange?.Text), "IconSpelling"));
                menu.Items.Add(new Separator());
            }
            menu.Items.Add(Item("Cu_t", Cut_Click, "IconCut"));
            menu.Items.Add(Item("_Copy", Copy_Click, "IconCopy"));
            menu.Items.Add(Item("_Paste", Paste_Click, "IconPaste"));
            menu.Items.Add(Item("Paste as Plain Te_xt", PastePlain_Click));
            menu.Items.Add(new Separator());

            var link = Fmt.AncestorAt<Hyperlink>(pos);
            if (link != null)
            {
                menu.Items.Add(Item("_Edit Hyperlink...", InsertLink_Click, "IconLink"));
                menu.Items.Add(Item("_Open Hyperlink", (o, a) => { if (link.NavigateUri != null) FollowLink(link.NavigateUri); }));
                menu.Items.Add(Item("_Remove Hyperlink", (o, a) => RemoveLink(link)));
                menu.Items.Add(new Separator());
            }
            else menu.Items.Add(Item("_Hyperlink...", InsertLink_Click, "IconLink"));

            if (Fmt.AncestorAt<TableCell>(pos) != null)
            {
                if (Fmt.AncestorAt<TableCell>(Editor.CaretPosition) == null) Editor.CaretPosition = pos;
                var ins = new MenuItem { Header = "_Insert" };
                ins.Items.Add(Item("Row _Above", RowAbove_Click)); ins.Items.Add(Item("Row _Below", RowBelow_Click));
                ins.Items.Add(Item("Column to the _Left", ColLeft_Click)); ins.Items.Add(Item("Column to the _Right", ColRight_Click));
                var del = new MenuItem { Header = "_Delete" };
                del.Items.Add(Item("_Row", DelRow_Click)); del.Items.Add(Item("_Column", DelCol_Click)); del.Items.Add(Item("_Table", DelTable_Click));
                menu.Items.Add(ins); menu.Items.Add(del);
                menu.Items.Add(Item("Cell _Shading...", CellShading_Click));
                menu.Items.Add(new Separator());
            }
            menu.Items.Add(Item("_Font...", FontDialog_Click, "IconFont"));
            menu.Items.Add(Item("P_aragraph...", ParagraphDialog_Click, "IconParagraph"));
            var bl = new MenuItem { Header = "_Bullets and Numbering", Icon = UI.Icon("IconBullets", 16) };
            foreach (var (h, tag) in new[] { ("● Bullets", "Disc"), ("○ Hollow Bullets", "Circle"), ("■ Square Bullets", "Square"), ("1. 2. 3.", "Decimal"), ("a. b. c.", "LowerLatin"), ("i. ii. iii.", "LowerRoman"), ("Remove List", "None") })
            {
                var mi = new MenuItem { Header = h, Tag = tag };
                mi.Click += ListStyle_Click;
                bl.Items.Add(mi);
            }
            menu.Items.Add(bl);
            var styles = new MenuItem { Header = "_Styles" };
            foreach (var st in StyleDef.All) { var id = st.Id; styles.Items.Add(Item(st.Name, (o, a) => ApplyStyle(id))); }
            menu.Items.Add(styles);
            Editor.ContextMenu = menu;
        }

        void SavePicture(Image img)
        {
            if (!(img.Source is BitmapSource src)) return;
            var dlg = new SaveFileDialog { Filter = "PNG Picture|*.png|JPEG Picture|*.jpg", FileName = "Picture" };
            if (dlg.ShowDialog(this) != true) return;
            BitmapEncoder enc = dlg.FilterIndex == 2 ? new JpegBitmapEncoder { QualityLevel = 92 } : new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            try { using var fs = File.Create(dlg.FileName); enc.Save(fs); }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message); }
        }

        // ---------------- custom dictionary ----------------
        static string DictPath => System.IO.Path.Combine(Settings.Dir, "custom.lex");

        void AddCustomDictionary()
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                if (!File.Exists(DictPath)) File.WriteAllText(DictPath, "#LID 1033\r\n", System.Text.Encoding.Unicode);
                var dicts = SpellCheck.GetCustomDictionaries(Editor);
                var uri = new Uri(DictPath);
                if (!dicts.Contains(uri)) dicts.Add(uri);
            }
            catch { }
        }

        void AddToDictionary(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return;
            try
            {
                File.AppendAllText(DictPath, word.Trim() + "\r\n", System.Text.Encoding.Unicode);
                foreach (var w in Application.Current.Windows.OfType<MainWindow>())
                {
                    var dicts = SpellCheck.GetCustomDictionaries(w.Editor);
                    var uri = new Uri(DictPath);
                    dicts.Remove(uri);
                    dicts.Add(uri);
                }
            }
            catch { }
            ScheduleLayout();
        }

        // =====================================================================================
        //  Find / Replace
        // =====================================================================================
        void ShowFind(bool replace)
        {
            var initial = Editor.Selection.IsEmpty || Editor.Selection.Text.Contains("\n") ? _lastQuery : Editor.Selection.Text;
            if (_findDlg == null || !_findDlg.IsLoaded)
            {
                _findDlg = new FindReplaceDialog(this, replace, initial);
                _findDlg.Closed += (o, e) => { _findDlg = null; Activate(); Editor.Focus(); };
                _findDlg.Show();
            }
            else { _findDlg.SetMode(replace); _findDlg.FocusFind(initial); }
        }

        void Find_Click(object sender, RoutedEventArgs e) => ShowFind(false);
        void Replace_Click(object sender, RoutedEventArgs e) => ShowFind(true);
        void FindNext_Click(object sender, RoutedEventArgs e) => FindAgain(false);

        void FindAgain(bool up)
        {
            if (string.IsNullOrEmpty(_lastQuery)) { ShowFind(false); return; }
            var msg = FindNext(_lastQuery, _lastCase, _lastWhole, up);
            if (!string.IsNullOrEmpty(msg)) Flash(msg);
        }

        public string FindNext(string query, bool matchCase, bool wholeWord, bool up)
        {
            if (string.IsNullOrEmpty(query)) return null;
            _lastQuery = query; _lastCase = matchCase; _lastWhole = wholeWord;
            var search = new TextSearch(Editor.Document);
            var from = up ? Editor.Selection.Start : Editor.Selection.End;
            var hit = search.Find(from, query, matchCase, wholeWord, up, out bool wrapped);
            if (hit == null) return $"Valco Word couldn't find \"{query}\".";
            Editor.Selection.Select(hit.Start, hit.End);
            BringSelectionIntoView();
            return wrapped ? (up ? "Reached the beginning of the document; continued from the end." : "Reached the end of the document; continued from the beginning.") : "";
        }

        void BringSelectionIntoView()
        {
            var r = Editor.Selection.Start.GetCharacterRect(LogicalDirection.Forward);
            if (r.IsEmpty) return;
            var pt = Editor.TranslatePoint(new Point(r.Left, r.Top), Scroller);
            if (pt.Y < 40 || pt.Y > Scroller.ViewportHeight - 60)
                Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + pt.Y - Scroller.ViewportHeight / 3);
        }

        bool SelectionMatches(string query, bool matchCase, bool wholeWord)
        {
            var t = Editor.Selection.Text;
            if (string.IsNullOrEmpty(t)) return false;
            var rx = TextSearch.BuildRegex(query, matchCase, wholeWord);
            var m = rx.Match(t);
            return m.Success && m.Index == 0 && m.Length == t.Length;
        }

        public string ReplaceOne(string query, string replacement, bool matchCase, bool wholeWord, bool up)
        {
            if (string.IsNullOrEmpty(query)) return null;
            if (SelectionMatches(query, matchCase, wholeWord))
            {
                Editor.Selection.Text = replacement ?? "";
                Editor.Selection.Select(Editor.Selection.End, Editor.Selection.End);
                MarkDirty();
            }
            return FindNext(query, matchCase, wholeWord, up);
        }

        public int ReplaceAll(string query, string replacement, bool matchCase, bool wholeWord)
        {
            if (string.IsNullOrEmpty(query)) return 0;
            _lastQuery = query; _lastCase = matchCase; _lastWhole = wholeWord;
            var hits = new TextSearch(Editor.Document).FindAll(query, matchCase, wholeWord);
            if (hits.Count == 0) return 0;
            Editor.BeginChange();
            for (int i = hits.Count - 1; i >= 0; i--) hits[i].Text = replacement ?? "";
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
            return hits.Count;
        }

        void GoTo_Click(object sender, RoutedEventArgs e)
        {
            var ans = InputDialog.Ask(this, "Go To", $"Enter a page number (1-{_pageCount}):", _currentPage.ToString());
            if (!int.TryParse(ans, out var page)) return;
            page = Math.Max(1, Math.Min(_pageCount, page));
            double y = page == 1 ? 0 : _boundaries[page - 2].Y;
            var pos = Editor.GetPositionFromPoint(new Point(4, y + 4), true);
            if (pos != null) Editor.CaretPosition = pos;
            var pt = Editor.TranslatePoint(new Point(0, y), Scroller);
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + pt.Y - 30);
            Editor.Focus();
        }

        // =====================================================================================
        //  File menu
        // =====================================================================================
        void New_Click(object sender, RoutedEventArgs e)
        {
            var w = new MainWindow();
            if (WindowState == WindowState.Normal) { w.Left = Left + 24; w.Top = Top + 24; w.WindowStartupLocation = WindowStartupLocation.Manual; }
            w.Show();
        }

        void Open_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = DocFormats.OpenFilter, Multiselect = true,
                InitialDirectory = _path != null ? System.IO.Path.GetDirectoryName(_path) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog(this) != true) return;
            foreach (var f in dlg.FileNames) OpenFile(f);
        }

        void OpenMenu_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
            var open = new MenuItem { Header = "_Open...", InputGestureText = "Ctrl+O", Icon = UI.Icon("IconOpen", 16) };
            open.Click += Open_Click;
            menu.Items.Add(open);
            var recent = Settings.Current.Recent.Where(File.Exists).ToList();
            if (recent.Count > 0)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem { Header = "Recent Documents", IsEnabled = false, FontWeight = FontWeights.SemiBold });
                foreach (var r in recent) menu.Items.Add(RecentItem(r, recent.IndexOf(r)));
            }
            menu.IsOpen = true;
        }

        MenuItem RecentItem(string path, int i)
        {
            var mi = new MenuItem { Header = $"_{i + 1}  {System.IO.Path.GetFileName(path)}", ToolTip = path, Tag = "recent", Icon = UI.Icon("IconDoc", 16) };
            mi.Click += (o, a) => OpenFile(path);
            return mi;
        }


        void Save_Click(object sender, RoutedEventArgs e) => Save();
        void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();
        void CloseDoc_Click(object sender, RoutedEventArgs e) => Close();
        void Exit_Click(object sender, RoutedEventArgs e)
        {
            foreach (var w in Application.Current.Windows.OfType<MainWindow>().ToList())
            {
                w.Close();
                if (w.IsVisible) return;
            }
        }

        void Print_Click(object sender, RoutedEventArgs e)
        {
            try { Printing.Print(this, Editor.Document, _page, DisplayName); }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The document couldn't be printed."); }
        }

        void Preview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var w = new PreviewWindow(this, Editor.Document, _page, DisplayName, () => Print_Click(null, null));
                Mouse.OverrideCursor = null;
                w.ShowDialog();
            }
            catch (Exception ex) { Mouse.OverrideCursor = null; VistaDialog.Error(this, ex.Message, "Print Preview couldn't be shown."); }
        }

        void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Printing.ExportPdf(this, Editor.Document, _page, DisplayName))
                    Flash("Choose where to save the PDF in the window that opens.");
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The PDF couldn't be created."); }
        }

        string AskExportPath(string filter, string ext)
        {
            var dlg = new SaveFileDialog
            {
                Filter = filter, DefaultExt = ext, AddExtension = true,
                FileName = System.IO.Path.GetFileNameWithoutExtension(_path ?? SuggestedName()),
                InitialDirectory = _path != null ? System.IO.Path.GetDirectoryName(_path) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        }

        void ExportXps_Click(object sender, RoutedEventArgs e)
        {
            var path = AskExportPath("XPS Document (*.xps)|*.xps", ".xps");
            if (path == null) return;
            try { Printing.ExportXps(Editor.Document, _page, path); Flash("Exported " + System.IO.Path.GetFileName(path)); }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The XPS document couldn't be created."); }
        }

        void ExportHtml_Click(object sender, RoutedEventArgs e)
        {
            var path = AskExportPath("Web Page (*.html)|*.html;*.htm", ".html");
            if (path == null) return;
            try { File.WriteAllText(path, DocFormats.ToHtml(Editor.Document, _page, _docTitle ?? DisplayName)); Flash("Exported " + System.IO.Path.GetFileName(path)); }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The web page couldn't be created."); }
        }

        void ExportText_Click(object sender, RoutedEventArgs e)
        {
            var path = AskExportPath("Text Document (*.txt)|*.txt", ".txt");
            if (path == null) return;
            try { File.WriteAllText(path, DocFormats.PlainText(Editor.Document), new System.Text.UTF8Encoding(false)); Flash("Exported " + System.IO.Path.GetFileName(path)); }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message); }
        }

        void PageSetup_Click(object sender, RoutedEventArgs e)
        {
            var d = new PageSetupDialog(this, _page);
            if (d.ShowDialog() != true || d.Result == null) return;
            _page = d.Result;
            ApplyPage();
            MarkDirty();
        }

        List<(string, string)> Stats(TextRange range, bool includePages)
        {
            var text = range.Text;
            int words = Regex.Matches(text, @"[\p{L}\p{N}][\p{L}\p{N}'’\-]*").Count;
            int charsSpaces = text.Count(c => c != '\r' && c != '\n');
            int chars = text.Count(c => !char.IsWhiteSpace(c));
            int paras = text.Split(new[] { "\r\n" }, StringSplitOptions.None).Count(l => l.Trim().Length > 0);
            var list = new List<(string, string)>();
            if (includePages) list.Add(("Pages", _pageCount.ToString("N0")));
            list.Add(("Words", words.ToString("N0")));
            list.Add(("Characters (no spaces)", chars.ToString("N0")));
            list.Add(("Characters (with spaces)", charsSpaces.ToString("N0")));
            list.Add(("Paragraphs", paras.ToString("N0")));
            return list;
        }

        void Properties_Click(object sender, RoutedEventArgs e)
        {
            var d = new PropertiesDialog(this, _path, _docTitle, _author ?? Environment.UserName,
                Stats(new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd), true));
            if (d.ShowDialog() != true) return;
            if (d.DocTitle != (_docTitle ?? "") || d.Author != (_author ?? "")) { _docTitle = d.DocTitle; _author = d.Author; MarkDirty(); }
        }

        // =====================================================================================
        //  Edit menu
        // =====================================================================================
        void Undo_Click(object sender, RoutedEventArgs e) { if (Editor.CanUndo) Editor.Undo(); Editor.Focus(); }
        void Redo_Click(object sender, RoutedEventArgs e) { if (Editor.CanRedo) Editor.Redo(); Editor.Focus(); }
        void Cut_Click(object sender, RoutedEventArgs e) { Editor.Cut(); RememberClipboard(); Editor.Focus(); }
        void Copy_Click(object sender, RoutedEventArgs e) { Editor.Copy(); RememberClipboard(); Editor.Focus(); }
        void Paste_Click(object sender, RoutedEventArgs e) { Editor.Paste(); Editor.Focus(); }
        void Clear_Click(object sender, RoutedEventArgs e) { if (!Editor.Selection.IsEmpty) Editor.Selection.Text = ""; Editor.Focus(); }
        void SelectAll_Click(object sender, RoutedEventArgs e) { Editor.SelectAll(); Editor.Focus(); }

        void PastePlain_Click(object sender, RoutedEventArgs e)
        {
            string text;
            try { text = Clipboard.GetText(); } catch { return; }
            if (string.IsNullOrEmpty(text)) return;
            Editor.BeginChange();
            Editor.Selection.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            Editor.Selection.Select(Editor.Selection.End, Editor.Selection.End);
            Editor.EndChange();
            MarkDirty(); Editor.Focus();
        }

        // =====================================================================================
        //  View / Tools / Help
        // =====================================================================================





        void NextSpelling_Click(object sender, RoutedEventArgs e)
        {
            if (!SpellCheck.GetIsEnabled(Editor))
            {
                if (VistaDialog.Confirm(this, "Spell checking is turned off. Turn it on?")) { Settings.Current.SpellCheck = true; Settings.Save(); ApplyViewSettings(); }
                return;
            }
            var pos = Editor.GetNextSpellingErrorPosition(Editor.Selection.End, LogicalDirection.Forward)
                      ?? Editor.GetNextSpellingErrorPosition(Editor.Document.ContentStart, LogicalDirection.Forward);
            if (pos == null) { VistaDialog.Show(this, "The spelling check is complete. No errors were found.", "Valco Word", MessageBoxButton.OK, DlgIcon.Info); return; }
            var r = Editor.GetSpellingErrorRange(pos);
            Editor.Selection.Select(r.Start, r.End);
            BringSelectionIntoView();
            Editor.Focus();
        }

        void WordCount_Click(object sender, RoutedEventArgs e)
        {
            var sel = Editor.Selection;
            if (!sel.IsEmpty && sel.Text.Trim().Length > 0) InfoDialogs.WordCount(this, Stats(sel, false), "Selected text");
            else InfoDialogs.WordCount(this, Stats(new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd), true), "Whole document");
        }


        void Shortcuts_Click(object sender, RoutedEventArgs e) => InfoDialogs.Shortcuts(this);
        void About_Click(object sender, RoutedEventArgs e) => InfoDialogs.About(this);
    }
}
