# montro für Windows

**montro** (Esperanto „Vorführung“) ist ein Präsentations-Player für Bühne und Regie und
funktioniert mit Microsoft PowerPoint. PowerPoint ist die Wiedergabe-Engine, damit Übergänge,
Animationen, Morph und Videos exakt so laufen wie in PowerPoint. montro baut darum herum:

- Playlist
- Referent*innenansicht
- Timer und Regie-Nachricht
- Ausgabesteuerung
- Fernsteuerung (Bitfocus Companion)
- Verbund mehrerer Rechner

Dies ist die Windows-Version der Mac-App (Repo `Sassisasah/montro`). Beide sprechen **dieselbe
Remote-API und dasselbe Verbund-Protokoll**. Das Companion-Modul funktioniert also unverändert,
und Mac- und Windows-Rechner laufen im selben Verbund. Details stehen in
[docs/KOMPATIBILITAET.md](docs/KOMPATIBILITAET.md).

Entwickelt für movidal. Konzept & Leitung: Sarah-Mathias Andrae. Programmiert mit Claude
(Anthropic). © movidal

## Stand

Meilenstein 1 ist in Arbeit: der PowerPoint-Kern mit dem Testfenster „PowerPoint testen“.
Den aktuellen Stand zeigt [docs/STATUS.md](docs/STATUS.md), den Fahrplan
[docs/ARCHITEKTUR.md](docs/ARCHITEKTUR.md#fahrplan).

## Technik

C# / .NET 8, WPF. PowerPoint wird per COM gesteuert (Late Binding, ohne feste Office-Version).
Es gibt keine Fremdbibliotheken.

## Bauen

```powershell
dotnet build src\montro\montro.csproj -c Release
dotnet run --project src\montro\montro.csproj
```

GitHub Actions baut jeden Push auf `windows-latest`. Das Ergebnis liegt als Artefakt
`montro-win-x64` am Workflow-Lauf. Es ist eigenständig, eine .NET-Installation ist nicht nötig.

## Doku

| Datei | Inhalt |
|---|---|
| [docs/BEDIENUNG.md](docs/BEDIENUNG.md) | Bedienung |
| [docs/EINRICHTUNG.md](docs/EINRICHTUNG.md) | Entwicklungsrechner und Show-Rechner einrichten |
| [docs/ARCHITEKTUR.md](docs/ARCHITEKTUR.md) | Aufbau, Entscheidungen, Stolpersteine, Fahrplan |
| [docs/KOMPATIBILITAET.md](docs/KOMPATIBILITAET.md) | Vertrag mit Mac-Version und Companion |
| [docs/STATUS.md](docs/STATUS.md) | Was getestet ist und was offen ist |
| [CHANGELOG.md](CHANGELOG.md) | Änderungsprotokoll |
