# Architektur

## Grundprinzip

Microsoft PowerPoint ist die Wiedergabe-Engine. montro steuert PowerPoint per COM, liest die
`.pptx` selbst aus (PPTX-Parser, Meilenstein 3) und baut darum herum Playlist,
Referent*innenansicht, Timer und Ausgabesteuerung.

Nach außen spricht montro dieselbe Remote-API und dasselbe Verbund-Protokoll wie die Mac-Version.
Der Vertrag dafür steht in [KOMPATIBILITAET.md](KOMPATIBILITAET.md).

## Bausteine

| Datei | Aufgabe |
|---|---|
| `src/montro/App.xaml(.cs)` | Start, globale Fehlerbehandlung, Farben laden |
| `src/montro/app.manifest` | DPI-Modus **PerMonitorV2**, Windows 10/11 |
| `src/montro/Themes/Brand.xaml` | Alle Farben (movidal-CD) und Grund-Stile. Keine Systemfarben |
| `src/montro/Core/AppPaths.cs` | `%APPDATA%\montro` (Zustand), `%LOCALAPPDATA%\montro` (Vorschau-Cache, Log) |
| `src/montro/Core/DebugLog.cs` | Log-Datei `montro.log`, letzte Zeilen im Speicher (später für `/api/diag`) |
| `src/montro/Core/AppState.cs` | Zentraler Zustand wie am Mac: Playlist, Vorbereitung, Live, Blackscreen, Ende-Sperre, Ausgabe, Timer, Speichern |
| `src/montro/Core/PreviewCache.cs` | Analyse und Vorschaubilder nach Dateiinhalt (SHA-256), Aufräumen |
| `src/montro/Core/PresentationItem.cs` | Playlist-Eintrag (Pfad, Meta, Vorschaubilder, Status, LIVE) |
| `src/montro/Core/TalkTimer.cs` | Redezeit-Timer, portiert aus `TalkTimer.swift` |
| `src/montro/Core/StateStore.cs` | `%APPDATA%\montro\state.json` lesen und schreiben |
| `src/montro/Core/Observable.cs` | `INotifyPropertyChanged`-Basis für die Bindungen |
| `src/montro/MainWindow.xaml(.cs)` | Hauptfenster wie `MainView`/`PlaylistView`/`InspectorView` am Mac |
| `src/montro/Views/SlideTile.cs` | Kachel der Folienübersicht (Mac: `SlideTile`) |
| `src/montro/Core/PresenterLayout.cs` | Aufbau der Referent*innenansicht: Bausteine mit normierter Lage, Standard „Windows“ (Mac: `PresenterLayout.swift`) |
| `src/montro/Views/PresenterView.cs` | Referent*innenansicht mit allen Bausteinen, Fläche 1080 Einheiten hoch (Mac: `PresenterView.swift`) |
| `src/montro/Display/OutputManager.cs` | Schwarzbild, Übergang und Ausgabefenster der Referent*innenansicht (Bildschirm oder Popup) |
| `src/montro/Testing/PresenterSnapshot.cs` | `--referentenbild`: Testbilder der Referent*innenansicht ohne PowerPoint |
| `src/montro/Core/AppState.Layouts.cs` | Standard, Presets, Entwurf und Zuweisung der Referent*innenansicht (Mac: `AppState`, Presets) |
| `src/montro/Cluster/ClusterManager.cs` | Verbund Master/Slave: Befehle, Stand jede Sekunde, Korrektur nach 1,5 s, Bediensperre (Mac: `ClusterManager.swift`) |
| `src/montro/Cluster/ClusterModels.cs` | Nachrichten im Verbund (Befehl, Stand, Info, Manifest), JSON wie am Mac |
| `src/montro/Cluster/DnsSd.cs` | DNS-SD `_montro._tcp` mit den Windows-Funktionen aus `dnsapi.dll` (ankündigen, suchen, auflösen) |
| `src/montro/Cluster/FileSync.cs` | Datei-Abgleich: Prüfsummen, Laden, Ersetzen mit Sicherung, Bereitlegen, Abkoppeln (Mac: `FileSync.swift`) |
| `src/montro/Core/AppState.Cluster.cs` | Anbindung: Bediensperre, Weitergabe, „Aktualisieren“ |
| `src/montro/Views/ClusterSection.cs` | Einstellungen › Verbund (Mac: `ClusterSection`) |
| `src/montro/Remote/RemoteApi.cs` | Fernsteuerung per HTTP/JSON auf `TcpListener` (Mac: `RemoteAPI.swift`, `docs/REMOTE-API.md` im Mac-Repo), ohne Verbund-Teil |
| `src/montro/Core/AppState.Remote.cs` | Einstellungen der Fernsteuerung (an/aus, Port, nur lokal) |
| `src/montro/Views/SettingsWindow.cs` | Einstellungen › Fernsteuerung (Mac: `RemoteAPISection`) |
| `src/montro/Core/ShowFile.cs` | Show-Datei `.montro` (JSON, Format wie `docs/SHOW-FORMAT.md` im Mac-Repo), Umwandlung der Farben `#RRGGBBAA` ↔ WPF `#AARRGGBB` |
| `src/montro/Core/AppState.Show.cs` | Show öffnen, sichern, mit Präsentationen sichern, fehlende Dateien suchen (Mac: `ShowFileIO`) |
| `src/montro/Testing/ShowFileTest.cs` | `--showtest`: Show-Dateien ohne Oberfläche prüfen, auch eine Show im Mac-Stil |
| `src/montro/Core/AppState.Editing.cs` | „In PowerPoint bearbeiten“ und Dateiwächter (Mac: `FileSync` ohne Verbund) |
| `src/montro/Views/LayoutBuilderWindow.cs` | Baukasten der Referent*innenansicht (Mac: `LayoutBuilderView`) |
| `src/montro/Views/ColorMenu.cs` | Farbauswahl-Menü mit den movidal-Farben |
| `src/montro/Display/LiveThumbnail.cs` | Live-Bild der Show als DWM-Miniatur in einem Element (Mac: `LiveCapture`) |
| `src/montro/Testing/ThumbnailTest.cs` | `--miniaturtest`: Miniatur in einem „layered“ Fenster prüfen |
| `src/montro/Views/Converters.cs` | Bindungshilfen (Sichtbarkeit, Bilder ohne Dateisperre) |
| `src/montro/Core/PresentationMeta.cs` | Ergebnis des Parsers: Folien mit Ausblendung, Notizen, Klicks, Videos (wie `SlideMeta` am Mac) |
| `src/montro/Pptx/PptxParser.cs` | PPTX-Parser, portiert aus `PPTXParser.swift`: liest das ZIP direkt, ohne PowerPoint |
| `src/montro/Pptx/MediaDuration.cs` | Videolänge aus dem MP4/MOV-Kopf (`moov/mvhd`), ohne Fremdbibliothek |
| `src/montro/Core/SlideMap.cs` | **Einzige Stelle** für die Umrechnung PowerPoint-Index ↔ sichtbare Folie |
| `src/montro/PowerPoint/StaWorker.cs` | Eigener STA-Thread mit Nachrichtenschleife. Alle COM-Aufrufe laufen nacheinander dort, mit Timeout |
| `src/montro/PowerPoint/MessageFilter.cs` | `IMessageFilter`: Ist PowerPoint beschäftigt (`RPC_E_CALL_REJECTED`), wird bis zu 6 s wiederholt |
| `src/montro/PowerPoint/PowerPointHost.cs` | Öffnen, Show starten, platzieren, weiter/zurück/gehe zu, Schwarz, beenden, PNG-Export, Statusabfrage |
| `src/montro/PowerPoint/ShowState.cs` | Momentaufnahme der Show (Zustand, Index, Klick) |
| `src/montro/PowerPoint/RegistryMonitor.cs` | PowerPoint-Einstellung „Monitor“ (HKCU) vorwählen |
| `src/montro/Display/Monitors.cs` | Monitore mit Bounds, WorkArea, DPI, Gerätename und Anzeigename (Win32) |
| `src/montro/Display/Native.cs` | P/Invoke-Deklarationen (user32, shcore) |
| `src/montro/Views/PowerPointTestWindow.xaml(.cs)` | Testfenster „PowerPoint testen“ |
| `src/montro/Testing/SelfTest.cs` | Selbsttest `--selbsttest`: alle Prüfungen aus STATUS.md automatisch, Bericht als Markdown |
| `src/montro/Testing/TestPowerPoint.cs` | Zweite COM-Verbindung für den Selbsttest: Testpräsentation, fremde Shows, Dialog, Aufräumen |
| `src/montro/Testing/ScreenCapture.cs` | Bildschirmfoto eines Monitors, Farbprüfung an Ecken und Kanten |
| `src/montro/Resources/montro.ico` | App-Symbol, aus dem Mac-Symbol (`Resources/AppIcon.iconset`) mit 16–256 px |

