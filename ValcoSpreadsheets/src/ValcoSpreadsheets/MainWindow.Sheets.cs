using System;
using System.Collections.Generic;
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

namespace ValcoSpreadsheets
{
    /// <summary>The sheet tabs at the bottom, Excel 2007 style.</summary>
    public partial class MainWindow
    {
        void BuildTabNav()
        {
            Button Nav(string geo, string tip, Action a)
            {
                var b = new Button { Style = (Style)FindResource("FmtButton"), Height = 18, MinWidth = 16, Width = 16, Padding = new Thickness(0), ToolTip = tip, Content = new Path { Data = Geometry.Parse(geo), Fill = new SolidColorBrush(Color.FromRgb(0x3B, 0x5A, 0x86)) } };
                b.Click += (s, e) => a();
                return b;
            }
            TabNav.Children.Add(Nav("M0,4 L0,0 L1.5,0 L1.5,8 L0,8 Z M2,4 L7,0 L7,8 Z", "Scroll to the first sheet", () => TabScroller.ScrollToLeftEnd()));
            TabNav.Children.Add(Nav("M0,4 L5,0 L5,8 Z", "Scroll to the previous sheet", () => TabScroller.ScrollToHorizontalOffset(TabScroller.HorizontalOffset - 60)));
            TabNav.Children.Add(Nav("M0,0 L5,4 L0,8 Z", "Scroll to the next sheet", () => TabScroller.ScrollToHorizontalOffset(TabScroller.HorizontalOffset + 60)));
            TabNav.Children.Add(Nav("M0,0 L5,4 L0,8 Z M5.5,0 L7,0 L7,8 L5.5,8 Z", "Scroll to the last sheet", () => TabScroller.ScrollToRightEnd()));
        }

