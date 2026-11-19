using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using System.Xml.Linq;
using Microsoft.Win32;

namespace ValcoWord
{
    public partial class MainWindow
    {
        /// <summary>Opens a generated document (labels, merged letters, envelopes) in a new window.</summary>
        public void LoadGenerated(FlowDocument fd, PageSettings ps, string name)
        {
            SetDocument(fd, ps, null, null, null);
            _displayName = name;
            _dirty = true;
            UpdateTitle();
        }

        static MainWindow OpenNewWindow()
        {
            var w = new MainWindow();
            w.Show();
            return w;
        }

        // =====================================================================================
        //  Envelopes
        // =====================================================================================
        static readonly (string Name, double W, double H)[] EnvelopeSizes =
        {
            ("Size 10 (4 1/8 x 9 1/2 in)", 9.5, 4.125), ("Size 6 3/4 (3 5/8 x 6 1/2 in)", 6.5, 3.625), ("Monarch (3 7/8 x 7 1/2 in)", 7.5, 3.875),
            ("DL (110 x 220 mm)", 8.66, 4.33), ("C5 (162 x 229 mm)", 9.02, 6.38), ("C6 (114 x 162 mm)", 6.38, 4.49),
        };

        string ReturnAddress
        {
            get => Settings.Current.Custom.TryGetValue("returnAddress", out var r) ? r : (Settings.Current.UserName ?? Environment.UserName);
            set { Settings.Current.Custom["returnAddress"] = value; Settings.Save(); }
        }

        string GuessDeliveryAddress()
        {
            if (!Editor.Selection.IsEmpty) return Editor.Selection.Text.Trim();
            if (_ex.MergeRecords.Count > 0 && _ex.MergePreview) return AddressBlockFor(_ex.MergeRecords[Math.Max(0, Math.Min(_ex.MergeRecords.Count - 1, _ex.MergeIndex))]);
            // a letter's inside address: first block of 2-6 short lines
            var paras = Fmt.AllParagraphs(Editor.Document.Blocks).Select(Spans.Text).Take(12).ToList();
            var lines = new List<string>();
            foreach (var p in paras)
            {
                if (p.Trim().Length == 0) { if (lines.Count >= 2) break; lines.Clear(); continue; }
                if (p.Length > 60) { lines.Clear(); continue; }
                lines.Add(p.Trim());
            }
            return lines.Count >= 2 && lines.Count <= 6 ? string.Join("\n", lines) : "";
        }

        void EnvelopesDialog()
        {
            var d = new DialogBase(this, "Envelopes and Labels", 520);
            var tabs = new TabControl();
            // envelope tab
            var delivery = new TextBox { Height = 90, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = GuessDeliveryAddress(), FontFamily = new FontFamily(Th.Minor), FontSize = 13 };
            var ret = new TextBox { Height = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = ReturnAddress, FontFamily = new FontFamily(Th.Minor), FontSize = 13 };
            var omit = new CheckBox { Content = "Omit", Margin = new Thickness(0, 0, 0, 2) };
            var size = new ComboBox { Width = 230, ItemsSource = EnvelopeSizes.Select(e => e.Name).ToList(), SelectedIndex = 0 };
            var preview = new Canvas { Width = 190, Height = 90, Background = Brushes.White };
            void Prev()
            {
                preview.Children.Clear();
                var es = EnvelopeSizes[Math.Max(0, size.SelectedIndex)];
                double sc = Math.Min(190 / es.W, 90 / es.H), w = es.W * sc, h = es.H * sc;
                var env = new Border { Width = w, Height = h, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Brushes.White };
                preview.Children.Add(env);
                if (omit.IsChecked != true) { var r = new TextBlock { Text = ret.Text, FontSize = 5 }; Canvas.SetLeft(r, 4); Canvas.SetTop(r, 3); preview.Children.Add(r); }
                var dt = new TextBlock { Text = delivery.Text, FontSize = 6 }; Canvas.SetLeft(dt, w * 0.42); Canvas.SetTop(dt, h * 0.45); preview.Children.Add(dt);
            }
            delivery.TextChanged += (s, e) => Prev(); ret.TextChanged += (s, e) => Prev(); omit.Click += (s, e) => Prev(); size.SelectionChanged += (s, e) => Prev();
            var env1 = new StackPanel { Margin = new Thickness(8) };
            env1.Children.Add(new TextBlock { Text = "Delivery address:" }); env1.Children.Add(delivery);
            var retHead = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(omit, Dock.Right); retHead.Children.Add(omit); retHead.Children.Add(new TextBlock { Text = "Return address:" });
            env1.Children.Add(retHead); env1.Children.Add(ret);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var szc = new StackPanel(); szc.Children.Add(new TextBlock { Text = "Envelope size:" }); szc.Children.Add(size);
            row.Children.Add(szc); row.Children.Add(new Border { Margin = new Thickness(16, 0, 0, 0), Child = preview });
            env1.Children.Add(row);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var printB = UI.Btn("Print", (s, e) => { SaveRet(); var (fd, ps) = BuildEnvelope(delivery.Text, omit.IsChecked == true ? "" : ret.Text, EnvelopeSizes[Math.Max(0, size.SelectedIndex)]); try { Printing.Print(this, fd, ps, "Envelope"); } catch (Exception ex) { VistaDialog.Error(this, ex.Message); } });
            var addB = UI.Btn("New Document", (s, e) => { SaveRet(); var (fd, ps) = BuildEnvelope(delivery.Text, omit.IsChecked == true ? "" : ret.Text, EnvelopeSizes[Math.Max(0, size.SelectedIndex)]); d.Close(); OpenNewWindow().LoadGenerated(fd, ps, "Envelope"); });
            addB.Margin = new Thickness(8, 0, 0, 0);
            btns.Children.Add(printB); btns.Children.Add(addB);
            env1.Children.Add(btns);
            void SaveRet() { if (omit.IsChecked != true) ReturnAddress = ret.Text; }
            tabs.Items.Add(new TabItem { Header = "Envelopes", Content = env1 });
            tabs.Items.Add(new TabItem { Header = "Labels", Content = LabelsPanel(d, delivery.Text) });
            d.Content = UI.DialogLayout(tabs, UI.Strip(UI.Btn("Close", null, false, true)));
            d.Loaded += (s, e) => Prev();
            d.ShowDialog();
        }

