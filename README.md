# Valco Office

A suite of Windows Vista-style apps for Windows 11 that replace Microsoft and other third-party programs, with Office 2007's Ribbon, Valco button and Blue / Silver / Black colour schemes.

| App | What it is | Built with |
|---|---|---|
| **Valco Word** | Word processor (.docx, .doc, .odt, .rtf) | C# / WPF, .NET 8 |
| **Valco Express** | E-mail (IMAP/POP/SMTP, .eml, mailto:) | C# / WPF, .NET 8 |
| **Valco Code** | Code editor (VS Code layout, Python/Java, Git/GitHub) | C# / WPF, .NET 8 |
| **Valco Weave** | Visual web editor (Dreamweaver-style) | Tauri + React |
| **Valco Office Setup** | Office 2007-style installer and Valco Office Update | C# / WPF, .NET 8 |

`ValcoCommon` holds the shared Ribbon, colour schemes, Valco button, Quick Access Toolbar, Valco menu and suite icons used by Word, Express and Code. Edit them there, never in the apps.

## Install
Download **ValcoOffice-Setup.exe** from the [latest release](https://github.com/Raidenplays/Valco-Office-Suite/releases/latest) and run it — it's one file with every program inside. The apps check these releases for updates once a day (and from Check for Updates); Valco Office Update downloads the new installer and updates the programs you have.

Windows 11 doesn't let installers change your default programs, so Setup ends with **Set Default Programs**: one click per app opens its page in Settings → Default apps.

## Build (Windows)
Needs the .NET 8 SDK and WiX v5 (`dotnet tool install --global wix`). Keep the folders side by side, as in this repo.

```
ValcoSetup\build-all.cmd      rebuild every app, then setup.exe
ValcoSetup\build.cmd          rebuild setup.exe from the apps' existing builds
```
The single-file installer ends up in `ValcoSetup\dist\ValcoOffice-Setup.exe` (also copied to `ValcoSetup\release`), and its release tag in `ValcoSetup\dist\release-tag.txt`.

Valco Weave's MSI is cross-built on Linux: `ValcoWeave/packaging/windows/build-msi-on-linux.sh`.

## Releasing an update
1. Bump `<Version>` in the app's project (or just rebuild — every build gets a new revision number).
2. Run `ValcoSetup\build-all.cmd`.
3. Create a GitHub release with the tag from `dist\release-tag.txt` (for example `v1.0.0.464900`) and attach only `ValcoOffice-Setup.exe`. The apps compare that tag with the installed suite version.

© 2026 ItsRaiden
