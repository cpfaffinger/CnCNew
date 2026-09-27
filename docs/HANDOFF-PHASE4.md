# N+1-Handoff: Phase 4 – Deterministischer Simulationskern

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`.

## Auftrag

Setze Phase 4 des Implementierungsplans vollständig um: einen headless laufenden,
deterministischen Simulationskern mit festem Tick, stabilen IDs, reproduzierbaren RNG-Streams,
Befehlsqueue, Objekt-/Modul-/Behavior-Lebenszyklus und kanonischen Zustandssnapshots.

Lies vor Änderungen vollständig `README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`,
`docs/IMPLEMENTATION-PLAN.md`, `docs/CONTENT-PIPELINE.md`, `docs/TYPED-DEFINITIONS.md` und
`docs/PHASE3-COMPATIBILITY.md`; befolge eine vorhandene `AGENTS.md`.

## Verbindlicher Ausgangsstand

- Branch `main`, kein Remote; nichts pushen und keinen Remote anlegen.
- Phase-3-Implementierung: Commit `07b5959` (`feat(data): compile typed game definitions`).
- Phase 1 und 2 bleiben abgeschlossen.
- 256/256 INI-Dateien und 66.623 Blöcke kompilieren zu 12.677 Definitionen, 16.360 Modulen und
  204.006 Schlüsseln.
- Der Referenzgraph löst 5.293/5.294 Verwendungen auf. Ausschließlich
  `Boss_VehicleSentryDrone` ist als `compatibility-unresolved-reference` ausgewiesen.
- 41 spätere Definitionsdomänen mit 2.801 Instanzen und alle 206 Bestandsmodule mit 16.360
  Verwendungen sind explizit inventarisiert.
- Zwei Vollkompilierungen erzeugen byteidentische Snapshots mit 67.885.672 Byte und SHA-256
  `312332B7255665F10D69567A3113CF5CBC207395F3C3470E79B6C37265E5F475`.
- Build und alle acht Testprogramme sind grün. Ausschließlich die dokumentierten transitiven
  Stride-NU1901/NU1903-Warnungen sind vorhanden.

## Architekturgrenzen

- Lege `CnCNew.Simulation` renderer-, engine-, archiv-, filesystem- und UI-unabhängig an.
- Die Simulation konsumiert nur `CompiledDefinitionGraph` und typisierte Definitionen; keine
  `IniNode`, rohen INI-Strings, BIG-Archive, Import-VFS-, Content- oder Stride-Typen.
- Präsentation liest immutable Simulationssnapshots und schreibt ausschließlich typisierte
  Inputbefehle. Wandzeit, Threads und Eingabegeräte dürfen den Kern nicht beeinflussen.
- Unbekannte Module dürfen nicht instanziiert werden. Beginne mit expliziten Deskriptoren und
  Factories für die Phase-4-Lebenszyklusmodule; verbleibende Einträge bleiben sichtbare,
  nicht ausführbare Kompatibilitätsfälle.

## Vollständiger Umfang

1. Definiere festen Tick, Simulationszeit und klare Regeln für Ganzzahl-, Fixed-Point- oder
   deterministisch begrenzte Fließkommaarithmetik. Verhindere implizite Wandzeitabhängigkeit.
2. Implementiere monotone, deterministische World-/Entity-/Player-/Command-/Event-IDs und eine
   kanonische Update-Reihenfolge unabhängig von HashMap- oder Threadreihenfolge.
3. Führe benannte RNG-Streams mit festgelegtem Algorithmus, Seedableitung, Snapshotzustand und
   Tests gegen Golden-Sequenzen ein. Kein `Random.Shared` im Kern.
4. Implementiere World, Entityspeicher, Spawn/Destroy-Transaktionen, Komponenten-/Modulbesitz,
   Events, Timer und klar getrennte Tickphasen. Mutationen während Iteration müssen über
   deterministische Queues laufen.
5. Binde `ModuleRegistry`-Deskriptoren deterministisch an Simulationfactories. Implementiere die
   minimalen Lifecycle-/Body-/Update-Behaviors, die ein synthetisches Objekt erzeugen, ticken,
   Ereignisse empfangen und zerstören können. Keine Präsentationsmodule in der Simulation.
6. Implementiere eine typisierte, tickadressierte Befehlsqueue mit validierter Spieler-/Entity-
   Zuordnung, stabiler Reihenfolge bei gleichem Tick sowie expliziter Ablehnung verspäteter,
   doppelter oder ungültiger Befehle.
7. Erzeuge kanonische maschinenlesbare World-Snapshots und schnelle Zustands-Hashes ohne absolute
   Pfade, Zeitstempel oder ungeordnete Collections. Restore ist nur einzuführen, wenn der Vertrag
   ohne Vorgriff auf Phase 9 stabil definierbar ist.
8. Baue ein Replay-Harness aus Definitionen, Karte/Fixture, Seed und Befehlsfolge. Zwei frische
   Läufe und ein Snapshot-/Fortsetzungslauf müssen über lange Strecken identische Tickhashes
   liefern.
9. Ergänze Speicher-/Lebenszyklusprüfungen und einen Headless-Stresstest mit vielen Entities,
   Timern, Events, Spawn/Destroy-Operationen und Befehlen. Lege ein begründetes Laufzeit- und
   Speicherbudget fest und dokumentiere Messergebnisse.
10. Aktualisiere README, Architektur, Roadmap und Implementierungsplan; dokumentiere Tickphasen,
    Numerik, RNG, ID-/Queueordnung, Snapshot-/Replayformat, Modulbindung und Abnahmebefehle.

## Mindesttests

- Tickrate, Zeitkonvertierung und Grenzwerte;
- ID-Stabilität und kanonische Iterationsreihenfolge;
- Golden-RNG-Sequenzen und getrennte Streams;
- Spawn, Tick, Event, Timer, Destroy und Mutationen während eines Ticks;
- Registryfactory, falscher Besitzer und unbekanntes/nicht implementiertes Modul;
- Command-Reihenfolge, Duplikate, verspätete und ungültige Befehle;
- identische Snapshots/Hashes bei unterschiedlichen Einfügereihenfolgen;
- Replaygleichheit über lange Läufe und nach Snapshotfortsetzung;
- beschädigte Snapshots/Replays und Versionsfehler;
- Stresstest ohne Leaks, Deadlocks oder nichtdeterministische Abweichung.

Fixtures müssen klein, synthetisch und rechtlich unbedenklich sein. Neue Warnungen und stille
Fallbacks gelten bis zur Erklärung als Fehler. Bewahre fremde Änderungen, verwende keine
destruktiven Git-Befehle, committe keine Originalassets, lokalen Snapshots, Manifeste, Captures,
Buildoutputs oder IDE-Daten. Erzeuge mehrere fachlich geschlossene Commits.

## Definition of Done

Phase 4 ist erst abgeschlossen, wenn Solution-Build und alle bestehenden/neuen Tests grün sind,
identische Definitionen/Karte/Seed/Befehle über lange Läufe bitidentische Tickhashes liefern,
World-/Entity-/Command-/Eventreihenfolgen spezifiziert und getestet sind, der Modulvertrag ohne
Reflection oder Engine-Typen gebunden ist, Speicher- und Headless-Stresstest bestehen,
Dokumentation und Code übereinstimmen, der Working Tree sauber ist und kein Remote angelegt oder
etwas gepusht wurde.

Berichte abschließend Implementierung, Determinismusregeln, Test-/Stress-/Replayergebnisse,
Snapshot-/Hashwerte, Modulabdeckung, bekannte Grenzen, alle Commit-Hashes, finalen Git-Status und
Remote-/Push-Bestätigung. Erzeuge danach den eigenständigen N+1-Handoff für Phase 5 oder – falls
Phase 4 nicht vollständig ist – einen präzisen Fortsetzungsprompt für Phase 4.
