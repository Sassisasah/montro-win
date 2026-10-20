# Architektur

## Grundprinzip

Microsoft PowerPoint ist die Wiedergabe-Engine. montro steuert PowerPoint per COM, liest die
`.pptx` selbst aus (geplant für Meilenstein 3) und baut darum herum Playlist,
Referent*innenansicht, Timer und Ausgabesteuerung.

Nach außen spricht montro dieselbe Remote-API und dasselbe Verbund-Protokoll wie die Mac-Version.
Der Vertrag dafür steht in [KOMPATIBILITAET.md](KOMPATIBILITAET.md).

## Bausteine

| Datei | Aufgabe |
|---|---|
| `src/montro/App.xaml(.cs)` | Start, globale Fehlerbehandlung, Farben laden |
| `src/montro/app.manifest` | DPI-Modus **PerMonitorV2**, Windows 10/11 |
| `src/montro/Themes/Brand.xaml` | Alle Farben (movidal-CD) und Grund-Stile. Keine Systemfarben |
| `src/montro/Core/AppPaths.cs` | `%APPDATA%\montro` (Zustand), `%LOCALAPPDATA%\montro` (Vorschaubilder, Log) |
| `src/montro/Core/DebugLog.cs` | Log-Datei `montro.log`, letzte Zeilen im Speicher (später für `/api/diag`) |
| `src/montro/Core/SlideMap.cs` | **Einzige Stelle** für die Umrechnung PowerPoint-Index ↔ sichtbare Folie |
| `src/montro/PowerPoint/StaWorker.cs` | Eigener STA-Thread mit Nachrichtenschleife. Alle COM-Aufrufe laufen nacheinander dort, mit Timeout |
| `src/montro/PowerPoint/MessageFilter.cs` | `IMessageFilter`: Ist PowerPoint beschäftigt (`RPC_E_CALL_REJECTED`), wird bis zu 6 s wiederholt |
| `src/montro/PowerPoint/PowerPointHost.cs` | Öffnen, Show starten, platzieren, weiter/zurück/gehe zu, Schwarz, beenden, PNG-Export, Statusabfrage |
| `src/montro/PowerPoint/ShowState.cs` | Momentaufnahme der Show (Zustand, Index, Klick) |
| `src/montro/PowerPoint/RegistryMonitor.cs` | PowerPoint-Einstellung „Monitor“ (HKCU) vorwählen |
| `src/montro/Display/Monitors.cs` | Monitore mit Bounds, WorkArea, DPI, Gerätename und Anzeigename (Win32) |
| `src/montro/Display/Native.cs` | P/Invoke-Deklarationen (user32, shcore) |
| `src/montro/Views/PowerPointTestWindow.xaml(.cs)` | Testfenster „PowerPoint testen“ |

## PowerPoint per COM

- **Late Binding** (`dynamic`, ProgID `PowerPoint.Application`). Es gibt keine Interop-Assembly,
  daher keine Abhängigkeit von der Office-Version (2016/2019/2021/2024/365 haben alle 16.0).
- **Ein STA-Thread** (`StaWorker`) mit WPF-`Dispatcher` als Nachrichtenschleife. COM-Objekte
  werden nur dort angefasst und nie an andere Threads weitergegeben. Nach außen gehen nur
  einfache Werte (`ShowState`, `SlideMap`).
- **Timeout** je Aufruf (Standard 8 s, Öffnen und Start 30 s). Hängt PowerPoint, weil ein
  Dialog offen ist, meldet montro einen Fehler, statt die Oberfläche einzufrieren.
- **Öffnen:** `Presentations.Open(pfad, ReadOnly, Untitled:false, WithWindow:false)`.
  Schreibgeschützt heißt, es gibt keine Sperrdatei und keine „Speichern?“-Frage.
  `Presentation.Close()` schließt per Automation ohne Rückfrage.
- **Show-Einstellungen:** Show-Typ Referent (1) oder Kiosk (3), `ShowPresenterView = false`
  (PowerPoints eigene Referentenansicht aus), alle Folien, mit Animationen. Jede Einstellung
  steht in einem eigenen `try`. Bei unlizenziertem PowerPoint startet die Show trotzdem
  (Lehre aus Mac 0.27).
- **Monitor:** `SlideShowSettings` hat keinen Monitor-Parameter. Deshalb geht montro so vor:
  1. Optional PowerPoints Einstellung „Monitor“ in der Registry vorwählen.
  2. Nach dem Start `SlideShowWindow.Left/Top/Width/Height` in **Punkt** setzen
     (px × 72 / System-DPI).
  3. Die Ist-Lage über `HWND` und `GetWindowRect` in **physischen Pixeln** messen. Bei
     Abweichung per `SetWindowPos` korrigieren, bis zu 4×, und jede Abweichung loggen.
- **Status:** Abfrage alle 200 ms (`View.State`, `Slide.SlideIndex`, `CurrentShowPosition`,
  `GetClickIndex/GetClickCount`). Am Endbildschirm wirft `View.Slide` eine Ausnahme, und
  `State == ppSlideShowDone (5)`. Vier Abfragen in Folge ohne Show bedeuten „beendet“.
  COM-Events (`SlideShowNextSlide`, `SlideShowEnd`) kommen später als Ergänzung.
- **Beenden:** alle `SlideShowWindows` von hinten nach vorn mit `View.Exit()`, dann die
  Präsentation schließen. Beim Start einer neuen Show laufen vorher alle alten aus.
- **Vorschaubilder:** `Slide.Export(pfad, "PNG", 1280, h)`, die Höhe aus dem Seitenformat. Ein
  Aufruf je Folie, damit die Warteschlange zwischendurch frei bleibt. Ausgeblendete Folien werden
  ebenfalls exportiert.

## Entscheidungen & Stolpersteine

- **Kein `HttpListener`** für die Remote-API (später), weil er für `http://+:8765/` eine
  URL-Reservierung mit Adminrechten braucht. Geplant ist ein eigener kleiner HTTP-Server auf
  `TcpListener`, wie am Mac auf `NWListener`. Das bedeutet keine Fremdbibliothek und volle
  Kontrolle über Kopfzeilen und JSON.
- **Kein NuGet** im Projekt. Die Cloud-Umgebung von Claude hat keinen Zugang zu nuget.org, und
  es soll ohne Fremdpakete gehen.
- **DPI:** Ohne PerMonitorV2 virtualisiert Windows die Koordinaten. Fensterlagen auf Monitoren
  mit anderer Skalierung stimmen dann nicht (Bild abgeschnitten oder verschoben, wie am Mac auf
  „CinePanorama“).
- **Publikumsfenster** (ab Meilenstein 4): topmost, randlos und exakt auf die **Bounds** des
  Monitors, nicht auf die WorkArea. Sonst bleibt die Taskleiste sichtbar.

## Fahrplan

1. PowerPoint-COM-Kern mit Testfenster ← **jetzt**
2. *(in 1 enthalten)* Gerüst, Doku, Build über GitHub Actions
3. PPTX-Parser portieren (Notizen, Klick-Animationen, Videos, ausgeblendete Folien)
4. Playlist, Inspector, Ausgaben (Schwarzfenster, Popup-Modus), Ende-Sperre
5. Remote-API 1:1, Test mit dem Companion-Modul
6. Referent*innenansicht mit Builder und Presets, Timer, Regie-Nachricht
7. Übergänge, Verbund (DNS-SD und Sync), gemischter Test mit dem Mac
8. Installer mit Firewall-Regel, Info-Fenster, Version aus CHANGELOG