## Oberfläche: wie am Mac (verbindlich)

Die Oberfläche sieht **so gleich wie möglich aus wie montro für Mac**. Vorlage ist der
SwiftUI-Code im Repo `Sassisasah/montro`, aktueller Stand (0.36, 21.10.2026) auf dem Branch
`feature/bearbeiten-abgleich`, nicht `main` (`Sources/montro/Views/MainView.swift`,
`PlaylistView.swift`, `SlideNavigatorView.swift`, `InspectorView.swift`,
`TimerControlView.swift`, `MessageControlView.swift`, `PresenterView.swift`,
`LayoutBuilderView.swift`, `Components.swift`, `Core/Theme.swift`) und der aktuelle Stand der
Mac-App (Screenshots vom 20.10.2026).

- Gleich: Aufbau (Playlist links mit Zähler und „+ Hinzufügen“, Folienleiste darunter mit
  „Ausgewählte live schalten“ und „Stopp“, Statuszeile unten, Inspector rechts mit den Karten
  Live-Folie, Referent*innenansicht, Timer, Regie-Nachricht), Farben, Abstände, Ecken,
  Schriftgrößen, Symbole, Texte und Reihenfolge der Bedienelemente.
- Windows-eigen: Fensterrahmen und Titelleiste, Menüs, Datei- und Systemdialoge, Strg statt ⌘.
- Schrift: Segoe UI statt SF Pro, in denselben Größen und Stärken.
- Das App-Symbol ist dasselbe wie am Mac.
- Das Testfenster „PowerPoint testen“ ist ein Prüfwerkzeug und fällt mit Meilenstein 4 weg.

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
  3. Die Ist-Lage über das Fenster-Handle und `GetWindowRect` in **physischen Pixeln** messen.
     Bei Abweichung per `SetWindowPos` korrigieren, bis zu 4×, und jede Abweichung loggen.
     `SlideShowWindow.HWND` fehlt in PowerPoint 365 (Build 19929). Dann sucht montro das
     sichtbare Fenster der Klasse `screenClass` von `POWERPNT.EXE`, dessen Titel den
     Präsentationsnamen enthält.
  4. Das Show-Fenster **topmost** legen und wieder nach oben holen: nach jedem Befehl, direkt
     nach dem Platzieren 1,5 s lang alle 100 ms und danach bei jeder Statusabfrage (200 ms).
     Sonst schiebt sich auf Nebenbildschirmen die Taskleiste über die Show, denn Windows blendet
     sie nur für das *aktive* Vollbildfenster aus, und PowerPoint läuft im Hintergrund.

