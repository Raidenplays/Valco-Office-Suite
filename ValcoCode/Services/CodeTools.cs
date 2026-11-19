using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ValcoCode.Services;

public record Problem(string Path, int Line, int Col, int EndLine, int EndCol, string Message, string Severity, string Source, string? Code = null);

public static class Formatter
{
    public static bool CanFormat(string lang) => lang is "python" or "java";

    public static string Describe(string lang) => lang switch { "python" => "Ruff", "java" => "google-java-format", _ => "" };

    /// <summary>Formats code with the bundled formatters. Throws with a readable message on syntax errors.</summary>
    public static async Task<string> Format(string code, string lang, string? path, Tools tools, int tabSize, int lineLength)
    {
        if (lang == "python")
        {
            if (tools.Ruff == null) throw new Exception("The Python formatter (ruff.exe) is missing from Valco Code's tools folder. Reinstall Valco Code.");
            var r = await Proc.Run(tools.Ruff, new[]
            {
                "format", "--quiet", "--line-length", lineLength.ToString(), "--config", $"indent-width={tabSize}",
                "--stdin-filename", path ?? "untitled.py", "-",
            }, path != null ? System.IO.Path.GetDirectoryName(path) : null, code);
            if (!r.Ok) throw new Exception(CleanRuff(r.Message));
            return r.Stdout;
        }
        if (lang == "java")
        {
            if (tools.JavaFormatter == null) throw new Exception("The Java formatter (google-java-format.exe) is missing from Valco Code's tools folder. Reinstall Valco Code.");
            var args = new List<string>();
            if (tabSize >= 4) args.Add("--aosp"); // 4-space indents; Google style uses 2
            args.Add("-");
            var r = await Proc.Run(tools.JavaFormatter, args, null, code);
            if (!r.Ok) throw new Exception(CleanJava(r.Message));
            return r.Stdout;
        }
        throw new Exception("No formatter for this language.");
    }

    static string CleanRuff(string msg)
    {
        var m = Regex.Match(msg, @"Failed to parse .*?:(\d+):(\d+): (.*)");
        return m.Success ? $"Syntax error on line {m.Groups[1].Value}: {m.Groups[3].Value}" : msg.Split('\n')[0];
    }

    static string CleanJava(string msg)
    {
        var m = Regex.Match(msg, @"<stdin>:(\d+):(\d+): error: (.*)");
        return m.Success ? $"Syntax error on line {m.Groups[1].Value}: {m.Groups[3].Value}" : msg.Split('\n')[0];
    }
}

public static class Linter
{
    /// <summary>Python problems from Ruff: syntax errors, undefined names, unused imports and variables…</summary>
    public static async Task<List<Problem>> LintPython(string code, string path, Tools tools, int lineLength)
    {
        var list = new List<Problem>();
        if (tools.Ruff == null) return list;
        var r = await Proc.Run(tools.Ruff, new[]
        {
            "check", "--quiet", "--no-cache", "--output-format", "json", "--select", "E4,E7,E9,F,B006,B008",
            "--line-length", lineLength.ToString(), "--stdin-filename", path, "-",
        }, System.IO.Path.GetDirectoryName(path), code, timeoutMs: 15000);
        if (string.IsNullOrWhiteSpace(r.Stdout)) return list;
        try
        {
            using var doc = JsonDocument.Parse(r.Stdout);
            foreach (var d in doc.RootElement.EnumerateArray())
            {
                var code_ = d.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                var loc = d.GetProperty("location");
                var end = d.GetProperty("end_location");
                var msg = d.GetProperty("message").GetString() ?? "";
                var sev = code_ == null || code_.StartsWith("E9") || code_ is "F821" or "F822" or "F823" ? "error" : "warning";
                list.Add(new Problem(path, loc.GetProperty("row").GetInt32(), loc.GetProperty("column").GetInt32(),
                    end.GetProperty("row").GetInt32(), end.GetProperty("column").GetInt32(), msg, sev, "Ruff", code_));
            }
        }
        catch { }
        return list;
    }
}

