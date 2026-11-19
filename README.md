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
Download **ValcoOffice-Setup.zip** from the [latest release](https://github.com/Raidenplays/Valco-Office-Suite/releases/latest), extract it and run `setup.exe`. Valco Office Update (Start → Valco Office → Valco Office Tools) keeps the apps up to date from these releases.

Windows 11 doesn't let installers change your default programs, so Setup ends with **Set Default Programs**: one click per app opens its page in Settings → Default apps.

## Build (Windows)
Needs the .NET 8 SDK and WiX v5 (`dotnet tool install --global wix`). Keep the folders side by side, as in this repo.

```
ValcoSetup\build-all.cmd      rebuild every app, then setup.exe
ValcoSetup\build.cmd          rebuild setup.exe from the apps' existing builds
```
Setup ends up in `ValcoSetup\dist\Valco Office`; the files for a GitHub release (setup.exe, the app packages and `valco-office.json`) in `ValcoSetup\release`.

Valco Weave's MSI is cross-built on Linux: `ValcoWeave/packaging/windows/build-msi-on-linux.sh`.

## Releasing an update
1. Bump `<Version>` in the app's project (or just rebuild — every build gets a new revision number).
2. Run `ValcoSetup\build-all.cmd`.
3. Create a GitHub release and upload everything in `ValcoSetup\release`. Valco Office Update reads `valco-office.json` from the latest release.

© 2026 ItsRaiden
