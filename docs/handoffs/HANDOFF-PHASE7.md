# N+1-Handoff: Phase 6 – Welt, Terrain, Bewegung, Sicht und Skirmish-KI

Du arbeitest autonom an diesem Repository (`<repo>` = Wurzel des Arbeitsverzeichnisses). Lies zuerst `CLAUDE.md`, `README.md`,
`docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§5 und §6), `docs/GAME-RULES.md` (besonders §4, §6c, §6e–§6h,
§10) und `docs/SIMULATION-CORE.md`. `docs/handoffs/HANDOFF-PHASE5.md` und `docs/handoffs/HANDOFF-PHASE6.md` bleiben als Verlauf erhalten; ihre Zahlen sind
veraltet.

## Ehrlicher Status von Phase 5 (Ausgangslage)

Phase 5 ist abgeschlossen, aber mit bewussten Grenzen (vollständig: `docs/GAME-RULES.md` §10, `docs/IMPLEMENTATION-PLAN.md` §5):

- **Block 0.4 offen:** Die RNG-Folge wurde nie gegen ein kompiliertes Original verglichen (kein Compiler, keine Originalläufe). Golden
  Masters sind aus dem Quelltext abgeleitet. Steht eine Möglichkeit zur Verfügung, ein Original zu kompilieren oder Originalläufe zu
  erfassen, hat das Vorrang; sonst nicht raten.
- **Kein Bauvorgang:** Dozer und Arbeiter bewegen und liefern, bauen aber nicht. Der Referenzmatch setzt Basen aus Startobjekten plus
  `SpawnSupplyInfrastructure` zusammen. Kein Verkauf, keine Bauplatzprüfung, kein Rallypunkt.
- **Kein Terrain, keine Kollision, keine Pfadsuche, keine Projektil-Flugzeit**, keine drehratenbegrenzte Ausrichtung.
- Wirkung von `CommandSet`/`CommandButton` und Radar fehlt. `ChinookAIUpdate` fehlt (Amerika hat im Referenzmatch keine
  Versorgungswirtschaft). Es gibt keinen `UseSpecialPowerCommand`-Generator im Referenzmatch.
- Veteranenbasierte `ArmorSet`/`WeaponSet`-Auswahl und Science Purchase Points aus Erfahrung sind nicht modelliert;
  `FireWeaponWhenDeadBehavior` hat keinen Flächenschaden.

## Reproduktionsbasis (Stand Ende Phase 5, 29. September 2026; vor dem Weiterarbeiten neu messen)

- Vollinventar mit allen Kartenoverlays: 228 INI-Dateien geladen (28 ausgeschlossen), 65.875 Blöcke, 12.574 Definitionen, 16.380 Module,
  333.061 Schlüssel, 18.138/19.688 Referenzen aufgelöst (1.550 sichtbare Fallbacks), Snapshot 144.813.963 Byte, SHA-256
  `D98CE0154ADF721DC58E9FF1FE769ADA646A058CE7B9AA9A8F0AC99254E5AB67`.
- Retail-Basisdaten (`--maps exclude`): 12.550 Definitionen, 2.115 Objekte, 16.065 Module, Snapshot 144.203.312 Byte, SHA-256
  `9F5307957A55CD4683E8A79CBEB079320BE69400802DA38D2FFDDB200713E264`.
- Kompatibilitätsbasislinie 746 Einträge; Modulkatalog 205 Typen, 74 implementiert; 1.384 von 2.115 Objekten instanziierbar (65,4 %),
  731 blockiert; Modulverwendungen abgedeckt 15.364 von 19.553 (78,6 %); 80 Module der Kategorie „Phase 5“, 29 „Phase 6“, 22 „Phase 7“ offen.
- Befehlstabelle 19 Typen (`CommandTypes.Version = 7`), Snapshot Version 3, Replay-Format v4.
- Retail-Referenzmatch: 13 Fraktionen, 0 übersprungene Startobjekte, 707 Lieferungen (93.075), Sieger Spieler 12 bei Tick 26.880,
  Endhash `B6E0CDECA3168EFC`.
- Stresstest (216.000 Ticks): Endhash `D80B367CEEF6D72D`, Kettenhash `8E1FA50D21433123`, 914.394 Befehle.
- Asset-Runtimebestand 25.682 Dateien; IniTool audit 256/256 INI-Dateien, 66.625 Blöcke.

## Teil A – Welt und Terrain (Datenmodell zuerst)

Umfang laut `docs/IMPLEMENTATION-PLAN.md` §6: MAP-Daten zu spielbarer Welt: Heightfield, Texturlayer, Wasser, Bridges, Waypoints,
Triggerbereiche und Props. Erst die Kartenformate an den realen Daten (`.local/phase2-import-a`, Karten-Overlays der Vollinventarmessung
mit `--maps include`) und am Original (`references/`) lesen, dann als eigenes Datenmodell bauen.

1. **Kartenformat und Weltmodell:** Ladepfad über `CnCNew.Formats`/`CnCNew.Import`, Laufzeitzugriff nur über `CnCNew.Content`; die
   Simulation sieht nur ein ganzzahliges/Festkomma-Weltmodell aus `CnCNew.Data` (keine Gleitkommazahlen, `SourceRules` beachten).
2. **Footprints, Bodentypen, Kollision:** Grundlage für `PhysicsBehavior`-Kräfte, `ToppleUpdate` (76 Objekte, davon 66 nur dadurch
   blockiert), echtes `CrushDie`/`SquishCollide` (bisher inert, §6g), `HeightDieUpdate` (31/15), `OCLAdjustPositionToPassable` und
   `CREATE_AT_EDGE_*`-Positionen (bisher `CreationSkipped`).
3. **Pfadsuche und Bewegung:** deterministische Pfadsuche mit lokaler Vermeidung und Neuberechnung, Formationen, drehratenbegrenzte
   Ausrichtung, `Locomotor`-Sätze; die geradlinige Bewegung aus §4 wird ersetzt, nicht ergänzt. Zwei Läufe müssen byteidentisch sein.
4. **Projektil-Flugzeit:** `DumbProjectileBehavior`, `MissileAIUpdate` (44/29); Schaden greift dann beim Einschlag, nicht beim Schuss.

## Teil B – Sicht, Bauvorgang und Skirmish-KI

5. **Sichtweite, Shroud, Fog, Tarnung und Aufdeckung:** `STEALTHED`/`DETECTED` bekommen einen Verbraucher im Zielsystem (§6);
   Zielwahl nach Sicht.
6. **Bauvorgang:** Dozer/Arbeiter bauen (`DozerAIUpdate`, `WorkerAIUpdate`), Bauplatzprüfung, Konstruktion, Reparatur durch Dozer, Verkauf,
   Rallypunkt; danach den Ersatz `SpawnSupplyInfrastructure` im Referenzmatch entfernen.
7. **`ChinookAIUpdate`/`TransportAIUpdate`** (Amerika-Versorgung, 11/0 bzw. 24/13), `TunnelContain`, `HelixContain`.
8. **Skirmish-KI:** Build-Pläne, Wirtschaft, Angriff, Verteidigung, Fähigkeitsnutzung (`CommandButtonHuntUpdate` 79/18,
   `SpecialAbilityUpdate` 64/3, `UseSpecialPowerCommand`-Generator im Referenzmatch). Mehrere lange KI-gegen-KI-Läufe müssen
   deterministisch und ohne Deadlock enden.
9. **Command-Set- und Radarwirkung**, veteranenbasierte `ArmorSet`/`WeaponSet`-Auswahl, Rang und Science Purchase Points aus
   Erfahrung, Flächenschaden für `FireWeaponWhenDeadBehavior`.

Miss vor jedem Schritt mit `SimTool audit-coverage .local/phase2-import-a`, was ein Modul allein freischaltet. Häufigste Blocker am
Ende von Phase 5 (Objekte insgesamt / nur durch dieses Modul blockiert): `CommandButtonHuntUpdate` 79/18, `ToppleUpdate` 76/66,
`SpecialAbilityUpdate` 64/3, `JetSlowDeathBehavior` 59/0, `MissileAIUpdate` 44/29, `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/2,
`HeightDieUpdate` 31/15, `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25,
`TransportAIUpdate` 24/13, `BaikonurLaunchPower` 23/23, `SlavedUpdate` 20/7, `HelicopterSlowDeathBehavior` 18/0,
`PointDefenseLaserUpdate` 17/4.

**Abnahme von Phase 6** (`docs/IMPLEMENTATION-PLAN.md` §6): Die Vertical-Slice-Karte und ein repräsentativer Kartensatz laden
vollständig, Einheiten navigieren zuverlässig, mehrere lange KI-gegen-KI-Läufe enden deterministisch ohne Deadlock oder unerlaubte
Information. Bleiben Grenzen, benenne sie ehrlich in `IMPLEMENTATION-PLAN.md`.

## Nicht verhandelbare Regeln (unverändert, siehe `CLAUDE.md`)

- Unbekannte Schlüssel/Module/Enums/Referenzen fallen nie still auf Standardwerte zurück; jeder Fall ist explizit, gezählt,
  dokumentiert und gegen `baselines/compatibility.json` abgesichert.
- Determinismus: kanonische Sortierung, keine Wandzeit, keine `Dictionary`-/`HashSet`-Reihenfolgeabhängigkeit; zwei unabhängige
  Läufe byteidentisch. `CnCNew.Simulation` hängt nur von `CnCNew.Data` ab (`SourceRules`-Test); jedes `IUpdateModule` speichert
  `nextWake`.
- `TreatWarningsAsErrors=true`; nur die zehn bekannten NU1901/NU1903-Warnungen sind erklärt.
- Bestandszahlen immer neu messen, nie aus Docs oder diesem Handoff übernehmen.
- Fremder Code darf gelesen, nicht unbesehen kopiert werden; jeder portierte Algorithmus braucht einen Quellverweis (Datei, Funktion).
- Neue Befehle erhöhen `CommandTypes.Version` (aktuell 7) und bekommen eigene Codes; bestehende ändern sich nie. Änderungen am
  Snapshot- oder Replay-Format erhöhen deren Version (aktuell Snapshot 3, Replay 4); ältere werden mit klarer Meldung abgewiesen.
- Nur lokal arbeiten, kein Remote, nichts pushen; mehrere fachlich geschlossene Commits; Working Tree am Ende sauber;
  am Ende dieses Durchlaufs `docs/handoffs/HANDOFF-PHASE8.md` (oder einen neuen Fortsetzungsauftrag) schreiben und committen.

## Abschlussbericht

Zahlen (mit Commit-Hashes), implementierte Regeln/Module, Determinismus-, Test-, Stress- und Replayergebnisse, Modulabdeckung und
Instanziierungsquote vorher/nachher mit Blockerliste, Golden-Master-Herleitungen, bekannte Grenzen und technische Schulden,
`git status` und Bestätigung „kein Remote, nichts gepusht“.
