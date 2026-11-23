using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace ValcoExpress
{
    // =====================================================================================
    //  Main window: Office 2007-style Ribbon, Valco button and Quick Access Toolbar
    // =====================================================================================
    public partial class MainWindow
    {
        RibbonBar _ribbon;
        ValcoTitleBar _title;
        Button BtnReply, BtnReplyAll, BtnForward, BtnPrint, BtnDelete, BtnFlag, BtnMove, BtnCopy, BtnRead, BtnUnread, BtnJunk, BtnForwardAtt;
        ToggleButton _tgFolders, _tgPreview, _tgSearch, _tgStatus, _tgHideRead;
        readonly Dictionary<string, ToggleButton> _tgSort = new Dictionary<string, ToggleButton>();
        readonly Dictionary<string, ToggleButton> _tgScheme = new Dictionary<string, ToggleButton>();
        ToggleButton _tgAsc, _tgDesc;
        bool _syncing;

        static RoutedEventArgs NoArgs => new RoutedEventArgs();

        void BuildRibbon()
        {
            _ribbon = new RibbonBar();
            var home = new RibbonTab("Home");
            BtnDelete = RB.Large("IconDelete", "Delete", () => Delete_Click(null, NoArgs), "Delete the selected messages (Del).");
            BtnReply = RB.Large("IconReply", "Reply", () => Reply_Click(null, NoArgs), "Reply to the sender of this message (Ctrl+R).");
            BtnReplyAll = RB.Large("IconReplyAll", "Reply\nto All", () => ReplyAll_Click(null, NoArgs), "Reply to the sender and all other recipients (Ctrl+Shift+R).");
            BtnForward = RB.Large("IconForward", "Forward", () => Forward_Click(null, NoArgs), "Forward this message to someone else (Ctrl+F).");
            BtnForwardAtt = RB.Small("IconAttach", "As Attachment", () => ForwardAtt_Click(null, NoArgs), "Forward the message as an attachment.");
            BtnMove = RB.Small("IconFolder", "Move to Folder", () => MoveTo_Click(null, NoArgs), "Move the selected messages to another folder (Ctrl+Shift+V).");
            BtnCopy = RB.Small("IconFolders", "Copy to Folder", () => CopyTo_Click(null, NoArgs), "Copy the selected messages to another folder.");
            BtnJunk = RB.Small("IconJunk", "Junk", null, "Mark the selected messages as junk or not junk.", () => RB.Menu(
                RB.MI("Mark as _Junk", () => MarkJunk_Click(null, NoArgs), "IconJunk"),
                RB.MI("Mark as _Not Junk", () => NotJunk_Click(null, NoArgs), "IconInbox"),
                null,
                RB.MI("Empty 'Junk E-mail' Folder", () => EmptyJunk_Click(null, NoArgs))));
            BtnRead = RB.Small("IconEnvOpen", "Mark as Read", () => MarkRead_Click(null, NoArgs), "Mark the selected messages as read (Ctrl+Q).");
            BtnUnread = RB.Small("IconEnvClosed", "Mark as Unread", () => MarkUnread_Click(null, NoArgs), "Mark the selected messages as unread (Ctrl+U).");
            BtnFlag = RB.Small("IconFlag", "Follow Up", () => Flag_Click(null, NoArgs), "Flag or unflag the selected messages.");

            home.Add(RB.Group("New", null,
                RB.Large("IconCreateMail", "New\nE-mail", () => NewMail_Click(null, NoArgs), "Create a new e-mail message (Ctrl+N)."),
                RB.Large("IconAddContact", "New\nItems", null, "Create a new folder or contact.", () => RB.Menu(
                    RB.MI("_E-mail Message", () => NewMail_Click(null, NoArgs), "IconCreateMail", "Ctrl+N"),
                    RB.MI("_Folder...", () => NewFolder_Click(null, NoArgs), "IconFolder", "Ctrl+Shift+E"),
                    RB.MI("_Contact...", () => NewContact_Click(null, NoArgs), "IconAddContact")))));
            home.Add(RB.Group("Delete", null, BtnDelete, RB.Stack(BtnJunk)));
            home.Add(RB.Group("Respond", null, BtnReply, BtnReplyAll, BtnForward, RB.Stack(BtnForwardAtt)));
            home.Add(RB.Group("Move", null, RB.Stack(BtnMove, BtnCopy)));
            home.Add(RB.Group("Tags", null, RB.Stack(BtnRead, BtnUnread, BtnFlag)));
            home.Add(RB.Group("Find", null, RB.Stack(
                RB.Small("IconFind", "Find Message", () => Find_Click(null, NoArgs), "Search the messages in this folder (Ctrl+E)."),
                RB.Small("IconContacts", "Contacts", () => Contacts_Click(null, NoArgs), "Open your contacts (Ctrl+Shift+C)."),
                RB.Small("IconAddContact", "Add Sender", () => AddSender_Click(null, NoArgs), "Add the sender of the selected message to your contacts."))));
            home.Add(RB.Group("Send/Receive", null, RB.Large("IconSendReceive", "Send/Receive\nAll Folders", () => SendReceive_Click(null, NoArgs), "Send all outgoing messages and check for new ones (Ctrl+M).")));

            var sr = new RibbonTab("Send / Receive");
            sr.Add(RB.Group("Send & Receive", null,
                RB.Large("IconSendReceive", "Send/Receive\nAll Folders", () => SendReceive_Click(null, NoArgs), "Send all outgoing messages and check for new ones (Ctrl+M)."),
                RB.Stack(RB.Small("IconRefresh", "Update Folder", () => Refresh_Click(null, NoArgs), "Check this folder for new messages (F5)."),
                         RB.Small("IconEnvOpen", "Mark All as Read", () => MarkAllRead_Click(null, NoArgs), "Mark every message in this folder as read (Ctrl+Shift+A)."))));
            sr.Add(RB.Group("Accounts", null,
                RB.Large("IconAccount", "Accounts", () => Accounts_Click(null, NoArgs), "Add, remove or change your e-mail accounts."),
                RB.Large("IconOptions", "Options", () => Options_Click(null, NoArgs), "Change how Valco Express sends, receives and shows mail.")));

            var folder = new RibbonTab("Folder");
            folder.Add(RB.Group("New", null, RB.Large("IconFolder", "New\nFolder", () => NewFolder_Click(null, NoArgs), "Create a new folder (Ctrl+Shift+E).")));
            folder.Add(RB.Group("Clean Up", null,
                RB.Large("IconDeleted", "Empty\nDeleted Items", () => EmptyDeleted_Click(null, NoArgs), "Permanently delete everything in Deleted Items."),
                RB.Large("IconJunk", "Empty\nJunk E-mail", () => EmptyJunk_Click(null, NoArgs), "Permanently delete everything in Junk E-mail."),
                RB.Stack(RB.Small("IconEnvOpen", "Mark All as Read", () => MarkAllRead_Click(null, NoArgs)),
                         RB.Small("IconSelect", "Select All", () => SelectAll_Click(null, NoArgs), "Select every message in the list (Ctrl+A)."))));
            folder.Add(RB.Group("Import & Export", null, RB.Stack(
                RB.Small("IconOpen", "Import Messages...", () => Import_Click(null, NoArgs), "Import .eml messages from a folder."),
                RB.Small("IconSaveAs", "Export Messages...", () => Export_Click(null, NoArgs), "Export the selected messages as .eml files."),
                RB.Small("IconOpen", "Open Message File...", () => OpenFile_Click(null, NoArgs), "Open a saved .eml message."))));

            var view = new RibbonTab("View");
            _tgFolders = RB.Toggle("IconFolders", "Folder List", v => SetLayout(st => st.ShowFolders = v), "Show or hide the folder list.");
            _tgPreview = RB.Toggle("IconPreview", "Reading Pane", v => SetLayout(st => st.ShowPreview = v), "Show or hide the reading pane.");
            _tgSearch = RB.Toggle("IconFind", "Search Bar", v => SetLayout(st => st.ShowToolbar = v), "Show or hide the search bar.");
            _tgStatus = RB.Toggle("IconProperties", "Status Bar", v => SetLayout(st => st.ShowStatusBar = v), "Show or hide the status bar.");
            view.Add(RB.Group("Layout", null, RB.Stack(_tgFolders, _tgPreview), RB.Stack(_tgSearch, _tgStatus)));
            _tgHideRead = RB.Toggle("IconEnvClosed", "Unread Only", v => { if (v) HideRead_Click(null, NoArgs); else ShowAll_Click(null, NoArgs); }, "Show only the messages you haven't read.");
            view.Add(RB.Group("Current View", null, RB.Stack(_tgHideRead)));
            ToggleButton SortT(string prop, string label)
            {
                var t = RB.Toggle(null, label, v => { _sortProp = prop; ApplySort(); UpdateSortMenus(); }, "Arrange messages by " + label.ToLowerInvariant() + ".");
                _tgSort[prop] = t; return t;
            }
            _tgAsc = RB.Toggle("IconMoveUp", "Ascending", v => SortAsc_Click(null, NoArgs));
            _tgDesc = RB.Toggle("IconMoveDown", "Descending", v => SortDesc_Click(null, NoArgs));
            view.Add(RB.Group("Arrangement", null, RB.Stack(SortT("Date", "Date"), SortT("Who", "From"), SortT("Subject", "Subject")), RB.Stack(SortT("Size", "Size"), SortT("IsFlagged", "Flag")), RB.Sep(), RB.Stack(_tgAsc, _tgDesc)));
            ToggleButton SchemeT(string name)
            {
                var t = RB.Toggle(null, name, v => SetScheme(name), $"Use the {name} colour scheme.");
                _tgScheme[name] = t; return t;
            }
            view.Add(RB.Group("Color Scheme", null, RB.Stack(SchemeT("Blue"), SchemeT("Silver"), SchemeT("Black")), RB.Stack(SchemeT("Windows Vista"), SchemeT("Dreamweaver CS6")), RB.Stack(SchemeT("Modern Dark"), SchemeT("2077"))));
            view.Add(RB.Group("Window", null, RB.Large("IconRefresh", "Refresh", () => Refresh_Click(null, NoArgs), "Check this folder for new messages (F5).")));

            var help = new RibbonTab("Help");
            help.Add(RB.Group("Help", null,
                RB.Large("IconQuestion", "Keyboard\nShortcuts", () => Shortcuts_Click(null, NoArgs)),
                RB.Large("IconInfo", "About", () => About_Click(null, NoArgs), "Version and copyright information for Valco Express."),
                RB.Large("IconRefresh", "Check for\nUpdates", CheckForUpdates, "Get the latest version of Valco Office from Valco Office Update.")));

            foreach (var t in new[] { home, sr, folder, view, help }) _ribbon.AddTab(t);
            if (Storage.Settings.RibbonMinimized) _ribbon.ToggleMinimize();
            RibbonHost.Children.Add(_ribbon);

            _title = new ValcoTitleBar(this, ShowValcoMenu, "Click here to create, open, save or print messages, and to see everything else you can do in Valco Express.")
                .QatOption("SendReceive", "IconSendReceive", "Send/Receive All Folders", () => SendReceive_Click(null, NoArgs))
                .QatOption("NewMail", "IconCreateMail", "New E-mail", () => NewMail_Click(null, NoArgs))
                .QatOption("Reply", "IconReply", "Reply", () => Reply_Click(null, NoArgs))
                .QatOption("Forward", "IconForward", "Forward", () => Forward_Click(null, NoArgs))
                .QatOption("Delete", "IconDelete", "Delete", () => Delete_Click(null, NoArgs))
                .QatOption("Print", "IconPrint", "Print", () => Print_Click(null, NoArgs))
                .QatOption("Contacts", "IconContacts", "Contacts", () => Contacts_Click(null, NoArgs))
                .QatOption("Find", "IconFind", "Find Message", () => Find_Click(null, NoArgs));
            Loaded += (s, e) => Updates.CheckOnStartup(this, "express", "Valco Express");
            _title.InitQat(
                () => (Storage.Settings.QuickAccess ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                l => { Storage.Settings.QuickAccess = string.Join(",", l); Storage.SaveSettings(); },
                _ribbon, m => { Storage.Settings.RibbonMinimized = m; Storage.SaveSettings(); });
        }

        void SetLayout(Action<AppSettings> change)
        {
            if (_syncing) return;
            change(Storage.Settings);
            Storage.SaveSettings();
            ApplyLayout();
            if (Storage.Settings.ShowPreview && Sel.Count == 1) _ = PreviewAsync(Sel[0]);
        }

        void SetScheme(string name)
        {
            if (_syncing) return;
            Storage.Settings.ColorScheme = name;
            Storage.SaveSettings();
            OfficeTheme.Apply(name);
            UpdateSchemeMenu();
        }

        /// <summary>Keeps the ribbon's toggles and enabled states in step with the window.</summary>
        void SyncRibbon()
        {
            if (_ribbon == null) return;
            _syncing = true;
            try
            {
                var st = Storage.Settings;
                _tgFolders.IsChecked = st.ShowFolders; _tgPreview.IsChecked = st.ShowPreview;
                _tgSearch.IsChecked = st.ShowToolbar; _tgStatus.IsChecked = st.ShowStatusBar; _tgHideRead.IsChecked = st.HideRead;
                foreach (var kv in _tgSort) kv.Value.IsChecked = kv.Key == _sortProp;
                _tgAsc.IsChecked = !_sortDesc; _tgDesc.IsChecked = _sortDesc;
                foreach (var kv in _tgScheme) kv.Value.IsChecked = kv.Key == OfficeTheme.Current;
                int n = MessageList.SelectedItems.Count;
                bool one = n == 1, any = n > 0;
                BtnForwardAtt.IsEnabled = one;
                BtnMove.IsEnabled = BtnCopy.IsEnabled = BtnRead.IsEnabled = BtnUnread.IsEnabled = BtnFlag.IsEnabled = BtnJunk.IsEnabled = any;
                foreach (var kv in _title.QatButtons)
                    kv.Value.IsEnabled = kv.Key switch { "Reply" or "Forward" or "Print" => one, "Delete" => any, _ => true };
            }
            finally { _syncing = false; }
        }

        void CheckForUpdates() => Updates.CheckNow(this, "express", "Valco Express");

        void ShowValcoMenu()
        {
            IEnumerable<FolderNode> All(IEnumerable<FolderNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(All(n.Children)));
            new AppMenu()
                .Item("IconCreateMail", "New", () => NewMail_Click(null, NoArgs), "Create a new item",
                    new AppMenuEntry("IconCreateMail", "E-mail Message", "Write a new e-mail message.", () => NewMail_Click(null, NoArgs)),
                    new AppMenuEntry("IconFolder", "Folder", "Create a new folder to organize your messages.", () => NewFolder_Click(null, NoArgs)),
                    new AppMenuEntry("IconAddContact", "Contact", "Add a person to your contacts.", () => NewContact_Click(null, NoArgs)))
                .Item("IconOpen", "Open", () => OpenFile_Click(null, NoArgs), "Open",
                    new AppMenuEntry("IconEnvOpen", "Selected Message", "Open the selected message in its own window.", () => Open_Click(null, NoArgs)),
                    new AppMenuEntry("IconOpen", "Message File", "Open a message saved as an .eml file.", () => OpenFile_Click(null, NoArgs)))
                .Item("IconSaveAs", "Save As", () => SaveAs_Click(null, NoArgs))
                .Sep()
                .Item("IconPrint", "Print", () => Print_Click(null, NoArgs))
                .Item("IconFolders", "Import and Export", null, "Move messages in and out of Valco Express",
                    new AppMenuEntry("IconOpen", "Import Messages", "Import .eml messages from a folder into the current folder.", () => Import_Click(null, NoArgs)),
                    new AppMenuEntry("IconSaveAs", "Export Messages", "Save the selected messages as .eml files in a folder.", () => Export_Click(null, NoArgs)))
                .Item("IconAccount", "Accounts", () => Accounts_Click(null, NoArgs))
                .Sep()
                .Item("IconExit", "Close", () => Close())
                .Recent("Folders", () => All(Roots).Where(f => f.Selectable).Take(17).Select(f => (f.Name + (f.Unread > 0 ? $"  ({f.Unread})" : ""), f.Name, (Action)(() => f.IsSelected = true))))
                .Footer("Valco Express Options", "IconOptions", () => Options_Click(null, NoArgs))
                .Footer("Exit Valco Express", "IconExit", () => Exit_Click(null, NoArgs))
                .Show(_title.Orb);
        }
    }

    // =====================================================================================
    //  Compose window ribbon (Message, Insert, Options), like Outlook 2007's message window
    // =====================================================================================
    public partial class ComposeWindow
    {
        RibbonBar _ribbon;
        ValcoTitleBar _title;
        ToggleButton _tgHigh, _tgLow, _tgSpell, _tgBcc;

        static RoutedEventArgs NoArgs => new RoutedEventArgs();

        void BuildRibbon()
        {
            _ribbon = new RibbonBar();
            // reuse the real font / bold / italic / underline / colour controls so their state logic keeps working
            foreach (var c in new FrameworkElement[] { FontBox, SizeBox, BoldBtn, ItalicBtn, UnderlineBtn, ColorBtn }) FormatPanel.Children.Remove(c);
            FontBox.Width = 140; FontBox.Height = 21; SizeBox.Width = 48; SizeBox.Height = 21;
            foreach (var t in new ButtonBase[] { BoldBtn, ItalicBtn, UnderlineBtn, ColorBtn })
            {
                t.Style = t is ToggleButton ? RB.TglStyle : RB.BtnStyle;
                t.Height = 22; t.MinWidth = 22; t.Margin = new Thickness(0, 0, 1, 0); t.HorizontalContentAlignment = HorizontalAlignment.Center;
            }

            var msg = new RibbonTab("Message");
            msg.Add(RB.Group("Send", null, RB.Large("IconSend", "Send", () => Send_Click(null, NoArgs), "Send this message (Alt+S).")));
            msg.Add(RB.Group("Clipboard", null,
                RB.Large("IconPaste", "Paste", () => ApplicationCommands.Paste.Execute(null, Keyboard.FocusedElement as IInputElement ?? Editor), "Paste the contents of the Clipboard (Ctrl+V)."),
                RB.Stack(RB.Small("IconCut", "Cut", () => ApplicationCommands.Cut.Execute(null, Keyboard.FocusedElement as IInputElement ?? Editor), "Cut the selection (Ctrl+X)."),
                         RB.Small("IconCopy", "Copy", () => ApplicationCommands.Copy.Execute(null, Keyboard.FocusedElement as IInputElement ?? Editor), "Copy the selection (Ctrl+C)."))));
            msg.Add(RB.Group("Basic Text", null, RB.Stack(
                RB.Row(FontBox, SizeBox),
                RB.Row(BoldBtn, ItalicBtn, UnderlineBtn, ColorBtn, RB.Sep(),
                    RB.Small("IconBullets", null, () => Bullets_Click(null, NoArgs), "Bullets"),
                    RB.Small("IconNumbering", null, () => Numbers_Click(null, NoArgs), "Numbering"),
                    RB.Small("IconOutdent", null, () => Outdent_Click(null, NoArgs), "Decrease Indent"),
                    RB.Small("IconIndent", null, () => Indent_Click(null, NoArgs), "Increase Indent")),
                RB.Row(RB.Small("IconAlignLeft", null, () => AlignLeft_Click(null, NoArgs), "Align Text Left"),
                    RB.Small("IconAlignCenter", null, () => AlignCenter_Click(null, NoArgs), "Center"),
                    RB.Small("IconAlignRight", null, () => AlignRight_Click(null, NoArgs), "Align Text Right")))));
            msg.Add(RB.Group("Names", null,
                RB.Large("IconContacts", "Address\nBook", () => PickTo_Click(null, NoArgs), "Choose recipients from your contacts."),
                RB.Stack(RB.Small("IconAddContact", "Add Cc", () => PickCc_Click(null, NoArgs)), RB.Small("IconAddContact", "Add Bcc", () => PickBcc_Click(null, NoArgs)))));
            msg.Add(RB.Group("Include", null,
                RB.Large("IconAttach", "Attach\nFile", () => Attach_Click(null, NoArgs), "Attach a file to this message."),
                RB.Stack(RB.Small("IconLink", "Hyperlink", () => Link_Click(null, NoArgs), "Create a link to a web page."),
                         RB.Small("IconHLine", "Horizontal Line", () => Rule_Click(null, NoArgs)))));
            _tgHigh = RB.Toggle("IconPriorityHigh", "High Importance", v => SetPriority(v ? 1 : 3), "Mark this message as high importance.");
            _tgLow = RB.Toggle("IconPriorityLow", "Low Importance", v => SetPriority(v ? 5 : 3), "Mark this message as low importance.");
            msg.Add(RB.Group("Options", null, RB.Stack(_tgHigh, _tgLow)));
            _tgSpell = RB.Toggle("IconSpelling", "Check As You Type", v => { SpellMenu.IsChecked = v; Spell_Click(null, NoArgs); }, "Underline misspelled words as you type.");
            _tgSpell.IsChecked = true;
            msg.Add(RB.Group("Proofing", null, RB.Stack(_tgSpell)));

            var insert = new RibbonTab("Insert");
            insert.Add(RB.Group("Include", null,
                RB.Large("IconAttach", "Attach\nFile", () => Attach_Click(null, NoArgs), "Attach a file to this message."),
                RB.Large("IconContacts", "Business\nCard", () => PickTo_Click(null, NoArgs), "Add recipients from your contacts.")));
            insert.Add(RB.Group("Links", null, RB.Large("IconLink", "Hyperlink", () => Link_Click(null, NoArgs), "Create a link to a web page.")));
            insert.Add(RB.Group("Text", null, RB.Large("IconHLine", "Horizontal\nLine", () => Rule_Click(null, NoArgs))));

            var options = new RibbonTab("Options");
            _tgBcc = RB.Toggle("IconContacts", "Show Bcc", v => ShowBcc(v), "Show the Bcc box for hidden recipients.");
            options.Add(RB.Group("Fields", null, RB.Stack(_tgBcc)));
            options.Add(RB.Group("Importance", null, RB.Stack(
                RB.Small("IconPriorityHigh", "High", () => SetPriority(1)), RB.Small("IconBlank", "Normal", () => SetPriority(3)), RB.Small("IconPriorityLow", "Low", () => SetPriority(5)))));
            options.Add(RB.Group("Save", null,
                RB.Large("IconSave", "Save\nDraft", () => Save_Click(null, NoArgs), "Save this message in Drafts (Ctrl+S)."),
                RB.Large("IconSaveAs", "Save As", () => SaveAs_Click(null, NoArgs), "Save the message as a file.")));

            foreach (var t in new[] { msg, insert, options }) _ribbon.AddTab(t);
            if (Storage.Settings.RibbonMinimized) _ribbon.ToggleMinimize();
            RibbonHost.Children.Add(_ribbon);

            _title = new ValcoTitleBar(this, ShowValcoMenu, "Click here to send, save or print this message, and to see everything else you can do with it.")
                .QatOption("Save", "IconSave", "Save", () => Save_Click(null, NoArgs))
                .QatOption("Send", "IconSend", "Send", () => Send_Click(null, NoArgs))
                .QatOption("Undo", "IconUndo", "Undo", () => ApplicationCommands.Undo.Execute(null, Editor))
                .QatOption("Redo", "IconRedo", "Redo", () => ApplicationCommands.Redo.Execute(null, Editor))
                .QatOption("Attach", "IconAttach", "Attach File", () => Attach_Click(null, NoArgs))
                .QatOption("Spelling", "IconSpelling", "Spelling", () => _tgSpell.IsChecked = !_tgSpell.IsChecked);
            _title.InitQat(
                () => (Storage.Settings.ComposeQuickAccess ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                l => { Storage.Settings.ComposeQuickAccess = string.Join(",", l); Storage.SaveSettings(); },
                _ribbon, m => { Storage.Settings.RibbonMinimized = m; Storage.SaveSettings(); });
        }

        bool _syncPri;
        void SetPriority(int p)
        {
            if (_syncPri) return;
            _priority = p;
            UpdatePriorityUi();
        }

        void SyncRibbon()
        {
            if (_ribbon == null) return;
            _syncPri = true;
            _tgHigh.IsChecked = _priority == 1;
            _tgLow.IsChecked = _priority == 5;
            _tgBcc.IsChecked = AllHeadersMenu.IsChecked;
            _syncPri = false;
        }

        void ShowValcoMenu()
        {
            new AppMenu()
                .Item("IconSend", "Send", () => Send_Click(null, NoArgs))
                .Item("IconSave", "Save", () => Save_Click(null, NoArgs))
                .Item("IconSaveAs", "Save As", () => SaveAs_Click(null, NoArgs))
                .Sep()
                .Item("IconAttach", "Attach File", () => Attach_Click(null, NoArgs))
                .Item("IconContacts", "Address Book", () => PickTo_Click(null, NoArgs))
                .Sep()
                .Item("IconExit", "Close", () => Close())
                .Recent("Recipients", () => Storage.Contacts.Where(c => !string.IsNullOrWhiteSpace(c.Email)).Take(17)
                    .Select(c => (c.Display, c.Email, (Action)(() => ToBox.Append(c.Display)))))
                .Footer("Close", "IconExit", Close)
                .Show(_title.Orb);
        }
    }
}
