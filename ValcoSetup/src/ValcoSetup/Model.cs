using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValcoSetup
{
    /// <summary>valco-office.json: what's in this setup (or on the update server).</summary>
    public class SuiteManifest
    {
        public string Product { get; set; } = "Valco Office";
        public string Version { get; set; } = "1.0.0";
        public string Publisher { get; set; } = "ItsRaiden";
        public string UpdateUrl { get; set; } = "https://github.com/Raidenplays/Valco-Office-Suite/releases/latest/download/valco-office.json";
        public string SetupVersion { get; set; } = "1.0.0";
        public string SetupFile { get; set; } = "setup.exe";
        public string SetupSha256 { get; set; }
        public List<AppEntry> Apps { get; set; } = new List<AppEntry>();
        /// <summary>Discontinued programs (e.g. Valco Shell): removed whenever Valco Office is installed, changed or updated.</summary>
        public List<AppEntry> Retired { get; set; } = new List<AppEntry>();
        /// <summary>Release builds: the commit it was built from and what changed in Setup itself.</summary>
        public string Commit { get; set; }
        public string SetupSourceHash { get; set; }
        public List<string> SetupChanges { get; set; }

        static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        public static SuiteManifest Parse(string json) => JsonSerializer.Deserialize<SuiteManifest>(json, Opts);
        public static SuiteManifest Load(string path) => Parse(File.ReadAllText(path));
        public string ToJson() => JsonSerializer.Serialize(this, Opts);
    }

    public class AppEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; } = "1.0.0";
        public string Description { get; set; } = "";
        /// <summary>"zip" (copied into the install folder) or "msi" (chained through Windows Installer).</summary>
        public string Kind { get; set; } = "zip";
        public string File { get; set; }
        public string Sha256 { get; set; }
        public long DownloadSize { get; set; }
        public long InstallSize { get; set; }
        public string Folder { get; set; }
        public string Exe { get; set; }
        public string Process { get; set; }
        public string ExitArgs { get; set; }
        public string Icon { get; set; }
        public string ShortcutDescription { get; set; }
        public bool DefaultSelected { get; set; } = true;
        public bool Desktop { get; set; } = true;
        public string UpgradeCode { get; set; }
        public string Notes { get; set; }
        /// <summary>Name under HKLM\\Software\\RegisteredApplications, used to open Settings → Default apps for this program.</summary>
        public string DefaultApps { get; set; }
        /// <summary>Optional taskbar AppUserModelID stamped on the app's shortcuts, so pinned icons relaunch through the shortcut's target.</summary>
        public string AppUserModelId { get; set; }
        public List<RegEntry> Registry { get; set; } = new List<RegEntry>();
        public List<ExtraShortcut> Shortcuts { get; set; } = new List<ExtraShortcut>();
        /// <summary>Release builds: hash of the sources this program was built from (unchanged programs keep their file and version).</summary>
        public string SourceHash { get; set; }
        /// <summary>ValcoNet: the Gecko (Firefox ESR) version inside.</summary>
        public string Engine { get; set; }
        /// <summary>What changed since the previous release, shown by Valco Office Update.</summary>
        public List<string> Changes { get; set; }
    }

    /// <summary>A registry value written for an app. {EXE}, {DIR} and {NAME} are expanded. Owned keys are deleted on removal; otherwise only the value is.</summary>
    public class RegEntry
    {
        public string Key { get; set; }
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public bool Owned { get; set; }
        public bool Associations { get; set; }
    }

    public class ExtraShortcut
    {
        public string Name { get; set; }
        public string Args { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public static class Ver
    {
        public static Version Of(string s) => System.Version.TryParse((s ?? "0").Trim().TrimStart('v'), out var v) ? v : new Version(0, 0);
        public static bool Newer(string a, string b) => Of(a) > Of(b);
    }
}