        void BuildTabs()
        {
            TabStrip.Children.Clear();
            foreach (var ws in _book.Wb.Worksheets)
            {
                if (ws.Visibility != XLWorksheetVisibility.Visible) continue;
                TabStrip.Children.Add(SheetTab(ws, ws.Position == SheetIdx));
            }
            // Insert Worksheet tab
            var add = new Button { Style = (Style)FindResource("FmtButton"), Height = 18, MinWidth = 22, Padding = new Thickness(2, 0, 2, 0), Margin = new Thickness(2, 0, 0, 0), ToolTip = RB.Tip("Insert Worksheet (Shift+F11)", null), VerticalAlignment = VerticalAlignment.Top };
            add.Content = new Image { Source = UI.Img("IconNewSheet"), Width = 16, Height = 16 };
            add.Click += (s, e) => InsertSheet(_book.Wb.Worksheets.Count + 1);
            TabStrip.Children.Add(add);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var active = TabStrip.Children.OfType<FrameworkElement>().FirstOrDefault(t => t.Tag is int i && i == SheetIdx);
                active?.BringIntoView();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        FrameworkElement SheetTab(IXLWorksheet ws, bool active)
        {
            Color? tabColor = null;
            try { if (ws.TabColor != null && ws.TabColor.HasValue) tabColor = XlColors.ToWpf(ws.TabColor, _book.Wb); } catch { }
            var label = new TextBlock { Text = ws.Name, Margin = new Thickness(10, 1, 10, 2), FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal, Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x2E, 0x45)) };
            Brush bg = active ? Brushes.White : (Brush)new LinearGradientBrush(Color.FromRgb(0xF4, 0xF7, 0xFB), Color.FromRgb(0xD8, 0xE3, 0xF1), 90);
            if (tabColor.HasValue)
            {
                if (active) bg = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Colors.White, 0), new GradientStop(Colors.White, 0.6), new GradientStop(tabColor.Value, 1) }, 90);
                else { bg = new SolidColorBrush(tabColor.Value); label.Foreground = Brightness(tabColor.Value) < 0.5 ? Brushes.White : Brushes.Black; }
            }
            var shape = new Border
            {
                Child = label, Background = bg, BorderBrush = new SolidColorBrush(Color.FromRgb(0x8D, 0xA6, 0xC7)), BorderThickness = new Thickness(1, 0, 1, 1),
                CornerRadius = new CornerRadius(0, 0, 4, 4), Margin = new Thickness(active ? 0 : 1, active ? -1 : 0, 0, 0), Height = active ? 19 : 17, VerticalAlignment = VerticalAlignment.Top,
                Tag = ws.Position, Cursor = Cursors.Arrow, ToolTip = active ? null : ws.Name,
            };
            Panel.SetZIndex(shape, active ? 10 : 0);
            int pos = ws.Position;
            Point down = default; bool dragging = false;
            shape.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { RenameSheet(pos); e.Handled = true; return; }
                if (pos != SheetIdx) ShowSheet(pos);
                down = e.GetPosition(TabStrip); dragging = true; shape.CaptureMouse();
                e.Handled = true;
            };
            shape.MouseMove += (s, e) =>
            {
                if (!dragging || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(TabStrip);
                if (Math.Abs(p.X - down.X) > 8) shape.Cursor = Cursors.Hand;
            };
            shape.MouseLeftButtonUp += (s, e) =>
            {
                if (!dragging) return;
                dragging = false; shape.ReleaseMouseCapture(); shape.Cursor = Cursors.Arrow;
                var p = e.GetPosition(TabStrip);
                if (Math.Abs(p.X - down.X) <= 8) return;
                // drop: move the sheet to where the mouse is
                int target = pos;
                foreach (var t in TabStrip.Children.OfType<Border>())
                {
                    var tp = t.TranslatePoint(new Point(0, 0), TabStrip);
                    if (p.X >= tp.X && p.X < tp.X + t.ActualWidth && t.Tag is int i) target = i;
                }
                if (p.X > TabStrip.Children.OfType<Border>().Last().TranslatePoint(new Point(0, 0), TabStrip).X + TabStrip.Children.OfType<Border>().Last().ActualWidth) target = _book.Wb.Worksheets.Count;
                if (target != pos) MoveSheet(pos, target, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            };
            shape.MouseRightButtonUp += (s, e) =>
            {
                if (pos != SheetIdx) ShowSheet(pos);
                SheetMenu(shape);
                e.Handled = true;
            };
            return shape;
        }

        static double Brightness(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

        void SheetMenu(FrameworkElement target)
        {
            var m = new ContextMenu();
            m.Items.Add(RB.MI("Insert...", () => InsertSheet(SheetIdx), "IconNewSheet"));
            m.Items.Add(RB.MI("Delete", () => DeleteSheet(SheetIdx), "IconDelete"));
            m.Items.Add(RB.MI("Rename", () => RenameSheet(SheetIdx)));
            m.Items.Add(RB.MI("Move or Copy...", MoveCopyDialog));
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI(WS.IsProtected ? "Unprotect Sheet..." : "Protect Sheet...", ProtectSheetToggle, "IconProtectSheet"));
            var tc = new MenuItem { Header = "Tab Color" };
            tc.Click += (s, e) => { if (e.OriginalSource == tc) ColorPicker.Show(target, "No Color", c => SetTabColor(c)); };
            m.Items.Add(tc);
            m.Items.Add(new Separator());
            m.Items.Add(RB.MI("Hide", () => HideSheet(SheetIdx)));
            var hidden = _book.Wb.Worksheets.Where(w => w.Visibility != XLWorksheetVisibility.Visible).ToList();
            var un = RB.MI("Unhide...", UnhideSheetDialog); un.IsEnabled = hidden.Count > 0;
            m.Items.Add(un);
            m.PlacementTarget = target;
            m.Placement = PlacementMode.Top;
            m.IsOpen = true;
        }

        void InsertSheet(int position)
        {
            int n = _book.Wb.Worksheets.Count + 1;
            string name;
            do name = "Sheet" + n++; while (_book.Wb.Worksheets.Any(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
            position = Math.Clamp(position, 1, _book.Wb.Worksheets.Count + 1);
            Change("Insert Worksheet", () => _book.Wb.AddWorksheet(name, position), structure: true);
            ShowSheet(position);
        }

        void DeleteSheet(int pos)
        {
            if (_book.Wb.Worksheets.Count(w => w.Visibility == XLWorksheetVisibility.Visible) <= 1)
            {
                VistaDialog.Show(this, "A workbook must contain at least one visible worksheet.\n\nTo hide, delete, or move the selected sheet(s), you must first insert a new sheet or unhide a sheet that is already hidden.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning);
                return;
            }
            var ws = _book.Wb.Worksheet(pos);
            if (!ws.RangeUsed(XLCellsUsedOptions.All).IsEmpty() && ws.FirstCellUsed(XLCellsUsedOptions.Contents) != null)
                if (VistaDialog.Show(this, "Data may exist in the sheet(s) selected for deletion. To permanently delete the data, press Delete.", "Valco Spreadsheets", MessageBoxButton.OKCancel, DlgIcon.Warning) != MessageBoxResult.OK) return;
            Change("Delete Sheet", () => ws.Delete(), structure: true);
            ShowSheet(Math.Min(pos, _book.Wb.Worksheets.Count));
        }

        void RenameSheet(int pos)
        {
            var ws = _book.Wb.Worksheet(pos);
            var name = InputDialog.Ask(this, "Rename Sheet", "Sheet name:", ws.Name);
            if (name == null || name == ws.Name) return;
            if (name.Length > 31 || name.IndexOfAny(new[] { ':', '\\', '/', '?', '*', '[', ']' }) >= 0 || name.StartsWith("'") || name.EndsWith("'") ||
                _book.Wb.Worksheets.Any(w => w != ws && w.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                VistaDialog.Show(this, "You typed an invalid name for a sheet or chart. Make sure that:\n\n   •  The name that you type does not exceed 31 characters.\n   •  The name does not contain any of the following characters:  :  \\  /  ?  *  [  ]\n   •  You did not leave the name blank.\n   •  No other sheet in the workbook has the same name.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning);
                return;
            }
            var old = ws.Name;
            Change("Rename Sheet", () => ws.Name = name, structure: true);
            G.RenameView(old, name);
            BuildTabs();
        }

        void MoveSheet(int from, int to, bool copy)
        {
            var ws = _book.Wb.Worksheet(from);
            if (copy)
            {
                string name = ws.Name + " (2)"; int n = 2;
                while (_book.Wb.Worksheets.Any(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = ws.Name + $" ({++n})";
                if (name.Length > 31) name = name.Substring(name.Length - 31);
                Change("Copy Sheet", () => ws.CopyTo(name, Math.Clamp(to, 1, _book.Wb.Worksheets.Count + 1)), structure: true);
                ShowSheet(Math.Clamp(to, 1, _book.Wb.Worksheets.Count));
                return;
            }
            Change("Move Sheet", () => ws.Position = Math.Clamp(to, 1, _book.Wb.Worksheets.Count), structure: true);
            ShowSheet(Math.Clamp(to, 1, _book.Wb.Worksheets.Count));
        }

        void MoveCopyDialog()
        {
            var books = Application.Current.Windows.OfType<MainWindow>().Select(w => w._book).ToList();
            var d = new MoveCopyDialog(this, books, _book, WS.Name);
            if (d.ShowDialog() != true) return;
            var target = d.TargetBook;
            if (target == _book)
            {
                MoveSheet(SheetIdx, d.BeforePosition > _book.Wb.Worksheets.Count ? _book.Wb.Worksheets.Count : (d.BeforePosition > SheetIdx && !d.Copy ? d.BeforePosition - 1 : d.BeforePosition), d.Copy);
                return;
            }
            // to another open workbook (or a new one)
            var src = WS;
            var win = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w._book == target);
            if (target == null)
            {
                var nb = new Book();
                foreach (var w in nb.Wb.Worksheets.ToList()) w.Delete();
                src.CopyTo(nb.Wb, src.Name);
                nb.RaiseChanged();
                win = new MainWindow(nb);
                win.Show();
            }
            else
            {
                string name = src.Name; int n = 1;
                while (target.Wb.Worksheets.Any(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = src.Name + $" ({++n})";
                target.Undo.Do("Copy Sheet", 1, null, () => src.CopyTo(target.Wb, name, Math.Clamp(d.BeforePosition, 1, target.Wb.Worksheets.Count + 1)));
                win?.RefreshAll();
            }
            if (!d.Copy) DeleteSheet(SheetIdx);
        }

        void SetTabColor(Color? c)
        {
            var ws = WS;
            Change("Tab Color", () => { if (c.HasValue) ws.SetTabColor(XlColors.FromWpf(c.Value)); else ws.TabColor = XLColor.NoColor; }, structure: true);
        }

        void HideSheet(int pos)
        {
            if (_book.Wb.Worksheets.Count(w => w.Visibility == XLWorksheetVisibility.Visible) <= 1)
            {
                VistaDialog.Show(this, "A workbook must contain at least one visible worksheet.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning);
                return;
            }
            Change("Hide Sheet", () => _book.Wb.Worksheet(pos).Hide(), structure: true);
            ShowSheet(NextVisibleSheet(pos, -1) == pos ? NextVisibleSheet(pos, 1) : NextVisibleSheet(pos, -1));
        }

        void UnhideSheetDialog()
        {
            var hidden = _book.Wb.Worksheets.Where(w => w.Visibility != XLWorksheetVisibility.Visible).Select(w => w.Name).ToList();
            if (hidden.Count == 0) return;
            var pick = ListDialog.Ask(this, "Unhide", "Unhide sheet:", hidden);
            if (pick == null) return;
            var ws = _book.Wb.Worksheet(pick);
            Change("Unhide Sheet", () => ws.Unhide(), structure: true);
            ShowSheet(ws.Position);
        }

        void ProtectSheetToggle()
        {
            if (WS.IsProtected)
            {
                var sheet0 = WS;
                if (!sheet0.IsPasswordProtected) { Change("Unprotect Sheet", () => sheet0.Unprotect(), structure: true); SyncRibbon(); return; }
                var pw = PasswordDialog.Ask(this, "Unprotect Sheet", "Password:");
                if (pw == null) return;
                try { sheet0.Unprotect(pw); sheet0.Protect(pw); }
                catch { VistaDialog.Show(this, "The password you supplied is not correct. Verify that the CAPS LOCK key is off and be sure to use the correct capitalization.", "Valco Spreadsheets", MessageBoxButton.OK, DlgIcon.Warning); return; }
                Change("Unprotect Sheet", () => sheet0.Unprotect(pw), structure: true);
                SyncRibbon();
                return;
            }
            var d = new ProtectSheetDialog(this);
            if (d.ShowDialog() != true) return;
            var sheet = WS;
            Change("Protect Sheet", () =>
            {
                var p = string.IsNullOrEmpty(d.Password) ? sheet.Protect() : sheet.Protect(d.Password);
                p.AllowElement(XLSheetProtectionElements.SelectLockedCells, d.SelectLocked);
                p.AllowElement(XLSheetProtectionElements.SelectUnlockedCells, d.SelectUnlocked);
                p.AllowElement(XLSheetProtectionElements.FormatCells, d.FormatCells);
                p.AllowElement(XLSheetProtectionElements.InsertRows, d.InsertRows);
                p.AllowElement(XLSheetProtectionElements.DeleteRows, d.DeleteRows);
                p.AllowElement(XLSheetProtectionElements.Sort, d.Sort);
                p.AllowElement(XLSheetProtectionElements.AutoFilter, d.AutoFilter);
            }, structure: true);
            SyncRibbon();
        }
    }
}
