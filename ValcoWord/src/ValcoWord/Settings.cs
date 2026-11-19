using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ValcoWord
{
    public class PageSettings
    {
        public string Paper { get; set; } = "Letter";
        public bool Landscape { get; set; }
        // all in inches
        public double MarginLeft { get; set; } = 1;
        public double MarginRight { get; set; } = 1;
        public double MarginTop { get; set; } = 1;
        public double MarginBottom { get; set; } = 1;
        [System.Text.Json.Serialization.JsonIgnore]
        public bool PageNumbers
        {
            get => (FooterCenter ?? "").Contains("{PAGE}") || (FooterRight ?? "").Contains("{PAGE}") || (FooterLeft ?? "").Contains("{PAGE}") || (HeaderRight ?? "").Contains("{PAGE}") || (HeaderText ?? "").Contains("{PAGE}") || (HeaderLeft ?? "").Contains("{PAGE}");
            set { if (value && !PageNumbers) FooterCenter = "{PAGE}"; else if (!value) foreach (var p in new[] { "FooterCenter", "FooterRight", "FooterLeft", "HeaderRight", "HeaderText", "HeaderLeft" }) { var pi = GetType().GetProperty(p); var v = (string)pi.GetValue(this) ?? ""; pi.SetValue(this, v.Replace("Page {PAGE} of {NUMPAGES}", "").Replace("Page {PAGE}", "").Replace("{PAGE}", "").Trim()); } }
        }
        public string HeaderText { get; set; } = "";   // centre header
        public string HeaderLeft { get; set; } = "";
        public string HeaderRight { get; set; } = "";
        public string FooterLeft { get; set; } = "";
        public string FooterCenter { get; set; } = "";
        public string FooterRight { get; set; } = "";
        public int Columns { get; set; } = 1;
        public double ColumnSpacingIn { get; set; } = 0.5;
        public string Watermark { get; set; } = "";
        public string WatermarkColor { get; set; } = "C0C0C0";
        public string PageColor { get; set; } = "";
        public bool PageBorder { get; set; }
        public string PageBorderColor { get; set; } = "000000";
        public double PageBorderWidth { get; set; } = 1;
        public bool LineNumbers { get; set; }
        public bool Hyphenation { get; set; }
        public string NumFormat { get; set; } = "1";
        public int NumStart { get; set; } = 1;
        public bool HasHeader => !string.IsNullOrWhiteSpace(HeaderLeft) || !string.IsNullOrWhiteSpace(HeaderText) || !string.IsNullOrWhiteSpace(HeaderRight);
        public bool HasFooter => !string.IsNullOrWhiteSpace(FooterLeft) || !string.IsNullOrWhiteSpace(FooterCenter) || !string.IsNullOrWhiteSpace(FooterRight);

        public static readonly (string Name, double W, double H)[] Papers =
        {
            ("Letter", 8.5, 11), ("Legal", 8.5, 14), ("Executive", 7.25, 10.5),
            ("A4", 8.27, 11.69), ("A5", 5.83, 8.27), ("B5", 6.93, 9.84)
        };

        public double WidthIn => Landscape ? PaperSize.H : PaperSize.W;
        public double HeightIn => Landscape ? PaperSize.W : PaperSize.H;
        (string Name, double W, double H) PaperSize => Paper == "Custom" && CustomW > 0 && CustomH > 0 ? ("Custom", CustomW, CustomH) : Papers.FirstOrDefault(p => p.Name == Paper) is var p && p.Name != null ? p : Papers[0];
        public double CustomW { get; set; }
        public double CustomH { get; set; }
        public double WidthPx => WidthIn * 96;
        public double HeightPx => HeightIn * 96;
        public double BodyWidthPx => (WidthIn - MarginLeft - MarginRight) * 96;
        public double BodyHeightPx => (HeightIn - MarginTop - MarginBottom) * 96;

        public PageSettings Clone() => (PageSettings)MemberwiseClone();

        public static string ClosestPaper(double wIn, double hIn)
        {
            double a = Math.Min(wIn, hIn), b = Math.Max(wIn, hIn);
            var best = Papers.OrderBy(p => Math.Abs(p.W - a) + Math.Abs(p.H - b)).First();
            return best.Name;
        }
    }

    public class Settings
    {
        public string DefaultFont { get; set; } = "Calibri";
        public double DefaultSizePt { get; set; } = 11;
        public bool SpellCheck { get; set; } = true;
        public bool GrammarCheck { get; set; } = true;
        public string UserName { get; set; } = Environment.UserName;
        public string Initials { get; set; } = "";
        public string ColorScheme { get; set; } = "Blue";
        public bool MiniToolbar { get; set; } = true;
        // Word 2007 default: the Insert key does NOT switch to overtype unless this is turned on
        public bool InsertKeyOvertype { get; set; }
        public bool ShowMarks { get; set; }
        public bool RibbonMinimized { get; set; }
        public List<string> Pinned { get; set; } = new List<string>();
        public Dictionary<string, string> Custom { get; set; } = new Dictionary<string, string>();
        public bool ShowRuler { get; set; } = true;
        public bool ShowFormatBar { get; set; } = true;
        public bool ShowToolbar { get; set; } = true;
        public bool ShowStatusBar { get; set; } = true;
        public int AutoRecoverMinutes { get; set; } = 3;
        public double Zoom { get; set; } = 1.0;
        public List<string> Recent { get; set; } = new List<string>();
        public PageSettings Page { get; set; } = new PageSettings();
        public double WinWidth { get; set; } = 1100;
        public double WinHeight { get; set; } = 780;
        public bool WinMax { get; set; }

        public static Settings Current { get; private set; } = new Settings();
        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Valco Word");
        static string FilePath => Path.Combine(Dir, "settings.json");

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(FilePath))
                    Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch { Current = new Settings(); }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public static void AddRecent(string path)
        {
            var r = Current.Recent;
            r.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
            r.Insert(0, path);
            while (r.Count > 9) r.RemoveAt(r.Count - 1);
            Save();
        }
    }

    /// <summary>Periodic backup copies so work survives a crash or power cut.</summary>
    public static class AutoRecover
    {
        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Valco Word", "AutoRecover");

        public static string NewSlot() => Path.Combine(Dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6)}.docx");

        public static List<string> Pending()
        {
            try
            {
                if (!Directory.Exists(Dir)) return new List<string>();
                return Directory.GetFiles(Dir, "*.docx").Where(f => !InUse(f)).OrderByDescending(File.GetLastWriteTime).ToList();
            }
            catch { return new List<string>(); }
        }

        static bool InUse(string slot)
        {
            var lockFile = slot + ".lock";
            if (!File.Exists(lockFile)) return false;
            try { using (new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } return false; }
            catch { return true; }
        }

        public static void Delete(string slot)
        {
            try { if (slot != null && File.Exists(slot)) File.Delete(slot); } catch { }
            try { if (slot != null && File.Exists(slot + ".name")) File.Delete(slot + ".name"); } catch { }
        }

        public static string OriginalName(string slot)
        {
            try { return File.Exists(slot + ".name") ? File.ReadAllText(slot + ".name") : null; } catch { return null; }
        }
    }
}
