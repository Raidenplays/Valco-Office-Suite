# Valco Weave

A free, open-source visual web editor in the spirit of Adobe Dreamweaver. Part of **Valco Office**, the suite of Vista-style Windows apps (Valco Word, Valco Express, Valco Code, Valco Shell): same glass window frame, Valco button with the suite mark, Blue/Silver/Black colour schemes, glossy orb icon and setup wizard.

Valco Weave opens a folder as a **site** and lets you edit its pages in code, visually, or both side by side. Every visual edit is written back to your HTML as a small, targeted text change, so your formatting, indentation and comments stay as you wrote them.

Ported by ItsRaiden · MIT licence

## Features

**Views**
- **Code**: CodeMirror 6 with HTML/CSS/JS/JSON/Markdown highlighting, auto-closing tags, autocomplete, code folding, find and replace (Ctrl+F) and Emmet (type `ul>li*3` and press Tab).
- **Split**: code and design side by side, vertically or horizontally. Clicking an element in Design selects its code, and moving the cursor in code selects the element in Design.
- **Design**: WYSIWYG editing. Click to select an element, double-click to edit its text. Enter starts a new paragraph or list item, and Ctrl+B / Ctrl+I / Ctrl+U format text. Scripts are paused in this view, like Dreamweaver's.
- **Live**: the page with scripts running. Clicking a link to another page in your site opens that page in a new tab.
- **Device widths and media-query bar**: preview at 1280 / 1024 / 768 / 414 / 375 px, or click any `@media` breakpoint found in your CSS.
- **Related files bar**: shows "Source Code · css/style.css · js/main.js" for the open page. Click a related file to edit it while the Design view keeps showing the page, and CSS changes appear live before you save.

