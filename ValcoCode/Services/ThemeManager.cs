using System.Windows;
using System.Windows.Media;

namespace ValcoCode.Services;

/// <summary>Colours for code. Names match the xshd colour names.</summary>
public record Syntax(
    string Keyword, string Control, string Storage, string Type, string Class, string Function,
    string Variable, string Param, string Property, string String, string Number, string Constant,
    string Comment, string Operator, string Punct, string Annotation, string Self,
    bool BoldKeywords = false, bool ItalicComments = false);

public class Theme
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool Dark { get; init; }
    /// <summary>Brush key → colour or gradient ("#rrggbb", "#aarrggbb", or "v:#top,#mid@0.5,#bottom" for vertical gradients).</summary>
    public required Dictionary<string, string> Brushes { get; init; }
    public required Syntax Syntax { get; init; }
    /// <summary>Title bar colour on Windows 11 (null = system).</summary>
    public string? Caption { get; init; }
    public string? CaptionText { get; init; }
    public double Radius { get; init; } = 4;
}

public static class ThemeManager
{
    public static event Action? Changed;
    public static Theme Current { get; private set; } = null!;

    static Dictionary<string, string> Base(Dictionary<string, string> d) => d;

    // ---- Valco Office colour schemes (Blue is the suite default; see valco-office-branding.md)
    public static readonly Theme ValcoBlue = new()
    {
        Id = "valco-blue", Name = "Valco Blue", Dark = false, Radius = 3,
        Brushes = Base(new()
        {
            ["Hover"] = "v:#FFFDF0,#FFEDB3@0.4,#FFD660@0.4,#FFE7A0",
            ["HoverBorder"] = "#DBCE99",
            ["Sel"] = "v:#FFE9B5,#FFD47A@0.4,#FFB940@0.4,#FFD88A",
            ["SelBorder"] = "#C28F3A",
            ["SelFg"] = "#000000",
            ["MenuHover"] = "v:#FFFDF0,#FFEDB3@0.4,#FFD660@0.4,#FFE7A0",
            ["MenuHoverFg"] = "#000000",
            ["ButtonHover"] = "v:#FFFDF0,#FFEDB3@0.4,#FFD660@0.4,#FFE7A0",
            ["Accent"] = "v:#FFF6D2,#FFE08A@0.45,#FFCB45@0.45,#FFE39A",
            ["AccentHover"] = "v:#FFFBE6,#FFEAA8@0.45,#FFD86A@0.45,#FFEDB8",
            ["AccentFg"] = "#3D2B00",
            ["TabAccent"] = "#F2B63A",
            ["Accent2"] = "#D9822B",
            ["Danger"] = "#C42B1C",
            ["Ok"] = "#2E8B3A",
            ["Warn"] = "#C77C0E",
            ["GitMod"] = "#B06A00",
            ["GitNew"] = "#2E8B3A",
            ["GitDel"] = "#C42B1C",
            ["Shadow"] = "#000000",
            ["Bg"] = "#FFFFFF",
            ["Panel"] = "v:#F4F8FD,#E6EEF9",
            ["Panel2"] = "#E3ECF8",
            ["Activity"] = "h:#DCE8F7,#CADBF1",
            ["ActivityFg"] = "#6A86AE",
            ["ActivityActive"] = "#15428B",
            ["MenuBar"] = "v:#DCE8F7,#CADBF1",
            ["MenuBarFg"] = "#15428B",
            ["TabsBg"] = "v:#DCE8F7,#CADBF1",
            ["TabActive"] = "#FFFFFF",
            ["TabInactive"] = "v:#EEF4FC,#D9E6F6",
            ["TabInactiveFg"] = "#15428B",
            ["TabActiveFg"] = "#000000",
            ["Line"] = "#A5BFE0",
            ["Fg"] = "#1B1B1B",
            ["Muted"] = "#4F6A8F",
            ["Input"] = "#FFFFFF",
            ["InputBorder"] = "#A5BFE0",
            ["StatusBg"] = "v:#DBE7F6,#B2CAEA",
            ["StatusFg"] = "#15428B",
            ["PanelBg"] = "#FFFFFF",
            ["MenuBg"] = "#FAFCFF",
            ["MenuFg"] = "#000000",
            ["Scroll"] = "v:#E3ECF8,#C5D7EF",
            ["ScrollHover"] = "v:#FFFDF0,#FFEDB3@0.4,#FFD660@0.4,#FFE7A0",
            ["ButtonBg"] = "v:#F7FAFE,#E3ECF9@0.45,#CFDEF4@0.45,#DCE7F7",
            ["ButtonFg"] = "#15428B",
            ["ButtonBorder"] = "#8DB2E3",
            ["EditorBg"] = "#FFFFFF",
            ["EditorFg"] = "#000000",
            ["Gutter"] = "#FFFFFF",
            ["GutterFg"] = "#2B91AF",
            ["ActiveLine"] = "#EEF5FD",
            ["Selection"] = "#ADD6FF",
            ["Caret"] = "#000000",
            ["TermBg"] = "#FFFFFF",
            ["TermFg"] = "#1B1B1B",
            ["Badge"] = "#15428B",
            ["BadgeFg"] = "#FFFFFF",
            ["SideHeadFg"] = "#15428B",
        }),
        Syntax = new("#0000ff", "#0000ff", "#0000ff", "#2b91af", "#2b91af", "#000000", "#000000", "#000000", "#000000",
            "#a31515", "#000000", "#0000ff", "#008000", "#000000", "#000000", "#808080", "#0000ff"),
    };

