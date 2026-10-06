N+1-Handoff (Phase 20): Tickrate als Parameter der Welt – Simulation (ergebnisgleich bei 30 Hz)

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`. Lies zuerst `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, **`docs/TICKRATE-AUDIT.md`** (geprüfte Fassung aus Phase 18), `docs/SIMULATION-CORE.md` (Zeit und Einheiten, Kataloge, Snapshot, Replay), `docs/GAME-RULES.md` und den Abschlussbericht der Phase 19.
Voraussetzung: Phase 19 ist abgeschlossen; das Referenzmanifest besteht.

## Ziel in einem Satz

Die Simulation kennt keine fest einkodierte Tickrate mehr; die Rate ist ein Wert der Welt. **Bei 30 Hz ändert sich nichts** – kein Hash, kein Snapshot-Byte. Bei 60 und 120 Hz läuft die Simulation fehlerfrei durch, ohne dass ihr Verhalten dort schon bewertet wird (das ist Phase 23).

## Ehrlicher Status (Ausgangslage; Zahlen aus `docs/TICKRATE-AUDIT.md`, dort geprüft)

- `SimulationTiming.LogicFramesPerSecond = 30` ist eine `const`; 61 Verwendungen in 32 Dateien der Simulation, alle Umrechnungen sind statische Funktionen.
- Die Kataloge (`ObjectTemplate`, `WeaponCatalog`, `UpgradeCatalog`, `SpecialPowerCatalog`, `TurretInfo`, jede `Parse(configuration)` eines Moduls über `ModuleConfigReader.Ticks`) rechnen beim Bau in Ticks um. Die geparsten Daten sind also ratenabhängig, der Katalog wird aber einmal je Datenbestand gebaut.
- Rund 80 nackte Tickzahlen in der Simulation und rund 30 in `Simulation/Testing`; dazu ganzzahlige Teilungen der Rate (`30 / 4 = 7`).
- Daten, die als rohe Bildwerte verbraucht werden: Ersatzwerte von `ModuleConfigReader.Ticks`, Zug-, Dämpfungs- und Wurfwerte, `InstantSpeed`, `TurretInfo.DefaultRate`, die Bildzähler der Kartenskripte (`SET_TIMER`, `*_FOR_FRAMECOUNT`).
- `WorldSnapshot`, `MissionSnapshot`, Replay-Kopf und `ReplayRunner` schreiben die Rate und lehnen jede andere als 30 ab; `Tests.State.cs` prüft, dass 60 abgelehnt wird.

**Nachtrag aus Phase 18 (6. Oktober 2026):** `docs/TICKRATE-AUDIT.md` ist jetzt die geprüfte Fassung (Stand `e5046b7`): 1.959 gekoppelte Stellen, davon 660 in `CnCNew.Simulation` (A 39, B 267, C 81, D 43, E 138, F 12, G 15, H 65); 63 Verwendungen der Konstante in 32 Dateien, 159 nackte Tickzahlen, 15 Ersatzwerte von `ModuleConfigReader.Ticks`, 222 gespeicherte absolute Tickfelder in 56 Dateien (310 tickbezogene Felder insgesamt). Zehn Widersprüche zum Plan stehen in der Zusammenfassung des Inventars (W1–W10) und in `docs/MULTIPLAYER-PLAN.md` §4a; für diese Phase wichtig: W2 (Literale, die Reihenfolge bedeuten, nicht skalieren), W3 (13 `const`-Ausdrücke und 31 Handrechnungen mit der Konstante), W6 (Skripte: Auswertung je Tick, Phasenmodul `% (2*30+1)`), W8 (`SaveGame` schreibt keine Rate, `ReplayRunner.Run(replay, world, …)` prüft sie nicht). Messlatte ist `SimTool reference-manifest check` (`baselines/`), nicht mehr die Hashes der Doku.

**Nachtrag aus Phase 19 (6. Oktober 2026, Stand `16d337c`):** die Simulation ist ohne Ergebnisänderung schneller (`docs/PERFORMANCE.md` §6a): 8 Spieler 1,14 ms je Tick statt 4,98, `MD_GLA05` 3,4 statt 13,5 ms; das volle Referenzmanifest (`reference-manifest check --set full`) dauert rund 10 statt 27 Minuten. Ein weiterer `MarkDirty`-Fehler ist behoben (`SpecialPowerModuleBase.PauseCountdown`; ein Manifestlauf hat neue Hashes). Neu und für diese Phase zu beachten: Bewegung, Sicht und Zustands-Hash haben Zwischenspeicher mit exakter Invalidierung (`NavigationGrid` merkt sich Antworten bis zur nächsten Fußabdruckänderung, `VisionMap` behält Hash und Schreibausdehnungen, die Objektbeschreibung hat vorbereitete Modulrahmen) – keiner davon enthält Tickzahlen, aber jeder ist Zustand außerhalb des Snapshots und muss nach einer Änderung der Rate weiter dieselben Ergebnisse liefern; die Welt führt Besitzer-, Art- und Modultyp-Listen (`IWorldServices`), die Skripte und Module statt Weltdurchläufen benutzen; `Fixed` multipliziert, teilt und zieht Wurzeln ohne 128-Bit-Division (für Phase 22 wichtig: die Routinen sind auf Q47.16 zugeschnitten und haben Äquivalenztests gegen die alte Fassung). Nicht erreicht und offen für eine eigene Entscheidung: das Tickmaximum (94 ms durch gleichzeitige lange Wegsuchen) und zwei Vorschläge für andere Hash-Konstruktionen (`docs/PERF-VISION.md` §7.1, `docs/PERF-HASH.md` §6–§7).

