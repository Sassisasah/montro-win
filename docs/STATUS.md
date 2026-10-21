# Status

## Getestet

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
- **21.10.2026** – Parser: noch nicht geprüft sind verknüpfte (nicht eingebettete) Videos,
  Videos mit Trigger-Start und andere Videoformate als MP4/MOV (Länge dann 0 = unbekannt).
  Abgleich mit dem Mac-Ergebnis für dieselben Dateien steht aus.
- Der Selbsttest kann nicht sehen, was ein Monitor wirklich anzeigt. Sichtprüfung bleibt nötig.
- **20.10.2026** – Während einer Pause schaltete Windows die Bildschirme ab, die Verbatims
  meldeten sich ab („No signal“). Für Meilenstein 4: Abschaltung während einer Show verhindern
  (`SetThreadExecutionState(ES_DISPLAY_REQUIRED)`).
- Eintrag in der Projekttabelle von `Sassisasah/Developer-Setup` (`README.md`).
