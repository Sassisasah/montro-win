# Kompatibilität mit der Mac-Version (verbindlich)

montro für Windows muss sich nach außen **genau wie montro für Mac** verhalten. Dafür gibt es zwei
Ziele:

1. **Companion:** Das Modul `Sassisasah/companion-module-movidal-montro` (id `movidal-montro`)
   steuert einen Windows-Rechner **ohne jede Änderung**. In Companion trägt man nur die IP des
   Windows-Rechners ein.
2. **Gemischter Verbund:** Mac und Windows laufen im selben Verbund, in beiden Richtungen
   (Mac-Master + Windows-Slave und Windows-Master + Mac-Slave).

Referenz ist immer der Mac-Code im Repo `Sassisasah/montro`:
`Sources/montro/Services/RemoteAPI.swift`, `ClusterManager.swift` und `docs/REMOTE-API.md`.
Weicht diese Datei davon ab, gilt der Mac-Code. Dann bitte diese Datei korrigieren.

**Stand 21.10.2026:** Der aktuelle Mac-Code (0.36) liegt auf dem Branch
`feature/bearbeiten-abgleich`, `main` steht bei 0.31. Seit 0.32 neu und hier noch nicht
eingearbeitet: Verbund-Dateiabgleich (`GET /api/cluster/manifest`, `GET /api/cluster/file`,
`ClusterInfo` mit `detached` und `offers`, `apiPort` in Sync/Befehl), Show-Dateien `.montro`
(`docs/SHOW-FORMAT.md`), „In PowerPoint bearbeiten“ und „Aktualisieren“. Vor Meilenstein 5
(Remote-API) und 7 (Verbund) gegen diesen Stand abgleichen.

## 1. Remote-API (für Companion)

- HTTP/1.1 und JSON auf Port **8765** (einstellbar), ohne Anmeldung.
- `Connection: close`, `Cache-Control: no-store`, `Content-Type: application/json; charset=utf-8`.
- CORS-Kopfzeilen wie am Mac: `Access-Control-Allow-Origin: *`,
  `Access-Control-Allow-Methods: GET, POST, OPTIONS`, `Access-Control-Allow-Headers: Content-Type`.
  `OPTIONS` antwortet mit 204.
- `GET /` liefert `{"app":"montro","api":1,"state":"/api/state"}`.
- Befehle gehen per **GET oder POST**. Antwort: `{"ok":true,"state":{…}}`. Fehler kommen mit
  400 (bzw. 404 bei unbekanntem Pfad oder Befehl) als `{"ok":false,"error":"…"}`. Fehlertexte
  sind deutsch und wie am Mac formuliert.
- `GET /api/state` (und `GET /api`) liefert den Status **ohne** Hülle `ok`/`state`.
- **Feldnamen, Schreibweise und Typen exakt wie am Mac.** Vollständige Liste:
  `app, api, live, starting, locked, blackout, endBlack, status, presentation, presentationFile,
  presentationIndex, selected, selectedIndex, slide, slideCount, nextSlide, animation,
  animationCount, playlistCount, nextPresentation, transition{style,duration}, playlist[{index,
  title, slides, live, selected, ready}], timer{text, remaining, elapsed, duration, state, running,
  phase, blink, color}, message{visible, text, blink, presets[]}, video{playing, name, duration,
  remaining, remainingText}, cluster{role, group, name, summary, warning, detached,
  pendingHandovers, masterReachable, slaves[{index, name, host, reachable, live, file, slide,
  blackout, inSync, detached, issue}]}, refresh{available, running, name, source}`.
- **Verbund per Fernsteuerung** (Branch `companion-verbund`, beide Plattformen gleich):
  `/api/refresh` (Alias `aktualisieren`) – „Aktualisieren“, Fehler „keine neue Version bereit“ bzw.
  „keine Präsentation live“; `/api/cluster/detach|rejoin|toggle` nur am Slave („dieser Rechner ist
  kein Slave“); `/api/slaves/<n|all>/detach|rejoin|toggle` (Alias `slave`) nur am Master, ruft
  `/api/cluster/…` am Slave auf. Während „Aktualisieren“ lehnen `golive` und `goto` mit „Aktualisieren
  läuft – bitte kurz warten“ ab.
- `slide`, `slideCount`, `nextSlide`, `playlist[].slides` zählen **nur sichtbare Folien**.
  Die Indizes sind 1-basiert, und 0 bedeutet „keine“.
- Die Werte für `timer.state` sind `stopped|running|paused`, für `timer.phase`
  `normal|warning1|warning2|overtime`, für `transition.style` `cut|black|crossfade`.
  `timer.color` ist `#RRGGBB` in Großbuchstaben.
- **Aliase** wie am Mac:
  - `prev` = `previous`
  - `live` = `golive`
  - `slide` = `goto`
  - `black` = `blackout`
  - `clear` = `message/hide`
  - `send` und `show` = `message`
  - `timer/stop` = `timer/pause`
  - `golive/next|previous|prev|<n>`
  - `select/next|previous|prev|<n>`
  - `blackout[/on|/off|/toggle]`
  - `timer/blink[/on|/off]`
  - `message/blink[/on|/off]`
- `/api/message` nimmt den Text aus `?text=…`, aus dem JSON-Body `{"text":"…"}` oder aus dem
  Pfad `/api/message/send/<text>`.
- Gültige Bereiche: `transition/duration/<sek>` von 0,3 bis 3, `timer/duration/<min>` größer 0.
- `GET /api/diag` liefert eigene Windows-Inhalte (Monitore, DPI, Zuordnung, PowerPoint-Fenster,
  60 Log-Zeilen). Companion nutzt diesen Pfad nicht.