    public static readonly Theme ValcoSilver = new()
    {
        Id = "valco-silver", Name = "Valco Silver", Dark = false, Radius = 3,
        Brushes = Base(new()
        {
            ["Hover"] = "v:#FFFDF0,#FFEDB3@0.4,#FFD660@0.4,#FFE7A0",
            ["HoverBorder"] = "#DBCE99",
            ["Sel"] = "v:#FFE9B5,#FFD47A@0.4,#FFB940@0.4,#FFD88A",
            ["SelBorder"] = "#C28F3A",
            ["SelFg"] = "#000000",
            ["MenuHover"] = "v:#FFFDF0,#FFEDB3@0.4,#FFD660@0.4,#FFE7A0",
            ["MenuHoverFg"] = "#000000",
            ["ButtonHover"] = "v:#FFFDF0,#FFEDB3@0.4,#FFD660@0.4,#FFE7A0",
            ["Accent"] = "v:#FFF6D2,#FFE08A@0.45,#FFCB45@0.45,#FFE39A",
            ["AccentHover"] = "v:#FFFBE6,#FFEAA8@0.45,#FFD86A@0.45,#FFEDB8",
            ["AccentFg"] = "#3D2B00",
            ["TabAccent"] = "#F2B63A",
            ["Accent2"] = "#D9822B",
            ["Danger"] = "#C42B1C",
            ["Ok"] = "#2E8B3A",
            ["Warn"] = "#C77C0E",
            ["GitMod"] = "#B06A00",
            ["GitNew"] = "#2E8B3A",
            ["GitDel"] = "#C42B1C",
            ["Shadow"] = "#000000",
            ["Bg"] = "#FFFFFF",
            ["Panel"] = "v:#F8F9FB,#ECEFF3",
            ["Panel2"] = "#E9ECF0",
            ["Activity"] = "h:#EEF1F5,#DDE2E9",
            ["ActivityFg"] = "#7A828D",
            ["ActivityActive"] = "#4C535C",
            ["MenuBar"] = "v:#EEF1F5,#DDE2E9",
            ["MenuBarFg"] = "#4C535C",
            ["TabsBg"] = "v:#EEF1F5,#DDE2E9",
            ["TabActive"] = "#FFFFFF",
            ["TabInactive"] = "v:#F5F7F9,#E1E5EA",
            ["TabInactiveFg"] = "#4C535C",
            ["TabActiveFg"] = "#000000",
            ["Line"] = "#B9C0C9",
            ["Fg"] = "#1B1B1B",
            ["Muted"] = "#6A717B",
            ["Input"] = "#FFFFFF",
            ["InputBorder"] = "#B9C0C9",
            ["StatusBg"] = "v:#EEF1F4,#D2D7DE",
            ["StatusFg"] = "#4C535C",
            ["PanelBg"] = "#FFFFFF",
            ["MenuBg"] = "#FBFBFC",
            ["MenuFg"] = "#000000",
            ["Scroll"] = "v:#EBEEF2,#D3D8DF",
            ["ScrollHover"] = "v:#FFFDF0,#FFEDB3@0.4,#FFD660@0.4,#FFE7A0",
            ["ButtonBg"] = "v:#FBFBFC,#EDEFF2@0.45,#DDE1E6@0.45,#E7EAEE",
            ["ButtonFg"] = "#2E343B",
            ["ButtonBorder"] = "#A7AEB8",
            ["EditorBg"] = "#FFFFFF",
            ["EditorFg"] = "#000000",
            ["Gutter"] = "#FFFFFF",
            ["GutterFg"] = "#2B91AF",
            ["ActiveLine"] = "#F2F4F7",
            ["Selection"] = "#ADD6FF",
            ["Caret"] = "#000000",
            ["TermBg"] = "#FFFFFF",
            ["TermFg"] = "#1B1B1B",
            ["Badge"] = "#4C535C",
            ["BadgeFg"] = "#FFFFFF",
            ["SideHeadFg"] = "#4C535C",
        }),
        Syntax = new("#0000ff", "#0000ff", "#0000ff", "#2b91af", "#2b91af", "#000000", "#000000", "#000000", "#000000",
            "#a31515", "#000000", "#0000ff", "#008000", "#000000", "#000000", "#808080", "#0000ff"),
    };

