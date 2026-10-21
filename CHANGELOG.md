# Änderungsprotokoll

## 0.3 – 21.10.2026
- Neu (Branch `meilenstein-4`, Etappe 1): Hauptfenster wie am Mac. Playlist mit Vorschaubild,
  „x Folien · y ausgeblendet · z Videos“, Status und LIVE-Markierung; Hinzufügen per Knopf,
  Ziehen & Ablegen oder Kommandozeile; Reihenfolge per Ziehen; Folienübersicht; „Ausgewählte
  live schalten“ und „Stopp“; Inspector mit Live-Folie (Bildschirmwahl, Zurück/Weiter,
  Blackscreen), vorläufiger Referent*innenansicht, Timer und „Am Ende der Präsentation“
  (Ende-Sperre); Statuszeile; Tastenkürzel wie am Mac. Die Playlist wird gespeichert.
- Das Testfenster „PowerPoint testen“ ist jetzt über Strg+Umschalt+P erreichbar.

## 0.2 – 21.10.2026
- Neu (Branch `meilenstein-3`): PPTX-Parser, portiert aus der Mac-Version. Liest ohne
  PowerPoint Folienreihenfolge, ausgeblendete Folien, Notizen, Klick-Animationen und Videos mit
  Länge und Startklick. Das Testfenster zeigt das Ergebnis an.
- Neu: `--selbsttest --parser <dateien…>` vergleicht den Parser mit PowerPoint. Auf dem Test-PC
  mit 7 echten Präsentationen und 2 Testpräsentationen: keine Abweichung.

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
- Behoben: „Show starten“ ein zweites Mal schlug fehl („Object does not exist“), weil PowerPoint
  die Präsentation mit dem Show-Ende schließt. montro öffnet sie jetzt neu.
- Neu: Selbsttest mit eigener Präsentation (`--datei`).
- Behoben: Auf den externen Monitoren (Verbatim MT15 per USB-C) kam nur Schwarz oder ein
  kaputtes Hintergrundbild an. Das Show-Fenster ist jetzt 1 Pixel niedriger als der Bildschirm.
