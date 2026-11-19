using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using MimeKit;

namespace ValcoExpress
{
    public partial class MessageViewer : UserControl
    {
        static readonly string[] Unsafe =
        {
            ".exe", ".com", ".bat", ".cmd", ".scr", ".pif", ".msi", ".msp", ".js", ".jse", ".vbs", ".vbe", ".wsf", ".wsh",
            ".ps1", ".hta", ".lnk", ".reg", ".cpl", ".jar", ".dll", ".application", ".appref-ms", ".iso", ".img", ".vhd", ".vhdx"
        };

        public bool FullHeader { get; set; }
        public MimeMessage Message { get; private set; }
        public event Action<string> MailtoClicked;

        readonly string _dir;
        bool _webReady, _webFailed, _allowRemote;
        string _currentUri;
        int _n;

        public MessageViewer()
        {
            InitializeComponent();
            _dir = Path.Combine(Storage.LocalDir, "view", Guid.NewGuid().ToString("N"));
            Dispatcher.ShutdownStarted += (s, e) => Cleanup();
        }

        public void Cleanup()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        async Task<bool> EnsureWebAsync()
        {
            if (_webReady) return true;
            if (_webFailed) return false;
            try
            {
                var env = await App.WebEnvAsync();
                await Web.EnsureCoreWebView2Async(env);
                var c = Web.CoreWebView2;
                c.Settings.IsScriptEnabled = false;
                c.Settings.AreDefaultScriptDialogsEnabled = false;
                c.Settings.AreDevToolsEnabled = false;
                c.Settings.IsWebMessageEnabled = false;
                c.Settings.AreBrowserAcceleratorKeysEnabled = false;
                c.Settings.IsGeneralAutofillEnabled = false;
                c.Settings.IsPasswordAutosaveEnabled = false;
                c.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                c.WebResourceRequested += OnResource;
                c.NavigationStarting += OnNavigating;
                c.NewWindowRequested += (s, e) => { e.Handled = true; OpenLink(e.Uri); };
                _webReady = true;
                return true;
            }
            catch
            {
                _webFailed = true;
                return false;
            }
        }

        void OnNavigating(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (_currentUri != null && e.Uri.StartsWith(_currentUri, StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            OpenLink(e.Uri);
        }

        void OpenLink(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return;
            if (uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) { MailtoClicked?.Invoke(uri); return; }
            if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
            }
        }

        void OnResource(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            var u = e.Request.Uri ?? "";
            bool remote = u.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
                          u.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
                          u.StartsWith("ftp:", StringComparison.OrdinalIgnoreCase) ||
                          u.StartsWith("ws", StringComparison.OrdinalIgnoreCase);
            if (remote && !_allowRemote)
            {
                e.Response = Web.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
                InfoBar.Visibility = Visibility.Visible;
            }
        }

        void InfoBar_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (Message == null) return;
            _allowRemote = true;
            InfoBar.Visibility = Visibility.Collapsed;
            _ = RenderAsync();
        }

        public void Clear(string text = "There is no message selected.")
        {
            Message = null;
            HeaderBand.Visibility = Visibility.Collapsed;
            InfoBar.Visibility = Visibility.Collapsed;
            AttachBar.Visibility = Visibility.Collapsed;
            Web.Visibility = Visibility.Collapsed;
            Fallback.Visibility = Visibility.Collapsed;
            EmptyText.Text = text;
            EmptyText.Visibility = Visibility.Visible;
        }

        public async Task ShowAsync(MimeMessage msg)
        {
            Message = msg;
            _allowRemote = !Storage.Settings.BlockImages;
            InfoBar.Visibility = Visibility.Collapsed;
            BuildHeader(msg);
            BuildAttachments(msg);
            EmptyText.Visibility = Visibility.Collapsed;
            await RenderAsync();
        }

        async Task RenderAsync()
        {
            var msg = Message;
            if (msg == null) return;
            if (await EnsureWebAsync())
            {
                if (msg != Message) return;
                Directory.CreateDirectory(_dir);
                foreach (var old in Directory.GetFiles(_dir)) { try { File.Delete(old); } catch { } }
                _n++;
                var html = BuildHtml(msg);
                var file = Path.Combine(_dir, $"message{_n}.html");
                File.WriteAllText(file, html, new UTF8Encoding(true));
                _currentUri = new Uri(file).AbsoluteUri;
                Fallback.Visibility = Visibility.Collapsed;
                Web.Visibility = Visibility.Visible;
                Web.CoreWebView2.Navigate(_currentUri);
            }
            else
            {
                Web.Visibility = Visibility.Collapsed;
                Fallback.Text = MailUtil.PlainText(msg);
                Fallback.Visibility = Visibility.Visible;
            }
        }

