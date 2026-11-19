using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using ValcoCode.Services;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Indentation;
using ICSharpCode.AvalonEdit.Rendering;

namespace ValcoCode.Editor;

public static class Languages
{
    public static readonly (string Id, string Name)[] All =
    {
        ("python", "Python"), ("java", "Java"), ("javascript", "JavaScript"), ("csharp", "C#"), ("html", "HTML"), ("css", "CSS"),
        ("xml", "XML"), ("json", "JSON"), ("markdown", "Markdown"), ("cpp", "C++"), ("powershell", "PowerShell"), ("plaintext", "Plain Text"),
    };

    public static string Detect(string? path)
    {
        if (path == null) return "plaintext";
        var e = Path.GetExtension(path).ToLowerInvariant();
        return e switch
        {
            ".py" or ".pyw" or ".pyi" => "python",
            ".java" => "java",
            ".js" or ".mjs" or ".cjs" or ".jsx" or ".ts" or ".tsx" => "javascript",
            ".cs" => "csharp",
            ".html" or ".htm" or ".xhtml" => "html",
            ".css" or ".scss" or ".less" => "css",
            ".xml" or ".xaml" or ".csproj" or ".svg" or ".xshd" or ".pom" or ".fxml" => "xml",
            ".json" => "json",
            ".md" or ".markdown" => "markdown",
            ".c" or ".cpp" or ".h" or ".hpp" or ".cc" => "cpp",
            ".ps1" or ".psm1" => "powershell",
            _ => "plaintext",
        };
    }

    public static string Name(string id) => All.FirstOrDefault(l => l.Id == id).Name ?? "Plain Text";

    static readonly Dictionary<string, IHighlightingDefinition?> Cache = new();

    public static IHighlightingDefinition? Highlighting(string lang)
    {
        if (Cache.TryGetValue(lang, out var d)) return d;
        d = lang switch
        {
            "python" => Load("Python"),
            "java" => Load("Java"),
            "javascript" => HighlightingManager.Instance.GetDefinition("JavaScript"),
            "csharp" => HighlightingManager.Instance.GetDefinition("C#"),
            "html" => HighlightingManager.Instance.GetDefinition("HTML"),
            "css" => HighlightingManager.Instance.GetDefinition("CSS"),
            "xml" => HighlightingManager.Instance.GetDefinition("XML"),
            "json" => HighlightingManager.Instance.GetDefinition("Json") ?? HighlightingManager.Instance.GetDefinition("JavaScript"),
            "markdown" => HighlightingManager.Instance.GetDefinition("MarkDown") ?? HighlightingManager.Instance.GetDefinition("MarkDownWithFontSize"),
            "cpp" => HighlightingManager.Instance.GetDefinition("C++"),
            "powershell" => HighlightingManager.Instance.GetDefinition("PowerShell"),
            _ => null,
        };
        Cache[lang] = d;
        if (d != null) Recolor(d, ThemeManager.Current);
        return d;
    }

