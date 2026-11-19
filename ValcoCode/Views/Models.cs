using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using ValcoCode.Editor;
using ValcoCode.Services;

namespace ValcoCode.Views;

public abstract class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    protected bool Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(f, v)) return false;
        f = v;
        Raise(n);
        return true;
    }
}

public static class FileIcons
{
    static readonly Dictionary<string, (string, string)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".py"] = ("py", "#3d7cc9"), [".java"] = ("J", "#e76f00"), [".class"] = ("J", "#9a6a3a"), [".jar"] = ("jar", "#9a6a3a"),
        [".js"] = ("JS", "#d8b800"), [".ts"] = ("TS", "#3178c6"), [".json"] = ("{}", "#cbcb41"), [".html"] = ("<>", "#e44d26"),
        [".htm"] = ("<>", "#e44d26"), [".css"] = ("#", "#563d7c"), [".md"] = ("M↓", "#519aba"), [".txt"] = ("≡", "#8a8a8a"),
        [".xml"] = ("<>", "#e37933"), [".yml"] = ("Y", "#cb171e"), [".yaml"] = ("Y", "#cb171e"), [".cs"] = ("C#", "#68217a"),
        [".png"] = ("▣", "#a074c4"), [".jpg"] = ("▣", "#a074c4"), [".gif"] = ("▣", "#a074c4"), [".svg"] = ("▣", "#ffb13b"),
        [".sql"] = ("DB", "#c08c3c"), [".csv"] = ("≣", "#89e051"), [".bat"] = (">", "#c1f12e"), [".ps1"] = (">", "#3178c6"),
        [".gitignore"] = ("◆", "#f14e32"), [".exe"] = ("▶", "#8a8a8a"), [".zip"] = ("Z", "#c9a227"),
    };

    public static (string Text, Brush Brush) For(string name)
    {
        if (name.Equals(".gitignore", StringComparison.OrdinalIgnoreCase)) return ("◆", B("#f14e32"));
        if (name.Equals("README.md", StringComparison.OrdinalIgnoreCase)) return ("ⓘ", B("#519aba"));
        return Map.TryGetValue(Path.GetExtension(name), out var v) ? (v.Item1, B(v.Item2)) : ("•", B("#8a8a8a"));
    }

    static readonly Dictionary<string, Brush> cache = new();
    public static Brush B(string hex)
    {
        if (!cache.TryGetValue(hex, out var b)) { b = new SolidColorBrush(ThemeManager.C(hex)); b.Freeze(); cache[hex] = b; }
        return b;
    }

    public static Brush GitBrush(string? letter) => letter switch
    {
        null or "" => (Brush)Application.Current.Resources["FgBrush"],
        "M" or "R" or "C" or "T" => (Brush)Application.Current.Resources["GitModBrush"],
        "U" or "A" or "dot-new" => (Brush)Application.Current.Resources["GitNewBrush"],
        "D" or "!" => (Brush)Application.Current.Resources["GitDelBrush"],
        _ => (Brush)Application.Current.Resources["GitModBrush"],
    };
}

public class FileNode : Bindable
{
    public string Name { get; private set; }
    public string FullPath { get; private set; }
    public bool IsDir { get; }
    public int Depth { get; }
    public FileNode? Parent { get; }
    public ObservableCollection<FileNode> Children { get; } = new();
    public Thickness Indent => new(Depth * 12, 0, 0, 0);
    public static readonly FileNode Placeholder = new("…", "", false, 0, null);
    bool loaded;
    public Func<FileNode, Task>? Loader;

    public FileNode(string name, string path, bool isDir, int depth, FileNode? parent)
    {
        Name = name; FullPath = path; IsDir = isDir; Depth = depth; Parent = parent;
        if (isDir) Children.Add(Placeholder);
    }

    bool expanded;
    public bool IsExpanded
    {
        get => expanded;
        set
        {
            if (!Set(ref expanded, value)) return;
            Raise(nameof(Icon));
            if (value && !loaded) { loaded = true; _ = Loader?.Invoke(this); }
        }
    }
    public bool Loaded { get => loaded; set => loaded = value; }

    public string Icon => IsDir ? (expanded ? "" : "") : FileIcons.For(Name).Text;
    public Brush IconBrush => IsDir ? FileIcons.B("#dcb67a") : FileIcons.For(Name).Brush;
    public FontFamily IconFont => IsDir ? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") : new FontFamily("Segoe UI Semibold, Segoe UI Symbol");
    public double IconSize => IsDir ? 14 : 10.5;

