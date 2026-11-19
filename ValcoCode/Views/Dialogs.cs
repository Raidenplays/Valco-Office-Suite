using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ValcoCode.Services;

namespace ValcoCode.Views;

/// <summary>A small themed dialog window.</summary>
public class ThemedDialog : VistaWindow
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(18, 14, 18, 16) };
    protected readonly StackPanel Buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };

    public ThemedDialog(Window? owner, string title, double width = 440)
    {
        Owner = owner;
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "PanelBgBrush");
        SetResourceReference(ForegroundProperty, "FgBrush");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;
        Content = new StackPanel { Children = { Body } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; } };
    }

    protected void Finish() => Body.Children.Add(Buttons);

    protected Button AddButton(string text, bool primary, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0), MinWidth = 80, IsDefault = primary };
        if (primary) b.Style = (Style)Application.Current.Resources["PrimaryButton"];
        b.Click += click;
        Buttons.Children.Add(b);
        return b;
    }

    protected TextBlock Text(string s, bool muted = false, double size = 13)
    {
        var t = new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), FontSize = size };
        if (muted) t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Body.Children.Add(t);
        return t;
    }

    protected TextBlock Error()
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        t.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        Body.Children.Add(t);
        return t;
    }

    protected static void ShowError(TextBlock t, string msg) { t.Text = msg; t.Visibility = Visibility.Visible; }
}

public class PromptDialog : ThemedDialog
{
    readonly TextBox box = new();
    public string? Result;
    public PromptDialog(Window owner, string title, string label, string initial) : base(owner, title)
    {
        Text(label);
        box.Text = initial;
        Body.Children.Add(box);
        AddButton("Cancel", false, (_, _) => DialogResult = false);
        AddButton("OK", true, (_, _) => { Result = box.Text; DialogResult = true; });
        Finish();
        Loaded += (_, _) =>
        {
            box.Focus();
            var dot = initial.LastIndexOf('.');
            if (dot > 0) box.Select(0, dot); else box.SelectAll();
        };
    }
}

public class SignInDialog : ThemedDialog
{
    public GitHubUser? User;
    public SignInDialog(Window owner, GitHubUser? current) : base(owner, current == null ? "Sign in to GitHub" : "GitHub account", 480)
    {
        if (current != null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            try
            {
                row.Children.Add(new Image { Source = new BitmapImage(new Uri(current.AvatarUrl)), Width = 48, Height = 48, Margin = new Thickness(0, 0, 12, 0) });
            }
            catch { }
            row.Children.Add(new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children = { new TextBlock { Text = current.Name ?? current.Login, FontWeight = FontWeights.SemiBold, FontSize = 15 }, new TextBlock { Text = "@" + current.Login, Opacity = 0.7 } },
            });
            Body.Children.Add(row);
            Text("Valco Code uses this account to push, pull, clone private repositories and publish new ones.", true);
            AddButton("Sign Out", false, (_, _) => { TokenStore.Clear(); User = null; DialogResult = true; });
            AddButton("Done", true, (_, _) => { User = current; DialogResult = false; });
            Finish();
            return;
        }
        Text("1.  Open GitHub's token page. The right boxes (repo, workflow, user) are already ticked. Pick an expiry, then click Generate token at the bottom.");
        var open = new Button { Content = "Open GitHub's token page ↗", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(18, 0, 0, 12) };
        open.Click += (_, _) => Shell.Open(GitHub.TokenUrl);
        Body.Children.Add(open);
        Text("2.  Paste the token here:");
        var box = new PasswordBox { Padding = new Thickness(4, 3, 4, 3) };
        box.SetResourceReference(BackgroundProperty, "InputBrush");
        box.SetResourceReference(ForegroundProperty, "FgBrush");
        box.SetResourceReference(BorderBrushProperty, "InputBorderBrush");
        Body.Children.Add(box);
        var err = Error();
        Text("The token is encrypted with your Windows account and stored only on this PC. Already signed in to Git another way (Git Credential Manager)? Pushing works without this.", true, 11.5)
            .Margin = new Thickness(0, 10, 0, 0);
        AddButton("Cancel", false, (_, _) => DialogResult = false);
        Button? ok = null;
        ok = AddButton("Sign In", true, async (_, _) =>
        {
            var tok = box.Password.Trim();
            if (tok.Length == 0) return;
            ok!.IsEnabled = false;
            ok.Content = "Checking…";
            try
            {
                User = await GitHub.User(tok);
                TokenStore.Set(tok);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                ShowError(err, ex.Message);
                ok.IsEnabled = true;
                ok.Content = "Sign In";
            }
        });
        Finish();
        Loaded += (_, _) => box.Focus();
    }
}