    static IHighlightingDefinition? Load(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(name + ".xshd"));
        if (res == null) return null;
        using var s = asm.GetManifestResourceStream(res)!;
        using var r = XmlReader.Create(s);
        return HighlightingLoader.Load(r, HighlightingManager.Instance);
    }

    /// <summary>Applies the theme's code colours to a highlighting definition (ours by exact name, built-ins by best guess).</summary>
    public static void Recolor(IHighlightingDefinition def, Theme t)
    {
        var s = t.Syntax;
        foreach (var c in def.NamedHighlightingColors)
        {
            var n = c.Name;
            string? col = n switch
            {
                "Comment" => s.Comment, "String" => s.String, "Number" => s.Number, "Keyword" => s.Keyword, "Control" => s.Control,
                "Storage" => s.Storage, "Constant" => s.Constant, "Type" => s.Type, "Class" => s.Class, "Function" => s.Function,
                "Self" => s.Self, "Annotation" => s.Annotation, "Operator" => s.Operator, "Punct" => s.Punct,
                _ => null,
            };
            if (col == null)
            {
                var ln = n.ToLowerInvariant();
                if (ln.Contains("comment") || ln.Contains("doc")) col = s.Comment;
                else if (ln.Contains("string") || ln.Contains("char") || ln.Contains("attributevalue") || ln.Contains("value")) col = s.String;
                else if (ln.Contains("digit") || ln.Contains("number")) col = s.Number;
                else if (ln.Contains("method") || ln.Contains("function")) col = s.Function;
                else if (ln.Contains("keyword") || ln.Contains("modifier") || ln.Contains("visibility") || ln.Contains("statement") || ln.Contains("operator") && !ln.Contains("punct")) col = s.Keyword;
                else if (ln.Contains("type") || ln.Contains("class")) col = s.Type;
                else if (ln.Contains("tag") || ln.Contains("element") || ln.Contains("heading")) col = s.Class;
                else if (ln.Contains("attribute") || ln.Contains("property") || ln.Contains("field")) col = s.Function;
                else if (ln.Contains("true") || ln.Contains("null") || ln.Contains("constant") || ln.Contains("bool")) col = s.Constant;
                else if (ln.Contains("punct") || ln.Contains("bracket")) col = s.Punct;
            }
            if (col == null) continue;
            c.Foreground = new SimpleHighlightingBrush(ThemeManager.C(col));
            c.Background = null;
            if (n is "Keyword" or "Control" or "Storage") c.FontWeight = s.BoldKeywords ? FontWeights.Bold : FontWeights.Normal;
            if (n == "Comment") c.FontStyle = s.ItalicComments ? FontStyles.Italic : FontStyles.Normal;
        }
    }

    public static void RecolorAll(Theme t)
    {
        foreach (var d in Cache.Values) if (d != null) Recolor(d, t);
    }
}

// ------------------------------------------------------------------ indentation

public class SmartIndent(string lang, int tabSize) : DefaultIndentationStrategy
{
    public override void IndentLine(TextDocument document, DocumentLine line)
    {
        var prev = line.PreviousLine;
        if (prev == null) return;
        var prevText = document.GetText(prev);
        var indent = new string(prevText.TakeWhile(char.IsWhiteSpace).ToArray());
        var trimmed = prevText.TrimEnd();
        var codePart = lang == "python" ? StripPyComment(trimmed) : trimmed;
        if ((lang == "python" && codePart.EndsWith(':')) || (lang != "python" && (codePart.EndsWith('{') || codePart.EndsWith('(') || codePart.EndsWith('['))))
            indent += new string(' ', tabSize);
        else if (lang == "python" && Regex.IsMatch(codePart.Trim(), @"^(return|pass|break|continue|raise)\b") && indent.Length >= tabSize)
            indent = indent[..^tabSize];
        var cur = document.GetText(line);
        var existing = cur.TakeWhile(char.IsWhiteSpace).Count();
        document.Replace(line.Offset, existing, indent);
    }

    static string StripPyComment(string s)
    {
        var inStr = '\0';
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (inStr != '\0') { if (c == '\\') i++; else if (c == inStr) inStr = '\0'; }
            else if (c is '"' or '\'') inStr = c;
            else if (c == '#') return s[..i].TrimEnd();
        }
        return s;
    }
}

// ------------------------------------------------------------------ folding

