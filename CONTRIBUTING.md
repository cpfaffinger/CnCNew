# Mitarbeiten

Kurzfassung für Menschen. Die vollständigen, verbindlichen Regeln stehen in [CLAUDE.md](CLAUDE.md) (sie gelten für
Coding-Agenten und Menschen gleichermaßen); die Lizenzlage steht in [NOTICE.md](NOTICE.md).

## Voraussetzungen und Daten

- Windows, .NET SDK 10.0.401 oder ein neueres Feature-Band (`global.json`), PowerShell 7 (`pwsh`).
- Eine eigene, frische englische Steam-Installation von *Command & Conquer Generals – Zero Hour*. Das Repository
  enthält keine Spieldaten.
- Entwicklerdaten erzeugen: `pwsh scripts/dev-content.ps1` liest die Installation (ohne sie zu verändern) und schreibt
  die Entwicklerinstallation nach `game/` (`game/content`); Einzelheiten in `docs/INSTALLATION.md`.

## Bauen und testen

```powershell
dotnet build CnCNew.slnx
dotnet run --project tests/CnCNew.Simulation.Tests
dotnet run --project tests/CnCNew.Simulation.Tests -- --content-dir game/content
dotnet run --project tests/CnCNew.Data.Tests -- --content-dir game/content
dotnet run --project tools/CnCNew.IniTool -- compat-check game/content baselines/compatibility.json
```

Es gibt noch keine CI: wer etwas einreicht, lässt die Testprogramme der berührten Projekte selbst laufen und nennt im
Pull Request, was gelaufen ist. Die vollständige Liste der Testprogramme, Audits und Manifestprüfungen steht in
`CLAUDE.md`, Abschnitt „Build, Tests, Audits“. Tests sind eigenständige Konsolenprogramme (kein xUnit/NUnit).

## Die nicht verhandelbaren Regeln in zehn Zeilen

1. **Nie Spieldaten committen** – nichts aus `game/` oder `.local/`, keine Captures, Snapshots, Manifeste des Inhalts,
   keine extrahierten Texte, Grafiken oder Töne. Prüfsummen, Größen, Namen und Anzahlen sind erlaubt, Inhalte nicht.
2. Testfixtures sind klein und synthetisch, keine Auszüge aus Originaldateien.
3. Nichts fällt still auf Standardwerte zurück: Unbekanntes wird gezählt, gemeldet oder abgelehnt
   (`baselines/compatibility.json`).
4. Schichten bleiben getrennt: Import → Runtime-Content → Datenmodelle → Simulation → Präsentation.
5. Die Simulation ist deterministisch: kein `float`/`double`, keine Wandzeit, keine Threads, kein ambienter Zufall, keine
   Reflection (der Test `SourceRules` erzwingt das).
6. `baselines/reference-manifest.json` ist die Messlatte; wer es ändert, tut das in einem eigenen Commit mit einer
   einzigen Ursache.
7. `TreatWarningsAsErrors`: der saubere Build hat 0 Warnungen.
8. Fremder Quelltext (EA, Community-Projekte) wird gelesen, verstanden und neu geschrieben, nie kopiert; die Quelle
   (Datei, Funktion) steht in Dokumentation oder Kommentar.
9. Neue NuGet-Abhängigkeiten nur mit Begründung; Zahlen in der Dokumentation werden gemessen, nicht übernommen.
10. Zeilenenden je Datei erhalten (`git ls-files --eol <datei>`; `.gitattributes` setzt `* -text`).

## Stil und Commits

- Bezeichner, Kommentare und Commitmessages englisch; Dokumentation (`docs/`, README) deutsch.
- Conventional Commits (`feat(simulation): …`, `fix: …`, `docs: …`, `test: …`), fachlich geschlossene Commits.
- Kein Force-Push, kein Umschreiben veröffentlichten Verlaufs.
- Beiträge stehen unter der Lizenz des Repositorys (`GPL-3.0-or-later`, `LICENSE.md`).
