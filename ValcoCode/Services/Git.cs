using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ValcoCode.Services;

public class GitFile
{
    public required string Path { get; init; } // repo-relative, forward slashes
    public string? Orig { get; init; }
    public char Index { get; init; }
    public char Work { get; init; }
    public bool Staged { get; init; }
    public string Letter => (Staged ? Index : Work) switch { '?' => "U", var c => c.ToString() };
    public string Name => System.IO.Path.GetFileName(Path);
    public string Dir { get { var d = System.IO.Path.GetDirectoryName(Path.Replace('/', '\\')) ?? ""; return d; } }
}

public class GitStatus
{
    public bool IsRepo;
    public string Top = "";
    public string Branch = "";
    public string? Upstream;
    public int Ahead, Behind;
    public bool HasCommits;
    public string? RemoteUrl;
    public List<GitFile> Staged = new(), Changes = new(), Conflicts = new();
    public int Count => Staged.Count + Changes.Count + Conflicts.Count;
}

public record Commit(string Hash, string Short, string Author, string Date, string Subject, string Refs);

public class GitException(string msg) : Exception(msg);

public static class Git
{
    public static string? Exe;

    public static async Task<ProcResult> Raw(string cwd, IEnumerable<string> args, bool auth = false, string? stdin = null)
    {
        if (Exe == null) throw new GitException("Git isn't installed. Get it from git-scm.com, then restart Valco Code.");
        var all = new List<string>();
        var env = new Dictionary<string, string> { ["GIT_TERMINAL_PROMPT"] = "0", ["LC_ALL"] = "C" };
        if (auth)
        {
            var tok = TokenStore.Get();
            if (tok != null)
            {
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + tok));
                all.AddRange(new[] { "-c", $"http.https://github.com/.extraheader=AUTHORIZATION: basic {basic}", "-c", "credential.helper=" });
                env["GCM_INTERACTIVE"] = "never";
            }
        }
        all.AddRange(args);
        return await Proc.Run(Exe, all, cwd, stdin, env, timeoutMs: 300_000);
    }

    public static async Task<string> Run(string cwd, params string[] args) => await Run(cwd, false, args);

    public static async Task<string> Run(string cwd, bool auth, params string[] args)
    {
        var r = await Raw(cwd, args, auth);
        if (!r.Ok) throw new GitException(string.IsNullOrWhiteSpace(r.Message) ? $"git {args.FirstOrDefault()} failed" : r.Message);
        return r.Stdout;
    }

    public static async Task<GitStatus> Status(string root)
    {
        var st = new GitStatus { Top = root };
        if (Exe == null) return st;
        var top = await Raw(root, new[] { "rev-parse", "--show-toplevel" });
        if (!top.Ok || string.IsNullOrWhiteSpace(top.Stdout)) return st;
        st.IsRepo = true;
        st.Top = top.Stdout.Trim().Replace('/', '\\');
        var outp = await Run(st.Top, "status", "--porcelain=v1", "-b", "-z", "--untracked-files=all");
        var parts = outp.Split('\0');
        for (int i = 0; i < parts.Length; i++)
        {
            var rec = parts[i];
            if (rec.Length == 0) continue;
            if (rec.StartsWith("## "))
            {
                var h = rec[3..];
                if (h.StartsWith("No commits yet on ")) { st.Branch = h["No commits yet on ".Length..]; continue; }
                st.HasCommits = true;
                var br = h;
                var bracket = h.IndexOf(" [", StringComparison.Ordinal);
                if (bracket >= 0)
                {
                    var info = h[(bracket + 2)..].TrimEnd(']');
                    br = h[..bracket];
                    foreach (var part in info.Split(", "))
                    {
                        if (part.StartsWith("ahead ")) int.TryParse(part[6..], out st.Ahead);
                        if (part.StartsWith("behind ")) int.TryParse(part[7..], out st.Behind);
                    }
                }
                var dots = br.IndexOf("...", StringComparison.Ordinal);
                if (dots >= 0) { st.Upstream = br[(dots + 3)..]; br = br[..dots]; }
                st.Branch = br.StartsWith("HEAD (no branch)") ? "detached" : br;
                continue;
            }
            char X = rec[0], Y = rec[1];
            var path = rec[3..];
            string? orig = null;
            if (X is 'R' or 'C') orig = parts[++i];
            var conflict = X == 'U' || Y == 'U' || (X == 'A' && Y == 'A') || (X == 'D' && Y == 'D');
            if (conflict) st.Conflicts.Add(new GitFile { Path = path, Orig = orig, Index = X, Work = Y });
            else
            {
                if (X != ' ' && X != '?') st.Staged.Add(new GitFile { Path = path, Orig = orig, Index = X, Work = Y, Staged = true });
                if (Y != ' ') st.Changes.Add(new GitFile { Path = path, Orig = orig, Index = X, Work = Y });
            }
        }
        var remote = await Raw(st.Top, new[] { "remote", "get-url", "origin" });
        st.RemoteUrl = remote.Ok ? remote.Stdout.Trim() : null;
        return st;
    }

    public static async Task<List<Commit>> Log(string top, int n = 200)
    {
        var o = await Run(top, "log", $"-n{n}", "--date=relative", "--pretty=format:%H%x1f%h%x1f%an%x1f%ad%x1f%s%x1f%D%x1e");
        return o.Split('\x1e', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).Where(r => r.Length > 0).Select(r =>
        {
            var f = r.Split('\x1f');
            return new Commit(f[0], f[1], f[2], f[3], f[4], f.Length > 5 ? f[5] : "");
        }).ToList();
    }

    public static async Task<(List<string> Local, List<string> Remote, string Current)> Branches(string top)
    {
        var o = await Run(top, "for-each-ref", "--format=%(HEAD)%09%(refname)", "refs/heads", "refs/remotes");
        var local = new List<string>(); var remote = new List<string>(); var cur = "";
        foreach (var l in o.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = l.Split('\t');
            if (p.Length < 2) continue;
            if (p[1].StartsWith("refs/heads/")) { var n = p[1][11..]; local.Add(n); if (p[0] == "*") cur = n; }
            else if (p[1].StartsWith("refs/remotes/") && !p[1].EndsWith("/HEAD")) remote.Add(p[1][13..]);
        }
        return (local, remote, cur);
    }

    /// <summary>Makes sure commits have a name and email; fills them in from GitHub when signed in.</summary>
    public static async Task<bool> EnsureIdentity(string top, GitHubUser? user)
    {
        var name = (await Raw(top, new[] { "config", "user.name" })).Stdout.Trim();
        var email = (await Raw(top, new[] { "config", "user.email" })).Stdout.Trim();
        if (name.Length > 0 && email.Length > 0) return true;
        if (user == null) return false;
        await Run(top, "config", "user.name", user.Name ?? user.Login);
        await Run(top, "config", "user.email", $"{user.Id}+{user.Login}@users.noreply.github.com");
        return true;
    }

    public static string? WebUrl(string? remote)
    {
        if (remote == null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(remote, @"github\.com[:/](.+?)(?:\.git)?$");
        return m.Success ? "https://github.com/" + m.Groups[1].Value : null;
    }

    public static string FriendlyError(string msg)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(msg, "Authentication failed|could not read Username|terminal prompts disabled|403|Permission denied", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return "GitHub needs you to sign in before pushing or pulling. " + msg.Split('\n')[0];
        if (msg.Contains("non-fast-forward") || msg.Contains("fetch first"))
            return "GitHub has commits you don't have yet. Pull first, then push.";
        if (msg.Contains("Please tell me who you are"))
            return "Git doesn't know your name and email yet. Sign in to GitHub, or set them in Source Control.";
        return msg.Split('\n').FirstOrDefault(l => l.StartsWith("fatal:") || l.StartsWith("error:")) ?? msg.Split('\n')[0];
    }
}

