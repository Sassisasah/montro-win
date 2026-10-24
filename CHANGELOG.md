# Änderungsprotokoll

## 0.8 – 24.10.2026
Updatepaket: Installer und Verbund (0.7) mit den Branches `schneller-umschalten`,
`schriften-farben`, `vorschau-cache` und `companion-verbund` zusammengeführt (Branch
`updatepaket`). Die neuen Teile sind am Gerät noch ungetestet. Ersetzt eine installierte 0.7.

**Schneller umschalten**
- Neu (am Gerät noch ungetestet): schneller live schalten.
  - Das Log misst jedes Umschalten: `Umschalten „…“: … ms (Vorbereiten, Abdecken, Öffnen,
    Show starten, Platzieren, Aufblenden)`; Öffnen und Vorab-Öffnen stehen mit Dauer im Log.
  - Unter der Übergangsabdeckung wartet das Platzieren nicht mehr 1,5 s auf die Taskleiste
    (das Nach-oben-Holen war dort ohnehin pausiert).
  - PowerPoint startet gleich mit montro im Hintergrund, nicht erst beim ersten Live-Schalten.
  - Ohne laufende Show öffnet montro die ausgewählte Präsentation vorab; „Live“ übernimmt sie nur
    noch. Mit der neuen Einstellung „Nächste Präsentation während der Show vorab öffnen“
    (Übergang-Karte, standardmäßig aus) auch während einer Show: die ausgewählte oder die
    nächste in der Playlist.
  - Vorschaubilder machen Pause, solange eine Präsentation live geschaltet wird.

**Schriften und Farben**
- **Schrift-Check** wie am Mac: Die Analyse liest alle Schriften der Präsentation (Folien, Master,
  Layouts, Design, Diagramme) und die eingebetteten. Karte **Schriften** im Inspector mit fehlenden
  Schriften (Foliennummern) und nur eingebetteten, gelbes Schrift-Symbol in der Playlist. Geprüft
  wird gegen die GDI-Schriftliste (auch nur für die Person installierte Schriften) und die
  Office-Cloud-Schriften. ↻ liest nach einer Installation neu ein. Remote-API: `missingFonts`.
- **Farb-Check je Ausgabe:** Ist auf einem Show-Bildschirm HDR bzw. die automatische
  Farbverwaltung an oder sendet der Ausgang YCbCr statt RGB, steht neben „Ausgabe: …“ ein gelbes
  Paletten-Symbol mit Hinweis.
- **Testbild auf Ausgaben** (Paletten-Knopf oben rechts, nur ohne laufende Präsentation): gleiches
  Bild wie am Mac auf allen Show-Bildschirmen, im Verbund auf allen Rechnern. Unten stehen Rechner,
  Bildschirm, Signalformat und Bittiefe. Verbund-Befehl `testpattern`, Remote-API
  `/api/testpattern[/on|/off]`, Stand `testPattern`.

**Vorschau-Cache**
- Neu: Vorschau-Cache nach Dateiinhalt. Analyse und Vorschaubilder liegen unter der
  SHA-256-Prüfsumme statt je Playlist-Eintrag: bei einer gesicherten Show im Ordner
  `montro-Analyse` neben der `.montro`-Datei (reist mit der Show, verschwindet mit ihr), sonst und
  bei schreibgeschütztem Show-Ordner lokal in `%LOCALAPPDATA%\montro\cache`. Show öffnen, Entfernen
  und wieder Hinzufügen, Verschieben, „Fehlende Dateien suchen“ oder eine gleiche Kopie aus dem
  Verbund rendern nicht mehr neu. Nur geänderte Zeit ohne geänderten Inhalt (Kopie,
  Cloud-Abgleich) ebenfalls nicht.
- Vorschaubilder als JPEG statt PNG (etwa ein Fünftel des Platzes).
- Show-Dateien enthalten die Prüfsumme je Präsentation (`sha256`). Beim Sichern holt montro die
  Einträge der Playlist zur Show und löscht dort, was keine Show im selben Ordner mehr braucht.
- Vorhandene Vorschaubilder werden nach dem Update einmalig übernommen, ohne Rendern.
- Lokale Ausweiche: höchstens 1 GB; Einträge, die keine Playlist benutzt, nach 60 Tagen bzw.
  darüber (älteste zuerst) gelöscht.
- „Neu analysieren & Vorschaubilder erzeugen“ verwirft den Eintrag und rendert wirklich neu.
- Behoben: Der Zustand merkte sich die aktuelle Änderungszeit der Datei statt der beim Erzeugen der
  Vorschauen. Eine während des Betriebs geänderte Datei galt nach dem Neustart als aktuell.
- Cache-Einträge von vor dem Schrift-Check (auch die nach dem Update übernommenen) bekommen einmal
  eine neue Analyse mit Schriften; die Vorschaubilder bleiben, PowerPoint wird dafür nicht gebraucht.

**Verbund fernsteuern (Companion)**
- Fernsteuerung für den Verbund wie am Mac (für das Companion-Modul): `/api/refresh`
  („Aktualisieren“), `/api/cluster/detach|rejoin|toggle` am Slave, `/api/slaves/<n>/…` am Master;
  Status `refresh` und `cluster.slaves`. Während „Aktualisieren“ lehnt die Fernsteuerung Live
  schalten und Folien-Sprünge ab.
- Fix „Aktualisieren“: Der Master übernimmt Folie **und** Klickstand des Slaves und springt dabei
  nur in der eigenen PowerPoint. Vorher bekamen auch die Slaves den Sprung und verloren die schon
  gelaufenen Animationen der Folie.

## 0.7 – 24.10.2026
- Neu (Branch `meilenstein-8`): Installer (MSI) – installiert nach „Programme\montro“ mit
  Startmenü-Eintrag, richtet die Firewall-Freigabe für Fernsteuerung und Verbund ein, öffnet
  .montro-Shows per Doppelklick und ergänzt „Öffnen mit › montro“ für PowerPoint-Dateien. Updates
  ersetzen die alte Version, Einstellungen bleiben erhalten.
- Neu: Info-Fenster „Über montro“ (Menü „Show“ und Einstellungen).
- GitHub baut bei jedem Push App und Installer; ein Tag `v<version>` erzeugt ein Release.
- Behoben: „Aktualisieren“ im Verbund legte die neue Version nur erneut bereit, statt sie zu laden.

## 0.6 – 22.10.2026
- Neu (Branch `meilenstein-7`): Verbund mehrerer Rechner wie am Mac und mit montro für macOS
  mischbar – Master/Slave, Finden per DNS-SD, Befehle und Abgleich, Datei-Abgleich mit Sicherung,
  Abkoppeln zum Bearbeiten, „Aktualisieren“. Einstellungen › Verbund, Hinweisleiste über der
  Playlist, Abzeichen „Neue Version“ / „Geändert“ / „An Master“.

## 0.5 – 22.10.2026
- Neu (Branch `meilenstein-5`): Fernsteuerung per HTTP/JSON wie am Mac (Port 8765, `/api/state`,
  alle Befehle, `/api/diag`) für Companion / Stream Deck. Einstellungen-Fenster mit an/aus, Port und
  „nur von diesem Rechner“.

## 0.4 – 21.10.2026
- Neu (22.10.2026): Referent*innenansicht ohne Live-Schalten gestalten – Karte und Baukasten zeigen
  die ausgewählte Präsentation mit ihrem Preset.
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
