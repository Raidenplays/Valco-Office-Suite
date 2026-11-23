# Valco Office

A suite of Windows Vista-style apps for Windows 11 that replace Microsoft and other third-party programs, with Office 2007's Ribbon, Valco button and Blue / Silver / Black colour schemes.

| App | What it is | Built with |
|---|---|---|
| **Valco Word** | Word processor (.docx, .doc, .odt, .rtf) | C# / WPF, .NET 8 |
| **Valco Express** | E-mail (IMAP/POP/SMTP, .eml, mailto:) | C# / WPF, .NET 8 |
| **Valco Code** | Code editor (VS Code layout, Python/Java, Git/GitHub) | C# / WPF, .NET 8 |
| **Valco Weave** | Visual web editor (Dreamweaver-style) | Tauri + React |
| **ValcoNet** | Web browser (Internet Explorer 8 style) on the Gecko engine | Gecko (Firefox ESR) + JS/CSS layer, C launcher |
| **Valco Office Setup** | Office 2007-style installer and Valco Office Update | C# / WPF, .NET 8 |

`ValcoCommon` holds the shared Ribbon, colour schemes, Valco button, Quick Access Toolbar, Valco menu and suite icons used by Word, Express and Code. Edit them there, never in the apps.

## Install
Download **ValcoOffice-Setup.exe** from the [latest release](https://github.com/Raidenplays/Valco-Office-Suite/releases/latest) and run it — it's one file with every program inside. The apps check these releases for updates once a day (and from Check for Updates); Valco Office Update downloads only the programs that changed (each release also carries every program as its own file) and updates the ones you have.

Windows 11 doesn't let installers change your default programs, so Setup ends with **Set Default Programs**: one click per app opens its page in Settings → Default apps.

## Build (Windows)
Needs the .NET 8 SDK and WiX v5 (`dotnet tool install --global wix`). Keep the folders side by side, as in this repo.

```
ValcoSetup\build-all.cmd      rebuild every app, then setup.exe
ValcoSetup\build.cmd          rebuild setup.exe from the apps' existing builds
```
The single-file installer ends up in `ValcoSetup\dist\ValcoOffice-Setup.exe` (also copied to `ValcoSetup\release`), and its release tag in `ValcoSetup\dist\release-tag.txt`.

Valco Weave's MSI is cross-built on Linux: `ValcoWeave/packaging/windows/build-msi-on-linux.sh`.

ValcoNet's build downloads the latest Firefox ESR as its engine each time, so rebuilding is how it gets engine security fixes — see `ValcoNet/README.md`.

## Releasing an update
Releases are automatic. **Push to `main`** and GitHub Actions (`.github/workflows/release.yml`) does the rest:

1. `ValcoSetup/ci/plan.py` compares each program's sources (`sourceDirs` in `ValcoSetup/apps.json`) with the hash stored in the latest release's `valco-office.json`.
2. Only the programs that changed are rebuilt (Valco Weave on Linux, everything else on Windows). Unchanged programs are copied from the previous release with the same file and version, so Valco Office Update doesn't download them again.
3. A release tagged `v1.0.0.<revision>` goes up with `ValcoOffice-Setup.exe`, `ValcoOffice-Updater.exe`, `valco-office.json` and each program's .zip/.msi and .ico. Release notes list each changed program's commit messages; Valco Office Update shows the same list.

- Pushes that only change Markdown files don't make a release; neither does a push where nothing a program is built from changed.
- Every Monday the workflow also checks for a new Firefox ESR and rebuilds ValcoNet with it (engine security fixes).
- **Actions → Release Valco Office → Run workflow** starts one by hand; tick *Rebuild everything* for a full rebuild.
- Write commit messages as `Valco Word: what changed` — the app name is dropped in that program's list.
- A manual release still works: run `ValcoSetup\build-all.cmd` and upload every file in `ValcoSetup\release` with the tag from `dist\release-tag.txt`.

© 2026 ItsRaiden
