# ValcoCommon

Shared source for every native Valco Office app (Valco Word, Valco Spreadsheets, Valco Express, Valco Code).
Apps link these files from their .csproj, so there is one copy of:

- `Ribbon.cs` — Office 2007 Ribbon (`RibbonBar`, `RibbonTab`, `RB` control factory) and `OfficeTheme` (Blue / Silver / Black schemes).
- `ValcoChrome.cs` — the Valco button (orb with the suite mark), Quick Access Toolbar, the Valco menu (`AppMenu`) and `Suite` helpers (Valco Office Update, user name, Default apps).
- `SuiteIcons.xaml` — the suite's Vista-style vector icons (IconNew, IconSave, IconPaste...).
- `ValcoOffice-mark.png` — the suite mark.

Keep this folder next to the app folders in Documents (Documents\ValcoCommon, Documents\ValcoWord, ...).
Edit here, never in the apps.