public static class Folding
{
    public static IEnumerable<NewFolding> Braces(TextDocument doc)
    {
        var stack = new Stack<int>();
        var res = new List<NewFolding>();
        var text = doc.Text;
        bool inStr = false, inChar = false, inLine = false, inBlock = false;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var n = i + 1 < text.Length ? text[i + 1] : '\0';
            if (inLine) { if (c == '\n') inLine = false; continue; }
            if (inBlock)
            {
                if (c == '*' && n == '/') inBlock = false;
                continue;
            }
            if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
            if (inChar) { if (c == '\\') i++; else if (c == '\'') inChar = false; continue; }
            if (c == '/' && n == '/') { inLine = true; continue; }
            if (c == '/' && n == '*') { var startB = i; inBlock = true; var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal); if (end > 0 && doc.GetLineByOffset(end).LineNumber > doc.GetLineByOffset(startB).LineNumber) res.Add(new NewFolding(startB, end + 2) { Name = "/* … */" }); continue; }
            if (c == '"') { inStr = true; continue; }
            if (c == '\'') { inChar = true; continue; }
            if (c == '{') stack.Push(i);
            else if (c == '}' && stack.Count > 0)
            {
                var s = stack.Pop();
                if (doc.GetLineByOffset(s).LineNumber < doc.GetLineByOffset(i).LineNumber) res.Add(new NewFolding(s, i + 1));
            }
        }
        res.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return res;
    }

    public static IEnumerable<NewFolding> Indent(TextDocument doc)
    {
        var res = new List<NewFolding>();
        var stack = new Stack<(int indent, int offset)>();
        int lastNonBlank = 0;
        for (int ln = 1; ln <= doc.LineCount; ln++)
        {
            var line = doc.GetLineByNumber(ln);
            var t = doc.GetText(line);
            if (t.Trim().Length == 0) continue;
            var ind = t.TakeWhile(char.IsWhiteSpace).Count();
            while (stack.Count > 0 && ind <= stack.Peek().indent)
            {
                var (_, off) = stack.Pop();
                var endLine = doc.GetLineByNumber(lastNonBlank);
                if (doc.GetLineByOffset(off).LineNumber < lastNonBlank) res.Add(new NewFolding(off, endLine.EndOffset));
            }
            if (t.TrimEnd().EndsWith(':')) stack.Push((ind, line.EndOffset));
            lastNonBlank = ln;
        }
        while (stack.Count > 0)
        {
            var (_, off) = stack.Pop();
            if (doc.GetLineByOffset(off).LineNumber < lastNonBlank) res.Add(new NewFolding(off, doc.GetLineByNumber(lastNonBlank).EndOffset));
        }
        res.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return res;
    }
}

// ------------------------------------------------------------------ squiggles (problems)

public class Squiggle
{
    public int Start, Length;
    public Color Color;
    public string Message = "";
}

public class SquiggleRenderer(TextEditor editor) : IBackgroundRenderer
{
    public List<Squiggle> Items { get; } = new();
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (Items.Count == 0 || !textView.VisualLinesValid) return;
        var lines = textView.VisualLines;
        if (lines.Count == 0) return;
        int vs = lines.First().FirstDocumentLine.Offset, ve = lines.Last().LastDocumentLine.EndOffset;
        foreach (var it in Items)
        {
            var len = Math.Max(1, it.Length);
            if (it.Start > ve || it.Start + len < vs) continue;
            var seg = new TextSegment { StartOffset = Math.Min(it.Start, editor.Document.TextLength), Length = Math.Min(len, Math.Max(0, editor.Document.TextLength - it.Start)) };
            if (seg.Length == 0) continue;
            var pen = new Pen(new SolidColorBrush(it.Color), 1);
            pen.Freeze();
            foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, seg))
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    double y = r.Bottom - 1, x = r.Left;
                    ctx.BeginFigure(new Point(x, y), false, false);
                    bool up = true;
                    while (x < r.Right) { x += 2; ctx.LineTo(new Point(x, up ? y - 2 : y), true, true); up = !up; }
                }
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
        }
    }

    public Squiggle? At(int offset) => Items.FirstOrDefault(i => offset >= i.Start && offset <= i.Start + Math.Max(1, i.Length));
}

/// <summary>Highlights other occurrences of the selected word, like VS Code.</summary>
public class SelectionMatchRenderer(TextEditor editor) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Selection;
    public Brush Fill { get; set; } = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128));

    public void Draw(TextView textView, DrawingContext dc)
    {
        var sel = editor.SelectedText;
        if (sel.Length < 2 || sel.Length > 100 || sel.Contains('\n') || !Regex.IsMatch(sel, @"^\w+$") || !textView.VisualLinesValid) return;
        var lines = textView.VisualLines;
        if (lines.Count == 0) return;
        int vs = lines.First().FirstDocumentLine.Offset, ve = lines.Last().LastDocumentLine.EndOffset;
        var text = editor.Document.GetText(vs, ve - vs);
        foreach (Match m in Regex.Matches(text, @"\b" + Regex.Escape(sel) + @"\b"))
        {
            var off = vs + m.Index;
            if (off == editor.SelectionStart) continue;
            foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = off, Length = m.Length }))
                dc.DrawRectangle(Fill, null, r);
        }
    }
}

