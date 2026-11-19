using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ValcoSpreadsheets
{
    public class Settings
    {
        public string ColorScheme { get; set; } = "Blue";
        public string UserName { get; set; } = "";
        public string DefaultFont { get; set; } = "Calibri";
        public double DefaultSize { get; set; } = 11;
        public int DefaultSheets { get; set; } = 3;
        public bool MoveAfterEnter { get; set; } = true;
        public string EnterDirection { get; set; } = "Down";
        public bool EditInCell { get; set; } = true;
        public bool FillHandle { get; set; } = true;
        public bool AutoComplete { get; set; } = true;
        public bool ShowFormulaBar { get; set; } = true;
        public bool ShowStatusBar { get; set; } = true;
        public bool AutoRecover { get; set; } = true;
        public int AutoRecoverMinutes { get; set; } = 10;
        public string DefaultFolder { get; set; } = "";
        public List<string> Recent { get; set; } = new List<string>();
        public List<string> Pinned { get; set; } = new List<string>();
        public List<string> QuickAccess { get; set; } = new List<string> { "Save", "Undo", "Redo" };
        public bool RibbonMinimized { get; set; }
        public double WinLeft { get; set; } = double.NaN;
        public double WinTop { get; set; } = double.NaN;
        public double WinWidth { get; set; } = 1180;
        public double WinHeight { get; set; } = 800;
        public bool WinMax { get; set; }

        public static Settings Current { get; private set; } = new Settings();
        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Valco", "Spreadsheets");
        static string FilePath => Path.Combine(Dir, "settings.json");
        public static string AutoRecoverDir => Path.Combine(Dir, "AutoRecover");

        public static void Load()
        {
            try { if (File.Exists(FilePath)) Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); }
            catch { Current = new Settings(); }
        }

        public static void Save()
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true })); }
            catch { }
        }

        public static void AddRecent(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Current.Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            Current.Recent.Insert(0, path);
            if (Current.Recent.Count > 17) Current.Recent.RemoveRange(17, Current.Recent.Count - 17);
            Save();
        }

        public static string EffectiveUserName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Current.UserName)) return Current.UserName;
                try { return Suite.UserName ?? Environment.UserName; } catch { return Environment.UserName; }
            }
        }
    }
}
