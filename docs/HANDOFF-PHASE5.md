# N+1-Handoff: Phase 5 – Fortsetzung (Spielregeln, Kampf, Wirtschaft und Produktion)

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`.

## Warum ein Fortsetzungsauftrag statt Phase 6

Über mehrere Durchläufe wurden Block 0 (0.1–0.3), ein erster funktionierender Kern des Hauptumfangs (Wirtschaft,
Sciences, Upgrades, direkter Kampf, einfache Bewegung, Produktion, Sieg/Niederlage), sechs weitere Blockermodule
(Schadenszustände/`TransitionDamageFX`, `FlammableUpdate`, `SlowDeathBehavior`, `FireWeaponWhenDeadBehavior`,
`StealthDetectorUpdate`, `PoisonedBehavior`) sowie die Containment-Grundinfrastruktur mit `GarrisonContain` (der bisher
größte Einzelsprung: +167 instanziierbare Objekte) umgesetzt, getestet und dokumentiert (Commits siehe unten). Der
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
- Modulkatalog: 205 Typen, **34 implementiert** (12 aus Phase 4, 22 aus Phase 5).
- Kompatibilitätsbasislinie: **771 Einträge**, `compat-check` besteht.
- Vollinventar: 228 geladene Dateien, 65.875 Blöcke, 12.574 Definitionen, 16.380 Module, 333.061 Schlüssel,
  15.724/17.270 Referenzen aufgelöst (1.546 sichtbare Fallbacks), Snapshot 143.781.645 Byte, SHA-256
  `522CC08D0B4001D5B1398CAAA344849504509439E45435C2B014C347DA0E2DCC` (unverändert seit `StealthDetectorUpdate` —
  `GarrisonContain` fügt keine neuen typisierten Referenzen hinzu, nur Flags/Bool/Percent).
- Retail-Basisdaten (`--maps exclude`): 2.115 Objekte, SHA-256
  `BFFA27023F6F8FCB75FCEDCABB60A88DE11135C1750D9D2E686057DFFE41F9E9`.
- Instanziierbarkeit: **861 von 2.115 Objekten (40,7 %)**, gegenüber 26/2.113 am Ende von Phase 4 und 694/2.115 vor
  `GarrisonContain`.
- Stresstest (`SimTool stress`, mit allen 34 Modulen): 216.000 Ticks, Endhash `9D6E0BDCECE43569`, Kettenhash
  `393F17B2DCF76EC2` (beide geändert gegenüber der letzten Messung: `Entity.ContainedBy` ist jetzt Teil jeder Entität
  im Snapshot/Hash, auch wenn sie kein `GarrisonContain` nutzt), drei frische Läufe und Snapshotfortsetzung identisch,
  alle Tabellen kehren auf null zurück.
- `CommandTypes.Version = 4` (16 Befehlstypen; `EnterContainerCommand`/`ExitContainerCommand` neu).

**Verbleibende Blocker nach Objekten, die ausschließlich noch an ihnen hängen** (frisch gemessen,
`SimTool audit-coverage`): `PhysicsBehavior` 119, `ToppleUpdate` 66, `SquishCollide` 2, `TransportContain` 2,
`CreateCrateDie`/`SpecialAbility`/`CommandButtonHuntUpdate`/`SpecialAbilityUpdate` je 0. `PhysicsBehavior`/
`SquishCollide`/`ToppleUpdate` brauchen alle drei echte Kollisionserkennung (`ToppleUpdate::onCollide` löst nur bei
echtem Geometrie-Overlap eines Fahrzeugs aus, geprüft per Referenzstudium; die einzigen anderen Aufrufer von
`applyTopplingForce` im Original — `NeutronMissileSlowDeathUpdate`, `TensileFormationUpdate`, `WaveGuideUpdate` —
sind selbst unimplementierte Phase-6-Module) — ausdrücklich Phase 6 (`ModuleCatalog.PlannedTarget` klassifiziert alle
drei bereits so). `TransportContain` kann die neue `IContainerModule`-Infrastruktur wiederverwenden, braucht aber ein
pro-Template-Slotkosten-Feld (`TransportSlotCount`, `ThingTemplate::getTransportSlotCount`) und gewichtete statt
einfache Kapazitätsrechnung (`m_extraSlotsInUse + containCount + transportSlotCount <= containMax`) — für nur 2
Objekte allein zurückgestellt, aber ein kleiner Folgeauftrag, kein neues Subsystem mehr.

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
- **Containment (`docs/GAME-RULES.md` §6a):** `Entity.ContainedBy`/`Entity.Container`, `IContainerModule` (bewusst
  schmal gehalten, bis `TransportContain` ihn tatsächlich erweitert), `EnterContainerCommand`/`ExitContainerCommand`.
  `GarrisonContain`: Kapazität, `KindOf`/Beziehungsfilter (gleicher Besitzer = Verbündeter, keine Teams), Sperre und
  Sofortauswurf bei `REALLYDAMAGED` (außer `GARRISONABLE_UNTIL_DESTROYED`), periodische Heilung, Tod wendet
  `DamagePercentToUnits` an und wirft Überlebende aus (tötet sie nicht).
- Block 0.1 (Weapon/Armor/Upgrade/Science/Locomotor/Faction), 0.2 (`StealthUpdate`/`AutoHealBehavior`) und 0.3
  (Furchtsignal) vollständig, unverändert seit dem letzten Handoff.

**Nicht erledigt / bewusst zurückgestellt** (siehe `docs/GAME-RULES.md` §10 und `docs/IMPLEMENTATION-PLAN.md` §5):

- Block 0.4 (RNG-Abgleich mit kompiliertem Original) — weiterhin kein Compiler verfügbar, bleibt dokumentierte Schuld.
- Teams/Diplomatie/Allianzen als eigenes Datenmodell (Sieg bleibt „letzter Überlebender" ohne Teams;
  `StealthDetectorUpdate`/`GarrisonContain` behelfen sich mit „gleicher Besitzer = Verbündeter").
- `CommandSet`/`CommandButton` als echtes Datenmodell (bisher nur `CommandSetUpgrade`s Zustandsmaschine, keine Wirkung).
- Spezialfähigkeiten mit echter `ObjectCreationList`-Ausführung (bisher nur Event-Anforderung wie `CreateObjectDie`).
- Flächenschaden, Projektil-Flugzeit, `WeaponBonus`-Multiplikatoren.
- Volle veteranenbasierte `ArmorSet`/`WeaponSet`-Bestauswahl (`SparseMatchFinder`).
- Erfahrungs-/Veteranenvergabe aus Kämpfen; Rang-/`SciencePurchasePoints`-Vergabe aus Erfahrung.
- Energie-Verlangsamungsfaktor bei der Produktion (`ProductionUpdate::calcTimeToBuild`).
- `TransportContain` (siehe oben — kleiner Folgeauftrag), `InitialRoster`/`InitialPayload` (synchrones verschachteltes
  Erzeugen beim Konstruieren), Feuern aus dem Container heraus, Garnisonspunkte (art-abgeleitet), Eroberungshandling.
  Supply (`SupplyCenter*`, `SupplyTruckAIUpdate`, `SupplyWarehouse*`), Bau (`DozerAIUpdate`, `BuildAssistant`),
  Reparatur, Verkauf, Radarwirkung — alle weiterhin komplett offen.
- `CreateCrateDie` (bräuchte einen neuen `Crate`-Katalog und eine Beziehungsprüfung, siehe oben).
- `PhysicsBehavior`/`SquishCollide`/`ToppleUpdate` (echte Kollisionserkennung, ausdrücklich Phase 6 — für
  `ToppleUpdate` per Referenzstudium verifiziert, siehe oben).
- Ein headless Referenzmatch aller Fraktionen vom Start bis zum Sieg (Punkt 4 unten).
- Golden-Master-Szenarien gegen bekannte Originalwerte mit Quellverweis (Punkt 5 unten).

## Auftrag für diesen Durchlauf

Setze den verbleibenden Umfang von Phase 5 fort, in dieser Reihenfolge (jeder Schritt mit Tests, Realdatenaudit,
Doku-Nachführung, wie in `CLAUDE.md` „Ablauf einer Phase“ beschrieben). Miss vor jedem Schritt selbst nach, welches
Modul wie viele Objekte **allein** freischaltet (`SimTool audit-coverage .local/phase2-import-a`) — verlass dich nicht
auf die obige Liste, sie ist bereits eine Momentaufnahme:

1. **`TransportContain`** (2 Objekte allein, kleiner Folgeauftrag auf der bestehenden `IContainerModule`-
   Infrastruktur): `ThingTemplate::getTransportSlotCount`/`TransportSlotCount` auf `ObjectTemplate` ergänzen (wie
   `VisionRange`), gewichtete Kapazitätsrechnung statt einfacher Zählung, `HealthRegen`/`ExitDelay` falls sinnvoll
   eigenständig umsetzbar. `InitialPayload`, Feuern aus dem Container, Eroberungshandling bleiben wie bei
   `GarrisonContain` zurückgestellt.
2. **`CreateCrateDie`**, sobald ein `Crate`-Katalog und eine (und sei es nur rudimentäre, dokumentierte) Beziehungs-
   /beziehungsweise Besitzer-Prüfung existieren (siehe `StealthDetectorUpdate`/`GarrisonContain`s „gleicher Besitzer =
   Verbündeter"-Präzedenzfall).
3. **Spezialfähigkeiten mit echter `ObjectCreationList`-Ausführung**: `ObjectCreationList` ist noch keine eigene
   Definitionsart (bleibt `Compatibility`) — das ist eine Voraussetzung, kein Teil dieses Punkts; typisiere sie zuerst.
4. **Teams/Diplomatie als Datenmodell**, falls für den Referenzmatch (Punkt 5) nötig — sonst zurückstellen und im
   headless Referenzmatch bei „letzter Überlebender ohne Teams" bleiben (dokumentierte Vereinfachung).
5. **Ein headless Referenzmatch aller Fraktionen** (synthetische Karte im Speicher, scriptlos) von Start bis Sieg,
   als Replay aufgezeichnet, ≥ 216.000-Tick-fähig, zwei frische Läufe plus Snapshotfortsetzung hashgleich.
6. **Golden-Master-Szenarien** für zentrale Kampf- und Wirtschaftsabläufe (Schadenskette mit Rüstung, Produktionszeit,
   Supply-Einnahmen, Erfahrung) gegen bekannte Originalwerte, mit unabhängiger Nachrechnung und Quellverweis.
7. **Doku**: `docs/GAME-RULES.md`, `SIMULATION-CORE.md`, `TYPED-DEFINITIONS.md`, `PHASE3-COMPATIBILITY.md`, README,
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
