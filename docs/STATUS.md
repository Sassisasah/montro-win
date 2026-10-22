# Status

## Getestet

- **22.10.2026** – Live-Bild per Bildschirmfoto: erst schwarz (ohne `CAPTUREBLT`), dann sichtbar,
  aber laut Nutzerin „eher ein Daumenkino“ (2 Bilder/s, Effekte und Video nicht flüssig).
  Umgebaut auf DWM-Miniatur des Show-Fensters. Geprüft mit `montro.exe --miniaturtest`: Die
  Miniatur erscheint auch im „layered“ Ausgabefenster, im Seitenverhältnis eingepasst. Test auf
  dem Verbatim steht aus.

- **22.10.2026** – Referent*innenansicht auf dem zweiten Verbatim: läuft (Nutzerin: „geht“).
  Die aktuelle Folie war extrem verpixelt: Der Bildspeicher gab das 280-px-Bild der
  Folienkacheln heraus. Behoben, der Speicher unterscheidet jetzt nach Bildbreite. Danach von
  der Nutzerin bestätigt: Folie scharf („geht“). Regie-Nachricht erscheint dort ebenfalls
  („regie geht auch“).
- **22.10.2026** – Meilenstein 4, Etappe 3 (Referent*innenansicht), Testbilder ohne PowerPoint
  (`montro.exe --referentenbild`, Technik-Testdeck mit den echten Vorschaubildern): Aufbau wie
  der Mac-Standard „Windows“ – Timer, Uhr oben, aktuelle Folie groß links, „Folie 3 von 14“ mit
  Animationspunkten, Regie-Nachricht darunter, rechts „Nächste Folie“ und „Notizen“. Letzte
  Folie: „Ende der Präsentation“. Popup-Größe 1120×660 passt sich an. Nicht live: Ruhe-Ansicht
  mit Uhr. Auf dem Verbatim noch nicht geprüft.

- **21.10.2026** – Meilenstein 4, Etappe 2, über UI Automation mit Bildschirmfotos von Verbatim 2:
  Ausgabe auf „Verbatim MT15 (2)“ gestellt → Verbatim schwarz; Technik-Testdeck live → Folie
  auf Verbatim 2 (1920×1079, Lage ok); Blackscreen → schwarz, wieder aus → Folie; zweite
  Präsentation live mit „Über Schwarz“ → während des Wechsels schwarz, danach neue Folie;
  Stopp → schwarz. Timer-Einstellungen, Regie-Nachricht und Übergang sichtbar wie am Mac.
- **21.10.2026** – Sichtprüfung (Video der Nutzerin): gewählter Verbatim schwarz, dann die Folie;
  der andere Verbatim bleibt unberührt. Vorher war das Schwarzbild auf 150 % um den Faktor 1,5
  zu groß (WPF reagierte auf WM_DPICHANGED) und ragte auf den anderen Verbatim – behoben.
- **21.10.2026** – Live-Schalten ohne Aufblitzen (gemessen alle 120 ms; vorher schob sich
  PowerPoint kurz über die Abdeckung) und Taskleiste bleibt unter dem Schwarzbild (Schwarzbild
  wird alle 250 ms nach oben geholt). Von der Nutzerin bestätigt („passt“).
- **21.10.2026** – Übergänge „Hart“ (kurz Schwarz, dann neue Folie) und „Über Schwarz“ (alte
  Folie blendet ab, Schwarz, neue Folie) auf dem Verbatim, mehrere Wechsel hintereinander, Videos
  der Nutzerin. „Überblenden“ ebenso: alte Folie bleibt als Standbild stehen und blendet direkt
  in die neue über (auf den Einzelbildern beide überlagert), ohne Schwarz dazwischen.
- **21.10.2026** – Nach den Korrekturen (Taskleiste ausgeblendet, Schwarzbild dauerhaft unter der
  Show, scharfes Standbild): Nutzerin „gefällt mir alles gut“. Letzter Rest: kleines Flackern
  beim Wechsel von der laufenden Folie ins Standbild – Standbild wird jetzt sofort statt
  eingeblendet gezeigt. Von der Nutzerin bestätigt: „perfekt“.
- **22.10.2026** – Ende der Präsentation funktioniert (Nutzerin: „geht“). Vorher lief die Show
  über das Ende hinaus und PowerPoint fragte „Speichern?“ (blockierte dann alles) – behoben.
- **22.10.2026** – Video (Technik-Testdeck, Folie 7) läuft flüssig. Ein einmaliges Ruckeln lag
  laut Nutzerin vermutlich an etwas anderem im Hintergrund.

- **21.10.2026** – Meilenstein 4, Etappe 1 (Branch `meilenstein-4`), über UI Automation
  bedient: 4 Präsentationen per Kommandozeile hinzugefügt, alle in 23 s analysiert und mit
  Vorschaubildern „bereit“; Technik-Testdeck ausgewählt, „Ausgewählte live schalten“ im
  Popup-Modus (PowerPoint 960×540 rechts auf dem Laptop, Lage ok), 2× „Weiter“ → Statuszeile
  „Folie 3 von 14 · Animation 0/5“, Folienübersicht markiert Folie 3, „Stopp“ → „Gestoppt –
  Ausgänge schwarz“. Bildschirmfoto im Aussehen nah an der Mac-Vorlage.