// ------------------------------------------------------------------ completion

public class CompletionItem(string text, string description, string kind, string? insert = null) : ICompletionData
{
    public System.Windows.Media.ImageSource? Image => null;
    public string Text { get; } = text;
    public object Content => new System.Windows.Controls.StackPanel
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal,
        Children =
        {
            new System.Windows.Controls.TextBlock { Text = KindIcon, Width = 18, Foreground = KindBrush, FontFamily = new FontFamily("Segoe UI Symbol") },
            new System.Windows.Controls.TextBlock { Text = Text },
            new System.Windows.Controls.TextBlock { Text = "  " + Kind, Opacity = 0.55, FontSize = 11, VerticalAlignment = VerticalAlignment.Center },
        },
    };
    public object Description { get; } = description;
    public double Priority => Kind switch { "snippet" => 2, "keyword" => 1, _ => 0 };
    public string Kind { get; } = kind;
    string KindIcon => Kind switch { "snippet" => "▭", "keyword" => "⌨", "class" => "◆", "method" => "ƒ", "constant" => "π", _ => "≡" };
    Brush KindBrush => Kind switch { "snippet" => Brushes.Gray, "keyword" => Brushes.SteelBlue, "class" => Brushes.Orange, "method" => Brushes.MediumPurple, _ => Brushes.Gray };

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs e)
    {
        var body = insert ?? Text;
        // snippets: $0 is where the cursor goes; \n lines get the current indentation
        var line = textArea.Document.GetLineByOffset(completionSegment.Offset);
        var lineText = textArea.Document.GetText(line);
        var indent = new string(lineText.TakeWhile(char.IsWhiteSpace).ToArray());
        var unit = textArea.Options.IndentationString;
        body = body.Replace("\t", unit).Replace("\n", "\n" + indent);
        var caret = body.IndexOf("$0", StringComparison.Ordinal);
        body = body.Replace("$0", "");
        textArea.Document.Replace(completionSegment, body);
        if (caret >= 0) textArea.Caret.Offset = completionSegment.Offset + caret;
    }
}

public static class Completions
{
    static readonly string[] PyKeywords = "False None True and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield match case self".Split(' ');
    static readonly string[] PyBuiltins = "print input len range int str float bool list dict set tuple open enumerate zip map filter sorted reversed sum min max abs round isinstance type super format repr chr ord any all hex bin divmod pow iter next vars dir help id hash".Split(' ');
    static readonly string[] JavaKeywords = "abstract assert boolean break byte case catch char class continue default do double else enum extends final finally float for if implements import instanceof int interface long new package private protected public return short static super switch synchronized this throw throws try void volatile while var record yield true false null".Split(' ');
    static readonly string[] JavaClasses = "String Integer Double Boolean Character Long Float Object Math System Scanner ArrayList List Map HashMap Set HashSet LinkedList Arrays Collections Optional StringBuilder Random Exception RuntimeException IllegalArgumentException IOException Thread Runnable Iterator Comparable Comparator LocalDate Files Path".Split(' ');