## Entscheidungen, die feststehen (`docs/MULTIPLAYER-PLAN.md` §2)

1. **Unterstützte Raten sind 30 · 2ᵏ: 30, 60, 120.** Jede andere wird beim Erzeugen der Welt abgelehnt. Grund: Bildwerte des Originals lassen sich exakt skalieren (× 2, × 4), und multiplikative Faktoren je Bild brauchen später nur Quadratwurzeln (Phase 23).
2. **Zeiten aus den Daten bleiben auf dem 30-Hz-Raster des Originals.** Eine Dauer von *m* Millisekunden wird wie im Original auf ganze Bilder aufgerundet (`ceil(m · 30 / 1000)`) und dann mit `Rate / 30` multipliziert. Eine Waffe mit 50 ms Nachladezeit braucht damit bei jeder Rate 66,7 ms wie im Original, nicht 50 ms. So bleibt die Spielbalance bei jeder unterstützten Rate dieselbe, und die höhere Rate bringt, was sie bringen soll: feinere Bewegung und schnellere Befehlsannahme.
3. **Der Katalog wird für eine Rate gebaut** (`TemplateCatalog` und alles, was Ticks enthält, nimmt die Rate beim Bau). Kein Umrechnen im heißen Pfad. Die Rate geht in den Fingerabdruck des Katalogs ein.
4. **Konstanten des Originals in Bildern** (`SPAWN_UPDATE_RATE = 15` u. ä.) werden über eine einzige Hilfsfunktion ausgedrückt – „*n* Bilder des Originals“ = `n · Rate / 30` Ticks. Eigene Rhythmen (KI-Denken, Sichtauffrischung) ebenso. Kein nacktes Tickliteral bleibt in der Simulation.

## Auftrag (in dieser Reihenfolge)

1. **Ausgangszustand reproduzieren.** Build, Tests, `reference-manifest check --set full`, Leistungsprofil. Zusätzlich einen byteweisen Snapshot-Vergleich vorbereiten: für fünf Läufe des Manifests Snapshots an festen Ticks sichern (Scratchpad), um nach dem Umbau Bytegleichheit zu belegen.
2. **Zeitvertrag.** Ein unveränderlicher Wert (Vorschlag `SimulationRate`) in der Weltkonfiguration und ein Zeitdienst der Welt mit den bisherigen Umrechnungen plus „Bilder des Originals → Ticks“. Die statische Klasse `SimulationTiming` behält nur ratenunabhängige Funktionen (Grad → Bogenmaß, Verhältnisse); die Konstante `LogicFramesPerSecond` verschwindet aus der Simulation.
3. **Kataloge je Rate.** Jede Stelle, die beim Bau Ticks oder Werte je Tick erzeugt, erhält die Rate. `ModuleConfigReader.Ticks(…, fallback)`: Ersatzwerte sind künftig Bilder des Originals oder Millisekunden, nie rohe Ticks. Messen, was der Katalogbau kostet (er fällt jetzt je Sitzung an) und ob ein Zwischenspeicher je Rate nötig ist.
4. **Literale abtragen** – jede Stelle des Inventars (Kategorien A bis C in der Simulation), einzeln, mit der Klasse aus dem Inventar:
   - Originalkonstante in Bildern → Hilfsfunktion, Quellverweis im Kommentar bleibt.
   - Zeitwert, der als Tickzahl hingeschrieben wurde (z. B. `ForceIdleTicks = 2` für 67 ms) → als Bilder des Originals, damit 30 Hz unverändert bleibt.
   - Ablaufrhythmus mit Modulo (`tick % N`) → skalierter Rhythmus; die Phasenlage bei 30 Hz bleibt exakt erhalten.
   - Ganzzahlige Teilung der Rate (`30 / 4`) → das bisherige Ergebnis (7 Bilder) als Bilder des Originals.
   - Rohe Daten je Bild: Geschwindigkeiten und additive Größen werden beim Katalogbau skaliert (× 30 / Rate bzw. × 900 / Rate²). **Multiplikative Faktoren je Bild** (Dämpfung, Reibung, `Acceleration 1.01`) und alles aus Kategorie D des Inventars bleiben in dieser Phase unverändert und werden in einer Liste für Phase 23 geführt – sie sind bei 30 Hz richtig und bei 60 Hz falsch.
   - Bildzähler der Kartenskripte (`SET_TIMER`, `*_FOR_FRAMECOUNT`): als Bilder des Originals skaliert.