public class PublishDialog : ThemedDialog
{
    public string? Url;
    public PublishDialog(Window owner, string root, GitHubUser user, Action<string> log) : base(owner, "Publish to GitHub", 480)
    {
        Text("Repository name");
        var name = new TextBox { Text = System.Text.RegularExpressions.Regex.Replace(Path.GetFileName(root.TrimEnd('\\')), @"[^\w.-]+", "-") };
        var prefix = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var who = new TextBlock { Text = user.Login + " / ", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        DockPanel.SetDock(who, Dock.Left);
        prefix.Children.Add(who);
        prefix.Children.Add(name);
        Body.Children.Add(prefix);
        Text("Description (optional)");
        var desc = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        Body.Children.Add(desc);
        var priv = new RadioButton { Content = "🔒  Private", IsChecked = true, GroupName = "vis" };
        var pub = new RadioButton { Content = "🌐  Public", GroupName = "vis" };
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { priv, pub }, Margin = new Thickness(0, 0, 0, 10) });
        Text("This commits everything in the folder (respecting .gitignore), creates the repository and pushes it.", true, 11.5);
        var status = Text("", true);
        status.Visibility = Visibility.Collapsed;
        var err = Error();
        AddButton("Cancel", false, (_, _) => DialogResult = false);
        Button? go = null;
        go = AddButton("Publish", true, async (_, _) =>
        {
            go!.IsEnabled = false;
            err.Visibility = Visibility.Collapsed;
            status.Visibility = Visibility.Visible;
            try
            {
                var tok = TokenStore.Get() ?? throw new Exception("Not signed in.");
                status.Text = "Preparing the local repository…";
                var st = await Git.Status(root);
                var top = st.IsRepo ? st.Top : root;
                if (!st.IsRepo)
                {
                    var r = await Git.Raw(root, new[] { "init", "-b", "main" });
                    if (!r.Ok) await Git.Run(root, "init");
                    st = await Git.Status(root);
                }
                if (st.RemoteUrl != null) throw new Exception("This folder already has a remote: " + st.RemoteUrl);
                await Git.EnsureIdentity(top, user);
                if (st.Count > 0 || !st.HasCommits)
                {
                    status.Text = "Committing your files…";
                    await Git.Run(top, "add", "-A");
                    var after = await Git.Status(top);
                    if (after.Staged.Count > 0) await Git.Run(top, "commit", "-m", st.HasCommits ? "Update" : "Initial commit");
                    else if (!st.HasCommits) await Git.Run(top, "commit", "--allow-empty", "-m", "Initial commit");
                }
                status.Text = $"Creating {user.Login}/{name.Text.Trim()} on GitHub…";
                var repo = await GitHub.CreateRepo(tok, name.Text.Trim(), priv.IsChecked == true, desc.Text.Trim());
                log("Created " + repo.HtmlUrl);
                await Git.Run(top, "remote", "add", "origin", repo.CloneUrl);
                var branch = (await Git.Run(top, "rev-parse", "--abbrev-ref", "HEAD")).Trim();
                status.Text = "Pushing…";
                await Git.Run(top, true, "push", "-u", "origin", branch);
                Url = repo.HtmlUrl;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                ShowError(err, ex.Message);
                status.Visibility = Visibility.Collapsed;
                go.IsEnabled = true;
            }
        });
        Finish();
        Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
    }
}

