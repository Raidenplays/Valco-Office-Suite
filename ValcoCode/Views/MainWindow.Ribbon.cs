using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ValcoCode.Services;

namespace ValcoCode.Views;

/// <summary>
/// Office 2007-style Ribbon, Valco button and Quick Access Toolbar for Valco Code (shared with Valco Word and Valco Express
/// through ValcoCommon). The VS Code workbench (activity bar, side bar, editor tabs, panel) stays underneath it.
/// </summary>
public partial class MainWindow
{
    RibbonBar? ribbon;
    ValcoTitleBar? titleBar;
    ToggleButton? tgWrap, tgWhitespace, tgAutoSave, tgFormatOnSave;
    bool syncingRibbon;

    void BuildRibbon()
    {
        ribbon = new RibbonBar();
        Action C(string id) => () => Cmd_(id);
        static IInputElement Target() => Keyboard.FocusedElement as IInputElement ?? Application.Current.MainWindow;

        var home = new RibbonTab("Home");
        home.Add(RB.Group("Clipboard", null,
            RB.Large("IconPaste", "Paste", () => ApplicationCommands.Paste.Execute(null, Target()), "Paste the contents of the Clipboard (Ctrl+V)."),
            RB.Stack(RB.Small("IconCut", "Cut", () => ApplicationCommands.Cut.Execute(null, Target()), "Cut the selection (Ctrl+X)."),
                     RB.Small("IconCopy", "Copy", () => ApplicationCommands.Copy.Execute(null, Target()), "Copy the selection (Ctrl+C)."),
                     RB.Small("IconSelect", "Select All", C("selectall"), "Select everything in the editor (Ctrl+A)."))));
        home.Add(RB.Group("File", null,
            RB.Large("IconNew", "New\nFile", null, "Create a new file.", () => RB.Menu(
                RB.MI("New _Text File", C("new"), "IconNew", "Ctrl+N"),
                RB.MI("New _Python File", C("newpy"), "IconDoc"),
                RB.MI("New _Java File", C("newjava"), "IconDoc"))),
            RB.Large("IconOpen", "Open\nFolder", C("openfolder"), "Open a folder as your workspace (Ctrl+K Ctrl+O)."),
            RB.Stack(RB.Small("IconOpen", "Open File...", C("openfile"), "Open a file (Ctrl+O)."),
                     RB.Small("IconSave", "Save", C("save"), "Save the current file (Ctrl+S)."),
                     RB.Small("IconSaveAs", "Save All", C("saveall"), "Save every open file (Ctrl+K S)."))));
        home.Add(RB.Group("Editing", null,
            RB.Stack(RB.Small("IconFind", "Find", C("find"), "Find in the current file (Ctrl+F)."),
                     RB.Small("IconReplace", "Replace", C("replace"), "Find and replace in the current file (Ctrl+H)."),
                     RB.Small("IconFind", "Find in Files", C("findfiles"), "Search the whole folder (Ctrl+Shift+F).")),
            RB.Stack(RB.Small("IconZoom", "Go to Line", C("gotoline"), "Jump to a line number (Ctrl+G)."),
                     RB.Small("IconComment", "Comment", C("comment"), "Comment or uncomment the selected lines (Ctrl+/)."),
                     RB.Small("IconParagraph", "Format", C("format"), "Format the document (Shift+Alt+F)."))));
        home.Add(RB.Group("Run", null,
            RB.Large("IconMacro", "Run\nFile", C("run"), "Run the current Python or Java file (F5)."),
            RB.Stack(RB.Small("IconMacro", "Run Without Debugging", C("run2"), "Run the file (Ctrl+F5)."),
                     RB.Small("IconDelete", "Stop", C("stop"), "Stop the running program (Shift+F5)."))));
        home.Add(RB.Group("Commands", null,
            RB.Large("IconQuickParts", "Command\nPalette", C("palette"), "Search every command (Ctrl+Shift+P)."),
            RB.Large("IconDocMap", "Go to\nFile", C("quickopen"), "Open a file by name (Ctrl+P).")));

        var edit = new RibbonTab("Edit");
        edit.Add(RB.Group("Undo", null, RB.Stack(RB.Small("IconUndo", "Undo", C("undo"), "Undo (Ctrl+Z)."), RB.Small("IconRedo", "Redo", C("redo"), "Redo (Ctrl+Y)."))));
        edit.Add(RB.Group("Lines", null,
            RB.Stack(RB.Small("IconMoveUp", "Move Line Up", C("moveup"), "Alt+Up"), RB.Small("IconMoveDown", "Move Line Down", C("movedown"), "Alt+Down")),
            RB.Stack(RB.Small("IconCopy", "Copy Line Up", C("copyup"), "Shift+Alt+Up"), RB.Small("IconCopy", "Copy Line Down", C("copydown"), "Shift+Alt+Down"))));
        edit.Add(RB.Group("Code", null,
            RB.Stack(RB.Small("IconComment", "Toggle Comment", C("comment")), RB.Small("IconParagraph", "Format Document", C("format")), RB.Small("IconQuickParts", "Suggest", C("suggest"), "Show suggestions (Ctrl+Space).")),
            RB.Stack(RB.Small("IconPromote", "Fold All", C("fold")), RB.Small("IconDemote", "Unfold All", C("unfold")), RB.Small("IconChangeCase", "Language Mode", C("lang"), "Change the language of this file."))));
        edit.Add(RB.Group("Replace", null,
            RB.Large("IconReplace", "Replace\nin Files", C("replacefiles"), "Find and replace across the whole folder.")));
        tgAutoSave = RB.Toggle("IconSave", "Auto Save", _ => { if (!syncingRibbon) Cmd_("autosave"); }, "Save files automatically after you stop typing.");
        tgFormatOnSave = RB.Toggle("IconParagraph", "Format on Save", _ => { if (!syncingRibbon) Cmd_("formatonsave"); }, "Format files every time you save them.");
        edit.Add(RB.Group("Saving", null, RB.Stack(tgAutoSave, tgFormatOnSave, RB.Small("IconColumns", "Tab Size", C("indent"), "Change the indentation of this file."))));

        var view = new RibbonTab("View");
        view.Add(RB.Group("Show", null,
            RB.Large("IconFolders", "Explorer", C("explorer"), "Show the Explorer (Ctrl+Shift+E)."),
            RB.Large("IconFind", "Search", C("search"), "Search across files (Ctrl+Shift+F)."),
            RB.Large("IconCompare", "Source\nControl", C("scm"), "Show Git changes (Ctrl+Shift+G)."),
            RB.Stack(RB.Small("IconMacro", "Run and Debug", C("runview")), RB.Small("IconWarning", "Problems", C("problems"), "Ctrl+Shift+M"), RB.Small("IconDraft", "Output", C("output")))));
        tgWrap = RB.Toggle("IconLineSpacing", "Word Wrap", _ => { if (!syncingRibbon) Cmd_("wrap"); }, "Wrap long lines (Alt+Z).");
        tgWhitespace = RB.Toggle("IconParagraph", "Whitespace", _ => { if (!syncingRibbon) Cmd_("whitespace"); }, "Show spaces and tabs.");
        view.Add(RB.Group("Layout", null,
            RB.Stack(RB.Small("IconDocMap", "Side Bar", C("sidebar"), "Show or hide the side bar (Ctrl+B)."), RB.Small("IconSplitWindow", "Panel", C("panel"), "Show or hide the panel (Ctrl+J)."), RB.Small("IconExit", "Close All Editors", C("closeall"))),
            RB.Stack(tgWrap, tgWhitespace)));
        view.Add(RB.Group("Zoom", null,
            RB.Large("IconZoom", "Zoom", C("zoomreset"), "Reset the zoom to 100% (Ctrl+0)."),
            RB.Stack(RB.Small("IconGrow", "Zoom In", C("zoomin"), "Ctrl+="), RB.Small("IconShrink", "Zoom Out", C("zoomout"), "Ctrl+-"))));
        view.Add(RB.Group("Themes", null,
            RB.Large("IconThemes", "Colour\nScheme", null, "Choose a colour scheme.", () =>
            {
                var m = new ContextMenu();
                foreach (var t in ThemeManager.All)
                {
                    var id = t.Id;
                    m.Items.Add(RB.MI(t.Name, () => SetTheme(id), null, null, ThemeManager.Current?.Id == id));
                }
                return m;
            }),
            RB.Large("IconOptions", "Settings", C("settings"), "Open Valco Code settings (Ctrl+,).")));

        var terminalTab = new RibbonTab("Terminal");
        terminalTab.Add(RB.Group("Terminal", null,
            RB.Large("IconMacro", "New\nTerminal", C("newterm"), "Open a new terminal (Ctrl+Shift+`)."),
            RB.Stack(RB.Small("IconSplitWindow", "Show / Hide", C("terminal"), "Ctrl+`"), RB.Small("IconDelete", "Kill Terminal", C("killterm")), RB.Small("IconClearFormat", "Clear", C("clearterm")))));
        terminalTab.Add(RB.Group("Run", null, RB.Large("IconMacro", "Run\nFile", C("run"), "Run the current file (F5)."), RB.Stack(RB.Small("IconDelete", "Stop", C("stop")))));

        var git = new RibbonTab("Git");
        git.Add(RB.Group("Changes", null,
            RB.Large("IconAccept", "Commit", C("git.commit"), "Commit your staged changes."),
            RB.Large("IconCompare", "Source\nControl", C("scm"), "Show changed files (Ctrl+Shift+G).")));
        git.Add(RB.Group("Remote", null,
            RB.Large("IconRefresh", "Sync", C("git.sync"), "Pull and then push."),
            RB.Stack(RB.Small("IconMoveDown", "Pull", C("git.pull")), RB.Small("IconMoveUp", "Push", C("git.push")), RB.Small("IconRefresh", "Fetch", C("git.fetch")))));
        git.Add(RB.Group("Repository", null,
            RB.Stack(RB.Small("IconSwitchWindows", "Checkout to...", C("git.checkout")), RB.Small("IconNew", "Initialize", C("git.init")), RB.Small("IconOpen", "Clone...", C("git.clone"))),
            RB.Large("IconLink", "Publish to\nGitHub", C("git.publish"), "Create a GitHub repository for this folder.")));
        git.Add(RB.Group("GitHub", null, RB.Large("IconProtect", "Sign In", C("gh.signin"), "Sign in to GitHub.")));

        var help = new RibbonTab("Help");
        help.Add(RB.Group("Help", null,
            RB.Large("IconInfo", "Welcome", C("welcome")),
            RB.Large("IconQuestion", "Keyboard\nShortcuts", C("shortcuts")),
            RB.Large("IconQuickParts", "All\nCommands", C("palette")),
            RB.Large("IconRefresh", "Check for\nUpdates", CheckForUpdates, "Get the latest version of Valco Office from Valco Office Update."),
            RB.Large("IconApp", "About", C("about"), "Version and copyright information for Valco Code.")));

        foreach (var t in new[] { home, edit, view, terminalTab, git, help }) ribbon.AddTab(t);
        if (Settings.RibbonMinimized) ribbon.ToggleMinimize();

        // the old menu bar row: hide it, keep the command center (moved into the ribbon's tab row)
        var root = (Grid)MenuBarRow.Parent;
        MenuBarRow.Visibility = Visibility.Collapsed;
        ((Panel)CommandCenter.Parent).Children.Remove(CommandCenter);
        CommandCenter.HorizontalAlignment = HorizontalAlignment.Right;
        CommandCenter.Width = 300;
        CommandCenter.Margin = new Thickness(0, 2, 8, 2);
        ribbon.HeaderRow.Children.Add(CommandCenter);
        Grid.SetRow(ribbon, 0);
        // behind the overlays (Command Palette, quick pick) that span the whole window
        root.Children.Insert(0, ribbon);
        Panel.SetZIndex(PaletteVeil, 100);

        titleBar = new ValcoTitleBar(this, ShowValcoMenu, "Click here to open, save or create files and folders, and to see everything else you can do in Valco Code.")
            .QatOption("Save", "IconSave", "Save", C("save"))
            .QatOption("SaveAll", "IconSaveAs", "Save All", C("saveall"))
            .QatOption("Undo", "IconUndo", "Undo", C("undo"))
            .QatOption("Redo", "IconRedo", "Redo", C("redo"))
            .QatOption("Run", "IconMacro", "Run File", C("run"))
            .QatOption("New", "IconNew", "New File", C("new"))
            .QatOption("Open", "IconOpen", "Open Folder", C("openfolder"))
            .QatOption("Palette", "IconQuickParts", "Command Palette", C("palette"))
            .QatOption("Terminal", "IconSplitWindow", "Toggle Terminal", C("terminal"));
        titleBar.InitQat(
            () => (Settings.QuickAccess ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            l => { Settings.QuickAccess = string.Join(",", l); Settings.Save(); },
            ribbon, m => { Settings.RibbonMinimized = m; Settings.Save(); });
        SyncRibbon();
    }

    /// <summary>Keeps the ribbon's toggle buttons in step with the settings (called whenever the menus are rebuilt).</summary>
    void SyncRibbon()
    {
        if (ribbon == null) return;
        syncingRibbon = true;
        tgWrap!.IsChecked = Settings.WordWrap;
        tgWhitespace!.IsChecked = Settings.ShowWhitespace;
        tgAutoSave!.IsChecked = Settings.AutoSave;
        tgFormatOnSave!.IsChecked = Settings.FormatOnSave;
        syncingRibbon = false;
    }

    void CheckForUpdates()
    {
        if (!Suite.CheckForUpdates())
            Notify("Valco Office Update comes with Valco Office Setup. Install Valco Office to get updates automatically.");
    }

    void ShowValcoMenu()
    {
        new AppMenu()
            .Item("IconNew", "New", () => Cmd_("new"), "Create a new file",
                new AppMenuEntry("IconNew", "Text File", "An empty file you can save with any extension.", () => Cmd_("new")),
                new AppMenuEntry("IconDoc", "Python File", "A new Python script.", () => Cmd_("newpy")),
                new AppMenuEntry("IconDoc", "Java File", "A new Java class.", () => Cmd_("newjava")))
            .Item("IconOpen", "Open", () => Cmd_("openfolder"), "Open",
                new AppMenuEntry("IconFolders", "Folder", "Open a folder as your workspace.", () => Cmd_("openfolder")),
                new AppMenuEntry("IconOpen", "File", "Open a single file.", () => Cmd_("openfile")),
                new AppMenuEntry("IconLink", "Git Repository", "Clone a repository from GitHub or another server.", () => Cmd_("git.clone")))
            .Item("IconSave", "Save", () => Cmd_("save"))
            .Item("IconSaveAs", "Save As", () => Cmd_("saveas"), "Save",
                new AppMenuEntry("IconSaveAs", "Save As", "Save the current file under a new name.", () => Cmd_("saveas")),
                new AppMenuEntry("IconSave", "Save All", "Save every open file.", () => Cmd_("saveall")))
            .Sep()
            .Item("IconOptions", "Preferences", () => Cmd_("settings"), "Preferences",
                new AppMenuEntry("IconOptions", "Settings", "Fonts, tab size, formatting and more.", () => Cmd_("settings")),
                new AppMenuEntry("IconThemes", "Colour Scheme", "Valco Blue, Silver or Black, or another theme.", () => Cmd_("theme")),
                new AppMenuEntry("IconQuestion", "Keyboard Shortcuts", "See every shortcut.", () => Cmd_("shortcuts")))
            .Item("IconFolders", "Close Folder", () => Cmd_("closefolder"))
            .Sep()
            .Item("IconExit", "Close", Close)
            .Recent("Recent Folders", () => Settings.Recent.Select(r => (Path.GetFileName(r.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : r, r, (Action)(() => OpenFolder(r)))))
            .Footer("Valco Code Options", "IconOptions", () => Cmd_("settings"))
            .Footer("Exit Valco Code", "IconExit", Close)
            .Show(titleBar!.Orb);
    }
}
