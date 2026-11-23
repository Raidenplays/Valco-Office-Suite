using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ValcoWord
{
    public partial class MainWindow
    {
        // =====================================================================================
        //  One-time wiring for the features in the partial files
        // =====================================================================================
        void InitFeatures()
        {
            Loaded += (s, e) => Updates.CheckOnStartup(this, "word", "Valco Word");
            Editor.PreviewTextInput += (s, e) =>
            {
                if (_recording != null && e.Text.Length > 0 && !char.IsControl(e.Text[0])) _recording.Add("type:" + e.Text);
                if (_ex.TrackChanges && !Editor.IsReadOnly && e.Text.Length > 0 && !char.IsControl(e.Text[0]) && e.Text != "\r")
                {
                    TrackedInsert(e.Text);
                    e.Handled = true;
                }
            };
            Editor.PreviewKeyDown += (s, e) =>
            {
                if (TrackKey(e)) { e.Handled = true; return; }
                if (e.Key == Key.F7 && Keyboard.Modifiers == ModifierKeys.Shift) { ShowResearchPane("Thesaurus"); e.Handled = true; }
                else if (e.Key == Key.F7 && Keyboard.Modifiers == ModifierKeys.None) { SpellingAndGrammar(); e.Handled = true; }
                else if (e.Key == Key.F9 && Keyboard.Modifiers == ModifierKeys.None) { UpdateFields(); e.Handled = true; }
                else if (e.Key == Key.E && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { SetTrackChanges(!_ex.TrackChanges); e.Handled = true; }
                else if (e.Key == Key.F && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt)) { InsertNote(false); e.Handled = true; }
                else if (e.Key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt)) { InsertNote(true); e.Handled = true; }
                else if (e.Key == Key.M && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt)) { NewComment(); e.Handled = true; }
                else if (e.Key == Key.X && Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift)) { MarkEntry("XE"); e.Handled = true; }
                else if (e.Key == Key.I && Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift)) { MarkEntry("TA"); e.Handled = true; }
                else if (e.SystemKey == Key.OemPlus && Keyboard.Modifiers == ModifierKeys.Alt) { EquationEditor(); e.Handled = true; }
                else if (_view == "outline" && Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && (e.SystemKey == Key.Left || e.SystemKey == Key.Right)) { ChangeOutlineLevel(e.SystemKey == Key.Left ? -1 : 1); e.Handled = true; }
                else if (_view == "outline" && Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && (e.SystemKey == Key.Up || e.SystemKey == Key.Down)) { MoveParagraph(e.SystemKey == Key.Up ? -1 : 1); e.Handled = true; }
            };
            _layoutTimer.Tick += (s, e) =>
            {
                if (NotesSection() != null) RenumberNotes();
                if (_leftPaneKind == "map") RefreshDocumentMap();
                else if (_leftPaneKind == "thumbs") RefreshThumbnails();
                if (_mirrorRefresh != null) _mirrorRefresh();
            };
            Scroller.SizeChanged += (s, e) => { if (_view == "web" || _view == "draft" || _view == "outline") ApplyViewLayout(); };
            Scroller.ScrollChanged += (s, e) => SyncSideBySide();
            Activated += (s, e) => _lastActive = this;
        }

        static MainWindow _lastActive;
        string _view = "print";
        DocExtras _ex = new DocExtras();
        bool _syncScroll, _gridlines, _convertible;
        Button _formatPainterButton;

        /// <summary>Called after a document is loaded so the window reflects its saved state.</summary>
        void ApplyExtras()
        {
            _noteLinks.Clear();
            if (TbTrack != null) TbTrack.IsChecked = _ex.TrackChanges;
            if (TrackStatusText != null) TrackStatusText.Text = "Track Changes: " + (_ex.TrackChanges ? "On" : "Off");
            if (CbBibStyle != null) { _updatingUI = true; CbBibStyle.SelectedItem = _ex.BibStyle; _updatingUI = false; }
            if (TbPreviewMerge != null) TbPreviewMerge.IsChecked = _ex.MergePreview;
            _markupMode = 0;
            foreach (var s in Spans.WithTag<RevInfo>(Editor.Document)) StyleRevision(s);
            foreach (var s in CommentSpans()) s.Background = new SolidColorBrush(CommentColor);
            _nextCommentId = Spans.Tagged<CommentInfo>(Editor.Document).Select(c => c.Id).DefaultIfEmpty(0).Max() + 1;
            ApplyProtection();
            if (CommentSpans().Any()) ShowCommentsPane();
            else if (_rightPaneKind == "comments") HideRightPane();
        }

        // =====================================================================================
        //  Document views
        // =====================================================================================
        void SetView(string view)
        {
            if (view == "full") { FullScreenReading(); return; }
            _view = view;
            foreach (var tb in new[] { TbPrintLayout, TbFullScreen, TbWebLayout, TbOutline, TbDraft }) if (tb != null) tb.IsChecked = (string)tb.Tag == view;
            foreach (var b in ViewButtons.Children.OfType<ToggleButton>()) b.IsChecked = (string)b.Tag == view;
            ApplyViewLayout();
            ApplyViewSettings();
            UpdateContextualTabs();
            if (view == "outline") Ribbon.Select(TabOutline);
            else if (Ribbon.Selected == TabOutline) Ribbon.Select(TabHome);
            DrawOverlay();
        }

        void ApplyViewLayout()
        {
            switch (_view)
            {
                case "web":
                    {
                        double w = Math.Max(300, Scroller.ViewportWidth / Zoom - 60);
                        Page.Width = w; Page.Margin = new Thickness(0);
                        Editor.Margin = new Thickness(24, 16, 24, 16); Editor.Width = w - 48; Editor.MinHeight = Math.Max(200, Scroller.ViewportHeight / Zoom - 40);
                        PageGrid.MinHeight = 0; Page.Effect = null; Page.BorderThickness = new Thickness(0);
                        break;
                    }
                case "draft":
                case "outline":
                    {
                        double w = Math.Max(300, Scroller.ViewportWidth / Zoom - 30);
                        Page.Width = w; Page.Margin = new Thickness(0);
                        Editor.Margin = new Thickness(_view == "outline" ? 40 : 16, 10, 16, 10); Editor.Width = _view == "outline" ? Math.Min(w - 56, _page.BodyWidthPx + 200) : _page.BodyWidthPx;
                        Editor.HorizontalAlignment = HorizontalAlignment.Left;
                        Editor.MinHeight = Math.Max(200, Scroller.ViewportHeight / Zoom - 30);
                        PageGrid.MinHeight = 0; Page.Effect = null; Page.BorderThickness = new Thickness(0);
                        break;
                    }
                default:
                    Page.Margin = new Thickness(24, 20, 24, 32);
                    Page.Effect = (System.Windows.Media.Effects.Effect)FindResource("PageShadow");
                    Page.BorderThickness = new Thickness(0.5);
                    Editor.HorizontalAlignment = HorizontalAlignment.Stretch;
                    ApplyPage();
                    break;
            }
            ScheduleLayout();
        }

        /// <summary>Overlay for draft and outline views (called from DrawOverlay's caller chain).</summary>
        void DrawViewOverlay()
        {
            if (_view == "outline")
            {
                foreach (var p in Fmt.AllParagraphs(Editor.Document.Blocks))
                {
                    var r = p.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                    if (r.IsEmpty) continue;
                    int lvl = HeadingLevel(p);
                    var sym = lvl > 0
                        ? (FrameworkElement)new Grid { Width = 11, Height = 11, Children = { new Ellipse { Stroke = Brushes.DimGray, Fill = Brushes.White, StrokeThickness = 1 }, new TextBlock { Text = "+", FontSize = 10, Margin = new Thickness(0, -3, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.DimGray } } }
                        : new Ellipse { Width = 6, Height = 6, Fill = Brushes.Gray };
                    Canvas.SetLeft(sym, Editor.Margin.Left - (lvl > 0 ? 22 : 18) + (lvl > 1 ? 0 : 0));
                    Canvas.SetTop(sym, Editor.Margin.Top + r.Top + (r.Height - (lvl > 0 ? 11 : 6)) / 2);
                    Overlay.Children.Add(sym);
                }
            }
            else if (_view == "draft")
            {
                foreach (var (y, manual) in _boundaries)
                {
                    double yy = Math.Round(Editor.Margin.Top + y) + 0.5;
                    Overlay.Children.Add(new Line { X1 = Editor.Margin.Left, X2 = Editor.Margin.Left + Editor.Width, Y1 = yy, Y2 = yy, Stroke = Brushes.Gray, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { manual ? 1 : 4, manual ? 2 : 4 } });
                }
            }
        }

        void FullScreenReading()
        {
            var w = new VistaWindow { Title = DisplayName + " - Full Screen Reading", WindowState = WindowState.Maximized, Owner = this, ShowInTaskbar = false };
            var doc = Printing.PrintCopy(Editor.Document, _page);
            doc.ColumnWidth = 520; doc.PagePadding = new Thickness(56, 48, 56, 48);
            var reader = new FlowDocumentReader { Document = doc, ViewingMode = FlowDocumentReaderViewingMode.TwoPage, IsFindEnabled = true, IsPrintEnabled = false, Background = (Brush)FindResource("Rb.Workspace") };
            var top = new DockPanel { Background = (Brush)FindResource("Rb.TabStrip"), Height = 34 };
            var close = UI.Btn("Close", (s, e) => w.Close());
            close.Margin = new Thickness(4); DockPanel.SetDock(close, Dock.Right); top.Children.Add(close);
            var opts = UI.Btn("View Options", null);
            opts.Margin = new Thickness(4); DockPanel.SetDock(opts, Dock.Right);
            opts.Click += (s, e) => RB.OpenMenu(opts, RB.Menu(
                RB.MI("Increase Text Size", () => reader.Zoom = Math.Min(200, reader.Zoom + 10)), RB.MI("Decrease Text Size", () => reader.Zoom = Math.Max(50, reader.Zoom - 10)),
                null, RB.MI("Show One Page", () => reader.ViewingMode = FlowDocumentReaderViewingMode.Page), RB.MI("Show Two Pages", () => reader.ViewingMode = FlowDocumentReaderViewingMode.TwoPage),
                RB.MI("Continuous Scroll", () => reader.ViewingMode = FlowDocumentReaderViewingMode.Scroll)));
            top.Children.Add(opts);
            var title = new TextBlock { Text = DisplayName, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), FontWeight = FontWeights.SemiBold };
            top.Children.Add(title);
            var dp = new DockPanel(); DockPanel.SetDock(top, Dock.Top); dp.Children.Add(top); dp.Children.Add(reader);
            w.Content = dp;
            w.KeyDown += (s, e) => { if (e.Key == Key.Escape) w.Close(); };
            w.ShowDialog();
        }

        void ToggleFullScreen()
        {
            _fullScreen = !_fullScreen;
            Ribbon.Visibility = _fullScreen ? Visibility.Collapsed : Visibility.Visible;
            StatusBar.Visibility = _fullScreen ? Visibility.Collapsed : Visibility.Visible;
            TheRuler.Visibility = _fullScreen ? Visibility.Collapsed : (Settings.Current.ShowRuler && _view != "web" ? Visibility.Visible : Visibility.Collapsed);
            if (_fullScreen) { _preFullState = WindowState; WindowState = WindowState.Maximized; Flash("Press Esc to exit full screen."); }
            else WindowState = _preFullState;
        }
        WindowState _preFullState;

        // ---------------- outline tools ----------------
        void SetOutlineLevel(int level)
        {
            Editor.BeginChange();
            foreach (var p in Fmt.SelectedParagraphs(Editor)) StyleDef.Get(level <= 0 ? "Normal" : "Heading" + Math.Min(3, level)).Apply(p, true);
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        void ChangeOutlineLevel(int delta)
        {
            Editor.BeginChange();
            foreach (var p in Fmt.SelectedParagraphs(Editor))
            {
                int l = HeadingLevel(p);
                int nl = l == 0 ? (delta < 0 ? 3 : 0) : l + delta;
                if (nl > 3) nl = 0;
                if (nl < 1 && l > 0 && delta < 0) nl = 1;
                StyleDef.Get(nl <= 0 ? "Normal" : "Heading" + nl).Apply(p, true);
            }
            Editor.EndChange();
            MarkDirty(); ScheduleLayout();
        }

        void MoveParagraph(int dir)
        {
            var paras = Fmt.SelectedParagraphs(Editor);
            if (paras.Count == 0) return;
            var first = paras[0]; var last = paras[paras.Count - 1];
            if (!(first.Parent is FlowDocument || first.Parent is Section) || first.Parent != last.Parent) { Flash("Only body paragraphs can be moved."); return; }
            var coll = first.SiblingBlocks;
            var other = dir < 0 ? first.PreviousBlock : last.NextBlock;
            if (other == null) return;
            Editor.BeginChange();
            coll.Remove(other);
            if (dir < 0) coll.InsertAfter(last, other); else coll.InsertBefore(first, other);
            Editor.EndChange();
            Editor.Selection.Select(first.ContentStart, last.ContentEnd);
            MarkDirty(); ScheduleLayout();
        }

        // =====================================================================================
        //  Document map and thumbnails
        // =====================================================================================
        void ShowLeftPane(string kind)
        {
            if (CkDocMap != null) CkDocMap.IsChecked = kind == "map";
            if (CkThumbs != null) CkThumbs.IsChecked = kind == "thumbs";
            if (kind == null)
            {
                _leftPaneKind = null;
                LeftPane.Child = null; LeftPane.Visibility = Visibility.Collapsed; LeftSplitter.Visibility = Visibility.Collapsed; LeftCol.Width = new GridLength(0);
                return;
            }
            if (kind == "map") { ShowLeftPaneContent("map", "Document Map", new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); RefreshDocumentMap(); }
            else { ShowLeftPaneContent("thumbs", "Thumbnails", new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); RefreshThumbnails(); }
        }

        ScrollViewer LeftPaneScroller => (LeftPane.Child as DockPanel)?.Children.OfType<ScrollViewer>().FirstOrDefault();

        void RefreshDocumentMap()
        {
            var sv = LeftPaneScroller; if (sv == null) return;
            var heads = Fmt.AllParagraphs(Editor.Document.Blocks).Where(p => HeadingLevel(p) > 0 || (p.Tag as string) == "Title").ToList();
            var sig = string.Join("|", heads.Select(h => HeadingLevel(h) + Spans.Text(h)));
            if (sv.Tag as string == sig) return;
            sv.Tag = sig;
            var sp = new StackPanel { Margin = new Thickness(2) };
            foreach (var h in heads)
            {
                var para = h; int lvl = Math.Max(1, HeadingLevel(h));
                var b = new Button { Style = RB.BtnStyle, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(4 + 12 * (lvl - 1), 2, 4, 2),
                    Content = new TextBlock { Text = Spans.Text(h).Trim().Length == 0 ? "(blank heading)" : Spans.Text(h).Trim(), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = lvl == 1 ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Brushes.Black } };
                b.Click += (s, e) => { GoToPosition(para.ContentStart); };
                sp.Children.Add(b);
            }
            if (heads.Count == 0) sp.Children.Add(Note("This document doesn't contain any headings. Apply Heading styles to show them here."));
            sv.Content = sp;
        }

        void RefreshThumbnails()
        {
            var sv = LeftPaneScroller; if (sv == null) return;
            var sig = _pageCount + ":" + string.Join(",", _boundaries.Select(b => (int)b.Y));
            var sp = sv.Content as StackPanel;
            if (sv.Tag as string == sig && sp != null) return;
            sv.Tag = sig;
            sp = new StackPanel { Margin = new Thickness(6) };
            double w = _page.WidthPx, h = _page.HeightPx, top = Editor.Margin.Top;
            var starts = new List<double> { 0 };
            starts.AddRange(_boundaries.Select(b => top + b.Y - _page.MarginTop * 96));
            for (int i = 0; i < starts.Count; i++)
            {
                double y0 = Math.Max(0, starts[i]);
                var brush = new VisualBrush(PageGrid) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, y0, w, h), Stretch = Stretch.Uniform, AlignmentY = AlignmentY.Top };
                var thumbW = Math.Max(80, LeftCol.ActualWidth - 50);
                var rect = new Rectangle { Width = thumbW, Height = thumbW * h / w, Fill = Brushes.White };
                var page = new Grid { Width = thumbW, Height = thumbW * h / w };
                page.Children.Add(new Rectangle { Fill = Brushes.White });
                page.Children.Add(new Rectangle { Fill = brush });
                int pg = i;
                var b = new Button { Style = RB.BtnStyle, Padding = new Thickness(3), Margin = new Thickness(0, 0, 0, 2), HorizontalContentAlignment = HorizontalAlignment.Center,
                    Content = new StackPanel { Children = { new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = page, Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 1, Opacity = .3, BlurRadius = 3 } }, new TextBlock { Text = (i + 1).ToString(), HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.DimGray } } } };
                b.Click += (s, e) => { var y = starts[pg]; Scroller.ScrollToVerticalOffset(Math.Max(0, (y + 20) * Zoom)); };
                sp.Children.Add(b);
            }
            sv.Content = sp;
        }

        // =====================================================================================
        //  Window group: new window, arrange, split, side by side, switch
        // =====================================================================================
        Action _mirrorRefresh;

        void OpenMirrorWindow()
        {
            var w = new VistaWindow { Title = DisplayName + ":2 - Valco Word", Width = Width * 0.8, Height = Height * 0.8, Owner = null };
            var viewer = new FlowDocumentScrollViewer { IsToolBarVisible = false, Background = (Brush)FindResource("Rb.Workspace"), Zoom = Zoom * 100 };
            var info = new Border { Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xC9)), Padding = new Thickness(8, 3, 8, 3), Child = new TextBlock { Text = "A live, read-only view of the document. Edit in the main window." } };
            var dp = new DockPanel(); DockPanel.SetDock(info, Dock.Top); dp.Children.Add(info); dp.Children.Add(viewer);
            w.Content = dp;
            void Refresh()
            {
                try
                {
                    var off = (viewer.Template?.FindName("PART_ContentHost", viewer) as ScrollViewer)?.VerticalOffset ?? 0;
                    var doc = CloneDocument();
                    doc.PagePadding = new Thickness(_page.MarginLeft * 96, 24, _page.MarginRight * 96, 24); doc.MaxPageWidth = _page.WidthPx; doc.Background = Brushes.White;
                    viewer.Document = doc;
                    Dispatcher.BeginInvoke(new Action(() => (viewer.Template?.FindName("PART_ContentHost", viewer) as ScrollViewer)?.ScrollToVerticalOffset(off)), DispatcherPriority.Loaded);
                }
                catch { }
            }
            Refresh();
            Action handler = Refresh;
            _mirrorRefresh += handler;
            w.Closed += (s, e) => _mirrorRefresh -= handler;
            Closed += (s, e) => { try { w.Close(); } catch { } };
            w.Show();
        }

        FlowDocument CloneDocument()
        {
            var ms = new MemoryStream();
            new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Save(ms, DataFormats.XamlPackage);
            ms.Position = 0;
            var fd = new FlowDocument();
            PrepareDocument(fd);
            new TextRange(fd.ContentStart, fd.ContentEnd).Load(ms, DataFormats.XamlPackage);
            return fd;
        }

        static List<Window> DocWindows => Application.Current.Windows.OfType<Window>().Where(w => w is MainWindow || (w.Title?.EndsWith(":2 - Valco Word") ?? false)).ToList();

        void ArrangeAll()
        {
            var wins = DocWindows;
            var wa = SystemParameters.WorkArea;
            double h = wa.Height / wins.Count;
            for (int i = 0; i < wins.Count; i++)
            {
                var w = wins[i];
                w.WindowState = WindowState.Normal;
                w.Left = wa.Left; w.Top = wa.Top + i * h; w.Width = wa.Width; w.Height = h;
            }
        }

        Grid _splitHost; FlowDocumentScrollViewer _splitView;

        void ToggleSplit()
        {
            if (_splitHost != null)
            {
                _mirrorRefresh -= RefreshSplit;
                _splitHost.Children.Remove(Scroller);
                WorkArea.Children.Remove(_splitHost);
                Grid.SetColumn(Scroller, 2); Grid.SetRow(Scroller, 0);
                WorkArea.Children.Add(Scroller);
                _splitHost = null; _splitView = null;
                return;
            }
            WorkArea.Children.Remove(Scroller);
            _splitHost = new Grid();
            _splitHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _splitHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _splitHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.4, GridUnitType.Star) });
            _splitView = new FlowDocumentScrollViewer { IsToolBarVisible = false, Background = (Brush)FindResource("Rb.Workspace"), Zoom = Zoom * 100 };
            var splitter = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xD8, 0xEE)), ResizeDirection = GridResizeDirection.Rows, ToolTip = "Drag to resize. Click Split again to remove." };
            Grid.SetRow(_splitView, 0); Grid.SetRow(splitter, 1); Grid.SetRow(Scroller, 2);
            _splitHost.Children.Add(_splitView); _splitHost.Children.Add(splitter); _splitHost.Children.Add(Scroller);
            Grid.SetColumn(_splitHost, 2);
            WorkArea.Children.Add(_splitHost);
            RefreshSplit();
            _mirrorRefresh += RefreshSplit;
        }

        void RefreshSplit()
        {
            if (_splitView == null) return;
            var sv = _splitView.Template?.FindName("PART_ContentHost", _splitView) as ScrollViewer;
            var off = sv?.VerticalOffset ?? 0;
            var doc = CloneDocument();
            doc.PagePadding = new Thickness(_page.MarginLeft * 96, 16, _page.MarginRight * 96, 16); doc.MaxPageWidth = _page.WidthPx; doc.Background = Brushes.White;
            _splitView.Document = doc;
            Dispatcher.BeginInvoke(new Action(() => (_splitView?.Template?.FindName("PART_ContentHost", _splitView) as ScrollViewer)?.ScrollToVerticalOffset(off)), DispatcherPriority.Loaded);
        }

        MainWindow _sidePartner;

        void ViewSideBySide()
        {
            var others = Application.Current.Windows.OfType<MainWindow>().Where(w => w != this).ToList();
            if (others.Count == 0) { Flash("Open another document to view it side by side."); return; }
            MainWindow other = others.Count == 1 ? others[0] : null;
            if (other == null)
            {
                var d = new DialogBase(this, "Compare Side by Side", 340);
                var list = new ListBox { Height = 140, ItemsSource = others.Select(o => o.DisplayName).ToList(), SelectedIndex = 0 };
                var sp = new StackPanel(); sp.Children.Add(new TextBlock { Text = "Compare side by side with:" }); sp.Children.Add(list);
                d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
                if (d.ShowDialog() != true || list.SelectedIndex < 0) return;
                other = others[list.SelectedIndex];
            }
            _sidePartner = other; other._sidePartner = this;
            _syncScroll = other._syncScroll = true;
            var wa = SystemParameters.WorkArea;
            foreach (var (w, i) in new[] { (this, 0), (other, 1) })
            {
                w.WindowState = WindowState.Normal;
                w.Left = wa.Left + i * wa.Width / 2; w.Top = wa.Top; w.Width = wa.Width / 2; w.Height = wa.Height;
            }
            Flash("Synchronous scrolling is on.");
        }

        bool _syncing;
        void SyncSideBySide()
        {
            if (!_syncScroll || _sidePartner == null || _syncing || !_sidePartner.IsLoaded) return;
            _sidePartner._syncing = true;
            try { _sidePartner.Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset); }
            finally { _sidePartner._syncing = false; }
        }

        ContextMenu SwitchWindowsMenu()
        {
            var m = RB.Menu();
            int i = 1;
            foreach (var w in DocWindows)
            {
                var win = w;
                var title = w is MainWindow mw ? mw.DisplayName : w.Title;
                m.Items.Add(RB.MI($"{i++} {title}", () => { if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal; win.Activate(); }, null, null, win == this));
            }
            return m;
        }

        // =====================================================================================
        //  Macros
        // =====================================================================================
        List<string> _recording;
        string _recordingName;

        void RecordCommand(string name)
        {
            if (_recording != null && name != "RecordMacro") _recording.Add(name);
        }

        static IEnumerable<string> MacroNames => Settings.Current.Custom.Keys.Where(k => k.StartsWith("macro:")).Select(k => k.Substring(6)).OrderBy(k => k);

        ContextMenu MacrosMenu()
        {
            var m = RB.Menu();
            m.Items.Add(RB.MI("View Macros...", ViewMacros, "IconMacro", "Alt+F8"));
            if (_recording == null) m.Items.Add(RB.MI("Record Macro...", StartRecording));
            else m.Items.Add(RB.MI("Stop Recording", StopRecording));
            var run = new MenuItem { Header = "Run" };
            foreach (var n in MacroNames) { var name = n; run.Items.Add(RB.MI(name, () => RunMacro(name))); }
            if (run.Items.Count > 0) { m.Items.Add(new Separator()); m.Items.Add(run); }
            return m;
        }

        void StartRecording()
        {
            var name = InputDialog.Ask(this, "Record Macro", "Macro name:", "Macro" + (MacroNames.Count() + 1));
            if (string.IsNullOrWhiteSpace(name)) return;
            _recordingName = new string(name.Trim().Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
            if (_recordingName.Length == 0) _recordingName = "Macro1";
            _recording = new List<string>();
            StatusMsg.Text = "Recording macro \"" + _recordingName + "\"… (View ▸ Macros ▸ Stop Recording)";
        }

        void StopRecording()
        {
            if (_recording == null) return;
            // merge consecutive typing into one step
            var steps = new List<string>();
            foreach (var s in _recording)
            {
                if (s.StartsWith("type:") && steps.Count > 0 && steps[^1].StartsWith("type:")) steps[^1] += s.Substring(5);
                else steps.Add(s);
            }
            Settings.Current.Custom["macro:" + _recordingName] = string.Join("\u001f", steps);
            Settings.Save();
            Flash($"Macro \"{_recordingName}\" recorded ({steps.Count} step{(steps.Count == 1 ? "" : "s")}).");
            StatusMsg.Text = _statusNote ?? "";
            _recording = null;
        }

        void RunMacro(string name)
        {
            if (!Settings.Current.Custom.TryGetValue("macro:" + name, out var body)) return;
            var wasRec = _recording; _recording = null;
            try
            {
                foreach (var step in body.Split('\u001f'))
                {
                    if (step.StartsWith("type:")) { var text = step.Substring(5); if (_ex.TrackChanges) TrackedInsert(text); else InsertText(text); }
                    else if (_commands.TryGetValue(step, out var act)) act();
                }
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The macro stopped because of an error."); }
            finally { _recording = wasRec; }
        }

        void ViewMacros()
        {
            var d = new DialogBase(this, "Macros", 460);
            var list = new ListBox { Height = 180, Width = 260 };
            var steps = new TextBox { Height = 180, Width = 160, AcceptsReturn = true, IsReadOnly = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 11 };
            void Fill() => list.ItemsSource = MacroNames.ToList();
            list.SelectionChanged += (s, e) => { if (list.SelectedItem is string n && Settings.Current.Custom.TryGetValue("macro:" + n, out var b)) steps.Text = string.Join("\n", b.Split('\u001f').Select(x => x.StartsWith("type:") ? "Type \"" + x.Substring(5) + "\"" : x)); };
            Fill();
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(list); var r2 = new StackPanel { Margin = new Thickness(8, 0, 0, 0) }; r2.Children.Add(new TextBlock { Text = "Steps:" }); r2.Children.Add(steps); row.Children.Add(r2);
            var sp = new StackPanel(); sp.Children.Add(new TextBlock { Text = "Macro name:" }); sp.Children.Add(row);
            var run = UI.Btn("Run", (s, e) => { if (list.SelectedItem is string n) { d.Close(); RunMacro(n); } }, true);
            var del = UI.Btn("Delete", (s, e) => { if (list.SelectedItem is string n && VistaDialog.Confirm(d, $"Do you want to delete macro {n}?")) { Settings.Current.Custom.Remove("macro:" + n); Settings.Save(); Fill(); steps.Text = ""; } });
            var rec = UI.Btn(_recording == null ? "Record..." : "Stop Recording", (s, e) => { d.Close(); if (_recording == null) StartRecording(); else StopRecording(); });
            d.Content = UI.DialogLayout(sp, UI.Strip(run, del, rec, UI.Btn("Close", null, false, true)));
            d.ShowDialog();
        }

        // =====================================================================================
        //  Office menu: quick print, templates, convert, options, e-mail, formats
        // =====================================================================================
        void QuickPrint()
        {
            try { Printing.Print(this, Editor.Document, _page, DisplayName, false); Flash("Sent to the default printer."); }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The document couldn't be printed."); }
        }

        static string TemplatesDir
        {
            get
            {
                var d = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Valco", "Valco Word", "Templates");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        static readonly (string Name, string Category, string Desc)[] BuiltInTemplates =
        {
            ("Blank document", "Blank and recent", "A new, empty document."),
            ("Letter", "Letters", "A business letter with your address, the date and a closing."),
            ("Memo", "Memos", "An interoffice memo with To, From, Date and Subject lines."),
            ("Fax Cover Sheet", "Faxes", "A fax cover sheet with sender and recipient details."),
            ("Resume", "Resumes", "A clean resume with sections for experience, education and skills."),
            ("Report", "Reports", "A report with a cover page, table of contents and headings."),
            ("Agenda", "Agendas", "A meeting agenda with times and topics."),
            ("Invoice", "Invoices", "A simple invoice with an itemized table and total."),
        };

        void NewFromTemplate()
        {
            var d = new DialogBase(this, "New Document", 640);
            var cats = new ListBox { Width = 160, Height = 300 };
            var mine = Directory.GetFiles(TemplatesDir).Where(f => f.EndsWith(".dotx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)).ToList();
            var catNames = new List<string> { "Blank and recent", "Installed Templates" };
            if (mine.Count > 0) catNames.Add("My templates");
            catNames.Add("New from existing...");
            cats.ItemsSource = catNames;
            var tiles = new WrapPanel { Width = 440 };
            var desc = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 0), MinHeight = 34 };
            string chosen = "Blank document"; string chosenFile = null;
            Button TileFor(string name, string file, string description)
            {
                var vis = new StackPanel { Width = 92 };
                vis.Children.Add(new Border { Width = 70, Height = 90, Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = TemplateThumb(name), Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 1, Opacity = .3, BlurRadius = 3 } });
                vis.Children.Add(new TextBlock { Text = name, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Foreground = Brushes.Black });
                var b = RB.Tile(vis, description, () => { chosen = name; chosenFile = file; desc.Text = description; }, 100, 130);
                b.MouseDoubleClick += (s, e) => { chosen = name; chosenFile = file; d.DialogResult = true; };
                return b;
            }
            cats.SelectionChanged += (s, e) =>
            {
                tiles.Children.Clear();
                switch (cats.SelectedItem as string)
                {
                    case "Blank and recent":
                        tiles.Children.Add(TileFor("Blank document", null, BuiltInTemplates[0].Desc));
                        foreach (var r in Settings.Current.Recent.Where(File.Exists).Where(f => f.EndsWith(".dotx", StringComparison.OrdinalIgnoreCase)).Take(6)) tiles.Children.Add(TileFor(System.IO.Path.GetFileNameWithoutExtension(r), r, r));
                        break;
                    case "Installed Templates":
                        foreach (var t in BuiltInTemplates.Skip(1)) tiles.Children.Add(TileFor(t.Name, null, t.Desc));
                        break;
                    case "My templates":
                        foreach (var f in mine) tiles.Children.Add(TileFor(System.IO.Path.GetFileNameWithoutExtension(f), f, f));
                        break;
                    case "New from existing...":
                        {
                            var o = new OpenFileDialog { Title = "New from Existing Document", Filter = DocFormats.OpenFilter };
                            if (o.ShowDialog(d) == true) { chosen = null; chosenFile = o.FileName; d.DialogResult = true; }
                            else cats.SelectedIndex = 0;
                            break;
                        }
                }
            };
            cats.SelectedIndex = 0;
            var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            right.Children.Add(new ScrollViewer { Content = tiles, Height = 270, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            right.Children.Add(desc);
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(cats); row.Children.Add(right);
            var openFolder = UI.Btn("Templates Folder", (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", TemplatesDir); } catch { } });
            d.Content = UI.DialogLayout(row, UI.Strip(UI.Btn("Create", (s, e) => d.DialogResult = true, true), openFolder, UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            var target = IsBlank ? this : OpenNewWindow();
            if (chosenFile != null)
            {
                try
                {
                    var doc = DocFormats.Load(chosenFile);
                    target.SetDocument(doc.Document, doc.Page, null, doc.Title, doc.Author, doc.Extras);
                    target._displayName = "Document" + (++_docCounter);
                    target.UpdateTitle();
                }
                catch (Exception ex) { VistaDialog.Error(this, ex.Message, "That template couldn't be opened."); }
                return;
            }
            if (chosen == "Blank document" || chosen == null) { if (target != this) return; NewDocument(); return; }
            target.BuildTemplate(chosen);
        }

        static UIElement TemplateThumb(string name)
        {
            var c = new Canvas { Width = 70, Height = 90 };
            void L(double x, double y, double w, double h, Brush b) { var r = new Rectangle { Width = w, Height = h, Fill = b }; Canvas.SetLeft(r, x); Canvas.SetTop(r, y); c.Children.Add(r); }
            var g = Brushes.LightGray; var a = Br(Th.Accent1);
            switch (name)
            {
                case "Blank document": break;
                case "Letter": L(40, 8, 22, 2, g); L(40, 12, 18, 2, g); L(8, 24, 20, 2, g); for (int i = 0; i < 7; i++) L(8, 34 + i * 5, 54, 2, g); L(8, 74, 16, 2, g); break;
                case "Memo": L(8, 8, 30, 5, a); for (int i = 0; i < 4; i++) L(8, 18 + i * 5, 34, 2, g); L(8, 40, 54, 1, a); for (int i = 0; i < 6; i++) L(8, 46 + i * 5, 54, 2, g); break;
                case "Fax Cover Sheet": L(8, 8, 26, 6, Brushes.Black); for (int i = 0; i < 5; i++) { L(8, 22 + i * 7, 14, 2, g); L(26, 22 + i * 7, 36, 2, g); } L(8, 62, 54, 16, Brushes.WhiteSmoke); break;
                case "Resume": L(8, 8, 36, 5, a); L(8, 15, 24, 2, g); for (int s = 0; s < 3; s++) { L(8, 24 + s * 20, 20, 3, a); for (int i = 0; i < 3; i++) L(12, 30 + s * 20 + i * 4, 50, 2, g); } break;
                case "Report": L(0, 0, 70, 30, a); L(8, 36, 40, 4, Brushes.DimGray); for (int i = 0; i < 8; i++) L(8, 46 + i * 5, 54, 2, g); break;
                case "Agenda": L(8, 8, 30, 5, a); for (int i = 0; i < 8; i++) { L(8, 20 + i * 8, 10, 2, a); L(22, 20 + i * 8, 40, 2, g); } break;
                case "Invoice": L(8, 8, 26, 5, a); L(46, 8, 16, 3, g); L(8, 30, 54, 4, a); for (int i = 0; i < 6; i++) L(8, 36 + i * 5, 54, 3, i % 2 == 0 ? Brushes.WhiteSmoke : Brushes.White); L(40, 70, 22, 4, a); break;
            }
            return c;
        }

        void BuildTemplate(string name)
        {
            NewDocument();
            var fd = Editor.Document;
            fd.Blocks.Clear();
            Paragraph P(string text, string style = "Normal", bool bold = false, TextAlignment align = TextAlignment.Left) { var p = NewPara(text, style); if (bold) p.FontWeight = FontWeights.Bold; p.TextAlignment = align; return p; }
            var me = string.IsNullOrWhiteSpace(Settings.Current.UserName) ? Environment.UserName : Settings.Current.UserName;
            var date = DateTime.Now.ToString("MMMM d, yyyy");
            switch (name)
            {
                case "Letter":
                    fd.Blocks.Add(P(me, bold: true, align: TextAlignment.Right)); fd.Blocks.Add(P("[Street Address]\n[City, Province  Postal Code]".Replace("\n", " · "), align: TextAlignment.Right));
                    fd.Blocks.Add(P(date)); fd.Blocks.Add(P("[Recipient Name]")); fd.Blocks.Add(P("[Title]")); fd.Blocks.Add(P("[Company Name]")); fd.Blocks.Add(P("[Street Address]"));
                    fd.Blocks.Add(P("Dear [Recipient Name]:")); fd.Blocks.Add(P("[Type the body of your letter here. To replace any placeholder text, just select it and start typing.]"));
                    fd.Blocks.Add(P("Sincerely,")); fd.Blocks.Add(P("")); fd.Blocks.Add(P(me));
                    break;
                case "Memo":
                    fd.Blocks.Add(P("Memo", "Title"));
                    foreach (var (k, v) in new[] { ("To:", "[Recipient names]"), ("From:", me), ("CC:", "[Names]"), ("Date:", date), ("Re:", "[Subject]") })
                    { var p = NewPara(); p.Inlines.Add(new Run(k + "\t") { FontWeight = FontWeights.Bold }); p.Inlines.Add(new Run(v)); p.Margin = new Thickness(0, 0, 0, 4); fd.Blocks.Add(p); }
                    var rule = P(""); rule.BorderBrush = Br(Th.Accent1); rule.BorderThickness = new Thickness(0, 0, 0, 1.5); fd.Blocks.Add(rule);
                    fd.Blocks.Add(P("[Type your memo text here.]"));
                    break;
                case "Fax Cover Sheet":
                    fd.Blocks.Add(P("FAX", "Title"));
                    fd.Blocks.Add(BuildTable(new List<string[]> { new[] { "To:", "[Recipient Name]", "From:", me }, new[] { "Fax:", "[Fax number]", "Pages:", "[Number of pages]" }, new[] { "Phone:", "[Phone number]", "Date:", date }, new[] { "Re:", "[Subject]", "CC:", "[Names]" } }, false));
                    fd.Blocks.Add(P("☐ Urgent   ☐ For Review   ☐ Please Comment   ☐ Please Reply   ☐ Please Recycle"));
                    fd.Blocks.Add(P("Comments:", bold: true)); fd.Blocks.Add(P("[Type comments]"));
                    break;
                case "Resume":
                    fd.Blocks.Add(P(me, "Title")); fd.Blocks.Add(P("[Address] · [Phone] · [E-mail]", "Subtitle"));
                    foreach (var sec in new[] { "Objective", "Experience", "Education", "Skills" })
                    {
                        fd.Blocks.Add(P(sec, "Heading1"));
                        if (sec == "Experience") { fd.Blocks.Add(P("[Job Title] · [Company Name] · [Dates]", bold: true)); fd.Blocks.Add(P("[Describe your responsibilities and achievements.]")); }
                        else if (sec == "Education") { fd.Blocks.Add(P("[Degree] · [School] · [Year]", bold: true)); fd.Blocks.Add(P("[Honours, relevant courses, activities.]")); }
                        else fd.Blocks.Add(P("[Type your " + sec.ToLowerInvariant() + " here.]"));
                    }
                    break;
                case "Report":
                    fd.Blocks.Add(P("[Report Title]", "Title")); fd.Blocks.Add(P("[Subtitle]", "Subtitle"));
                    fd.Blocks.Add(P("Introduction", "Heading1")); fd.Blocks.Add(P("[Type the introduction.]"));
                    fd.Blocks.Add(P("Background", "Heading1")); fd.Blocks.Add(P("Details", "Heading2")); fd.Blocks.Add(P("[Type the details.]"));
                    fd.Blocks.Add(P("Conclusion", "Heading1")); fd.Blocks.Add(P("[Type the conclusion.]"));
                    Editor.CaretPosition = fd.ContentStart;
                    InsertCoverPage("Aero");
                    break;
                case "Agenda":
                    fd.Blocks.Add(P("[Meeting Title]", "Title")); fd.Blocks.Add(P(date + " · [Time] · [Location]", "Subtitle"));
                    fd.Blocks.Add(BuildTable(new List<string[]> { new[] { "Time", "Item", "Owner" }, new[] { "9:00", "Welcome", "[Name]" }, new[] { "9:10", "[Topic]", "[Name]" }, new[] { "9:40", "[Topic]", "[Name]" }, new[] { "10:20", "Wrap-up and next steps", "[Name]" } }, true, true));
                    break;
                case "Invoice":
                    fd.Blocks.Add(P("INVOICE", "Title")); fd.Blocks.Add(P("Invoice #[100] · Date: " + date));
                    fd.Blocks.Add(P("Bill To: [Customer Name], [Address]"));
                    fd.Blocks.Add(BuildTable(new List<string[]> { new[] { "Description", "Qty", "Unit Price", "Amount" }, new[] { "[Item]", "1", "0.00", "0.00" }, new[] { "[Item]", "1", "0.00", "0.00" }, new[] { "", "", "Total", "0.00" } }, true, true));
                    fd.Blocks.Add(P("Thank you for your business!", align: TextAlignment.Center));
                    break;
            }
            if (fd.Blocks.Count == 0) fd.Blocks.Add(NewPara());
            foreach (var t in fd.Blocks.OfType<Table>().ToList()) if (name == "Agenda" || name == "Invoice") ApplyTableStyleTo(t, "Medium Shading 1");
            Editor.CaretPosition = fd.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            _dirty = false;
            ScheduleLayout();
        }

        void ConvertDocument()
        {
            if (VistaDialog.Show(this, "This action will convert the document to the newest file format. While you'll be able to use all of the new features in Valco Word, layout may change slightly. Converting allows you to use all the new features and reduces the size of your file.\n\nThe document will be saved as a Word Document (.docx).",
                    "Valco Word", MessageBoxButton.OKCancel, DlgIcon.Info) != MessageBoxResult.OK) return;
            var dir = _sourcePath != null ? System.IO.Path.GetDirectoryName(_sourcePath) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var target = System.IO.Path.Combine(dir, (System.IO.Path.GetFileNameWithoutExtension(_sourcePath ?? DisplayName)) + ".docx");
            int n = 1; while (File.Exists(target)) target = System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(_sourcePath ?? DisplayName) + $" ({n++}).docx");
            if (SaveTo(target)) { _convertible = false; _sourcePath = null; }
        }
        string _sourcePath;

        void SaveAsFormat(string ext)
        {
            var filters = new Dictionary<string, string>
            {
                [".docx"] = "Word Document (*.docx)|*.docx", [".dotx"] = "Word Template (*.dotx)|*.dotx", [".odt"] = "OpenDocument Text (*.odt)|*.odt",
                [".rtf"] = "Rich Text Format (*.rtf)|*.rtf", [".txt"] = "Plain Text (*.txt)|*.txt",
            };
            var dlg = new SaveFileDialog
            {
                Filter = filters.TryGetValue(ext, out var f) ? f : DocFormats.SaveFilter, DefaultExt = ext, AddExtension = true, OverwritePrompt = true,
                FileName = _path != null ? System.IO.Path.GetFileNameWithoutExtension(_path) : SuggestedName(),
                InitialDirectory = ext == ".dotx" ? TemplatesDir : _path != null ? System.IO.Path.GetDirectoryName(_path) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog(this) != true) return;
            if (ext == ".dotx")
            {
                try { DocFormats.Save(dlg.FileName, Editor.Document, _page, _docTitle, _author ?? ReviewerName, _ex); Flash("Saved the template " + System.IO.Path.GetFileName(dlg.FileName)); Settings.AddRecent(dlg.FileName); }
                catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The template couldn't be saved."); }
                return;
            }
            SaveTo(dlg.FileName);
        }

        // ---------------- e-mail (Simple MAPI: hands the message to the default mail program) ----------------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        class MapiMessage { public int reserved; public string subject; public string noteText; public string messageType; public string dateReceived; public string conversationID; public int flags; public IntPtr originator; public int recipCount; public IntPtr recips; public int fileCount; public IntPtr files; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        class MapiRecipDesc { public int reserved; public int recipClass; public string name; public string address; public int eIDSize; public IntPtr entryID; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        class MapiFileDesc { public int reserved; public int flags; public int position; public string path; public string name; public IntPtr type; }
        [DllImport("MAPI32.DLL", CharSet = CharSet.Ansi)]
        static extern int MAPISendMail(IntPtr session, IntPtr hwnd, MapiMessage message, int flg, int rsv);

        bool SendMail(string[] to, string subject, string body, string attachment, bool showDialog)
        {
            var msg = new MapiMessage { subject = subject, noteText = body };
            var allocs = new List<IntPtr>();
            try
            {
                if (to != null && to.Length > 0)
                {
                    int size = Marshal.SizeOf(typeof(MapiRecipDesc));
                    var ptr = Marshal.AllocHGlobal(size * to.Length); allocs.Add(ptr);
                    for (int i = 0; i < to.Length; i++) Marshal.StructureToPtr(new MapiRecipDesc { recipClass = 1, name = to[i], address = "SMTP:" + to[i] }, ptr + i * size, false);
                    msg.recips = ptr; msg.recipCount = to.Length;
                }
                if (attachment != null)
                {
                    var ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MapiFileDesc))); allocs.Add(ptr);
                    Marshal.StructureToPtr(new MapiFileDesc { position = -1, path = attachment, name = System.IO.Path.GetFileName(attachment) }, ptr, false);
                    msg.files = ptr; msg.fileCount = 1;
                }
                const int MAPI_LOGON_UI = 0x1, MAPI_DIALOG = 0x8;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int r = MAPISendMail(IntPtr.Zero, hwnd, msg, MAPI_LOGON_UI | (showDialog ? MAPI_DIALOG : 0), 0);
                if (r == 0 || r == 1) return r == 0; // 1 = user cancelled
                // no MAPI client: fall back to a mailto link (attachments can't be included)
                var mailto = $"mailto:{string.Join(";", to ?? new string[0])}?subject={Uri.EscapeDataString(subject ?? "")}&body={Uri.EscapeDataString(Truncate(body ?? "", 1500))}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(mailto) { UseShellExecute = true });
                if (attachment != null) { Flash("Your mail program doesn't accept attachments from other programs. The file is in: " + attachment); System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + attachment + "\""); }
                return true;
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The message couldn't be created. Make sure an e-mail program (such as Valco Express) is set as the default."); return false; }
            finally { foreach (var p in allocs) Marshal.FreeHGlobal(p); }
        }

        void EmailDocument(bool pdf = false)
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Valco Word Mail", Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            var name = (_path != null ? System.IO.Path.GetFileNameWithoutExtension(_path) : DisplayName);
            var file = System.IO.Path.Combine(dir, name + (pdf ? ".pdf" : ".docx"));
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                if (pdf)
                {
                    if (!Printing.ExportPdfTo(this, Editor.Document, _page, DisplayName, file)) { Mouse.OverrideCursor = null; return; }
                }
                else DocFormats.Save(file, Editor.Document, _page, _docTitle, _author ?? ReviewerName, _ex);
            }
            catch (Exception ex) { Mouse.OverrideCursor = null; VistaDialog.Error(this, ex.Message, "The attachment couldn't be created."); return; }
            finally { Mouse.OverrideCursor = null; }
            SendMail(null, name, "", file, true);
        }

        // ---------------- Word Options ----------------
        void WordOptions() => WordOptions(null);
        void WordOptions(string page)
        {
            var s = Settings.Current;
            var d = new DialogBase(this, "Valco Word Options", 680);
            var nav = new ListBox { Width = 150, Height = 380, ItemsSource = new[] { "Popular", "Display", "Proofing", "Save", "Advanced", "Customize", "Resources" } };
            var host = new ContentControl { Width = 490, Margin = new Thickness(12, 0, 0, 0) };
            // controls (kept so OK can read them)
            var mini = new CheckBox { Content = "Show Mini Toolbar on selection", IsChecked = s.MiniToolbar };
            var scheme = new ComboBox { Width = 120, ItemsSource = OfficeTheme.Schemes, SelectedItem = OfficeTheme.Current };
            var user = D.Box(s.UserName ?? "", 220); var initials = D.Box(s.Initials ?? "", 60);
            var marks = new CheckBox { Content = "Show all formatting marks", IsChecked = s.ShowMarks };
            var ruler = new CheckBox { Content = "Show the ruler", IsChecked = s.ShowRuler };
            var printBg = new CheckBox { Content = "Print background colors and images", IsChecked = !s.Custom.TryGetValue("printBg", out var pb) || pb == "1" };
            var spell = new CheckBox { Content = "Check spelling as you type", IsChecked = s.SpellCheck };
            var grammar = new CheckBox { Content = "Check grammar with spelling", IsChecked = s.GrammarCheck };
            var ignoreUpper = new CheckBox { Content = "Ignore words in UPPERCASE", IsChecked = !s.Custom.TryGetValue("ignoreUpper", out var iu) || iu == "1" };
            var autoCorrect = new CheckBox { Content = "Replace text as you type (AutoCorrect)", IsChecked = !s.Custom.TryGetValue("autoCorrect", out var ac) || ac == "1" };
            var format = new ComboBox { Width = 200, ItemsSource = new[] { "Word Document (*.docx)", "OpenDocument Text (*.odt)", "Rich Text Format (*.rtf)" }, SelectedIndex = s.Custom.TryGetValue("defaultFormat", out var df) ? (df == ".odt" ? 1 : df == ".rtf" ? 2 : 0) : 0 };
            var recover = D.Box(s.AutoRecoverMinutes.ToString(), 50);
            var font = new ComboBox { Width = 180, IsEditable = true, ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f).ToList(), Text = s.DefaultFont };
            var size = D.Box(s.DefaultSizePt.ToString(CultureInfo.InvariantCulture), 50);
            var units = new ComboBox { Width = 120, ItemsSource = new[] { "Inches", "Centimeters" }, SelectedItem = s.Custom.TryGetValue("units", out var u) ? u : "Inches" };
            var recentCount = D.Box(s.Custom.TryGetValue("recentCount", out var rc) ? rc : "17", 50);
            var autoUpd = new CheckBox { Content = "Check for Valco Office updates automatically (from GitHub)", IsChecked = Updates.AutoCheck, Margin = new Thickness(0, 6, 0, 0) };
            var smartCut = new CheckBox { Content = "Use smart cut and paste", IsChecked = true };
            var dict = UI.Btn("Custom Dictionaries...", (o, e) => CustomDictionaryEditor(d)); dict.HorizontalAlignment = HorizontalAlignment.Left;
            var qatList = new ListBox { Height = 200, Width = 200 };
            var allCmds = new ListBox { Height = 200, Width = 200, ItemsSource = QatOptions.Select(q => q.Label).ToList() };
            void FillQat() => qatList.ItemsSource = QatItems.Select(k => QatOptions.FirstOrDefault(q => q.Key == k).Label).Where(x => x != null).ToList();
            FillQat();
            UIElement Page(string p)
            {
                var sp = new StackPanel();
                void H(string t) => sp.Children.Add(new TextBlock { Text = t, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)), Margin = new Thickness(0, 6, 0, 6) });
                switch (p)
                {
                    case "Display": H("Page display options"); sp.Children.Add(ruler); sp.Children.Add(marks); H("Printing options"); sp.Children.Add(printBg); break;
                    case "Proofing": H("When correcting spelling and grammar"); sp.Children.Add(spell); sp.Children.Add(grammar); sp.Children.Add(ignoreUpper); sp.Children.Add(autoCorrect); sp.Children.Add(new Border { Height = 8 }); sp.Children.Add(dict); break;
                    case "Save": H("Save documents"); sp.Children.Add(D.Form(("Save files in this format:", format), ("Save AutoRecover information every (minutes):", recover))); break;
                    case "Advanced": H("Editing options"); sp.Children.Add(smartCut); H("Display"); sp.Children.Add(D.Form(("Show this number of Recent Documents:", recentCount), ("Show measurements in units of:", units))); H("Default font"); sp.Children.Add(D.Form(("Font:", font), ("Size:", size))); break;
                    case "Customize":
                        {
                            H("Customize the Quick Access Toolbar");
                            var add = UI.Btn("Add >>", (o, e) => { if (allCmds.SelectedIndex >= 0) { var k = QatOptions[allCmds.SelectedIndex].Key; var l = QatItems; if (!l.Contains(k)) { l.Add(k); QatItems = l; BuildQat(); FillQat(); } } });
                            var rem = UI.Btn("<< Remove", (o, e) => { if (qatList.SelectedItem is string lab) { var k = QatOptions.First(q => q.Label == lab).Key; var l = QatItems; l.Remove(k); QatItems = l; BuildQat(); FillQat(); } });
                            var reset = UI.Btn("Reset", (o, e) => { QatItems = new List<string> { "Save", "Undo", "Redo" }; BuildQat(); FillQat(); });
                            foreach (var b in new[] { add, rem, reset }) b.Margin = new Thickness(0, 0, 0, 4);
                            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) }; mid.Children.Add(add); mid.Children.Add(rem); mid.Children.Add(reset);
                            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(allCmds); row.Children.Add(mid); row.Children.Add(qatList);
                            sp.Children.Add(row);
                            break;
                        }
                    case "Resources":
                        H("Valco Word");
                        sp.Children.Add(new TextBlock { Text = "Part of the Valco Office suite.", Margin = new Thickness(0, 0, 0, 6) });
                        var about = UI.Btn("About", (o, e) => InfoDialogs.About(d)); about.HorizontalAlignment = HorizontalAlignment.Left; sp.Children.Add(about);
                        var upd = UI.Btn("Check for Updates", (o, e) => CheckForUpdates()); upd.HorizontalAlignment = HorizontalAlignment.Left; upd.Margin = new Thickness(0, 6, 0, 0); sp.Children.Add(upd);
                        sp.Children.Add(autoUpd);
                        sp.Children.Add(new TextBlock { Text = "Thesaurus data: WordNet 3.0 © Princeton University. Research uses Wikipedia; translation uses MyMemory (internet connection required).", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 10, 0, 0) });
                        break;
                    default:
                        H("Top options for working with Valco Word"); sp.Children.Add(mini); sp.Children.Add(D.Form(("Color scheme:", scheme)));
                        H("Personalize your copy of Valco Word"); sp.Children.Add(D.Form(("User name:", user), ("Initials:", initials)));
                        break;
                }
                return sp;
            }
            // each page is built once: its controls can only have one parent, and OK reads them all
            var pages = new Dictionary<string, UIElement>();
            nav.SelectionChanged += (o, e) =>
            {
                var key = (string)nav.SelectedItem ?? "Popular";
                if (!pages.TryGetValue(key, out var pg)) pages[key] = pg = Page(key);
                host.Content = pg;
            };
            nav.SelectedItem = page ?? "Popular";
            scheme.SelectionChanged += (o, e) => { if (scheme.SelectedItem is string sc) OfficeTheme.Apply(sc); };
            var row2 = new StackPanel { Orientation = Orientation.Horizontal }; row2.Children.Add(nav); row2.Children.Add(host);
            d.Content = UI.DialogLayout(row2, UI.Strip(UI.Btn("OK", (o, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            var oldScheme = OfficeTheme.Current;
            if (d.ShowDialog() != true) { OfficeTheme.Apply(oldScheme); return; }
            s.MiniToolbar = mini.IsChecked == true; s.ColorScheme = (string)scheme.SelectedItem ?? "Blue";
            s.UserName = user.Text.Trim(); s.Initials = initials.Text.Trim();
            s.ShowMarks = marks.IsChecked == true; s.ShowRuler = ruler.IsChecked == true;
            s.Custom["printBg"] = printBg.IsChecked == true ? "1" : "0";
            s.SpellCheck = spell.IsChecked == true; s.GrammarCheck = grammar.IsChecked == true;
            s.Custom["ignoreUpper"] = ignoreUpper.IsChecked == true ? "1" : "0"; s.Custom["autoCorrect"] = autoCorrect.IsChecked == true ? "1" : "0";
            s.Custom["defaultFormat"] = format.SelectedIndex == 1 ? ".odt" : format.SelectedIndex == 2 ? ".rtf" : ".docx";
            s.AutoRecoverMinutes = (int)Math.Max(1, Math.Min(120, D.ParseNum(recover.Text) ?? 10));
            if (font.Text.Trim().Length > 0) s.DefaultFont = font.Text.Trim();
            s.DefaultSizePt = Math.Max(1, Math.Min(400, D.ParseNum(size.Text) ?? 11));
            s.Custom["units"] = (string)units.SelectedItem ?? "Inches"; s.Custom["recentCount"] = ((int)Math.Max(0, Math.Min(50, D.ParseNum(recentCount.Text) ?? 17))).ToString();
            Updates.AutoCheck = autoUpd.IsChecked == true;
            Settings.Save();
            foreach (var w in Application.Current.Windows.OfType<MainWindow>()) w.ApplyViewSettings();
            DrawOverlay();
        }

        /// <summary>Checks GitHub for a newer Valco Word and offers Valco Office Update.</summary>
        void CheckForUpdates() => Updates.CheckNow(this, "word", "Valco Word");

        void CustomDictionaryEditor(Window owner)
        {
            var d = new DialogBase(owner, "Custom Dictionary", 320);
            var list = new ListBox { Height = 200, ItemsSource = Settings.Current.Custom.TryGetValue("dictWords", out var w) ? w.Split('\n').Where(x => x.Length > 0).ToList() : new List<string>() };
            var word = D.Box("", 200);
            var words = ((List<string>)list.ItemsSource).ToList();
            void Save() { Settings.Current.Custom["dictWords"] = string.Join("\n", words); Settings.Save(); list.ItemsSource = null; list.ItemsSource = words; AddCustomDictionary(); }
            var add = UI.Btn("Add", (s, e) => { if (word.Text.Trim().Length > 0 && !words.Contains(word.Text.Trim())) { words.Add(word.Text.Trim()); words.Sort(); Save(); word.Text = ""; } });
            var del = UI.Btn("Delete", (s, e) => { if (list.SelectedItem is string x) { words.Remove(x); Save(); } });
            var sp = new StackPanel(); sp.Children.Add(new TextBlock { Text = "Word:" }); sp.Children.Add(word); sp.Children.Add(new TextBlock { Text = "Dictionary:", Margin = new Thickness(0, 6, 0, 0) }); sp.Children.Add(list);
            d.Content = UI.DialogLayout(sp, UI.Strip(add, del, UI.Btn("Close", null, false, true)));
            d.ShowDialog();
        }
    }
}