## PPTX-Parser

Portiert aus `Sources/montro/Services/PPTXParser.swift` (Mac). Liest die Datei als ZIP
(`System.IO.Compression`) und die XML-Teile mit `XmlReader`, ohne PowerPoint und ohne
Fremdbibliothek. Zählweise absichtlich wie am Mac:

- **Reihenfolge:** `ppt/presentation.xml` › `sldIdLst` › `r:id` › `presentation.xml.rels`.
- **Ausgeblendet:** `<p:sld show="0">`.
- **Notizen:** Text der Platzhalter vom Typ `body` der Notizenseite. Absätze und `<a:br/>`
  werden Zeilenumbrüche, mehrere Body-Platzhalter mit Leerzeile getrennt.
- **Klick-Animationen:** Anzahl der `cTn nodeType="clickEffect"` außerhalb einer
  `interactiveSeq` (Trigger-Animationen zählen nicht, „Mit/Nach vorherigem“ auch nicht).
- **Videos:** `p:pic` mit `a:videoFile r:link`. Eingebettet wird die Datei kurz nach `%TEMP%`
  entpackt, verknüpft relativ zur .pptx gesucht. Startklick = Klickzahl beim ersten
  `cmd playFrom…` auf diese Form, `null` bei Trigger. Länge aus `moov/mvhd` (MP4/MOV/M4V),
  sonst 0 = unbekannt (am Mac: AVFoundation).