        (FlowDocument, PageSettings) BuildEnvelope(string delivery, string ret, (string Name, double W, double H) size)
        {
            var ps = new PageSettings { Paper = "Custom", CustomW = size.H, CustomH = size.W, Landscape = true, MarginLeft = 0.4, MarginRight = 0.4, MarginTop = 0.35, MarginBottom = 0.3 };
            var fd = new FlowDocument();
            PrepareDocument(fd);
            var rp = NewPara(); rp.Margin = new Thickness(0, 0, 0, 0);
            AddLines(rp, ret);
            fd.Blocks.Add(rp);
            var dp = NewPara(); dp.Margin = new Thickness(size.W * 96 * 0.42 - 0.4 * 96, Math.Max(10, size.H * 96 * 0.42 - (ret.Split('\n').Length * 20)), 0, 0);
            dp.FontSize = 12 * Fmt.PxPerPt;
            AddLines(dp, delivery);
            fd.Blocks.Add(dp);
            return (fd, ps);
        }

        static void AddLines(Paragraph p, string text)
        {
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++) { if (i > 0) p.Inlines.Add(new LineBreak()); p.Inlines.Add(new Run(lines[i])); }
        }

        // =====================================================================================
        //  Labels
        // =====================================================================================
        static readonly (string Name, double W, double H, int Cols, int Rows, double Top, double Side, double GapX, double GapY, bool A4)[] LabelProducts =
        {
            ("Avery 5160 / 8160 Address (1 x 2 5/8 in, 30 per page)", 2.625, 1, 3, 10, 0.5, 0.19, 0.125, 0, false),
            ("Avery 5161 Address (1 x 4 in, 20 per page)", 4, 1, 2, 10, 0.5, 0.16, 0.19, 0, false),
            ("Avery 5162 Address (1 1/3 x 4 in, 14 per page)", 4, 1.333, 2, 7, 0.83, 0.16, 0.19, 0, false),
            ("Avery 5163 Shipping (2 x 4 in, 10 per page)", 4, 2, 2, 5, 0.5, 0.16, 0.19, 0, false),
            ("Avery 5164 Shipping (3 1/3 x 4 in, 6 per page)", 4, 3.333, 2, 3, 0.5, 0.16, 0.19, 0, false),
            ("Avery 5167 Return Address (1/2 x 1 3/4 in, 80 per page)", 1.75, 0.5, 4, 20, 0.5, 0.3, 0.3, 0, false),
            ("Avery L7160 (38.1 x 63.5 mm, 21 per page, A4)", 2.5, 1.5, 3, 7, 0.6, 0.28, 0.1, 0, true),
            ("Avery L7163 (38.1 x 99.1 mm, 14 per page, A4)", 3.9, 1.5, 2, 7, 0.6, 0.18, 0.1, 0, true),
        };

        void LabelsDialog() => EnvelopesDialog(1);
        void EnvelopesDialog(int tab)
        {
            var d = new DialogBase(this, "Envelopes and Labels", 520);
            d.Content = UI.DialogLayout(LabelsPanel(d, GuessDeliveryAddress()), UI.Strip(UI.Btn("Close", null, false, true)));
            d.ShowDialog();
        }

        UIElement LabelsPanel(Window owner, string address)
        {
            var addr = new TextBox { Height = 90, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = address, FontFamily = new FontFamily(Th.Minor), FontSize = 13 };
            var useRet = new CheckBox { Content = "Use return address", Margin = new Thickness(0, 4, 0, 0) };
            useRet.Click += (s, e) => addr.Text = useRet.IsChecked == true ? ReturnAddress : address;
            var full = new RadioButton { Content = "Full page of the same label", IsChecked = true };
            var single = new RadioButton { Content = "Single label   Row:" };
            var rowB = D.Box("1", 40); var colB = D.Box("1", 40);
            var srow = new StackPanel { Orientation = Orientation.Horizontal }; srow.Children.Add(single); srow.Children.Add(rowB); srow.Children.Add(new TextBlock { Text = " Column: ", VerticalAlignment = VerticalAlignment.Center }); srow.Children.Add(colB);
            var product = new ComboBox { Width = 400, ItemsSource = LabelProducts.Select(l => l.Name).ToList(), SelectedIndex = 0 };
            var mergeLabels = new CheckBox { Content = "Use mail merge recipients (one label per recipient)", IsEnabled = _ex.MergeRecords.Count > 0, IsChecked = _ex.MergeType == "Labels" && _ex.MergeRecords.Count > 0, Margin = new Thickness(0, 6, 0, 0) };
            var sp = new StackPanel { Margin = new Thickness(8) };
            sp.Children.Add(new TextBlock { Text = "Address:" }); sp.Children.Add(addr); sp.Children.Add(useRet);
            var pr = new StackPanel(); pr.Children.Add(full); pr.Children.Add(srow);
            sp.Children.Add(D.Group("Print", pr));
            sp.Children.Add(D.Group("Label", product));
            sp.Children.Add(mergeLabels);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            (FlowDocument, PageSettings) Build()
            {
                var lp = LabelProducts[Math.Max(0, product.SelectedIndex)];
                List<string> texts;
                if (mergeLabels.IsChecked == true) texts = IncludedRecords().Select(AddressBlockFor).ToList();
                else if (full.IsChecked == true) texts = Enumerable.Repeat(addr.Text, lp.Cols * lp.Rows).ToList();
                else
                {
                    int r = (int)Math.Max(1, D.ParseNum(rowB.Text) ?? 1), c = (int)Math.Max(1, D.ParseNum(colB.Text) ?? 1);
                    texts = Enumerable.Repeat("", lp.Cols * lp.Rows).ToList();
                    int idx = (Math.Min(lp.Rows, r) - 1) * lp.Cols + Math.Min(lp.Cols, c) - 1;
                    texts[idx] = addr.Text;
                }
                return BuildLabels(texts, lp);
            }
            var print = UI.Btn("Print", (s, e) => { var (fd, ps) = Build(); try { Printing.Print(this, fd, ps, "Labels"); } catch (Exception ex) { VistaDialog.Error(this, ex.Message); } });
            var nd = UI.Btn("New Document", (s, e) => { var (fd, ps) = Build(); owner.Close(); OpenNewWindow().LoadGenerated(fd, ps, "Labels"); });
            nd.Margin = new Thickness(8, 0, 0, 0);
            btns.Children.Add(print); btns.Children.Add(nd);
            sp.Children.Add(btns);
            return sp;
        }

