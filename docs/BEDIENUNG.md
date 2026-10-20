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
