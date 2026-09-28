# N+1-Handoff: Phase 5 – Fortsetzung (Spielregeln, Kampf, Wirtschaft und Produktion)

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`.

## Warum ein Fortsetzungsauftrag statt Phase 6

Der letzte Durchlauf hat Block 0.1/0.2 sowie einen ersten, funktionierenden Kern des Hauptumfangs (Wirtschaft,
Upgrades, direkter Kampf, einfache Bewegung, Produktion, Sieg/Niederlage) umgesetzt, getestet und dokumentiert
(Commits siehe unten). Der volle Umfang von `docs/GAME-RULES.md`s Zielbild – Teams/Diplomatie, Sciences,
Supply/Garrison/Transport, Reparatur/Verkauf, Radarwirkung, Spezialfähigkeiten, volle Rüstungs-/Waffen-Bestauswahl,
ein headless Referenzmatch aller Fraktionen und Golden-Master-Szenarien – ist **nicht** erreicht. Das war zum
Zeitpunkt des ursprünglichen `docs/HANDOFF-PHASE5.md` bereits als möglich benannt: „Falls Phase 5 nicht vollständig
ist, erzeuge stattdessen einen präzisen Fortsetzungsprompt für Phase 5.“ Dieses Dokument ist dieser Fortsetzungsprompt.

## Was bereits erledigt ist (mit Zahlen, neu messen vor dem Weiterarbeiten)

Lies zuerst `docs/GAME-RULES.md` (das Regelwerk-Dokument dieses Durchlaufs) vollständig, dazu die aktualisierten
Abschnitte in `docs/SIMULATION-CORE.md` (§5, §6, §11, §12), `docs/TYPED-DEFINITIONS.md` (Block-0.1-Abschnitt am Ende)
und `docs/PHASE3-COMPATIBILITY.md`.

Commits dieses Durchlaufs (in dieser Reihenfolge, `git log`):

1. `fix(data): recognize End by its first token like the reference INI reader` – Parserkorrektur, ändert Objektzahl
   und Definitionsfingerabdruck (2.113 → 2.115 Objekte).
2. `feat(data): type Weapon/Armor/Upgrade/Science/Locomotor and catalog phase-5 modules` – Block 0.1 (Domänenschemata)
   und Block 0.2 (Modulkatalogeinträge für die 15 neuen Module), inklusive der Armor-Referenzkorrektur.
3. `chore(data): regenerate the compatibility baseline for phase-5 typing` – 344 → 437 Einträge.
4. `feat(simulation): economy, upgrades, combat, movement, production and victory` – der komplette
   Simulationsschicht-Umfang dieses Durchlaufs.
5. `test: cover phase-5 economy, combat, upgrades, movement, production and victory` – `Tests.Phase5.cs` und
   Anpassungen bestehender Tests an die geänderte `ModuleBinding`-Signatur.
6. `docs: describe phase-5 game rules and refresh reference numbers` – `docs/GAME-RULES.md` (neu) und Aktualisierungen.

**Ausgangsstand zum Reproduzieren** (28. September 2026, neu messen – nicht diesen Zahlen vertrauen):

- `dotnet build CnCNew.slnx`: 0 Fehler, 10 bekannte NU1901/NU1903-Warnungen.
- Alle neun Testprogramme grün, einschließlich `CnCNew.Simulation.Tests` mit `--content-dir .local/phase2-import-a`
  und `-c Release -- --long` (216.000 Ticks, identische Hashes über drei frische Läufe und Snapshotfortsetzung).
- Modulkatalog: 205 Typen, **27 implementiert** (12 aus Phase 4, 15 aus diesem Durchlauf).
- Kompatibilitätsbasislinie: **437 Einträge**, `compat-check` besteht.
- Vollinventar: 228 geladene Dateien, 65.875 Blöcke, 12.574 Definitionen, 16.380 Module, 333.061 Schlüssel,
  15.467/17.013 Referenzen aufgelöst (1.546 sichtbare Fallbacks), Snapshot 142.618.645 Byte, SHA-256
  `A2B7859EC9C026F5EE4C898BD74482E73ED15C4CF11395B5BD80D8E35E9581B8`.
- Retail-Basisdaten (`--maps exclude`): 2.115 Objekte, SHA-256
  `128B244283C1D7466194E105FAED92A326500788C585D44191EF2DDDB3E1E378`.
- Instanziierbarkeit: **623 von 2.115 Objekten (29,5 %)**, gegenüber 26/2.113 am Ende von Phase 4.
- Stresstest (`SimTool stress`, mit allen 27 Modulen): 216.000 Ticks, Endhash `21BEAB4C15053FC0`, Kettenhash
  `3B4777772E900E0C`, drei frische Läufe und Snapshotfortsetzung identisch, alle Tabellen kehren auf null zurück.

**Implementiert** (Details, Numerik und jede bewusste Vereinfachung mit Quellverweis in `docs/GAME-RULES.md`):

- Wirtschaft: Guthaben (`Money::withdraw`-Klemmsemantik), inkrementelle Energiebilanz (noch ohne Verbrauchswirkung).
- Upgrades: 128-Bit-`UpgradeMask`, `UpgradeMuxState` (wortgetreue Portierung von `UpgradeModule.cpp`), elf
  `*Upgrade`-Module (fünf mit echter Wirkung, sechs mit korrekt laufender Zustandsmaschine und dokumentiertem No-Op,
  weil das zugehörige Teilsystem noch fehlt).
- Kampf: `WeaponCatalog`/`ArmorCatalog`, `ArmorInfo.Adjust` (`ArmorTemplate::adjustDamage`), `FireWeaponCommand` mit
  Magazin/Nachladen/Reichweite, nur Volltreffer (kein Flächenschaden), nur zwei `ArmorSet`/`WeaponSet`-Bedingungen.
- Bewegung: `AIUpdateInterface`, geradlinig, vektorbasiert, ohne Drehratenbegrenzung/Trigonometrie (neues
  `Fixed.Sqrt`, deterministische Ganzzahl-Newton-Iteration).
- Produktion: `ProductionUpdate`, Kopf-der-Warteschlange-Fortschritt, volle Sofortabbuchung/-erstattung.
- Sieg: `IsPlayerDefeated`/`TryGetWinner` („letzter Überlebender“, keine Teams).
- Block 0.2 vollständig (`StealthUpdate`, `AutoHealBehavior`); Block 0.1 vollständig für die fünf benötigten Domänen.

**Nicht erledigt / bewusst zurückgestellt** (siehe `docs/GAME-RULES.md` §10 und `docs/IMPLEMENTATION-PLAN.md` §5):

- Block 0.3 (Furchtsignal-`GameLogicRandomValue`-Zug in `ActiveBody::attemptDamage`) — **wichtig für Golden-Master-
  Vergleiche**, sollte vor den Golden-Master-Szenarien nachgeholt werden.
- Block 0.4 (RNG-Abgleich mit kompiliertem Original) — weiterhin kein Compiler verfügbar, bleibt dokumentierte Schuld.
- Spieler/Teams/Diplomatie/Fraktionen als eigenes Datenmodell (`PlayerTemplate`/`Faction` sind noch flach typisiert).
- Sciences (Kauf, Voraussetzungen, `SciencePurchasePointCost`).
- `CommandSet`/`CommandButton` als echtes Datenmodell (bisher nur `CommandSetUpgrade`s Zustandsmaschine, keine Wirkung).
- Spezialfähigkeiten mit echter `ObjectCreationList`-Ausführung (bisher nur Event-Anforderung wie `CreateObjectDie`).
- Flächenschaden, Projektil-Flugzeit, `WeaponBonus`-Multiplikatoren.
- Volle veteranenbasierte `ArmorSet`/`WeaponSet`-Bestauswahl (`SparseMatchFinder`).
- Erfahrungs-/Veteranenvergabe aus Kämpfen.
- Energie-Verlangsamungsfaktor bei der Produktion (`ProductionUpdate::calcTimeToBuild`).
- Supply (`SupplyCenter*`, `SupplyTruckAIUpdate`, `SupplyWarehouse*`), Garrison/Transport (`GarrisonContain`,
  `TransportContain`), Bau (`DozerAIUpdate`, `BuildAssistant`), Reparatur, Verkauf, Radarwirkung.
- Restliche Blockermodule (Häufigkeit absteigend, `SimTool audit-coverage .local/phase2-import-a` neu messen):
  `FlammableUpdate` (845), `PhysicsBehavior` (739, nur der für Kampf nötige Teil), `TransitionDamageFX` (708),
  `SlowDeathBehavior` (616), `GarrisonContain` (200), `CreateCrateDie` (184), `SquishCollide` (170),
  `PoisonedBehavior` (168), `FireWeaponWhenDeadBehavior` (127), `StealthDetectorUpdate` (103), `SpecialAbility` (88),
  `TransportContain` (85), `CommandButtonHuntUpdate` (79), `ToppleUpdate` (76), `SpecialAbilityUpdate` (64),
  `GenerateMinefieldBehavior` (63), `VeterancyGainCreate` (61), `OCLSpecialPower` (60), `JetSlowDeathBehavior` (59),
  `RebuildHoleBehavior` (57) — die vollständige Liste liefert das Werkzeug.
- Ein headless Referenzmatch aller Fraktionen vom Start bis zum Sieg (Punkt 7 des ursprünglichen Auftrags).
- Golden-Master-Szenarien gegen bekannte Originalwerte mit Quellverweis (Punkt 8).

## Auftrag für diesen Durchlauf

Setze den verbleibenden Umfang von Phase 5 fort, in dieser Reihenfolge (jeder Schritt mit Tests, Realdatenaudit,
Doku-Nachführung, wie in `CLAUDE.md` „Ablauf einer Phase“ beschrieben):

1. **Block 0.3 nachholen**, bevor du an Golden Masters arbeitest: `ActiveBody::attemptDamage`s Furchtsignal
   (`GameLogicRandomValue(0, 99)` bei Unterschreiten von `YELLOW_DAMAGE_PERCENT = 0.25f`) und jeder andere
   `GameLogicRandomValue`-Aufrufer der Referenz, der deine bisherigen Regeln berührt (Quellverweis Datei/Funktion).
2. **Spieler/Teams/Fraktionen als Datenmodell**: `PlayerTemplate`/`Faction` domänentypisieren (Block 0.1 fortsetzen,
   analog zu Weapon/Armor/…), Sciences als eigene Typisierung inklusive Kaufprüfung.
3. **Die nächsten Blockermodule nach Häufigkeit** (siehe Liste oben; beginne mit den fünf häufigsten, die keine
   Terrain-/KI-Abhängigkeit haben): `FlammableUpdate`, `TransitionDamageFX`, `SlowDeathBehavior`, `CreateCrateDie`,
   `PoisonedBehavior`. `PhysicsBehavior` nur so weit, wie Kampf ihn braucht (Knockback o. Ä.), nicht die volle
   Freifallphysik. Miss die Instanziierungsquote vor/nach jedem Modul.
4. **Supply/Garrison/Transport/Bau/Reparatur/Verkauf/Radarwirkung**, sobald die häufigsten Blocker abgetragen sind.
5. **Spezialfähigkeiten mit echter `ObjectCreationList`-Ausführung**: `ObjectCreationList` ist noch keine eigene
   Definitionsart (bleibt `Compatibility`) — das ist eine Voraussetzung, kein Teil dieses Punkts; typisiere sie zuerst.
6. **Ein headless Referenzmatch aller Fraktionen** (synthetische Karte im Speicher, scriptlos) von Start bis Sieg,
   als Replay aufgezeichnet, ≥ 216.000-Tick-fähig, zwei frische Läufe plus Snapshotfortsetzung hashgleich.
7. **Golden-Master-Szenarien** für zentrale Kampf- und Wirtschaftsabläufe (Schadenskette mit Rüstung, Produktionszeit,
   Supply-Einnahmen, Erfahrung) gegen bekannte Originalwerte, mit unabhängiger Nachrechnung und Quellverweis.
8. **Doku**: `docs/GAME-RULES.md`, `SIMULATION-CORE.md`, `TYPED-DEFINITIONS.md`, `PHASE3-COMPATIBILITY.md`, README,
   ARCHITECTURE, ROADMAP, IMPLEMENTATION-PLAN, CLAUDE.md nachführen; frische Zahlen, keine übernommenen.

Bleibt Phase 5 danach immer noch nicht vollständig, schreibe erneut einen präzisen Fortsetzungsprompt statt eines
Phase-6-Handoffs — das ist weiterhin ausdrücklich erlaubt.

## Nicht verhandelbare Regeln (unverändert, siehe `CLAUDE.md`)

- Unbekannte Schlüssel/Module/Enums/Referenzen fallen nie still auf Standardwerte zurück; jeder Fall ist explizit,
  gezählt, dokumentiert und gegen `baselines/compatibility.json` abgesichert.
- Determinismus: kanonische Sortierung, keine Wandzeit, keine `Dictionary`-/`HashSet`-Reihenfolgeabhängigkeit; zwei
  unabhängige Läufe müssen byteidentisch sein. `CnCNew.Simulation` hängt weiterhin nur von `CnCNew.Data` ab
  (`SourceRules`-Test).
- `TreatWarningsAsErrors=true`; nur die zehn bekannten NU1901/NU1903-Warnungen sind erklärt.
- Bestandszahlen immer neu messen, nie aus Docs oder diesem Handoff übernehmen.
- Fremder Code darf gelesen, nicht unbesehen kopiert werden; jeder portierte Algorithmus braucht einen Quellverweis
  (Datei, Funktion) im Code-Kommentar.
- Neue Befehle erhöhen `CommandTypes.Version` (aktuell 2) und bekommen eigene Codes; bestehende ändern sich nie.
- Nur lokal arbeiten, kein Remote, nichts pushen; mehrere fachlich geschlossene Commits; Working Tree am Ende sauber.

## Abschlussbericht

Wie beim vorherigen Durchlauf: Zahlen (mit Commit-Hashes), implementierte Regeln/Module, Determinismus-, Test-,
Stress- und Replayergebnisse, Modulabdeckung und Instanziierungsquote vorher/nachher mit Blockerliste,
Golden-Master-Herleitungen (falls erreicht), bekannte Grenzen und technische Schulden, `git status` und Bestätigung
„kein Remote, nichts gepusht“.