        (FlowDocument, PageSettings) BuildLabels(List<string> texts, (string Name, double W, double H, int Cols, int Rows, double Top, double Side, double GapX, double GapY, bool A4) lp)
        {
            var ps = new PageSettings { Paper = lp.A4 ? "A4" : "Letter", MarginTop = lp.Top, MarginBottom = 0.2, MarginLeft = lp.Side, MarginRight = 0.1 };
            var fd = new FlowDocument();
            PrepareDocument(fd);
            int perPage = lp.Cols * lp.Rows;
            for (int start = 0; start < Math.Max(1, texts.Count); start += perPage)
            {
                var t = new Table { CellSpacing = 0, Margin = new Thickness(0), BreakPageBefore = start > 0 };
                for (int c = 0; c < lp.Cols; c++)
                {
                    t.Columns.Add(new TableColumn { Width = new GridLength(lp.W * 96) });
                    if (c < lp.Cols - 1 && lp.GapX > 0) t.Columns.Add(new TableColumn { Width = new GridLength(lp.GapX * 96) });
                }
                var g = new TableRowGroup();
                for (int r = 0; r < lp.Rows; r++)
                {
                    var row = new TableRow();
                    for (int c = 0; c < lp.Cols; c++)
                    {
                        int i = start + r * lp.Cols + c;
                        var p = NewCellParagraph();
                        p.LineHeight = 14 * Fmt.PxPerPt; p.FontSize = 10.5 * Fmt.PxPerPt;
                        if (i < texts.Count) AddLines(p, texts[i]);
                        var cell = new TableCell(p) { BorderThickness = new Thickness(0), Padding = new Thickness(8, 6, 4, 0) };
                        // fixed label height: pad the paragraph so rows don't grow or shrink
                        p.Margin = new Thickness(0);
                        row.Cells.Add(cell);
                        if (c < lp.Cols - 1 && lp.GapX > 0) row.Cells.Add(new TableCell(NewCellParagraph()) { BorderThickness = new Thickness(0), Padding = new Thickness(0) });
                    }
                    g.Rows.Add(row);
                    // emulate the row height with a minimum-height spacer in the first cell
                    var first = row.Cells[0].Blocks.FirstBlock as Paragraph;
                    if (first != null) first.MinHeight(lp.H * 96 - 8);
                }
                t.RowGroups.Add(g);
                fd.Blocks.Add(t);
                if (texts.Count == 0) break;
            }
            return (fd, ps);
        }

        // =====================================================================================
        //  Mail merge: recipients
        // =====================================================================================
        static readonly string[] DefaultMergeFields = { "Title", "First Name", "Last Name", "Company Name", "Address Line 1", "Address Line 2", "City", "State", "ZIP Code", "Country or Region", "Home Phone", "Work Phone", "E-mail Address" };

        void StartMerge(string type)
        {
            _ex.MergeType = type;
            if (type == null)
            {
                SetMergePreview(false);
                _ex.MergeRecords.Clear(); _ex.MergeFields.Clear(); _ex.MergeSource = null;
                Flash("This is now a normal Valco Word document.");
            }
            else Flash($"Mail merge: {type}. Next, click Select Recipients.");
            MarkDirty();
        }

