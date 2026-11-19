# ValcoNet

A web browser for Windows 11 that looks and works like **Internet Explorer 8 on Windows Vista**, running on the
modern **Gecko** engine (the engine inside Firefox ESR), so every current website works.

![ValcoNet](assets/ValcoNet-256.png)

## What it has

**The IE8 frame**
- Vista Aero glass title bar and navigation row (Windows 11's acrylic backdrop shows through, or painted glass if you turn translucency off), glass caption buttons with the red Close.
- Round glass Back/Forward orbs with the Recent Pages dropdown.
- Address bar with the page icon, padlock "Security Report", autocomplete dropdown, IE's red bar on certificate errors, and Refresh/Stop glued to its right.
- Search box (shows your search engine's name) with the engine menu.
- Favorites bar with the **Favorites** button and the green **Add to Favorites Bar** star.
- IE8 tabs, colour-coded tab groups for tabs opened from the same page, Quick Tabs (Ctrl+Q) with live thumbnails, and the Tab List.
- Command bar: Home ▾, Feeds ▾ (turns orange when a page has RSS/Atom), Read Mail ▾, Print ▾, Page ▾, Safety ▾, Tools ▾, Help ▾.
- Status bar: status text, green progress bar, pop-up blocked and padlock panes, zone ("Internet | Protected Mode: On"), zoom (click to cycle 100/125/150 %).
- The menu bar appears with Alt, like IE7/8 (Favorites instead of Bookmarks).

**IE features**
- **Favorites Center** (Alt+C): Favorites, Feeds and History (by date, site, most visited, today, search). It flies out over the page and can be pinned.
- **Feeds**: feed detection, IE7's feed reading view with sorting, search and categories, subscriptions, and headlines on ValcoNet Home.
- **InPrivate Browsing** (Ctrl+Shift+P) with the blue InPrivate badges and page.
- **New tab page**: "What do you want to do next?" — reopen closed tabs, reopen last session, use copied text, browse InPrivate.
- **Internet Options** (General, Security, Privacy, Content, Connections, Programs, Advanced), **Delete Browsing History**, **Add a Favorite**, **Add or Change Home Page** and **About ValcoNet**, all as Vista dialogs wired to real settings.
- **Read Mail** opens your default mail program (Valco Express if it's your default).
- Error pages and every "Firefox" name are rebranded to ValcoNet.

**Keyboard**: Alt+C Favorites Center · Ctrl+Shift+I/H/J pin Favorites/History/Feeds · Ctrl+Q Quick Tabs · Alt+P/S/O/L Page/Safety/Tools/Help menus · Alt+J Feeds · Alt+R Print · Alt+Z Add to Favorites · Ctrl+Shift+Del Delete Browsing History · F12 Developer Tools.

## How it's built

ValcoNet is a layer on top of an unmodified Gecko engine:

```
ValcoNet\
  valconet.exe           launcher: starts the engine with ValcoNet's own profile
  engine\                Firefox ESR's files, with:
    valconet.cfg           privileged bootstrap (Gecko autoconfig)
    defaults\pref\         points Gecko at valconet.cfg
    distribution\          policies: no Mozilla updates, telemetry, studies or onboarding
    valconet\              the ValcoNet layer (chrome.manifest, skin, pages, modules)
```

- `app/valconet/modules/ValcoNet.sys.mjs` sets ValcoNet's defaults, swaps in ValcoNet's branding, and dresses every browser window.
- `app/valconet/content/valconet-window.js` builds the title row, status bar, menus, Quick Tabs and Favorites Center; `ValcoWidgets.sys.mjs` adds the toolbar widgets through Gecko's CustomizableUI.
- `app/valconet/skin/valconet.css` is the IE8/Vista look. All artwork is original (`tools/gen_icons.py`).
- The profile lives in `%APPDATA%\Valco\ValcoNet\Profile` (or `.\Profile` in the portable build), so ValcoNet never touches an installed Firefox.

## Build (Windows)

```
build.cmd              ->  dist\ValcoNet (packed by Valco Office Setup) + dist\ValcoNet-<ver>-portable-x64.zip
build.cmd -Msi         ->  also dist\ValcoNet-Setup.msi (standalone installer, needs the .NET 8 SDK)
```

The build downloads the latest Firefox ESR for the engine every time, so **rebuilding is how ValcoNet picks up engine security fixes** — rebuild and re-release regularly. It needs nothing beyond Windows itself: 7-Zip and MSVC/MinGW are used if you have them, otherwise Windows' `tar` unpacks the engine and `launcher\prebuilt\valconet.exe` is used. `ValcoSetup\build-all.cmd` builds ValcoNet along with the other apps.

Version: `valconet.json`.

## Developing on Linux

`tools/dev-run.sh start fresh` runs Linux Firefox ESR with the ValcoNet layer linked in (under Xvfb, with Marionette on port 2828); `tools/vn.py js '…'` runs chrome JavaScript in it and `tools/vn.py shot out.png` takes a screenshot. Gecko's chrome is the same on both platforms, so this covers everything except the Windows-only bits (acrylic glass, the launcher, registry).

## Notices

ValcoNet is not affiliated with Mozilla or Microsoft. See [NOTICE.md](NOTICE.md) and [LICENSE](LICENSE) (MPL 2.0).

© 2026 ItsRaiden