    public static readonly Theme ValcoBlack = new()
    {
        Id = "valco-black", Name = "Valco Black", Dark = true, Radius = 3,
        Brushes = Base(new()
        {
            ["Hover"] = "v:#666666,#575757",
            ["HoverBorder"] = "#7A7A7A",
            ["Sel"] = "v:#FFE9B5,#FFD47A@0.4,#FFB940@0.4,#FFD88A",
            ["SelBorder"] = "#C28F3A",
            ["SelFg"] = "#000000",
            ["MenuHover"] = "v:#FFFDF0,#FFEDB3@0.4,#FFD660@0.4,#FFE7A0",
            ["MenuHoverFg"] = "#000000",
            ["ButtonHover"] = "v:#7A7A7A,#666666@0.45,#535353@0.45,#5E5E5E",
            ["Accent"] = "v:#FFF6D2,#FFE08A@0.45,#FFCB45@0.45,#FFE39A",
            ["AccentHover"] = "v:#FFFBE6,#FFEAA8@0.45,#FFD86A@0.45,#FFEDB8",
            ["AccentFg"] = "#3D2B00",
            ["TabAccent"] = "#F2B63A",
            ["Accent2"] = "#D9822B",
            ["Danger"] = "#C42B1C",
            ["Ok"] = "#2E8B3A",
            ["Warn"] = "#C77C0E",
            ["GitMod"] = "#B06A00",
            ["GitNew"] = "#2E8B3A",
            ["GitDel"] = "#C42B1C",
            ["Shadow"] = "#000000",
            ["Bg"] = "#2B2B2B",
            ["Panel"] = "v:#4A4A4A,#3D3D3D",
            ["Panel2"] = "#555555",
            ["Activity"] = "h:#535353,#3B3B3B",
            ["ActivityFg"] = "#B8B8B8",
            ["ActivityActive"] = "#FFFFFF",
            ["MenuBar"] = "v:#535353,#3B3B3B",
            ["MenuBarFg"] = "#FFFFFF",
            ["TabsBg"] = "v:#555555,#3F3F3F",
            ["TabActive"] = "#2B2B2B",
            ["TabInactive"] = "v:#5E5E5E,#4A4A4A",
            ["TabInactiveFg"] = "#E6E6E6",
            ["TabActiveFg"] = "#FFFFFF",
            ["Line"] = "#262626",
            ["Fg"] = "#F0F0F0",
            ["Muted"] = "#B0B0B0",
            ["Input"] = "#3A3A3A",
            ["InputBorder"] = "#6A6A6A",
            ["StatusBg"] = "v:#4E4E4E,#2F2F2F",
            ["StatusFg"] = "#FFFFFF",
            ["PanelBg"] = "#333333",
            ["MenuBg"] = "#F5F5F5",
            ["MenuFg"] = "#000000",
            ["Scroll"] = "v:#6A6A6A,#565656",
            ["ScrollHover"] = "v:#FFFDF0,#FFEDB3@0.4,#FFD660@0.4,#FFE7A0",
            ["ButtonBg"] = "v:#6E6E6E,#5A5A5A@0.45,#474747@0.45,#525252",
            ["ButtonFg"] = "#FFFFFF",
            ["ButtonBorder"] = "#2A2A2A",
            ["EditorBg"] = "#2B2B2B",
            ["EditorFg"] = "#E8E8E8",
            ["Gutter"] = "#2B2B2B",
            ["GutterFg"] = "#8C8C8C",
            ["ActiveLine"] = "#353535",
            ["Selection"] = "#264F78",
            ["Caret"] = "#FFD660",
            ["TermBg"] = "#1E1E1E",
            ["TermFg"] = "#E8E8E8",
            ["Badge"] = "#FFD660",
            ["BadgeFg"] = "#000000",
            ["SideHeadFg"] = "#FFFFFF",
        }),
        Syntax = new("#cc7832", "#cc7832", "#cc7832", "#ffc66d", "#ffc66d", "#ffc66d", "#e8e8e8", "#e8e8e8", "#9876aa",
            "#6a8759", "#6897bb", "#cc7832", "#808080", "#e8e8e8", "#cccccc", "#bbb529", "#94558d"),
    };