## 2. Verbund (Master/Slave)

### Finden

- DNS-SD/Bonjour-Dienst **`_montro._tcp`**. Der Instanzname ist der Rechnername (einstellbar),
  der Port ist der API-Port.
- TXT-Einträge `id` (feste Instanz-ID, eine UUID, einmal erzeugt und gespeichert), `role`
  (`standalone|master|slave`), `group` (Verbundname) und `v` = `"1"`.
- Angekündigt wird nur, wenn die Rolle nicht `standalone` ist und „Nur lokal“ aus ist.
- Windows: `DnsServiceRegister`/`DnsServiceBrowse`/`DnsServiceResolve` (dnsapi.dll, ab
  Windows 10). Apple-Bonjour ist nicht nötig. Prüfen lässt sich die Ankündigung am Mac mit
  `dns-sd -B _montro._tcp`.
- Manuelle Einträge `host[:port]` werden genauso angesprochen.

### Nachrichten (JSON, Feldnamen exakt)

Alle Nachrichten gehen per HTTP POST an die Remote-API des anderen Rechners, mit einem Timeout
von 1,5 s.

| Pfad | Richtung | Inhalt |
|---|---|---|
| `POST /api/cluster/command` | Master → Slave, sofort bei Bedienung | `{group, from, fromName, cmd, file?, slide?, on?}`, wobei `cmd` einer der Werte `next, previous, golive, stop, goto, blackout, unlock` ist |
| `POST /api/cluster/sync` | Master → Slave, alle 250 ms bei Änderung, sonst jede Sekunde | `{group, from, fromName, live, starting, file?, slide, click, blackout, locked, timer{total, elapsed, state, blink}, message{visible, text, blink}}` |
| Antwort und `GET /api/cluster` | Slave → Master | `{id, name, role, group, live, file?, slide, blackout, issue?, accepted?}` |

- Die Gegenseite ist Swift `Codable`:
  - Pflichtfelder müssen **immer** vorhanden sein und den richtigen Typ haben.
  - `slide` und `click` sind ganze Zahlen (`3`, nicht `3.0`).
  - `total` und `elapsed` sind Zahlen.
  - Optionale Felder (`file`, `slide` bei command, `on`, `issue`, `accepted`) bei „kein Wert“
    **weglassen** (der Mac lässt sie auch weg). `null` lesen wir trotzdem.
- `from` ist die Instanz-ID des Senders, `fromName` sein Rechnername.
- **`file`** ist nur der Dateiname, **kleingeschrieben**. Vor Vergleich und Versand wird er auf
  **Unicode NFC** normalisiert. Der Mac liefert Dateinamen aus dem Dateisystem teils in NFD
  („a“ + kombinierendes Trema), sonst findet „präsentation.pptx“ nichts.
  - Ersatz-Zuordnung wie am Mac: Titel (Dateiname ohne Endung) kleingeschrieben.
- **`slide`** im Verbund ist der **PowerPoint-Folienindex inklusive ausgeblendeter Folien**. Das
  ist anders als bei der Remote-API für Companion. Die Umrechnung sitzt bei uns zentral in
  `SlideMap`.
- Ein Slave nimmt nur an, wenn `role == slave` ist und `group` exakt übereinstimmt (Groß- und
  Kleinschreibung beachten). Sonst antwortet er mit `accepted:false` und seinem Stand.
- Ein Slave korrigiert per Sync nur Abweichungen, die **mindestens 1,5 s** bestehen. Befehle haben
  Vorrang. Korrigiert werden:
  - live/stopp
  - Folie (nicht bei Ende-Sperre)
  - Blackscreen
  - Timer, wenn „Timer übernehmen“ an ist. Er wird nur gesetzt, wenn der Zustand oder `blink`
    abweicht, `total` um mehr als 0,5 s abweicht oder `elapsed` um mehr als 1 s.
  - Regie-Nachricht, wenn „Nachricht übernehmen“ an ist
- Ein Slave sperrt die eigene Bedienung standardmäßig. Befehle vom Master umgehen die Sperre.
- Es gibt kein automatisches Umschalten bei Ausfall. „Diesen Rechner zum Master machen“ geht nur
  von Hand.
- Ein Master fragt Rechner, die keine Slaves sind, alle 2 s per `GET /api/cluster` ab.

## 3. Was pro Rechner bleibt

Bildschirmzuordnung, Presets der Referent*innenansicht, Einstellungen und die Playlist werden
nicht übertragen. Die Zuordnung der Präsentationen läuft nur über den Dateinamen. Die Ordner
dürfen verschieden sein.

## 4. Test-Matrix (wird in `STATUS.md` abgehakt)

| Test | Erwartung |
|---|---|
| Companion → Windows | alle Presets des Moduls funktionieren, Variablen `$(montro:…)` stimmen |
| `GET /api/state` Mac vs. Windows | gleiche Schlüssel und Typen (Vergleichsskript geplant für Meilenstein 5) |
| Mac-Master → Windows-Slave | live, Folie, Animationen, Schwarz, Stopp, Timer, Nachricht |
| Windows-Master → Mac-Slave | dasselbe |
| Umlaut im Dateinamen | „Präsentation.pptx“ wird in beiden Richtungen gefunden |
| Ausgeblendete Folien | gleiche Folie trotz ausgeblendeter Folien davor |
| Firewall | Windows-Slave ist vom Mac aus erreichbar (Installer-Regel) |
