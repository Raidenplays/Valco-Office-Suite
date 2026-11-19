using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace ValcoExpress
{
    public interface IMailStore
    {
        string DisplayName { get; }
        Account Account { get; }
        FolderNode Root { get; }
        Task<List<FolderNode>> LoadChildrenAsync();
        Task RefreshCountsAsync();
        Task<List<MailItem>> ListAsync(FolderNode folder);
        Task<MimeMessage> GetMessageAsync(MailItem item);
        Task SetReadAsync(IList<MailItem> items, bool read);
        Task SetFlaggedAsync(IList<MailItem> items, bool flagged);
        Task MoveAsync(IList<MailItem> items, FolderNode dest);
        Task DeleteAsync(IList<MailItem> items);
        Task DeletePermanentAsync(IList<MailItem> items);
        Task<string> AppendAsync(FolderNode folder, MimeMessage message, bool read);
        Task EmptyFolderAsync(FolderNode folder);
        Task CreateFolderAsync(FolderNode parent, string name);
        Task DeleteFolderAsync(FolderNode folder);
        Task DisconnectAsync();
    }

    public static class StoreExtensions
    {
        public static FolderNode Find(this IMailStore s, FolderKind kind) =>
            s.Root?.Descendants().FirstOrDefault(f => f.Kind == kind);
    }

    // ===================================================================
    //  Local Folders  —  each folder is a directory of .eml files
    // ===================================================================
    public class LocalStore : IMailStore
    {
        class Flags { public bool Read { get; set; } public bool Flagged { get; set; } }

        readonly string _root;
        readonly object _lock = new object();
        public string DisplayName => "Local Folders";
        public Account Account => null;
        public FolderNode Root { get; }

        static readonly (string Name, FolderKind Kind)[] Std =
        {
            ("Inbox", FolderKind.Inbox), ("Outbox", FolderKind.Outbox), ("Sent Items", FolderKind.Sent),
            ("Deleted Items", FolderKind.Deleted), ("Drafts", FolderKind.Drafts), ("Junk E-mail", FolderKind.Junk)
        };

        public LocalStore()
        {
            _root = Path.Combine(Storage.RoamingDir, "Local Folders");
            Root = new FolderNode { Store = this, Id = "", Name = DisplayName, Kind = FolderKind.Root, Selectable = false };
        }

        string Dir(FolderNode f) => Path.Combine(_root, f.Id);

        public Task<List<FolderNode>> LoadChildrenAsync() => Task.Run(() =>
        {
            bool first = !Directory.Exists(_root);
            Directory.CreateDirectory(_root);
            foreach (var s in Std) Directory.CreateDirectory(Path.Combine(_root, s.Name));
            if (first) Welcome.Seed(Path.Combine(_root, "Inbox"));

            var list = new List<FolderNode>();
            foreach (var s in Std)
            {
                var n = new FolderNode { Store = this, Id = s.Name, Name = s.Name, Kind = s.Kind, Parent = Root };
                AddSubfolders(n);
                list.Add(n);
            }
            foreach (var d in Directory.GetDirectories(_root).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
            {
                var name = Path.GetFileName(d);
                if (Std.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                var n = new FolderNode { Store = this, Id = name, Name = name, Kind = FolderKind.Normal, Parent = Root };
                AddSubfolders(n);
                list.Add(n);
            }
            foreach (var n in list.SelectMany(x => new[] { x }.Concat(x.Descendants()))) Count(n);
            return list;
        });

        void AddSubfolders(FolderNode parent)
        {
            foreach (var d in Directory.GetDirectories(Dir(parent)).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
            {
                var name = Path.GetFileName(d);
                var n = new FolderNode { Store = this, Id = Path.Combine(parent.Id, name), Name = name, Kind = FolderKind.Normal, Parent = parent };
                AddSubfolders(n);
                parent.Children.Add(n);
            }
        }

        void Count(FolderNode f)
        {
            var files = Directory.GetFiles(Dir(f), "*.eml");
            var idx = LoadIndex(f);
            f.Total = files.Length;
            f.Unread = files.Count(p => !(idx.TryGetValue(Path.GetFileName(p), out var fl) && fl.Read));
        }

        public Task RefreshCountsAsync() => Task.Run(() =>
        {
            foreach (var n in Root.Descendants().ToList())
            {
                try { Count(n); } catch { }
            }
        });

        Dictionary<string, Flags> LoadIndex(FolderNode f)
        {
            lock (_lock)
            {
                try
                {
                    var p = Path.Combine(Dir(f), ".index.json");
                    if (File.Exists(p))
                        return JsonSerializer.Deserialize<Dictionary<string, Flags>>(File.ReadAllText(p)) ?? new Dictionary<string, Flags>();
                }
                catch { }
                return new Dictionary<string, Flags>();
            }
        }

        void SaveIndex(FolderNode f, Dictionary<string, Flags> idx)
        {
            lock (_lock)
            {
                try { File.WriteAllText(Path.Combine(Dir(f), ".index.json"), JsonSerializer.Serialize(idx)); } catch { }
            }
        }

        public Task<List<MailItem>> ListAsync(FolderNode folder) => Task.Run(() =>
        {
            var idx = LoadIndex(folder);
            var list = new List<MailItem>();
            foreach (var p in Directory.GetFiles(Dir(folder), "*.eml"))
            {
                try
                {
                    var msg = MimeMessage.Load(p);
                    var name = Path.GetFileName(p);
                    idx.TryGetValue(name, out var fl);
                    list.Add(Summarize(msg, folder, name, fl?.Read ?? false, fl?.Flagged ?? false, new FileInfo(p).Length));
                }
                catch { }
            }
            return list;
        });

        MailItem Summarize(MimeMessage msg, FolderNode folder, string key, bool read, bool flagged, long size)
        {
            var from = msg.From.Mailboxes.FirstOrDefault();
            return new MailItem
            {
                Store = this, Folder = folder, Key = key,
                FromName = from?.Name, FromAddress = from?.Address ?? msg.From.ToString(),
                To = MailUtil.Names(msg.To),
                Subject = msg.Subject ?? "",
                Date = msg.Date == DateTimeOffset.MinValue ? DateTimeOffset.Now : msg.Date,
                Size = size, IsRead = read, IsFlagged = flagged,
                Priority = MailUtil.PriorityOf(msg),
                HasAttachments = msg.Attachments.Any()
            };
        }

        public Task<MimeMessage> GetMessageAsync(MailItem item) =>
            Task.Run(() => MimeMessage.Load(Path.Combine(Dir(item.Folder), item.Key)));

        void UpdateFlags(IList<MailItem> items, Action<Flags> change)
        {
            foreach (var g in items.GroupBy(i => i.Folder))
            {
                var idx = LoadIndex(g.Key);
                foreach (var it in g)
                {
                    if (!idx.TryGetValue(it.Key, out var fl)) idx[it.Key] = fl = new Flags();
                    change(fl);
                }
                SaveIndex(g.Key, idx);
            }
        }

        public Task SetReadAsync(IList<MailItem> items, bool read) => Task.Run(() => UpdateFlags(items, f => f.Read = read));
        public Task SetFlaggedAsync(IList<MailItem> items, bool flagged) => Task.Run(() => UpdateFlags(items, f => f.Flagged = flagged));

        public Task MoveAsync(IList<MailItem> items, FolderNode dest) => Task.Run(() =>
        {
            var destIdx = LoadIndex(dest);
            foreach (var g in items.GroupBy(i => i.Folder))
            {
                if (g.Key == dest) continue;
                var idx = LoadIndex(g.Key);
                foreach (var it in g)
                {
                    var src = Path.Combine(Dir(g.Key), it.Key);
                    var name = NewName();
                    File.Move(src, Path.Combine(Dir(dest), name));
                    idx.Remove(it.Key, out var fl);
                    destIdx[name] = fl ?? new Flags { Read = it.IsRead, Flagged = it.IsFlagged };
                }
                SaveIndex(g.Key, idx);
            }
            SaveIndex(dest, destIdx);
        });

        public async Task DeleteAsync(IList<MailItem> items)
        {
            var trash = this.Find(FolderKind.Deleted);
            var toTrash = items.Where(i => i.Folder != trash).ToList();
            var perm = items.Where(i => i.Folder == trash).ToList();
            if (toTrash.Count > 0) await MoveAsync(toTrash, trash);
            if (perm.Count > 0) await DeletePermanentAsync(perm);
        }

        public Task DeletePermanentAsync(IList<MailItem> items) => Task.Run(() =>
        {
            foreach (var g in items.GroupBy(i => i.Folder))
            {
                var idx = LoadIndex(g.Key);
                foreach (var it in g)
                {
                    try { File.Delete(Path.Combine(Dir(g.Key), it.Key)); } catch { }
                    idx.Remove(it.Key);
                }
                SaveIndex(g.Key, idx);
            }
        });

        static string NewName() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".eml";

        public Task<string> AppendAsync(FolderNode folder, MimeMessage message, bool read) => Task.Run(() =>
        {
            var name = NewName();
            using (var fs = File.Create(Path.Combine(Dir(folder), name))) message.WriteTo(fs);
            var idx = LoadIndex(folder);
            idx[name] = new Flags { Read = read };
            SaveIndex(folder, idx);
            return name;
        });

        public Task EmptyFolderAsync(FolderNode folder) => Task.Run(() =>
        {
            foreach (var p in Directory.GetFiles(Dir(folder), "*.eml")) { try { File.Delete(p); } catch { } }
            SaveIndex(folder, new Dictionary<string, Flags>());
        });

        public Task CreateFolderAsync(FolderNode parent, string name) => Task.Run(() =>
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            Directory.CreateDirectory(Path.Combine(_root, parent.Kind == FolderKind.Root ? "" : parent.Id, name));
        });

        public Task DeleteFolderAsync(FolderNode folder) => Task.Run(() => Directory.Delete(Dir(folder), true));
        public Task DisconnectAsync() => Task.CompletedTask;
    }

    // ===================================================================
    //  IMAP account
    // ===================================================================
    public class ImapStore : IMailStore
    {
        readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        readonly Dictionary<string, IMailFolder> _folders = new Dictionary<string, IMailFolder>();
        readonly LinkedList<(string Key, MimeMessage Msg)> _cache = new LinkedList<(string, MimeMessage)>();
        ImapClient _client;

        public Account Account { get; }
        public string DisplayName => Account.Title;
        public FolderNode Root { get; }

        public ImapStore(Account acct)
        {
            Account = acct;
            Root = new FolderNode { Store = this, Id = "", Name = acct.Title, Kind = FolderKind.Root, Selectable = false };
        }

        public static SecureSocketOptions Map(ConnSecurity s) => s switch
        {
            ConnSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            ConnSecurity.StartTls => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None
        };

        async Task<ImapClient> ClientAsync()
        {
            if (_client != null && _client.IsConnected && _client.IsAuthenticated) return _client;
            try { _client?.Dispose(); } catch { }
            _client = null;
            var c = new ImapClient { Timeout = 45000 };
            await c.ConnectAsync(Account.ImapHost, Account.ImapPort, Map(Account.ImapSecurity));
            await c.AuthenticateAsync(Account.Username ?? Account.Email, Account.Password);
            _client = c;
            foreach (var f in _folders.Values.ToList())
            {
                try { _folders[f.FullName] = c.GetFolder(f.FullName); } catch { }
            }
            return c;
        }

        async Task<T> Run<T>(Func<ImapClient, Task<T>> fn)
        {
            await _gate.WaitAsync();
            try
            {
                try { return await fn(await ClientAsync()); }
                catch (Exception ex) when (ex is IOException || ex is ServiceNotConnectedException ||
                                           ex is ImapProtocolException || ex is System.Net.Sockets.SocketException)
                {
                    try { _client?.Dispose(); } catch { }
                    _client = null;
                    return await fn(await ClientAsync());
                }
            }
            finally { _gate.Release(); }
        }

        Task Run(Func<ImapClient, Task> fn) => Run<bool>(async c => { await fn(c); return true; });

        IMailFolder F(FolderNode n) => _folders.TryGetValue(n.Id, out var f) ? f : throw new InvalidOperationException("Folder not found: " + n.Name);

        static async Task<IMailFolder> OpenAsync(IMailFolder f)
        {
            if (!f.IsOpen || f.Access != FolderAccess.ReadWrite) await f.OpenAsync(FolderAccess.ReadWrite);
            return f;
        }

        static FolderKind KindOf(IMailFolder f, IMailFolder inbox)
        {
            if (inbox != null && f.FullName == inbox.FullName) return FolderKind.Inbox;
            var a = f.Attributes;
            if (a.HasFlag(FolderAttributes.Sent)) return FolderKind.Sent;
            if (a.HasFlag(FolderAttributes.Trash)) return FolderKind.Deleted;
            if (a.HasFlag(FolderAttributes.Drafts)) return FolderKind.Drafts;
            if (a.HasFlag(FolderAttributes.Junk)) return FolderKind.Junk;
            var n = f.Name.ToLowerInvariant();
            if (n == "sent" || n == "sent items" || n == "sent mail" || n == "sent messages") return FolderKind.Sent;
            if (n == "trash" || n == "deleted" || n == "deleted items" || n == "deleted messages" || n == "bin") return FolderKind.Deleted;
            if (n == "drafts" || n == "draft") return FolderKind.Drafts;
            if (n == "junk" || n == "spam" || n == "junk e-mail" || n == "junk email" || n == "bulk mail") return FolderKind.Junk;
            return FolderKind.Normal;
        }

        public Task<List<FolderNode>> LoadChildrenAsync() => Run(async c =>
        {
            var ns = c.PersonalNamespaces.Count > 0 ? c.PersonalNamespaces[0] : new FolderNamespace('/', "");
            var all = (await c.GetFoldersAsync(ns, false)).ToList();
            var inbox = c.Inbox;
            if (!all.Any(f => f.FullName == inbox.FullName)) all.Insert(0, inbox);
            _folders.Clear();

            var nodes = new Dictionary<string, FolderNode>();
            var top = new List<FolderNode>();
            var taken = new HashSet<FolderKind>();
            foreach (var f in all.OrderBy(f => f.FullName.Count(ch => ch == f.DirectorySeparator)).ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase))
            {
                if (f.Attributes.HasFlag(FolderAttributes.NonExistent)) continue;
                _folders[f.FullName] = f;
                var kind = KindOf(f, inbox);
                if (kind != FolderKind.Normal && kind != FolderKind.Inbox)
                {
                    if (taken.Contains(kind)) kind = FolderKind.Normal; else taken.Add(kind);
                }
                var node = new FolderNode
                {
                    Store = this, Id = f.FullName, Kind = kind,
                    Name = kind == FolderKind.Inbox ? "Inbox" : f.Name,
                    Selectable = !f.Attributes.HasFlag(FolderAttributes.NoSelect),
                    IsExpanded = true
                };
                nodes[f.FullName] = node;
                var parentName = f.ParentFolder?.FullName;
                if (!string.IsNullOrEmpty(parentName) && nodes.TryGetValue(parentName, out var p))
                {
                    node.Parent = p;
                    p.Children.Add(node);
                }
                else
                {
                    node.Parent = Root;
                    top.Add(node);
                }
            }
            int Rank(FolderNode n) => n.Kind switch
            {
                FolderKind.Inbox => 0, FolderKind.Drafts => 1, FolderKind.Sent => 2,
                FolderKind.Deleted => 3, FolderKind.Junk => 4, _ => 5
            };
            return top.OrderBy(Rank).ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        });

        /// <summary>Returns unread Inbox messages that arrived since the last check (persisted per account).</summary>
        public Task<List<MailItem>> CheckNewAsync() => Run(async c =>
        {
            var result = new List<MailItem>();
            var node = this.Find(FolderKind.Inbox);
            if (node == null || !_folders.ContainsKey(node.Id)) return result;
            var f = await OpenAsync(F(node));
            uint highest = 0;
            if (f.Count > 0)
            {
                var last = await f.FetchAsync(f.Count - 1, f.Count - 1, new FetchRequest(MessageSummaryItems.UniqueId));
                highest = last.Count > 0 ? last[0].UniqueId.Id : 0;
            }
            if (Account.LastUidValidity != f.UidValidity || Account.LastInboxUid == 0)
            {
                // First check for this account (or the server reset its UIDs): set a baseline, don't notify.
                Account.LastUidValidity = f.UidValidity;
                Account.LastInboxUid = highest;
                Storage.SaveAccounts();
                return result;
            }
            if (highest <= Account.LastInboxUid) return result;
            var range = new UniqueIdRange(new UniqueId(f.UidValidity, Account.LastInboxUid + 1), UniqueId.MaxValue);
            var sums = await f.FetchAsync(range, new FetchRequest(MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags));
            foreach (var s in sums.Where(x => x.UniqueId.Id > Account.LastInboxUid).OrderBy(x => x.UniqueId.Id))
            {
                bool seen = s.Flags.HasValue && s.Flags.Value.HasFlag(MessageFlags.Seen);
                if (seen) continue;
                var from = s.Envelope?.From?.Mailboxes.FirstOrDefault();
                result.Add(new MailItem
                {
                    Store = this, Folder = node, Uid = s.UniqueId.Id, Key = s.UniqueId.Id.ToString(),
                    FromName = from?.Name, FromAddress = from?.Address ?? "",
                    Subject = s.Envelope?.Subject ?? "", Date = s.Envelope?.Date ?? DateTimeOffset.Now
                });
            }
            Account.LastInboxUid = Math.Max(highest, sums.Count > 0 ? sums.Max(x => x.UniqueId.Id) : highest);
            Storage.SaveAccounts();
            return result;
        });

        public Task RefreshCountsAsync() => Run(async c =>
        {
            foreach (var n in Root.Descendants().Where(n => n.Selectable).ToList())
            {
                if (!_folders.TryGetValue(n.Id, out var f)) continue;
                try
                {
                    await f.StatusAsync(StatusItems.Unread | StatusItems.Count);
                    n.Total = f.Count;
                    n.Unread = f.Unread;
                }
                catch (ImapCommandException) { }
            }
        });

        public Task<List<MailItem>> ListAsync(FolderNode folder) => Run(async c =>
        {
            var f = await OpenAsync(F(folder));
            var list = new List<MailItem>();
            if (f.Count == 0) return list;
            int start = Math.Max(0, f.Count - Math.Max(50, Storage.Settings.MaxMessages));
            var items = MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
                        MessageSummaryItems.InternalDate | MessageSummaryItems.Size | MessageSummaryItems.BodyStructure;
            var sums = await f.FetchAsync(start, -1, new FetchRequest(items));
            foreach (var s in sums)
            {
                var env = s.Envelope;
                var from = env?.From?.Mailboxes.FirstOrDefault();
                bool attach = false;
                try { attach = s.Attachments.Any(); } catch { }
                list.Add(new MailItem
                {
                    Store = this, Folder = folder, Uid = s.UniqueId.Id, Key = s.UniqueId.Id.ToString(),
                    FromName = from?.Name, FromAddress = from?.Address ?? "",
                    To = env?.To != null ? MailUtil.Names(env.To) : "",
                    Subject = env?.Subject ?? "",
                    Date = env?.Date ?? s.InternalDate ?? DateTimeOffset.Now,
                    Size = s.Size ?? 0,
                    IsRead = s.Flags.HasValue && s.Flags.Value.HasFlag(MessageFlags.Seen),
                    IsFlagged = s.Flags.HasValue && s.Flags.Value.HasFlag(MessageFlags.Flagged),
                    HasAttachments = attach
                });
            }
            folder.Total = f.Count;
            folder.Unread = list.Count(m => !m.IsRead);
            return list;
        });

        public async Task<MimeMessage> GetMessageAsync(MailItem item)
        {
            var key = item.Folder.Id + "\u0001" + item.Uid;
            lock (_cache)
            {
                var hit = _cache.FirstOrDefault(x => x.Key == key);
                if (hit.Msg != null) return hit.Msg;
            }
            var msg = await Run(async c =>
            {
                var f = await OpenAsync(F(item.Folder));
                return await f.GetMessageAsync(new UniqueId(item.Uid));
            });
            item.Priority = MailUtil.PriorityOf(msg);
            lock (_cache)
            {
                _cache.AddFirst((key, msg));
                while (_cache.Count > 40) _cache.RemoveLast();
            }
            return msg;
        }

        static IList<UniqueId> Uids(IEnumerable<MailItem> items) => items.Select(i => new UniqueId(i.Uid)).ToList();

        Task Store(IList<MailItem> items, MessageFlags flag, bool add) => Run(async c =>
        {
            foreach (var g in items.GroupBy(i => i.Folder))
            {
                var f = await OpenAsync(F(g.Key));
                await f.StoreAsync(Uids(g), new StoreFlagsRequest(add ? StoreAction.Add : StoreAction.Remove, flag) { Silent = true });
            }
        });

        public Task SetReadAsync(IList<MailItem> items, bool read) => Store(items, MessageFlags.Seen, read);
        public Task SetFlaggedAsync(IList<MailItem> items, bool flagged) => Store(items, MessageFlags.Flagged, flagged);

        public Task MoveAsync(IList<MailItem> items, FolderNode dest) => Run(async c =>
        {
            var d = F(dest);
            foreach (var g in items.GroupBy(i => i.Folder))
            {
                if (g.Key == dest) continue;
                var f = await OpenAsync(F(g.Key));
                await f.MoveToAsync(Uids(g), d);
            }
        });

        public async Task DeleteAsync(IList<MailItem> items)
        {
            var trash = this.Find(FolderKind.Deleted);
            var toTrash = items.Where(i => trash != null && i.Folder != trash).ToList();
            var perm = items.Except(toTrash).ToList();
            if (toTrash.Count > 0) await MoveAsync(toTrash, trash);
            if (perm.Count > 0) await DeletePermanentAsync(perm);
        }

        public Task DeletePermanentAsync(IList<MailItem> items) => Run(async c =>
        {
            foreach (var g in items.GroupBy(i => i.Folder))
            {
                var f = await OpenAsync(F(g.Key));
                var uids = Uids(g);
                await f.StoreAsync(uids, new StoreFlagsRequest(StoreAction.Add, MessageFlags.Deleted) { Silent = true });
                if (c.Capabilities.HasFlag(ImapCapabilities.UidPlus)) await f.ExpungeAsync(uids);
                else await f.ExpungeAsync();
            }
        });

        public Task<string> AppendAsync(FolderNode folder, MimeMessage message, bool read) => Run(async c =>
        {
            var f = F(folder);
            var flags = read ? MessageFlags.Seen : MessageFlags.None;
            if (folder.Kind == FolderKind.Drafts) flags |= MessageFlags.Draft;
            var uid = await f.AppendAsync(message, flags);
            return uid.HasValue ? uid.Value.Id.ToString() : null;
        });

        public Task EmptyFolderAsync(FolderNode folder) => Run(async c =>
        {
            var f = await OpenAsync(F(folder));
            if (f.Count == 0) return;
            var all = await f.SearchAsync(SearchQuery.All);
            await f.StoreAsync(all, new StoreFlagsRequest(StoreAction.Add, MessageFlags.Deleted) { Silent = true });
            await f.ExpungeAsync();
        });

        public Task CreateFolderAsync(FolderNode parent, string name) => Run(async c =>
        {
            IMailFolder p;
            if (parent.Kind == FolderKind.Root)
                p = c.GetFolder(c.PersonalNamespaces.Count > 0 ? c.PersonalNamespaces[0] : new FolderNamespace('/', ""));
            else p = F(parent);
            await p.CreateAsync(name, true);
        });

        public Task DeleteFolderAsync(FolderNode folder) => Run(async c =>
        {
            var f = F(folder);
            if (f.IsOpen) await f.CloseAsync();
            await f.DeleteAsync();
        });

        public async Task DisconnectAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (_client != null && _client.IsConnected) await _client.DisconnectAsync(true);
            }
            catch { }
            finally
            {
                try { _client?.Dispose(); } catch { }
                _client = null;
                _gate.Release();
            }
        }

        public static async Task TestAsync(Account a)
        {
            using (var c = new ImapClient { Timeout = 30000 })
            {
                await c.ConnectAsync(a.ImapHost, a.ImapPort, Map(a.ImapSecurity));
                await c.AuthenticateAsync(a.Username ?? a.Email, a.Password);
                await c.DisconnectAsync(true);
            }
        }
    }

    // ===================================================================
    //  Helpers shared by all stores + sending
    // ===================================================================
    public static class MailUtil
    {
        public static string Names(InternetAddressList list) =>
            string.Join("; ", list.Mailboxes.Select(m => string.IsNullOrWhiteSpace(m.Name) ? m.Address : m.Name));

        public static string Full(InternetAddressList list) =>
            string.Join("; ", list.Mailboxes.Select(m => string.IsNullOrWhiteSpace(m.Name) ? m.Address : $"{m.Name} <{m.Address}>"));

        public static int PriorityOf(MimeMessage m)
        {
            if (m.Importance == MessageImportance.High || m.Priority == MessagePriority.Urgent ||
                m.XPriority == XMessagePriority.High || m.XPriority == XMessagePriority.Highest) return 1;
            if (m.Importance == MessageImportance.Low || m.Priority == MessagePriority.NonUrgent ||
                m.XPriority == XMessagePriority.Low || m.XPriority == XMessagePriority.Lowest) return 5;
            return 3;
        }

        public static string PlainText(MimeMessage m)
        {
            if (m.TextBody != null) return m.TextBody;
            if (m.HtmlBody != null)
            {
                try { return HtmlToPlain(m.HtmlBody); } catch { }
            }
            return "";
        }

        public static string HtmlToPlain(string html)
        {
            var t = System.Text.RegularExpressions.Regex.Replace(html, @"<(script|style|head)[^>]*>.*?</\1>", "",
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"<(br|/p|/div|/tr|/h[1-6]|/li|/blockquote)[^>]*>", "\n",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            t = System.Text.RegularExpressions.Regex.Replace(t, @"<li[^>]*>", "\u2022 ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            t = System.Text.RegularExpressions.Regex.Replace(t, @"<[^>]+>", "");
            t = System.Net.WebUtility.HtmlDecode(t);
            t = System.Text.RegularExpressions.Regex.Replace(t, @"[ \t]*\n[ \t]*", "\n");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\n{3,}", "\n\n");
            return t.Trim();
        }

        public static string FileNameOf(MimeEntity e)
        {
            if (e is MimePart p && !string.IsNullOrWhiteSpace(p.FileName)) return p.FileName;
            if (e is MessagePart mp) return (string.IsNullOrWhiteSpace(mp.Message?.Subject) ? "message" : Safe(mp.Message.Subject)) + ".eml";
            return e.ContentDisposition?.FileName ?? "attachment";
        }

        public static string Safe(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length > 80 ? name.Substring(0, 80) : name;
        }

        public static void SaveEntity(MimeEntity e, string path)
        {
            using (var fs = File.Create(path))
            {
                if (e is MimePart p) p.Content.DecodeTo(fs);
                else if (e is MessagePart mp) mp.Message.WriteTo(fs);
            }
        }
    }

    public static class Mail
    {
        public static LocalStore Local { get; private set; }
        public static List<ImapStore> Accounts { get; } = new List<ImapStore>();
        public static IEnumerable<IMailStore> All => new IMailStore[] { Local }.Concat(Accounts);

        public static void Init()
        {
            Local = new LocalStore();
            Accounts.Clear();
            foreach (var a in Storage.Accounts) Accounts.Add(new ImapStore(a));
        }

        public static Account DefaultAccount =>
            Storage.Accounts.FirstOrDefault(a => a.IsDefault) ?? Storage.Accounts.FirstOrDefault();

        public static ImapStore StoreFor(Account a) => Accounts.FirstOrDefault(s => s.Account == a);

        /// <summary>Loads (or reloads) a store's folder tree onto its root node. Call on the UI thread.</summary>
        public static async Task LoadTreeAsync(IMailStore store)
        {
            store.Root.Status = store is LocalStore ? null : "connecting...";
            try
            {
                var children = await store.LoadChildrenAsync();
                store.Root.Children.Clear();
                foreach (var c in children) store.Root.Children.Add(c);
                store.Root.Status = null;
                await store.RefreshCountsAsync();
            }
            catch (Exception ex)
            {
                store.Root.Status = "offline";
                throw new MailException(store.DisplayName, ex);
            }
        }

        public static async Task MoveAsync(IList<MailItem> items, FolderNode dest)
        {
            if (items.Count == 0) return;
            var src = items[0].Store;
            if (src == dest.Store) { await src.MoveAsync(items, dest); return; }
            foreach (var it in items)
            {
                var msg = await src.GetMessageAsync(it);
                await dest.Store.AppendAsync(dest, msg, it.IsRead);
            }
            await src.DeletePermanentAsync(items);
        }

        public static async Task CopyAsync(IList<MailItem> items, FolderNode dest)
        {
            foreach (var it in items)
            {
                var msg = await it.Store.GetMessageAsync(it);
                await dest.Store.AppendAsync(dest, msg, it.IsRead);
            }
        }

        public static async Task SendAsync(MimeMessage msg, Account acct)
        {
            msg.Headers.Remove("X-Valco-Account");
            using (var smtp = new SmtpClient { Timeout = 60000 })
            {
                await smtp.ConnectAsync(acct.SmtpHost, acct.SmtpPort, ImapStore.Map(acct.SmtpSecurity));
                if (acct.SmtpRequiresAuth && smtp.Capabilities.HasFlag(SmtpCapabilities.Authentication))
                    await smtp.AuthenticateAsync(acct.Username ?? acct.Email, acct.Password);
                await smtp.SendAsync(msg);
                await smtp.DisconnectAsync(true);
            }
            await SaveSentCopyAsync(msg, acct);
        }

        static async Task SaveSentCopyAsync(MimeMessage msg, Account acct)
        {
            if (acct.SaveSentCopy)
            {
                var store = StoreFor(acct);
                var sent = store?.Find(FolderKind.Sent);
                if (sent != null)
                {
                    try { await store.AppendAsync(sent, msg, true); return; } catch { }
                }
            }
            await Local.AppendAsync(Local.Find(FolderKind.Sent), msg, true);
        }

        public static async Task TestSmtpAsync(Account a)
        {
            using (var smtp = new SmtpClient { Timeout = 30000 })
            {
                await smtp.ConnectAsync(a.SmtpHost, a.SmtpPort, ImapStore.Map(a.SmtpSecurity));
                if (a.SmtpRequiresAuth && smtp.Capabilities.HasFlag(SmtpCapabilities.Authentication))
                    await smtp.AuthenticateAsync(a.Username ?? a.Email, a.Password);
                await smtp.DisconnectAsync(true);
            }
        }
    }

    public class MailException : Exception
    {
        public MailException(string where, Exception inner) : base($"{where}: {inner.Message}", inner) { }
    }
}