        void TypeNewList()
        {
            var fields = _ex.MergeFields.Count > 0 && _ex.MergeSource == null ? _ex.MergeFields.ToList() : DefaultMergeFields.ToList();
            var records = _ex.MergeSource == null ? _ex.MergeRecords.Select(r => new Dictionary<string, string>(r)).ToList() : new List<Dictionary<string, string>>();
            if (!RecipientEditor("New Address List", fields, records)) return;
            var dlg = new SaveFileDialog { Title = "Save Address List", Filter = "Comma Separated Values (*.csv)|*.csv", FileName = "Address List", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
            string path = null;
            if (dlg.ShowDialog(this) == true)
            {
                path = dlg.FileName;
                try { WriteCsv(path, fields, records); } catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The list couldn't be saved."); path = null; }
            }
            SetRecipients(fields, records, path);
        }

        bool RecipientEditor(string title, List<string> fields, List<Dictionary<string, string>> records)
        {
            var d = new DialogBase(this, title, 760);
            var table = new DataTable();
            foreach (var f in fields) table.Columns.Add(f);
            foreach (var r in records) table.Rows.Add(fields.Select(f => (object)(r.TryGetValue(f, out var v) ? v : "")).ToArray());
            if (table.Rows.Count == 0) table.Rows.Add(fields.Select(f => (object)"").ToArray());
            var grid = new DataGrid { ItemsSource = table.DefaultView, Height = 300, CanUserAddRows = true, AutoGenerateColumns = true, HeadersVisibility = DataGridHeadersVisibility.All, GridLinesVisibility = DataGridGridLinesVisibility.All, HorizontalGridLinesBrush = Brushes.LightGray, VerticalGridLinesBrush = Brushes.LightGray };
            var newEntry = UI.Btn("New Entry", (s, e) => { table.Rows.Add(fields.Select(f => (object)"").ToArray()); });
            var del = UI.Btn("Delete Entry", (s, e) => { if (grid.SelectedItem is DataRowView v) table.Rows.Remove(v.Row); });
            var cols = UI.Btn("Customize Columns...", (s, e) =>
            {
                var cd = new DialogBase(d, "Customize Address List", 340);
                var list = new ListBox { Height = 220, ItemsSource = fields.ToList() };
                void Refill() => list.ItemsSource = fields.ToList();
                Button B(string t, Action a) { var b = UI.Btn(t, (o, a2) => { a(); Refill(); }); b.Margin = new Thickness(0, 0, 0, 4); return b; }
                var side = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
                side.Children.Add(B("Add...", () => { var n = InputDialog.Ask(cd, "Add Field", "Type a name for your field:", ""); if (!string.IsNullOrWhiteSpace(n) && !fields.Contains(n.Trim())) { fields.Add(n.Trim()); table.Columns.Add(n.Trim()); } }));
                side.Children.Add(B("Delete", () => { if (list.SelectedItem is string n) { fields.Remove(n); table.Columns.Remove(n); } }));
                side.Children.Add(B("Rename...", () => { if (list.SelectedItem is string n) { var nn = InputDialog.Ask(cd, "Rename Field", "To:", n); if (!string.IsNullOrWhiteSpace(nn) && !fields.Contains(nn.Trim())) { fields[fields.IndexOf(n)] = nn.Trim(); table.Columns[n].ColumnName = nn.Trim(); } } }));
                side.Children.Add(B("Move Up", () => { if (list.SelectedItem is string n && fields.IndexOf(n) > 0) { int i = fields.IndexOf(n); fields.RemoveAt(i); fields.Insert(i - 1, n); table.Columns[n].SetOrdinal(i - 1); } }));
                side.Children.Add(B("Move Down", () => { if (list.SelectedItem is string n && fields.IndexOf(n) < fields.Count - 1) { int i = fields.IndexOf(n); fields.RemoveAt(i); fields.Insert(i + 1, n); table.Columns[n].SetOrdinal(i + 1); } }));
                var r = new StackPanel { Orientation = Orientation.Horizontal }; r.Children.Add(list); r.Children.Add(side);
                list.Width = 200;
                cd.Content = UI.DialogLayout(r, UI.Strip(UI.Btn("OK", null, true, true)));
                cd.ShowDialog();
                grid.ItemsSource = null; grid.ItemsSource = table.DefaultView;
            });
            foreach (var b in new[] { newEntry, del, cols }) b.Margin = new Thickness(0, 0, 6, 0);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) }; bar.Children.Add(newEntry); bar.Children.Add(del); bar.Children.Add(cols);
            var sp = new StackPanel(); sp.Children.Add(new TextBlock { Text = "Type recipient information in the table. To add more entries, click New Entry.", Margin = new Thickness(0, 0, 0, 6) }); sp.Children.Add(grid); sp.Children.Add(bar);
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => { grid.CommitEdit(DataGridEditingUnit.Row, true); d.DialogResult = true; }, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return false;
            records.Clear();
            foreach (DataRow row in table.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                var rec = fields.ToDictionary(f => f, f => row[f]?.ToString() ?? "");
                if (rec.Values.Any(v => v.Trim().Length > 0)) records.Add(rec);
            }
            return true;
        }

        void UseExistingList()
        {
            var dlg = new OpenFileDialog { Title = "Select Data Source", Filter = "All Data Sources (*.csv;*.txt;*.tsv;*.xlsx)|*.csv;*.txt;*.tsv;*.xlsx|Comma Separated Values (*.csv)|*.csv|Excel Workbooks (*.xlsx)|*.xlsx|Text Files (*.txt;*.tsv)|*.txt;*.tsv", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                List<string[]> rows = dlg.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadXlsx(dlg.FileName) : ReadDelimited(dlg.FileName);
                rows = rows.Where(r => r.Any(c => c.Trim().Length > 0)).ToList();
                if (rows.Count == 0) { VistaDialog.Error(this, "That file doesn't contain any rows."); return; }
                var fields = rows[0].Select((h, i) => string.IsNullOrWhiteSpace(h) ? "Column " + (i + 1) : h.Trim()).ToList();
                var records = rows.Skip(1).Select(r => fields.Select((f, i) => (f, v: i < r.Length ? r[i] : "")).GroupBy(x => x.f).ToDictionary(g => g.Key, g => g.First().v)).ToList();
                SetRecipients(fields, records, dlg.FileName);
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "That data source couldn't be opened."); }
        }

        void SetRecipients(List<string> fields, List<Dictionary<string, string>> records, string source)
        {
            _ex.MergeFields = fields; _ex.MergeRecords = records; _ex.MergeSource = source; _ex.MergeExcluded.Clear(); _ex.MergeIndex = 0;
            if (_ex.MergeType == null) _ex.MergeType = "Letters";
            MarkDirty();
            Flash($"{records.Count} recipient{(records.Count == 1 ? "" : "s")} loaded. Insert merge fields, then click Preview Results.");
            if (_ex.MergePreview) SetMergePreview(true);
        }

        static List<string[]> ReadDelimited(string path)
        {
            var text = File.ReadAllText(path);
            char sep = path.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) || (text.Split('\n')[0].Count(c => c == '\t') > text.Split('\n')[0].Count(c => c == ',')) ? '\t' : text.Split('\n')[0].Count(c => c == ';') > text.Split('\n')[0].Count(c => c == ',') ? ';' : ',';
            var rows = new List<string[]>();
            var cur = new List<string>(); var cell = new StringBuilder(); bool q = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (q)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else q = false; }
                    else cell.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == sep) { cur.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\n') { cur.Add(cell.ToString().TrimEnd('\r')); cell.Clear(); rows.Add(cur.ToArray()); cur.Clear(); }
                else cell.Append(c);
            }
            if (cell.Length > 0 || cur.Count > 0) { cur.Add(cell.ToString().TrimEnd('\r')); rows.Add(cur.ToArray()); }
            return rows;
        }

        static void WriteCsv(string path, List<string> fields, List<Dictionary<string, string>> records)
        {
            string Q(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", fields.Select(Q)));
            foreach (var r in records) sb.AppendLine(string.Join(",", fields.Select(f => Q(r.TryGetValue(f, out var v) ? v ?? "" : ""))));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>Reads the first worksheet of an .xlsx file (values only).</summary>
        static List<string[]> ReadXlsx(string path)
        {
            using var pkg = Package.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var shared = new List<string>();
            var ssPart = pkg.GetParts().FirstOrDefault(p => p.Uri.OriginalString.EndsWith("sharedStrings.xml"));
            if (ssPart != null) using (var s = ssPart.GetStream()) shared = XDocument.Load(s).Root.Elements(S + "si").Select(si => string.Concat(si.Descendants(S + "t").Select(t => t.Value))).ToList();
            var wbPart = pkg.GetParts().First(p => p.Uri.OriginalString.EndsWith("/workbook.xml"));
            XDocument wb; using (var s = wbPart.GetStream()) wb = XDocument.Load(s);
            var firstSheet = wb.Root.Element(S + "sheets")?.Elements(S + "sheet").FirstOrDefault();
            var relId = (string)firstSheet?.Attribute(R + "id");
            var rel = wbPart.GetRelationships().FirstOrDefault(r => r.Id == relId);
            var sheetUri = rel != null ? PackUriHelper.ResolvePartUri(wbPart.Uri, rel.TargetUri) : new Uri("/xl/worksheets/sheet1.xml", UriKind.Relative);
            XDocument sheet; using (var s = pkg.GetPart(sheetUri).GetStream()) sheet = XDocument.Load(s);
            var rows = new List<string[]>();
            foreach (var row in sheet.Descendants(S + "row"))
            {
                var cells = new SortedDictionary<int, string>();
                foreach (var c in row.Elements(S + "c"))
                {
                    var refAttr = (string)c.Attribute("r") ?? "";
                    int col = 0; foreach (var ch in refAttr.TakeWhile(char.IsLetter)) col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
                    string t = (string)c.Attribute("t"), v = c.Element(S + "v")?.Value ?? string.Concat(c.Descendants(S + "t").Select(x => x.Value));
                    if (t == "s" && int.TryParse(v, out var si) && si < shared.Count) v = shared[si];
                    cells[Math.Max(0, col - 1)] = v;
                }
                int max = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
                rows.Add(Enumerable.Range(0, max).Select(i => cells.TryGetValue(i, out var v) ? v : "").ToArray());
            }
            return rows;
        }

        IEnumerable<Dictionary<string, string>> IncludedRecords() => _ex.MergeRecords.Where((r, i) => !_ex.MergeExcluded.Contains(i));

        void EditRecipientList()
        {
            if (_ex.MergeRecords.Count == 0) { Flash("Select recipients first (Select Recipients ▸ Type New List or Use Existing List)."); return; }
            var d = new DialogBase(this, "Mail Merge Recipients", 780);
            var table = new DataTable();
            table.Columns.Add("✓", typeof(bool));
            foreach (var f in _ex.MergeFields) table.Columns.Add(f);
            for (int i = 0; i < _ex.MergeRecords.Count; i++)
            {
                var r = _ex.MergeRecords[i];
                table.Rows.Add(new object[] { !_ex.MergeExcluded.Contains(i) }.Concat(_ex.MergeFields.Select(f => (object)(r.TryGetValue(f, out var v) ? v : ""))).ToArray());
            }
            var view = table.DefaultView;
            var grid = new DataGrid { ItemsSource = view, Height = 320, CanUserAddRows = false, AutoGenerateColumns = true, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HorizontalGridLinesBrush = Brushes.LightGray };
            var filter = D.Box("", 220);
            filter.TextChanged += (s, e) =>
            {
                var f = filter.Text.Replace("'", "''");
                view.RowFilter = f.Length == 0 ? "" : string.Join(" OR ", _ex.MergeFields.Select(c => $"[{c.Replace("]", "\\]")}] LIKE '%{f}%'"));
            };
            var all = UI.Btn("Select All", (s, e) => { foreach (DataRow r in table.Rows) r[0] = true; });
            var none = UI.Btn("Clear All", (s, e) => { foreach (DataRow r in table.Rows) r[0] = false; });
            var edit = UI.Btn("Edit Data Source...", (s, e) =>
            {
                var fields = _ex.MergeFields.ToList(); var recs = _ex.MergeRecords.Select(r => new Dictionary<string, string>(r)).ToList();
                if (RecipientEditor("Edit Data Source", fields, recs))
                {
                    if (_ex.MergeSource != null && _ex.MergeSource.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) try { WriteCsv(_ex.MergeSource, fields, recs); } catch { }
                    _ex.MergeFields = fields; _ex.MergeRecords = recs; _ex.MergeExcluded.Clear(); d.Close(); EditRecipientList();
                }
            });
            foreach (var b in new[] { all, none, edit }) b.Margin = new Thickness(6, 0, 0, 0);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            bar.Children.Add(new TextBlock { Text = "Find/filter:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }); bar.Children.Add(filter); bar.Children.Add(all); bar.Children.Add(none); bar.Children.Add(edit);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = (_ex.MergeSource != null ? "Data source: " + System.IO.Path.GetFileName(_ex.MergeSource) + ".  " : "") + "Use the check boxes to add or remove recipients from the merge. Click a column heading to sort.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            sp.Children.Add(bar); sp.Children.Add(grid);
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            _ex.MergeExcluded.Clear();
            for (int i = 0; i < table.Rows.Count; i++) if (!(table.Rows[i][0] is bool b && b)) _ex.MergeExcluded.Add(i);
            MarkDirty();
            if (_ex.MergePreview) SetMergePreview(true);
        }

        // =====================================================================================
        //  Mail merge: fields
        // =====================================================================================
        static readonly (string Key, string[] Aliases)[] FieldAliases =
        {
            ("Courtesy Title", new[] { "title", "courtesy title", "prefix", "salutation" }), ("First Name", new[] { "first name", "firstname", "first", "given name" }),
            ("Last Name", new[] { "last name", "lastname", "last", "surname", "family name" }), ("Company", new[] { "company name", "company", "organization", "organisation" }),
            ("Address 1", new[] { "address line 1", "address 1", "address1", "address", "street", "street address" }), ("Address 2", new[] { "address line 2", "address 2", "address2" }),
            ("City", new[] { "city", "town" }), ("State", new[] { "state", "province", "state/province", "region" }), ("Postal Code", new[] { "zip code", "zip", "postal code", "postcode", "postal" }),
            ("Country", new[] { "country or region", "country" }), ("Full Name", new[] { "full name", "name" }),
        };

        static string Field(Dictionary<string, string> rec, string key)
        {
            var aliases = FieldAliases.FirstOrDefault(a => a.Key == key).Aliases ?? new[] { key.ToLowerInvariant() };
            foreach (var a in aliases)
            {
                var hit = rec.Keys.FirstOrDefault(k => string.Equals(k.Trim(), a, StringComparison.OrdinalIgnoreCase) || string.Equals(k.Replace("_", " ").Trim(), a, StringComparison.OrdinalIgnoreCase));
                if (hit != null && !string.IsNullOrWhiteSpace(rec[hit])) return rec[hit].Trim();
            }
            return "";
        }

        string AddressBlockFor(Dictionary<string, string> rec)
        {
            string fmt = _ex.Custom.TryGetValue("addrName", out var f) ? f : "full";
            bool company = !_ex.Custom.TryGetValue("addrCompany", out var c) || c != "0";
            bool country = _ex.Custom.TryGetValue("addrCountry", out var co) && co == "1";
            var first = Field(rec, "First Name"); var last = Field(rec, "Last Name"); var title = Field(rec, "Courtesy Title");
            string name = fmt switch
            {
                "title" => string.Join(" ", new[] { title, first, last }.Where(x => x.Length > 0)),
                "first" => first,
                "lastfirst" => last.Length > 0 && first.Length > 0 ? last + ", " + first : last + first,
                _ => string.Join(" ", new[] { first, last }.Where(x => x.Length > 0)),
            };
            if (name.Length == 0) name = Field(rec, "Full Name");
            var lines = new List<string>();
            if (name.Length > 0) lines.Add(name);
            if (company && Field(rec, "Company") is string cmp && cmp.Length > 0) lines.Add(cmp);
            foreach (var k in new[] { "Address 1", "Address 2" }) if (Field(rec, k) is string a && a.Length > 0) lines.Add(a);
            var cityLine = string.Join(" ", new[] { Field(rec, "City") + (Field(rec, "State").Length > 0 && Field(rec, "City").Length > 0 ? "," : ""), Field(rec, "State"), Field(rec, "Postal Code") }.Where(x => x.Trim(',').Length > 0));
            if (cityLine.Length > 0) lines.Add(cityLine);
            if (country && Field(rec, "Country") is string ctry && ctry.Length > 0) lines.Add(ctry);
            return string.Join("\n", lines);
        }

        string GreetingFor(Dictionary<string, string> rec)
        {
            string sal = _ex.Custom.TryGetValue("greetSal", out var s) ? s : "Dear";
            string fmt = _ex.Custom.TryGetValue("greetName", out var f) ? f : "title-last";
            string punct = _ex.Custom.TryGetValue("greetPunct", out var p) ? p : ",";
            string fallback = _ex.Custom.TryGetValue("greetFallback", out var fb) ? fb : "Dear Sir or Madam,";
            var first = Field(rec, "First Name"); var last = Field(rec, "Last Name"); var title = Field(rec, "Courtesy Title");
            string name = fmt switch
            {
                "first" => first,
                "full" => string.Join(" ", new[] { first, last }.Where(x => x.Length > 0)),
                "title-full" => string.Join(" ", new[] { title, first, last }.Where(x => x.Length > 0)),
                _ => title.Length > 0 && last.Length > 0 ? title + " " + last : string.Join(" ", new[] { first, last }.Where(x => x.Length > 0)),
            };
            return name.Length == 0 ? fallback : sal + " " + name + punct;
        }

        string MergeValue(string field, Dictionary<string, string> rec)
        {
            if (field == "AddressBlock") return AddressBlockFor(rec);
            if (field == "GreetingLine") return GreetingFor(rec);
            if (rec.TryGetValue(field, out var v)) return v;
            var hit = rec.Keys.FirstOrDefault(k => string.Equals(k, field, StringComparison.OrdinalIgnoreCase));
            return hit != null ? rec[hit] : "";
        }

        static string FieldPlaceholder(string name) => "«" + name + "»";

        void InsertMergeField(string name)
        {
            Editor.BeginChange();
            if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
            var ip = Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var s = new Span(new Run(FieldPlaceholder(name)), ip) { Tag = new MergeFieldInfo { Name = name } };
            if (TbHighlightFields?.IsChecked == true) s.Background = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9));
            Editor.CaretPosition = s.ElementEnd;
            Editor.EndChange();
            if (_ex.MergePreview) SetMergePreview(true);
            MarkDirty(); Editor.Focus();
        }

        ContextMenu MergeFieldMenu()
        {
            var m = RB.Menu();
            if (_ex.MergeFields.Count == 0)
            {
                m.Items.Add(new MenuItem { Header = "Select recipients first to see their fields.", IsEnabled = false });
                foreach (var f in DefaultMergeFields) { var n = f; m.Items.Add(RB.MI(n, () => InsertMergeField(n))); }
                return m;
            }
            foreach (var f in _ex.MergeFields) { var n = f; m.Items.Add(RB.MI(n, () => InsertMergeField(n), "IconMergeField")); }
            return m;
        }

        void InsertAddressBlock()
        {
            var d = new DialogBase(this, "Insert Address Block", 460);
            var nameFmt = new ListBox { Height = 100, ItemsSource = new[] { "Josh Randall Jr.", "Mr. Josh Randall Jr.", "Josh", "Randall, Josh" }, SelectedIndex = 0 };
            var keys = new[] { "full", "title", "first", "lastfirst" };
            if (_ex.Custom.TryGetValue("addrName", out var cur)) nameFmt.SelectedIndex = Math.Max(0, Array.IndexOf(keys, cur));
            var company = new CheckBox { Content = "Insert company name", IsChecked = !_ex.Custom.TryGetValue("addrCompany", out var c) || c != "0" };
            var country = new CheckBox { Content = "Insert country/region in address", IsChecked = _ex.Custom.TryGetValue("addrCountry", out var co) && co == "1" };
            var preview = new TextBlock { FontFamily = new FontFamily(Th.Minor), Margin = new Thickness(6), MinHeight = 80 };
            void Prev()
            {
                _ex.Custom["addrName"] = keys[Math.Max(0, nameFmt.SelectedIndex)]; _ex.Custom["addrCompany"] = company.IsChecked == true ? "1" : "0"; _ex.Custom["addrCountry"] = country.IsChecked == true ? "1" : "0";
                var rec = _ex.MergeRecords.FirstOrDefault() ?? new Dictionary<string, string> { ["First Name"] = "Josh", ["Last Name"] = "Randall", ["Company Name"] = "Valco Inc.", ["Address Line 1"] = "123 Main St.", ["City"] = "Truro", ["State"] = "NS", ["ZIP Code"] = "B2N 1A1", ["Country or Region"] = "Canada", ["Title"] = "Mr." };
                preview.Text = AddressBlockFor(rec);
            }
            nameFmt.SelectionChanged += (s, e) => Prev(); company.Click += (s, e) => Prev(); country.Click += (s, e) => Prev();
            var left = new StackPanel { Width = 210 };
            left.Children.Add(new TextBlock { Text = "Insert recipient's name in this format:" }); left.Children.Add(nameFmt); left.Children.Add(company); left.Children.Add(country);
            var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Width = 200 };
            right.Children.Add(new TextBlock { Text = "Preview:" }); right.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Background = Brushes.White, Child = preview });
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(left); row.Children.Add(right);
            d.Content = UI.DialogLayout(row, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            d.Loaded += (s, e) => Prev();
            if (d.ShowDialog() == true) InsertMergeField("AddressBlock");
        }

        void InsertGreetingLine()
        {
            var d = new DialogBase(this, "Insert Greeting Line", 460);
            var sal = new ComboBox { Width = 70, IsEditable = true, ItemsSource = new[] { "Dear", "To", "Hello", "Hi" }, Text = _ex.Custom.TryGetValue("greetSal", out var s0) ? s0 : "Dear" };
            var keys = new[] { "title-last", "first", "full", "title-full" };
            var name = new ComboBox { Width = 140, ItemsSource = new[] { "Mr. Randall", "Josh", "Josh Randall", "Mr. Josh Randall" }, SelectedIndex = Math.Max(0, Array.IndexOf(keys, _ex.Custom.TryGetValue("greetName", out var n0) ? n0 : "title-last")) };
            var punct = new ComboBox { Width = 50, ItemsSource = new[] { ",", ":", "" }, SelectedItem = _ex.Custom.TryGetValue("greetPunct", out var p0) ? p0 : "," };
            var fallback = new ComboBox { Width = 200, IsEditable = true, ItemsSource = new[] { "Dear Sir or Madam,", "To Whom It May Concern:", "Dear Friend," }, Text = _ex.Custom.TryGetValue("greetFallback", out var f0) ? f0 : "Dear Sir or Madam," };
            var preview = new TextBlock { FontFamily = new FontFamily(Th.Minor), Margin = new Thickness(6) };
            void Prev()
            {
                _ex.Custom["greetSal"] = sal.Text; _ex.Custom["greetName"] = keys[Math.Max(0, name.SelectedIndex)]; _ex.Custom["greetPunct"] = (string)punct.SelectedItem ?? ","; _ex.Custom["greetFallback"] = fallback.Text;
                var rec = _ex.MergeRecords.FirstOrDefault() ?? new Dictionary<string, string> { ["First Name"] = "Josh", ["Last Name"] = "Randall", ["Title"] = "Mr." };
                preview.Text = GreetingFor(rec);
            }
            sal.SelectionChanged += (s, e) => Dispatcher.BeginInvoke(new Action(Prev)); sal.LostFocus += (s, e) => Prev();
            name.SelectionChanged += (s, e) => Prev(); punct.SelectionChanged += (s, e) => Prev();
            fallback.SelectionChanged += (s, e) => Dispatcher.BeginInvoke(new Action(Prev)); fallback.LostFocus += (s, e) => Prev();
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(sal); row.Children.Add(name); row.Children.Add(punct);
            foreach (FrameworkElement x in row.Children) x.Margin = new Thickness(0, 0, 6, 0);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "Greeting line format:" }); sp.Children.Add(row);
            sp.Children.Add(new TextBlock { Text = "Greeting line for invalid recipient names:", Margin = new Thickness(0, 8, 0, 0) }); sp.Children.Add(fallback);
            sp.Children.Add(new TextBlock { Text = "Preview:", Margin = new Thickness(0, 8, 0, 0) }); sp.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Background = Brushes.White, Child = preview });
            d.Content = UI.DialogLayout(sp, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            d.Loaded += (s, e) => Prev();
            if (d.ShowDialog() == true) InsertMergeField("GreetingLine");
        }

        // =====================================================================================
        //  Mail merge: preview and finish
        // =====================================================================================
        void SetMergePreview(bool on)
        {
            if (on && _ex.MergeRecords.Count == 0) { Flash("Select recipients first."); if (TbPreviewMerge != null) TbPreviewMerge.IsChecked = false; on = false; }
            _ex.MergePreview = on;
            if (TbPreviewMerge != null && TbPreviewMerge.IsChecked != on) TbPreviewMerge.IsChecked = on;
            var rec = on ? _ex.MergeRecords[Math.Max(0, Math.Min(_ex.MergeRecords.Count - 1, _ex.MergeIndex))] : null;
            bool was = _loading; _loading = true;
            Editor.BeginChange();
            foreach (var s in Spans.WithTag<MergeFieldInfo>(Editor.Document).ToList())
            {
                var name = ((MergeFieldInfo)s.Tag).Name;
                var text = rec == null ? FieldPlaceholder(name) : MergeValue(name, rec);
                s.Inlines.Clear();
                var lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++) { if (i > 0) s.Inlines.Add(new LineBreak()); s.Inlines.Add(new Run(lines[i])); }
            }
            Editor.EndChange();
            _loading = was;
            if (TxtRecord != null) TxtRecord.Text = (_ex.MergeIndex + 1).ToString();
            ScheduleLayout();
        }

        void HighlightMergeFields(bool on)
        {
            bool was = _loading; _loading = true;
            foreach (var s in Spans.WithTag<MergeFieldInfo>(Editor.Document)) { if (on) s.Background = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)); else s.ClearValue(TextElement.BackgroundProperty); }
            _loading = was;
        }

        void GoToRecord(int i)
        {
            if (_ex.MergeRecords.Count == 0) { Flash("Select recipients first."); return; }
            _ex.MergeIndex = Math.Max(0, Math.Min(_ex.MergeRecords.Count - 1, i));
            if (TxtRecord != null) TxtRecord.Text = (_ex.MergeIndex + 1).ToString();
            SetMergePreview(true);
        }

        void FindRecipient()
        {
            if (_ex.MergeRecords.Count == 0) { Flash("Select recipients first."); return; }
            var d = new DialogBase(this, "Find Entry", 360);
            var find = D.Box("", 260);
            var field = new ComboBox { Width = 200, ItemsSource = new[] { "All fields" }.Concat(_ex.MergeFields).ToList(), SelectedIndex = 0 };
            var msg = new TextBlock { Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 0) };
            int from = _ex.MergeIndex;
            var next = UI.Btn("Find Next", (s, e) =>
            {
                var q = find.Text.Trim(); if (q.Length == 0) return;
                for (int k = 1; k <= _ex.MergeRecords.Count; k++)
                {
                    int i = (from + k) % _ex.MergeRecords.Count;
                    var rec = _ex.MergeRecords[i];
                    bool hit = field.SelectedIndex == 0 ? rec.Values.Any(v => v?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) : (rec.TryGetValue((string)field.SelectedItem, out var v) && v?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
                    if (hit) { from = i; GoToRecord(i); msg.Text = $"Showing recipient {i + 1}."; return; }
                }
                msg.Text = "No matching recipient was found.";
            }, true);
            var sp = new StackPanel(); sp.Children.Add(D.Form(("Find:", find), ("Look in:", field))); sp.Children.Add(msg);
            d.Content = UI.DialogLayout(sp, UI.Strip(next, UI.Btn("Close", null, false, true)));
            d.ShowDialog();
        }

        void FinishMerge(bool print) => FinishMerge(print ? "print" : "edit");
        void FinishMerge(string mode)
        {
            if (_ex.MergeRecords.Count == 0) { Flash("Select recipients first."); return; }
            if (!Spans.WithTag<MergeFieldInfo>(Editor.Document).Any() && _ex.MergeType != "Labels") { Flash("Insert at least one merge field first."); return; }
            var records = IncludedRecords().ToList();
            var d = new DialogBase(this, mode == "email" ? "Merge to E-mail" : mode == "print" ? "Merge to Printer" : "Merge to New Document", 340);
            var all = new RadioButton { Content = "All", IsChecked = true }; var cur = new RadioButton { Content = "Current record" };
            var fromB = D.Box("1", 50); var toB = D.Box(records.Count.ToString(), 50);
            var rng = new RadioButton { Content = "From:" };
            var r3 = new StackPanel { Orientation = Orientation.Horizontal }; r3.Children.Add(rng); r3.Children.Add(fromB); r3.Children.Add(new TextBlock { Text = " To: ", VerticalAlignment = VerticalAlignment.Center }); r3.Children.Add(toB);
            var sp = new StackPanel(); sp.Children.Add(all); sp.Children.Add(cur); sp.Children.Add(r3);
            var emailField = new ComboBox { Width = 200, ItemsSource = _ex.MergeFields, SelectedItem = _ex.MergeFields.FirstOrDefault(f => f.ToLowerInvariant().Contains("mail")) };
            var subject = D.Box(_docTitle ?? DisplayName, 200);
            var root = new StackPanel();
            if (mode == "email") root.Children.Add(D.Group("Message options", D.Form(("To:", emailField), ("Subject line:", subject))));
            root.Children.Add(D.Group("Merge records", sp));
            d.Content = UI.DialogLayout(root, UI.Strip(UI.Btn("OK", (s, e) => d.DialogResult = true, true), UI.Btn("Cancel", null, false, true)));
            if (d.ShowDialog() != true) return;
            if (cur.IsChecked == true) records = new List<Dictionary<string, string>> { _ex.MergeRecords[_ex.MergeIndex] };
            else if (rng.IsChecked == true) { int f = (int)Math.Max(1, D.ParseNum(fromB.Text) ?? 1), t = (int)Math.Min(records.Count, D.ParseNum(toB.Text) ?? records.Count); records = records.Skip(f - 1).Take(Math.Max(0, t - f + 1)).ToList(); }
            if (records.Count == 0) return;

            bool wasPreview = _ex.MergePreview; int wasIndex = _ex.MergeIndex;
            var outDoc = new FlowDocument();
            PrepareDocument(outDoc);
            bool directory = _ex.MergeType == "Directory";
            int sent = 0;
            try
            {
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                foreach (var rec in records)
                {
                    _ex.MergeIndex = _ex.MergeRecords.IndexOf(rec);
                    SetMergePreview(true);
                    if (mode == "email")
                    {
                        var to = emailField.SelectedItem is string ef && rec.TryGetValue(ef, out var addr) ? addr : "";
                        if (to.Trim().Length == 0) continue;
                        if (SendMail(new[] { to.Trim() }, subject.Text, DocFormats.PlainText(Editor.Document), null, false)) sent++;
                        continue;
                    }
                    var ms = new MemoryStream();
                    new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Save(ms, DataFormats.XamlPackage);
                    ms.Position = 0;
                    var holder = new FlowDocument();
                    new TextRange(holder.ContentStart, holder.ContentEnd).Load(ms, DataFormats.XamlPackage);
                    var blocks = holder.Blocks.ToList();
                    if (!directory && outDoc.Blocks.Count > 0 && blocks.Count > 0) blocks[0].BreakPageBefore = true;
                    foreach (var b in blocks) { holder.Blocks.Remove(b); outDoc.Blocks.Add(b); }
                }
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _ex.MergeIndex = wasIndex;
                SetMergePreview(wasPreview);
            }
            if (mode == "email") { Flash($"Sent {sent} message{(sent == 1 ? "" : "s")}."); return; }
            if (mode == "print")
            {
                try { Printing.Print(this, outDoc, _page, DisplayName + " (merged)"); } catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The merged documents couldn't be printed."); }
                return;
            }
            OpenNewWindow().LoadGenerated(outDoc, _page.Clone(), (_ex.MergeType == "Directory" ? "Directory" : "Letters") + "1");
        }
    }

    static class ParagraphExt
    {
        /// <summary>Gives a paragraph a minimum height by padding its bottom.</summary>
        public static void MinHeight(this Paragraph p, double px)
        {
            double lines = Math.Max(1, p.Inlines.OfType<LineBreak>().Count() + 1);
            double used = lines * (double.IsNaN(p.LineHeight) ? p.FontSize * 1.2 : p.LineHeight);
            p.Padding = new Thickness(0, 0, 0, Math.Max(0, px - used));
        }
    }
}