    static readonly (string Trigger, string Body, string Desc)[] PySnippets =
    {
        ("def", "def $0():\n\tpass", "function"),
        ("class", "class $0:\n\tdef __init__(self):\n\t\tpass", "class"),
        ("ifmain", "if __name__ == \"__main__\":\n\t$0main()", "if __name__ == \"__main__\""),
        ("main", "def main():\n\t$0pass\n\n\nif __name__ == \"__main__\":\n\tmain()", "main() + entry point"),
        ("for", "for $0 in :\n\tpass", "for loop"),
        ("forr", "for i in range($0):\n\tpass", "for i in range()"),
        ("while", "while $0:\n\tpass", "while loop"),
        ("try", "try:\n\t$0pass\nexcept Exception as e:\n\tprint(e)", "try/except"),
        ("withopen", "with open(\"$0\", \"r\") as f:\n\tdata = f.read()", "open a file"),
        ("print", "print($0)", "print()"),
        ("input", "input(\"$0\")", "input()"),
    };
    static readonly (string Trigger, string Body, string Desc)[] JavaSnippets =
    {
        ("main", "public static void main(String[] args) {\n\t$0\n}", "public static void main"),
        ("psvm", "public static void main(String[] args) {\n\t$0\n}", "public static void main"),
        ("sout", "System.out.println($0);", "System.out.println"),
        ("souf", "System.out.printf(\"$0%n\");", "System.out.printf"),
        ("serr", "System.err.println($0);", "System.err.println"),
        ("fori", "for (int i = 0; i < $0; i++) {\n\t\n}", "indexed for loop"),
        ("foreach", "for (String item : $0) {\n\t\n}", "enhanced for loop"),
        ("if", "if ($0) {\n\t\n}", "if block"),
        ("while", "while ($0) {\n\t\n}", "while loop"),
        ("try", "try {\n\t$0\n} catch (Exception e) {\n\te.printStackTrace();\n}", "try/catch"),
        ("class", "public class $0 {\n\t\n}", "class"),
        ("scanner", "Scanner in = new Scanner(System.in);$0", "new Scanner(System.in)"),
        ("switch", "switch ($0) {\n\tcase 1 -> {}\n\tdefault -> {}\n}", "switch"),
    };

    static readonly Dictionary<string, string[]> JavaMembers = new()
    {
        ["System.out"] = new[] { "println", "print", "printf", "flush" },
        ["System"] = new[] { "out", "err", "in", "currentTimeMillis", "nanoTime", "exit", "getProperty", "lineSeparator", "arraycopy" },
        ["Math"] = new[] { "abs", "max", "min", "pow", "sqrt", "round", "floor", "ceil", "random", "PI", "E", "sin", "cos", "tan", "log", "log10", "hypot", "floorMod" },
        ["Arrays"] = new[] { "toString", "sort", "asList", "fill", "copyOf", "stream", "equals", "binarySearch" },
        ["Collections"] = new[] { "sort", "reverse", "shuffle", "max", "min", "unmodifiableList", "emptyList", "frequency" },
        ["Integer"] = new[] { "parseInt", "valueOf", "MAX_VALUE", "MIN_VALUE", "toString", "compare", "sum" },
        ["Double"] = new[] { "parseDouble", "valueOf", "MAX_VALUE", "MIN_VALUE", "compare", "isNaN" },
        ["String"] = new[] { "valueOf", "format", "join" },
        ["List"] = new[] { "of", "copyOf" },
    };
    static readonly string[] StringMethods = "length charAt substring indexOf lastIndexOf contains equals equalsIgnoreCase startsWith endsWith toUpperCase toLowerCase trim strip split replace isEmpty isBlank compareTo repeat matches toCharArray".Split(' ');
    static readonly string[] ScannerMethods = "nextLine nextInt nextDouble next hasNext hasNextInt hasNextLine nextBoolean nextLong close".Split(' ');
    static readonly string[] CollectionMethods = "add get set remove size isEmpty contains indexOf clear forEach stream sort addAll put getOrDefault containsKey keySet values entrySet".Split(' ');
    static readonly string[] PyStrMethods = "upper lower strip split join replace startswith endswith find format title isdigit isalpha count".Split(' ');
    static readonly string[] PyListMethods = "append extend insert pop remove sort reverse index count copy clear".Split(' ');
    static readonly string[] PyDictMethods = "keys values items get pop update setdefault clear copy".Split(' ');

