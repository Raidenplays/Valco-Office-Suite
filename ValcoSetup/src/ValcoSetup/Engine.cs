using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ValcoSetup
{
    /// <summary>Installs, repairs, updates and removes the suite. Runs on a worker thread; reports progress through a callback.</summary>
    public class Engine
    {
        public const string RegRoot = @"SOFTWARE\Valco\Office";
        public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ValcoOffice";
        public const string StartFolderName = "Valco Office";

        public SuiteManifest Manifest;
        public string SourceDir;             // folder holding the payload files
        public string InstallRoot = DefaultRoot;
        public bool DesktopShortcuts = true, Associations = true, AutoUpdate = true, RemoveOldVersions = true;
        public string UserName, Initials, Organization;
        public Action<double, string> Progress = (p, s) => { };
        /// <summary>Asked when apps are running: return true to retry, false to skip closing.</summary>
        public Func<string[], bool> AskCloseApps = names => false;

        public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Valco Office");
        public string SetupDir => Path.Combine(InstallRoot, "Setup");
        public string CacheDir => Path.Combine(SetupDir, "Cache");
        static string StartMenuDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), StartFolderName);
        static string DesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        public static bool IsAdmin
        {
            get { try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; } }
        }

        // =====================================================================================
        //  Installed state
        // =====================================================================================
        public static RegistryKey OpenRoot(bool write = false) => write ? Registry.LocalMachine.CreateSubKey(RegRoot) : Registry.LocalMachine.OpenSubKey(RegRoot);

        public static bool IsInstalled { get { using var k = OpenRoot(); return k?.GetValue("InstallRoot") is string; } }

        public static string InstalledRoot { get { using var k = OpenRoot(); return k?.GetValue("InstallRoot") as string; } }

        /// <summary>Installed apps and their versions.</summary>
        public static Dictionary<string, string> InstalledApps()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var k = Registry.LocalMachine.OpenSubKey(RegRoot + @"\Apps");
            if (k == null) return d;
            foreach (var id in k.GetSubKeyNames())
            {
                using var a = k.OpenSubKey(id);
                d[id] = a?.GetValue("Version") as string ?? "0";
            }
            return d;
        }

        public static string GetOption(string name, string def)
        {
            using var k = OpenRoot();
            return k?.GetValue(name) as string ?? def;
        }

        /// <summary>The manifest saved at install time (lists every app this copy of setup knows about).</summary>
        public static SuiteManifest InstalledManifest()
        {
            var root = InstalledRoot;
            if (root == null) return null;
            var p = Path.Combine(root, "Setup", "valco-office.json");
            try { return File.Exists(p) ? SuiteManifest.Load(p) : null; } catch { return null; }
        }

        // =====================================================================================
        //  Previous (single-app MSI) versions
        // =====================================================================================
        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        static extern uint MsiEnumRelatedProducts(string upgradeCode, uint reserved, uint index, StringBuilder productCode);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        static extern uint MsiGetProductInfo(string product, string property, StringBuilder value, ref int len);

        public static List<(string AppId, string Name, string ProductCode, string Version)> PreviousVersions(SuiteManifest m)
        {
            var list = new List<(string, string, string, string)>();
            foreach (var a in m.Apps.Concat(m.Retired ?? new List<AppEntry>()).Where(a => !string.IsNullOrEmpty(a.UpgradeCode) && a.Kind != "msi"))
            {
                try
                {
                    var code = "{" + a.UpgradeCode.Trim('{', '}') + "}";
                    for (uint i = 0; ; i++)
                    {
                        var sb = new StringBuilder(39);
                        if (MsiEnumRelatedProducts(code, 0, i, sb) != 0) break;
                        var vb = new StringBuilder(64); int len = 64;
                        MsiGetProductInfo(sb.ToString(), "VersionString", vb, ref len);
                        list.Add((a.Id, a.Name, sb.ToString(), vb.ToString()));
                    }
                }
                catch { }
            }
            return list;
        }

        static int RunWait(string file, string args)
        {
            var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true });
            p.WaitForExit();
            return p.ExitCode;
        }

        // =====================================================================================
        //  Running apps
        // =====================================================================================
        /// <summary>
        /// The app's running processes. "process" is an exe name (ValcoWord), or a path inside the app's folder
        /// (engine\\firefox.exe for ValcoNet) so that only that copy is matched, never the user's own Firefox.
        /// </summary>
        Process[] Running(AppEntry a)
        {
            if (!a.Process.Contains('\\') && !a.Process.Contains('/')) return Process.GetProcessesByName(a.Process);
            var full = Path.GetFullPath(Path.Combine(InstallRoot, a.Folder ?? a.Name, a.Process));
            return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(full)).Where(p =>
            {
                try { return string.Equals(p.MainModule?.FileName, full, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            }).ToArray();
        }

        void CloseApps(IEnumerable<AppEntry> apps)
        {
            var list = apps.Where(a => !string.IsNullOrEmpty(a.Process)).ToList();
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var running = list.Where(a => Running(a).Length > 0).ToList();
                if (running.Count == 0) return;
                foreach (var a in running)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(a.ExitArgs) && a.Exe != null)
                        {
                            var exe = Path.Combine(InstallRoot, a.Folder ?? a.Name, a.Exe);
                            if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe, a.ExitArgs) { UseShellExecute = false })?.WaitForExit(8000);
                        }
                        foreach (var p in Running(a)) { try { p.CloseMainWindow(); } catch { } }
                    }
                    catch { }
                }
                Thread.Sleep(2500);
                running = list.Where(a => Running(a).Length > 0).ToList();
                if (running.Count == 0) return;
                if (!AskCloseApps(running.Select(a => a.Name).ToArray()))
                {
                    foreach (var a in running) foreach (var p in Running(a)) { try { p.Kill(); p.WaitForExit(5000); } catch { } }
                    return;
                }
            }
        }

        // =====================================================================================
        //  Install / change
        // =====================================================================================
        /// <summary>Makes the installed set equal to <paramref name="wanted"/> (installing missing apps, removing the rest).</summary>
        public void Apply(ICollection<string> wanted, bool repairAll = false, ICollection<string> forceReinstall = null)
        {
            var installed = InstalledApps();
            var toInstall = Manifest.Apps.Where(a => wanted.Contains(a.Id) && (repairAll || !installed.ContainsKey(a.Id) || (forceReinstall?.Contains(a.Id) ?? false) || Ver.Newer(a.Version, installed[a.Id]))).ToList();
            var toRemove = installed.Keys.Where(id => !wanted.Contains(id)).ToList();
            var known = (InstalledManifest()?.Apps ?? new List<AppEntry>()).Concat(Manifest.Retired ?? new List<AppEntry>()).Concat(Manifest.Apps).GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            int steps = toInstall.Count * 3 + toRemove.Count + 4;
            int step = 0;
            void P(string s) => Progress(Math.Min(1, step / (double)Math.Max(1, steps)), s);

            P("Preparing to install...");
            Directory.CreateDirectory(InstallRoot);
            CacheSource();
            CloseApps(toInstall.Concat(toRemove.Where(known.ContainsKey).Select(id => known[id])));

            if (RemoveOldVersions)
            {
                var retiredIds = (Manifest.Retired ?? new List<AppEntry>()).Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var old in PreviousVersions(Manifest).Where(o => wanted.Contains(o.AppId) || retiredIds.Contains(o.AppId)))
                {
                    P($"Removing the previous version of {old.Name}...");
                    try { RunWait("msiexec.exe", $"/x {old.ProductCode} /qn /norestart"); } catch { }
                }
            }
            step++;

            foreach (var id in toRemove)
            {
                P($"Removing {(known.TryGetValue(id, out var k) ? k.Name : id)}...");
                RemoveApp(id, known.TryGetValue(id, out var ka) ? ka : null);
                step++;
            }

            foreach (var app in toInstall)
            {
                var src = Path.Combine(SourceDir ?? CacheDir, app.File);
                P($"Copying files for {app.Name}...");
                if (!File.Exists(src)) src = Path.Combine(CacheDir, app.File);
                if (!File.Exists(src)) throw new FileNotFoundException($"The installation files for {app.Name} couldn't be found ({app.File}).");
                if (!string.IsNullOrEmpty(app.Sha256) && !Sha256(src).Equals(app.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The installation file for {app.Name} is damaged. Download Valco Office Setup again.");
                // keep a local copy so features can be added or repaired later without the original source (like MSOCache)
                Directory.CreateDirectory(CacheDir);
                var cached = Path.Combine(CacheDir, app.File);
                if (!PathsEqual(src, cached)) File.Copy(src, cached, true);
                step++;
                P($"Installing {app.Name}...");
                if (app.Kind == "msi") InstallMsi(app, cached);
                else InstallZip(app, cached);
                step++;
                P($"Registering {app.Name}...");
                Register(app);
                step++;
            }

            P("Creating shortcuts and registering Valco Office...");
            WriteSuiteRegistration(wanted);
            step = steps;
            P("Installation complete.");
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Copies the whole installation source (every program, icons, license) to the local cache, like Office 2007's MSOCache.</summary>
        void CacheSource()
        {
            if (SourceDir == null || PathsEqual(SourceDir, CacheDir)) return;
            Directory.CreateDirectory(CacheDir);
            var files = Manifest.Apps.SelectMany(a => new[] { a.File, a.Icon }).Where(f => !string.IsNullOrEmpty(f)).Concat(new[] { "License.rtf" });
            foreach (var f in files.Distinct())
            {
                var src = Path.Combine(SourceDir, f);
                if (!File.Exists(src)) continue;
                var dst = Path.Combine(CacheDir, f);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                try { if (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(src).Length || File.GetLastWriteTimeUtc(dst) < File.GetLastWriteTimeUtc(src)) File.Copy(src, dst, true); } catch { }
            }
        }

        static bool PathsEqual(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        public static string Sha256(string path)
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs));
        }

        string AppDir(AppEntry a) => Path.Combine(InstallRoot, a.Folder ?? a.Name);

        void InstallZip(AppEntry app, string zip)
        {
            var dir = AppDir(app);
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
                    catch { try { File.Move(f, Path.Combine(Path.GetTempPath(), "valco-old-" + Guid.NewGuid().ToString("N"))); } catch { } }
                }
            }
            Directory.CreateDirectory(dir);
            ZipFile.ExtractToDirectory(zip, dir, true);
        }

        void InstallMsi(AppEntry app, string msi)
        {
            // a rebuilt MSI is a new product: remove the old one first so there's never two copies
            if (!string.IsNullOrEmpty(app.UpgradeCode))
                foreach (var old in RelatedProducts(app.UpgradeCode).ToList()) { try { RunWait("msiexec.exe", $"/x {old} /qn /norestart"); } catch { } }
            var log = Path.Combine(Path.GetTempPath(), "ValcoOffice-" + app.Id + ".log");
            int code = RunWait("msiexec.exe", $"/i \"{msi}\" /qn /norestart INSTALLDIR=\"{AppDir(app)}\" /l*v \"{log}\"");
            if (code != 0 && code != 3010) throw new Exception($"Windows Installer couldn't install {app.Name} (error {code}). Details are in {log}.");
        }

        string Expand(string s, AppEntry a) => (s ?? "").Replace("{EXE}", Path.Combine(AppDir(a), a.Exe ?? "")).Replace("{DIR}", AppDir(a)).Replace("{NAME}", a.Name);

        void Register(AppEntry app)
        {
            using var key = Registry.LocalMachine.CreateSubKey(RegRoot + @"\Apps\" + app.Id);
            key.SetValue("Version", app.Version);
            key.SetValue("Name", app.Name);
            key.SetValue("Kind", app.Kind);
            key.SetValue("Folder", AppDir(app));
            var owned = new List<string>(); var values = new List<string>(); var shortcuts = new List<string>();
            // file types, protocols and Default apps registration (MSI-chained apps too)
            foreach (var r in app.Registry)
            {
                if (r.Associations && !Associations) continue;
                var k = Expand(r.Key, app);
                using (var rk = Registry.LocalMachine.CreateSubKey(k)) rk.SetValue(Expand(r.Name, app), Expand(r.Value, app));
                if (r.Owned) owned.Add(k); else values.Add(k + "|" + Expand(r.Name, app));
            }
            if (app.Kind != "msi")
            {
                var exe = Path.Combine(AppDir(app), app.Exe ?? "");
                Directory.CreateDirectory(StartMenuDir);
                var lnk = Path.Combine(StartMenuDir, app.Name + ".lnk");
                ShellLink.Create(lnk, exe, "", AppDir(app), app.ShortcutDescription ?? app.Description, exe);
                shortcuts.Add(lnk);
                foreach (var s in app.Shortcuts)
                {
                    var l2 = Path.Combine(StartMenuDir, s.Name + ".lnk");
                    ShellLink.Create(l2, exe, s.Args, AppDir(app), s.Description, exe);
                    shortcuts.Add(l2);
                }
                if (DesktopShortcuts && app.Desktop)
                {
                    var dl = Path.Combine(DesktopDir, app.Name + ".lnk");
                    ShellLink.Create(dl, exe, "", AppDir(app), app.ShortcutDescription ?? app.Description, exe);
                    shortcuts.Add(dl);
                }
            }
            else
            {
                // MSI-chained apps register themselves; remember the product so it can be removed later
                var code = string.IsNullOrEmpty(app.UpgradeCode) ? null : RelatedProducts(app.UpgradeCode).FirstOrDefault();
                if (code != null) key.SetValue("ProductCode", code);
            }
            key.SetValue("OwnedKeys", owned.ToArray(), RegistryValueKind.MultiString);
            key.SetValue("Values", values.ToArray(), RegistryValueKind.MultiString);
            key.SetValue("Shortcuts", shortcuts.ToArray(), RegistryValueKind.MultiString);
        }

        static IEnumerable<string> RelatedProducts(string upgradeCode)
        {
            var code = "{" + upgradeCode.Trim('{', '}') + "}";
            for (uint i = 0; ; i++)
            {
                var sb = new StringBuilder(39);
                if (MsiEnumRelatedProducts(code, 0, i, sb) != 0) yield break;
                yield return sb.ToString();
            }
        }

        public void RemoveApp(string id, AppEntry known)
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegRoot + @"\Apps\" + id);
            if (key == null) return;
            var kind = key.GetValue("Kind") as string;
            var folder = key.GetValue("Folder") as string;
            if (kind == "msi")
            {
                var code = key.GetValue("ProductCode") as string;
                if (code == null && known?.UpgradeCode != null) code = RelatedProducts(known.UpgradeCode).FirstOrDefault();
                if (code != null) { try { RunWait("msiexec.exe", $"/x {code} /qn /norestart"); } catch { } }
            }
            foreach (var lnk in key.GetValue("Shortcuts") as string[] ?? new string[0]) { try { File.Delete(lnk); } catch { } }
            foreach (var v in key.GetValue("Values") as string[] ?? new string[0])
            {
                var i = v.LastIndexOf('|');
                if (i < 0) continue;
                try { using var rk = Registry.LocalMachine.OpenSubKey(v.Substring(0, i), true); rk?.DeleteValue(v.Substring(i + 1), false); } catch { }
            }
            foreach (var k in (key.GetValue("OwnedKeys") as string[] ?? new string[0]).OrderByDescending(x => x.Length))
            {
                try { Registry.LocalMachine.DeleteSubKeyTree(k, false); } catch { }
            }
            if (kind != "msi" && folder != null && Directory.Exists(folder) && folder.StartsWith(InstallRoot, StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.Delete(folder, true); }
                catch { foreach (var f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) { try { File.Delete(f); } catch { MoveFileEx(f, null, 4); } } }
            }
            key.Close();
            try { Registry.LocalMachine.DeleteSubKeyTree(RegRoot + @"\Apps\" + id, false); } catch { }
        }

        void WriteSuiteRegistration(ICollection<string> wanted)
        {
            // setup itself, its manifest and icon live in <root>\Setup so Programs and Features can change or remove the suite
            Directory.CreateDirectory(SetupDir);
            var self = Environment.ProcessPath;
            var setupExe = Path.Combine(SetupDir, "setup.exe");
            if (!PathsEqual(self, setupExe))
            {
                // a newer setup replaces the installed one (retry briefly in case the old one is still exiting)
                // ValcoOffice-Setup.exe carries the whole payload; only the setup program itself is kept
                bool replace = !File.Exists(setupExe) || Ver.Newer(FileVersionInfo.GetVersionInfo(self).FileVersion, FileVersionInfo.GetVersionInfo(setupExe).FileVersion) || Package.ExeLength(self) != new FileInfo(setupExe).Length;
                for (int i = 0; replace && i < 10; i++) { try { Package.CopyExe(self, setupExe); break; } catch { Thread.Sleep(500); } }
            }
            var installedManifest = Path.Combine(SetupDir, "valco-office.json");
            // merge: remember every app either manifest knows, with the newest metadata
            var merged = InstalledManifest() ?? new SuiteManifest();
            foreach (var a in Manifest.Apps) { merged.Apps.RemoveAll(x => x.Id == a.Id); merged.Apps.Add(a); }
            merged.Version = Manifest.Version; merged.UpdateUrl = Manifest.UpdateUrl; merged.SetupVersion = Manifest.SetupVersion; merged.Product = Manifest.Product;
            File.WriteAllText(installedManifest, merged.ToJson());
            var icon = Path.Combine(SetupDir, "ValcoOffice.ico");
            try
            {
                using var s = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/ValcoOffice.ico")).Stream;
                using var f = File.Create(icon); s.CopyTo(f);
            }
            catch { }

            using (var root = OpenRoot(true))
            {
                root.SetValue("InstallRoot", InstallRoot);
                root.SetValue("Version", Manifest.Version);
                root.SetValue("UpdateUrl", Manifest.UpdateUrl ?? "");
                root.SetValue("DesktopShortcuts", DesktopShortcuts ? "1" : "0");
                root.SetValue("Associations", Associations ? "1" : "0");
                root.SetValue("AutoUpdate", AutoUpdate ? "1" : "0");
            }
            if (!string.IsNullOrEmpty(UserName))
            {
                using var u = Registry.CurrentUser.CreateSubKey(@"Software\Valco\Office\UserInfo");
                u.SetValue("UserName", UserName); u.SetValue("Initials", Initials ?? ""); u.SetValue("Company", Organization ?? "");
            }

            // Programs and Features
            long kb = Manifest.Apps.Where(a => wanted.Contains(a.Id)).Sum(a => a.InstallSize) / 1024;
            using (var un = Registry.LocalMachine.CreateSubKey(UninstallKey))
            {
                un.SetValue("DisplayName", Manifest.Product);
                un.SetValue("DisplayVersion", Manifest.Version);
                un.SetValue("Publisher", Manifest.Publisher);
                un.SetValue("DisplayIcon", icon);
                un.SetValue("InstallLocation", InstallRoot);
                un.SetValue("UninstallString", $"\"{setupExe}\" /uninstall");
                un.SetValue("ModifyPath", $"\"{setupExe}\" /maintenance");
                un.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, kb), RegistryValueKind.DWord);
                un.SetValue("NoRepair", 0, RegistryValueKind.DWord);
                un.SetValue("URLInfoAbout", "https://github.com/Raidenplays");
                un.SetValue("HelpLink", "https://github.com/Raidenplays");
                un.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }

            // Valco Office Tools in the Start menu, plus the background update check at sign-in
            var tools = Path.Combine(StartMenuDir, "Valco Office Tools");
            Directory.CreateDirectory(tools);
            ShellLink.Create(Path.Combine(tools, "Valco Office Update.lnk"), setupExe, "/update", SetupDir, "Check for and install updates to Valco Office", setupExe);
            ShellLink.Create(Path.Combine(tools, "Set Default Programs.lnk"), setupExe, "/defaults", SetupDir, "Make Valco Office programs the defaults for documents, e-mail and web pages", setupExe);
            ShellLink.Create(Path.Combine(tools, "Valco Office Setup.lnk"), setupExe, "/maintenance", SetupDir, "Add or remove Valco Office programs, or repair the installation", setupExe);
            using (var run = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (AutoUpdate) run.SetValue("Valco Office Update", $"\"{setupExe}\" /checkupdates");
                else run.DeleteValue("Valco Office Update", false);
            }
        }

        /// <summary>Removes the whole suite.</summary>
        public void UninstallAll()
        {
            var installed = InstalledApps();
            var known = (InstalledManifest()?.Apps ?? new List<AppEntry>()).ToDictionary(a => a.Id, a => a, StringComparer.OrdinalIgnoreCase);
            CloseApps(installed.Keys.Where(known.ContainsKey).Select(id => known[id]));
            int i = 0;
            foreach (var id in installed.Keys)
            {
                Progress(i++ / (double)(installed.Count + 1), $"Removing {(known.TryGetValue(id, out var a) ? a.Name : id)}...");
                RemoveApp(id, known.TryGetValue(id, out var k) ? k : null);
            }
            Progress(0.95, "Removing shared components...");
            try { Directory.Delete(StartMenuDir, true); } catch { }
            using (var run = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)) run?.DeleteValue("Valco Office Update", false);
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try { Registry.LocalMachine.DeleteSubKeyTree(RegRoot, false); } catch { }
            // setup.exe is running from the folder being removed: schedule the rest for the next restart
            foreach (var f in Directory.Exists(SetupDir) ? Directory.GetFiles(SetupDir, "*", SearchOption.AllDirectories) : new string[0])
            {
                try { File.Delete(f); } catch { MoveFileEx(f, null, 4); }
            }
            foreach (var d in new[] { CacheDir, SetupDir, InstallRoot })
            {
                try { Directory.Delete(d, false); } catch { MoveFileEx(d, null, 4); }
            }
            Progress(1, "Valco Office has been removed.");
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool MoveFileEx(string existing, string newName, int flags);
        [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
    }
}
