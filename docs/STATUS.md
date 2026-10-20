# Status

## Getestet

- **20.10.2026** – Meilenstein 1 (Branch `meilenstein-1`) mit dem Selbsttest
  (`montro.exe --selbsttest`, siehe [BEDIENUNG.md](BEDIENUNG.md#selbsttest)) auf dem Test-PC
  geprüft: **65 von 65 Prüfungen ok**, in mehreren Läufen hintereinander.
  - Test-PC: Windows 11 Pro (Build 26200), Microsoft PowerPoint 16.0 (Microsoft 365,
    Build 19929), .NET-Laufzeit 10.0.13 (montro für .NET 8, gestartet mit
    `DOTNET_ROLL_FORWARD=Major`).
  - Bildschirme: Laptop 1920×1080 (Hauptbildschirm) und zwei Verbatim MT15 mit je 1920×1080,
    alle drei bei diesem Lauf mit 100 % Skalierung.
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

- **20.10.2026** – Selbsttest mit **unterschiedlicher Skalierung** wiederholen (Verbatims auf
  150 %, Laptop auf 100 %). Beim ersten Start standen die Verbatims auf 150 %, beim Testlauf
  meldete Windows 100 %.
- **20.10.2026** – Von Hand prüfen (montro per Doppelklick starten, nicht aus der
  Claude-Sitzung): Testfenster › „Vorschaubilder exportieren“ schreibt nach
  `%LOCALAPPDATA%\montro\thumbs\test`. In der Claude-Sitzung werden Schreibzugriffe auf AppData
  umgeleitet, und PowerPoint sah den Ordner dort nicht. Der Selbsttest arbeitet deshalb unter
  `%TEMP%`.
- PowerPoint ohne Lizenz: lässt sich auf dem Test-PC nicht nachstellen.
- Sichtprüfung durch die Nutzerin auf den drei Bildschirmen.
- Eintrag in der Projekttabelle von `Sassisasah/Developer-Setup` (`README.md`).
