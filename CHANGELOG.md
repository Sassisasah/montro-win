# Änderungsprotokoll

## 0.4 – 21.10.2026
- Neu (22.10.2026): Schleife für Videos in der Video-Karte (Wunsch der Nutzerin, gibt es am Mac nicht).
- Neu (22.10.2026): Show-Dateien `.montro` wie am Mac (öffnen, sichern, mit Präsentationen
  sichern, fehlende Dateien suchen), mit montro für macOS austauschbar.
- Neu (22.10.2026): „In PowerPoint bearbeiten“ (Stift, Kontextmenü) mit Dateiwächter – nach dem
  Speichern erneuert montro Analyse und Vorschaubilder. Preset-Zuweisung im Kontextmenü.
- Neu (22.10.2026): Video-Karte im Inspector wie am Mac – laufendes Video mit Pause/Stopp,
  Videos der Folie mit Start und „Abspielen“.
- Neu (22.10.2026): Video-Laufzeit in der Referent*innenansicht (Name, Gesamt, Rest, Fortschritt),
  sobald auf der Live-Folie ein Video läuft.
- Neu (22.10.2026): Baukasten der Referent*innenansicht wie am Mac mit Presets je Präsentation,
  Entwurf während der Show, Notizen-Schriftgröße im Preset.
- Neu (Etappe 3, 22.10.2026): Referent*innenansicht wie am Mac (Timer, Uhr, aktuelle und nächste
  Folie, Foliennummer mit Animationspunkten, Notizen, Regie-Nachricht) auf eigenem Bildschirm
  oder im Popup-Fenster; Popup-Modus mit Referent*innenansicht links und PowerPoint rechts.
- Während eine Präsentation live ist, schaltet Windows die Bildschirme nicht mehr ab.
- Live-Bild: Die aktuelle Folie in Vorschau und Referent*innenansicht zeigt das Show-Fenster
  flüssig mit Animationen und laufendem Video (DWM-Miniatur), auch im Popup-Modus. Das
  LIVE-Abzeichen steht jetzt neben „Live-Folie“.
- Neu (Branch `meilenstein-4`, Etappe 2): Timer-Einstellungen, Regie-Nachricht mit Vorlagen,
  Farben und Blinken (in der Referent*innenansicht), Übergang zwischen Präsentationen (Hart /
  Über Schwarz / Überblenden), Publikumsbildschirm schwarz, wenn nichts live ist, beim Start,
  bei Blackscreen und am Ende. Ausgabe während der Show umstellen startet an gleicher Folie neu.
- Dunkle Kontextmenüs und Bildlaufleisten wie am Mac.
- Taskleiste auf dem Publikumsbildschirm wird ausgeblendet, solange er Ausgabe ist (wie Dock und
  Menüleiste am Mac). Kein Aufblitzen mehr beim Live-Schalten.

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