- **Prüfung:** `--selbsttest --parser <dateien…>` vergleicht Folie für Folie mit dem, was
  PowerPoint per COM meldet (Ausblendung, Notizen, `MainSequence` mit Auslöser „Bei Klick“,
  Videos mit `MediaFormat.Length`). Der normale Selbsttest erzeugt zusätzlich eine
  Testpräsentation mit gemischten Effekten, Trigger und Notizen mit Umlauten.
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

- **Referent*innenansicht auf eigenem Bildschirm:** randloses Fenster in voller Größe, aber
  „layered“ (`AllowsTransparency`) wie die Abdeckungen. Ein normales randloses Vollbildfenster
  ginge auf den Verbatims (USB-C, AMD Vega 8) in den Vollbild-Direktmodus und zeigte Schwarz oder
  Störungen. Kein Fokus, keine Mausklicks, damit Clicker-Tasten bei montro bleiben. Nicht live
  zeigt es Schwarz (am Mac liegt dort das Schwarzbild darüber). Gezeichnet wird auf einer
  Fläche von 1080 Einheiten Höhe; die Breite folgt dem Seitenverhältnis, so gelten die
  Mac-Schriftgrößen unverändert. Die vielen Änderungsmeldungen pro Takt (Timer, Uhr, Blinken)
  werden zu einem Neuzeichnen zusammengefasst.
- **Live-Bild:** DWM-Miniatur des Show-Fensters (`DwmRegisterThumbnail`, `Display/LiveThumbnail.cs`),
  wie die Taskleisten-Vorschau: volle Bildrate, ohne Rechenlast, funktioniert auch im Popup-Modus
  und im „layered“ Ausgabefenster. Bildschirmfotos (GDI) schafften nur ~2 Bilder/s bei 30–60 ms
  je Aufnahme („Daumenkino“) und brauchten `CAPTUREBLT`, sonst war die Show schwarz. Die
  Miniatur liegt über dem WPF-Inhalt ihres Fensters, daher ist das LIVE-Abzeichen der Karte
  „Live-Folie“ neben die Überschrift gewandert; sie wird auf ScrollViewer und Fenster
  zugeschnitten. Bei Schwarz, Start und Übergang keine Miniatur, dann das Vorschaubild.

- **Kein `HttpListener`** für die Remote-API (später), weil er für `http://+:8765/` eine
  URL-Reservierung mit Adminrechten braucht. Geplant ist ein eigener kleiner HTTP-Server auf
  `TcpListener`, wie am Mac auf `NWListener`. Das bedeutet keine Fremdbibliothek und volle
  Kontrolle über Kopfzeilen und JSON.
- **Kein NuGet** im Projekt. Die Cloud-Umgebung von Claude hat keinen Zugang zu nuget.org, und
  es soll ohne Fremdpakete gehen.
- **`System.IO` in WPF:** Bei WPF-Projekten fehlt `System.IO` in den ImplicitUsings (wegen
  `System.Windows.Shapes.Path`). Es steht deshalb in `GlobalUsings.cs`.
- **Build-Logs:** Die Cloud-Sitzung kann die Logs von GitHub Actions nicht laden. Compilerfehler
  stehen aber als Annotationen am Check-Run (`/check-runs/<job>/annotations`).
- **DPI:** Ohne PerMonitorV2 virtualisiert Windows die Koordinaten. Fensterlagen auf Monitoren
  mit anderer Skalierung stimmen dann nicht (Bild abgeschnitten oder verschoben, wie am Mac auf
  „CinePanorama“).
- **Publikumsfenster** (ab Meilenstein 4): topmost, randlos und exakt auf die **Bounds** des
  Monitors, nicht auf die WorkArea. Sonst bleibt die Taskleiste sichtbar.
- **Laufzeit .NET 8 vs. 10:** Auf dem Test-PC ist nur die .NET-10-Laufzeit installiert. Das
  SDK 10 baut für .NET 8 (Targeting-Pakete kommen beim Wiederherstellen), zum Starten des
  Debug-Builds dort `DOTNET_ROLL_FORWARD=Major` setzen. Das veröffentlichte Artefakt ist
  eigenständig und braucht keine Laufzeit.