        public void Print()
        {
            if (_webReady && Message != null) Web.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        }

        // ---------------------------------------------------------------- header
        void BuildHeader(MimeMessage m)
        {
            HeaderLines.Children.Clear();
            string from = FullHeader ? MailUtil.Full(m.From) : MailUtil.Names(m.From);
            if (FullHeader)
            {
                Line(("From:", from));
                Line(("Date:", m.Date.LocalDateTime.ToString("dddd, MMMM d, yyyy h:mm tt")));
                Line(("To:", MailUtil.Full(m.To)));
                if (m.Cc.Count > 0) Line(("Cc:", MailUtil.Full(m.Cc)));
                Line(("Subject:", m.Subject ?? ""));
            }
            else
            {
                Line(("From:", from), ("To:", MailUtil.Names(m.To)));
                Line(("Subject:", m.Subject ?? ""));
            }
            HeaderBand.Visibility = Visibility.Visible;
        }

        void Line(params (string Label, string Value)[] parts)
        {
            var tb = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 1) };
            bool first = true;
            foreach (var p in parts)
            {
                if (!first) tb.Inlines.Add(new Run("     "));
                tb.Inlines.Add(new Run(p.Label + " ") { FontWeight = FontWeights.Bold });
                tb.Inlines.Add(new Run(p.Value));
                first = false;
            }
            HeaderLines.Children.Add(tb);
        }

        // ---------------------------------------------------------------- attachments
        void BuildAttachments(MimeMessage m)
        {
            AttachPanel.Children.Clear();
            var atts = m.Attachments.ToList();
            if (atts.Count == 0) { AttachBar.Visibility = Visibility.Collapsed; return; }
            AttachPanel.Children.Add(new TextBlock { Text = "Attach:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0) });
            foreach (var a in atts)
            {
                var name = MailUtil.FileNameOf(a);
                long size = 0;
                if (a is MimePart mp && mp.Content != null)
                {
                    try { using (var ms = new MemoryStream()) { mp.Content.DecodeTo(ms); size = ms.Length; } } catch { }
                }
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(UI.Icon("IconAttach", 16));
                sp.Children.Add(new TextBlock
                {
                    Text = size > 0 ? $"{name} ({Math.Max(1, size / 1024):N0} KB)" : name,
                    Margin = new Thickness(3, 0, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x33, 0x99)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                var btn = new Button { Content = sp, Style = (Style)FindResource("FmtButton"), Margin = new Thickness(0, 0, 4, 0), ToolTip = name };
                var entity = a;
                btn.Click += (s, e) =>
                {
                    var cm = new ContextMenu();
                    var open = new MenuItem { Header = "_Open" };
                    open.Click += (s2, e2) => OpenAttachment(entity);
                    var save = new MenuItem { Header = "Save _As..." };
                    save.Click += (s2, e2) => SaveAttachment(entity);
                    var all = new MenuItem { Header = "Save A_ll Attachments..." };
                    all.Click += (s2, e2) => SaveAll(atts);
                    cm.Items.Add(open); cm.Items.Add(save); cm.Items.Add(new Separator()); cm.Items.Add(all);
                    cm.PlacementTarget = btn;
                    cm.IsOpen = true;
                };
                AttachPanel.Children.Add(btn);
            }
            AttachBar.Visibility = Visibility.Visible;
        }

        bool IsUnsafe(string name) => Unsafe.Contains(Path.GetExtension(name).ToLowerInvariant());

        void OpenAttachment(MimeEntity e)
        {
            var name = MailUtil.Safe(MailUtil.FileNameOf(e));
            if (Storage.Settings.BlockUnsafeAttachments && IsUnsafe(name))
            {
                VistaDialog.Show(Window.GetWindow(this),
                    $"\"{name}\" could harm your computer, so Valco Express won't open it directly. You can still save it and scan it first.\n\nYou can change this under Tools > Options > Security.",
                    null, MessageBoxButton.OK, DlgIcon.Warning, "This attachment was blocked");
                return;
            }
            try
            {
                var dir = Path.Combine(Storage.LocalDir, "attachments", Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, name);
                MailUtil.SaveEntity(e, path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) { VistaDialog.Error(Window.GetWindow(this), ex.Message, "The attachment could not be opened."); }
        }

        void SaveAttachment(MimeEntity e)
        {
            var name = MailUtil.Safe(MailUtil.FileNameOf(e));
            var dlg = new SaveFileDialog { FileName = name, Title = "Save Attachment As", Filter = "All Files (*.*)|*.*" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try { MailUtil.SaveEntity(e, dlg.FileName); }
            catch (Exception ex) { VistaDialog.Error(Window.GetWindow(this), ex.Message, "The attachment could not be saved."); }
        }

        void SaveAll(System.Collections.Generic.List<MimeEntity> atts)
        {
            var dlg = new OpenFolderDialog { Title = "Save Attachments To" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                foreach (var a in atts)
                {
                    var name = MailUtil.Safe(MailUtil.FileNameOf(a));
                    var path = Path.Combine(dlg.FolderName, name);
                    int i = 1;
                    while (File.Exists(path)) path = Path.Combine(dlg.FolderName, $"{Path.GetFileNameWithoutExtension(name)} ({i++}){Path.GetExtension(name)}");
                    MailUtil.SaveEntity(a, path);
                }
            }
            catch (Exception ex) { VistaDialog.Error(Window.GetWindow(this), ex.Message, "The attachments could not be saved."); }
        }

        // ---------------------------------------------------------------- html
        string BuildHtml(MimeMessage msg)
        {
            string body;
            if (msg.HtmlBody != null) body = ReplaceCids(msg, msg.HtmlBody);
            else
            {
                var conv = new MimeKit.Text.TextToHtml { OutputHtmlFragment = true };
                body = "<div class=\"ve-plain\">" + conv.Convert(msg.TextBody ?? "") + "</div>";
            }
            string Enc(string s) => WebUtility.HtmlEncode(s ?? "");
            var head = new StringBuilder();
            head.Append("<meta charset=\"utf-8\">");
            head.Append("<style>html,body{background:#fff}body{font-family:Calibri,'Segoe UI',Arial,sans-serif;font-size:11pt;margin:10px 12px;color:#000;word-wrap:break-word}");
            head.Append(".ve-plain{white-space:normal}blockquote{margin:0 0 0 5px;padding-left:5px;border-left:2px solid #000}");
            head.Append(".ve-print{display:none}@media print{.ve-print{display:block;font-family:Arial;font-size:10pt;border-bottom:2px solid #000;margin-bottom:12px;padding-bottom:6px}}</style>");
            var print = new StringBuilder("<div class=\"ve-print\">");
            print.Append($"<b>From:</b> {Enc(MailUtil.Full(msg.From))}<br><b>Sent:</b> {Enc(msg.Date.LocalDateTime.ToString("f"))}<br><b>To:</b> {Enc(MailUtil.Full(msg.To))}<br>");
            if (msg.Cc.Count > 0) print.Append($"<b>Cc:</b> {Enc(MailUtil.Full(msg.Cc))}<br>");
            print.Append($"<b>Subject:</b> {Enc(msg.Subject)}</div>");

            var headRx = new Regex("<head[^>]*>", RegexOptions.IgnoreCase);
            var bodyRx = new Regex("<body[^>]*>", RegexOptions.IgnoreCase);
            if (headRx.IsMatch(body)) body = headRx.Replace(body, mm => mm.Value + head, 1);
            else body = head + body;
            if (bodyRx.IsMatch(body)) body = bodyRx.Replace(body, mm => mm.Value + print, 1);
            else body = print + body;
            return body;
        }

        string ReplaceCids(MimeMessage msg, string html)
        {
            int i = 0;
            return Regex.Replace(html, @"cid:([^""'\s>)]+)", m =>
            {
                var id = Uri.UnescapeDataString(m.Groups[1].Value).Trim('<', '>');
                var part = msg.BodyParts.OfType<MimePart>().FirstOrDefault(p => p.ContentId != null && p.ContentId.Trim('<', '>') == id);
                if (part == null) return m.Value;
                try
                {
                    Directory.CreateDirectory(_dir);
                    var ext = Path.GetExtension(part.FileName ?? "");
                    if (string.IsNullOrEmpty(ext)) ext = "." + part.ContentType.MediaSubtype;
                    var name = $"cid{_n + 1}_{++i}{ext}";
                    using (var fs = File.Create(Path.Combine(_dir, name))) part.Content.DecodeTo(fs);
                    return name;
                }
                catch { return m.Value; }
            }, RegexOptions.IgnoreCase);
        }
    }
}
