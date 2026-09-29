# N+1-Handoff: Phase 5 – Fortsetzung (Spielregeln, Kampf, Wirtschaft und Produktion)

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`.

## Warum ein Fortsetzungsauftrag statt Phase 6

Über mehrere Durchläufe wurden Block 0 (0.1–0.3), ein erster funktionierender Kern des Hauptumfangs (Wirtschaft,
Sciences, Upgrades, direkter Kampf, einfache Bewegung, Produktion, Sieg/Niederlage), sechs weitere Blockermodule
(Schadenszustände/`TransitionDamageFX`, `FlammableUpdate`, `SlowDeathBehavior`, `FireWeaponWhenDeadBehavior`,
`StealthDetectorUpdate`, `PoisonedBehavior`) sowie das komplette Containment-Subsystem (`GarrisonContain`, der bisher
größte Einzelsprung mit +167 instanziierbaren Objekten, und `TransportContain`, +2) umgesetzt, getestet und
dokumentiert (Commits siehe unten). Der
volle Umfang von `docs/GAME-RULES.md`s Zielbild – Teams/Diplomatie, Supply/Transport, Reparatur/Verkauf, Radarwirkung,
Spezialfähigkeiten, volle Rüstungs-/Waffen-Bestauswahl, ein headless Referenzmatch aller Fraktionen und
Golden-Master-Szenarien – ist **nicht** erreicht. Das war von Anfang an als möglich benannt: „Falls Phase 5 nicht
vollständig ist, erzeuge stattdessen einen präzisen Fortsetzungsprompt für Phase 5.“ Dieses Dokument ist dieser
Fortsetzungsprompt.

## Was bereits erledigt ist (mit Zahlen, neu messen vor dem Weiterarbeiten)

Lies zuerst `docs/GAME-RULES.md` vollständig (insbesondere die neuen §3-Unterabschnitte und §6a), dazu die
aktualisierten Abschnitte in `docs/SIMULATION-CORE.md` (§5, §6, §11, §12), `docs/TYPED-DEFINITIONS.md` (die
Block-0.1-Abschnitte am Ende) und `docs/PHASE3-COMPATIBILITY.md`.

Commits seit dem letzten Handoff (in dieser Reihenfolge, `git log`), pro Modul jeweils drei: Datenschema+Baseline,
Simulationsimplementierung, Tests (Ausnahme: `GarrisonContain` hat vier, siehe unten):

1. Schadenszustände (`ActiveBody.CalcDamageState`) und `TransitionDamageFX` (`c99fc29`, `93d4954`, `6604046`).
2. `FlammableUpdate` (`0e8f63d`, `ffed6a0`, `66cc0de`) — führte den generischen `IDamageModule`-Hook ein
   (`OnDamage`/`OnBodyDamageStateChange`).
3. `SlowDeathBehavior` (`e0d20f7`, `cdd74fa`, `63e03c0`) — gewichtete Geschwister-Lotterie beim Tod.
4. `FireWeaponWhenDeadBehavior` (`289ad03`, `8009713`, `f661af4`).
5. `StealthDetectorUpdate` (`5c1c59f`, `35a4985`, `1d0159b`) — führte `ObjectTemplate.VisionRange` und
   `StealthUpdate.MarkDetected`/den `DETECTED`-Ablaufmechanismus ein.
6. `PoisonedBehavior` (`085ab72`, `2551b0a`, `e0fd3f3`) — führte `IDamageModule.OnHealing` ein und schloss eine
   bestehende Lücke (`OnBodyDamageStateChange` fehlte bisher im Heilungspfad).
7. `GarrisonContain` (`5a16c3c` Schema+Baseline, `784cb22` ein eigenständiger Fix — `ModuleConfigReader.Real` kannte
   `Percentage`-typisierte Werte noch nicht, aufgedeckt durch `DamagePercentToUnits` —, `d3f0a9a` Containment-
   Infrastruktur + Modul, `0e77be1` Tests) — größter Einzelsprung dieser Phase, siehe unten.
8. `TransportContain` (`515c7a0` Schema+Baseline, `64965d3` `ContainModuleBase`-Extraktion + Modul, `52fe5e7` Tests) —
   zweiter Implementierer der Containment-Infrastruktur, rechtfertigte die `ContainModuleBase`-Extraktion.
9. `CreateCrateDie` (`323a827` Schema+Baseline `CrateData`, `918498a` `CrateCatalog` + Modul) — Zugreihenfolge wie
   `CreateCrateDie.cpp`; Kistenobjekte brauchen noch `*CrateCollide`.
10. Erzeugungslisten (`0998461` `ObjectCreationList` streng typisiert, `3c097b5` Tests/Fixtures, `e2d7b0a`
    `GameData/Gravity` typisiert, `adc2e94` `SimulationWorld.CreateFromList` mit verzögertem `SpawnSetup`,
    `9a32488` Tests inklusive `FixedTrig`) — `docs/GAME-RULES.md` §6b.

11. Referenzmatch und Golden Master (`19b2df4` Replay-Format v2 mit Fraktionen und allen 16 Befehlstypen, `e5bf4cf`
    `ReferenceMatchScenario` + `SimTool reference-match` + synthetischer Nachweis, `dd3e6df` Golden Master für
    Schadenskette und Produktionsuhr, `9959b49` Doku) — `docs/GAME-RULES.md` §7a. Das Match läuft auf der
    synthetischen Fixture, **nicht** auf Retail-Daten (siehe unten).

**Wichtige Neupriorisierung gegenüber dem vorigen Handoff:** Die ursprüngliche Reihenfolge „nach Häufigkeit“
wurde durchgehend durch frische `audit-coverage`-Messungen ersetzt. `CreateCrateDie` schaltet **0 Objekte allein**
frei (jedes Vorkommen hängt zusätzlich an einem anderen, noch fehlenden Modul) und bräuchte einen neuen `Crate`-
Katalog **und** eine Beziehungs-/Diplomatieprüfung — bewusst zurückgestellt. `ToppleUpdate` wurde per Referenzstudium
als Phase-6-Kandidat verifiziert (braucht echte Kollisionserkennung), nicht nur vermutet. `GarrisonContain` (167
allein) stellte sich als der mit Abstand größte Einzelblocker heraus, obwohl es in der ursprünglichen
„nach Häufigkeit“-Liste weiter hinten stand. **Miss vor jedem weiteren Modul selbst nach, was es allein freischaltet**
(`SimTool audit-coverage`) — Häufigkeit einer Modulverwendung ist kein guter Indikator für den Freischaltungsgewinn.

**Ausgangsstand zum Reproduzieren** (29. September 2026, neu messen – nicht diesen Zahlen vertrauen):

- `dotnet build CnCNew.slnx`: 0 Fehler, 10 bekannte NU1901/NU1903-Warnungen.
- Alle acht Testprogramme grün, einschließlich `CnCNew.Simulation.Tests` mit `--content-dir .local/phase2-import-a`
  und `-c Release -- stress` (216.000 Ticks, identische Hashes über drei frische Läufe und Snapshotfortsetzung).
- Modulkatalog: 205 Typen, **36 implementiert** (12 aus Phase 4, 24 aus Phase 5).
- Kompatibilitätsbasislinie: **784 Einträge**, `compat-check` besteht.
- Vollinventar: 228 geladene Dateien, 65.875 Blöcke, 12.574 Definitionen, 16.380 Module, 333.061 Schlüssel,
  17.291/18.841 Referenzen aufgelöst (1.550 sichtbare Fallbacks), Snapshot 144.432.181 Byte, SHA-256
  `A1BB5D3464024113750A338C9DE97D9454CA718595EFD6DBB5395F6B5AB2170B` (Hash geändert gegenüber der letzten Messung:
  `ObjectCreationList`, `CrateData`, `GameData/Gravity` sind jetzt strikt typisiert).
- Retail-Basisdaten (`--maps exclude`): 2.115 Objekte, SHA-256
  `45E6802750BFBAB72B20EBAA1EBC2ED54C555BBB86B50C586F3BCD6F536C3606`.
- Instanziierbarkeit: **863 von 2.115 Objekten (40,8 %)**, gegenüber 26/2.113 am Ende von Phase 4 und 694/2.115 vor
  dem Containment-Subsystem.
- Stresstest (`SimTool stress`, mit allen 36 Modulen): 216.000 Ticks, Endhash `9E4AF85CBFBB84C6`, Kettenhash
  `36211DD938214B9C` (Erzeugungslisten verbrauchen jetzt Zufallszüge), Läufe und Snapshotfortsetzung identisch, alle
  Tabellen kehren auf null zurück.
- `CommandTypes.Version = 4` (16 Befehlstypen; `EnterContainerCommand`/`ExitContainerCommand` neu); Replay-Format
  Version 2 (Fraktionen in der Spielertabelle, alle 16 Befehlstypen kodierbar).
- 47 Simulationstests grün (`Tests.ReferenceMatch`, `Tests.Phase5GoldenDamageChain`, `Tests.Phase5GoldenProduction`
  neu); Stresstest (frisch gemessen): 914.394 Befehle, Replay-Datei 47.094.803 Byte, Spitze 15.775 Entities,
  2.170 `UnknownEntity`-Ablehnungen, Snapshot bei Tick 108.000: 10.262.399 Byte.

**Verbleibende Blocker nach Objekten, die ausschließlich noch an ihnen hängen** (frisch gemessen,
`SimTool audit-coverage`, 29. September): `PhysicsBehavior` 158, `ToppleUpdate` 66, `RebuildHoleBehavior` 57,
`RailroadBehavior` 25, `BaikonurLaunchPower` 23, `GenerateMinefieldBehavior` 13, `ReplaceObjectUpgrade` 12,
`VeterancyGainCreate` 8, `SquishCollide` 2, `SpecialAbility`/`CommandButtonHuntUpdate`/`SpecialAbilityUpdate` je 0. `PhysicsBehavior`/
`SquishCollide`/`ToppleUpdate` brauchen alle drei echte Kollisionserkennung (`ToppleUpdate::onCollide` löst nur bei
echtem Geometrie-Overlap eines Fahrzeugs aus, geprüft per Referenzstudium; die einzigen anderen Aufrufer von
`applyTopplingForce` im Original — `NeutronMissileSlowDeathUpdate`, `TensileFormationUpdate`, `WaveGuideUpdate` —
sind selbst unimplementierte Phase-6-Module) — ausdrücklich Phase 6 (`ModuleCatalog.PlannedTarget` klassifiziert alle
drei bereits so).

**Implementiert** (Details, Numerik und jede bewusste Vereinfachung mit Quellverweis in `docs/GAME-RULES.md`):

- Wirtschaft, Spieler/Fraktionen, Sciences, Upgrades, Kampf, Bewegung, Produktion, Sieg: siehe die vorigen Handoffs
  (unverändert in diesem Durchlauf) und `docs/GAME-RULES.md` §1–§7.
- Schadenszustände (`PRISTINE`/`DAMAGED`/`REALLYDAMAGED`/`RUBBLE`) und `TransitionDamageFX` (OCL-Anforderung beim
  Wechsel in einen schlechteren Zustand, nur mit auflösbarer Schadensquelle).
- `FlammableUpdate`: Flammenschaden-Akkumulation, Entzündung, periodischer Selbstschaden, dauerhaftes `BURNED`.
- `SlowDeathBehavior`: gewichtete Geschwister-Lotterie, verzögerte Zerstörung, `Entity.SlowDeathStarted`-Sperre.
- `FireWeaponWhenDeadBehavior`: `DeathWeapon` beim Tod, `StartsActive`/`TriggeredBy`, erneute `ConflictsWith`-Prüfung
  beim Tod selbst (nicht nur bei der Aktivierung).
- `StealthDetectorUpdate`: periodische Enttarnungssuche, `StealthUpdate.MarkDetected`/`DETECTED`-Ablauf,
  `ObjectTemplate.VisionRange` als Fallback für `DetectionRange`.
- `PoisonedBehavior`: periodischer Gift-Nachschaden mit dem tatsächlich zugefügten Betrag, Verlängerung bei
  erneuter Vergiftung, Heilung beendet den Zyklus (neuer `IDamageModule.OnHealing`-Hook).
- **Containment (`docs/GAME-RULES.md` §6a), komplett:** `Entity.ContainedBy`/`Entity.Container`, `IContainerModule`,
  `ContainModuleBase` (geteilte Gating-/Ein-/Austritts-/Todeslogik), `EnterContainerCommand`/`ExitContainerCommand`.
  `GarrisonContain`: Kapazität, `KindOf`/Beziehungsfilter (gleicher Besitzer = Verbündeter, keine Teams), Sperre und
  Sofortauswurf bei `REALLYDAMAGED` (außer `GARRISONABLE_UNTIL_DESTROYED`), periodische Heilung, Tod wendet
  `DamagePercentToUnits` an und wirft Überlebende aus (tötet sie nicht). `TransportContain`: gewichtete Kapazität
  (`ObjectTemplate.TransportSlotCount`), kontinuierliche `HealthRegen%PerSec`-Heilung statt Rampe.
- Block 0.1 (Weapon/Armor/Upgrade/Science/Locomotor/Faction), 0.2 (`StealthUpdate`/`AutoHealBehavior`) und 0.3
  (Furchtsignal) vollständig, unverändert seit dem letzten Handoff.

**Nicht erledigt / bewusst zurückgestellt** (siehe `docs/GAME-RULES.md` §10 und `docs/IMPLEMENTATION-PLAN.md` §5):

- Block 0.4 (RNG-Abgleich mit kompiliertem Original) — weiterhin kein Compiler verfügbar, bleibt dokumentierte Schuld.
- Teams/Diplomatie/Allianzen als eigenes Datenmodell (Sieg bleibt „letzter Überlebender" ohne Teams;
  `StealthDetectorUpdate`/`GarrisonContain` behelfen sich mit „gleicher Besitzer = Verbündeter").
- `CommandSet`/`CommandButton` als echtes Datenmodell (bisher nur `CommandSetUpgrade`s Zustandsmaschine, keine Wirkung).
- Spezialfähigkeiten (`SpecialAbility*`, `OCLSpecialPower`); die Listenausführung selbst existiert, `CreateDebris`,
  `ApplyRandomForce`, `FireWeapon`, `Attack` und `DeliverPayload` werden als `CreationSkipped` gemeldet.
- Flächenschaden, Projektil-Flugzeit, `WeaponBonus`-Multiplikatoren.
- Volle veteranenbasierte `ArmorSet`/`WeaponSet`-Bestauswahl (`SparseMatchFinder`).
- Erfahrungs-/Veteranenvergabe aus Kämpfen; Rang-/`SciencePurchasePoints`-Vergabe aus Erfahrung.
- Energie-Verlangsamungsfaktor bei der Produktion (`ProductionUpdate::calcTimeToBuild`).
- `InitialRoster`/`InitialPayload` (synchrones verschachteltes Erzeugen beim Konstruieren), Feuern aus dem Container
  heraus, Garnisonspunkte (art-abgeleitet), Eroberungshandling. Supply (`SupplyCenter*`, `SupplyTruckAIUpdate`,
  `SupplyWarehouse*`), Bau (`DozerAIUpdate`, `BuildAssistant`), Reparatur, Verkauf, Radarwirkung — alle weiterhin
  komplett offen.
- `*CrateCollide`-Module (die Kisten selbst).
- `PhysicsBehavior`/`SquishCollide`/`ToppleUpdate` (echte Kollisionserkennung, ausdrücklich Phase 6 — für
  `ToppleUpdate` per Referenzstudium verifiziert, siehe oben).
- **Referenzmatch aller Fraktionen auf Retail-Daten:** Szenario, Aufnahme, Replay und Snapshotfortsetzung sind fertig und
  mit einer synthetischen Fixture bewiesen. `SimTool reference-match .local/phase2-import-a` meldet jedoch 0 von 13
  spielbaren Fraktionen (Exitcode 3): 0 von 41 (America), 26 (China), 33 (GLA) usw. bewaffneten Einheiten und 0
  Produktionsgebäuden je Seite sind instanziierbar. Größte Blocker: `PhysicsBehavior` (739 Objekte), `SquishCollide` (170),
  `SpecialAbility` (88), `CommandButtonHuntUpdate` (79), `ToppleUpdate` (76), `OCLSpecialPower` (60),
  `BaseRegenerateUpdate` (55), `DefaultProductionExitUpdate` (42). Ohne Phase-6-Physik und ein Spezialkraft-Subsystem ist
  der Retail-Match nicht erreichbar; Ersatzobjekte (`MatchParticipant.Substitution`) helfen erst, wenn wenigstens ein
  bewaffneter Einheitentyp je Seite instanziierbar ist.
- **Golden Master:** Schadenskette und Produktionsuhr sind mit unabhängigem Modell abgesichert (`Tests.GoldenMaster.cs`),
  Supply-Einnahmen und Erfahrung fehlen mangels Implementierung. Es gibt keine Originalläufe als Referenz, nur aus dem
  Quelltext abgeleitete Rechnungen.

## Auftrag für diesen Durchlauf

Setze den verbleibenden Umfang von Phase 5 fort, in dieser Reihenfolge (jeder Schritt mit Tests, Realdatenaudit,
Doku-Nachführung, wie in `CLAUDE.md` „Ablauf einer Phase“ beschrieben). Miss vor jedem Schritt selbst nach, welches
Modul wie viele Objekte **allein** freischaltet (`SimTool audit-coverage .local/phase2-import-a`) — verlass dich nicht
auf die obige Liste, sie ist bereits eine Momentaufnahme:

1. ~~`CreateCrateDie`~~ – erledigt (Punkt 9 oben).
2. ~~Erzeugungslisten typisieren und ausführen~~ – erledigt (Punkt 10 oben).
3. **Teams/Diplomatie als Datenmodell**, falls für den Referenzmatch (Punkt 4) nötig — sonst zurückstellen und im
   headless Referenzmatch bei „letzter Überlebender ohne Teams" bleiben (dokumentierte Vereinfachung).
4. **Referenzmatch auf Retail-Daten ermöglichen.** Die Maschinerie steht (`ReferenceMatchScenario`, `SimTool
   reference-match`); es fehlen instanziierbare bewaffnete Einheiten und Produktionsgebäude. Vorgehen: Zuerst
   `reference-match` und `audit-coverage` neu messen, dann je Seite gezielt die Module freischalten, die das kleinste
   Set an Einheiten plus Kommandozentrale/Kaserne/Waffenfabrik instanziierbar machen (Kandidaten: `DefaultProductionExitUpdate`,
   `QueueProductionExitUpdate`, `BaseRegenerateUpdate`, `HealContain`, `RebuildHoleBehavior`/`RebuildHoleExposeDie`,
   `GenerateMinefieldBehavior`, `PreorderCreate`, `RadarUpdate`, ein `OCLSpecialPower`-/`SpecialPower`-Subsystem, und für die
   Einheiten `PhysicsBehavior`/`SquishCollide` als Phase-6-Vorgriff). Erst wenn mindestens zwei Fraktionen teilnehmen,
   Match von Start bis Sieg, ≥ 216.000-Tick-fähig, zwei frische Läufe plus Snapshotfortsetzung hashgleich, in den Doku
   und in einem Test unter `--content-dir` nachweisen.
5. **Golden-Master-Szenarien für Supply-Einnahmen und Erfahrung**, sobald beides implementiert ist (Schadenskette und
   Produktionsuhr sind erledigt), mit unabhängiger Nachrechnung und Quellverweis.
6. **Doku**: `docs/GAME-RULES.md`, `SIMULATION-CORE.md`, `TYPED-DEFINITIONS.md`, `PHASE3-COMPATIBILITY.md`, README,
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
- Neue Befehle erhöhen `CommandTypes.Version` (aktuell 4) und bekommen eigene Codes; bestehende ändern sich nie.
- Nur lokal arbeiten, kein Remote, nichts pushen; mehrere fachlich geschlossene Commits; Working Tree am Ende sauber.

## Abschlussbericht

Wie bei den vorherigen Durchläufen: Zahlen (mit Commit-Hashes), implementierte Regeln/Module, Determinismus-, Test-,
Stress- und Replayergebnisse, Modulabdeckung und Instanziierungsquote vorher/nachher mit Blockerliste,
Golden-Master-Herleitungen (falls erreicht), bekannte Grenzen und technische Schulden, `git status` und Bestätigung
„kein Remote, nichts gepusht“.