- **Claude-Sitzung auf dem Test-PC:** Dort werden Schreibzugriffe nach `%APPDATA%` und
  `%LOCALAPPDATA%` umgeleitet. PowerPoint läuft als eigener Prozess außerhalb davon und sieht
  dort angelegte Ordner nicht („Pfad nicht gefunden“ beim Speichern, Export und Öffnen).
  `%TEMP%` und `Dokumente` sind nicht betroffen. Der Selbsttest arbeitet deshalb unter `%TEMP%`.
- **Show-Fenster 1 Pixel niedriger als der Bildschirm:** Ein Fenster, das den Monitor exakt
  füllt, gibt Windows im Vollbild-Direktmodus aus (Independent Flip). Auf dem Test-PC (AMD
  Radeon Vega 8, Verbatim MT15 per USB-C, 150 %) kam dann auf den externen Monitoren nur
  Schwarz, eine kaputte Taskleiste oder ein zerstückeltes Hintergrundbild an, **auch mit
  PowerPoint allein** (F5). Bildschirmfotos (DWM) zeigten trotzdem die Folie. Mit 1 Pixel
  weniger Höhe kam das Bild an (Test „C/D“ am 20.10.2026). Die fehlende Zeile unten ist schwarz.
  Für das spätere Publikumsfenster (Meilenstein 4) gilt dasselbe.
- **Vorschaubilder für die Playlist:** `ExportFileThumbnailsAsync` öffnet eine eigene
  schreibgeschützte Kopie (oder benutzt die schon offene, ohne sie zu schließen), exportiert nach
  `%TEMP%` und verschiebt dann in den Vorschau-Cache. Laufende Shows bleiben unberührt.
- **Vorschau-Cache (`Core/PreviewCache.cs`, ab 0.7):** Analyse und Vorschaubilder liegen nach
  Dateiinhalt unter `%LOCALAPPDATA%\montro\cache\<SHA-256>\` (`analyse.json` + `slide-<n>.png`),
  nicht mehr je Playlist-Eintrag. `Prepare` prüft erst die Prüfsumme (dieselbe wie im Verbund) und
  rendert nur bei unbekanntem Inhalt. Beim Start gilt ein Eintrag ohne Prüfung als bereit, wenn
  Größe und Änderungszeit denen beim Erzeugen entsprechen (`SavedItem.FileSize/FileTime`).
  Aufräumen nach `PrepareAll`: unbenutzte Einträge nach 60 Tagen bzw. über 5 GB (Ordner-Zeit =
  zuletzt benutzt). Alte Ordner `thumbs\<ID>` übernimmt `MoveToCache` einmalig; verwaiste löscht
  montro beim Start. `analyse.json` ist die Vorstufe der gemeinsamen Analysedatei für Mac und
  Windows (Konzept „Weitere Präsentationsformate“).
- **Oberfläche testen ohne Maus:** WPF stellt alle Knöpfe und Listen über UI Automation bereit.
  Damit lässt sich das Hauptfenster aus einem Skript bedienen (Eintrag wählen, „Ausgewählte live
  schalten“, „Weiter“, Statuszeile lesen), so geprüft am 21.10.2026.
- **Schwarzbild liegt immer unter oder über der Show:** Auf dem Publikumsbildschirm bleibt das
  Schwarzbild (layered, volle Bildschirmhöhe) dauerhaft liegen. Ist Schwarz gefragt (nichts live,
  Blackscreen, Ende auf Schwarz), holt montro es nach oben, sonst liegt es unter dem
  PowerPoint-Fenster. So scheint beim Umschalten und in der untersten Pixelzeile (Show 1 Pixel
  niedriger) nie Desktop oder Taskleiste durch. Während eines Übergangs wird nur dessen Abdeckung
  nach oben geholt, nicht das Schwarzbild – sonst lag es über der neuen Show und das Aufblenden
  fehlte.
- **Taskleiste auf dem Publikumsbildschirm:** Wegen der 1 Pixel niedrigeren Fenster gilt der
  Bildschirm für Windows nicht als „Vollbild“, die Taskleiste (`Shell_SecondaryTrayWnd`) kam
  immer wieder über Show und Schwarzbild. 1 Pixel *größer* (TEST E) brachte das Verbatim-Problem
  zurück. Lösung wie am Mac (Dock/Menüleiste ausblenden): montro versteckt das Taskleistenfenster
  dieses Bildschirms, solange er Ausgabe ist, und zeigt es beim Umstellen, Beenden und nach einem
  Absturz beim nächsten Start wieder. Keine Windows-Einstellung wird geändert.
- **Eigene Vollbildfenster auf Bildschirmen mit anderer Skalierung:** WPF vergrößert ein Fenster
  beim Wechsel auf einen Monitor mit 150 % selbst (WM_DPICHANGED). Schwarzbild- und
  Übergangsfenster fangen die Meldung ab und werden nur in physischen Pixeln per `SetWindowPos`
  platziert, ebenfalls 1 Pixel niedriger als der Bildschirm.
- **„Änderungen speichern?“ nach dem Show-Ende:** Die Show-Einstellungen (Show-Typ,
  Referentenansicht aus …) markieren die Präsentation als geändert. Endete die Show, schloss
  PowerPoint die fensterlose Präsentation und fragte nach dem Speichern – der Dialog blockierte
  danach jeden Befehl. montro setzt deshalb `Presentation.Saved = true` nach den Einstellungen und
  vor jedem Beenden/Schließen.
- **Offener PowerPoint-Dialog:** Antwortet PowerPoint nicht (Zeitlimit), sucht montro sichtbare
  Dialoge von `POWERPNT.EXE` (`#32770`, `NUIDialog`), nennt deren Text in der Statuszeile und holt
  sie auf den Hauptbildschirm (Mac 0.34).