    public static readonly Theme WeaveCraft = new()
    {
        Id = "weavecraft", Name = "WeaveCraft Dark", Dark = true, Caption = "#1a1c21", CaptionText = "#c9ced8", Radius = 5,
        Brushes = Base(new()
        {
            ["Bg"] = "#1b1d23", ["Panel"] = "#24272e", ["Panel2"] = "#2c3038", ["Activity"] = "#1a1c21", ["ActivityFg"] = "#7d838f",
            ["ActivityActive"] = "#e6e9ef", ["MenuBar"] = "#1a1c21", ["MenuBarFg"] = "#c9ced8", ["TabsBg"] = "#1a1c21",
            ["TabActive"] = "#1b1d23", ["TabInactive"] = "#1f2127", ["TabInactiveFg"] = "#8b919c", ["TabActiveFg"] = "#e6e9ef", ["TabAccent"] = "#36c2b4",
            ["Line"] = "#2f333c", ["Fg"] = "#d7dae0", ["Muted"] = "#8b919c", ["Accent"] = "#36c2b4", ["AccentFg"] = "#0e1b1d",
            ["AccentHover"] = "#45d3c5", ["Accent2"] = "#e8b04a", ["Danger"] = "#e5534b", ["Ok"] = "#57c27a", ["Warn"] = "#e8b04a",
            ["Sel"] = "#2a4a4f", ["SelBorder"] = "#2a4a4f", ["SelFg"] = "#ffffff", ["Hover"] = "#2c3038", ["HoverBorder"] = "#2c3038",
            ["Input"] = "#1a1c22", ["InputBorder"] = "#3a3f4a", ["StatusBg"] = "#22252c", ["StatusFg"] = "#aab0bb",
            ["PanelBg"] = "#1d1f25", ["MenuBg"] = "#2a2d35", ["MenuFg"] = "#d7dae0", ["MenuHover"] = "#36c2b4", ["MenuHoverFg"] = "#0e1b1d",
            ["Scroll"] = "#4a8c96a5", ["ScrollHover"] = "#808c96a5",
            ["GitMod"] = "#e8b04a", ["GitNew"] = "#57c27a", ["GitDel"] = "#e5534b",
            ["ButtonBg"] = "#2c3038", ["ButtonFg"] = "#d7dae0", ["ButtonBorder"] = "#3a3f4a", ["ButtonHover"] = "#343844",
            ["EditorBg"] = "#1b1d23", ["EditorFg"] = "#d7dae0", ["Gutter"] = "#1b1d23", ["GutterFg"] = "#565c68", ["ActiveLine"] = "#22252d",
            ["Selection"] = "#2e4a52", ["Caret"] = "#36c2b4", ["TermBg"] = "#1d1f25", ["TermFg"] = "#d7dae0", ["Badge"] = "#36c2b4", ["BadgeFg"] = "#0e1b1d",
            ["SideHeadFg"] = "#aab0bb", ["Shadow"] = "#000000",
        }),
        Syntax = new("#c678dd", "#c678dd", "#c678dd", "#e5c07b", "#e5c07b", "#61afef", "#d7dae0", "#e8b9a0", "#e06c75",
            "#98c379", "#d19a66", "#d19a66", "#6b7380", "#56b6c2", "#abb2bf", "#36c2b4", "#e06c75"),
    };