    string? badge;
    public string? Badge { get => badge; set { if (Set(ref badge, value)) { Raise(nameof(NameBrush)); Raise(nameof(BadgeVisibility)); Raise(nameof(Decorations)); } } }
    public Visibility BadgeVisibility => string.IsNullOrEmpty(badge) || badge.StartsWith("dot") ? Visibility.Collapsed : Visibility.Visible;
    public Brush NameBrush => FileIcons.GitBrush(badge);
    public TextDecorationCollection? Decorations => badge == "D" ? TextDecorations.Strikethrough : null;
    public void RefreshTheme() { Raise(nameof(NameBrush)); }

    public void Rename(string newPath)
    {
        FullPath = newPath;
        Name = Path.GetFileName(newPath);
        Raise(nameof(Name)); Raise(nameof(FullPath)); Raise(nameof(Icon)); Raise(nameof(IconBrush));
    }
}

public class SearchNode : Bindable
{
    public string Before { get; init; } = "";
    public string Match { get; init; } = "";
    public string After { get; init; } = "";
    public string Dir { get; init; } = "";
    public string Path { get; init; } = "";
    public int Line { get; init; }
    public int Col { get; init; }
    public int Len { get; init; }
    public bool IsFile { get; init; }
    public Thickness Indent => new(IsFile ? 0 : 20, 0, 0, 0);
    public ObservableCollection<SearchNode> Children { get; } = new();
    public int Count => Children.Count;
    public Visibility CountVisibility => IsFile ? Visibility.Visible : Visibility.Collapsed;
    public string Icon => IsFile ? FileIcons.For(Path).Text : "";
    public Brush IconBrush => FileIcons.For(Path).Brush;
    public string Tip => IsFile ? Path : $"Line {Line}";
    bool expanded = true;
    public bool IsExpanded { get => expanded; set => Set(ref expanded, value); }
}

public class GitRow
{
    public required GitFile File { get; init; }
    public required string Top { get; init; }
    public string Path => File.Path;
    public string Name => File.Name;
    public string DirText => File.Dir.Length > 0 ? "  " + File.Dir : "";
    public string Letter => File.Letter;
    public Brush LetterBrush => FileIcons.GitBrush(Letter);
    public string Icon => FileIcons.For(File.Name).Text;
    public Brush IconBrush => FileIcons.For(File.Name).Brush;
    public string StageGlyph => File.Staged ? "" : "";
    public string StageTip => File.Staged ? "Unstage Changes" : "Stage Changes";
    public Visibility DiscardVisibility => File.Staged ? Visibility.Collapsed : Visibility.Visible;
    public Visibility OpenVisibility => Letter == "D" ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ActionsVisibility => Visibility.Visible;
    public TextDecorationCollection? Decorations => Letter == "D" ? TextDecorations.Strikethrough : null;
    public string FullPath => System.IO.Path.Combine(Top, File.Path.Replace('/', '\\'));
}

public class CommitRow(Commit c)
{
    public string Hash => c.Hash;
    public string Short => c.Short;
    public string Author => c.Author;
    public string Date => c.Date;
    public string Subject => c.Subject;
    public string RefsText => string.IsNullOrEmpty(c.Refs) ? "" : "  " + c.Refs.Replace("HEAD -> ", "");
}

public class ProblemRow
{
    public string Text { get; init; } = "";
    public string Where { get; init; } = "";
    public string Icon { get; init; } = "";
    public Brush IconBrush { get; init; } = Brushes.Gray;
    public FontWeight Weight { get; init; } = FontWeights.Normal;
    public Thickness Indent { get; init; }
    public Problem? Problem { get; init; }
    public string? FilePath { get; init; }
}

public class PickItem
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string? Description { get; init; }
    public string DescriptionText => string.IsNullOrEmpty(Description) ? "" : "   " + Description;
    public string? Detail { get; init; }
    public Visibility DetailVisibility => string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;
    public string? Hint { get; init; }
    public string Icon { get; init; } = "";
    public Brush? IconBrush { get; init; }
    public object? Data { get; init; }
}

public enum TabKind { File, Diff, Welcome, Settings, Shortcuts }

public class DocTab
{
    public TabKind Kind;
    public string? Path;
    public string Title = "";
    public string Lang = "plaintext";
    public bool Dirty;
    public string SavedText = "";
    public CodeEditor? Code;
    public FrameworkElement? View;
    public FrameworkElement Content => (FrameworkElement?)Code ?? View!;
    public FrameworkElement? Header;
    public DateTime DiskTime;
    public bool Untitled => Kind == TabKind.File && Path == null;
}