- **Tastaturfokus nach dem Start:** `SlideShowSettings.Run()` aktiviert das Show-Fenster; Tasten und
  Clicker gingen dann direkt an PowerPoint, an der Ende-Sperre vorbei bis „Ende der
  Bildschirmpräsentation“. Mit externem Publikumsbildschirm holt montro den Fokus danach zurück.
- **Log der Nutzerin lesen:** Die Claude-App ist ein MSIX-Paket, ihre Prozesse sehen `%LOCALAPPDATA%`
  umgeleitet (`…\Packages\Claude_…\LocalCache\Local`). Die echte Datei lässt sich über einen per WMI
  (`Win32_Process.Create`) gestarteten Prozess nach `%TEMP%` kopieren.
- **Präsentation verschwindet mit dem Show-Ende:** Eine mit `WithWindow:false` geöffnete
  Präsentation schließt PowerPoint selbst, sobald ihre Show beendet wird. Danach liefert jeder
  Zugriff „Object does not exist“ (0x80048010). montro prüft vor Show-Start und Export, ob die
  Präsentation noch lebt, und öffnet sie sonst still neu.
- **Datei aus dem Internet:** Per Automation öffnet PowerPoint 365 Dateien mit
  Zone.Identifier (ZoneId=3) direkt, ohne Geschützte Ansicht. Der Hinweis in EINRICHTUNG.md gilt
  für Dateien, die jemand vorher selbst in PowerPoint geöffnet hat.

## Fahrplan

1. PowerPoint-COM-Kern mit Testfenster ✓ (21.10.2026)
2. *(in 1 enthalten)* Gerüst, Doku, Build über GitHub Actions ✓
3. PPTX-Parser portieren (Notizen, Klick-Animationen, Videos, ausgeblendete Folien) ✓ (21.10.2026)
4. Playlist, Inspector, Ausgaben (Schwarzfenster, Popup-Modus), Ende-Sperre, dazu (vorgezogen aus 6
   und 7) Referent*innenansicht mit Baukasten und Presets, Timer, Regie-Nachricht, Übergänge,
   Video-Karte, „In PowerPoint bearbeiten“, Show-Dateien ✓ (22.10.2026, Branch `meilenstein-4`)
5. Remote-API 1:1, Test mit dem Companion-Modul ✓ (22.10.2026)
6. *(in 4 enthalten)*
7. Verbund (DNS-SD und Sync, „Aktualisieren“), gemischter Test mit dem Mac ← **in Arbeit** (Branch `meilenstein-7`)
8. Installer mit Firewall-Regel, Info-Fenster, Version aus CHANGELOG
