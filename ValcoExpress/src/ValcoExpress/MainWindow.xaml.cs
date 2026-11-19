using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MimeKit;

namespace ValcoExpress
{
    public partial class MainWindow : VistaWindow
    {
        public ObservableCollection<FolderNode> Roots { get; } = new ObservableCollection<FolderNode>();
        ObservableCollection<MailItem> _messages = new ObservableCollection<MailItem>();
        ICollectionView _view;
        FolderNode _current;
        MailItem _previewItem;
        int _loadVersion, _previewVersion;
        bool _userNavigated, _busySR, _closingDone, _exiting, _everShown;
        TrayService _tray;
        string _pendingSelectKey;
        string _sortProp;
        bool _sortDesc;
        readonly DispatcherTimer _markTimer = new DispatcherTimer();
        readonly DispatcherTimer _checkTimer = new DispatcherTimer();
        readonly DispatcherTimer _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        Point _dragStart;
        bool _dragArmed;

        public MainWindow()
        {
            InitializeComponent();
            BuildRibbon();
            var st = Storage.Settings;
            if (!double.IsNaN(st.WinLeft) && !double.IsNaN(st.WinTop) &&
                st.WinLeft > SystemParameters.VirtualScreenLeft - 50 && st.WinTop > SystemParameters.VirtualScreenTop - 50 &&
                st.WinLeft < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = st.WinLeft; Top = st.WinTop;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = Math.Max(MinWidth, st.WinWidth);
            Height = Math.Max(MinHeight, st.WinHeight);
            if (st.Maximized) WindowState = WindowState.Maximized;
            FolderCol.Width = new GridLength(Math.Max(120, st.FolderPaneWidth));
            PreviewRowDef.Height = new GridLength(Math.Max(120, st.PreviewHeight));
            _sortProp = st.SortColumn ?? "Date";
            _sortDesc = st.SortDescending;

            UI.ApplyTreeSelection(FolderTree);
            Roots.Add(Mail.Local.Root);
            foreach (var s in Mail.Accounts) Roots.Add(s.Root);
            FolderTree.ItemsSource = Roots;
            BindList(new List<MailItem>());
            ApplyLayout();
            UpdateSortMenus();
            UpdateSchemeMenu();
            UpdateCommands();

            _markTimer.Tick += async (s, e) =>
            {
                _markTimer.Stop();
                if (_previewItem != null && !_previewItem.IsRead) await MarkReadAsync(new List<MailItem> { _previewItem }, true);
            };
            _checkTimer.Tick += async (s, e) => await SendReceiveAsync(false);
            _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); _view?.Refresh(); UpdateStatus(); };
            Viewer.MailtoClicked += uri => ComposeWindow.FromMailto(uri).Show();
            Sender.Changed += OnStoresChanged;
            PreviewKeyDown += OnKeys;
            Closing += OnClosing;
            IsVisibleChanged += (s, e) => { if (IsVisible) _everShown = true; };
            _tray = new TrayService(this);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => OnLoaded(this, null)));
        }

        // ================================================================== startup
        async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try { await Mail.LoadTreeAsync(Mail.Local); }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "Local Folders could not be opened."); }
            var inbox = Mail.Local.Find(FolderKind.Inbox);
            if (inbox != null) inbox.IsSelected = true;

            var loads = Mail.Accounts.ToList().Select(s => LoadAccountAsync(s, true)).ToList();
            ResetCheckTimer();
            await Task.WhenAll(loads);
            if (Storage.Settings.SendReceiveAtStartup) await SendReceiveAsync(false);
            else UpdateTrayUnread();
            if (Storage.Accounts.Count == 0) SetStatus(null, "No accounts set up");
        }

        async Task LoadAccountAsync(ImapStore s, bool initial)
        {
            try
            {
                await Mail.LoadTreeAsync(s);
                if (initial && !_userNavigated && s == Mail.Accounts.FirstOrDefault())
                {
                    var inbox = s.Find(FolderKind.Inbox);
                    if (inbox != null) { inbox.IsSelected = true; _userNavigated = false; }
                }
                UpdateOnlineStatus();
            }
            catch (Exception ex)
            {
                UpdateOnlineStatus(ex.Message);
            }
        }

        void UpdateOnlineStatus(string err = null)
        {
            bool offline = Mail.Accounts.Any(a => a.Root.Status == "offline");
            if (Storage.Accounts.Count == 0) { StatusRight.Text = "No accounts set up"; StatusIcon.Source = UI.Img("IconLocalFolders"); return; }
            StatusRight.Text = offline ? "Some accounts are offline" : "Working Online";
            StatusIcon.Source = UI.Img(offline ? "IconWarning" : "IconAccount");
            StatusRight.ToolTip = err;
        }

        void ResetCheckTimer()
        {
            _checkTimer.Stop();
            if (Storage.Settings.CheckMinutes > 0)
            {
                _checkTimer.Interval = TimeSpan.FromMinutes(Storage.Settings.CheckMinutes);
                _checkTimer.Start();
            }
        }

        // ================================================================== folders & list
        void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!(e.NewValue is FolderNode node)) return;
            if (FolderTree.IsKeyboardFocusWithin || FolderTree.IsMouseOver) _userNavigated = true;
            if (!node.Selectable)
            {
                _current = node;
                Title = $"{node.Name} - Valco Express"; FolderTitle.Text = node.Name;
                BindList(new List<MailItem>());
                Viewer.Clear(node.Store is LocalStore
                    ? "Local Folders store messages on this computer. Choose a folder on the left."
                    : (node.Status == "offline" ? "This account couldn't connect. Check its settings under Tools > Accounts, then press F5." : "Choose a folder on the left to see its messages."));
                UpdateStatus();
                return;
            }
            _ = LoadFolderAsync(node, false);
        }

        async Task LoadFolderAsync(FolderNode node, bool keepSelection)
        {
            int ver = ++_loadVersion;
            var keep = keepSelection ? new HashSet<string>(MessageList.SelectedItems.Cast<MailItem>().Select(m => m.Key)) : null;
            var keepPreview = keepSelection ? _previewItem?.Key : null;
            if (!keepSelection || node != _current)
            {
                _current = node;
                Title = $"{node.Name} - Valco Express"; FolderTitle.Text = node.Name;
                FromHeader.Text = node.ShowsRecipients ? "To" : "From";
                DateHeader.Text = node.ShowsRecipients ? "Sent" : "Received";
                SetStatus("Loading messages...");
            }
            List<MailItem> list;
            try { list = await node.Store.ListAsync(node); }
            catch (Exception ex)
            {
                if (ver != _loadVersion) return;
                BindList(new List<MailItem>());
                Viewer.Clear("Valco Express couldn't open this folder.\n\n" + ex.Message);
                SetStatus("Error");
                if (node.Store is ImapStore) node.Store.Root.Status = "offline";
                UpdateOnlineStatus(ex.Message);
                return;
            }
            if (ver != _loadVersion) return;
            node.Total = list.Count;
            node.Unread = list.Count(m => !m.IsRead);
            BindList(list);
            if (keep != null && keep.Count > 0)
            {
                foreach (var m in _messages.Where(m => keep.Contains(m.Key))) MessageList.SelectedItems.Add(m);
                if (keepPreview != null) _previewItem = _messages.FirstOrDefault(m => m.Key == keepPreview);
            }
            else if (_pendingSelectKey != null && _messages.FirstOrDefault(m => m.Key == _pendingSelectKey) is MailItem wanted)
            {
                MessageList.SelectedItem = wanted;
                MessageList.ScrollIntoView(wanted);
            }
            else
            {
                _previewItem = null;
                Viewer.Clear(_messages.Count == 0 ? "There are no messages in this folder." : "There is no message selected.");
            }
            _pendingSelectKey = null;
            UpdateStatus();
            UpdateCommands();
        }

        void BindList(List<MailItem> list)
        {
            _messages = new ObservableCollection<MailItem>(list);
            MessageList.ItemsSource = _messages;
            _view = CollectionViewSource.GetDefaultView(_messages);
            _view.Filter = Filter;
            ApplySort();
            ListEmpty.Visibility = list.Count == 0 && _current != null && _current.Selectable ? Visibility.Visible : Visibility.Collapsed;
        }

        bool Filter(object o)
        {
            var m = (MailItem)o;
            if (Storage.Settings.HideRead && m.IsRead && m != _previewItem) return false;
            var q = SearchBox.Text.Trim();
            if (q.Length == 0) return true;
            return (m.Subject ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                   (m.Who ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                   (m.FromAddress ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (m.To ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        void ApplySort()
        {
            if (_view == null) return;
            using (_view.DeferRefresh())
            {
                _view.SortDescriptions.Clear();
                _view.SortDescriptions.Add(new SortDescription(_sortProp, _sortDesc ? ListSortDirection.Descending : ListSortDirection.Ascending));
                if (_sortProp != "Date") _view.SortDescriptions.Add(new SortDescription("Date", ListSortDirection.Descending));
            }
        }

        void Header_Click(object sender, RoutedEventArgs e)
        {
            if (!(e.OriginalSource is GridViewColumnHeader h) || h.Column == null) return;
            var tag = (h.Column.Header as FrameworkElement)?.Tag as string;
            if (tag == null) return;
            if (_sortProp == tag) _sortDesc = !_sortDesc;
            else { _sortProp = tag; _sortDesc = tag == "Date" || tag == "Size" || tag == "IsFlagged" || tag == "HasAttachments"; }
            ApplySort();
            UpdateSortMenus();
        }

        void UpdateSortMenus()
        {
            MiSortFrom.IsChecked = _sortProp == "Who";
            MiSortSubject.IsChecked = _sortProp == "Subject";
            MiSortDate.IsChecked = _sortProp == "Date";
            MiSortSize.IsChecked = _sortProp == "Size";
            MiSortFlag.IsChecked = _sortProp == "IsFlagged";
            MiSortAsc.IsChecked = !_sortDesc;
            MiSortDesc.IsChecked = _sortDesc;
            SyncRibbon();
            foreach (var col in ((GridView)MessageList.View).Columns)
            {
                if (col.Header is TextBlock tb && tb.Tag is string t)
                {
                    var baseText = tb.Text.TrimEnd(' ', '▲', '▼');
                    tb.Text = t == _sortProp ? baseText + (_sortDesc ? "  ▼" : "  ▲") : baseText;
                }
            }
        }

        void SortMenu_Click(object sender, RoutedEventArgs e)
        {
            _sortProp = (string)((MenuItem)sender).Tag;
            ApplySort(); UpdateSortMenus();
        }
        void SortAsc_Click(object sender, RoutedEventArgs e) { _sortDesc = false; ApplySort(); UpdateSortMenus(); }
        void SortDesc_Click(object sender, RoutedEventArgs e) { _sortDesc = true; ApplySort(); UpdateSortMenus(); }

        void Search_TextChanged(object sender, TextChangedEventArgs e) { _searchTimer.Stop(); _searchTimer.Start(); }
        void Search_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { SearchBox.Text = ""; MessageList.Focus(); }
            else if (e.Key == Key.Enter || e.Key == Key.Down) { MessageList.Focus(); if (MessageList.Items.Count > 0) MessageList.SelectedIndex = 0; }
        }

        void UpdateStatus()
        {
            int total = _messages.Count;
            int unread = _messages.Count(m => !m.IsRead);
            int shown = _view?.Cast<object>().Count() ?? total;
            var text = $"{total} message(s), {unread} unread";
            if (shown != total) text += $"  ({shown} shown)";
            SetStatus(text);
            ListEmpty.Visibility = shown == 0 && _current != null && _current.Selectable ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetStatus(string left, string right = null)
        {
            if (left != null) StatusLeft.Text = left;
            if (right != null) StatusRight.Text = right;
        }

        List<MailItem> Sel => MessageList.SelectedItems.Cast<MailItem>().ToList();

        public MailItem Neighbor(MailItem item, int dir)
        {
            var items = _view?.Cast<MailItem>().ToList() ?? new List<MailItem>();
            var i = items.IndexOf(item);
            if (i < 0) return null;
            i += dir;
            return i >= 0 && i < items.Count ? items[i] : null;
        }

        public void SelectItem(MailItem item)
        {
            if (!_messages.Contains(item)) return;
            MessageList.SelectedItem = item;
            MessageList.ScrollIntoView(item);
        }

        // ================================================================== preview
        void MessageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCommands();
            _markTimer.Stop();
            var sel = Sel;
            if (sel.Count == 1) _ = PreviewAsync(sel[0]);
            else
            {
                _previewVersion++;
                if (sel.Count == 0) Viewer.Clear(_messages.Count == 0 ? "There are no messages in this folder." : "There is no message selected.");
                else Viewer.Clear($"{sel.Count} messages are selected.");
            }
        }

        async Task PreviewAsync(MailItem m)
        {
            _previewItem = m;
            if (!Storage.Settings.ShowPreview) return;
            int v = ++_previewVersion;
            MimeMessage msg;
            try { msg = await m.Store.GetMessageAsync(m); }
            catch (Exception ex)
            {
                if (v == _previewVersion) Viewer.Clear("This message could not be displayed.\n\n" + ex.Message);
                return;
            }
            if (v != _previewVersion) return;
            await Viewer.ShowAsync(msg);
            if (!m.IsRead && Storage.Settings.MarkReadEnabled)
            {
                if (Storage.Settings.MarkReadSeconds <= 0) await MarkReadAsync(new List<MailItem> { m }, true);
                else { _markTimer.Interval = TimeSpan.FromSeconds(Storage.Settings.MarkReadSeconds); _markTimer.Start(); }
            }
        }

        void Message_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (UI.FindAncestor<Border>(e.OriginalSource as DependencyObject)?.ToolTip as string == "Click to flag or unflag") return;
            OpenSelected();
        }

        void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

        async void OpenSelected()
        {
            foreach (var m in Sel.Take(10))
            {
                try
                {
                    var msg = await m.Store.GetMessageAsync(m);
                    if (m.Folder != null && (m.Folder.Kind == FolderKind.Drafts || m.Folder.Kind == FolderKind.Outbox))
                    {
                        ComposeWindow.FromDraft(msg, m).Show();
                        continue;
                    }
                    var w = new ReadWindow(msg, m, this);
                    w.Show();
                }
                catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The message could not be opened."); }
            }
        }

        // ================================================================== mark/flag
        public async Task MarkReadAsync(List<MailItem> items, bool read)
        {
            items = items.Where(i => i.IsRead != read).ToList();
            if (items.Count == 0) return;
            foreach (var i in items) i.IsRead = read;
            try
            {
                foreach (var g in items.GroupBy(i => i.Store)) await g.Key.SetReadAsync(g.ToList(), read);
            }
            catch (Exception ex)
            {
                foreach (var i in items) i.IsRead = !read;
                SetStatus("Couldn't update the message: " + ex.Message);
            }
            foreach (var f in items.Select(i => i.Folder).Distinct())
                if (f != null) f.Unread = Math.Max(0, f.Unread + (read ? -items.Count(i => i.Folder == f) : items.Count(i => i.Folder == f)));
            if (Storage.Settings.HideRead) _view?.Refresh();
            UpdateStatus();
        }

        async Task FlagAsync(List<MailItem> items, bool flag)
        {
            foreach (var i in items) i.IsFlagged = flag;
            try { foreach (var g in items.GroupBy(i => i.Store)) await g.Key.SetFlaggedAsync(g.ToList(), flag); }
            catch (Exception ex) { foreach (var i in items) i.IsFlagged = !flag; SetStatus("Couldn't flag the message: " + ex.Message); }
            UpdateCommands();
        }

        async void FlagCell_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is MailItem m)
            {
                e.Handled = true;
                await FlagAsync(new List<MailItem> { m }, !m.IsFlagged);
            }
        }

        async void Flag_Click(object sender, RoutedEventArgs e)
        {
            var s = Sel;
            if (s.Count == 0) return;
            await FlagAsync(s, !s.All(m => m.IsFlagged));
        }

        async void MarkRead_Click(object sender, RoutedEventArgs e) => await MarkReadAsync(Sel, true);
        async void MarkUnread_Click(object sender, RoutedEventArgs e) { _markTimer.Stop(); await MarkReadAsync(Sel, false); }
        async void MarkAllRead_Click(object sender, RoutedEventArgs e) => await MarkReadAsync(_messages.ToList(), true);

        // ================================================================== delete/move
        void Delete_Click(object sender, RoutedEventArgs e) => _ = DeleteItemsAsync(Sel, this);

        public async Task<bool> DeleteItemsAsync(List<MailItem> items, Window owner)
        {
            if (items.Count == 0) return false;
            var store = items[0].Store;
            var trash = store.Find(FolderKind.Deleted);
            bool permanent = trash == null || items.All(i => i.Folder == trash);
            if (permanent && !VistaDialog.Confirm(owner,
                    items.Count == 1 ? "Are you sure you want to permanently delete this message?" : $"Are you sure you want to permanently delete these {items.Count} messages?",
                    DlgIcon.Warning))
                return false;
            int idx = MessageList.SelectedIndex;
            try
            {
                SetStatus("Deleting...");
                await store.DeleteAsync(items);
            }
            catch (Exception ex)
            {
                VistaDialog.Error(owner, ex.Message, "The message could not be deleted.");
                UpdateStatus();
                return false;
            }
            RemoveFromList(items, idx);
            if (trash != null) _ = RefreshCountsQuiet(store);
            return true;
        }

        void RemoveFromList(List<MailItem> items, int idx)
        {
            foreach (var i in items) _messages.Remove(i);
            if (_current != null) { _current.Total = _messages.Count; _current.Unread = _messages.Count(m => !m.IsRead); }
            if (_messages.Count > 0 && MessageList.SelectedItems.Count == 0)
            {
                var visible = _view.Cast<MailItem>().ToList();
                if (visible.Count > 0)
                {
                    var next = visible[Math.Min(Math.Max(0, idx), visible.Count - 1)];
                    MessageList.SelectedItem = next;
                    MessageList.ScrollIntoView(next);
                }
            }
            UpdateStatus();
        }

        async Task RefreshCountsQuiet(IMailStore store)
        {
            try { await store.RefreshCountsAsync(); } catch { }
        }

        IEnumerable<FolderNode> PickRoots() => Roots.Where(r => r.Children.Count > 0);

        async void MoveTo_Click(object sender, RoutedEventArgs e)
        {
            var s = Sel;
            if (s.Count == 0) return;
            var dest = FolderPickerDialog.Pick(this, "Move", "Move the selected message(s) to:", PickRoots());
            if (dest != null) await MoveItemsAsync(s, dest);
        }

        async void CopyTo_Click(object sender, RoutedEventArgs e)
        {
            var s = Sel;
            if (s.Count == 0) return;
            var dest = FolderPickerDialog.Pick(this, "Copy", "Copy the selected message(s) to:", PickRoots());
            if (dest == null) return;
            try
            {
                SetStatus("Copying...");
                await Mail.CopyAsync(s, dest);
                await RefreshCountsQuiet(dest.Store);
                UpdateStatus();
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The messages could not be copied."); }
        }

        async Task MoveItemsAsync(List<MailItem> items, FolderNode dest)
        {
            if (items.Count == 0 || dest == null || !dest.Selectable || items.All(i => i.Folder == dest)) return;
            int idx = MessageList.SelectedIndex;
            try
            {
                SetStatus("Moving...");
                var src = items[0].Store;
                await Mail.MoveAsync(items, dest);
                RemoveFromList(items, idx);
                await RefreshCountsQuiet(dest.Store);
                if (src != dest.Store) await RefreshCountsQuiet(src);
            }
            catch (Exception ex)
            {
                VistaDialog.Error(this, ex.Message, "The messages could not be moved.");
                UpdateStatus();
            }
        }

        async void MarkJunk_Click(object sender, RoutedEventArgs e)
        {
            var s = Sel;
            if (s.Count == 0) return;
            var junk = s[0].Store.Find(FolderKind.Junk) ?? Mail.Local.Find(FolderKind.Junk);
            await MoveItemsAsync(s, junk);
        }

        async void NotJunk_Click(object sender, RoutedEventArgs e)
        {
            var s = Sel;
            if (s.Count == 0) return;
            var inbox = s[0].Store.Find(FolderKind.Inbox) ?? Mail.Local.Find(FolderKind.Inbox);
            await MoveItemsAsync(s, inbox);
        }

        async Task EmptyAsync(FolderNode f)
        {
            if (f == null) return;
            if (!VistaDialog.Confirm(this, $"Are you sure you want to permanently delete all messages in '{f.Name}'?", DlgIcon.Warning)) return;
            try
            {
                await f.Store.EmptyFolderAsync(f);
                f.Unread = 0; f.Total = 0;
                if (_current == f) await LoadFolderAsync(f, false);
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The folder could not be emptied."); }
        }

        async void EmptyDeleted_Click(object sender, RoutedEventArgs e) =>
            await EmptyAsync((_current?.Store ?? Mail.Local).Find(FolderKind.Deleted) ?? Mail.Local.Find(FolderKind.Deleted));
        async void EmptyJunk_Click(object sender, RoutedEventArgs e) =>
            await EmptyAsync((_current?.Store ?? Mail.Local).Find(FolderKind.Junk) ?? Mail.Local.Find(FolderKind.Junk));

        // ================================================================== drag & drop
        void MessageList_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            _dragArmed = UI.FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject) != null;
        }

        void MessageList_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(null);
            if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragArmed = false;
            var items = Sel;
            if (items.Count == 0) return;
            var data = new DataObject("ValcoExpress.Items", items);
            DragDrop.DoDragDrop(MessageList, data, DragDropEffects.Move | DragDropEffects.Copy);
        }

        FolderNode NodeAt(DragEventArgs e) =>
            UI.FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext as FolderNode;

        void FolderTree_DragOver(object sender, DragEventArgs e)
        {
            var node = NodeAt(e);
            bool ok = e.Data.GetDataPresent("ValcoExpress.Items") && node != null && node.Selectable && node != _current;
            e.Effects = ok ? ((e.KeyStates & DragDropKeyStates.ControlKey) != 0 ? DragDropEffects.Copy : DragDropEffects.Move) : DragDropEffects.None;
            e.Handled = true;
        }

        async void FolderTree_Drop(object sender, DragEventArgs e)
        {
            var node = NodeAt(e);
            if (node == null || !node.Selectable || !(e.Data.GetData("ValcoExpress.Items") is List<MailItem> items)) return;
            if ((e.KeyStates & DragDropKeyStates.ControlKey) != 0)
            {
                try { await Mail.CopyAsync(items, node); await RefreshCountsQuiet(node.Store); }
                catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The messages could not be copied."); }
            }
            else await MoveItemsAsync(items, node);
        }

        // ================================================================== folder context menu
        void FolderTree_RightDown(object sender, MouseButtonEventArgs e)
        {
            var tvi = UI.FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (tvi?.DataContext is FolderNode node)
            {
                tvi.IsSelected = true;
                var cm = new ContextMenu();
                if (node.Selectable) cm.Items.Add(Cmd.Item("_Open", (s, a) => _ = LoadFolderAsync(node, false)));
                if (node.Store is ImapStore st && node.Kind == FolderKind.Root)
                {
                    cm.Items.Add(Cmd.Item("_Send and Receive", async (s, a) => await SendReceiveAsync(true), null, "IconSendReceive"));
                    cm.Items.Add(Cmd.Item("_Reconnect", async (s, a) => { await st.DisconnectAsync(); await LoadAccountAsync(st, false); }));
                    cm.Items.Add(Cmd.Item("_Properties", (s, a) => { if (AccountDialog.Edit(this, st.Account, false)) { Storage.SaveAccounts(); AccountChanged(st.Account); } }, null, "IconProperties"));
                    cm.Items.Add(new Separator());
                }
                cm.Items.Add(Cmd.Item("_New Folder...", async (s, a) => await NewFolderAsync(node), "Ctrl+Shift+E", "IconFolder"));
                if (node.Kind == FolderKind.Normal)
                {
                    cm.Items.Add(Cmd.Item("_Delete Folder", async (s, a) => await DeleteFolderAsync(node), null, "IconDelete"));
                }
                if (node.Selectable)
                {
                    cm.Items.Add(new Separator());
                    cm.Items.Add(Cmd.Item("Mark All _Read", async (s, a) =>
                    {
                        if (_current != node) await LoadFolderAsync(node, false);
                        await MarkReadAsync(_messages.ToList(), true);
                    }));
                }
                if (node.Kind == FolderKind.Deleted || node.Kind == FolderKind.Junk)
                    cm.Items.Add(Cmd.Item($"_Empty '{node.Name}' Folder", async (s, a) => await EmptyAsync(node)));
                cm.PlacementTarget = tvi;
                cm.IsOpen = true;
                e.Handled = true;
            }
        }

        async void NewFolder_Click(object sender, RoutedEventArgs e) =>
            await NewFolderAsync(FolderTree.SelectedItem as FolderNode ?? Mail.Local.Root);

        async Task NewFolderAsync(FolderNode parent)
        {
            var name = InputDialog.Ask(this, "Create Folder", $"Folder name (it will be created inside '{parent.Name}'):");
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                await parent.Store.CreateFolderAsync(parent, name);
                await Mail.LoadTreeAsync(parent.Store);
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The folder could not be created."); }
        }

        async Task DeleteFolderAsync(FolderNode node)
        {
            if (!VistaDialog.Confirm(this, $"Are you sure you want to delete the folder '{node.Name}' and all of the messages in it?", DlgIcon.Warning)) return;
            try
            {
                await node.Store.DeleteFolderAsync(node);
                if (_current == node) { _current = null; BindList(new List<MailItem>()); Viewer.Clear(); }
                await Mail.LoadTreeAsync(node.Store);
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The folder could not be deleted."); }
        }

        // ================================================================== compose
        void NewMail_Click(object sender, RoutedEventArgs e) => ComposeWindow.New().Show();

        async Task<MimeMessage> SelectedMessageAsync()
        {
            var m = Sel.FirstOrDefault();
            if (m == null) return null;
            if (_previewItem == m && Viewer.Message != null) return Viewer.Message;
            try { return await m.Store.GetMessageAsync(m); }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The message could not be opened."); return null; }
        }

        async void Reply_Click(object sender, RoutedEventArgs e) { var m = await SelectedMessageAsync(); if (m != null) ComposeWindow.Reply(m, false).Show(); }
        async void ReplyAll_Click(object sender, RoutedEventArgs e) { var m = await SelectedMessageAsync(); if (m != null) ComposeWindow.Reply(m, true).Show(); }
        async void Forward_Click(object sender, RoutedEventArgs e) { var m = await SelectedMessageAsync(); if (m != null) ComposeWindow.Forward(m, false).Show(); }
        async void ForwardAtt_Click(object sender, RoutedEventArgs e) { var m = await SelectedMessageAsync(); if (m != null) ComposeWindow.Forward(m, true).Show(); }

        async void Print_Click(object sender, RoutedEventArgs e)
        {
            var m = Sel.FirstOrDefault();
            if (m == null) return;
            if (Storage.Settings.ShowPreview && _previewItem == m && Viewer.Message != null) { Viewer.Print(); return; }
            var msg = await SelectedMessageAsync();
            if (msg != null) new ReadWindow(msg, m, this).Show();
        }

        // ================================================================== send / receive
        async void SendReceive_Click(object sender, RoutedEventArgs e) => await SendReceiveAsync(true);

        public async Task SendReceiveAsync(bool interactive, bool receive = true)
        {
            if (_busySR) return;
            _busySR = true;
            ProgressWindow dlg = null;
            if (interactive) { dlg = new ProgressWindow(this, "Valco Express", "IconSendReceive"); dlg.Show(); }
            bool newMail = false;
            var arrived = new List<MailItem>();
            try
            {
                await Sender.EnsureLocalAsync();
                var outbox = Mail.Local.Find(FolderKind.Outbox);
                var pending = outbox != null ? await Mail.Local.ListAsync(outbox) : new List<MailItem>();
                int n = 0;
                foreach (var it in pending)
                {
                    if (dlg?.Cancelled == true) break;
                    n++;
                    dlg?.SetText($"Sending message {n} of {pending.Count}...");
                    SetStatus($"Sending message {n} of {pending.Count}...");
                    try
                    {
                        var msg = await Mail.Local.GetMessageAsync(it);
                        var id = msg.Headers["X-Valco-Account"];
                        var acct = Storage.Accounts.FirstOrDefault(a => a.Id == id) ?? Mail.DefaultAccount;
                        if (acct == null) { dlg?.AddError($"'{it.Subject}': no e-mail account is set up to send it."); continue; }
                        await Mail.SendAsync(msg, acct);
                        await Mail.Local.DeletePermanentAsync(new[] { it });
                    }
                    catch (Exception ex) { dlg?.AddError($"Sending '{it.Subject}': {ex.Message}"); }
                }

                foreach (var s in Mail.Accounts.ToList())
                {
                    if (dlg?.Cancelled == true || !receive) break;
                    if (!s.Account.IncludeInSendReceive) continue;
                    dlg?.SetText($"Checking for new messages on '{s.DisplayName}'...");
                    SetStatus($"Checking for new messages on '{s.DisplayName}'...");
                    try
                    {
                        var inbox = s.Find(FolderKind.Inbox);
                        int before = inbox?.Unread ?? 0;
                        if (s.Root.Children.Count == 0) await Mail.LoadTreeAsync(s);
                        else { await s.RefreshCountsAsync(); s.Root.Status = null; }
                        inbox = s.Find(FolderKind.Inbox);
                        if (inbox != null && inbox.Unread > before) newMail = true;
                        arrived.AddRange(await s.CheckNewAsync());
                    }
                    catch (Exception ex)
                    {
                        s.Root.Status = "offline";
                        dlg?.AddError($"'{s.DisplayName}': {ex.Message}");
                    }
                }
                await RefreshCountsQuiet(Mail.Local);
                if (_current != null && _current.Selectable) await LoadFolderAsync(_current, true);
                if ((newMail || arrived.Count > 0) && Storage.Settings.PlaySound) PlayNewMail();
                if (arrived.Count > 0) NotifyNewMail(arrived);
            }
            finally
            {
                _busySR = false;
                UpdateOnlineStatus();
                UpdateStatus();
                UpdateTrayUnread();
                dlg?.Finish();
            }
        }

        void NotifyNewMail(List<MailItem> arrived)
        {
            if (!Storage.Settings.ShowNotifications) return;
            if (IsVisible && IsActive && WindowState != WindowState.Minimized) return;
            var first = arrived[0];
            string Subj(MailItem m) => string.IsNullOrWhiteSpace(m.Subject) ? "(No Subject)" : m.Subject;
            string title, text;
            if (arrived.Count == 1)
            {
                title = first.FromDisplay ?? "New message";
                text = Subj(first);
            }
            else
            {
                title = $"{arrived.Count} new messages";
                text = string.Join("\n", arrived.Take(3).Select(m => $"{m.FromDisplay}: {Subj(m)}"));
                if (arrived.Count > 3) text += $"\n…and {arrived.Count - 3} more";
            }
            _tray.Notify(title, text, () => OpenArrived(first));
        }

        void OpenArrived(MailItem item)
        {
            ShowFromTray();
            var inbox = item.Store.Find(FolderKind.Inbox);
            if (inbox == null) return;
            _userNavigated = true;
            _pendingSelectKey = item.Key;
            if (_current == inbox) _ = LoadFolderAsync(inbox, false);
            else inbox.IsSelected = true;
        }

        void UpdateTrayUnread() =>
            _tray?.SetUnread(Mail.All.Sum(s => s.Find(FolderKind.Inbox)?.Unread ?? 0));

        public void ShowFromTray()
        {
            if (_closingDone) return;
            Show();
            if (WindowState == WindowState.Minimized) WindowState = Storage.Settings.Maximized ? WindowState.Maximized : WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        static void PlayNewMail()
        {
            try
            {
                var wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify Email.wav");
                if (!File.Exists(wav)) wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify.wav");
                if (File.Exists(wav)) new SoundPlayer(wav).Play();
                else SystemSounds.Asterisk.Play();
            }
            catch { }
        }

        async void OnStoresChanged()
        {
            await RefreshCountsQuiet(Mail.Local);
            if (_current != null && _current.Selectable &&
                (_current.Kind == FolderKind.Outbox || _current.Kind == FolderKind.Sent || _current.Kind == FolderKind.Drafts))
                await LoadFolderAsync(_current, true);
        }

        // ================================================================== accounts / settings
        public void AccountAdded(Account a)
        {
            var s = new ImapStore(a);
            Mail.Accounts.Add(s);
            Roots.Add(s.Root);
            _ = LoadAccountAsync(s, false);
            UpdateOnlineStatus();
        }

        public async void AccountRemoved(Account a)
        {
            var s = Mail.StoreFor(a);
            if (s == null) return;
            Mail.Accounts.Remove(s);
            Roots.Remove(s.Root);
            if (_current?.Store == s)
            {
                _current = null;
                BindList(new List<MailItem>());
                Viewer.Clear();
                var inbox = Mail.Local.Find(FolderKind.Inbox);
                if (inbox != null) inbox.IsSelected = true;
            }
            await s.DisconnectAsync();
            UpdateOnlineStatus();
        }

        public async void AccountChanged(Account a)
        {
            var s = Mail.StoreFor(a);
            if (s == null) return;
            s.Root.Name = a.Title;
            bool wasCurrent = _current?.Store == s;
            await s.DisconnectAsync();
            await LoadAccountAsync(s, false);
            if (wasCurrent)
            {
                var inbox = s.Find(FolderKind.Inbox);
                if (inbox != null) inbox.IsSelected = true;
            }
        }

        public void SettingsChanged()
        {
            UpdateSchemeMenu();
            ResetCheckTimer();
            _view?.Refresh();
            UpdateStatus();
        }

        void Accounts_Click(object sender, RoutedEventArgs e) => new AccountsWindow(this).ShowDialog();
        void Options_Click(object sender, RoutedEventArgs e) => new OptionsWindow(this).ShowDialog();
        void About_Click(object sender, RoutedEventArgs e) => new AboutWindow(this).ShowDialog();
        void Contacts_Click(object sender, RoutedEventArgs e) => new ContactsWindow(this, false).Show();

        void NewContact_Click(object sender, RoutedEventArgs e)
        {
            var c = new Contact();
            if (ContactDialog.Edit(this, c, true)) { Storage.Contacts.Add(c); Storage.SaveContacts(); }
        }

        void AddSender_Click(object sender, RoutedEventArgs e)
        {
            var m = Sel.FirstOrDefault();
            if (m == null) return;
            var c = new Contact { Name = m.FromName, Email = m.FromAddress };
            if (ContactDialog.Edit(this, c, true)) { Storage.Contacts.Add(c); Storage.SaveContacts(); }
        }

        void Shortcuts_Click(object sender, RoutedEventArgs e) =>
            VistaDialog.Show(this,
                "Ctrl+N\tNew message\nCtrl+R\tReply to sender\nCtrl+Shift+R\tReply to all\nCtrl+F\tForward\nCtrl+M\tSend and receive\n" +
                "Ctrl+Q / Ctrl+U\tMark read / unread\nCtrl+Shift+A\tMark all read\nCtrl+Shift+V\tMove to folder\nCtrl+Shift+E\tNew folder\n" +
                "Ctrl+E\tSearch\nCtrl+P\tPrint\nDel\tDelete\nEnter\tOpen message\nF5\tRefresh\nCtrl+Shift+C\tContacts\nAlt+S (compose)\tSend",
                "Keyboard Shortcuts", MessageBoxButton.OK, DlgIcon.Info, "Keyboard shortcuts");

        // ================================================================== files
        void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Open Message", Filter = "Mail Messages (*.eml)|*.eml|All Files (*.*)|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            try { new ReadWindow(MimeMessage.Load(dlg.FileName), null, null).Show(); }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The message could not be opened."); }
        }

        async void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            var m = Sel.FirstOrDefault();
            if (m == null) return;
            var msg = await SelectedMessageAsync();
            if (msg == null) return;
            var dlg = new SaveFileDialog
            {
                Title = "Save Message As", Filter = "Mail (*.eml)|*.eml|Text Files (*.txt)|*.txt",
                FileName = MailUtil.Safe(string.IsNullOrWhiteSpace(m.Subject) ? "message" : m.Subject)
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                if (dlg.FilterIndex == 2) File.WriteAllText(dlg.FileName, MailUtil.PlainText(msg));
                else using (var fs = File.Create(dlg.FileName)) msg.WriteTo(fs);
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message); }
        }

        async void Export_Click(object sender, RoutedEventArgs e)
        {
            var s = Sel;
            if (s.Count == 0) { VistaDialog.Show(this, "Select the messages you want to export first."); return; }
            var dlg = new OpenFolderDialog { Title = "Export Messages To" };
            if (dlg.ShowDialog(this) != true) return;
            int done = 0;
            foreach (var m in s)
            {
                try
                {
                    var msg = await m.Store.GetMessageAsync(m);
                    var baseName = MailUtil.Safe(string.IsNullOrWhiteSpace(m.Subject) ? "message" : m.Subject);
                    var path = Path.Combine(dlg.FolderName, baseName + ".eml");
                    int i = 1;
                    while (File.Exists(path)) path = Path.Combine(dlg.FolderName, $"{baseName} ({i++}).eml");
                    using (var fs = File.Create(path)) msg.WriteTo(fs);
                    done++;
                }
                catch { }
            }
            SetStatus($"Exported {done} message(s).");
        }

        async void Import_Click(object sender, RoutedEventArgs e)
        {
            var target = _current != null && _current.Selectable ? _current : Mail.Local.Find(FolderKind.Inbox);
            var dlg = new OpenFolderDialog { Title = $"Import .eml messages into '{target.Name}'" };
            if (dlg.ShowDialog(this) != true) return;
            var files = Directory.GetFiles(dlg.FolderName, "*.eml");
            if (files.Length == 0) { VistaDialog.Show(this, "That folder doesn't contain any .eml messages."); return; }
            int done = 0;
            foreach (var f in files)
            {
                try { await target.Store.AppendAsync(target, MimeMessage.Load(f), true); done++; }
                catch { }
                SetStatus($"Importing {done} of {files.Length}...");
            }
            await LoadFolderAsync(target, false);
            VistaDialog.Show(this, $"{done} of {files.Length} message(s) were imported into '{target.Name}'.", "Import");
        }

        // ================================================================== layout
        void ApplyLayout()
        {
            var st = Storage.Settings;
            MiFolders.IsChecked = st.ShowFolders;
            MiPreview.IsChecked = st.ShowPreview;
            MiToolbar.IsChecked = st.ShowToolbar;
            MiStatus.IsChecked = st.ShowStatusBar;
            MiHideRead.IsChecked = st.HideRead;
            MiShowAll.IsChecked = !st.HideRead;

            if (st.ShowFolders)
            {
                FolderPane.Visibility = Visibility.Visible;
                FolderSplitter.Visibility = Visibility.Visible;
                if (FolderCol.Width.Value < 40) FolderCol.Width = new GridLength(Math.Max(150, st.FolderPaneWidth));
            }
            else
            {
                if (FolderCol.ActualWidth > 40) st.FolderPaneWidth = FolderCol.ActualWidth;
                FolderPane.Visibility = Visibility.Collapsed;
                FolderSplitter.Visibility = Visibility.Collapsed;
                FolderCol.Width = new GridLength(0);
            }
            if (st.ShowPreview)
            {
                Viewer.Visibility = Visibility.Visible;
                PreviewSplitter.Visibility = Visibility.Visible;
                if (PreviewRowDef.Height.Value < 40) PreviewRowDef.Height = new GridLength(Math.Max(150, st.PreviewHeight));
            }
            else
            {
                if (PreviewRowDef.ActualHeight > 40) st.PreviewHeight = PreviewRowDef.ActualHeight;
                Viewer.Visibility = Visibility.Collapsed;
                PreviewSplitter.Visibility = Visibility.Collapsed;
                PreviewRowDef.Height = new GridLength(0);
            }
            Toolbar.Visibility = st.ShowToolbar ? Visibility.Visible : Visibility.Collapsed;
            StatusBar.Visibility = st.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
            SyncRibbon();
        }

        void Layout_Click(object sender, RoutedEventArgs e)
        {
            var st = Storage.Settings;
            st.ShowFolders = MiFolders.IsChecked;
            st.ShowPreview = MiPreview.IsChecked;
            st.ShowToolbar = MiToolbar.IsChecked;
            st.ShowStatusBar = MiStatus.IsChecked;
            ApplyLayout();
            if (st.ShowPreview && Sel.Count == 1) _ = PreviewAsync(Sel[0]);
        }

        void Scheme_Click(object sender, RoutedEventArgs e)
        {
            Storage.Settings.ColorScheme = (string)((MenuItem)sender).Tag;
            Storage.SaveSettings();
            OfficeTheme.Apply(Storage.Settings.ColorScheme);
            UpdateSchemeMenu();
        }

        void UpdateSchemeMenu()
        {
            MiSchemeBlue.IsChecked = OfficeTheme.Current == "Blue";
            MiSchemeSilver.IsChecked = OfficeTheme.Current == "Silver";
            MiSchemeBlack.IsChecked = OfficeTheme.Current == "Black";
            SyncRibbon();
        }

        void ToggleFolders_Click(object sender, RoutedEventArgs e)
        {
            Storage.Settings.ShowFolders = !Storage.Settings.ShowFolders;
            ApplyLayout();
        }

        void HideRead_Click(object sender, RoutedEventArgs e) { Storage.Settings.HideRead = true; ApplyLayout(); _view?.Refresh(); UpdateStatus(); }
        void ShowAll_Click(object sender, RoutedEventArgs e) { Storage.Settings.HideRead = false; ApplyLayout(); _view?.Refresh(); UpdateStatus(); }

        async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

        async Task RefreshAsync()
        {
            if (_current?.Store is ImapStore s && s.Root.Children.Count == 0) { await LoadAccountAsync(s, false); return; }
            if (_current != null && _current.Selectable) await LoadFolderAsync(_current, true);
            if (_current != null) await RefreshCountsQuiet(_current.Store);
        }

        void SelectAll_Click(object sender, RoutedEventArgs e) => MessageList.SelectAll();
        void Find_Click(object sender, RoutedEventArgs e) { Storage.Settings.ShowToolbar = true; ApplyLayout(); SearchBox.Focus(); SearchBox.SelectAll(); }

        // ================================================================== commands state
        void UpdateCommands()
        {
            int n = MessageList.SelectedItems.Count;
            bool one = n == 1, any = n > 0;
            BtnReply.IsEnabled = BtnReplyAll.IsEnabled = BtnForward.IsEnabled = BtnPrint.IsEnabled = one;
            BtnDelete.IsEnabled = any;
            MiReply.IsEnabled = MiReplyAll.IsEnabled = MiForward.IsEnabled = MiForwardAtt.IsEnabled = MiPrint.IsEnabled = MiSaveAs.IsEnabled = one;
            MiOpen.IsEnabled = MiDelete.IsEnabled = MiMove.IsEnabled = MiCopy.IsEnabled = MiRead.IsEnabled = MiUnread.IsEnabled =
                MiFlag.IsEnabled = MiJunkMenu.IsEnabled = MiExport.IsEnabled = MiAddSender.IsEnabled = any;
            var flagged = any && MessageList.SelectedItems.Cast<MailItem>().All(m => m.IsFlagged);
            MiFlag.Header = flagged ? "Unfla_g Message" : "Fla_g Message";
            CtxFlag.Header = MiFlag.Header;
            SyncRibbon();
        }

        void Menu_Opened(object sender, RoutedEventArgs e) => UpdateCommands();

        void MessageList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (MessageList.SelectedItems.Count == 0) { e.Handled = true; return; }
            UpdateCommands();
        }

        // ================================================================== keyboard
        void OnKeys(object sender, KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            bool inText = Keyboard.FocusedElement is TextBox;
            bool handled = true;
            if (mods == ModifierKeys.Control)
            {
                switch (key)
                {
                    case Key.N: ComposeWindow.New().Show(); break;
                    case Key.R: Reply_Click(this, null); break;
                    case Key.F: Forward_Click(this, null); break;
                    case Key.P: Print_Click(this, null); break;
                    case Key.M: _ = SendReceiveAsync(true); break;
                    case Key.Q: _ = MarkReadAsync(Sel, true); break;
                    case Key.U: _markTimer.Stop(); _ = MarkReadAsync(Sel, false); break;
                    case Key.E: Find_Click(this, null); break;
                    case Key.D: if (!inText) Delete_Click(this, null); else handled = false; break;
                    case Key.A: if (!inText) MessageList.SelectAll(); else handled = false; break;
                    default: handled = false; break;
                }
            }
            else if (mods == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                switch (key)
                {
                    case Key.R: ReplyAll_Click(this, null); break;
                    case Key.A: _ = MarkReadAsync(_messages.ToList(), true); break;
                    case Key.V: MoveTo_Click(this, null); break;
                    case Key.E: NewFolder_Click(this, null); break;
                    case Key.C: Contacts_Click(this, null); break;
                    default: handled = false; break;
                }
            }
            else if (mods == ModifierKeys.None && !inText)
            {
                switch (key)
                {
                    case Key.Delete: Delete_Click(this, null); break;
                    case Key.Enter: if (MessageList.IsKeyboardFocusWithin) OpenSelected(); else handled = false; break;
                    case Key.F5: _ = RefreshAsync(); break;
                    default: handled = false; break;
                }
            }
            else if (mods == ModifierKeys.None && key == Key.F5) _ = RefreshAsync();
            else handled = false;
            if (handled) e.Handled = true;
        }

        // ================================================================== exit
        void Exit_Click(object sender, RoutedEventArgs e) => ExitApp();

        public void MarkExiting() => _exiting = true;

        public void ExitApp()
        {
            _exiting = true;
            _ = ExitAppAsync();
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            e.Cancel = true;
            if (!_exiting && Storage.Settings.CloseToTray)
            {
                SaveLayout();
                Hide();
                if (!Storage.Settings.TrayHintShown)
                {
                    Storage.Settings.TrayHintShown = true;
                    Storage.SaveSettings();
                    _tray.Notify("Valco Express is still running",
                        "It will keep checking for new mail. Right-click the envelope in the notification area to exit.", ShowFromTray);
                }
                return;
            }
            ExitApp();
        }

        void SaveLayout()
        {
            if (!_everShown) return;
            var st = Storage.Settings;
            st.Maximized = WindowState == WindowState.Maximized;
            var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            st.WinLeft = b.Left; st.WinTop = b.Top; st.WinWidth = b.Width; st.WinHeight = b.Height;
            if (st.ShowFolders && FolderCol.ActualWidth > 40) st.FolderPaneWidth = FolderCol.ActualWidth;
            if (st.ShowPreview && PreviewRowDef.ActualHeight > 40) st.PreviewHeight = PreviewRowDef.ActualHeight;
            st.SortColumn = _sortProp;
            st.SortDescending = _sortDesc;
            Storage.SaveSettings();
        }

        async Task ExitAppAsync()
        {
            if (_closingDone) return;
            if (IsVisible) SaveLayout();
            _closingDone = true;
            _checkTimer.Stop();
            Hide();
            Viewer.Cleanup();
            _tray?.Dispose();
            var st = Storage.Settings;
            try
            {
                if (st.EmptyDeletedOnExit)
                {
                    var del = Mail.Local.Find(FolderKind.Deleted);
                    if (del != null) await Mail.Local.EmptyFolderAsync(del);
                }
                var tasks = Mail.Accounts.Select(a => a.DisconnectAsync()).ToList();
                await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(3000));
            }
            catch { }
            try { Directory.Delete(Path.Combine(Storage.LocalDir, "view"), true); } catch { }
            Application.Current.Shutdown();
        }
    }
}
