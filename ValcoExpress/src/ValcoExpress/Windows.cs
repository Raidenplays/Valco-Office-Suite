using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MimeKit;

namespace ValcoExpress
{
    public static class Cmd
    {
        public static Button Button(string icon, string label, RoutedEventHandler click, string tip = null, double size = 20)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(UI.Icon(icon, size));
            if (label != null) sp.Children.Add(new TextBlock { Text = label, Style = (Style)Application.Current.FindResource("CmdLabel") });
            var b = new Button { Content = sp, Style = (Style)Application.Current.FindResource("CmdButton"), ToolTip = tip ?? label };
            if (click != null) b.Click += click;
            return b;
        }

        public static Border Separator() => new Border { Style = (Style)Application.Current.FindResource("CmdSeparator") };

        public static Border Bar(params UIElement[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var i in items) sp.Children.Add(i);
            return new Border { Style = (Style)Application.Current.FindResource("CommandBar"), Child = sp };
        }

        public static MenuItem Item(string header, RoutedEventHandler click, string gesture = null, string icon = null)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
            if (icon != null) mi.Icon = UI.Icon(icon);
            if (click != null) mi.Click += click;
            return mi;
        }
    }

    // ===================================================================== sending
    public static class Sender
    {
        public static event Action Changed;
        public static void RaiseChanged() => Changed?.Invoke();

        public static async Task EnsureLocalAsync()
        {
            if (Mail.Local.Root.Children.Count == 0) await Mail.LoadTreeAsync(Mail.Local);
        }

        public static async Task SendAsync(Window owner, MimeMessage msg, Account acct)
        {
            await EnsureLocalAsync();
            var outbox = Mail.Local.Find(FolderKind.Outbox);
            if (acct == null)
            {
                await Mail.Local.AppendAsync(outbox, msg, true);
                VistaDialog.Show(owner, "Your message has been placed in the Outbox. To send it, set up an e-mail account under Tools > Accounts, then click Send/Receive.",
                    null, MessageBoxButton.OK, DlgIcon.Info, "No e-mail account is set up yet");
                RaiseChanged();
                return;
            }
            var prog = new ProgressWindow(owner, "Sending Message", "IconSend");
            prog.SetText($"Sending message to {string.Join(", ", msg.To.Mailboxes.Concat(msg.Cc.Mailboxes).Select(m => m.Address).Take(3))}...");
            prog.Show();
            try
            {
                await Mail.SendAsync(msg, acct);
                prog.Close();
            }
            catch (Exception ex)
            {
                prog.Close();
                msg.Headers.Remove("X-Valco-Account");
                msg.Headers.Add("X-Valco-Account", acct.Id);
                try { await Mail.Local.AppendAsync(outbox, msg, true); } catch { }
                VistaDialog.Error(owner, $"It has been placed in your Outbox and will be sent the next time you click Send/Receive.\n\nThe server said: {ex.Message}",
                    "Your message could not be sent");
            }
            finally { RaiseChanged(); }
        }
    }

    /// <summary>Vista progress dialog used for Send/Receive and sending.</summary>
    public class ProgressWindow : VistaWindow
    {
        readonly TextBlock _text;
        readonly ProgressBar _bar;
        readonly ListBox _errors;
        readonly Button _stop, _hide;
        public bool Cancelled { get; private set; }
        public bool HasErrors => _errors.Items.Count > 0;

        public ProgressWindow(Window owner, string title, string icon)
        {
            Title = title;
            if (owner != null && owner.IsVisible) Owner = owner;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Width = 440;
            SizeToContent = SizeToContent.Height;

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var img = UI.Icon(icon, 32);
            img.VerticalAlignment = VerticalAlignment.Top;
            img.Margin = new Thickness(0, 0, 12, 0);
            g.Children.Add(img);
            var sp = new StackPanel();
            Grid.SetColumn(sp, 1);
            _text = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Working..." };
            _bar = new ProgressBar { Height = 15, IsIndeterminate = true, Margin = new Thickness(0, 10, 0, 0) };
            _errors = new ListBox { Height = 90, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed, Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x10, 0x10)) };
            sp.Children.Add(_text); sp.Children.Add(_bar); sp.Children.Add(_errors);
            g.Children.Add(sp);
            _hide = UI.Btn("Hide", (s, e) => Hide());
            _stop = UI.Btn("Stop", (s, e) => { Cancelled = true; _stop.IsEnabled = false; _text.Text = "Stopping..."; });
            Content = UI.DialogLayout(g, UI.Strip(_hide, _stop));
        }

        public void SetText(string t) => _text.Text = t;

        public void AddError(string t)
        {
            _errors.Items.Add(t);
            _errors.Visibility = Visibility.Visible;
        }

        public void Finish()
        {
            if (!HasErrors) { Close(); return; }
            _bar.IsIndeterminate = false;
            _bar.Value = 100;
            _text.Text = "Finished, but some tasks had errors:";
            _stop.Content = "Close";
            _stop.IsEnabled = true;
            _stop.Click += (s, e) => Close();
            _hide.Visibility = Visibility.Collapsed;
            if (!IsVisible) Show();
            Activate();
        }
    }

    // ===================================================================== read window
    public class ReadWindow : VistaWindow
    {
        readonly MessageViewer _viewer;
        readonly MainWindow _main;
        MailItem _item;

        public ReadWindow(MimeMessage msg, MailItem item, MainWindow main)
        {
            _main = main;
            _item = item;
            Width = 820; Height = 640; MinWidth = 420; MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _viewer = new MessageViewer { FullHeader = true };
            _viewer.MailtoClicked += uri => ComposeWindow.FromMailto(uri).Show();

            // Ribbon like Outlook 2007's message window
            var ribbon = new RibbonBar();
            var tab = new RibbonTab("Message");
            tab.Add(RB.Group("Respond", null,
                RB.Large("IconReply", "Reply", () => Reply(false), "Reply to the sender (Ctrl+R)."),
                RB.Large("IconReplyAll", "Reply\nto All", () => Reply(true), "Reply to everyone (Ctrl+Shift+R)."),
                RB.Large("IconForward", "Forward", () => Fwd(false), "Forward the message (Ctrl+F)."),
                RB.Stack(RB.Small("IconAttach", "As Attachment", () => Fwd(true)))));
            tab.Add(RB.Group("Actions", null,
                RB.Large("IconDelete", "Delete", () => _ = DeleteAsync(), "Delete this message (Ctrl+D)."),
                RB.Stack(RB.Small("IconSaveAs", "Save As...", SaveAs), RB.Small("IconPrint", "Print", () => _viewer.Print(), "Print this message (Ctrl+P)."))));
            tab.Add(RB.Group("Contacts", null, RB.Large("IconAddContact", "Add\nSender", AddSender, "Add the sender to your contacts.")));
            tab.Add(RB.Group("Navigate", null, RB.Stack(
                RB.Small("IconPrevious", "Previous Item", () => _ = GoAsync(-1), "Previous message (Ctrl+<)."),
                RB.Small("IconNext", "Next Item", () => _ = GoAsync(1), "Next message (Ctrl+>)."))));
            ribbon.AddTab(tab);
            if (Storage.Settings.RibbonMinimized) ribbon.ToggleMinimize();
            ValcoTitleBar title = null;
            title = new ValcoTitleBar(this, () => new AppMenu()
                    .Item("IconReply", "Reply", () => Reply(false))
                    .Item("IconForward", "Forward", () => Fwd(false))
                    .Item("IconSaveAs", "Save As", SaveAs)
                    .Item("IconPrint", "Print", () => _viewer.Print())
                    .Sep()
                    .Item("IconDelete", "Delete", () => _ = DeleteAsync())
                    .Item("IconExit", "Close", Close)
                    .Recent("Message", () => new (string, string, Action)[0])
                    .Footer("Close", "IconExit", Close)
                    .Show(title.Orb), "Click here to reply, save or print this message.")
                .QatOption("Reply", "IconReply", "Reply", () => Reply(false))
                .QatOption("Previous", "IconPrevious", "Previous Item", () => _ = GoAsync(-1))
                .QatOption("Next", "IconNext", "Next Item", () => _ = GoAsync(1))
                .QatOption("Print", "IconPrint", "Print", () => _viewer.Print())
                .QatOption("Delete", "IconDelete", "Delete", () => _ = DeleteAsync());
            title.InitQat(() => new List<string> { "Previous", "Next" }, l => { }, ribbon, m => { Storage.Settings.RibbonMinimized = m; Storage.SaveSettings(); });

            var dp = new DockPanel();
            DockPanel.SetDock(ribbon, Dock.Top);
            dp.Children.Add(ribbon); dp.Children.Add(_viewer);
            Content = dp;

            PreviewKeyDown += OnKeys;
            Closed += (s, e) => _viewer.Cleanup();
            Loaded += async (s, e) => await ShowMessageAsync(msg);
        }

        async Task ShowMessageAsync(MimeMessage msg)
        {
            Title = (string.IsNullOrWhiteSpace(msg.Subject) ? "(No Subject)" : msg.Subject);
            await _viewer.ShowAsync(msg);
            if (_item != null && !_item.IsRead && _main != null) await _main.MarkReadAsync(new List<MailItem> { _item }, true);
        }

        void OnKeys(object sender, KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (mods == ModifierKeys.Control && e.Key == Key.R) { Reply(false); e.Handled = true; }
            else if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.R) { Reply(true); e.Handled = true; }
            else if (mods == ModifierKeys.Control && e.Key == Key.F) { Fwd(false); e.Handled = true; }
            else if (mods == ModifierKeys.Control && e.Key == Key.P) { _viewer.Print(); e.Handled = true; }
            else if (mods == ModifierKeys.Control && e.Key == Key.D) { _ = DeleteAsync(); e.Handled = true; }
            else if (mods == ModifierKeys.Control && (e.Key == Key.OemPeriod || e.Key == Key.OemComma))
            { _ = GoAsync(e.Key == Key.OemPeriod ? 1 : -1); e.Handled = true; }
        }

        void Reply(bool all) { if (_viewer.Message != null) ComposeWindow.Reply(_viewer.Message, all).Show(); }
        void Fwd(bool att) { if (_viewer.Message != null) ComposeWindow.Forward(_viewer.Message, att).Show(); }

        void AddSender()
        {
            var from = _viewer.Message?.From.Mailboxes.FirstOrDefault();
            if (from == null) return;
            var c = new Contact { Name = from.Name, Email = from.Address };
            if (ContactDialog.Edit(this, c, true)) { Storage.Contacts.Add(c); Storage.SaveContacts(); }
        }

        async Task GoAsync(int dir)
        {
            if (_main == null || _item == null) return;
            var next = _main.Neighbor(_item, dir);
            if (next == null) { System.Media.SystemSounds.Beep.Play(); return; }
            try
            {
                var msg = await next.Store.GetMessageAsync(next);
                _item = next;
                _main.SelectItem(next);
                await ShowMessageAsync(msg);
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message, "The message could not be opened."); }
        }

        async Task DeleteAsync()
        {
            if (_main == null || _item == null) { Close(); return; }
            var next = _main.Neighbor(_item, 1) ?? _main.Neighbor(_item, -1);
            if (!await _main.DeleteItemsAsync(new List<MailItem> { _item }, this)) return;
            if (next == null) { Close(); return; }
            try
            {
                var msg = await next.Store.GetMessageAsync(next);
                _item = next;
                _main.SelectItem(next);
                await ShowMessageAsync(msg);
            }
            catch { Close(); }
        }

        void SaveAs()
        {
            var msg = _viewer.Message;
            if (msg == null) return;
            var dlg = new SaveFileDialog
            {
                Title = "Save Message As", Filter = "Mail (*.eml)|*.eml|Text Files (*.txt)|*.txt",
                FileName = MailUtil.Safe(string.IsNullOrWhiteSpace(msg.Subject) ? "message" : msg.Subject)
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                if (dlg.FilterIndex == 2) File.WriteAllText(dlg.FileName, MailUtil.PlainText(msg));
                else using (var fs = File.Create(dlg.FileName)) msg.WriteTo(fs);
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message); }
        }
    }

    // ===================================================================== contacts
    public class ContactsWindow : VistaWindow
    {
        readonly ListView _list;
        readonly TextBox _search;
        readonly bool _pick;
        public List<Contact> Picked { get; private set; }

        public ContactsWindow(Window owner, bool pick)
        {
            _pick = pick;
            Title = pick ? "Select Recipients" : "Contacts";
            if (owner != null && owner.IsVisible) Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 600; Height = 440; MinWidth = 380; MinHeight = 260;
            ShowInTaskbar = !pick;

            _search = new TextBox { Width = 180, Style = (Style)FindResource("SearchBox"), VerticalAlignment = VerticalAlignment.Center };
            _search.TextChanged += (s, e) => Refresh();
            var barItems = new List<UIElement>
            {
                Cmd.Button("IconAddContact", "New Contact", (s, e) => NewContact()),
                Cmd.Button("IconProperties", "Properties", (s, e) => Props()),
                Cmd.Button("IconDelete", "Delete", (s, e) => Delete()),
                Cmd.Separator(),
                Cmd.Button("IconCreateMail", "E-mail", (s, e) => Email())
            };
            var bar = Cmd.Bar(barItems.ToArray());
            var barGrid = new DockPanel();
            DockPanel.SetDock(_search, Dock.Right);
            barGrid.Children.Add(_search);
            barGrid.Children.Add(bar.Child is UIElement child ? Detach(bar) : bar);
            bar.Child = barGrid;

            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "Name", Width = 210, DisplayMemberBinding = new System.Windows.Data.Binding("Name") });
            gv.Columns.Add(new GridViewColumn { Header = "E-mail Address", Width = 230, DisplayMemberBinding = new System.Windows.Data.Binding("Email") });
            gv.Columns.Add(new GridViewColumn { Header = "Source", Width = 110, DisplayMemberBinding = new System.Windows.Data.Binding("Source") });
            _list = new ListView { View = gv, BorderThickness = new Thickness(0), ItemContainerStyle = (Style)FindResource("VistaListViewItem") };
            _list.MouseDoubleClick += (s, e) => { if (_pick) Accept(); else Props(); };
            _list.KeyDown += (s, e) => { if (e.Key == Key.Delete && !_pick) Delete(); };

            var dp = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            dp.Children.Add(bar);
            if (_pick)
            {
                var strip = UI.Strip(UI.Btn("Add", (s, e) => Accept(), true), UI.Btn("Cancel", null, false, true));
                DockPanel.SetDock(strip, Dock.Bottom);
                dp.Children.Add(strip);
            }
            dp.Children.Add(_list);
            Content = dp;
            Refresh();
        }

        static UIElement Detach(Border b) { var c = b.Child; b.Child = null; return c; }

        void Refresh()
        {
            var q = _search.Text.Trim();
            _list.ItemsSource = AddressBook.All().Where(c => q.Length == 0 ||
                (c.Name ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                (c.Email ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        List<Contact> Sel => _list.SelectedItems.Cast<Contact>().ToList();

        void NewContact()
        {
            var c = new Contact();
            if (!ContactDialog.Edit(this, c, true)) return;
            Storage.Contacts.Add(c);
            Storage.SaveContacts();
            Refresh();
        }

        void Props()
        {
            var c = Sel.FirstOrDefault();
            if (c == null) return;
            if (c.ReadOnly)
            {
                try { Process.Start(new ProcessStartInfo(c.FilePath) { UseShellExecute = true }); } catch { }
                AddressBook.Invalidate();
                return;
            }
            if (ContactDialog.Edit(this, c, false)) { Storage.SaveContacts(); Refresh(); }
        }

        void Delete()
        {
            var sel = Sel.Where(c => !c.ReadOnly).ToList();
            if (sel.Count == 0) return;
            if (!VistaDialog.Confirm(this, sel.Count == 1 ? $"Are you sure you want to delete '{sel[0].Name ?? sel[0].Email}'?" : $"Are you sure you want to delete these {sel.Count} contacts?", DlgIcon.Warning)) return;
            foreach (var c in sel) Storage.Contacts.Remove(c);
            Storage.SaveContacts();
            Refresh();
        }

        void Email()
        {
            var sel = Sel.Where(c => !string.IsNullOrWhiteSpace(c.Email)).ToList();
            if (_pick) { Accept(); return; }
            ComposeWindow.New(string.Join("; ", sel.Select(c => c.Display))).Show();
        }

        void Accept()
        {
            Picked = Sel;
            DialogResult = true;
        }

        public static List<Contact> Pick(Window owner)
        {
            var w = new ContactsWindow(owner, true);
            return w.ShowDialog() == true ? w.Picked : null;
        }
    }

    public class ContactDialog : VistaWindow
    {
        public static bool Edit(Window owner, Contact c, bool isNew)
        {
            var d = new ContactDialog { Title = isNew ? "New Contact" : (c.Name ?? c.Email) + " Properties" };
            if (owner != null && owner.IsVisible) d.Owner = owner;
            d.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            d.ResizeMode = ResizeMode.NoResize;
            d.ShowInTaskbar = false;
            d.Width = 420;
            d.SizeToContent = SizeToContent.Height;
            var name = new TextBox { Text = c.Name ?? "", Height = 23, VerticalContentAlignment = VerticalAlignment.Center };
            var email = new TextBox { Text = c.Email ?? "", Height = 23, VerticalContentAlignment = VerticalAlignment.Center };
            var phone = new TextBox { Text = c.Phone ?? "", Height = 23, VerticalContentAlignment = VerticalAlignment.Center };
            var notes = new TextBox { Text = c.Notes ?? "", Height = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var g = Form(("Full name:", name), ("E-mail:", email), ("Phone:", phone), ("Notes:", notes));
            d.Content = UI.DialogLayout(g, UI.Strip(
                UI.Btn("OK", (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(email.Text) || !email.Text.Contains("@"))
                    { VistaDialog.Show(d, "Type an e-mail address such as someone@example.com.", null, MessageBoxButton.OK, DlgIcon.Warning); return; }
                    c.Name = name.Text.Trim(); c.Email = email.Text.Trim(); c.Phone = phone.Text.Trim(); c.Notes = notes.Text;
                    d.DialogResult = true;
                }, true),
                UI.Btn("Cancel", null, false, true)));
            d.Loaded += (s, e) => name.Focus();
            return d.ShowDialog() == true;
        }

        public static Grid Form(params (string Label, FrameworkElement Field)[] rows)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < rows.Length; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = rows[i].Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) };
                if (rows[i].Field.Height > 30) l.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetRow(l, i);
                rows[i].Field.Margin = new Thickness(0, 0, 0, 6);
                Grid.SetRow(rows[i].Field, i);
                Grid.SetColumn(rows[i].Field, 1);
                g.Children.Add(l);
                g.Children.Add(rows[i].Field);
            }
            return g;
        }
    }

    // ===================================================================== accounts
    public class AccountsWindow : VistaWindow
    {
        readonly ListView _list;
        readonly MainWindow _main;

        class Row
        {
            public Account A { get; set; }
            public string Name => A.Title + (A.IsDefault || (Mail.DefaultAccount == A) ? " (default)" : "");
            public string Type => "mail (IMAP)";
            public string Connection => A.ImapHost;
        }

        public AccountsWindow(MainWindow main)
        {
            _main = main;
            Title = "Internet Accounts";
            Owner = main;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Width = 560; Height = 360;

            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "Account", Width = 200, DisplayMemberBinding = new System.Windows.Data.Binding("Name") });
            gv.Columns.Add(new GridViewColumn { Header = "Type", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding("Type") });
            gv.Columns.Add(new GridViewColumn { Header = "Connection", Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding("Connection") });
            _list = new ListView { View = gv, ItemContainerStyle = (Style)FindResource("VistaListViewItem") };
            _list.MouseDoubleClick += (s, e) => Props();

            var buttons = new StackPanel { Margin = new Thickness(10, 0, 0, 0), Width = 110 };
            Button B(string t, Action a) { var b = UI.Btn(t, (s, e) => a()); b.Margin = new Thickness(0, 0, 0, 7); b.HorizontalAlignment = HorizontalAlignment.Stretch; return b; }
            buttons.Children.Add(B("Add...", Add));
            buttons.Children.Add(B("Remove", Remove));
            buttons.Children.Add(B("Properties", Props));
            buttons.Children.Add(B("Set as Default", SetDefault));

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());
            var hint = UI.Text("Valco Express connects to your e-mail with IMAP (incoming) and SMTP (outgoing). Select an account and click Properties to change its settings.");
            hint.Margin = new Thickness(0, 0, 0, 10);
            Grid.SetColumnSpan(hint, 2);
            g.Children.Add(hint);
            Grid.SetRow(_list, 1); g.Children.Add(_list);
            Grid.SetRow(buttons, 1); Grid.SetColumn(buttons, 1); g.Children.Add(buttons);
            Content = UI.DialogLayout(g, UI.Strip(UI.Btn("Close", (s, e) => Close(), true, true)));
            Refresh();
        }

        void Refresh() => _list.ItemsSource = Storage.Accounts.Select(a => new Row { A = a }).ToList();
        Account Sel => (_list.SelectedItem as Row)?.A;

        void Add()
        {
            var a = new Account();
            if (!AccountDialog.Edit(this, a, true)) return;
            if (Storage.Accounts.Count == 0) a.IsDefault = true;
            Storage.Accounts.Add(a);
            Storage.SaveAccounts();
            _main.AccountAdded(a);
            Refresh();
        }

        void Remove()
        {
            var a = Sel;
            if (a == null) return;
            if (!VistaDialog.Confirm(this, $"Are you sure you want to remove the account '{a.Title}'?\n\nMessages on the server are not deleted.", DlgIcon.Warning)) return;
            Storage.Accounts.Remove(a);
            if (a.IsDefault && Storage.Accounts.Count > 0) Storage.Accounts[0].IsDefault = true;
            Storage.SaveAccounts();
            _main.AccountRemoved(a);
            Refresh();
        }

        void Props()
        {
            var a = Sel;
            if (a == null) return;
            if (!AccountDialog.Edit(this, a, false)) return;
            Storage.SaveAccounts();
            _main.AccountChanged(a);
            Refresh();
        }

        void SetDefault()
        {
            var a = Sel;
            if (a == null) return;
            foreach (var x in Storage.Accounts) x.IsDefault = x == a;
            Storage.SaveAccounts();
            Refresh();
        }
    }

    public class AccountDialog : VistaWindow
    {
        static readonly (string[] Domains, string Imap, int ImapPort, ConnSecurity ImapSec, string Smtp, int SmtpPort, ConnSecurity SmtpSec)[] Presets =
        {
            (new[] { "gmail.com", "googlemail.com" }, "imap.gmail.com", 993, ConnSecurity.SslOnConnect, "smtp.gmail.com", 465, ConnSecurity.SslOnConnect),
            (new[] { "outlook.com", "hotmail.com", "live.com", "msn.com", "live.ca", "hotmail.ca" }, "outlook.office365.com", 993, ConnSecurity.SslOnConnect, "smtp-mail.outlook.com", 587, ConnSecurity.StartTls),
            (new[] { "yahoo.com", "yahoo.ca", "ymail.com", "rocketmail.com" }, "imap.mail.yahoo.com", 993, ConnSecurity.SslOnConnect, "smtp.mail.yahoo.com", 465, ConnSecurity.SslOnConnect),
            (new[] { "icloud.com", "me.com", "mac.com" }, "imap.mail.me.com", 993, ConnSecurity.SslOnConnect, "smtp.mail.me.com", 587, ConnSecurity.StartTls),
            (new[] { "aol.com" }, "imap.aol.com", 993, ConnSecurity.SslOnConnect, "smtp.aol.com", 465, ConnSecurity.SslOnConnect),
            (new[] { "gmx.com", "gmx.net" }, "imap.gmx.com", 993, ConnSecurity.SslOnConnect, "mail.gmx.com", 587, ConnSecurity.StartTls),
            (new[] { "zoho.com" }, "imap.zoho.com", 993, ConnSecurity.SslOnConnect, "smtp.zoho.com", 465, ConnSecurity.SslOnConnect),
            (new[] { "proton.me", "protonmail.com" }, "127.0.0.1", 1143, ConnSecurity.StartTls, "127.0.0.1", 1025, ConnSecurity.StartTls),
            (new[] { "fastmail.com" }, "imap.fastmail.com", 993, ConnSecurity.SslOnConnect, "smtp.fastmail.com", 465, ConnSecurity.SslOnConnect),
        };

        static ComboBox SecBox(ConnSecurity s)
        {
            var cb = new ComboBox { Height = 23, Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
            cb.Items.Add("SSL/TLS"); cb.Items.Add("STARTTLS"); cb.Items.Add("None (not secure)");
            cb.SelectedIndex = (int)s;
            return cb;
        }

        static TextBox Box(string t, double w = double.NaN) =>
            new TextBox { Text = t ?? "", Height = 23, VerticalContentAlignment = VerticalAlignment.Center, Width = w, HorizontalAlignment = double.IsNaN(w) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left };

        public static bool Edit(Window owner, Account a, bool isNew)
        {
            var d = new AccountDialog { Title = isNew ? "Add E-mail Account" : a.Title + " Properties" };
            d.Owner = owner;
            d.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            d.ResizeMode = ResizeMode.NoResize;
            d.ShowInTaskbar = false;
            d.Width = 520;
            d.SizeToContent = SizeToContent.Height;

            var display = Box(a.DisplayName);
            var email = Box(a.Email);
            var acctName = Box(a.AccountName);
            var imap = Box(a.ImapHost);
            var imapPort = Box(a.ImapPort.ToString(), 60);
            var imapSec = SecBox(a.ImapSecurity);
            var smtp = Box(a.SmtpHost);
            var smtpPort = Box(a.SmtpPort.ToString(), 60);
            var smtpSec = SecBox(a.SmtpSecurity);
            var smtpAuth = new CheckBox { Content = "Outgoing server requires authentication", IsChecked = a.SmtpRequiresAuth, Margin = new Thickness(0, 2, 0, 0) };
            var user = Box(a.Username);
            var pass = new PasswordBox { Password = a.Password ?? "", Height = 23, VerticalContentAlignment = VerticalAlignment.Center };
            var saveSent = new CheckBox { Content = "Save a copy of sent messages in this account's Sent folder", IsChecked = a.SaveSentCopy };
            var include = new CheckBox { Content = "Include this account when receiving mail or synchronizing", IsChecked = a.IncludeInSendReceive, Margin = new Thickness(0, 4, 0, 0) };

            void Autofill()
            {
                var domain = email.Text.Split('@').Skip(1).FirstOrDefault()?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(domain)) return;
                if (string.IsNullOrWhiteSpace(user.Text)) user.Text = email.Text.Trim();
                if (string.IsNullOrWhiteSpace(acctName.Text)) acctName.Text = domain;
                if (!string.IsNullOrWhiteSpace(imap.Text)) return;
                var p = Presets.FirstOrDefault(x => x.Domains.Contains(domain));
                if (p.Imap != null)
                {
                    imap.Text = p.Imap; imapPort.Text = p.ImapPort.ToString(); imapSec.SelectedIndex = (int)p.ImapSec;
                    smtp.Text = p.Smtp; smtpPort.Text = p.SmtpPort.ToString(); smtpSec.SelectedIndex = (int)p.SmtpSec;
                }
                else
                {
                    imap.Text = "imap." + domain; imapPort.Text = "993"; imapSec.SelectedIndex = 0;
                    smtp.Text = "smtp." + domain; smtpPort.Text = "587"; smtpSec.SelectedIndex = 1;
                }
            }
            email.LostFocus += (s, e) => Autofill();
            imapSec.SelectionChanged += (s, e) => { if (imapPort.Text == "993" || imapPort.Text == "143") imapPort.Text = imapSec.SelectedIndex == 0 ? "993" : "143"; };
            smtpSec.SelectionChanged += (s, e) => { if (smtpPort.Text == "465" || smtpPort.Text == "587" || smtpPort.Text == "25") smtpPort.Text = smtpSec.SelectedIndex == 0 ? "465" : "587"; };

            StackPanel Row(params FrameworkElement[] els)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var el in els) { el.Margin = new Thickness(0, 0, 8, 0); sp.Children.Add(el); }
                return sp;
            }
            GroupBox Group(string header, Grid content) => new GroupBox { Header = header, Padding = new Thickness(8, 6, 8, 2), Margin = new Thickness(0, 0, 0, 8), Content = content };
            TextBlock L(string t) => new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Center };

            var root = new StackPanel();
            root.Children.Add(Group("User Information", ContactDialog.Form(("Your name:", display), ("E-mail address:", email), ("Account name:", acctName))));
            root.Children.Add(Group("Incoming Mail (IMAP)", ContactDialog.Form(("Server:", imap), ("Port:", Row(imapPort, L("Security:"), imapSec)))));
            var smtpRows = ContactDialog.Form(("Server:", smtp), ("Port:", Row(smtpPort, L("Security:"), smtpSec)), ("", smtpAuth));
            root.Children.Add(Group("Outgoing Mail (SMTP)", smtpRows));
            root.Children.Add(Group("Logon", ContactDialog.Form(("User name:", user), ("Password:", pass))));
            root.Children.Add(saveSent);
            root.Children.Add(include);
            root.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x5F, 0x6B)),
                Text = "Tip: Gmail, Yahoo and iCloud need an app password (create one in your account's security settings after turning on two-step verification). Outlook.com and Hotmail require Microsoft sign-in and may refuse passwords."
            });

            bool Apply(Account target)
            {
                if (string.IsNullOrWhiteSpace(email.Text) || !email.Text.Contains("@")) { VistaDialog.Show(d, "Type your e-mail address.", null, MessageBoxButton.OK, DlgIcon.Warning); email.Focus(); return false; }
                Autofill();
                if (string.IsNullOrWhiteSpace(imap.Text) || string.IsNullOrWhiteSpace(smtp.Text)) { VistaDialog.Show(d, "Type the incoming and outgoing server names.", null, MessageBoxButton.OK, DlgIcon.Warning); return false; }
                if (!int.TryParse(imapPort.Text, out var ip) || !int.TryParse(smtpPort.Text, out var sp)) { VistaDialog.Show(d, "Port numbers must be numbers, such as 993 or 587.", null, MessageBoxButton.OK, DlgIcon.Warning); return false; }
                target.DisplayName = display.Text.Trim();
                target.Email = email.Text.Trim();
                target.AccountName = string.IsNullOrWhiteSpace(acctName.Text) ? target.Email : acctName.Text.Trim();
                target.ImapHost = imap.Text.Trim(); target.ImapPort = ip; target.ImapSecurity = (ConnSecurity)imapSec.SelectedIndex;
                target.SmtpHost = smtp.Text.Trim(); target.SmtpPort = sp; target.SmtpSecurity = (ConnSecurity)smtpSec.SelectedIndex;
                target.SmtpRequiresAuth = smtpAuth.IsChecked == true;
                target.Username = string.IsNullOrWhiteSpace(user.Text) ? target.Email : user.Text.Trim();
                target.Password = pass.Password;
                target.SaveSentCopy = saveSent.IsChecked == true;
                target.IncludeInSendReceive = include.IsChecked == true;
                return true;
            }

            Button test = null;
            test = UI.Btn("Test Settings", async (s, e) =>
            {
                var tmp = new Account();
                if (!Apply(tmp)) return;
                test.IsEnabled = false;
                d.Cursor = Cursors.Wait;
                string imapResult, smtpResult;
                try { await ImapStore.TestAsync(tmp); imapResult = "Incoming (IMAP): connected and signed in."; }
                catch (Exception ex) { imapResult = "Incoming (IMAP): " + ex.Message; }
                try { await Mail.TestSmtpAsync(tmp); smtpResult = "Outgoing (SMTP): connected" + (tmp.SmtpRequiresAuth ? " and signed in." : "."); }
                catch (Exception ex) { smtpResult = "Outgoing (SMTP): " + ex.Message; }
                d.Cursor = null;
                test.IsEnabled = true;
                bool ok = imapResult.Contains("signed in") && smtpResult.Contains("connected");
                VistaDialog.Show(d, imapResult + "\n\n" + smtpResult, "Test Account Settings", MessageBoxButton.OK, ok ? DlgIcon.Info : DlgIcon.Warning,
                    ok ? "Congratulations! Everything works." : "Some tests didn't pass");
            });
            test.Margin = new Thickness(0);
            var strip = UI.Strip(UI.Btn("OK", (s, e) => { if (Apply(a)) d.DialogResult = true; }, true), UI.Btn("Cancel", null, false, true));
            var leftDock = new DockPanel();
            DockPanel.SetDock(test, Dock.Left);
            leftDock.Children.Add(test);
            leftDock.Children.Add(strip.Child is UIElement c ? Take(strip) : null);
            strip.Child = leftDock;

            d.Content = UI.DialogLayout(root, strip);
            d.Loaded += (s, e) => (isNew ? (Control)display : imap).Focus();
            return d.ShowDialog() == true;
        }

        static UIElement Take(Border b) { var c = b.Child; b.Child = null; return c; }
    }

    // ===================================================================== options
    public class OptionsWindow : VistaWindow
    {
        public OptionsWindow(Window owner)
        {
            Title = "Options";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Width = 480;
            SizeToContent = SizeToContent.Height;
            var st = Storage.Settings;

            CheckBox Chk(string t, bool v) => new CheckBox { Content = t, IsChecked = v, Margin = new Thickness(0, 0, 0, 8) };
            TextBox Num(int v) => new TextBox { Text = v.ToString(), Width = 44, Height = 22, Margin = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
            StackPanel Line(params UIElement[] els)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                foreach (var el in els) sp.Children.Add(el);
                return sp;
            }

            var playSound = Chk("Play sound when new messages arrive", st.PlaySound);
            var atStartup = Chk("Send and receive messages at startup", st.SendReceiveAtStartup);
            var checkEvery = new CheckBox { Content = "Check for new messages every", IsChecked = st.CheckMinutes > 0, VerticalAlignment = VerticalAlignment.Center };
            var minutes = Num(st.CheckMinutes > 0 ? st.CheckMinutes : 10);
            var emptyOnExit = Chk("Empty messages from the 'Deleted Items' folder in Local Folders on exit", st.EmptyDeletedOnExit);
            var notify = Chk("Show a Windows notification when new messages arrive", st.ShowNotifications);
            var toTray = Chk("Keep running in the notification area when the window is closed", st.CloseToTray);
            var autostart = Chk("Start Valco Express in the background when I sign in to Windows", st.StartWithWindows);
            var general = new StackPanel { Margin = new Thickness(12) };
            general.Children.Add(playSound);
            general.Children.Add(atStartup);
            general.Children.Add(Line(checkEvery, minutes, new TextBlock { Text = "minute(s)", VerticalAlignment = VerticalAlignment.Center }));
            var scheme = new ComboBox { Width = 120, Height = 23, Margin = new Thickness(8, 0, 0, 0), ItemsSource = OfficeTheme.Schemes, SelectedItem = OfficeTheme.Current };
            general.Children.Add(Line(new TextBlock { Text = "Color scheme:", VerticalAlignment = VerticalAlignment.Center }, scheme));
            general.Children.Add(notify);
            general.Children.Add(toTray);
            general.Children.Add(autostart);
            general.Children.Add(emptyOnExit);

            var markRead = new CheckBox { Content = "Mark message read after displaying for", IsChecked = st.MarkReadEnabled, VerticalAlignment = VerticalAlignment.Center };
            var seconds = Num(st.MarkReadSeconds);
            var maxMsgs = Num(st.MaxMessages);
            maxMsgs.Width = 56;
            var read = new StackPanel { Margin = new Thickness(12) };
            read.Children.Add(Line(markRead, seconds, new TextBlock { Text = "second(s)", VerticalAlignment = VerticalAlignment.Center }));
            read.Children.Add(Line(new TextBlock { Text = "Show up to", VerticalAlignment = VerticalAlignment.Center }, maxMsgs,
                new TextBlock { Text = "of the newest messages in each server folder", VerticalAlignment = VerticalAlignment.Center }));

            var font = new ComboBox { Width = 200, Height = 23, ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(x => x).ToList(), SelectedItem = st.ComposeFont };
            var size = new ComboBox { Width = 60, Height = 23, Margin = new Thickness(8, 0, 0, 0), ItemsSource = new double[] { 8, 9, 10, 11, 12, 14, 16, 18 }, SelectedItem = st.ComposeSize };
            var compose = new StackPanel { Margin = new Thickness(12) };
            compose.Children.Add(UI.Text("Default font for new messages:"));
            var fontLine = Line(font, size);
            fontLine.Margin = new Thickness(0, 6, 0, 8);
            compose.Children.Add(fontLine);

            var blockImg = Chk("Block images and other external content in HTML e-mail", st.BlockImages);
            var blockAtt = Chk("Do not allow attachments to be opened that could potentially be a virus", st.BlockUnsafeAttachments);
            var security = new StackPanel { Margin = new Thickness(12) };
            security.Children.Add(blockAtt);
            security.Children.Add(blockImg);
            security.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Text = "Scripts in HTML messages never run in Valco Express." });

            var tabs = new TabControl { Height = 290 };
            tabs.Items.Add(new TabItem { Header = "General", Content = general });
            tabs.Items.Add(new TabItem { Header = "Read", Content = read });
            tabs.Items.Add(new TabItem { Header = "Compose", Content = compose });
            tabs.Items.Add(new TabItem { Header = "Security", Content = security });

            void Apply()
            {
                st.PlaySound = playSound.IsChecked == true;
                st.SendReceiveAtStartup = atStartup.IsChecked == true;
                st.CheckMinutes = checkEvery.IsChecked == true && int.TryParse(minutes.Text, out var m) ? Math.Max(1, m) : 0;
                st.EmptyDeletedOnExit = emptyOnExit.IsChecked == true;
                st.ShowNotifications = notify.IsChecked == true;
                st.CloseToTray = toTray.IsChecked == true;
                st.StartWithWindows = autostart.IsChecked == true;
                AutoStart.Apply(st.StartWithWindows);
                if (scheme.SelectedItem is string sc) { st.ColorScheme = sc; OfficeTheme.Apply(sc); }
                st.MarkReadEnabled = markRead.IsChecked == true;
                if (int.TryParse(seconds.Text, out var sec)) st.MarkReadSeconds = Math.Max(0, sec);
                if (int.TryParse(maxMsgs.Text, out var mx)) st.MaxMessages = Math.Max(50, mx);
                if (font.SelectedItem is string f) st.ComposeFont = f;
                if (size.SelectedItem is double sz) st.ComposeSize = sz;
                st.BlockImages = blockImg.IsChecked == true;
                st.BlockUnsafeAttachments = blockAtt.IsChecked == true;
                Storage.SaveSettings();
                (owner as MainWindow)?.SettingsChanged();
            }

            var dp = UI.DialogLayout(tabs, UI.Strip(
                UI.Btn("OK", (s, e) => { Apply(); DialogResult = true; }, true),
                UI.Btn("Cancel", null, false, true),
                UI.Btn("Apply", (s, e) => Apply())));
            Content = dp;
        }
    }

    // ===================================================================== about
    public class AboutWindow : VistaWindow
    {
        public AboutWindow(Window owner)
        {
            Title = "About Valco Express";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Width = 460;
            SizeToContent = SizeToContent.Height;

            var banner = new Border
            {
                Height = 96,
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(0x0E, 0x3E, 0x7A), 0), new GradientStop(Color.FromRgb(0x1D, 0x6F, 0xC0), 0.55),
                        new GradientStop(Color.FromRgb(0x2F, 0xA7, 0xC9), 1)
                    }, new Point(0, 0), new Point(1, 1))
            };
            var bg = new Grid();
            bg.Children.Add(new Border
            {
                Background = new LinearGradientBrush(Color.FromArgb(0x60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0, 0.5))
            });
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(UI.Icon("IconApp", 60));
            var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = "Valco Express", FontSize = 26, FontWeight = FontWeights.Light, Foreground = Brushes.White });
            titles.Children.Add(new TextBlock { Text = "E-mail, the Vista way", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 255, 255, 255)) });
            sp.Children.Add(titles);
            bg.Children.Add(sp);
            banner.Child = bg;

            var ver = typeof(AboutWindow).Assembly.GetName().Version;
            var info = new StackPanel { Margin = new Thickness(22, 14, 22, 16) };
            info.Children.Add(UI.Text($"Version {ver.Major}.{ver.Minor}.{ver.Build}"));
            info.Children.Add(UI.Text("An e-mail program for Windows, from the Valco suite.", 4));
            var suite = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var mark = OfficeTheme.SuiteMark;
            if (mark != null) suite.Children.Add(new Image { Source = mark, Width = 28, Height = 28, Margin = new Thickness(0, 0, 8, 0) });
            var suiteText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            suiteText.Children.Add(new TextBlock { Text = "Valco Office", FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x42, 0x8B)) });
            suiteText.Children.Add(new TextBlock { Text = "Valco Express · Valco Word · Valco Spreadsheets · Valco Code", Foreground = Brushes.DimGray, FontSize = 11 });
            suite.Children.Add(suiteText);
            info.Children.Add(suite);
            info.Children.Add(UI.Text("© 2026 ItsRaiden", 12));
            info.Children.Add(UI.Text("Built with .NET, WPF, MailKit and Microsoft Edge WebView2.", 10));
            var nb = UI.Text("Valco Express is an independent project inspired by Windows Mail for Windows Vista. It is not made by or affiliated with Microsoft.", 10);
            nb.Foreground = Brushes.DimGray;
            info.Children.Add(nb);

            var main = new DockPanel();
            DockPanel.SetDock(banner, Dock.Top);
            main.Children.Add(banner);
            var strip = UI.Strip(UI.Btn("OK", (s, e) => Close(), true, true));
            DockPanel.SetDock(strip, Dock.Bottom);
            main.Children.Add(strip);
            main.Children.Add(new Border { Background = Brushes.White, Child = info });
            Content = main;
        }
    }
}
