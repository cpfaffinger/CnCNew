# N+1-Handoff: Phase 5 – Fortsetzung (Spielregeln, Kampf, Wirtschaft und Produktion)

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`.

## Warum ein Fortsetzungsauftrag statt Phase 6

Über mehrere Durchläufe wurden Block 0 (0.1–0.3), ein erster funktionierender Kern des Hauptumfangs (Wirtschaft,
Sciences, Upgrades, direkter Kampf, einfache Bewegung, Produktion, Sieg/Niederlage) sowie sechs weitere Blockermodule
(Schadenszustände/`TransitionDamageFX`, `FlammableUpdate`, `SlowDeathBehavior`, `FireWeaponWhenDeadBehavior`,
`StealthDetectorUpdate`, `PoisonedBehavior`) umgesetzt, getestet und dokumentiert (Commits siehe unten). Der volle
Umfang von `docs/GAME-RULES.md`s Zielbild – Teams/Diplomatie, Supply/Garrison/Transport, Reparatur/Verkauf,
Radarwirkung, Spezialfähigkeiten, volle Rüstungs-/Waffen-Bestauswahl, ein headless Referenzmatch aller Fraktionen und
Golden-Master-Szenarien – ist **nicht** erreicht. Das war von Anfang an als möglich benannt: „Falls Phase 5 nicht
vollständig ist, erzeuge stattdessen einen präzisen Fortsetzungsprompt für Phase 5.“ Dieses Dokument ist dieser
Fortsetzungsprompt.

## Was bereits erledigt ist (mit Zahlen, neu messen vor dem Weiterarbeiten)

Lies zuerst `docs/GAME-RULES.md` vollständig, dazu die aktualisierten Abschnitte in `docs/SIMULATION-CORE.md` (§5, §6,
§11, §12), `docs/TYPED-DEFINITIONS.md` (die Block-0.1-Abschnitte am Ende) und `docs/PHASE3-COMPATIBILITY.md`.

Commits seit dem letzten Handoff (in dieser Reihenfolge, `git log`), pro Modul jeweils drei: Datenschema+Baseline,
Simulationsimplementierung, Tests:

1. Schadenszustände (`ActiveBody.CalcDamageState`) und `TransitionDamageFX` (`c99fc29`, `93d4954`, `6604046`).
2. `FlammableUpdate` (`0e8f63d`, `ffed6a0`, `66cc0de`) — führte den generischen `IDamageModule`-Hook ein
   (`OnDamage`/`OnBodyDamageStateChange`).
3. `SlowDeathBehavior` (`e0d20f7`, `cdd74fa`, `63e03c0`) — gewichtete Geschwister-Lotterie beim Tod.
4. `FireWeaponWhenDeadBehavior` (`289ad03`, `8009713`, `f661af4`).
5. `StealthDetectorUpdate` (`5c1c59f`, `35a4985`, `1d0159b`) — führte `ObjectTemplate.VisionRange` und
   `StealthUpdate.MarkDetected`/den `DETECTED`-Ablaufmechanismus ein.
6. `PoisonedBehavior` (`085ab72`, `2551b0a`, `e0fd3f3`) — führte `IDamageModule.OnHealing` ein und schloss eine
   bestehende Lücke (`OnBodyDamageStateChange` fehlte bisher im Heilungspfad).

**Wichtige Neupriorisierung gegenüber dem vorigen Handoff:** Die ursprüngliche Reihenfolge „nach Häufigkeit“
(`CreateCrateDie`, `PoisonedBehavior` nach den ersten drei) wurde durch eine frische `audit-coverage`-Messung ersetzt.
`CreateCrateDie` und `PoisonedBehavior` schalten beide **0 Objekte allein** frei (jedes Vorkommen hängt zusätzlich an
einem anderen, noch fehlenden Modul) — `PoisonedBehavior` wurde trotzdem umgesetzt (voll eigenständig, kein neues
Subsystem nötig), `CreateCrateDie` bewusst zurückgestellt (bräuchte einen neuen `Crate`-Katalog **und** eine
Beziehungs-/Diplomatieprüfung, für weiterhin 0 sofortigen Nutzen). Stattdessen wurden `FireWeaponWhenDeadBehavior`
(3 allein) und `StealthDetectorUpdate` (9 allein) vorgezogen. Miss vor jedem weiteren Modul selbst nach, was es
**allein** freischaltet (`SimTool audit-coverage`) — die Häufigkeit einer Modulverwendung ist kein guter Indikator für
den Freischaltungsgewinn.

**Ausgangsstand zum Reproduzieren** (29. September 2026, neu messen – nicht diesen Zahlen vertrauen):

- `dotnet build CnCNew.slnx`: 0 Fehler, 10 bekannte NU1901/NU1903-Warnungen.
- Alle acht Testprogramme grün, einschließlich `CnCNew.Simulation.Tests` mit `--content-dir .local/phase2-import-a`
  und `-c Release -- stress` (216.000 Ticks, identische Hashes über drei frische Läufe und Snapshotfortsetzung).
- Modulkatalog: 205 Typen, **33 implementiert** (12 aus Phase 4, 21 aus Phase 5).
- Kompatibilitätsbasislinie: **766 Einträge**, `compat-check` besteht.
- Vollinventar: 228 geladene Dateien, 65.875 Blöcke, 12.574 Definitionen, 16.380 Module, 333.061 Schlüssel,
  15.724/17.270 Referenzen aufgelöst (1.546 sichtbare Fallbacks), Snapshot 143.781.645 Byte, SHA-256
  `522CC08D0B4001D5B1398CAAA344849504509439E45435C2B014C347DA0E2DCC`.
- Retail-Basisdaten (`--maps exclude`): 2.115 Objekte, SHA-256
  `BFFA27023F6F8FCB75FCEDCABB60A88DE11135C1750D9D2E686057DFFE41F9E9`.
- Instanziierbarkeit: **694 von 2.115 Objekten (32,8 %)**, gegenüber 26/2.113 am Ende von Phase 4 und 623/2.115 vor
  diesem Block.
- Stresstest (`SimTool stress`, mit allen 33 Modulen): 216.000 Ticks, Endhash `C75FD6C190386265`, Kettenhash
  `B9E345E0584BED48`, drei frische Läufe und Snapshotfortsetzung identisch, alle Tabellen kehren auf null zurück.
- `CommandTypes.Version = 3` (14 Befehlstypen, unverändert seit dem letzten Handoff).

**Verbleibende Blocker nach Objekten, die ausschließlich noch an ihnen hängen** (frisch gemessen,
`SimTool audit-coverage`): `GarrisonContain` 167, `PhysicsBehavior` 119, `ToppleUpdate` 66, `SquishCollide` 2,
`TransportContain` 2, `CreateCrateDie`/`PoisonedBehavior` (jetzt erledigt)/`SpecialAbility`/`CommandButtonHuntUpdate` je
0. `GarrisonContain`/`TransportContain` brauchen ein komplettes Containment-Subsystem (Kapazität, Ein-/Aussteigen,
Garnisonspunkte, Ausgangs-Türen) — keine reine Modul-Portierung, sondern eine neue Teilarchitektur, vergleichbar im
Umfang mit dem Player/Faction/Science-Block. `PhysicsBehavior`/`SquishCollide` brauchen Terrain-/Kollisionsphysik
(3D-Gravitation, Bodenhöhe, Geometrieüberlappung) — das ist ausdrücklich Phase 6 (`ModuleCatalog.PlannedTarget`
klassifiziert `PhysicsBehavior` bereits so). `ToppleUpdate` ist neu sichtbar geworden (durch die anderen Module nicht
mehr verdeckt) und noch nicht untersucht.

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
- Block 0.1 (Weapon/Armor/Upgrade/Science/Locomotor/Faction), 0.2 (`StealthUpdate`/`AutoHealBehavior`) und 0.3
  (Furchtsignal) vollständig, unverändert seit dem letzten Handoff.

**Nicht erledigt / bewusst zurückgestellt** (siehe `docs/GAME-RULES.md` §10 und `docs/IMPLEMENTATION-PLAN.md` §5):

- Block 0.4 (RNG-Abgleich mit kompiliertem Original) — weiterhin kein Compiler verfügbar, bleibt dokumentierte Schuld.
- Teams/Diplomatie/Allianzen als eigenes Datenmodell (Sieg bleibt „letzter Überlebender" ohne Teams;
  `StealthDetectorUpdate` behilft sich mit „gleicher Besitzer = Verbündeter").
- `CommandSet`/`CommandButton` als echtes Datenmodell (bisher nur `CommandSetUpgrade`s Zustandsmaschine, keine Wirkung).
- Spezialfähigkeiten mit echter `ObjectCreationList`-Ausführung (bisher nur Event-Anforderung wie `CreateObjectDie`).
- Flächenschaden, Projektil-Flugzeit, `WeaponBonus`-Multiplikatoren.
- Volle veteranenbasierte `ArmorSet`/`WeaponSet`-Bestauswahl (`SparseMatchFinder`).
- Erfahrungs-/Veteranenvergabe aus Kämpfen; Rang-/`SciencePurchasePoints`-Vergabe aus Erfahrung.
- Energie-Verlangsamungsfaktor bei der Produktion (`ProductionUpdate::calcTimeToBuild`).
- Containment-Subsystem (`GarrisonContain`, `TransportContain`, sowie die ganze `CrateCollide`-Familie),
  Supply (`SupplyCenter*`, `SupplyTruckAIUpdate`, `SupplyWarehouse*`), Bau (`DozerAIUpdate`, `BuildAssistant`),
  Reparatur, Verkauf, Radarwirkung.
- `CreateCrateDie` (bräuchte einen neuen `Crate`-Katalog und eine Beziehungsprüfung, siehe oben).
- `PhysicsBehavior`/`SquishCollide` (Terrain-/Kollisionsphysik, ausdrücklich Phase 6).
- `ToppleUpdate` (66 Objekte allein blockiert, noch nicht untersucht — Kandidat für den nächsten Durchlauf).
- Ein headless Referenzmatch aller Fraktionen vom Start bis zum Sieg (Punkt 5 unten).
- Golden-Master-Szenarien gegen bekannte Originalwerte mit Quellverweis (Punkt 6 unten).

## Auftrag für diesen Durchlauf

Setze den verbleibenden Umfang von Phase 5 fort, in dieser Reihenfolge (jeder Schritt mit Tests, Realdatenaudit,
Doku-Nachführung, wie in `CLAUDE.md` „Ablauf einer Phase“ beschrieben). Miss vor jedem Schritt selbst nach, welches
Modul wie viele Objekte **allein** freischaltet (`SimTool audit-coverage .local/phase2-import-a`) — verlass dich nicht
auf die obige Liste, sie ist bereits eine Momentaufnahme:

1. **`ToppleUpdate`** untersuchen: ist es (wie die meisten Blockermodule bisher) eigenständig umsetzbar, oder hängt es
   an Terrain-/Physikinfrastruktur? Falls eigenständig: umsetzen, testen, Baseline neu erzeugen, Instanziierungsquote
   messen.
2. **Containment-Subsystem** (`GarrisonContain`/`TransportContain`, 167+2 Objekte allein): das ist kein einzelnes
   Blockermodul, sondern eine neue Teilarchitektur (Kapazität, Enthalten-Liste, Ein-/Ausstiegsbefehle, Heilung
   enthaltener Objekte, Auswurf bei `REALLYDAMAGED` über den bereits vorhandenen `OnBodyDamageStateChange`-Hook).
   Plane sie wie einen eigenen Unterabschnitt (vergleichbar mit dem Player/Faction/Science-Block): Datenschema,
   Entity-Erweiterung, Befehle, Tests, Doku — bevor du mit der Modul-Implementierung beginnst.
3. **`CreateCrateDie`**, sobald ein `Crate`-Katalog und eine (und sei es nur rudimentäre, dokumentierte) Beziehungs-
   /beziehungsweise Besitzer-Prüfung existieren (siehe `StealthDetectorUpdate`s „gleicher Besitzer = Verbündeter"-
   Präzedenzfall).
4. **Spezialfähigkeiten mit echter `ObjectCreationList`-Ausführung**: `ObjectCreationList` ist noch keine eigene
   Definitionsart (bleibt `Compatibility`) — das ist eine Voraussetzung, kein Teil dieses Punkts; typisiere sie zuerst.
5. **Teams/Diplomatie als Datenmodell**, falls für den Referenzmatch (Punkt 6) nötig — sonst zurückstellen und im
   headless Referenzmatch bei „letzter Überlebender ohne Teams" bleiben (dokumentierte Vereinfachung).
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
- Neue Befehle erhöhen `CommandTypes.Version` (aktuell 3) und bekommen eigene Codes; bestehende ändern sich nie.
- Nur lokal arbeiten, kein Remote, nichts pushen; mehrere fachlich geschlossene Commits; Working Tree am Ende sauber.

## Abschlussbericht

Wie bei den vorherigen Durchläufen: Zahlen (mit Commit-Hashes), implementierte Regeln/Module, Determinismus-, Test-,
Stress- und Replayergebnisse, Modulabdeckung und Instanziierungsquote vorher/nachher mit Blockerliste,
Golden-Master-Herleitungen (falls erreicht), bekannte Grenzen und technische Schulden, `git status` und Bestätigung
„kein Remote, nichts gepusht“.