public record JavaBuild(bool Ok, string Output, string ClassDir, string MainClass, string SourceRoot, List<Problem> Problems);

public static class JavaBuilder
{
    // Wraps the program so System.out is unbuffered: prompts printed with System.out.print()
    // show up before Scanner waits for input, even though the output goes through a pipe.
    const string Runner = """
        public final class ValcoCodeRunner {
            public static void main(String[] args) throws Throwable {
                java.io.PrintStream out = new java.io.PrintStream(new java.io.FileOutputStream(java.io.FileDescriptor.out), true, "UTF-8");
                java.io.PrintStream err = new java.io.PrintStream(new java.io.FileOutputStream(java.io.FileDescriptor.err), true, "UTF-8");
                System.setOut(out);
                System.setErr(err);
                Class<?> c = Class.forName(args[0]);
                String[] rest = java.util.Arrays.copyOfRange(args, 1, args.length);
                java.lang.reflect.Method m = c.getMethod("main", String[].class);
                m.setAccessible(true);
                try {
                    m.invoke(null, (Object) rest);
                } catch (java.lang.reflect.InvocationTargetException e) {
                    throw e.getCause();
                } finally {
                    out.flush();
                    err.flush();
                }
            }
        }
        """;

    static readonly string[] Skip = { ".git", "node_modules", "target", "build", "out", "bin", ".idea", ".gradle", "__pycache__" };

    static IEnumerable<string> JavaFiles(string dir, int depth = 0)
    {
        if (depth > 20) yield break;
        string[] files, dirs;
        try { files = Directory.GetFiles(dir, "*.java"); dirs = Directory.GetDirectories(dir); }
        catch { yield break; }
        foreach (var f in files) yield return f;
        foreach (var d in dirs)
            if (!Skip.Contains(System.IO.Path.GetFileName(d)))
                foreach (var f in JavaFiles(d, depth + 1)) yield return f;
    }

    public static async Task<JavaBuild> Build(string file, Tools tools)
    {
        if (tools.Javac == null) throw new Exception("A Java JDK wasn't found. Install Temurin 21 from adoptium.net, then restart Valco Code.");
        var src = await File.ReadAllTextAsync(file);
        var cls = System.IO.Path.GetFileNameWithoutExtension(file);
        var pkg = Regex.Match(src, @"^\s*package\s+([\w.]+)\s*;", RegexOptions.Multiline);
        var root = System.IO.Path.GetDirectoryName(file)!;
        if (pkg.Success)
            foreach (var _ in pkg.Groups[1].Value.Split('.'))
                root = System.IO.Path.GetDirectoryName(root) ?? root;
        var hash = (uint)root.ToLowerInvariant().GetHashCode();
        var outDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"valcocode-java-{hash:x8}");
        try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch { }
        Directory.CreateDirectory(outDir);
        var runnerSrc = System.IO.Path.Combine(outDir, "ValcoCodeRunner.java");
        await File.WriteAllTextAsync(runnerSrc, Runner);
        var sources = JavaFiles(root).ToList();
        var argfile = System.IO.Path.Combine(outDir, "sources.txt");
        await File.WriteAllLinesAsync(argfile, sources.Append(runnerSrc).Select(s => "\"" + s.Replace("\\", "\\\\") + "\""));
        var r = await Proc.Run(tools.Javac, new[] { "-d", outDir, "-encoding", "UTF-8", "-Xdiags:verbose", "@" + argfile }, root);
        var output = (r.Stdout + r.Stderr).TrimEnd();
        var problems = new List<Problem>();
        foreach (Match m in Regex.Matches(output, @"^(.*\.java):(\d+): (error|warning): (.*)$", RegexOptions.Multiline))
        {
            var line = int.Parse(m.Groups[2].Value);
            problems.Add(new Problem(m.Groups[1].Value.Trim(), line, 1, line, 1, m.Groups[4].Value.Trim(), m.Groups[3].Value, "javac"));
        }
        return new JavaBuild(r.Ok, output, outDir, pkg.Success ? pkg.Groups[1].Value + "." + cls : cls, root, problems);
    }
}
