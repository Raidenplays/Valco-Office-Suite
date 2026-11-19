using System;
using System.Windows;
using Microsoft.Win32;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace ValcoExpress
{
    /// <summary>Notification-area icon, its menu, and new-mail notifications.</summary>
    public sealed class TrayService : IDisposable
    {
        readonly WF.NotifyIcon _icon;
        readonly MainWindow _main;
        Action _onClick;

        public TrayService(MainWindow main)
        {
            _main = main;
            var menu = new WF.ContextMenuStrip();
            var open = new WF.ToolStripMenuItem("Open Valco Express", null, (s, e) => _main.ShowFromTray());
            open.Font = new SD.Font(open.Font, SD.FontStyle.Bold);
            menu.Items.Add(open);
            menu.Items.Add(new WF.ToolStripMenuItem("Create Mail", null, (s, e) => ComposeWindow.New().Show()));
            menu.Items.Add(new WF.ToolStripMenuItem("Send and Receive", null, async (s, e) => await _main.SendReceiveAsync(false)));
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(new WF.ToolStripMenuItem("Exit", null, (s, e) => _main.ExitApp()));

            _icon = new WF.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "Valco Express",
                ContextMenuStrip = menu,
                Visible = true
            };
            _icon.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) _main.ShowFromTray(); };
            _icon.BalloonTipClicked += (s, e) =>
            {
                var a = _onClick;
                _onClick = null;
                (a ?? _main.ShowFromTray)();
            };
            _icon.BalloonTipClosed += (s, e) => _onClick = null;
        }

        static SD.Icon LoadIcon()
        {
            try
            {
                var s = Application.GetResourceStream(new Uri("pack://application:,,,/ValcoExpress.ico")).Stream;
                return new SD.Icon(s, WF.SystemInformation.SmallIconSize);
            }
            catch { return SD.SystemIcons.Application; }
        }

        public void Notify(string title, string text, Action onClick)
        {
            _onClick = onClick;
            if (title.Length > 63) title = title.Substring(0, 62) + "…";
            if (text.Length > 250) text = text.Substring(0, 249) + "…";
            _icon.ShowBalloonTip(10000, title, string.IsNullOrWhiteSpace(text) ? " " : text, WF.ToolTipIcon.None);
        }

        public void SetUnread(int unread)
        {
            var t = unread > 0 ? $"Valco Express - {unread} unread message(s)" : "Valco Express";
            _icon.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }

    public static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Valco Express";

        public static void Apply(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    var exe = Environment.ProcessPath;
                    if (enabled && !string.IsNullOrEmpty(exe)) key.SetValue(Name, $"\"{exe}\" --background");
                    else if (key.GetValue(Name) != null) key.DeleteValue(Name, false);
                }
            }
            catch { }
        }
    }
}