    /// <summary>Completions for the word being typed (or members after a dot).</summary>
    public static List<CompletionItem> For(string lang, TextDocument doc, int caret, out int wordStart)
    {
        var text = doc.Text;
        wordStart = caret;
        while (wordStart > 0 && (char.IsLetterOrDigit(text[wordStart - 1]) || text[wordStart - 1] is '_' or '$')) wordStart--;
        var prefix = text[wordStart..caret];
        var list = new List<CompletionItem>();
        if (wordStart > 0 && text[wordStart - 1] == '.')
        {
            var ts = wordStart - 1;
            while (ts > 0 && (char.IsLetterOrDigit(text[ts - 1]) || text[ts - 1] is '_' or '.' or '$')) ts--;
            var target = text[ts..(wordStart - 1)];
            string[]? names = null;
            if (lang == "java")
            {
                if (!JavaMembers.TryGetValue(target, out names))
                {
                    var decl = Regex.Match(text, @"([A-Z][\w<>,\s\[\]]*?)\s+" + Regex.Escape(target) + @"\s*[=;:,)]");
                    var type = decl.Success ? decl.Groups[1].Value.Trim() : "";
                    if (type.StartsWith("String")) names = StringMethods;
                    else if (type.StartsWith("Scanner")) names = ScannerMethods;
                    else if (Regex.IsMatch(type, @"^(Array)?List|^LinkedList|^(Hash|Tree)?Map|^(Hash|Tree)?Set")) names = CollectionMethods;
                    else if (type.EndsWith("[]")) names = new[] { "length", "clone" };
                }
            }
            else if (lang == "python")
            {
                var assign = Regex.Match(text, @"\b" + Regex.Escape(target) + @"\s*=\s*(.)");
                var first = assign.Success ? assign.Groups[1].Value : "";
                names = first switch { "\"" or "'" => PyStrMethods, "[" => PyListMethods, "{" => PyDictMethods, _ => null };
                if (target == "self") names = Regex.Matches(text, @"self\.(\w+)\s*=").Select(m => m.Groups[1].Value)
                    .Concat(Regex.Matches(text, @"def\s+(\w+)\s*\(\s*self").Select(m => m.Groups[1].Value)).Distinct().ToArray();
            }
            if (names != null)
                foreach (var n in names)
                    list.Add(new CompletionItem(n, target + "." + n, Regex.IsMatch(n, "^[A-Z_]+$") ? "constant" : "method"));
            return list;
        }
        if (prefix.Length == 0) return list;
        var seen = new HashSet<string>();
        void Add(CompletionItem c) { if (seen.Add(c.Text + c.Kind)) list.Add(c); }
        if (lang == "python")
        {
            foreach (var s in PySnippets) Add(new CompletionItem(s.Trigger, s.Desc, "snippet", s.Body));
            foreach (var k in PyKeywords) Add(new CompletionItem(k, "keyword", "keyword"));
            foreach (var b in PyBuiltins) Add(new CompletionItem(b, "built-in function", "method"));
        }
        else if (lang == "java")
        {
            foreach (var s in JavaSnippets) Add(new CompletionItem(s.Trigger, s.Desc, "snippet", s.Body));
            foreach (var k in JavaKeywords) Add(new CompletionItem(k, "keyword", "keyword"));
            foreach (var c in JavaClasses) Add(new CompletionItem(c, "class", "class"));
        }
        // words already in the document
        foreach (Match m in Regex.Matches(text, @"[A-Za-z_$][\w$]{2,}"))
        {
            if (m.Index == wordStart) continue;
            Add(new CompletionItem(m.Value, "word in this file", "text"));
        }
        return list.Where(c => c.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && c.Text != prefix).Take(200).ToList();
    }
}

// ------------------------------------------------------------------ line commands

