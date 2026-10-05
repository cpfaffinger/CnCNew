N+1-Handoff (Phase 18): Messbasis – Referenzmanifest, Determinismus über Rechnergrenzen, Leistungsprofil, geprüftes Tickraten-Inventar

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`. Lies zuerst `CLAUDE.md`, `README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md`, **`docs/MULTIPLAYER-PLAN.md`** (Scope-Änderung vom 5. Oktober 2026, Zielarchitektur, Register der Engstellen, Regeln für alle Phasen des Programms), `docs/TICKRATE-AUDIT.md` und `docs/SIMULATION-CORE.md` (Zustands-Hash, Snapshot, Replay, Referenzwerte).
Voraussetzung: Phase 17 ist abgeschlossen **oder** der Projektinhaber hat ausdrücklich entschieden, das Programm vorzuziehen. In beiden Fällen gilt: während der Phasen 18–23 ändert niemand sonst das Verhalten der Simulation auf `main` – jeder Vergleich „vorher/nachher“ in diesem Programm hängt daran.

Diese Phase ist die erste von zwölf (18–29). Sie **baut nichts Neues**, sondern stellt fest, ob das Fundament trägt, und schafft die Messlatten, an denen alle folgenden Phasen gemessen werden. Ihr Ergebnis ist eine Freigabe oder eine begründete Ablehnung.

## Ehrlicher Status (Ausgangslage, 5. Oktober 2026; alles neu messen)

**Gemessene Referenzwerte am Ende der ersten Phase-17-Sitzung (5. Oktober 2026, Stand `2a66661` plus Doku; ein Rechner, Release):** sauberer Build 0 Warnungen; `CommandTypes.Version` 14, Replay 11, `WorldSnapshot` 9, `MissionSnapshot` 2; Retail-Referenzmatch Sieger FactionGLA bei Tick 14.490, Endhash `23B4B25DF62765D8`, Kette `DE5849AF9F8493C4`; Stresstest `56882F763EABEF67` / `B8E663493F2C73D5` (61 µs je Tick ruhig); 16 KI-Läufe 11 entschieden, Endhashes in `docs/SIMULATION-CORE.md` §33.6; compat-check 751; Zielfahrer 24/24 und 24/24, Audit 24/24 deterministisch und identisch nach Speichern/Laden; `mission-run` 3.000 Ticks `MD_USA05` `1CB28E6AA923BDEF` (Kette `0816C3438CB7F689`), `MD_GLA05` `317B464E8BA98377` (Kette `6823506872EBFF9E`). Phase 17 ist **nicht abgeschlossen** (`docs/HANDOFF-PHASE17.md`, Abschnitt „Rest der Phase 17“): die dort offenen Punkte 2 bis 5 ändern das Verhalten der Simulation.

1. **Determinismus ist nur auf einem Rechner belegt.** „Zwei unabhängige Läufe byteidentisch“ wurde immer auf derselben Maschine, demselben Betriebssystem und derselben .NET-Version gemessen. Lockstep verlangt bitgleiche Zustände auf verschiedenen CPUs und .NET-Patchständen. Das ist nie geprüft worden.
2. **Die Referenzwerte stehen nur in der Dokumentation.** Die Tests enthalten keine Referenz-Zustandshashes; Referenzmatch, Stresstest und KI-Läufe sind 137 Hex-Werte in 26 Doku-Dateien und `CLAUDE.md` (`docs/TICKRATE-AUDIT.md` §7). Ein „hash-neutraler“ Umbau ist damit nur von Hand prüfbar, und drei der nächsten fünf Phasen sind genau solche Umbauten.
3. **Ein lesender Suchlauf hat konkrete Risiken gefunden** (unten, „Befunde“). Keiner ist durch einen Test bestätigt oder widerlegt.
4. **Leistung in echten Matches ist unvermessen und nach einer ersten Probe kritisch.** Eine nicht ruhige Messung vom 5. Oktober 2026 (`SimTool ai-match … --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --max-ticks 18000`, parallel liefen andere Prozesse): Aufzeichnung der 18.000 Ticks in 123,1 s (einschließlich Datenkompilierung und acht KIs), anschließende Wiedergabe ohne KI 163,0 s (Endhash `72CF0F846E0D9AA6`, Kette `5E446FE63C5BEF46`, zweiter Lauf identisch). Das sind **rund 7 bis 9 ms je Tick** bei am Ende nur 218 Objekten der Spieler (0 bis 53 je Spieler) auf einer Karte mit 486 × 469 Zellen. Zum Vergleich: der synthetische Stresstest liegt bei 70–80 µs je Tick mit 13.220 Objekten. Die Kosten hängen also nicht an der Objektzahl. Bei 60 Hz wären das rund die Hälfte eines Kerns nur für die Simulation und ein Aufholtempo von etwa dem Doppelten der Echtzeit.
5. **Das Tickraten-Inventar ist ungeprüft** (`docs/TICKRATE-AUDIT.md`).

### Befunde des Suchlaufs (zu prüfen; Zeilennummern Stand `2a66661`)

| Nr. | Befund | Stelle | Wirkung, falls er zutrifft |
|---|---|---|---|
| D1 | Knochentabelle der Modelle wird in `float` gerechnet (`Matrix4x4`, `MathF.Atan2`), dann auf 1/16 Einheit und 1/1024 rad gerundet | `src/CnCNew.Game/Data/ModelGeometryLoader.cs:23–105`, `W3dSkeleton.cs` | letztes Bit von `Atan2` und FMA-Nutzung können je CPU/OS abweichen; die Tabelle steckt im Fingerabdruck – zwei Rechner würden sich gegenseitig *ablehnen* |
| D2 | Kartenkoordinaten und -winkel gehen über `(decimal)float` nach `Fixed` | `SageMapDecoder.cs:582–587`, `TerrainMap.cs:142`, `ScriptProgram.cs:291/331`, `SkirmishMatchSession.cs:224` | Routine der Laufzeit; Stabilität über .NET-Versionen unbelegt |
| D3 | `_entities` ist ein `Dictionary<uint, Entity>`; Schleifen mit Nebenwirkung zählen es ungeordnet auf; ein geladener Snapshot fügt in ID-Reihenfolge ein | `SimulationWorld.cs:54`, z. B. `SimulationWorld.Collision.cs:116`, `CrateServices.cs:60/81`, `BattlePlan.cs:139/173` | eine aus Snapshot wiederhergestellte Welt kann anders aufzählen als die durchlaufende (Wiedereinstieg, Phase 27); hängt an BCL-Interna |
| D4 | Kulturabhängiges im gehashten Skriptzustand | `ScriptEngine.TeamAbilities.cs:29` (`SortedDictionary<string,int>` ohne Vergleicher), `ScriptEngine.Phase13.cs:50` (Zahl in `Diagnose`), `ScriptEngine.Actions.cs:389` | anderer Hash auf Rechnern mit anderer Kultur |
| D5 | Fingerabdrücke decken nicht alles | Gelände-Fingerabdruck ohne Wegpunkt-`Id`/`NextId` und ohne Kartenobjekte (`TerrainMap.cs:277–311`); Definitions-Fingerabdruck ohne Codeversion, Karte, KI-Startwert | zwei Rechner mit verschiedener Karte oder verschiedenem Code hielten sich für kompatibel |
| D6 | `SourceRules` prüft nur `src/CnCNew.Simulation` und kennt weder `Math.Atan2/Tan/Asin/Acos/Round/Floor` noch `var x = 1.5`, `Vector128`, Kulturvergleicher, `string.GetHashCode` | `tests/CnCNew.Simulation.Tests/Tests.State.cs:347–400` | künftige Verstöße blieben unbemerkt; der Code, der die Simulation speist, ist gar nicht erfasst |
| D7 | Ein Sortiervergleich ist nicht total | `TerrainMap.cs:144` (Name, X, Y; nicht Z, Id) | doppelte Wegpunkte sortieren instabil |
| D8 | Eine Zustandsänderung ohne `MarkDirty` fällt erst bei `VerifyIncrementalHash` auf | `SimulationWorld.cs:1706–1763` | stiller Desync zwischen Rechnern, der im Hash nicht sichtbar ist |
| D9 | JSON-Escaping im Definitions-Fingerabdruck kann sich zwischen .NET-Versionen unterscheiden | `DefinitionSnapshotWriter.cs:38–118` | gegenseitige Ablehnung |

Sauber nach dem Suchlauf (trotzdem durch den Matrixlauf bestätigen): `Fixed` (Int128-Zwischenwerte, feste Rundung, `checked`), `FixedTrig` (fest einkodierte Ganzzahl-Konstanten, CORDIC, Polynome), `SageRandom`, der Definitions-Compiler (`decimal`, invariante Kultur, ordinale Ladereihenfolge), Geländehöhen (Byte × 0,625), Befehlsnutzlasten (Rohwerte, binär).

## Auftrag (in dieser Reihenfolge)

1. **Ausgangszustand reproduzieren.** `git status`, sauberer Build (0 Warnungen), alle Testprogramme, die Audits, alle Referenzwerte aus `CLAUDE.md` neu messen. Abweichungen von der Doku zuerst klären.
2. **Referenzmanifest (die neue Messlatte).**
   - `SimTool reference-manifest write <content> <datei>` und `… check <content> <datei>`: führt eine feste Liste von Läufen aus und schreibt bzw. vergleicht je Lauf Parameter, Tickrate, Endtick, Sieger, Endhash und Kettenhash. Deterministische Datei (sortiert, keine Pfade, keine Zeitstempel).
   - Inhalt mindestens: Referenzmatch, Stresstest, die 16 KI-Läufe der Tabelle in `docs/SIMULATION-CORE.md` §19, die drei Alpine-Assault-Paarungen, drei 8-Spieler-Läufe auf verschiedenen Karten, `skirmish-sweep` als Zusammenfassung, je Kampagne zwei Karten `mission-run` mit Hash vor und nach Speichern/Laden. Stufen `--set quick` (Minuten) und `--set full`.
   - Dazu ein **Eingangsmanifest**: Definitions-Fingerabdruck, Hash der Knochentabelle, Gelände-Fingerabdruck und Dateihash aller 150 Karten, Hash der gebauten Kataloge. Damit lässt sich bei einer Abweichung zwischen zwei Rechnern trennen, ob schon die Eingänge oder erst die Rechnung abweicht.
   - Ablage unter `baselines/` (enthält nur Hashes und Zahlen, keine Originaldaten). Jede Änderung der Datei ist Review-Gegenstand wie `baselines/compatibility.json`. Die Doku nennt weiter Werte, maßgeblich ist das Manifest.
3. **Determinismus-Matrix.** Das Manifest (`check`, Stufe `full`) läuft unter jeder Zeile; das Ergebnis ist eine Tabelle „Zeile × identisch ja/nein × erste Abweichung“:
   - derselbe Rechner: Release und Debug; `DOTNET_TieredCompilation=0`; `DOTNET_TieredPGO=0`; `DOTNET_ReadyToRun=0`; `DOTNET_EnableHWIntrinsic=0`; `DOTNET_EnableAVX2=0`; Server-GC; andere Betriebssystem-Kultur (z. B. `de-DE`, `tr-TR`, `ar-SA` über `CultureInfo.DefaultThreadCurrentCulture` in einem Testschalter);
   - anderes Betriebssystem auf demselben Rechner: die Headless-Werkzeuge unter Linux (WSL oder Container) gegen denselben Inhalt (ICU statt NLS, andere C-Laufzeit);
   - eine andere .NET-Patchversion, falls installierbar;
   - **ein zweiter physischer Rechner mit anderer CPU** (anderer Hersteller oder andere Generation). Das braucht den Menschen: Anleitung schreiben (`docs/DETERMINISM.md`), wie Werkzeug und `.local/phase2-import-a` dorthin kommen (lokal kopieren, nie committen). Findet der Lauf nicht statt, steht das im Bericht als offen – nicht als bestanden.
4. **Befunde D1–D9 abarbeiten.** Jeden zuerst mit einem Test bestätigen oder widerlegen, dann beheben:
   - D1: die Knochentabelle so berechnen, dass das Ergebnis nicht von CPU, FMA oder C-Laufzeit abhängt (z. B. Kette in `decimal` oder exakter Ganzzahlrechnung, Winkel über eine eigene Ganzzahl-Routine); goldener Tabellenhash im Eingangsmanifest.
   - D2: Float-Bits ohne Laufzeitroutine nach `Fixed` wandeln. Bevorzugt hash-neutral (eigene Ganzzahl-Nachbildung der bisherigen Sieben-Stellen-Rundung, geprüft gegen alle Float-Werte aller 150 Karten); sonst als benannte, vermessene Hash-Änderung in einem eigenen Commit.
   - D3: belegen, ob irgendeine ungeordnete Aufzählung das Ergebnis beeinflusst (Test: Welt nach jedem Prüfpunkt aus Snapshot laden und parallel weiterlaufen lassen, über das volle Manifest); wo ja, ordnen. Ändert das Hashes, ist es eine eigene, vermessene Änderung.
   - D4, D7: beheben (ordinale Vergleicher, invariante Formatierung, totale Ordnung).
   - D5: Karten über den Dateihash aus dem Content-Manifest identifizieren; Gelände-Fingerabdruck vervollständigen.
   - D6: `SourceRules` erweitern und einen zweiten Regelsatz für alles einführen, was die Simulation speist (`CnCNew.Data`, die Kartendekodierung in `CnCNew.Formats`, `ModelGeometryLoader`): dort ist `float` als Dateiformat erlaubt, aber jede Wandlung in Simulationseingänge läuft über genau eine geprüfte Routine.
   - D8: ein Prüfmodus des Manifests, der den inkrementellen Hash in jedem Tick gegen den vollen prüft (kurze Läufe), deckt fehlende `MarkDirty` auf.
   - D9: Fingerabdruck über eine eigene, festgelegte Kodierung statt über das Escaping der Laufzeit, oder goldener Wert über die Matrix.
5. **Leistungsprofil (ruhige Maschine, nichts parallel).**
   - `SimTool perf <content> --map … --players N --ticks T`: je Tick die Zeit, daraus Mittel, 50/95/99-%-Quantil und Maximum; Aufteilung nach Tickphase (Befehle, Zeitgeber, Updates, Kollision, Sicht, Ereignisse, Hash) und KI; Allokationen und GC-Läufe; Objektzahl über die Zeit.
   - Läufe: 2, 4 und 8 Spieler auf mindestens drei Karten verschiedener Größe, je 54.000 Ticks (30 Spielminuten bei 30 Hz); dazu Wiedergabe ohne KI (Aufholtempo als Vielfaches der Echtzeit), Snapshot schreiben/lesen samt Größe am Ende, zwei große Missionen.
   - Spuren aus dem Suchlauf, **nicht belegt**: der Sicht-Hash wird alle drei Ticks über jede Zelle für jeden Spieler neu berechnet (`VisionMap.cs:132–149`); `Describe` hasht Vorlagen-, Modul- und Tag-Namen zeichenweise; `MissionSession.StateHash()` beschreibt den Skriptzustand jedes Mal vollständig.
   - Ergebnis: eine Tabelle und eine Hochrechnung auf 60 Hz. Sie ist die Eingabe für Phase 19; hier wird nichts optimiert.
6. **Tickraten-Inventar prüfen.** `docs/TICKRATE-AUDIT.md` Stelle für Stelle nachvollziehen, mit den Suchmustern ergänzen, jede Stelle einer Klasse zuordnen (Originalkonstante in Bildern / Zeitwert / Ablaufrhythmus / rohe Daten je Bild / nicht invariante Integration / Präsentationsuhr / Format / Test) und das Dokument als geprüfte Fassung neu schreiben.
7. **Freigabe.** `docs/MULTIPLAYER-PLAN.md` §4 (Register) mit den gemessenen Ergebnissen fortschreiben und eine klare Aussage treffen: Programm fortsetzen wie geplant / mit geänderter Reihenfolge / nicht fortsetzen, jeweils mit Begründung. Danach die „Ausgangslage“ in `docs/HANDOFF-PHASE19.md` und `docs/HANDOFF-PHASE20.md` aktualisieren.
8. **Doku:** `docs/DETERMINISM.md` (neu: Matrix, Vorgehen, Ergebnisse), `docs/TICKRATE-AUDIT.md`, `docs/SIMULATION-CORE.md`, `docs/MULTIPLAYER-PLAN.md`, README, ROADMAP, IMPLEMENTATION-PLAN, `CLAUDE.md` (neue Befehle, Verweis auf das Manifest).

## Nicht verhandelbare Regeln

`CLAUDE.md` und `docs/MULTIPLAYER-PLAN.md` §6. Für diese Phase besonders: keine Verhaltensänderung der Simulation außer den Determinismus-Korrekturen; jede Korrektur, die einen Referenzwert ändert, steht allein in einem Commit, mit Vorher/Nachher-Werten und Begründung.

## Abnahme

Das Referenzmanifest besteht zweimal hintereinander und unter jeder ausgeführten Zeile der Matrix; jede nicht ausgeführte Zeile ist als offen benannt. D1–D9 sind je bestätigt und behoben oder widerlegt, mit Test. Das Leistungsprofil und das geprüfte Inventar liegen vor. Der Abschlussbericht nennt Zahlen mit Commit-Hashes, die Freigabeaussage und ausdrücklich, was nicht belegt ist.
