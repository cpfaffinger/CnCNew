# N+1-Handoff: Phase 5 – Spielregeln, Kampf, Wirtschaft und Produktion

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`.

## Auftrag

Setze Phase 5 des Implementierungsplans vollständig um: Auf dem deterministischen Simulationskern (`CnCNew.Simulation`, Phase 4)
laufen die Regeln eines RTS-Matches headless – Spieler, Teams, Fraktionen, Command Sets, Tech-Voraussetzungen, Ressourcen und
Supply, Bauplätze, Konstruktion, Produktion, Reparatur, Verkauf, Waffen, Projektile, Zielwahl, Rüstung, Schaden, Status,
Erfahrung, Upgrades, Sciences, Energie, Spezialfähigkeiten, Transport, Garrison, Stealth und Sieg-/Niederlagebedingungen. Zuvor
schließt ein kleiner **Block 0** die Schuld an domänenspezifisch typisierten Definitionen und an nicht implementierten
Standardmodulen, die Phase 5 direkt braucht (siehe unten). Block 0 darf Phase 5 nicht verdrängen.

Phase 5 endet mit einem headless Referenzmatch aller Fraktionen vom Start bis zum Sieg ohne Sonderpfade und mit
Golden-Master-Szenarien für zentrale Kampf- und Wirtschaftsabläufe gegen bekannte Originalwerte. Terrain, Navigation, Sicht/Shroud
und Skirmish-KI folgen in Phase 6; **einfache Bewegung** (Geradeausfahrt mit Locomotor-Geschwindigkeit, Drehrate,
Beschleunigung, ohne Terrain/Pfadsuche) gehört in Phase 5, damit Kampf und Wirtschaft ohne Sonderpfade laufen.

Lies vor Änderungen vollständig `CLAUDE.md` (verbindliche Regeln, Befehle, Git-Regeln), `README.md`, `docs/ARCHITECTURE.md`,
`docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md`, `docs/SIMULATION-CORE.md`, `docs/TYPED-DEFINITIONS.md` (inkl. „Bekannte
Grenzen“) und `docs/PHASE3-COMPATIBILITY.md`.

## Verbindlicher Ausgangsstand

- Branch `main`, kein Remote; nichts pushen, keinen Remote anlegen. Phase 1–4 sind abgeschlossen. Phase-4-Commits (in dieser
  Reihenfolge, Hashes siehe `git log`): Handoff-/Leitfaden-Commit, `feat(data)` Härtung, `test(data)`, `feat(simulation)`,
  `test(simulation)`, `fix(data)`, weitere Tests, Doku. Der Working Tree ist sauber.
- Reproduziere zuerst: `git status`, `dotnet build CnCNew.slnx` (nur die zehn bekannten transitiven Stride-NU1901/NU1903-Warnungen),
  alle neun Testprogramme (Archives, Content, Data, IO, Images, Formats, Import, W3D mit `--content-dir`, Simulation), die Audits,
  `IniTool compat-check`, `SimTool stress`. Referenzwerte (neu messen): 228 geladene INI-Dateien (Vollinventar), 65.873 Blöcke,
  12.573 Definitionen, 16.360 Module, 333.061 Schlüssel, 13.134/14.679 Referenzen aufgelöst (1.545 sichtbare Fallbacks),
  Snapshot 142.302.102 Byte, SHA-256 `131ED730F2AD4DB85D6F50CE91A7059358CA32D6728F8EB6E91E6CF8775E58A4`; Retail-Basisdaten
  (`--maps exclude`) 12.548 Definitionen, 2.113 Objekte, SHA-256 `9B49E9DD1EE403C6797B32AD66F63CF3C0E538CEB510A84CEFF4AD5257FCCD38`;
  Modulkatalog 205 Typen (12 implementiert), Kompatibilitätsbasislinie 344 Einträge; 26 von 2.113 Objekten instanziierbar;
  Stresstest 216.000 Ticks, Endhash `A299C38BFE07681D`.
- Nach Änderungen an Definitionsdaten ändern sich Snapshothash und Basislinie erwartungsgemäß: erkläre jede Abweichung und
  aktualisiere `baselines/compatibility.json` bewusst (`IniTool compat-baseline`), nie stillschweigend.

## Architekturgrenzen

- `CnCNew.Simulation` hängt nur von `CnCNew.Data` ab; kein Stride, DirectX, UI, Content-/Archiv-/Dateisystemzugriff, keine Wandzeit,
  keine Threads im Kern, kein `Random.Shared`, kein `float`/`double` im Zustand (Fixed-Point Q47.16, Rundung: nächste, Halbwerte weg
  von null; Umrechnungen in `SimulationTiming`). Der Test `SourceRules` erzwingt das mechanisch – nicht umgehen.
- Regeln lesen nur streng typisierte Definitionen. Nicht implementierte Module machen Objekte nicht instanziierbar
  (`ObjectTemplate.MissingModules`); es gibt keine stillen Ignorierer und keine Default-Fallbacks für unbekannte Werte.
- Jede Änderung am Zustand, die den Hash beeinflusst, läuft über die Tickphasen (Befehle → Timer → Updates → Events → Zerstören →
  Spawnen → Events → Hash); Mutationen während einer Iteration laufen über Queues. Jeder Aufruf in ein Modul markiert das Entity als
  geändert (inkrementeller Hash); neuer Zustand außerhalb von Entities braucht einen Beitrag zu `DescribeGlobal`/`DescribeFull`
  und zu `LoadFull` (Snapshot/Hash bleiben aus einer Quelle, Restore-Test).
- Neue Befehle erhöhen `CommandTypes.Version` und bekommen Codes, Replay-Kodierung, Validierung mit Ablehnungsgrund und Tests;
  bestehende Codes ändern sich nie. Jede Formatänderung erhöht die Snapshot-/Replayversion mit klarer Ablehnung alter Dateien.
- Präsentation liest immutable Snapshots/Events, schreibt nur typisierte Befehle; Draw-/ClientUpdate-Module (22 Typen) bleiben
  außerhalb der Simulation.

## Block 0: Schuld aus Phase 3/4 (nur soweit für Phase 5 nötig)

0.1 **Domänenspezifische Definitionstypen** (bisher flach, `IniTool debt-report` liefert die Zahlen: Armor 884 Kopffelder alle `List`,
    Weapon 6.248 (2.449 Text/List), Locomotor 3.599 (552), Upgrade 427 (263), Science 456 (254), SpecialPower 542 (193),
    CommandButton 5.240 (4.413), Faction 409 (333), GameData 323 (85); dazu ObjectCreationList (299 Definitionen) und Rank/
    ExperienceLevel, derzeit Compat). Ableiten aus den Referenz-Parsetabellen (`Weapon.cpp`, `Armor.cpp`, `DamageFX.cpp`,
    `Locomotor.cpp`, `Upgrade.cpp`, `Science.cpp`, `SpecialPower.cpp`, `ControlBar`/`CommandButton`, `PlayerTemplate.cpp`,
    `ObjectCreationList.cpp`, `GlobalData.cpp`): `FieldSchema` je Schlüssel, Pflichtfelder, wiederholbare Schlüssel mit Parserquelle,
    strikte Namenstabellen für neue Enums/Flags; unbekannte Schlüssel = Fehler oder gezählte Liste; Einheitenkonvertierung
    (Dauer, Geschwindigkeit, Winkel, Prozent) über `SimulationTiming`. Die Sektionen der Objekte (`WeaponSet`, `ArmorSet`,
    `Prerequisites`, `UnitSpecificFX`) werden typisiert gelesen. Aktualisiere Snapshotschema, Basislinie, Doku; neue Referenzzahlen.
0.2 **Geerbte Standardmodule.** Die vom `DefaultThingTemplate` geerbten Module `StealthUpdate` (1.128 Objekte blockiert, 141 nur durch
    dieses) und `AutoHealBehavior` (774 / 3) blockieren fast alle Objekte. Implementiere sie mit ihren realen Feldern und
    Upgrade-Aktivierung (kein ruhender Platzhalter); das hebt die Instanziierungsquote sichtbar.
0.3 **Zufallsverbräuche des Originals.** `ActiveBody::attemptDamage` zieht `GameLogicRandomValue(0, 99)` beim Unterschreiten der
    Furchtschwelle; solche Verbräuche gehören in Phase 5 in die Regeln, sonst sind Golden-Master-Vergleiche unmöglich. Liste alle
    `GameLogicRandomValue`-Aufrufer der Referenz, die deine Regeln berühren, und übernimm sie mit Quellverweis.
0.4 **RNG-Abgleich mit kompiliertem Original.** In Phase 4 stand kein C++-Compiler zur Verfügung (unabhängige Python-Transkription
    als Ersatz). Falls ein Compiler (MSVC/clang/gcc) verfügbar ist, kompiliere `RandomValue.cpp` mit einem Minimal-Harness und
    vergleiche die Golden-Sequenzen; andernfalls belasse die Schuld dokumentiert.
0.5 Nach jedem Schritt: Tests, `compat-check`, Doku (`TYPED-DEFINITIONS.md`, `PHASE3-COMPATIBILITY.md`, `SIMULATION-CORE.md`).

Block 0 endet mit einem eigenen Commit und aktualisierten Zahlen.

## Vollständiger Umfang Phase 5

Neue Regeln als Module und Systeme in `CnCNew.Simulation` (Unterordner nach Domäne), jeweils mit strengem Katalogschema, Bindung,
Tests und Modulabdeckungszahlen. Reihenfolge nach Wirkung (Blocker-Liste `SimTool audit-coverage`; Anzahl blockierter Objekte
in Klammern): `FlammableUpdate` (843), `PhysicsBehavior` (737, nur der für Kampf nötige Teil), `TransitionDamageFX` (706; Schadenszustände
und FX-Anforderungen), `SlowDeathBehavior` (614), `AIUpdateInterface`-Familie soweit für Kampf und Produktion nötig (534),
`ProductionUpdate` (236), `GarrisonContain`/`TransportContain`, `CreateCrateDie`, `SquishCollide`, `PoisonedBehavior`,
`*Upgrade`-Module, `SpecialAbility*`, Stealth, Supply-Module (`SupplyCenter*`, `SupplyTruckAIUpdate`, `SupplyWarehouse*`), Docks,
Bau (`DozerAIUpdate`, Konstruktion, `BuildAssistant`), Kraftwerke/Energie, Radar, Reparatur, Verkauf.

1. **Spieler und Teams.** Spielertabelle aus `PlayerTemplate`/`Faction` (Ressourcen, Energie, Ränge, Sciences, Upgrades, Diplomatie,
   Teams). Befehle mit Ownership-Validierung erweitern (Bauen, Produzieren, Verkaufen, Reparieren, Upgrade kaufen, Science kaufen,
   Bewegen, Angreifen, Spezialfähigkeit, Rallypunkt). Neue Befehlstypen mit Versionierung.
2. **Ressourcen, Supply, Produktion, Konstruktion.** Geld, Kosten, Bauzeiten (Sekunden → Ticks), Warteschlangen, Ausgang, Voraussetzungen
   (`Prerequisites`), Bauplätze (footprintfrei; Gelände-/Kollisionsprüfung folgt in Phase 6), Reparatur, Verkauf, Erstattung.
3. **Waffen, Schaden, Rüstung.** Waffenvorlagen mit Nachladen, Salven, Reichweiten, Vorhalt, Projektilen (Dumb-/Sofort-Treffer zuerst),
   Schadensarten und Rüstungskoeffizienten (`Armor`/`ArmorSet`), Schadenszustände, Todesarten, Erfahrung/Ränge, Waffenboni. Fear-/Retaliate-
   Zufallsverbräuche wie im Original.
4. **Upgrades, Sciences, Spezialfähigkeiten.** `Upgrade`-Aktivierung über Module (`TriggeredBy`/`ConflictsWith`), Sciences, Kommandoleisten
   (`CommandSet`/`CommandButton`) als Datenmodell, Spezialkräfte mit Cooldown und `ObjectCreationList`-Ausführung (die in Phase 4 nur
   angeforderten `FxRequested`/`CreationListRequested`/`WeaponFireRequested`-Events werden zu echten Regeln bzw. bleiben Präsentation).
5. **Einfache Bewegung.** Locomotor-Modell (Geschwindigkeit, Beschleunigung, Drehrate, Luft/Boden) ohne Terrain; Positionen und Winkel sind
   bereits Zustand.
6. **Sieg und Niederlage.** Regeln wie `VictoryConditions`; Match-Ende als Event.
7. **Referenzmatch.** Ein headless Referenzmatch aller Fraktionen (synthetische Karte im Speicher, scriptlos) von Start bis Sieg, als Replay
   aufgezeichnet, ≥ 216.000 Ticks-fähig, zwei frische Läufe plus Snapshotfortsetzung hashgleich; Stresstest mit den neuen Modulen.
8. **Golden-Master-Szenarien.** Kleine, synthetische, rechtlich unbedenkliche Szenarien für zentrale Kampf- und Wirtschaftsabläufe (Schadenskette
   mit Rüstung, Produktionszeit, Supply-Einnahmen, Erfahrung) gegen bekannte Originalwerte aus den Referenzquellen/-daten; Herleitung
   der Erwartungswerte im Doc mit Quellverweis (Datei, Funktion) und unabhängiger Nachrechnung.
9. **Doku.** README, Architektur, Roadmap, Implementierungsplan aktualisieren; neues `docs/GAME-RULES.md` (Regeln, Tickphasen-Einbindung,
   Numerik je Regel, Befehlstabelle, Modulabdeckung, bekannte Grenzen); `SIMULATION-CORE.md` und `CLAUDE.md` nachführen.

## Mindesttests

- Block 0: Definitionstypen (Schemata, Pflichtfelder, unbekannte Schlüssel), Einheitenumrechnung, `compat-check`, Modulkatalog vs. Bindung.
- Je Modul/Regel: Golden-Werte, Grenzwerte (Überlauf, 0, negative Werte), Zufallsverbrauch, Snapshot-Roundtrip, Hash-Determinismus
  bei permutierter Einfügereihenfolge, Ablehnungsfälle der Befehle.
- Produktion/Wirtschaft/Kampf: Ende-zu-Ende-Szenarien mit erwarteten Zahlen; Sieg/Niederlage.
- Replay des Referenzmatches über lange Läufe, nach Snapshotfortsetzung, mit beschädigten/versionsfremden Dateien.
- Realdatentest (`--content-dir`): Instanziierungsquote und Blockerliste vor/nach jedem Schritt; alle instanziierbaren Objekte
  laufen ohne Ausnahme und deterministisch.
- Stresstest ohne Leaks/Deadlocks/Nichtdeterminismus mit den neuen Modulen; Budgets fortschreiben.

## Definition of Done

Phase 5 ist erst abgeschlossen, wenn:

- Solution-Build und alle bestehenden und neuen Tests grün sind (Warnungen: nur die bekannten NU1901/NU1903);
- Block 0 umgesetzt ist, `compat-check` gegen die (bewusst aktualisierte) Basislinie besteht und die Simulation nur von `CnCNew.Data` abhängt;
- die Instanziierungsquote gegenüber Phase 4 (26/2.113) deutlich steigt und jeder Blocker mit Zahl dokumentiert ist;
- ein headless Referenzmatch aller Fraktionen ohne Sonderpfade vom Start bis zum Sieg deterministisch läuft und die Golden-Master-Szenarien
  bestehen;
- Determinismus über lange Läufe und nach Snapshotfortsetzung nachgewiesen ist, Stresstest und Budgets bestehen;
- Dokumentation und Code übereinstimmen, der Implementierungsplan bekannte Grenzen ehrlich benennt;
- der Working Tree sauber ist, alle Änderungen in mehreren fachlich geschlossenen Commits vorliegen, keine Originalassets, lokalen
  Snapshots, Manifeste, Captures, Buildoutputs oder IDE-Daten committed wurden und nichts gepusht wurde.

## Abschlussbericht und N+1

Berichte: Block-0-Ergebnisse (mit Zahlen), implementierte Regeln und Module, Determinismusregeln, Test-/Stress-/Replayergebnisse samt Hashwerten,
Modulabdeckung und Instanziierungsquote (vorher/nachher, Blockerliste), Golden-Master-Herleitungen, bekannte Grenzen und technische Schulden,
alle Commit-Hashes, finalen Git-Status sowie Bestätigung „kein Remote, nichts gepusht“.

Erzeuge danach als `docs/HANDOFF-PHASE6.md` einen eigenständigen N+1-Handoff für Phase 6 „Welt, Terrain, Bewegung, Sicht und Skirmish-KI“ und
committe ihn. Falls Phase 5 nicht vollständig ist, erzeuge stattdessen einen präzisen Fortsetzungsprompt für Phase 5.