    // Windows Vista: Explorer's dark-blue glass toolbar, glossy two-tone buttons, rounded light-blue
    // highlights, and Visual Studio 2008 code colours on white.
    public static readonly Theme Vista = new()
    {
        Id = "vista", Name = "Windows Vista", Dark = false, Caption = "#1d3a5c", CaptionText = "#ffffff", Radius = 3,
        Brushes = Base(new()
        {
            ["Bg"] = "#ffffff", ["Panel"] = "v:#fbfdff,#eef4fb", ["Panel2"] = "#e9f0f8",
            ["Activity"] = "h:#1c3a5c,#0f243b", ["ActivityFg"] = "#9fb9d6", ["ActivityActive"] = "#ffffff",
            ["MenuBar"] = "v:#3a6491,#23466e@0.48,#0f2a47@0.52,#1b3c62", ["MenuBarFg"] = "#ffffff",
            ["TabsBg"] = "v:#e6eef8,#cfdcec", ["TabActive"] = "#ffffff", ["TabInactive"] = "v:#f6f9fd,#e0e9f4@0.5,#d3e0ef@0.5,#dfe9f5",
            ["TabInactiveFg"] = "#1e3957", ["TabActiveFg"] = "#000000", ["TabAccent"] = "#ffb340",
            ["Line"] = "#a9bdd4", ["Fg"] = "#1b1b1b", ["Muted"] = "#4f6680", ["Accent"] = "v:#7cc1f2,#3d95dd@0.5,#2a7fcc@0.5,#4aa0e4",
            ["AccentFg"] = "#ffffff", ["AccentHover"] = "v:#a2d6fb,#5aaeee@0.5,#3a92de@0.5,#64b6f2", ["Accent2"] = "#d9822b",
            ["Danger"] = "#c42b1c", ["Ok"] = "#2e8b3a", ["Warn"] = "#c77c0e",
            ["Sel"] = "v:#ebf4fd,#c1dcfc", ["SelBorder"] = "#7da2ce", ["SelFg"] = "#000000",
            ["Hover"] = "v:#fafbfd,#e6f0fa", ["HoverBorder"] = "#b8d6fb",
            ["Input"] = "#ffffff", ["InputBorder"] = "#abadb3", ["StatusBg"] = "v:#f3f7fb,#dfe8f2@0.5,#d3dfec@0.5,#dce6f1", ["StatusFg"] = "#1e3957",
            ["PanelBg"] = "#ffffff", ["MenuBg"] = "#f5f5f5", ["MenuFg"] = "#000000", ["MenuHover"] = "v:#f2f8fe,#d6e9fd", ["MenuHoverFg"] = "#000000",
            ["Scroll"] = "v:#f3f3f3,#d6d6d6", ["ScrollHover"] = "v:#e3f4fc,#a9dbf6",
            ["GitMod"] = "#b06a00", ["GitNew"] = "#2e8b3a", ["GitDel"] = "#c42b1c",
            ["ButtonBg"] = "v:#f2f2f2,#ebebeb@0.5,#dddddd@0.5,#cfcfcf", ["ButtonFg"] = "#000000", ["ButtonBorder"] = "#707070",
            ["ButtonHover"] = "v:#eaf6fd,#d9f0fc@0.5,#bee6fd@0.5,#a7d9f5",
            ["EditorBg"] = "#ffffff", ["EditorFg"] = "#000000", ["Gutter"] = "#ffffff", ["GutterFg"] = "#2b91af", ["ActiveLine"] = "#eef6ff",
            ["Selection"] = "#add6ff", ["Caret"] = "#000000", ["TermBg"] = "#0c0c0c", ["TermFg"] = "#c0c0c0", ["Badge"] = "v:#7cc1f2,#2a7fcc", ["BadgeFg"] = "#ffffff",
            ["SideHeadFg"] = "#1e3957", ["Shadow"] = "#000000",
        }),
        Syntax = new("#0000ff", "#0000ff", "#0000ff", "#2b91af", "#2b91af", "#000000", "#000000", "#000000", "#000000",
            "#a31515", "#000000", "#0000ff", "#008000", "#000000", "#000000", "#808080", "#0000ff"),
    };

