# Bedienung

Ziel ist dieselbe Bedienung wie am Mac (siehe `docs/BEDIENUNG.md` im Repo `Sassisasah/montro`),
nur mit Strg statt ⌘. Dieser Abschnitt wächst mit jedem Meilenstein.

## PowerPoint testen (Meilenstein 1)

Hauptfenster › **PowerPoint testen …** öffnet ein Testfenster für den PowerPoint-Kern:

1. **Datei wählen …**: eine `.pptx`, `.pptm`, `.ppsx` oder `.ppsm`. montro öffnet sie
   schreibgeschützt in PowerPoint. Oben steht die PowerPoint-Version.
2. **Bildschirm** wählen, auf dem die Show laufen soll. Die Liste zeigt jeden Monitor mit Name,
   Auflösung, Skalierung und Lage. „Aktualisieren“ liest die Monitore neu ein.
3. Optionen:
   - **Kiosk-Modus**: Show-Typ „Kiosk“ statt „Referent“. PowerPoints eigene
     Referentenansicht ist in beiden Fällen aus.
   - **Mit Dokumentfenster öffnen**: PowerPoint zeigt die Datei zusätzlich im Bearbeitungsfenster.
     Das ist nur zum Testen gedacht, falls die Show ohne Fenster nicht startet.
   - **Monitor in PowerPoint vorwählen**: setzt zusätzlich PowerPoints eigene Einstellung
     „Monitor“ (Registry), bevor die Show startet.
4. **Show starten**, dann **◀ Zurück**, **Weiter ▶**, **Gehe zu** (Nummer wie in „Folie x von n“,
   also ohne ausgeblendete Folien), **Schwarz**, **Show beenden**.
5. **Lage prüfen** misst das Show-Fenster und legt es bei Abweichung erneut auf den Bildschirm
   (bis zu 4 Versuche).
6. **Vorschaubilder exportieren** schreibt `slide-<n>.png` (1280 px breit) nach
   `%LOCALAPPDATA%\montro\thumbs\test\` und öffnet den Ordner.
7. **Verwaiste Shows beenden** schließt alle Bildschirmpräsentationen, die in PowerPoint noch
   laufen, etwa nach einem Absturz.

Die Statuszeile zeigt Folie x von n, den PowerPoint-Index, den Klick in der Folie und den Zustand
(läuft / schwarz / Ende). Darunter steht das Log. Dasselbe Log liegt unter
`%LOCALAPPDATA%\montro\montro.log`.

Das Show-Fenster liegt nach dem Platzieren über allen anderen Fenstern, auch über der
Taskleiste.

## Selbsttest

```powershell
montro.exe --selbsttest                    # etwa 1 Minute
montro.exe --selbsttest --kiosk-minuten 6  # zusätzlich: Kiosk 6 min ohne Eingabe stehen lassen
montro.exe --selbsttest --datei "C:\Pfad\Vortrag.pptx"  # eigene Präsentation auf jedem Bildschirm
montro.exe --selbsttest --parser "A.pptx" "B.pptx"      # PPTX-Parser gegen PowerPoint vergleichen
```

Mit `--parser` startet keine Show. montro liest jede Datei mit dem eigenen PPTX-Parser und
vergleicht Folie für Folie mit PowerPoint: ausgeblendete Folien, Notizen, Klick-Animationen,
Anzahl und Länge der Videos.

Das Testfenster zeigt nach dem Öffnen zusätzlich, was der Parser gelesen hat (Folien,
ausgeblendet, Videos, Klick-Animationen, Folien mit Notizen).

Mit `--datei` startet montro nur die angegebene Präsentation auf jedem Bildschirm, zweimal
hintereinander ohne Beenden dazwischen, prüft Lage und Zustand und macht Bildschirmfotos.

montro startet dabei ohne Hauptfenster. Der Selbsttest erzeugt in PowerPoint eine
Testpräsentation (6 Folien, Folie 3 und 5 ausgeblendet und rot, Folie 2 mit zwei
Klick-Animationen) und prüft damit **auf jedem angeschlossenen Bildschirm**: Öffnen, Show
starten, Lage (gemessen und per Bildschirmfoto), Weiter/Zurück/Gehe zu, Schwarz, Endbildschirm
und Beenden. Danach kommen Kiosk-Modus, verwaiste Shows, PNG-Export, ein offener Dialog in
PowerPoint und eine Datei „aus dem Internet“.

- Läuft schon eine Bildschirmpräsentation, startet der Selbsttest nicht.
- Während des Tests wechseln die Bildschirme. Maus und Tastatur bitte nicht benutzen.
- Ergebnis: `%TEMP%\montro-selbsttest\<Datum-Zeit>\bericht.md` mit allen Prüfungen, dazu die
  Bildschirmfotos und die Testpräsentation. Der Rückgabewert ist die Zahl der Fehler (0 = alles
  ok, -1 = abgebrochen).
