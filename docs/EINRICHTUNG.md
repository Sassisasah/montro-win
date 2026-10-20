# Einrichtung

## Entwicklungsrechner (Windows 11)

Diese Schritte einmalig selbst erledigen. Sie brauchen Admin-Rechte, und Claude bedient keine
UAC-Dialoge.

1. **.NET SDK 8 oder neuer** (x64) installieren: <https://dotnet.microsoft.com/download>.
   Ein neueres SDK (z. B. 10) baut .NET 8 ohne Zusatzinstallation. Ist nur die .NET-10-Laufzeit
   installiert, den Debug-Build mit `$env:DOTNET_ROLL_FORWARD='Major'` starten.
2. **Git für Windows** installieren.
3. **GitHub CLI** installieren und als `Sassisasah` anmelden: `gh auth login`.
4. **Microsoft PowerPoint** (lizenziert) installieren und einmal von Hand starten, damit
   Aktivierungs- und Willkommensdialoge erledigt sind. Offene Dialoge blockieren die
   Fernsteuerung.
5. Das Repo in den Projektordner klonen (Test-PC: `C:\Users\mathi\Projekte`):
   ```powershell
   cd C:\Users\mathi\Projekte
   gh repo clone Sassisasah/montro-win
   ```
6. Den PC mit der Claude-Desktop-App verknüpfen und den Projektordner als verbundenen Ordner
   freigeben.

## Bildschirme

Getestet wird mit dem Laptop und zwei externen Bildschirmen (Publikum, Referent*in).

- Windows-Einstellungen › System › Bildschirm: **Erweitern** (nicht „Duplizieren“).
- Hauptbildschirm bleibt der Laptop.
- Unterschiedliche Skalierung je Bildschirm ist erlaubt. montro rechnet pro Monitor
  (DPI-Modus PerMonitorV2).

## Show-Rechner

Der Installer folgt mit Meilenstein 8. Er legt auch die Firewall-Regel für Port 8765 an
(Privat/Domäne). Bis dahin genügt das Artefakt `montro-win-x64` aus GitHub Actions: entpacken
und `montro.exe` starten.

- SmartScreen warnt vorerst („Unbekannter Herausgeber“), weil es noch kein
  Code-Signing-Zertifikat gibt. Dann „Weitere Informationen“ › „Trotzdem ausführen“.
- **Dateien aus dem Internet oder von Netzlaufwerken** öffnet PowerPoint in der *Geschützten
  Ansicht*. Dort lässt sich keine Show starten. Abhilfe: Datei › Eigenschaften › „Zulassen“
  ankreuzen, oder die Datei lokal kopieren.
