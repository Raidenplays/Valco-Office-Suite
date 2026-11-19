using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MimeKit;
using MimeKit.Utils;

namespace ValcoExpress
{
    public partial class ComposeWindow : VistaWindow
    {
        const string DraftRtfType = "x-valco-rtf";

        readonly List<string> _attachments = new List<string>();
        readonly List<string> _tempDirs = new List<string>();
        string _inReplyTo;
        List<string> _references = new List<string>();
        int _priority = 3;
        bool _dirty, _sent, _updating, _savedNoticeShown;
        MailItem _draftItem;

        static readonly (string Name, Color Color)[] Palette =
        {
            ("Black", Colors.Black), ("Maroon", Color.FromRgb(0x80,0,0)), ("Green", Color.FromRgb(0,0x80,0)), ("Olive", Color.FromRgb(0x80,0x80,0)),
            ("Navy", Color.FromRgb(0,0,0x80)), ("Purple", Color.FromRgb(0x80,0,0x80)), ("Teal", Color.FromRgb(0,0x80,0x80)), ("Gray", Color.FromRgb(0x80,0x80,0x80)),
            ("Silver", Color.FromRgb(0xC0,0xC0,0xC0)), ("Red", Color.FromRgb(0xE0,0,0)), ("Lime", Color.FromRgb(0,0xC0,0)), ("Yellow", Color.FromRgb(0xE6,0xC8,0)),
            ("Blue", Color.FromRgb(0,0,0xE0)), ("Fuchsia", Color.FromRgb(0xE0,0,0xE0)), ("Aqua", Color.FromRgb(0,0xB0,0xC8)), ("White", Colors.White)
        };