public record GitHubUser(string Login, string? Name, long Id, string AvatarUrl);
public record GitHubRepo(string FullName, string CloneUrl, string HtmlUrl, bool Private, string? Description);

public static class GitHub
{
    public const string TokenUrl = "https://github.com/settings/tokens/new?scopes=repo,workflow,read:user,user:email&description=Valco%20Code";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    static HttpRequestMessage Req(HttpMethod m, string url, string token)
    {
        var r = new HttpRequestMessage(m, url);
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        r.Headers.UserAgent.Add(new ProductInfoHeaderValue("ValcoCode", "0.1"));
        return r;
    }

    public static async Task<GitHubUser> User(string token)
    {
        using var res = await Http.SendAsync(Req(HttpMethod.Get, "https://api.github.com/user", token));
        if ((int)res.StatusCode == 401) throw new Exception("GitHub rejected that token. It may be wrong or expired.");
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var e = doc.RootElement;
        return new GitHubUser(e.GetProperty("login").GetString()!, e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null,
            e.GetProperty("id").GetInt64(), e.GetProperty("avatar_url").GetString() ?? "");
    }

    public static async Task<GitHubRepo> CreateRepo(string token, string name, bool priv, string description)
    {
        var req = Req(HttpMethod.Post, "https://api.github.com/user/repos", token);
        req.Content = new StringContent(JsonSerializer.Serialize(new { name, @private = priv, description, auto_init = false }), Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var e = doc.RootElement;
        if (!res.IsSuccessStatusCode)
        {
            var why = e.TryGetProperty("errors", out var errs) && errs.GetArrayLength() > 0 && errs[0].TryGetProperty("message", out var m) ? m.GetString()
                : e.TryGetProperty("message", out var m2) ? m2.GetString() : res.ReasonPhrase;
            throw new Exception("Couldn't create the repository: " + why);
        }
        return new GitHubRepo(e.GetProperty("full_name").GetString()!, e.GetProperty("clone_url").GetString()!, e.GetProperty("html_url").GetString()!, priv, description);
    }

    public static async Task<List<GitHubRepo>> Repos(string token)
    {
        using var res = await Http.SendAsync(Req(HttpMethod.Get, "https://api.github.com/user/repos?per_page=100&sort=updated", token));
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.EnumerateArray().Select(e => new GitHubRepo(
            e.GetProperty("full_name").GetString()!, e.GetProperty("clone_url").GetString()!, e.GetProperty("html_url").GetString()!,
            e.GetProperty("private").GetBoolean(), e.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null)).ToList();
    }
}
