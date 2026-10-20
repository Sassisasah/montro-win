# Änderungsprotokoll

## 0.1 – 20.10.2026
- Neu: Repo `montro-win` mit Doku-Gerüst und verbindlichem Kompatibilitätsvertrag zur
  Mac-Version (Remote-API für Companion, Verbund Mac ↔ Windows).
- Neu (Branch `meilenstein-1`, ungetestet): WPF-Gerüst (.NET 8, PerMonitorV2, movidal-Farben),
  PowerPoint-Kern per COM auf eigenem STA-Thread und Testfenster „PowerPoint testen“. Es kann
  öffnen, die Show auf einem gewählten Bildschirm starten, die Lage prüfen und korrigieren,
  weiter/zurück/gehe zu, Schwarz, beenden und Vorschaubilder exportieren. Dazu kommt ein Build
  über GitHub Actions.