- **21.10.2026** – Sichtprüfung durch die Nutzerin: Live schalten aus dem Hauptfenster auf einen
  Verbatim funktioniert („mega“). Vorher war das Bildschirm-Menü weiß auf weiß (behoben,
  Kontextmenüs jetzt dunkel).

- **21.10.2026** – Meilenstein 3 (Branch `meilenstein-3`): PPTX-Parser gegen PowerPoint
  verglichen (`--selbsttest --parser`), **0 Abweichungen**:
  - 7 echte Präsentationen vom Desktop der Nutzerin (1 bis 24 Folien, bis 408 MB): Folienzahl,
    ausgeblendete Folien (bis 4 je Datei), Notizen (bis 17 Folien je Datei), Klick-Animationen
    (bis 9), 2 eingebettete Videos mit Länge 198,1 s und 168,1 s (wie PowerPoint), Start mit
    Klick 1. Lesezeit 0,0–1,6 s.
  - Testpräsentation aus dem Selbsttest (ausgeblendete Folien, 2 Klicks).
  - Eigene Parser-Testpräsentation: Klick gemischt mit „Mit/Nach vorherigem“, Trigger-Animation,
    ausgeblendete Folie, Notizen mit Absätzen, Zeilenumbruch und Umlauten. Ergebnis 2/0/1/0
    Klicks wie erwartet.
  - Gesamter Selbsttest danach: 77 von 77 ok.
  - **21.10.2026** – „Technik-Testdeck-movidal.pptx“ (14 Folien, von der Nutzerin): Notizen auf
    allen 14 Folien, 13 Klick-Animationen, eingebettetes MP4 „Testvideo“ 10,0 s mit Start 0
    (automatisch, wie in den Notizen beschrieben). Alles gleich wie PowerPoint.

- **20.10.2026** – Sichtprüfung durch die Nutzerin (eigenständige Version aus `publish`, per
  Doppelklick gestartet). Erster Versuch: Die Bildschirm-Auswahl war weiß auf weiß (behoben,
  ComboBox dunkel). Laut Fotos und Log:
  - Erster Start auf Verbatim 2 lief (Lage ok), Zustand aber „pausiert“ und kein Bild sichtbar.
  - Zweites „Show starten“: „Object does not exist“ – behoben (Präsentation wird neu geöffnet).
  - Das „Pixelgewitter“ war ein PowerPoint-Dialog auf dem Verbatim (150 %), unleserlich
    gezeichnet. Solange er offen war, meldete montro „PowerPoint antwortet nicht … Dialog
    offen?“. Inhalt des Dialogs noch unbekannt; vermutlich hat er die Show pausiert.
  - Nachgestellt mit derselben Datei (`--selbsttest --datei …`, 15 Folien, 720×405 pt): auf
    allen drei Bildschirmen je zweimal gestartet, Lage ok, Show läuft, Bildschirmfoto korrekt.
  - **Aber:** Auf den Verbatims selbst kam nur Schwarz, eine kaputte Taskleiste oder ein
    zerstückeltes Hintergrundbild an – auch mit PowerPoint allein. Die Bildschirmfotos (DWM)
    zeigten die Folie, sie prüfen also nicht, was der Monitor wirklich anzeigt.
  - Test C/D auf Verbatim 2: Show-Fenster 1 Pixel niedriger → Bild kommt an; exakt
    bildschirmfüllend → kein Bild. montro legt das Fenster jetzt 1 Pixel niedriger.
  - **21.10.2026 – Sichtprüfung ok:** Mit dieser Version (Fenster 1920×1079) sind die Folien
    auf beiden Verbatims sauber zu sehen, Weiter/Zurück/Schwarz funktionieren.
  - **21.10.2026:** „Vorschaubilder exportieren“ aus dem per Doppelklick gestarteten montro
    funktioniert (nach `%LOCALAPPDATA%\montro\thumbs\test`).