5. **Sitzungen, Szenarien, Werkzeuge der Simulation.** `SkirmishMatchOptions`, `MissionOptions`, die Szenarien in `Simulation/Testing`: Regelwerte (Gnadenfrist, Wissenschaftspunkt-Intervall, Siegprüfung) in Bildern des Originals, Laufbudgets (`MaxTicks`) bleiben Ticks. `SimTool`/`CampaignTool`: `--tick-rate N` (Standard 30 bis Phase 23), Ausgaben in Spielminuten aus der Rate statt `/ 30 / 60.0`.
6. **Formate.** Snapshot und Replay tragen die Rate bereits; künftig wird die Welt *mit der gespeicherten Rate* erzeugt statt eine fremde abzulehnen (abgelehnt wird nur eine nicht unterstützte). Bei 30 Hz bleiben die Bytes identisch, also keine Versionserhöhung. `Tests.State.cs` (Rate 60 abgelehnt) wird ersetzt: 50 wird abgelehnt, 60 und 120 werden angenommen.
7. **`SourceRules` erweitern:** in `src/CnCNew.Simulation` verboten sind die alte Konstante, numerische Ratenliterale und Konstanten bzw. Felder mit `Tick`/`Frame`/`Interval` im Namen, die mit einem Zahlliteral initialisiert werden (Ausnahmen namentlich im Test, jede mit Begründung). Die Suchmuster aus `docs/TICKRATE-AUDIT.md` §8 liefern für die Simulation keinen unbewerteten Treffer mehr.
8. **Probelauf bei 60 und 120 Hz** (Bewertung ausdrücklich *nicht* Gegenstand): Referenzmatch, zwei KI-Läufe, drei Missionen laufen ohne Ausnahme durch. Dabei zählen: Umrechnungen, die auf null runden (ein Wert je Tick wurde zu 0), Überläufe, verletzte Annahmen. Die Zähler sind Eingabe für Phase 22 (Festkomma) und Phase 23.
9. **Doku:** `docs/SIMULATION-CORE.md` (Abschnitt Zeit und Einheiten neu), `docs/GAME-RULES.md`, `docs/SCRIPT-ENGINE.md` (Bildzähler), `docs/TICKRATE-AUDIT.md` (Simulationsteil als erledigt markieren, Liste „für Phase 23“), `docs/MULTIPLAYER-PLAN.md`, `CLAUDE.md`, README, ARCHITECTURE, ROADMAP, IMPLEMENTATION-PLAN.

## Engstellen und Showstopper dieser Phase

- **Bytegleichheit bei 30 Hz ist der einzige Beweis.** Jeder Zwischenstand, bei dem das Manifest nicht besteht, ist ein Fehler im Umbau, kein „neuer Referenzwert“. In kleinen Commits arbeiten und nach jedem prüfen (Stufe `quick`), am Ende `full`.
- **Rundung bei Umrechnungen je Tick.** `PerSecondToPerTick` rundet auf `Fixed`; die Reihenfolge von Teilen und Multiplizieren muss bei 30 Hz exakt die alte bleiben.
- **`decimal` zur Laufzeit** (`ScriptEngine.*`, `SimulationWorld.CreationLists.cs:343` mit `/ 900m`): mit in den Zeitdienst.
- **Der Katalog ist heute Teil von `GameData`** und wird von Werkzeugen und Client geteilt; mit der Rate als Parameter darf kein Aufrufer versehentlich einen Katalog der falschen Rate an eine Welt geben (beim Erzeugen der Welt prüfen und werfen).

## Nicht Gegenstand

Präsentation, Spielschicht, UI, Ton, Client (Phase 21 – sie liest nach dieser Phase die Rate aus der Welt); die Festkomma-Auflösung (Phase 22); richtiges Verhalten bei 60 Hz und die Umstellung des Standards (Phase 23).

## Abnahme

`reference-manifest check --set full` besteht unverändert; die gesicherten Snapshots sind byteidentisch; `SourceRules` mit den neuen Regeln ist grün; der Probelauf bei 60 und 120 Hz läuft durch und seine Zähler sind dokumentiert; das Leistungsprofil zeigt keine Verschlechterung. Der Abschlussbericht nennt die Zahl der abgetragenen Stellen je Klasse mit Commit-Hashes, die Liste für Phase 23 und was nicht belegt ist. Am Ende die „Ausgangslage“ in `docs/HANDOFF-PHASE21.md` aktualisieren.