public static class EditCommands
{
    public static void ToggleComment(TextEditor ed, string lang)
    {
        var token = lang switch { "python" or "powershell" => "#", "plaintext" or "markdown" => null, "html" or "xml" => null, _ => "//" };
        if (token == null) return;
        var doc = ed.Document;
        var start = doc.GetLineByOffset(ed.SelectionStart);
        var endOff = ed.SelectionLength > 0 ? ed.SelectionStart + ed.SelectionLength : ed.SelectionStart;
        var end = doc.GetLineByOffset(endOff);
        if (ed.SelectionLength > 0 && end.Offset == endOff && end.LineNumber > start.LineNumber) end = end.PreviousLine;
        var lines = Enumerable.Range(start.LineNumber, end.LineNumber - start.LineNumber + 1).Select(doc.GetLineByNumber).ToList();
        var nonBlank = lines.Where(l => doc.GetText(l).Trim().Length > 0).ToList();
        if (nonBlank.Count == 0) return;
        var allCommented = nonBlank.All(l => doc.GetText(l).TrimStart().StartsWith(token));
        var minIndent = nonBlank.Min(l => doc.GetText(l).TakeWhile(char.IsWhiteSpace).Count());
        doc.BeginUpdate();
        foreach (var l in Enumerable.Reverse(nonBlank))
        {
            var t = doc.GetText(l);
            if (allCommented)
            {
                var i = t.IndexOf(token, StringComparison.Ordinal);
                var len = token.Length + (i + token.Length < t.Length && t[i + token.Length] == ' ' ? 1 : 0);
                doc.Remove(l.Offset + i, len);
            }
            else doc.Insert(l.Offset + minIndent, token + " ");
        }
        doc.EndUpdate();
    }

    public static void MoveLines(TextEditor ed, int dir, bool copy)
    {
        var doc = ed.Document;
        var first = doc.GetLineByOffset(ed.SelectionStart);
        var last = doc.GetLineByOffset(ed.SelectionStart + ed.SelectionLength);
        if (ed.SelectionLength > 0 && last.Offset == ed.SelectionStart + ed.SelectionLength && last.LineNumber > first.LineNumber) last = last.PreviousLine;
        var nl = TextUtilities.GetNewLineFromDocument(doc, 1);
        var block = doc.GetText(first.Offset, last.EndOffset - first.Offset);
        var caretCol = ed.TextArea.Caret.Column;
        var caretLineDelta = ed.TextArea.Caret.Line - first.LineNumber;
        doc.BeginUpdate();
        if (copy)
        {
            if (dir > 0) { doc.Insert(last.EndOffset, nl + block); ed.TextArea.Caret.Line = last.LineNumber + 1 + caretLineDelta; }
            else { doc.Insert(first.Offset, block + nl); ed.TextArea.Caret.Line = first.LineNumber + caretLineDelta; }
        }
        else if (dir < 0 && first.PreviousLine != null)
        {
            var prev = first.PreviousLine;
            var prevText = doc.GetText(prev);
            doc.Replace(prev.Offset, last.EndOffset - prev.Offset, block + nl + prevText);
            ed.TextArea.Caret.Line = prev.LineNumber + caretLineDelta;
        }
        else if (dir > 0 && last.NextLine != null)
        {
            var next = last.NextLine;
            var nextText = doc.GetText(next);
            doc.Replace(first.Offset, next.EndOffset - first.Offset, nextText + nl + block);
            ed.TextArea.Caret.Line = first.LineNumber + 1 + caretLineDelta;
        }
        doc.EndUpdate();
        ed.TextArea.Caret.Column = caretCol;
        ed.TextArea.ClearSelection();
    }

    public static void DuplicateOrSelectWord(TextEditor ed)
    {
        // Ctrl+D: select the word under the cursor, or jump to the next match of the selection
        if (ed.SelectionLength == 0)
        {
            var off = ed.CaretOffset;
            var s = TextUtilities.GetNextCaretPosition(ed.Document, Math.Min(off + 1, ed.Document.TextLength), System.Windows.Documents.LogicalDirection.Backward, CaretPositioningMode.WordStart);
            var e = TextUtilities.GetNextCaretPosition(ed.Document, Math.Max(s, 0), System.Windows.Documents.LogicalDirection.Forward, CaretPositioningMode.WordBorder);
            if (s >= 0 && e > s) ed.Select(s, e - s);
            return;
        }
        var sel = ed.SelectedText;
        var from = ed.SelectionStart + ed.SelectionLength;
        var idx = ed.Text.IndexOf(sel, from, StringComparison.Ordinal);
        if (idx < 0) idx = ed.Text.IndexOf(sel, 0, StringComparison.Ordinal);
        if (idx >= 0) { ed.Select(idx, sel.Length); ed.ScrollTo(ed.Document.GetLineByOffset(idx).LineNumber, 0); }
    }
}
