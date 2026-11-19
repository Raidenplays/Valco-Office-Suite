using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace ValcoExpress
{
    public abstract class Notify : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            Raise(name);
        }
        protected void Raise([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public enum FolderKind { Root, Inbox, Outbox, Sent, Deleted, Drafts, Junk, Normal }

    public class FolderNode : Notify
    {
        public IMailStore Store { get; set; }
        public string Id { get; set; }
        public FolderNode Parent { get; set; }
        public FolderKind Kind { get; set; }
        public bool Selectable { get; set; } = true;
        public ObservableCollection<FolderNode> Children { get; } = new ObservableCollection<FolderNode>();

        string _name;
        public string Name { get => _name; set => Set(ref _name, value); }

        int _unread;
        public int Unread
        {
            get => _unread;
            set { Set(ref _unread, value); Raise(nameof(HasUnread)); Raise(nameof(UnreadText)); }
        }
        public int Total { get; set; }
        public bool HasUnread => Unread > 0 && Kind != FolderKind.Sent && Kind != FolderKind.Drafts;
        public string UnreadText => HasUnread ? $"({Unread})" : (Kind == FolderKind.Outbox && Total > 0 ? $"[{Total}]" : "");

        bool _expanded = true;
        public bool IsExpanded { get => _expanded; set => Set(ref _expanded, value); }
        bool _selected;
        public bool IsSelected { get => _selected; set => Set(ref _selected, value); }

        string _status;
        public string Status { get => _status; set { Set(ref _status, value); Raise(nameof(HasStatus)); } }
        public bool HasStatus => !string.IsNullOrEmpty(Status);

        public string IconKey => Kind switch
        {
            FolderKind.Root => Store is LocalStore ? "IconLocalFolders" : "IconAccount",
            FolderKind.Inbox => "IconInbox",
            FolderKind.Outbox => "IconOutbox",
            FolderKind.Sent => "IconSent",
            FolderKind.Deleted => "IconDeleted",
            FolderKind.Drafts => "IconDrafts",
            FolderKind.Junk => "IconJunk",
            _ => "IconFolder"
        };

        public bool ShowsRecipients => Kind == FolderKind.Sent || Kind == FolderKind.Drafts || Kind == FolderKind.Outbox;

        public string Path
        {
            get
            {
                var parts = new List<string>();
                for (var n = this; n != null; n = n.Parent) parts.Insert(0, n.Name);
                return string.Join(" › ", parts);
            }
        }

        public IEnumerable<FolderNode> Descendants()
        {
            foreach (var c in Children)
            {
                yield return c;
                foreach (var d in c.Descendants()) yield return d;
            }
        }
    }

    public class MailItem : Notify
    {
        public IMailStore Store { get; set; }
        public FolderNode Folder { get; set; }
        public string Key { get; set; }
        public uint Uid { get; set; }
        public string FromName { get; set; }
        public string FromAddress { get; set; }
        public string To { get; set; }
        public string Subject { get; set; }
        public DateTimeOffset Date { get; set; }
        public long Size { get; set; }
        public int Priority { get; set; } = 3;
        public bool HasAttachments { get; set; }

        bool _read;
        public bool IsRead { get => _read; set { Set(ref _read, value); Raise(nameof(EnvelopeIcon)); } }
        bool _flagged;
        public bool IsFlagged { get => _flagged; set => Set(ref _flagged, value); }

        public string FromDisplay => string.IsNullOrWhiteSpace(FromName) ? FromAddress : FromName;
        public string Who => Folder != null && Folder.ShowsRecipients ? To : FromDisplay;
        public string EnvelopeIcon => IsRead ? "IconEnvOpen" : "IconEnvClosed";
        public string DateDisplay
        {
            get
            {
                var d = Date.LocalDateTime;
                return d.ToString("d") + " " + d.ToString("t");
            }
        }
        public string SizeDisplay => Size <= 0 ? "" : Math.Max(1, (Size + 1023) / 1024).ToString("N0") + " KB";
        public bool IsHighPriority => Priority < 3;
        public bool IsLowPriority => Priority > 3;
    }

    public enum ConnSecurity { SslOnConnect, StartTls, None }

    public class Account
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string AccountName { get; set; }
        public string DisplayName { get; set; }
        public string Email { get; set; }
        public string ImapHost { get; set; }
        public int ImapPort { get; set; } = 993;
        public ConnSecurity ImapSecurity { get; set; } = ConnSecurity.SslOnConnect;
        public string SmtpHost { get; set; }
        public int SmtpPort { get; set; } = 587;
        public ConnSecurity SmtpSecurity { get; set; } = ConnSecurity.StartTls;
        public bool SmtpRequiresAuth { get; set; } = true;
        public string Username { get; set; }
        public string PasswordProtected { get; set; }
        public bool SaveSentCopy { get; set; } = true;
        public bool IncludeInSendReceive { get; set; } = true;
        public bool IsDefault { get; set; }
        public uint LastInboxUid { get; set; }
        public uint LastUidValidity { get; set; }

        [JsonIgnore]
        public string Password
        {
            get => Storage.Unprotect(PasswordProtected);
            set => PasswordProtected = Storage.Protect(value);
        }
        [JsonIgnore]
        public string Title => string.IsNullOrWhiteSpace(AccountName) ? (ImapHost ?? Email) : AccountName;
        public override string ToString() =>
            $"{(string.IsNullOrWhiteSpace(DisplayName) ? Email : DisplayName)} <{Email}>  ({Title})";
    }

    public class Contact
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Notes { get; set; }
        [JsonIgnore] public string Source { get; set; } = "Valco Express";
        [JsonIgnore] public string FilePath { get; set; }
        [JsonIgnore] public bool ReadOnly => FilePath != null;
        [JsonIgnore] public string Display => string.IsNullOrWhiteSpace(Name) ? Email : $"{Name} <{Email}>";
    }

    public class AppSettings
    {
        public int CheckMinutes { get; set; } = 5;
        public bool CloseToTray { get; set; } = true;
        public bool ShowNotifications { get; set; } = true;
        public bool StartWithWindows { get; set; } = true;
        public bool TrayHintShown { get; set; }
        public bool PlaySound { get; set; } = true;
        public bool SendReceiveAtStartup { get; set; } = true;
        public bool EmptyDeletedOnExit { get; set; }
        public int MarkReadSeconds { get; set; } = 5;
        public bool MarkReadEnabled { get; set; } = true;
        public int MaxMessages { get; set; } = 500;
        public bool BlockImages { get; set; } = true;
        public bool BlockUnsafeAttachments { get; set; } = true;
        public bool ShowPreview { get; set; } = true;
        public bool ShowFolders { get; set; } = true;
        public bool ShowToolbar { get; set; } = true;
        public bool ShowStatusBar { get; set; } = true;
        public bool HideRead { get; set; }
        public double FolderPaneWidth { get; set; } = 210;
        public double PreviewHeight { get; set; } = 320;
        public double WinLeft { get; set; } = double.NaN;
        public double WinTop { get; set; } = double.NaN;
        public double WinWidth { get; set; } = 1040;
        public double WinHeight { get; set; } = 720;
        public bool Maximized { get; set; }
        public string ComposeFont { get; set; } = "Calibri";
        public double ComposeSize { get; set; } = 11;
        public string ColorScheme { get; set; } = "Blue";
        public string QuickAccess { get; set; } = "SendReceive,NewMail,Delete";
        public string ComposeQuickAccess { get; set; } = "Save,Undo,Redo";
        public bool RibbonMinimized { get; set; }
        public string SortColumn { get; set; } = "Date";
        public bool SortDescending { get; set; } = true;
    }

    public static class Storage
    {
        public static string RoamingDir { get; private set; }
        public static string LocalDir { get; private set; }
        public static AppSettings Settings { get; private set; }
        public static List<Account> Accounts { get; private set; }
        public static List<Contact> Contacts { get; private set; }

        static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { WriteIndented = true };
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ValcoExpress.v1.secret");

        public static void Init()
        {
            RoamingDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Valco Express");
            LocalDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Valco Express");
            Directory.CreateDirectory(RoamingDir);
            Directory.CreateDirectory(LocalDir);
            Settings = Load<AppSettings>("settings.json") ?? new AppSettings();
            Accounts = Load<List<Account>>("accounts.json") ?? new List<Account>();
            Contacts = Load<List<Contact>>("contacts.json") ?? new List<Contact>();
        }

        static T Load<T>(string name) where T : class
        {
            try
            {
                var p = System.IO.Path.Combine(RoamingDir, name);
                if (!File.Exists(p)) return null;
                return JsonSerializer.Deserialize<T>(File.ReadAllText(p), Opt);
            }
            catch { return null; }
        }

        static void Save(string name, object o)
        {
            try
            {
                var p = System.IO.Path.Combine(RoamingDir, name);
                File.WriteAllText(p + ".tmp", JsonSerializer.Serialize(o, o.GetType(), Opt));
                File.Move(p + ".tmp", p, true);
            }
            catch { }
        }

        public static void SaveSettings() => Save("settings.json", Settings);
        public static void SaveAccounts() => Save("accounts.json", Accounts);
        public static void SaveContacts() => Save("contacts.json", Contacts);

        public static string Protect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), Entropy, DataProtectionScope.CurrentUser));
        }

        public static string Unprotect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), Entropy, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
    }

    public static class AddressBook
    {
        static List<Contact> _windows;

        public static void Invalidate() => _windows = null;

        public static List<Contact> WindowsContacts()
        {
            if (_windows != null) return _windows;
            var list = new List<Contact>();
            try
            {
                var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Contacts");
                if (Directory.Exists(dir))
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*.contact", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var x = XDocument.Load(f);
                            var name = x.Descendants().FirstOrDefault(e => e.Name.LocalName == "FormattedName")?.Value;
                            if (string.IsNullOrWhiteSpace(name)) name = System.IO.Path.GetFileNameWithoutExtension(f);
                            var emails = x.Descendants().Where(e => e.Name.LocalName == "EmailAddress")
                                .Select(e => e.Elements().FirstOrDefault(c => c.Name.LocalName == "Address")?.Value)
                                .Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList();
                            if (emails.Count == 0)
                                list.Add(new Contact { Name = name, Email = "", Source = "Windows Contacts", FilePath = f });
                            foreach (var em in emails)
                                list.Add(new Contact { Name = name, Email = em.Trim(), Source = "Windows Contacts", FilePath = f });
                        }
                        catch { }
                    }
                }
            }
            catch { }
            _windows = list;
            return list;
        }

        public static IEnumerable<Contact> All() =>
            Storage.Contacts.Concat(WindowsContacts()).OrderBy(c => string.IsNullOrWhiteSpace(c.Name) ? c.Email : c.Name, StringComparer.CurrentCultureIgnoreCase);

        public static IEnumerable<Contact> Search(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Enumerable.Empty<Contact>();
            text = text.Trim();
            return All().Where(c => !string.IsNullOrWhiteSpace(c.Email) &&
                ((c.Name ?? "").IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                 c.Email.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        public static void Add(string name, string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return;
            if (All().Any(c => string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase))) return;
            Storage.Contacts.Add(new Contact { Name = name, Email = email });
            Storage.SaveContacts();
        }
    }
}