        public ComposeWindow()
        {
            InitializeComponent();
            BuildRibbon();

            if (Storage.Accounts.Count == 0)
            {
                FromBox.Items.Add("(no account set up — the message will wait in your Outbox)");
                FromBox.SelectedIndex = 0;
                FromBox.IsEnabled = false;
            }
            else
            {
                foreach (var a in Storage.Accounts) FromBox.Items.Add(a);
                FromBox.SelectedItem = Mail.DefaultAccount;
            }

            _updating = true;
            FontBox.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(x => x).ToList();
            SizeBox.ItemsSource = new double[] { 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36, 48 };
            Editor.Document.FontFamily = new FontFamily(Storage.Settings.ComposeFont);
            Editor.Document.FontSize = PtToPx(Storage.Settings.ComposeSize);
            Editor.Document.PagePadding = new Thickness(10, 8, 10, 8);
            FontBox.SelectedItem = Storage.Settings.ComposeFont;
            SizeBox.SelectedItem = Storage.Settings.ComposeSize;
            _updating = false;

            Editor.SelectionChanged += (s, e) => UpdateFormatState();
            Editor.TextChanged += (s, e) => _dirty = true;
            ToBox.TextChanged += (s, e) => _dirty = true;
            CcBox.TextChanged += (s, e) => _dirty = true;
            BccBox.TextChanged += (s, e) => _dirty = true;
            SubjectBox.TextChanged += (s, e) =>
            {
                _dirty = true;
                Title = string.IsNullOrWhiteSpace(SubjectBox.Text) ? "New Message" : SubjectBox.Text;
            };
            PreviewKeyDown += OnKeys;
            PreviewDragOver += OnDragOver;
            PreviewDrop += OnDrop;
            Closing += OnClosing;
            Closed += (s, e) =>
            {
                foreach (var d in _tempDirs) { try { Directory.Delete(d, true); } catch { } }
            };
            Loaded += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(ToBox.Text)) ToBox.Focus();
                else
                {
                    Editor.Focus();
                    Editor.CaretPosition = Editor.Document.ContentStart;
                }
                _dirty = false;
            };
            UpdatePriorityUi();
        }

        static double PtToPx(double pt) => pt * 96.0 / 72.0;
        static double PxToPt(double px) => Math.Round(px * 72.0 / 96.0 * 2) / 2;

        // ================================================================ factories
        public static ComposeWindow New(string to = null, string subject = null, string body = null)
        {
            var w = new ComposeWindow();
            if (to != null) w.ToBox.Text = to.Trim().TrimEnd(';') + "; ";
            if (subject != null) w.SubjectBox.Text = subject;
            if (!string.IsNullOrEmpty(body)) w.SetPlainBody(body);
            return w;
        }

        public static ComposeWindow FromMailto(string uri)
        {
            string to = "", subject = null, body = null, cc = null, bcc = null;
            try
            {
                var rest = uri.Substring("mailto:".Length);
                var q = rest.IndexOf('?');
                to = Uri.UnescapeDataString(q >= 0 ? rest.Substring(0, q) : rest);
                if (q >= 0)
                {
                    foreach (var kv in rest.Substring(q + 1).Split('&'))
                    {
                        var i = kv.IndexOf('=');
                        if (i < 0) continue;
                        var k = kv.Substring(0, i).ToLowerInvariant();
                        var v = Uri.UnescapeDataString(kv.Substring(i + 1).Replace('+', ' '));
                        if (k == "subject") subject = v;
                        else if (k == "body") body = v;
                        else if (k == "cc") cc = v;
                        else if (k == "bcc") bcc = v;
                    }
                }
            }
            catch { }
            var w = New(to, subject, body);
            if (cc != null) w.CcBox.Text = cc;
            if (bcc != null) { w.BccBox.Text = bcc; w.ShowBcc(true); }
            return w;
        }

        static string Prefix(string prefix, string subject)
        {
            subject = subject ?? "";
            var p = prefix.TrimEnd(':', ' ').ToLowerInvariant();
            var s = subject.TrimStart().ToLowerInvariant();
            if (s.StartsWith(p + ":") || (p == "fw" && s.StartsWith("fwd:"))) return subject;
            return prefix + subject;
        }

        static string Join(IEnumerable<MailboxAddress> list)
        {
            var text = string.Join("; ", list.GroupBy(m => m.Address.ToLowerInvariant()).Select(g => g.First().ToString()));
            return text.Length > 0 ? text + "; " : "";
        }

        public static ComposeWindow Reply(MimeMessage orig, bool all)
        {
            var w = new ComposeWindow();
            var mine = new HashSet<string>(Storage.Accounts.Select(a => (a.Email ?? "").ToLowerInvariant()));
            var replyTo = orig.ReplyTo.Mailboxes.Any() ? orig.ReplyTo.Mailboxes.ToList() : orig.From.Mailboxes.ToList();
            var to = new List<MailboxAddress>(replyTo);
            var cc = new List<MailboxAddress>();
            if (all)
            {
                to.AddRange(orig.To.Mailboxes.Where(m => !mine.Contains(m.Address.ToLowerInvariant())));
                cc.AddRange(orig.Cc.Mailboxes.Where(m => !mine.Contains(m.Address.ToLowerInvariant())));
                if (to.Count == 0) to.AddRange(replyTo);
            }
            w.ToBox.Text = Join(to);
            w.CcBox.Text = Join(cc.Where(c => !to.Any(t => t.Address.Equals(c.Address, StringComparison.OrdinalIgnoreCase))));
            w.SubjectBox.Text = Prefix("Re: ", orig.Subject);
            w._inReplyTo = orig.MessageId;
            w._references = orig.References.ToList();
            if (!string.IsNullOrEmpty(orig.MessageId)) w._references.Add(orig.MessageId);

            var addressed = orig.To.Mailboxes.Concat(orig.Cc.Mailboxes).Select(m => m.Address.ToLowerInvariant()).ToList();
            var acct = Storage.Accounts.FirstOrDefault(a => addressed.Contains((a.Email ?? "").ToLowerInvariant()));
            if (acct != null) w.FromBox.SelectedItem = acct;

            w.SetQuotedBody(orig);
            return w;
        }

        public static ComposeWindow Forward(MimeMessage orig, bool asAttachment)
        {
            var w = new ComposeWindow();
            w.SubjectBox.Text = Prefix("Fw: ", orig.Subject);
            var dir = w.NewTempDir();
            if (asAttachment)
            {
                var path = Path.Combine(dir, MailUtil.Safe(string.IsNullOrWhiteSpace(orig.Subject) ? "message" : orig.Subject) + ".eml");
                using (var fs = File.Create(path)) orig.WriteTo(fs);
                w.AddAttachment(path);
            }
            else
            {
                w.SetQuotedBody(orig);
                foreach (var a in orig.Attachments)
                {
                    try
                    {
                        var path = Path.Combine(dir, MailUtil.Safe(MailUtil.FileNameOf(a)));
                        MailUtil.SaveEntity(a, path);
                        w.AddAttachment(path);
                    }
                    catch { }
                }
            }
            return w;
        }

        public static ComposeWindow FromDraft(MimeMessage draft, MailItem item)
        {
            var w = new ComposeWindow();
            w._draftItem = item;
            w.ToBox.Text = Join(draft.To.Mailboxes);
            w.CcBox.Text = Join(draft.Cc.Mailboxes);
            w.BccBox.Text = Join(draft.Bcc.Mailboxes);
            if (draft.Bcc.Count > 0) w.ShowBcc(true);
            w.SubjectBox.Text = draft.Subject ?? "";
            w._inReplyTo = draft.InReplyTo;
            w._references = draft.References.ToList();
            w._priority = MailUtil.PriorityOf(draft);
            var acctId = draft.Headers["X-Valco-Account"];
            var acct = Storage.Accounts.FirstOrDefault(a => a.Id == acctId);
            if (acct != null) w.FromBox.SelectedItem = acct;

            var dir = w.NewTempDir();
            bool loaded = false;
            foreach (var a in draft.Attachments)
            {
                if (a is MimePart p && p.ContentType.MediaSubtype == DraftRtfType)
                {
                    try
                    {
                        using (var ms = new MemoryStream())
                        {
                            p.Content.DecodeTo(ms);
                            ms.Position = 0;
                            new TextRange(w.Editor.Document.ContentStart, w.Editor.Document.ContentEnd).Load(ms, DataFormats.Rtf);
                            loaded = true;
                        }
                    }
                    catch { }
                    continue;
                }
                try
                {
                    var path = Path.Combine(dir, MailUtil.Safe(MailUtil.FileNameOf(a)));
                    MailUtil.SaveEntity(a, path);
                    w.AddAttachment(path);
                }
                catch { }
            }
            if (!loaded) w.SetPlainBody(MailUtil.PlainText(draft));
            w.UpdatePriorityUi();
            return w;
        }

        string NewTempDir()
        {
            var d = Path.Combine(Storage.LocalDir, "compose", Guid.NewGuid().ToString("N").Substring(0, 10));
            Directory.CreateDirectory(d);
            _tempDirs.Add(d);
            return d;
        }

        void SetPlainBody(string text)
        {
            Editor.Document.Blocks.Clear();
            var p = new Paragraph();
            AddLines(p, text);
            Editor.Document.Blocks.Add(p);
        }

        static void AddLines(Paragraph p, string text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) p.Inlines.Add(new LineBreak());
                if (lines[i].Length > 0) p.Inlines.Add(new Run(lines[i]));
            }
        }

        void SetQuotedBody(MimeMessage orig)
        {
            var doc = Editor.Document;
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph());
            doc.Blocks.Add(new Paragraph());

            var quote = new Section
            {
                BorderBrush = Brushes.Black, BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(6, 0, 0, 0), Margin = new Thickness(5, 0, 0, 0)
            };
            var hdr = new Paragraph { FontFamily = new FontFamily("Arial"), FontSize = PtToPx(10) };
            hdr.Inlines.Add(new Run("----- Original Message -----"));
            void H(string label, string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                hdr.Inlines.Add(new LineBreak());
                hdr.Inlines.Add(new Bold(new Run(label + " ")));
                hdr.Inlines.Add(new Run(value));
            }
            H("From:", MailUtil.Full(orig.From));
            H("To:", MailUtil.Full(orig.To));
            H("Cc:", MailUtil.Full(orig.Cc));
            H("Sent:", orig.Date.LocalDateTime.ToString("dddd, MMMM d, yyyy h:mm tt"));
            H("Subject:", orig.Subject);
            quote.Blocks.Add(hdr);
            quote.Blocks.Add(new Paragraph());
            var body = new Paragraph();
            AddLines(body, MailUtil.PlainText(orig).TrimEnd());
            quote.Blocks.Add(body);
            doc.Blocks.Add(quote);
        }

        // ================================================================ attachments
        void AddAttachment(string path)
        {
            if (_attachments.Any(a => string.Equals(a, path, StringComparison.OrdinalIgnoreCase))) return;
            _attachments.Add(path);
            _dirty = true;
            RenderAttachments();
        }

        void RenderAttachments()
        {
            AttachPanel.Children.Clear();
            foreach (var path in _attachments.ToList())
            {
                long size = 0;
                try { size = new FileInfo(path).Length; } catch { }
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(UI.Icon("IconAttach", 16));
                sp.Children.Add(new TextBlock { Text = $"{Path.GetFileName(path)} ({Math.Max(1, size / 1024):N0} KB)", Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                var btn = new Button { Content = sp, Style = (Style)FindResource("FmtButton"), Margin = new Thickness(0, 0, 4, 2), ToolTip = path + "\nClick to remove" };
                var p = path;
                btn.Click += (s, e) =>
                {
                    var cm = new ContextMenu();
                    var rm = new MenuItem { Header = "_Remove" };
                    rm.Click += (s2, e2) => { _attachments.Remove(p); _dirty = true; RenderAttachments(); };
                    cm.Items.Add(rm);
                    cm.PlacementTarget = btn;
                    cm.IsOpen = true;
                };
                AttachPanel.Children.Add(btn);
            }
            AttachRow.Height = _attachments.Count > 0 ? GridLength.Auto : new GridLength(0);
        }

        void Attach_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Multiselect = true, Title = "Insert Attachment", Filter = "All Files (*.*)|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            foreach (var f in dlg.FileNames) AddAttachment(f);
        }

        void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            foreach (var f in (string[])e.Data.GetData(DataFormats.FileDrop))
                if (File.Exists(f)) AddAttachment(f);
            e.Handled = true;
        }

        // ================================================================ building / sending
        static bool ParseInto(InternetAddressList list, string text, out string bad)
        {
            bad = null;
            var t = Regex.Replace((text ?? "").Replace(';', ','), @"^[,\s]+|[,\s]+$", "");
            t = Regex.Replace(t, @",\s*,", ",");
            if (t.Length == 0) return true;
            if (!InternetAddressList.TryParse(t, out var parsed)) { bad = text.Trim(); return false; }
            foreach (var m in parsed.Mailboxes)
            {
                if (!m.Address.Contains("@")) { bad = m.ToString(); return false; }
            }
            list.AddRange(parsed);
            return true;
        }

        MimeMessage Build(bool forDraft, bool validate)
        {
            var msg = new MimeMessage();
            var acct = FromBox.SelectedItem as Account;
            if (acct != null) msg.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(acct.DisplayName) ? acct.Email : acct.DisplayName, acct.Email));

            foreach (var (box, list) in new[] { (ToBox, msg.To), (CcBox, msg.Cc), (BccBox, msg.Bcc) })
            {
                if (!ParseInto(list, box.Text, out var bad) && validate)
                {
                    VistaDialog.Show(this, $"Valco Express couldn't understand the address \"{bad}\". Check that each address looks like name@example.com and that addresses are separated by semicolons.",
                        null, MessageBoxButton.OK, DlgIcon.Warning, "Check the recipient names");
                    box.Focus();
                    return null;
                }
            }
            msg.Subject = SubjectBox.Text;
            msg.Date = DateTimeOffset.Now;
            var domain = acct?.Email?.Split('@').LastOrDefault();
            msg.MessageId = MimeUtils.GenerateMessageId(string.IsNullOrWhiteSpace(domain) ? "localhost" : domain);
            if (!string.IsNullOrEmpty(_inReplyTo)) msg.InReplyTo = _inReplyTo;
            foreach (var r in _references) msg.References.Add(r);
            if (_priority == 1) { msg.Importance = MessageImportance.High; msg.XPriority = XMessagePriority.High; msg.Priority = MessagePriority.Urgent; }
            else if (_priority == 5) { msg.Importance = MessageImportance.Low; msg.XPriority = XMessagePriority.Low; msg.Priority = MessagePriority.NonUrgent; }

            var doc = Editor.Document;
            var bb = new BodyBuilder
            {
                HtmlBody = HtmlWriter.ToHtml(doc),
                TextBody = new TextRange(doc.ContentStart, doc.ContentEnd).Text
            };
            foreach (var f in _attachments)
            {
                if (!File.Exists(f))
                {
                    if (validate) { VistaDialog.Error(this, f, "An attachment could not be found."); return null; }
                    continue;
                }
                bb.Attachments.Add(f);
            }
            if (forDraft)
            {
                using (var ms = new MemoryStream())
                {
                    new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.Rtf);
                    bb.Attachments.Add("draft-body.rtf", ms.ToArray(), new ContentType("application", DraftRtfType));
                }
            }
            msg.Body = bb.ToMessageBody();
            if (acct != null) msg.Headers.Add("X-Valco-Account", acct.Id);
            msg.Headers.Add("X-Mailer", "Valco Express 1.0");
            return msg;
        }

        async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

        async Task SendAsync()
        {
            var msg = Build(false, true);
            if (msg == null) return;
            if (msg.To.Count + msg.Cc.Count + msg.Bcc.Count == 0)
            {
                VistaDialog.Show(this, "Type at least one e-mail address in the To, Cc or Bcc box.", null, MessageBoxButton.OK, DlgIcon.Warning, "The message must have at least one recipient");
                ToBox.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(SubjectBox.Text) &&
                VistaDialog.Show(this, "Do you want to send it without a subject?", null, MessageBoxButton.YesNo, DlgIcon.Question, "This message has no subject") != MessageBoxResult.Yes)
            {
                SubjectBox.Focus();
                return;
            }
            IsEnabled = false;
            try
            {
                await Sender.SendAsync(this, msg, FromBox.SelectedItem as Account);
                _sent = true;
                if (_draftItem != null)
                {
                    try { await _draftItem.Store.DeletePermanentAsync(new[] { _draftItem }); } catch { }
                    Sender.RaiseChanged();
                }
                Close();
            }
            finally { IsEnabled = true; }
        }

        async void Save_Click(object sender, RoutedEventArgs e) => await SaveDraftAsync(true);

        async Task<bool> SaveDraftAsync(bool notify)
        {
            var msg = Build(true, false);
            if (msg == null) return false;
            var acct = FromBox.SelectedItem as Account;
            IMailStore store = acct != null ? (IMailStore)Mail.StoreFor(acct) : Mail.Local;
            var drafts = store?.Find(FolderKind.Drafts);
            if (drafts == null) { store = Mail.Local; drafts = Mail.Local.Find(FolderKind.Drafts); }
            try
            {
                var key = await store.AppendAsync(drafts, msg, true);
                if (_draftItem != null)
                {
                    try { await _draftItem.Store.DeletePermanentAsync(new[] { _draftItem }); } catch { }
                }
                _draftItem = key == null ? null : new MailItem { Store = store, Folder = drafts, Key = key, Uid = uint.TryParse(key, out var u) ? u : 0 };
                _dirty = false;
                Sender.RaiseChanged();
                if (notify && !_savedNoticeShown)
                {
                    _savedNoticeShown = true;
                    VistaDialog.Show(this, $"Your message has been saved in your '{drafts.Name}' folder.", "Saved Message", MessageBoxButton.OK, DlgIcon.Info);
                }
                return true;
            }
            catch (Exception ex)
            {
                VistaDialog.Error(this, ex.Message, "The message could not be saved.");
                return false;
            }
        }

        void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            var msg = Build(false, false);
            if (msg == null) return;
            var dlg = new SaveFileDialog
            {
                Title = "Save Message As", Filter = "Mail (*.eml)|*.eml|Text Files (*.txt)|*.txt|HTML (*.htm)|*.htm",
                FileName = MailUtil.Safe(string.IsNullOrWhiteSpace(SubjectBox.Text) ? "Untitled" : SubjectBox.Text)
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                switch (dlg.FilterIndex)
                {
                    case 2: File.WriteAllText(dlg.FileName, msg.TextBody ?? ""); break;
                    case 3: File.WriteAllText(dlg.FileName, msg.HtmlBody ?? ""); break;
                    default: using (var fs = File.Create(dlg.FileName)) msg.WriteTo(fs); break;
                }
            }
            catch (Exception ex) { VistaDialog.Error(this, ex.Message); }
        }

        void CloseMenu_Click(object sender, RoutedEventArgs e) => Close();

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (_sent || !_dirty) return;
            var r = VistaDialog.Show(this, "Do you want to save changes to this message?", "Valco Express", MessageBoxButton.YesNoCancel, DlgIcon.Question);
            if (r == MessageBoxResult.No) return;
            e.Cancel = true;
            if (r == MessageBoxResult.Yes)
            {
                Dispatcher.InvokeAsync(async () =>
                {
                    if (await SaveDraftAsync(false)) { _dirty = false; Close(); }
                });
            }
        }

        void OnKeys(object sender, KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (mods == ModifierKeys.Alt && key == Key.S) { e.Handled = true; _ = SendAsync(); }
            else if (mods == ModifierKeys.Control && key == Key.Enter) { e.Handled = true; _ = SendAsync(); }
            else if (mods == ModifierKeys.Control && key == Key.S) { e.Handled = true; _ = SaveDraftAsync(true); }
            else if (key == Key.Escape && mods == ModifierKeys.None) { e.Handled = true; Close(); }
        }

        // ================================================================ header fields
        void AllHeaders_Click(object sender, RoutedEventArgs e) => ShowBcc(AllHeadersMenu.IsChecked);

        void ShowBcc(bool show)
        {
            BccRow.Height = show ? GridLength.Auto : new GridLength(0);
            AllHeadersMenu.IsChecked = show;
            SyncRibbon();
        }

        void Pick(AddressBox box)
        {
            var picked = ContactsWindow.Pick(this);
            if (picked == null) return;
            foreach (var c in picked.Where(c => !string.IsNullOrWhiteSpace(c.Email))) box.Append(c.Display);
        }

        void PickTo_Click(object sender, RoutedEventArgs e) => Pick(ToBox);
        void PickCc_Click(object sender, RoutedEventArgs e) => Pick(CcBox);
        void PickBcc_Click(object sender, RoutedEventArgs e) { ShowBcc(true); Pick(BccBox); }

        void Priority_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && int.TryParse(mi.Tag?.ToString(), out var p)) _priority = p;
            UpdatePriorityUi();
        }

        void PriorityBtn_Click(object sender, RoutedEventArgs e)
        {
            var cm = new ContextMenu { PlacementTarget = PriorityBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var (label, val, icon) in new[] { ("_High Priority", 1, "IconPriorityHigh"), ("_Normal Priority", 3, (string)null), ("_Low Priority", 5, "IconPriorityLow") })
            {
                var mi = new MenuItem { Header = label, IsCheckable = true, IsChecked = _priority == val };
                if (icon != null) mi.Icon = UI.Icon(icon);
                var v = val;
                mi.Click += (s2, e2) => { _priority = v; UpdatePriorityUi(); };
                cm.Items.Add(mi);
            }
            cm.IsOpen = true;
        }

        void UpdatePriorityUi()
        {
            PriHighMenu.IsChecked = _priority == 1;
            PriNormalMenu.IsChecked = _priority == 3;
            PriLowMenu.IsChecked = _priority == 5;
            PriorityImg.Source = UI.Img(_priority == 5 ? "IconPriorityLow" : "IconPriorityHigh");
            PriorityMark.Source = PriorityImg.Source;
            PriorityMark.Visibility = _priority == 3 ? Visibility.Collapsed : Visibility.Visible;
            SyncRibbon();
        }

        void Spell_Click(object sender, RoutedEventArgs e) => SpellCheck.SetIsEnabled(Editor, SpellMenu.IsChecked);

        void SpellBtn_Click(object sender, RoutedEventArgs e)
        {
            SpellMenu.IsChecked = !SpellMenu.IsChecked;
            SpellCheck.SetIsEnabled(Editor, SpellMenu.IsChecked);
        }

        // ================================================================ formatting
        void Exec(RoutedUICommand cmd)
        {
            cmd.Execute(null, Editor);
            Editor.Focus();
            UpdateFormatState();
        }

        void Bold_Click(object sender, RoutedEventArgs e) => Exec(EditingCommands.ToggleBold);
        void Italic_Click(object sender, RoutedEventArgs e) => Exec(EditingCommands.ToggleItalic);
        void Underline_Click(object sender, RoutedEventArgs e) => Exec(EditingCommands.ToggleUnderline);
        void Bullets_Click(object sender, RoutedEventArgs e) => Exec(EditingCommands.ToggleBullets);
        void Numbers_Click(object sender, RoutedEventArgs e) => Exec(EditingCommands.ToggleNumbering);
        void Indent_Click(object sender, RoutedEventArgs e) => Exec(EditingCommands.IncreaseIndentation);
        void Outdent_Click(object sender, RoutedEventArgs e) => Exec(EditingCommands.DecreaseIndentation);
        void AlignLeft_Click(object sender, RoutedEventArgs e) => Exec(EditingCommands.AlignLeft);
        void AlignCenter_Click(object sender, RoutedEventArgs e) => Exec(EditingCommands.AlignCenter);
        void AlignRight_Click(object sender, RoutedEventArgs e) => Exec(EditingCommands.AlignRight);

        void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating || !(FontBox.SelectedItem is string name)) return;
            Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(name));
            Editor.Focus();
        }

        void SizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating || !(SizeBox.SelectedItem is double pt)) return;
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, PtToPx(pt));
            Editor.Focus();
        }

        void Color_Click(object sender, RoutedEventArgs e)
        {
            var cm = new ContextMenu { PlacementTarget = ColorBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var (name, color) in Palette)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new Border { Width = 14, Height = 14, Background = new SolidColorBrush(color), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 0) });
                sp.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
                var mi = new MenuItem { Header = sp };
                var c = color;
                mi.Click += (s2, e2) =>
                {
                    Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(c));
                    ColorSwatch.Fill = new SolidColorBrush(c);
                    Editor.Focus();
                };
                cm.Items.Add(mi);
            }
            cm.IsOpen = true;
        }

        void Link_Click(object sender, RoutedEventArgs e)
        {
            var url = InputDialog.Ask(this, "Hyperlink", "Type the address (URL) for the link:", "https://");
            if (string.IsNullOrWhiteSpace(url) || url == "https://") return;
            if (!url.Contains(":")) url = "https://" + url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) { VistaDialog.Error(this, "That doesn't look like a valid web address."); return; }
            var sel = Editor.Selection;
            if (sel.IsEmpty)
            {
                var run = new Run(url, sel.Start);
                var link = new Hyperlink(run.ContentStart, run.ContentEnd) { NavigateUri = uri };
                Editor.CaretPosition = link.ElementEnd;
            }
            else new Hyperlink(sel.Start, sel.End) { NavigateUri = uri };
            Editor.Focus();
        }

        void Rule_Click(object sender, RoutedEventArgs e)
        {
            var p = new Paragraph(new Run("────────────────────────────────────────")) { Foreground = Brushes.Gray };
            var cur = Editor.CaretPosition.Paragraph;
            if (cur != null) Editor.Document.Blocks.InsertAfter(cur, p);
            else Editor.Document.Blocks.Add(p);
            Editor.Focus();
        }

        void UpdateFormatState()
        {
            _updating = true;
            try
            {
                var sel = Editor.Selection;
                var fw = sel.GetPropertyValue(TextElement.FontWeightProperty);
                BoldBtn.IsChecked = fw is FontWeight w && w.ToOpenTypeWeight() >= 600;
                var fs = sel.GetPropertyValue(TextElement.FontStyleProperty);
                ItalicBtn.IsChecked = fs is FontStyle st && st != FontStyles.Normal;
                var td = sel.GetPropertyValue(Inline.TextDecorationsProperty);
                UnderlineBtn.IsChecked = td is TextDecorationCollection tdc && tdc.Count > 0;
                if (sel.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily ff) FontBox.SelectedItem = ff.Source;
                else FontBox.SelectedItem = null;
                if (sel.GetPropertyValue(TextElement.FontSizeProperty) is double px) SizeBox.SelectedItem = PxToPt(px);
                else SizeBox.SelectedItem = null;
            }
            finally { _updating = false; }
        }
    }
}
