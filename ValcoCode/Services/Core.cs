using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ValcoCode.Services;

public class AppSettings
{
    public string Theme { get; set; } = "valco-blue";
    public string QuickAccess { get; set; } = "Save,Undo,Redo,Run";
    public bool RibbonMinimized { get; set; }
    public double FontSize { get; set; } = 13;
    public string FontFamily { get; set; } = "Cascadia Mono, Consolas";
    public double TerminalFontSize { get; set; } = 13;
    public int TabSize { get; set; } = 4;
    public int LineLength { get; set; } = 88;
    public bool WordWrap { get; set; }
    public bool FormatOnSave { get; set; }
    public bool AutoSave { get; set; }
    public bool ShowWhitespace { get; set; }
    public double SidebarWidth { get; set; } = 260;
    public double PanelHeight { get; set; } = 240;
    public List<string> Recent { get; set; } = new();
    public string? SessionRoot { get; set; }
    public List<string> SessionFiles { get; set; } = new();
    public string? SessionActive { get; set; }
    public double WinLeft { get; set; } = double.NaN;
    public double WinTop { get; set; } = double.NaN;
    public double WinWidth { get; set; } = 1280;
    public double WinHeight { get; set; } = 820;
    public bool WinMax { get; set; }

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Valco Code");
    static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static string DataDir { get { Directory.CreateDirectory(Dir); return Dir; } }
}

public record ProcResult(int Code, string Stdout, string Stderr)
{
    public bool Ok => Code == 0;
    public string Message => (string.IsNullOrWhiteSpace(Stderr) ? Stdout : Stderr).Trim();
}

public static class Proc
{
    /// <summary>Runs a program to completion with no console window, optionally feeding stdin.</summary>
    public static async Task<ProcResult> Run(string file, IEnumerable<string> args, string? cwd = null, string? stdin = null,
        IDictionary<string, string>? env = null, int timeoutMs = 120_000)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin != null,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = cwd ?? Environment.CurrentDirectory,
        };
        if (stdin != null) psi.StandardInputEncoding = new UTF8Encoding(false);
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (env != null) foreach (var (k, v) in env) psi.Environment[k] = v;
        using var p = new Process { StartInfo = psi };
        try { p.Start(); }
        catch (Exception e) { return new ProcResult(-1, "", e.Message); }
        var outT = p.StandardOutput.ReadToEndAsync();
        var errT = p.StandardError.ReadToEndAsync();
        if (stdin != null)
        {
            await p.StandardInput.WriteAsync(stdin);
            p.StandardInput.Close();
        }
        using var cts = new CancellationTokenSource(timeoutMs);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            return new ProcResult(-1, await outT, "Timed out");
        }
        return new ProcResult(p.ExitCode, await outT, await errT);
    }

    public static string? Which(params string[] names)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';');
        foreach (var n in names)
            foreach (var dir in paths)
            {
                foreach (var ext in new[] { "" }.Concat(exts))
                {
                    try
                    {
                        var f = Path.Combine(dir.Trim('"'), n + ext);
                        if (!File.Exists(f)) continue;
                        // the Microsoft Store "python.exe" stub only opens the Store
                        if (f.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase) && n.StartsWith("python")) continue;
                        return f;
                    }
                    catch { }
                }
            }
        return null;
    }
}

public class Tools
{
    public string? Python, Java, Javac, Git;
    public string? Ruff, JavaFormatter;

    public static Tools Find()
    {
        var t = new Tools
        {
            Python = Proc.Which("python", "python3", "py"),
            Java = Proc.Which("java"),
            Javac = Proc.Which("javac"),
            Git = Proc.Which("git"),
        };
        // a JDK that isn't on PATH (common after installing Temurin without the PATH option)
        if (t.Javac == null)
        {
            var jh = Environment.GetEnvironmentVariable("JAVA_HOME");
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(jh)) candidates.Add(Path.Combine(jh, "bin"));
            foreach (var root in new[] { @"C:\Program Files\Eclipse Adoptium", @"C:\Program Files\Java", @"C:\Program Files\Microsoft", @"C:\Program Files\Zulu" })
                if (Directory.Exists(root))
                    candidates.AddRange(Directory.GetDirectories(root).OrderByDescending(d => d).Select(d => Path.Combine(d, "bin")));
            foreach (var bin in candidates)
                if (File.Exists(Path.Combine(bin, "javac.exe")))
                {
                    t.Javac = Path.Combine(bin, "javac.exe");
                    t.Java = Path.Combine(bin, "java.exe");
                    break;
                }
        }
        if (t.Git == null)
            foreach (var g in new[] { @"C:\Program Files\Git\cmd\git.exe", @"C:\Program Files (x86)\Git\cmd\git.exe",
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\cmd\git.exe") })
                if (File.Exists(g)) { t.Git = g; break; }
        if (t.Python == null)
        {
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python");
            if (Directory.Exists(local))
                foreach (var d in Directory.GetDirectories(local).OrderByDescending(d => d))
                    if (File.Exists(Path.Combine(d, "python.exe"))) { t.Python = Path.Combine(d, "python.exe"); break; }
        }
        var here = Path.Combine(AppContext.BaseDirectory, "tools");
        t.Ruff = File.Exists(Path.Combine(here, "ruff.exe")) ? Path.Combine(here, "ruff.exe") : Proc.Which("ruff");
        t.JavaFormatter = File.Exists(Path.Combine(here, "google-java-format.exe")) ? Path.Combine(here, "google-java-format.exe") : null;
        return t;
    }
}

/// <summary>Stores the GitHub token encrypted with Windows DPAPI (only your Windows account can read it).</summary>
public static class TokenStore
{
    static string FilePath => Path.Combine(AppSettings.DataDir, "github.token");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DATA_BLOB pDataOut);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DATA_BLOB pDataOut);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr h);

    static byte[]? Transform(byte[] input, bool protect)
    {
        var inBlob = new DATA_BLOB { cbData = input.Length, pbData = Marshal.AllocHGlobal(input.Length) };
        var outBlob = new DATA_BLOB();
        try
        {
            Marshal.Copy(input, 0, inBlob.pbData, input.Length);
            var ok = protect
                ? CryptProtectData(ref inBlob, "Valco Code GitHub token", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref outBlob);
            if (!ok) return null;
            var res = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, res, 0, outBlob.cbData);
            return res;
        }
        finally
        {
            Marshal.FreeHGlobal(inBlob.pbData);
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }

    public static string? Get()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var b = Transform(File.ReadAllBytes(FilePath), false);
            return b == null ? null : Encoding.UTF8.GetString(b);
        }
        catch { return null; }
    }

    public static void Set(string token)
    {
        var b = Transform(Encoding.UTF8.GetBytes(token.Trim()), true) ?? throw new Exception("Windows couldn't encrypt the token.");
        File.WriteAllBytes(FilePath, b);
    }

    public static void Clear()
    {
        try { File.Delete(FilePath); } catch { }
    }
}

public static class Shell
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    /// <summary>Sends a file or folder to the Recycle Bin.</summary>
    public static bool Recycle(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = 3, // FO_DELETE
            pFrom = path + "\0\0",
            fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400, // ALLOWUNDO | NOCONFIRMATION | SILENT | NOERRORUI
        };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }

    public static void Reveal(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }
}