- **20.10.2026** – Meilenstein 1 (Branch `meilenstein-1`) mit dem Selbsttest
  (`montro.exe --selbsttest`, siehe [BEDIENUNG.md](BEDIENUNG.md#selbsttest)) auf dem Test-PC
  geprüft: **65 von 65 Prüfungen ok**, in mehreren Läufen hintereinander.
  - Test-PC: Windows 11 Pro (Build 26200), Microsoft PowerPoint 16.0 (Microsoft 365,
    Build 19929), .NET-Laufzeit 10.0.13 (montro für .NET 8, gestartet mit
    `DOTNET_ROLL_FORWARD=Major`).
  - Bildschirme: Laptop 1920×1080 (Hauptbildschirm) und zwei Verbatim MT15 mit je 1920×1080.
    Geprüft mit allen drei auf 100 % und mit **gemischter Skalierung** (Laptop 100 %, Verbatims
    150 %): Lage in beiden Fällen ohne Korrektur exakt, Fenster-DPI 144 auf den Verbatims.
  - Build auf GitHub Actions grün (Lauf 39466002669).
  - Geprüft auf **jedem der drei Bildschirme**: Öffnen ohne Dokumentfenster, Show-Typ Referent
    mit ausgeschalteter PowerPoint-Referentenansicht, Lage gemessen und per Bildschirmfoto
    (Folie füllt den Bildschirm bis in die Ecken), Weiter mit zwei Klick-Animationen, Weiter
    und Zurück überspringen ausgeblendete Folien, Gehe zu (gezählt ohne ausgeblendete), Schwarz
    an/aus (auch im Bildschirmfoto), Endbildschirm erkannt, Beenden.
  - Kiosk-Modus: Show-Typ Kiosk, Lage, Weiter, Gehe zu. Mit `--kiosk-minuten 6`: Nach 6 min
    ohne Eingabe steht die Show noch auf derselben Folie, **kein Rücksprung** auf Folie 1.
  - Verwaiste Shows: Eine neue Show beendet vorher alle alten, „Verwaiste Shows beenden“ beendet
    alle (auch fremde).
  - PNG-Export: alle 6 Folien inklusive ausgeblendeter, 1280×720, 0,4 s.
  - Offener Dialog in PowerPoint: Der Befehl kommt nach 5 s mit „PowerPoint antwortet nicht …
    Ist dort ein Dialog offen?“ zurück, die Oberfläche friert nicht ein. Nach dem Schließen des
    Dialogs ist die Show wieder bedienbar.
  - Datei aus dem Internet (Zone.Identifier ZoneId=3): PowerPoint öffnet sie per Automation
    direkt, ohne Geschützte Ansicht. Die Show startet normal.
  - `GotoSlide` auf eine ausgeblendete Folie zeigt sie an (zur Info, so verhält sich PowerPoint).
- **20.10.2026** – Dabei gefunden und behoben:
  - `SlideShowWindow.HWND` gibt es in PowerPoint 365 (Build 19929) nicht. montro konnte die Lage
    deshalb nicht messen. Jetzt sucht montro das Fenster der Klasse `screenClass` von
    `POWERPNT.EXE` über den Präsentationsnamen.
  - Auf den Nebenbildschirmen lag die **Taskleiste über der Show**. Das Show-Fenster liegt jetzt
    über allem (topmost) und wird nach jedem Befehl und alle 200 ms wieder nach oben geholt.
  - Monitornamen kamen als „Generic PnP Monitor“. Jetzt liest montro den Namen aus den
    EDID-Daten („Verbatim MT15“).
  - Das Log war UTF-8 ohne BOM, Windows PowerShell 5.1 zeigte Umlaute falsch. Jetzt mit BOM.
- **20.10.2026** – Branch `meilenstein-1` baut fehlerfrei auf GitHub Actions (`windows-latest`).
  Das Artefakt `montro-win-x64` ist eigenständig (ohne .NET-Installation lauffähig). Lokal baut
  das .NET-SDK 10 das Projekt für .NET 8 ohne Zusatzinstallation.

## Offen

- PowerPoint ohne Lizenz: lässt sich auf dem Test-PC nicht nachstellen.
- **22.10.2026** – Meilenstein 4, Etappe 3: Referent*innenansicht auf dem zweiten Verbatim mit
  Timer-Blinken und Ende auf Schwarz prüfen; Popup-Modus (links
  Referent*innenansicht, rechts PowerPoint). Video-Laufzeit bleibt leer, bis die
  Video-Erkennung kommt.
- **22.10.2026** – Live-Bild (DWM-Miniatur) in Live-Folie-Karte und Referent*innenansicht auf dem
  Verbatim und im Popup-Modus prüfen: Animationen, Video flüssig, Schwarz/Übergang.
- **21.10.2026** – Meilenstein 4: noch nicht geprüft sind Timer-Einstellungen und Ziehen &
  Ablegen. Offen: Builder und Presets der Referent*innenansicht, Video-Karte, „In PowerPoint
  bearbeiten“, „Aktualisieren“, Show-Dateien `.montro`.
- **22.10.2026** – Sperre gegen Bildschirm-Abschaltung während der Show eingebaut (Log: „Bildschirm-
  Abschaltung gesperrt“), noch nicht über die eingestellte Abschaltzeit hinaus geprüft.
- **21.10.2026** – Parser: noch nicht geprüft sind verknüpfte (nicht eingebettete) Videos,
  Videos mit Trigger-Start und andere Videoformate als MP4/MOV (Länge dann 0 = unbekannt).
  Abgleich mit dem Mac-Ergebnis für dieselben Dateien steht aus.
- Der Selbsttest kann nicht sehen, was ein Monitor wirklich anzeigt. Sichtprüfung bleibt nötig.
- **20.10.2026** – Während einer Pause schaltete Windows die Bildschirme ab, die Verbatims
  meldeten sich ab („No signal“). Seit 22.10.2026 verhindert montro das, solange eine
  Präsentation live ist.
- Eintrag in der Projekttabelle von `Sassisasah/Developer-Setup` (`README.md`).
