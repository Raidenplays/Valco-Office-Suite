# Valco Express

A native Windows e-mail client that looks and feels like Windows Mail / Outlook Express from Windows Vista.
C# / WPF on .NET 8, MailKit for IMAP + SMTP, WebView2 for showing HTML mail (scripts off, remote pictures blocked by default).

## Build

```powershell
.\build.ps1
```
Produces `publish\ValcoExpress.exe` (self-contained, no .NET install needed) and `dist\ValcoExpress-Setup.msi`.
The script installs the .NET 8 SDK for the current user if it is missing. WiX v5 is pulled from NuGet automatically.

## Layout
- `src/ValcoExpress` – the app (Vista glass frame, menus, command bar, folder tree, message list, preview pane, compose, contacts, accounts, options)
- `installer` – WiX v5 MSI project (WixUI_InstallDir with Vista-style banner and side artwork, Start Menu + desktop shortcuts, "Open with" for .eml)
- `assets` – icon and installer bitmaps (regenerate with `tools/gen_assets.py`)
- `tools/gen_icons.py` – regenerates the vector icon set (`Themes/Icons.xaml`)

User data lives in `%APPDATA%\Valco Express` (accounts, settings, contacts, Local Folders as .eml files).
Passwords are encrypted with Windows DPAPI for the current user.
