using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClosedXML.Excel;
using Microsoft.Win32;
using ValcoSpreadsheets.Grid;
using ValcoSpreadsheets.Model;

namespace ValcoSpreadsheets
{
    public partial class MainWindow
    {
        const string OpenFilter = "All Excel Files (*.xlsx;*.xlsm;*.xltx;*.xltm;*.xls;*.csv)|*.xlsx;*.xlsm;*.xltx;*.xltm;*.xls;*.xlt;*.csv|Excel Workbook (*.xlsx)|*.xlsx|Excel Macro-Enabled Workbook (*.xlsm)|*.xlsm|Excel Template (*.xltx;*.xltm)|*.xltx;*.xltm|Excel 97-2003 Workbook (*.xls)|*.xls;*.xlt|CSV (Comma delimited) (*.csv)|*.csv|Text Files (*.txt;*.tsv;*.prn)|*.txt;*.tsv;*.tab;*.prn|All Files (*.*)|*.*";

        static readonly (string Filter, BookFormat Format, string Ext)[] SaveTypes =
        {
            ("Excel Workbook (*.xlsx)|*.xlsx", BookFormat.Xlsx, ".xlsx"),
            ("Excel Macro-Enabled Workbook (*.xlsm)|*.xlsm", BookFormat.Xlsm, ".xlsm"),
            ("Excel Template (*.xltx)|*.xltx", BookFormat.Xltx, ".xltx"),
            ("Excel Macro-Enabled Template (*.xltm)|*.xltm", BookFormat.Xltm, ".xltm"),
            ("Excel 97-2003 Workbook (*.xls)|*.xls", BookFormat.Xls, ".xls"),
            ("CSV (Comma delimited) (*.csv)|*.csv", BookFormat.Csv, ".csv"),
            ("Text (Tab delimited) (*.txt)|*.txt", BookFormat.Text, ".txt"),
        };

        void NewWorkbook()
        {
            var w = new MainWindow();
            w.Show();
        }

        void NewFromTemplate()
        {
            var dlg = new OpenFileDialog { Title = "New from Existing Workbook", Filter = "Excel Templates and Workbooks (*.xltx;*.xltm;*.xlsx;*.xlsm)|*.xltx;*.xltm;*.xlsx;*.xlsm" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var b = Book.Open(dlg.FileName);
                var w = new MainWindow(b);
                w.Show();
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "Valco Spreadsheets cannot open the file."); }
        }

        void OpenFile()
        {
            var dlg = new OpenFileDialog { Filter = OpenFilter, Multiselect = true };
            if (!string.IsNullOrEmpty(Settings.Current.DefaultFolder) && Directory.Exists(Settings.Current.DefaultFolder)) dlg.InitialDirectory = Settings.Current.DefaultFolder;
            if (dlg.ShowDialog(this) != true) return;
            foreach (var f in dlg.FileNames) OpenPath(f);
        }

