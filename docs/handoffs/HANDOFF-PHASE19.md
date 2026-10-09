N+1-Handoff (Phase 19): Leistung – Tickkosten echter Matches senken, ohne ein Ergebnis zu ändern

Du arbeitest autonom an diesem Repository (`<repo>` = Wurzel des Arbeitsverzeichnisses). Lies zuerst `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, `docs/DETERMINISM.md`, `docs/SIMULATION-CORE.md` (Tickphasen, Zustands-Hash, Sicht, Navigation, Leistungsbudget) und den Abschlussbericht der Phase 18 mit dem Leistungsprofil.
Voraussetzung: Phase 18 ist abgeschlossen und hat das Programm freigegeben; das Referenzmanifest (`baselines/`, `SimTool reference-manifest check`) besteht.

## Warum diese Phase so früh kommt

Die Tickrate soll von 30 auf 60 Hz steigen, und acht Spieler sollen gleichzeitig spielen. Beides verdoppelt bzw. vervielfacht die Rechenlast. Außerdem müssen die Phasen 22 und 23 **jeden** Referenzwert neu erheben, und Phase 23 bei doppelter Tickzahl. Eine schnelle Simulation ist also Voraussetzung für das Ziel *und* für die Arbeit daran. Optimierungen sind am sichersten, solange die Hashes als Messlatte unverändert gelten – das ist jetzt, vor den Phasen, die sie ändern.

## Ehrlicher Status (Ausgangslage; maßgeblich ist das Profil aus Phase 18)

**Ruhig gemessen am Ende von Phase 18 (6. Oktober 2026, Stand `b432fb7`, `scripts/perf-profile.ps1`, AMD Ryzen 7 PRO 6850U; Tabellen in `docs/PERFORMANCE.md` §6 – das ist die Ausgangslage, die Probe unten ist überholt):** 8 Spieler, Twilight Flame, 54.000 Ticks: Mittel **4,98 ms** je Tick, Median 4,29, 95 % 8,36, 99 % 13,60, Maximum **547 ms** (ein Tick, 100 % Updates), 95 KB Allokation je Tick, GC 616/68/2, Wiedergabe ohne KI 4,87 ms, Aufholtempo 6,8-fach bei 30 Hz und 3,4-fach bei 60 Hz, Endhash `0A69FC72FF6975CD`. Phasen im Mittel: Updates 3.264 µs (davon `AIUpdateInterface` 81,4 %, `TransportAIUpdate` 4,6 %, `SupplyTruckAIUpdate` 3,8 %), Vision 716, VisionHash 379, Hash 531, KI 45. Mit 4 Spielern 2,07 ms, mit 2 Spielern 0,80 ms auf derselben Karte. Missionen: `MD_USA05` 5,5–5,9 ms (Updates 2,0–2,2, Hash 1,6–1,7, Vision 0,9, VisionHash 0,56, Zielerwerb 0,3, Skripte 0,2–0,3), `MD_GLA05` 12,6–13,5 ms (Updates 4,6–5,3 mit `PointDefenseLaserUpdate` 30,6 %, Skripte 2,8–3,0 ms und 4 MB Allokation je Tick, Hash 2,2, Vision 1,5–1,7, VisionHash 0,8). Gegen die Sollwerte unten fehlt beim Mittel rund der Faktor 5, beim 99-%-Quantil 4,5, beim Maximum 68. Der Stresstest liegt ruhig bei 61 µs je Tick. Das Referenzmanifest (`SimTool reference-manifest check … --set full`, 50 Läufe) dauert auf diesem Rechner 25 bis 40 Minuten und ist die Prüfung nach jedem Commit; der Schnellsatz 5 Minuten.

Erste, nicht ruhige Probe vom 5. Oktober 2026 (siehe `docs/handoffs/HANDOFF-PHASE18.md`): 8 KI-Spieler auf Twilight Flame (486 × 469 Zellen), 18.000 Ticks – Aufzeichnung 123,1 s, Wiedergabe ohne KI 163,0 s, also **rund 7 bis 9 ms je Tick** bei 218 Spielerobjekten am Ende. Der synthetische Stresstest braucht 70–80 µs je Tick bei 13.220 Objekten. Die Kosten eines echten Matches kommen demnach nicht aus der Objektzahl, sondern aus etwas, das mit Kartengröße, Spielerzahl oder echten Modulen wächst. Frühere Messungen (Phase 17-Handoff): Kampagnenaudit je Karte 2 bis 10 Minuten, Gesamtlauf über eine Stunde.

Unbelegte Spuren (jede erst im Profil bestätigen, nichts auf Verdacht umbauen):

- Der Sicht-Hash wird alle `VisionIntervalTicks` (3) über jede Zelle für jeden Spieler berechnet (`VisionMap.cs`); bei 8 Spielern und 228.000 Zellen sind das 1,8 Millionen Zellen je Durchgang.
- `RefreshVision()` selbst im selben Rhythmus.
- `Describe` für jedes veränderte Objekt hasht Namen zeichenweise; die globale Beschreibung läuft in jedem Tick.
- Zielerfassung und Verfolgung im festen Rhythmus, Wegsuche mit zeitweise blockierten Zellen, Kollisionsverteilung, die KI-Beobachtung alle 5 Ticks über `EntitiesVisibleTo`.
- LINQ und Allokationen in heißen Pfaden (jede Speicherbereinigung ist im Lockstep ein Stocken, das aufgeholt werden muss).

## Ziel (Vorschlag; mit den Zahlen aus Phase 18 bestätigen oder begründet ändern)

Auf dem Entwicklungsrechner, 8 Spieler, 30 Spielminuten, große Karte, Release:

| Größe | Soll |
|---|---|
| Tickzeit Mittel | ≤ 1,0 ms |
| Tickzeit 99-%-Quantil | ≤ 3 ms |
| Tickzeit Maximum | ≤ 8 ms |
| Aufholtempo (Wiedergabe mit KI) | ≥ 16-fache Echtzeit bei 30 Hz (entspricht ≥ 8-fach bei 60 Hz) |
| Speicherbereinigung im laufenden Match | keine Gen-2-Läufe, Allokation je Tick im Ruhezustand nahe 0 |
| Kampagnenaudit je Karte (900 Ticks) | unter 1 Minute |

Die Zahlen stehen für: die Simulation belegt bei 60 Hz höchstens einen kleinen Teil eines Kerns, ein wiedereinsteigender Client holt 30 Minuten in wenigen Minuten auf, und die Neuerhebung der Referenzwerte dauert Minuten statt Stunden.

## Auftrag (in dieser Reihenfolge)

1. **Ausgangszustand reproduzieren.** Build, Tests, Manifest (`check --set full`), das Leistungsprofil aus Phase 18 auf ruhiger Maschine wiederholen. Ohne reproduzierte Ausgangszahlen keine Optimierung.
2. **Profil lesen und eine Rangliste schreiben:** je Kostenstelle Anteil an der Tickzeit, wie sie mit Spielerzahl, Kartengröße und Objektzahl wächst, und ob sie sich ohne Ergebnisänderung senken lässt.
3. **Kostenstellen einzeln abtragen, größte zuerst.** Für jede: Messung vorher, Änderung, `reference-manifest check` (muss unverändert bestehen), Messung nachher, ein Commit. Zulässige Mittel: inkrementelle statt vollständiger Berechnung (z. B. Sicht-Hash als fortgeschriebene Summe je Spieler, nur geänderte Zellen), Vorberechnung unveränderlicher Werte (Namenshashes), Datenstrukturen ohne Allokation, das Vermeiden wiederholter Arbeit im selben Tick.
4. **Der Zustands-Hash selbst.** Sein Wert darf sich in dieser Phase nicht ändern (das Manifest hängt daran). Wenn die Messung zeigt, dass nur eine andere Hashkonstruktion das Ziel erreicht, ist das eine eigene, benannte Entscheidung: Manifest mit Begründung neu schreiben, alle Läufe vorher/nachher auf identische *Ergebnisse* (Sieger, Endtick, Objektlisten) prüfen.
5. **Lastverteilung nur deterministisch.** Wenn eine Arbeit über mehrere Ticks verteilt werden muss (Wegsuche, KI), dann nach festen Zählgrößen (Knoten je Tick, Reihenfolge nach ID), niemals nach Wandzeit. Eine solche Verteilung ändert Ergebnisse und ist deshalb keine „Optimierung“, sondern eine vermessene Verhaltensänderung in einem eigenen Commit mit neu geschriebenem Manifest – nur wenn das Ziel ohne sie nicht erreichbar ist.
6. **Keine Threads im Simulationskern** (`SourceRules`). Ob der Client die Simulation auf einen eigenen Thread legen sollte, ist nicht Gegenstand dieser Phase; festhalten, was dem im Weg stünde (die Präsentation liest über `SimulationQueries` und den `FrameProjector` die laufende Welt).
7. **Spielschicht mitmessen.** `LiveGame.SyncAfterTick` (Projektion, Effekte, Nebel) läuft je Tick; seine Kosten je Tick mit 8 Spielern messen und die größten Stellen abtragen, soweit bild- und hashneutral (Golden-Hashes der Captures unverändert).
8. **Messwerkzeug dauerhaft machen.** `SimTool perf` schreibt eine maschinenlesbare Zusammenfassung; ein Vergleichslauf (`perf-compare alt neu`) zeigt Verschlechterungen. Es wird bewusst **kein** fehlschlagender Test daraus (Zeiten sind rechnerabhängig); der Lauf gehört aber ab jetzt zum Phasenabschluss.
9. **Doku:** `docs/SIMULATION-CORE.md` (Leistungsbudget, Kostenstellen, was geändert wurde und warum es ergebnisneutral ist), `docs/MULTIPLAYER-PLAN.md` (Register), `CLAUDE.md` (Befehle, neue Messwerte), README, ROADMAP, IMPLEMENTATION-PLAN.

## Nicht verhandelbare Regeln

`CLAUDE.md` und `docs/MULTIPLAYER-PLAN.md` §6. Für diese Phase: **Nach jedem Commit besteht das unveränderte Referenzmanifest.** Jede Ausnahme ist ein eigener Commit mit Begründung, Vorher/Nachher-Werten und Ergebnisvergleich. Messungen nur auf ruhiger Maschine, jede Zahl aus einem Lauf, Release-Werkzeuge vor jeder Messung neu gebaut.

## Abnahme

Die Sollwerte sind erreicht oder die verbleibenden Kostenstellen sind einzeln benannt, mit Anteil und dem Grund, warum sie ohne Ergebnisänderung nicht weiter sinken. Das Manifest ist unverändert (oder jede Änderung einzeln begründet). Der Abschlussbericht enthält die Tabelle vorher/nachher je Kostenstelle mit Commit-Hashes und die Hochrechnung auf 60 Hz. Am Ende die „Ausgangslage“ in `docs/handoffs/HANDOFF-PHASE20.md` aktualisieren.
