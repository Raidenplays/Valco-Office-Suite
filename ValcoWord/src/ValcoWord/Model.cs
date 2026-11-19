using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace ValcoWord
{
    /// <summary>Per-document state that isn't part of the text flow.</summary>
    public class DocExtras
    {
        public string Theme = "Office";
        public string StyleSet = "Word 2007";
        public List<Source> Sources = new List<Source>();
        public string BibStyle = "APA";
        public bool MarkedFinal, Protected;
        public string ProtectHash;
        public List<string> MergeFields = new List<string>();
        public List<Dictionary<string, string>> MergeRecords = new List<Dictionary<string, string>>();
        public HashSet<int> MergeExcluded = new HashSet<int>();
        public string MergeSource;
        public string MergeType;
        public int MergeIndex;
        public bool MergePreview;
        public bool TrackChanges;
        public Dictionary<string, string> Custom = new Dictionary<string, string>();
    }

    public class Source
    {
        public string Tag = "", Type = "Book", Author = "", Title = "", Year = "", Publisher = "", City = "", Url = "", Journal = "", Pages = "";
        public string FirstAuthorLast => (Author ?? "").Split(';')[0].Split(',')[0].Trim();
        public string Cite(string style) => style == "MLA" ? $"({FirstAuthorLast})" : style == "Chicago" ? $"({FirstAuthorLast} {Year})" : $"({FirstAuthorLast}, {Year})";
        public string Reference(string style)
        {
            string a = Author?.Replace(";", ",") ?? "";
            switch (style)
            {
                case "MLA": return $"{a}. {Title}. {(City != "" ? City + ": " : "")}{Publisher}{(Publisher != "" ? ", " : "")}{Year}." + (Url != "" ? " " + Url : "");
                case "Chicago": return $"{a}. {Year}. {Title}. {(City != "" ? City + ": " : "")}{Publisher}." + (Url != "" ? " " + Url : "");
                default: return $"{a}. ({Year}). {Title}. {(City != "" ? City + ": " : "")}{Publisher}." + (Url != "" ? " Retrieved from " + Url : "");
            }
        }
    }

    public class CommentInfo
    {
        public int Id; public string Author = "", Initials = ""; public DateTime Date = DateTime.Now; public string Text = "";
    }

    public class RevInfo
    {
        public bool Insert; public string Author = ""; public DateTime Date = DateTime.Now;
        public static readonly Color InsColor = Color.FromRgb(0xC0, 0x00, 0x00);
        public static readonly Color DelColor = Color.FromRgb(0xC0, 0x00, 0x00);
    }

    public class NoteRef { public bool Endnote; public int Number; }
    public class NoteBody { public bool Endnote; public int Number; }
    public class BookmarkInfo { public string Name; }
    public class FieldInfo { public string Code; }
    public class MergeFieldInfo { public string Name; }
    public class CitationRef { public string SourceTag; }
    public class IndexEntry { public string Term; public string Kind = "XE"; public string Category; }

    /// <summary>Tags for generated sections (table of contents etc.).</summary>
    public static class SectionKind
    {
        public const string TOC = "TOC", Index = "Index", Bibliography = "Bibliography", TOA = "TOA", Notes = "Notes", Figures = "Figures";
        public static string Of(Section s) => s.Tag as string;
    }

    public static class Spans
    {
        public static IEnumerable<Span> All(FlowDocument doc) => Fmt.AllParagraphs(doc.Blocks).SelectMany(p => Fmt.Descendants(p)).OfType<Span>();
        public static IEnumerable<T> Tagged<T>(FlowDocument doc) where T : class => All(doc).Select(s => s.Tag as T).Where(t => t != null);
        public static IEnumerable<Span> WithTag<T>(FlowDocument doc) where T : class => All(doc).Where(s => s.Tag is T);

        /// <summary>Removes a span but keeps its contents in place.</summary>
        public static void Unwrap(Span s)
        {
            var kids = s.Inlines.ToList();
            foreach (var k in kids) { s.Inlines.Remove(k); s.SiblingInlines.InsertBefore(s, k); }
            s.SiblingInlines.Remove(s);
        }

        public static string Text(TextElement e) => new TextRange(e.ContentStart, e.ContentEnd).Text;
    }
}