    // Dreamweaver CS6: the light-grey workspace, blue list highlights and Dreamweaver's code colours.
    public static readonly Theme Dreamweaver = new()
    {
        Id = "dw-cs6", Name = "Dreamweaver CS6", Dark = false, Caption = "#d6d6d6", CaptionText = "#1e1e1e", Radius = 2,
        Brushes = Base(new()
        {
            ["Bg"] = "#ffffff", ["Panel"] = "#e4e4e4", ["Panel2"] = "#d4d4d4", ["Activity"] = "#cfcfcf", ["ActivityFg"] = "#666666",
            ["ActivityActive"] = "#1e1e1e", ["MenuBar"] = "v:#ebebeb,#d6d6d6", ["MenuBarFg"] = "#1e1e1e", ["TabsBg"] = "#c4c4c4",
            ["TabActive"] = "#ffffff", ["TabInactive"] = "v:#dcdcdc,#c8c8c8", ["TabInactiveFg"] = "#333333", ["TabActiveFg"] = "#000000", ["TabAccent"] = "#3875d7",
            ["Line"] = "#a8a8a8", ["Fg"] = "#1e1e1e", ["Muted"] = "#5e5e5e", ["Accent"] = "#3875d7", ["AccentFg"] = "#ffffff",
            ["AccentHover"] = "#4b86e3", ["Accent2"] = "#c86a00", ["Danger"] = "#cc0000", ["Ok"] = "#2a7d2a", ["Warn"] = "#b36b00",
            ["Sel"] = "#3875d7", ["SelBorder"] = "#3875d7", ["SelFg"] = "#ffffff", ["Hover"] = "#d8d8d8", ["HoverBorder"] = "#d8d8d8",
            ["Input"] = "#ffffff", ["InputBorder"] = "#8e8e8e", ["StatusBg"] = "#d6d6d6", ["StatusFg"] = "#2b2b2b",
            ["PanelBg"] = "#f0f0f0", ["MenuBg"] = "#f0f0f0", ["MenuFg"] = "#000000", ["MenuHover"] = "#3875d7", ["MenuHoverFg"] = "#ffffff",
            ["Scroll"] = "#9a9a9a", ["ScrollHover"] = "#7a7a7a",
            ["GitMod"] = "#a05a00", ["GitNew"] = "#2a7d2a", ["GitDel"] = "#cc0000",
            ["ButtonBg"] = "v:#fafafa,#dedede", ["ButtonFg"] = "#1e1e1e", ["ButtonBorder"] = "#8e8e8e", ["ButtonHover"] = "v:#ffffff,#e9e9e9",
            ["EditorBg"] = "#ffffff", ["EditorFg"] = "#000000", ["Gutter"] = "#efefef", ["GutterFg"] = "#888888", ["ActiveLine"] = "#f4f7fb",
            ["Selection"] = "#b5d5ff", ["Caret"] = "#000000", ["TermBg"] = "#ffffff", ["TermFg"] = "#1e1e1e", ["Badge"] = "#3875d7", ["BadgeFg"] = "#ffffff",
            ["SideHeadFg"] = "#1e1e1e", ["Shadow"] = "#000000",
        }),
        Syntax = new("#000099", "#000099", "#000099", "#990099", "#990099", "#009999", "#000000", "#000000", "#000000",
            "#0000ff", "#ff0000", "#ff0000", "#999999", "#000000", "#000000", "#996600", "#000099", BoldKeywords: true),
    };