        /// <summary>Opens a file in this window if it's an untouched new workbook, otherwise in a new window.</summary>
        public void OpenPath(string path)
        {
            // already open? switch to it
            foreach (var w in Application.Current.Windows.OfType<MainWindow>())
                if (w._book.FilePath != null && string.Equals(w._book.FilePath, path, StringComparison.OrdinalIgnoreCase)) { w.Activate(); return; }
            Book b;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                b = Book.Open(path);
            }
            catch (IOException ex) when (ex.Message.Contains("being used"))
            {
                Mouse.OverrideCursor = null;
                VistaDialog.Error(this, $"'{Path.GetFileName(path)}' is locked for editing by another program.", "File in Use");
                return;
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                VistaDialog.Error(this, $"Valco Spreadsheets cannot open the file '{Path.GetFileName(path)}' because the file format or file extension is not valid, or the file is damaged.\n\n{ex.Message}", "Valco Spreadsheets");
                return;
            }
            finally { Mouse.OverrideCursor = null; }
            if (b.FilePath != null) Settings.AddRecent(b.FilePath);
            if (!_book.Dirty && _book.FilePath == null && _book.Undo.CanUndo == false)
            {
                LoadBook(b);
            }
            else
            {
                var w = new MainWindow(b);
                w.Show();
            }
        }

        void LoadBook(Book b)
        {
            _book.Undo.StackChanged -= UpdateUndoButtons;
            _book = b;
            _book.Undo.StackChanged += UpdateUndoButtons;
            _clip = _clip != null && _clip.Book == null ? null : _clip;
            G.ForgetViews();
            ShowSheet(ActiveSheetOf(b));
            UpdateTitle();
            ShowBookMessages();
            UpdateUndoButtons();
        }

        /// <summary>Save. Returns false if the user cancelled.</summary>
        public bool Save()
        {
            if (G.IsEditing) CommitEdit(0, 0);
            if (_book.FilePath == null || _book.IsTemplate) return SaveAs();
            if (_book.UnkeptParts.Count > 0)
            {
                var r = VistaDialog.Show(this, $"This workbook has {string.Join(", ", _book.UnkeptParts)} that Valco Spreadsheets can't save. Saving over '{_book.DisplayName}' would remove them.\n\nClick Yes to save a copy under a new name instead, or No to save over the file anyway.", "Valco Spreadsheets", MessageBoxButton.YesNoCancel, DlgIcon.Warning);
                if (r == MessageBoxResult.Cancel) return false;
                if (r == MessageBoxResult.Yes) return SaveAs();
                _book.UnkeptParts.Clear();
            }
            return SaveTo(_book.FilePath, _book.Format);
        }

        public bool SaveAs()
        {
            if (G.IsEditing) CommitEdit(0, 0);
            var dlg = new SaveFileDialog
            {
                Filter = string.Join("|", SaveTypes.Select(t => t.Filter)),
                FileName = Path.GetFileNameWithoutExtension(_book.DisplayName),
                AddExtension = true,
                OverwritePrompt = true,
            };
            var fmt = _book.Format;
            if (_book.HasMacros && fmt == BookFormat.Xlsx) fmt = BookFormat.Xlsm;
            dlg.FilterIndex = Math.Max(0, Array.FindIndex(SaveTypes, t => t.Format == fmt)) + 1;
            if (_book.FilePath != null) dlg.InitialDirectory = Path.GetDirectoryName(_book.FilePath);
            else if (!string.IsNullOrEmpty(Settings.Current.DefaultFolder) && Directory.Exists(Settings.Current.DefaultFolder)) dlg.InitialDirectory = Settings.Current.DefaultFolder;
            if (dlg.ShowDialog(this) != true) return false;
            var chosen = SaveTypes[Math.Clamp(dlg.FilterIndex - 1, 0, SaveTypes.Length - 1)];
            var path = dlg.FileName;
            if (!path.EndsWith(chosen.Ext, StringComparison.OrdinalIgnoreCase)) path = Path.ChangeExtension(path, chosen.Ext);
            return SaveTo(path, chosen.Format);
        }

        bool SaveTo(string path, BookFormat fmt)
        {
            // Excel 97-2003: say what the old format can't hold before saving, like Excel 2007's Compatibility Checker
            if (fmt == BookFormat.Xls)
            {
                var issues = XlsFile.CheckCompatibility(_book);
                if (issues.Count > 0 && !CompatibilityChecker(issues, true)) return false;
            }
            else
            // macros don't fit in a macro-free workbook
            if (_book.HasMacros && (fmt == BookFormat.Xlsx || fmt == BookFormat.Xltx))
            {
                var r = VistaDialog.Show(this, "The following features cannot be saved in macro-free workbooks:\n\n   •  VB project\n\nTo save a file with these features, click No, and then choose a macro-enabled file type in the File Type list.\nTo continue saving as a macro-free workbook, click Yes.", "Valco Spreadsheets", MessageBoxButton.YesNo, DlgIcon.Warning);
                if (r != MessageBoxResult.Yes) return SaveAs();
                _book.DiscardMacros();
                ShowBookMessages();
            }
            if ((fmt == BookFormat.Csv || fmt == BookFormat.Text) && _book.Wb.Worksheets.Count > 1)
            {
                var r = VistaDialog.Show(this, $"The selected file type does not support workbooks that contain multiple sheets.\n\n   •  To save only the active sheet, click OK.\n   •  To save all sheets, save them individually using a different file name for each, or choose a file type that supports multiple sheets.", "Valco Spreadsheets", MessageBoxButton.OKCancel, DlgIcon.Warning);
                if (r != MessageBoxResult.OK) return false;
            }
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                G.WriteSheetView();
                _book.ActiveSheetForExport = WS;
                _book.SaveAs(path, fmt);
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                VistaDialog.Error(this, $"Your changes could not be saved to '{Path.GetFileName(path)}'.\n\n{ex.Message}", "Valco Spreadsheets");
                return false;
            }
            finally { Mouse.OverrideCursor = null; }
            Settings.AddRecent(path);
            UpdateTitle();
            ShowBookMessages();
            StatusMsg.Text = "";
            return true;
        }

        /// <summary>Excel 2007's Compatibility Checker. When saving, returns true if the user chose Continue.</summary>
        bool CompatibilityChecker(List<string> issues, bool saving)
        {
            var w = new Window
            {
                Title = "Valco Spreadsheets - Compatibility Checker",
                Width = 520, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ShowInTaskbar = false,
                Background = SystemColors.ControlBrush,
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = issues.Count == 0
                    ? "No compatibility issues were found. This workbook can be saved in the Excel 97-2003 format without losing anything."
                    : "The following features in this workbook are not supported by earlier versions of Excel. These features may be lost or degraded when you save this workbook in an earlier file format.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            });
            if (issues.Count > 0)
            {
                var list = new StackPanel();
                list.Children.Add(new System.Windows.Controls.Grid
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xDB, 0xE7, 0xF6)),
                    Children = { new TextBlock { Text = "Summary", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)), Margin = new Thickness(6, 3, 6, 3) } },
                });
                foreach (var i in issues)
                {
                    var row = new DockPanel { Margin = new Thickness(6, 6, 6, 6) };
                    var ic = new Image { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 6, 0) };
                    if (TryFindResource("IconWarning") is ImageSource src) ic.Source = src;
                    DockPanel.SetDock(ic, Dock.Left);
                    row.Children.Add(ic);
                    row.Children.Add(new TextBlock { Text = i, TextWrapping = TextWrapping.Wrap });
                    list.Children.Add(row);
                    list.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xE3, 0xE8, 0xEF)) });
                }
                root.Children.Add(new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)), BorderThickness = new Thickness(1), Background = Brushes.White,
                    Child = new ScrollViewer { MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list },
                });
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            bool ok = false;
            if (saving && issues.Count > 0)
            {
                var cont = new Button { Content = "_Continue", MinWidth = 75, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
                cont.Click += (s, e) => { ok = true; w.DialogResult = true; };
                buttons.Children.Add(cont);
                buttons.Children.Add(new Button { Content = "Cancel", MinWidth = 75, IsCancel = true });
            }
            else buttons.Children.Add(new Button { Content = "OK", MinWidth = 75, IsDefault = true, IsCancel = true });
            root.Children.Add(buttons);
            w.Content = root;
            w.ShowDialog();
            return ok;
        }

        void RunCompatibilityChecker() => CompatibilityChecker(XlsFile.CheckCompatibility(_book), false);

        /// <summary>Convert (Excel 2007): turns a Compatibility Mode workbook into the current format.</summary>
        void ConvertWorkbook()
        {
            if (_book.Format != BookFormat.Xls) return;
            var r = VistaDialog.Show(this, "This action will convert the workbook to the current file format. This will allow you to use all the new features of Valco Spreadsheets, such as charts, 1,048,576 rows and new conditional formatting.\n\nThe original Excel 97-2003 file is kept; the converted workbook is saved next to it with the .xlsx extension.", "Valco Spreadsheets", MessageBoxButton.OKCancel, DlgIcon.Info);
            if (r != MessageBoxResult.OK) return;
            var fmt = _book.HasMacros ? BookFormat.Xlsm : BookFormat.Xlsx;
            var path = _book.FilePath != null ? System.IO.Path.ChangeExtension(_book.FilePath, fmt == BookFormat.Xlsm ? ".xlsm" : ".xlsx") : null;
            if (path == null || File.Exists(path)) { SaveAsFormat(fmt); return; }
            SaveTo(path, fmt);
        }

        void CloseWorkbook()
        {
            if (G.IsEditing) CommitEdit(0, 0);
            if (_book.Dirty && !ConfirmDiscard()) return;
            if (Application.Current.Windows.OfType<MainWindow>().Count() > 1) { _book.Dirty = false; Close(); return; }
            // last window: keep it open with a fresh workbook, like Excel's blank grey state
            _book.Dirty = false;
            LoadBook(new Book());
        }

        void ExitApp()
        {
            foreach (var w in Application.Current.Windows.OfType<MainWindow>().ToList())
            {
                w.Close();
                if (w.IsVisible) return; // cancelled
            }
        }

        void SaveAsPdf()
        {
            var dlg = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf|XPS Document (*.xps)|*.xps", FileName = Path.GetFileNameWithoutExtension(_book.DisplayName) };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var doc = SheetPrinter.BuildDocument(_book, WS, G.Layout, PrintSetup.For(WS));
                if (dlg.FileName.EndsWith(".xps", StringComparison.OrdinalIgnoreCase)) SheetPrinter.SaveXps(doc, dlg.FileName);
                else SheetPrinter.SavePdf(doc, dlg.FileName);
                StatusMsg.Text = "Published " + Path.GetFileName(dlg.FileName);
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The document could not be published."); }
        }

        // ===================================================================== printing
        void PrintDialogShow()
        {
            if (G.IsEditing) CommitEdit(0, 0);
            var pd = new PrintDialog();
            if (pd.ShowDialog() != true) return;
            try
            {
                var setup = PrintSetup.For(WS);
                setup.Selection = G.ActiveRect.Count > 1 && !G.ActiveRect.IsWholeColumns && !G.ActiveRect.IsWholeRows ? G.ActiveRect : (CellRect?)null;
                setup.Selection = null;
                var doc = SheetPrinter.BuildDocument(_book, WS, G.Layout, setup, pd.PrintableAreaWidth, pd.PrintableAreaHeight);
                pd.PrintDocument(doc.DocumentPaginator, _book.DisplayName);
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The document could not be printed."); }
        }

        void QuickPrint()
        {
            try
            {
                var pd = new PrintDialog();
                var doc = SheetPrinter.BuildDocument(_book, WS, G.Layout, PrintSetup.For(WS), pd.PrintableAreaWidth, pd.PrintableAreaHeight);
                pd.PrintDocument(doc.DocumentPaginator, _book.DisplayName);
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The document could not be printed."); }
        }

        void PrintPreview()
        {
            if (G.IsEditing) CommitEdit(0, 0);
            try
            {
                var doc = SheetPrinter.BuildDocument(_book, WS, G.Layout, PrintSetup.For(WS));
                var w = new PrintPreviewWindow(this, doc, _book.DisplayName, () => PrintDialogShow(), () => { PageSetupDialogShow(); return SheetPrinter.BuildDocument(_book, WS, G.Layout, PrintSetup.For(WS)); });
                w.ShowDialog();
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "Print Preview isn't available."); }
        }

        void PageSetupDialogShow()
        {
            var d = new PageSetupDialog(this, WS, G.ActiveRect);
            if (d.ShowDialog() == true) { _book.RaiseChanged(); UpdateTitle(); G.Redraw(); }
        }

        // ===================================================================== about / options
        void ShowAbout()
        {
            var sp = new StackPanel { Margin = new Thickness(4) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(new Image { Source = UI.AppIconImage, Width = 48, Height = 48, Margin = new Thickness(0, 0, 12, 0) });
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(new TextBlock { Text = "Valco Spreadsheets", FontSize = 18, Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x39, 0x57)) });
            var ver = typeof(MainWindow).Assembly.GetName().Version;
            t.Children.Add(new TextBlock { Text = $"Version {ver?.ToString(3)}", Foreground = Brushes.Gray });
            head.Children.Add(t);
            sp.Children.Add(head);
            sp.Children.Add(new TextBlock { Text = "A spreadsheet program for Windows, from the Valco suite.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            sp.Children.Add(new TextBlock { Text = "© 2026 ItsRaiden", Margin = new Thickness(0, 0, 0, 6) });
            sp.Children.Add(new TextBlock { Text = "Opens and saves Excel workbooks (.xlsx, .xlsm, .xltx), Excel 97-2003 workbooks (.xls), CSV and text files. Workbook engine: ClosedXML (MIT); Excel 97-2003 files: NPOI (Apache 2.0).", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, MaxWidth = 380 });
            var w = new VistaWindow { Title = "About Valco Spreadsheets", Owner = this, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
            w.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => w.Close(), true, true)));
            w.ShowDialog();
        }

        void ShowOptions()
        {
            var d = new OptionsDialog(this);
            if (d.ShowDialog() == true)
            {
                OfficeTheme.Apply(Settings.Current.ColorScheme);
                FormulaBar.Visibility = Settings.Current.ShowFormulaBar ? Visibility.Visible : Visibility.Collapsed;
                StatusBar.Visibility = Settings.Current.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
                _autoRecover.Interval = TimeSpan.FromMinutes(Math.Max(1, Settings.Current.AutoRecoverMinutes));
                if (Settings.Current.AutoRecover) _autoRecover.Start(); else _autoRecover.Stop();
                G.Redraw();
            }
        }

        void CheckForUpdates() => Updates.CheckNow(this, "sheets", "Valco Spreadsheets");
    }
}
