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
- **Bildschirm-Abschaltung:** Schaltet Windows die Bildschirme wegen Inaktivität ab, melden sich
  USB-C-Monitore (z. B. Verbatim MT15) ganz ab und zeigen „No signal“. Auf Show-Rechnern unter
  Einstellungen › System › Energie „Bildschirm ausschalten“ auf „Nie“ stellen. Während eine
  Präsentation live ist, verhindert montro das selbst (`SetThreadExecutionState`), in den Pausen
  dazwischen aber nicht.

## Show-Rechner

**Installer:** `montro-<version>-win-x64.msi` aus den
[Releases](https://github.com/Sassisasah/montro-win/releases) (oder als Artefakt `montro-msi` aus
GitHub Actions) per Doppelklick installieren – braucht einmal Admin-Rechte. Er installiert nach
„Programme\montro“ mit Startmenü-Eintrag, legt die Firewall-Freigabe für montro an (Fernsteuerung
und Verbund, alle Netzprofile – auch „Öffentlich“, das Windows oft für unbekannte Netze wählt),
öffnet `.montro`-Shows per Doppelklick und ergänzt „Öffnen mit › montro“ für PowerPoint-Dateien.
Ein Update installiert man genauso; Einstellungen und Playlist bleiben erhalten. Ohne Installation
geht weiterhin das Artefakt `montro-win-x64`: entpacken und `montro.exe` starten (dann fragt die
Firewall beim ersten Start).

Selbst bauen: `dotnet publish src/montro/montro.csproj -c Release -r win-x64 --self-contained true
-o publish`, danach `dotnet build installer/montro.wixproj -c Release -p:ProductVersion=0.7.0`
(holt WiX 5 über NuGet) → `installer/bin/Release/montro-0.7.0-win-x64.msi`.

- SmartScreen warnt vorerst („Unbekannter Herausgeber“), weil es noch kein
  Code-Signing-Zertifikat gibt. Dann „Weitere Informationen“ › „Trotzdem ausführen“.
- **Dateien aus dem Internet oder von Netzlaufwerken** öffnet PowerPoint in der *Geschützten
  Ansicht*. Dort lässt sich keine Show starten. Abhilfe: Datei › Eigenschaften › „Zulassen“
  ankreuzen, oder die Datei lokal kopieren.