public class CloneDialog : ThemedDialog
{
    public string? ClonedPath;
    public CloneDialog(Window owner, GitHubUser? user, Action signIn) : base(owner, "Clone Repository", 560)
    {
        Text("Repository URL, or owner/name:");
        var url = new TextBox();
        Body.Children.Add(url);
        var status = Text("", true);
        status.Visibility = Visibility.Collapsed;
        status.Margin = new Thickness(0, 8, 0, 0);
        var err = Error();
        var list = new ListBox { Height = 260, Margin = new Thickness(0, 6, 0, 0) };
        list.SetResourceReference(BorderBrushProperty, "LineBrush");
        list.BorderThickness = new Thickness(1);
        var filter = new TextBox { Tag = "Filter your repositories…", Margin = new Thickness(0, 12, 0, 0) };
        List<GitHubRepo> repos = new();

        async Task Clone(string u)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(u, @"^[\w.-]+/[\w.-]+$")) u = $"https://github.com/{u}.git";
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where to put the cloned folder" };
            if (dlg.ShowDialog(this) != true) return;
            var parent = dlg.FolderName;
            var nm = Path.GetFileNameWithoutExtension(u.TrimEnd('/'));
            err.Visibility = Visibility.Collapsed;
            status.Text = $"Cloning {nm}…";
            status.Visibility = Visibility.Visible;
            IsEnabled = false;
            try
            {
                await Git.Run(parent, true, "clone", u, nm);
                ClonedPath = Path.Combine(parent, nm);
                DialogResult = true;
            }
            catch (Exception ex) { ShowError(err, Git.FriendlyError(ex.Message)); status.Visibility = Visibility.Collapsed; }
            finally { IsEnabled = true; }
        }

        if (user != null)
        {
            Body.Children.Add(filter);
            Body.Children.Add(list);
            filter.TextChanged += (_, _) => list.ItemsSource = repos.Where(r => r.FullName.Contains(filter.Text, StringComparison.OrdinalIgnoreCase))
                .Select(r => new PickItem { Label = r.FullName, Detail = r.Description, Icon = r.Private ? "🔒" : "📖", Data = r }).ToList();
            list.ItemTemplate = (DataTemplate)Application.Current.Resources["PickTemplate"];
            list.MouseDoubleClick += async (_, _) => { if (list.SelectedItem is PickItem p && p.Data is GitHubRepo r) await Clone(r.CloneUrl); };
            Loaded += async (_, _) =>
            {
                try
                {
                    var tok = TokenStore.Get();
                    if (tok == null) return;
                    repos = await GitHub.Repos(tok);
                    list.ItemsSource = repos.Select(r => new PickItem { Label = r.FullName, Detail = r.Description, Icon = r.Private ? "🔒" : "📖", Data = r }).ToList();
                }
                catch (Exception ex) { ShowError(err, ex.Message); }
            };
            Text("Double-click one of your repositories, or paste a URL above.", true, 11.5).Margin = new Thickness(0, 6, 0, 0);
        }
        else
        {
            var link = new Button { Content = "Sign in to GitHub to pick from your repositories and clone private ones", Style = (Style)Application.Current.Resources["LinkButton"], Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            link.Click += (_, _) => { DialogResult = false; signIn(); };
            Body.Children.Add(link);
        }
        AddButton("Cancel", false, (_, _) => DialogResult = false);
        AddButton("Clone…", true, async (_, _) =>
        {
            var u = url.Text.Trim();
            if (u.Length == 0 && list.SelectedItem is PickItem p && p.Data is GitHubRepo r) u = r.CloneUrl;
            if (u.Length > 0) await Clone(u);
        });
        Finish();
        Loaded += (_, _) => url.Focus();
    }
}

public class AboutDialog : ThemedDialog
{
    public AboutDialog(Window owner) : base(owner, "About Valco Code", 460)
    {
        BitmapImage Img(string f) => new(new Uri("pack://application:,,,/Assets/" + f));
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var logo = new Image { Source = Img("logo.png"), Width = 64, Height = 64, Margin = new Thickness(0, 0, 14, 0) };
        DockPanel.SetDock(logo, Dock.Left);
        top.Children.Add(logo);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = "Valco Code", FontSize = 24, FontWeight = FontWeights.Light });
        var ver = new TextBlock { Text = "Version " + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0") };
        ver.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        names.Children.Add(ver);
        top.Children.Add(names);
        Body.Children.Add(top);

        var suite = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 12) };
        suite.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        suite.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        var sp = new DockPanel();
        var badge = new Image { Source = Img("ValcoOffice-256.png"), Width = 44, Height = 44, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(badge, Dock.Left);
        sp.Children.Add(badge);
        var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        st.Children.Add(new TextBlock { Text = "Part of Valco Office", FontWeight = FontWeights.SemiBold });
        var apps = new TextBlock { Text = "Valco Word · Valco Express · Valco Code · Valco Shell", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        apps.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        st.Children.Add(apps);
        sp.Children.Add(st);
        suite.Child = sp;
        Body.Children.Add(suite);

        Text("A code editor for Windows, from the Valco suite.", false, 12.5);
        Text("Python and Java editing, formatting, running and Git. MIT licensed.", true, 12);
        Text("© 2026 ItsRaiden", true, 12);
        Text("Built with AvalonEdit (MIT). Formatting by Ruff (MIT) and google-java-format (Apache 2.0).", true, 12);
        AddButton("OK", true, (_, _) => DialogResult = true);
        Finish();
    }
}