**VS Code-style coding** (powered by Microsoft's open-source HTML and CSS language services, the same engine VS Code uses)
- **Suggestions as you type**: HTML tags and attributes, every CSS property and value, and JavaScript names, each with a description panel, MDN links and browser-support badges.
- **Site-aware suggestions**: your own classes and ids in `class=""` / `id=""`, file and folder paths in `href`/`src`/`url()`, CSS `var(--…)` names, `.class`/`#id` selectors in CSS files, and ids inside `getElementById("…")` / `querySelector("…")`.
- **Hover docs** for HTML tags, attributes and CSS properties.
- **Snippets with tab stops**: `!` (HTML5 page), `link:css`, `a:blank`, `flex-center`, `grid-cols`, `@media`, `log`, `qs`, `on`, `ready` and more, plus Emmet.
- **Linked tag editing**: rename `<h2>` and its `</h2>` changes too.
- **Format Document** (Shift+Alt+F) and Format Selection (Ctrl+K Ctrl+F) for HTML, CSS and JS.
- **Ctrl+click** a class or id to jump to its CSS rule, or a link to open that file.
- **CSS checking** (unknown properties, empty rules, duplicates), including inside `<style>` blocks.
- **Editor extras**: coloured bracket pairs, indent guides, a minimap, inline colour swatches with a picker, multiple cursors (Ctrl+D, Ctrl+Shift+L, Alt+click, Ctrl+Alt+↑/↓), move/copy lines (Alt+↑/↓, Shift+Alt+↑/↓), toggle comment (Ctrl+/), Go to Line (Ctrl+G) and code zoom (Ctrl+= / Ctrl+-).
- **Command Palette** (Ctrl+Shift+P) and **Go to File** (Ctrl+P) with fuzzy search.

**Themes** (View › Theme): each one comes in **Light** and **Dark** (or *Match Windows*; toggle with Ctrl+Alt+T or the status bar). Valco Weave, **Windows Vista** (Aero glass, glossy buttons, VS 2008-style code colours), **Dreamweaver CS6** (classic light-grey workspace with Dreamweaver's code colours) and **2077** (colours from Endormi's MIT-licensed *2077* VS Code theme). They're recreated with CSS colours only, with no vendor artwork.

**W3C Validator** (Shift+F6, the *W3C validation* button in the status bar, or Site › Validate): works like the *W3C Web Validator* VS Code extension. It sends the page or stylesheet to the official W3C Nu Html Checker, underlines problems in the code with hover messages (each one clears when you edit that line), shows "File is valid" when it passes, and lists every message with its code extract in the W3C Validator tab. HTML fragments are wrapped in a temporary page so they can be checked too. Options: validate on save, show warnings, show info, notifications, and *Validate whole site*.

**Git & GitHub** (Git tab, Ctrl+Shift+G; desktop app, uses your installed Git)
- Sign in with a GitHub personal access token, or with GitHub's browser/device sign-in if you register an OAuth app (enable *Device Flow* and paste its Client ID). The token is stored in the OS credential store (Windows Credential Manager, macOS Keychain or Secret Service), never in a file.
- See changes, stage/unstage files, discard changes, commit (Ctrl+Enter), Commit & Push, Pull, and browse history.
- **Publish to GitHub** creates the repository on your account, commits the site and pushes it in one step.
- If Git has no name/email set, commits use your GitHub name and private no-reply address for that repository.

**Panels**
- **Files**: the site tree, with new file and folder, rename, delete, preview, and copy relative path. Drag files onto the page: images become `<img>`, CSS becomes `<link>`, and pages become links.
- **Insert**: about 45 elements in Text / HTML / Layout / Media / Form / Head categories, placed Before / After / Nested in / Wrapping the selection. Click to insert or drag into the Design view.
- **Properties**: settings for the selected tag (link targets, image src/alt/size, input types, media flags and so on), plus Format (P/H1–H6…), ID, Class (with suggestions from your CSS), inline style, text, and an editor for every attribute.
- **CSS Designer**: Sources → @Media → Selectors → Properties, like Dreamweaver's. "Current" shows only the rules that apply to the selected element. You can add, rename or delete rules, use colour swatches, attach or create stylesheets, or add a `<style>` block. Your CSS formatting is kept (postcss).
- **DOM**: a collapsible element tree.
- **Problems**: HTML parse errors, duplicate IDs, missing alt text, broken links to files that don't exist, a missing title, and a missing `lang`. Click a problem to jump to it.
- **Find in Site**: search all files (regex and case options, optionally HTML only), with Replace All.

**Site**
- **Tag selector** in the status bar (`<body> <main> <section.hero> <h1>`), as in classic Dreamweaver.
- **Templates**: Blank HTML5, Responsive starter, Bootstrap 5, a "2009 personal homepage", CSS, JS and Markdown.
- **Publish over FTP or FTPS** (explicit TLS, passive or active mode). You can send only the files changed since the last publish, with a progress bar and log. Passwords are never saved.
- **Export Site as ZIP**.
- **Preview in Browser (F12)**: saves the page and its related files, then opens it in your default browser.

## Keyboard shortcuts

| Keys | Action |
|---|---|
| Ctrl+N / Ctrl+O | New document / open site folder |
| Ctrl+S / Ctrl+Shift+S | Save / save all |
| Ctrl+W | Close tab |
| Alt+1 … Alt+4 | Code / Split / Design / Live |
| F12 | Preview in browser |
| Ctrl+Shift+F | Find in site |
| Double-click | Edit text in Design view |
| Del | Delete the selected element |
| Ctrl+D | Duplicate element |
| Alt+↑ / Alt+↓ | Move element up / down |
| Esc | Select parent element |
| Ctrl+Z / Ctrl+Shift+Z | Undo / redo (shared by Code and Design) |
| Ctrl+Shift+P / Ctrl+P | Command Palette / Go to File |
| Ctrl+Space | Show suggestions |
| Shift+Alt+F | Format document |
| Ctrl+G | Go to line |
| Ctrl+D / Ctrl+Shift+L | Select next match / all matches (multi-cursor) |
| Alt+click, Ctrl+Alt+↑/↓ | Add cursor |
| Ctrl+/ | Toggle comment |
| Ctrl+click | Go to CSS rule / open linked file |
| Ctrl+= / Ctrl+- / Ctrl+0 | Code zoom |

## Building

You need Node 20+ and Rust (stable). On Ubuntu/Kubuntu, also install Tauri's system libraries:

```bash
sudo apt install libwebkit2gtk-4.1-dev libgtk-3-dev libayatana-appindicator3-dev librsvg2-dev build-essential pkg-config
```

Then:

```bash
npm install
npm run tauri dev          # run the desktop app with hot reload
npm run tauri build        # build installers (.deb/.AppImage on Linux, .msi/.exe on Windows, .dmg on macOS)
npm run build:web          # single-file web build in dist-web/ (demo site + local folders in Chrome/Edge)
```

**Windows installer (Valco suite style):** `packaging/windows/build-msi-on-linux.sh` cross-builds the app and makes `ValcoWeave-Setup.msi` with the suite's standard setup wizard (aurora banner and side art, licence page, install folder, "Launch Valco Weave"). The installer also adds **Open as site in Valco Weave** to Explorer's folder right-click menu and lists Valco Weave under **Open with** for .html, .htm and .css files. The icon and wizard art come from `tools/gen_assets.py`.

The web build uses the File System Access API to edit real folders in Chrome and Edge. In other browsers it runs a demo site that's saved in the browser. FTP publishing needs the desktop app.

## How it works

- `src/lib/htmlmap.ts` parses the page with **parse5** using source locations. Each element knows exactly where its tags and attributes sit in the file.
- The Design view is a sandbox-free `iframe` rendered from the same parse, with each element tagged `data-wc-id`. Site files (CSS, images, fonts, scripts) are swapped for blob URLs, so unsaved CSS shows up immediately.
- Each visual action (setting an attribute, renaming a tag, editing text, inserting, moving, deleting) becomes one CodeMirror transaction on the source. That's why undo/redo works the same in both views and why the rest of the file is left alone.
- `src-tauri/src/lib.rs` handles file access (atomic writes), opening files in the default browser, and FTP/FTPS via `suppaftp` + `rustls`.

## Project layout

```
src/
  App.tsx                 workspace, menus, shortcuts, editing actions
  components/             DesignView, CodeEditor, CssDesigner, PropertiesPanel, FilesPanel, Panels, Dialogs
  lib/                    htmlmap, render, css, lint, inserts, templates, platform, zip, paths
src-tauri/                Rust backend (Tauri 2)
```

## Licence

MIT. Valco Weave is not affiliated with Adobe. "Dreamweaver" is a trademark of Adobe Inc. and is used here only to describe the kind of tool this is.
