# Änderungsprotokoll

## 0.1 – 20.10.2026
- Neu: Repo `montro-win` mit Doku-Gerüst und verbindlichem Kompatibilitätsvertrag zur
  Mac-Version (Remote-API für Companion, Verbund Mac ↔ Windows).
- Neu (Branch `meilenstein-1`, ungetestet): WPF-Gerüst (.NET 8, PerMonitorV2, movidal-Farben),
  PowerPoint-Kern per COM auf eigenem STA-Thread und Testfenster „PowerPoint testen“. Es kann
  öffnen, die Show auf einem gewählten Bildschirm starten, die Lage prüfen und korrigieren,
  weiter/zurück/gehe zu, Schwarz, beenden und Vorschaubilder exportieren. Dazu kommt ein Build
  über GitHub Actions.
- Neu: Selbsttest `montro.exe --selbsttest` prüft den PowerPoint-Kern auf allen Bildschirmen
  automatisch, mit Bildschirmfotos und Bericht. Auf dem Test-PC: 65 von 65 Prüfungen ok.
- Neu: App-Symbol wie am Mac.
- Behoben: Lage des Show-Fensters ließ sich in PowerPoint 365 nicht messen (`HWND` fehlt).
- Behoben: Taskleiste lag auf Nebenbildschirmen über der Show.
- Behoben: Monitore hießen „Generic PnP Monitor“, jetzt mit echtem Namen (z. B. „Verbatim MT15“).
- Behoben: Umlaute im Log wurden in Windows PowerShell falsch angezeigt.
- Behoben: Die Bildschirm-Auswahl im Testfenster war weiß auf weiß (ComboBox jetzt dunkel).