    // 2077: colours taken from Endormi's "2077 theme" v1.5.3 for VS Code.
    public static readonly Theme Cyber2077 = new()
    {
        Id = "cyber2077", Name = "2077", Dark = true, Caption = "#0b082e", CaptionText = "#ff2592", Radius = 2,
        Brushes = Base(new()
        {
            ["Bg"] = "#030d22", ["Panel"] = "#030d22", ["Panel2"] = "#0d0931", ["Activity"] = "#030d22", ["ActivityFg"] = "#3e8efd",
            ["ActivityActive"] = "#ff2592", ["MenuBar"] = "#0b082e", ["MenuBarFg"] = "#fdfeff", ["TabsBg"] = "#030d22",
            ["TabActive"] = "#1d1641", ["TabInactive"] = "#141138", ["TabInactiveFg"] = "#3e8efd", ["TabActiveFg"] = "#fdfeff", ["TabAccent"] = "#ee0077",
            ["Line"] = "#181657", ["Fg"] = "#fdfeff", ["Muted"] = "#4d8bee", ["Accent"] = "#ee0077", ["AccentFg"] = "#ffffff",
            ["AccentHover"] = "#ff2e97", ["Accent2"] = "#ffd400", ["Danger"] = "#ee1682", ["Ok"] = "#06ad00", ["Warn"] = "#ffd400",
            ["Sel"] = "#073170", ["SelBorder"] = "#073170", ["SelFg"] = "#ffffff", ["Hover"] = "#0a2f66", ["HoverBorder"] = "#0a2f66",
            ["Input"] = "#0d0931", ["InputBorder"] = "#2a2470", ["StatusBg"] = "#0a0631", ["StatusFg"] = "#4d8bee",
            ["PanelBg"] = "#0e0952", ["MenuBg"] = "#04112c", ["MenuFg"] = "#fdfeff", ["MenuHover"] = "#073170", ["MenuHoverFg"] = "#ffffff",
            ["Scroll"] = "#54fc3096", ["ScrollHover"] = "#99fc3096",
            ["GitMod"] = "#fdd81d", ["GitNew"] = "#0097fc", ["GitDel"] = "#ee1682",
            ["ButtonBg"] = "#087eb4", ["ButtonFg"] = "#ffffff", ["ButtonBorder"] = "#087eb4", ["ButtonHover"] = "#0a95d4",
            ["EditorBg"] = "#030d22", ["EditorFg"] = "#fdfeff", ["Gutter"] = "#030d22", ["GutterFg"] = "#ee0077", ["ActiveLine"] = "#1c1347",
            ["Selection"] = "#310072", ["Caret"] = "#ee0077", ["TermBg"] = "#0e0952", ["TermFg"] = "#e4eeff", ["Badge"] = "#ee0077", ["BadgeFg"] = "#ffffff",
            ["SideHeadFg"] = "#ff2592", ["Shadow"] = "#ee0077",
        }),
        Syntax = new("#ff2cf1", "#ff2cf1", "#40a9ff", "#ff1bf0", "#ff2e97", "#39c0ff", "#fdfeff", "#ff2e97", "#40a9ff",
            "#0ef3ff", "#ffd400", "#ff2e97", "#0098df", "#fdfeff", "#ffee80", "#a83dff", "#ff2e97", ItalicComments: true),
    };

    public static readonly Theme[] All = { ValcoBlue, ValcoSilver, ValcoBlack, WeaveCraft, Vista, Dreamweaver, Cyber2077 };

    public static Theme ById(string? id) => All.FirstOrDefault(t => t.Id == id) ?? ValcoBlue;

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static Brush MakeBrush(string spec)
    {
        if (spec.StartsWith("v:") || spec.StartsWith("h:"))
        {
            var vertical = spec[0] == 'v';
            var parts = spec[2..].Split(',');
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = vertical ? new Point(0, 1) : new Point(1, 0) };
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i].Split('@');
                double off = p.Length > 1 ? double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture) : (parts.Length == 1 ? 0 : (double)i / (parts.Length - 1));
                g.GradientStops.Add(new GradientStop(C(p[0]), off));
            }
            g.Freeze();
            return g;
        }
        var b = new SolidColorBrush(C(spec));
        b.Freeze();
        return b;
    }

    /// <summary>First colour of a spec, for places that need a plain Color.</summary>
    public static Color FirstColor(string spec) => C(spec.StartsWith("v:") || spec.StartsWith("h:") ? spec[2..].Split(',')[0].Split('@')[0] : spec);

    public static void Apply(Theme theme)
    {
        Current = theme;
        // ribbon and Valco button colours (shared with Word and Express); the theme's own brushes are applied after, so they win
        OfficeTheme.Apply(theme.Id switch { "valco-silver" or "dw-cs6" => "Silver", "valco-black" or "weavecraft" or "cyber2077" => "Black", _ => "Blue" });
        var res = Application.Current.Resources;
        foreach (var (k, v) in theme.Brushes)
        {
            res[k + "Brush"] = MakeBrush(v);
            res[k + "Color"] = FirstColor(v);
        }
        res["ThemeRadius"] = new CornerRadius(theme.Radius);
        res["IsVista"] = theme.Id == "vista";
        Changed?.Invoke();
    }
}
