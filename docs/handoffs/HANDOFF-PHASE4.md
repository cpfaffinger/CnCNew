# N+1-Handoff: Phase 4 – Deterministischer Simulationskern

> **Status: abgeschlossen am 28. September 2026** (Ergebnisse in `docs/SIMULATION-CORE.md`, `docs/TYPED-DEFINITIONS.md`,
> `docs/IMPLEMENTATION-PLAN.md`). Der nächste Auftrag steht in `docs/handoffs/HANDOFF-PHASE5.md`. Dieses Dokument bleibt als Historie erhalten.

Du arbeitest autonom an diesem Repository (`<repo>` = Wurzel des Arbeitsverzeichnisses).

## Auftrag

Setze Phase 4 des Implementierungsplans vollständig um: einen headless laufenden,
deterministischen Simulationskern (`CnCNew.Simulation`) mit festem Tick, stabilen IDs,
reproduzierbaren RNG-Streams, Befehlsqueue, Objekt-/Modul-/Behavior-Lebenszyklus, kanonischen
Zustandssnapshots, Replay-Harness und Stresstest. Zuvor schließt ein kleiner **Block 0** die für
Phase 4 notwendigen Lücken aus Phase 3 (siehe unten).

Lies vor Änderungen vollständig `CLAUDE.md` (verbindliche Regeln, Befehle, Git-Regeln),
`README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md`,
`docs/CONTENT-PIPELINE.md`, `docs/TYPED-DEFINITIONS.md` (inkl. „Bekannte Grenzen“) und
`docs/PHASE3-COMPATIBILITY.md`.

## Verbindlicher Ausgangsstand

- Branch `main`, kein Remote; nichts pushen, keinen Remote anlegen. Phase 1–3 sind abgeschlossen
  (letzte Phase-3-Commits `07b5959`, `a030a98`, Doku `ae71e55`, `ebe04fe`).
- Falls `CLAUDE.md`, `AGENTS.md`, `docs/handoffs/HANDOFF-PHASE4.md` und die Doku-Nachträge zu
  `docs/TYPED-DEFINITIONS.md` / `docs/IMPLEMENTATION-PLAN.md` noch unversioniert sind: zuerst
  als eigener Commit `docs: add agent guidance and phase 4 handoff` einchecken.
- Verifiziert am 28. September 2026: Solution-Build (nur die 10 bekannten transitiven
  Stride-NU1901/NU1903-Warnungen) und alle acht Testprogramme grün; `AssetTool verify`,
  `IniTool audit`, `ImageTool audit-tga`, `W3dTool audit` grün.
- Frische Vollkompilierung: 256/256 INIs, 66.623 Blöcke, 12.677 Definitionen, 16.360 Module,
  335.592 Schlüssel, 12.441/12.468 Referenzen aufgelöst (27 sichtbare Kompatibilitätsfälle: 25
  Veterancy-Pseudoupgrades, `DieByFireUSA`, `Boss_VehicleSentryDrone`), 41 Definitionsdomänen
  (2.801 Instanzen) und 206 Module inventarisiert, Snapshot 112.664.082 Byte, SHA-256
  `14154C512DED47C4AD72B651F8FCB0BA702EACBF8678C51DDAB7D125BEC675D4`. Messe alles neu.
- Definitionsanzahl je Art: Object 2.127, Weapon 364, Armor 53, Damage 11, Upgrade 82,
  Science 96, Faction 15, CommandButton 820, CommandSet 479, FxList 429, Fx 1.087,
  AudioEvent 1.410, DialogEvent 2.569, MusicTrack 69, Locomotor 184, SpecialPower 80,
  GameData 1, sonstige (Compatibility) 2.801.

## Architekturgrenzen

- `CnCNew.Simulation` hängt nur von `CnCNew.Data` ab. Kein Stride, DirectX, UI, Content-,
  Archiv-, Filesystem- oder Netzwerkzugriff; keine `IniNode`, keine rohen INI-Strings.
- Keine Wandzeit (`DateTime`, `Stopwatch`, `Environment.TickCount`) im Kern, keine Threads im
  Tick, kein `Random.Shared`, keine `Dictionary`-/`HashSet`-Iterationsreihenfolge in
  zustandsrelevanter Logik, keine Reflection-Reihenfolge, keine ungebundenen `float`/`double`
  im Zustand (siehe Numerik).
- Die Präsentation liest später nur immutable Snapshots und schreibt nur typisierte Befehle.
- Unbekannte Module dürfen nicht instanziiert werden. Nicht implementierte Bestandsmodule
  bleiben sichtbare, nicht ausführbare Kompatibilitätsfälle.

## Block 0: Phase-3-Härtung (nur soweit für Phase 4 nötig)

Die Nachprüfung von Phase 3 bestätigte Zahlen und Determinismus, fand aber fachliche Lücken
(`docs/TYPED-DEFINITIONS.md`, „Bekannte Grenzen“). Schließe vorab genau diese Punkte; alles
darüber hinaus (domänenspezifische Typen für Weapon/Armor/Locomotor/… ) ist Vorleistung für
Phase 5 und wird nur mit Zahlen als Schuld dokumentiert. Block 0 darf Phase 4 nicht verdrängen.

0.1 **Kompatibilitätsliste als geprüfte Basislinie.** Bisher zählt der Compiler jeden
    unregistrierten Modul-/Definitionsnamen automatisch; die Liste ist nirgends fixiert.
    Checke eine kleine, reviewte Basislinie ein (Namen, Besitzerarten, Häufigkeiten, Zielphase;
    keine Assetinhalte) und ergänze `IniTool compat-check`, das bei jeder Abweichung (neuer,
    fehlender, in der Häufigkeit geänderter Eintrag) mit Exitcode ≠ 0 endet. Dazu ein Test mit
    synthetischen Daten. Ein Tippfehler im Modulnamen muss von einem bekannten,
    noch nicht implementierten Modul unterscheidbar sein.
0.2 **Modulbindung.** `ModuleDescriptor.Factory` ist `Func<ModuleConfiguration, object>` –
    untypisiert und nicht in Data bindbar, ohne dass Data die Simulation kennt. Entwirf die
    Trennung sauber: Data führt die Deskriptoren (Name, Besitzerarten, Feldschema,
    `CompatibilityOnly`, Zielphase) inklusive aller 206 Bestandsmodule als explizit bekannte,
    nicht implementierte Einträge; `CnCNew.Simulation` besitzt eine explizite, sortierte,
    reflexionsfreie Bindungstabelle Modulname → typisierte Factory und prüft sie beim Start gegen die
    Data-Deskriptoren (unbekannter Name, falscher Besitzer, Duplikat = Fehler). Der strikte Modus
    (`StrictUnknownModules`) muss auf den Bestandsdaten bestehen und bei jedem neuen Namen
    scheitern. Test: Katalog == im Vollbestand beobachtete Modulmenge.
0.3 **Wertgrammatik für das, was Phase 4 liest.** Messe zuerst die realen Formen im Bestand
    (bekannt: `R:255 G:0 B:0 [A:..]` 9.150×, `X:0 Y:0 Z:0` 6.161×, `KindOf = A B C` 1.964×,
    wiederholtes `Locomotor = SET_NORMAL <Name>`). Implementiere echte `Color`-, `Vector2/3`-,
    `Enum`- und `Flags`-Werte mit strikten Namenstabellen, die aus der Referenz abgeleitet und
    dort zitiert werden (z. B. `KindOfMaskType`-Namen); ein unbekannter Token ist ein Fehler, nicht
    `Text`. Umfang: alle Felder, die die Phase-4-Module und die Objektinstanziierung lesen
    (mindestens `KindOf`, Side/Geometrie-Grundlagen, Modulfelder aus 0.2). Der Rest → Phase 5.
    Pflichtfelder und Schlüsselschemata für diese Felder validieren; unbekannte Schlüssel an
    solchen Definitionen sind Fehler oder stehen in einer expliziten, gezählten Liste.
0.4 **Ladereihenfolge und Mehrfachdefinitionen.** Gleiche die Reihenfolge des Compilers mit
    `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/Common/GameEngine.cpp`
    (Zeilen ca. 363–532: `initSubsystem` mit Default-/Haupt-INI und `Data\INI\Object`) sowie den
    Ladearten `INI_LOAD_OVERWRITE`/`CREATE_OVERRIDES`/`MULTIFILE` in `INI.cpp` ab. Erzeuge einen
    Report aller Definitionen, die in mehr als einer Datei vorkommen (mit Dateireihenfolge), und
    weise nach, dass die Compilerreihenfolge dieselben Gewinner liefert wie das Original; andernfalls
    korrigieren. Prüfe außerdem, welche Schlüssel innerhalb eines Definitionskopfs mehrfach
    vorkommen und ob das Überschreiben (`fields.RemoveAll`) für sie fachlich richtig ist;
    wiederholbare Schlüssel behalten ihre Vielfachheit und Reihenfolge.
0.5 **Fehlende Phase-3-Tests nachziehen** (klein, synthetisch): Definition-IDs und Snapshots
    unabhängig von der Einfüge- beziehungsweise `EnumerateIniFiles`-Reihenfolge; abgeschnittene
    Eingaben im Compiler; falsch typisierter Modulfeldwert; Enum/Flags/Color/Vector-Fehlerfälle;
    zwei Vollkompilierungen synthetisch byteidentisch.

Block 0 endet mit einem eigenen Commit und aktualisierten Zahlen; der Snapshot-Hash ändert sich
erwartungsgemäß – erkläre jede Abweichung (Feldarten-Zähler, Anzahl `Text`/`List` vorher/nachher)
und dokumentiere den neuen Referenzhash.

## Vollständiger Umfang Phase 4

Neue Projekte: `src/CnCNew.Simulation`, `tests/CnCNew.Simulation.Tests` (Konsolenprogramm im Stil
der übrigen Tests), `tools/CnCNew.SimTool` (Replay, Hash, Stresstest, Audit); alle in
`CnCNew.slnx` eintragen.

1. **Tick und Numerik.** Fester Logiktakt 30 Hz (`LOGICFRAMES_PER_SECOND`,
   `GameCommon.h:72`). Verwende Ganzzahl- oder Fixed-Point-Arithmetik (begründe die Wahl; z. B.
   Q-Format mit `long`) für Position, Geschwindigkeit, Winkel und Schaden; kein `float`/`double`
   im Zustand. Lege Umrechnungen aus den Data-Werten (`GameTime` in ms, Geschwindigkeit pro
   Sekunde, Winkel in Grad, Prozent) in Ticks, Distanz pro Tick und Fixed-Point explizit fest –
   inklusive Rundungsregel. Prüfe dazu `ConvertDurationFromMsecsToFrames` und Verwandte in
   `GameCommon.h:80–107` sowie die Dauer-/Geschwindigkeitsparser in `INI.cpp` und dokumentiere
   Übernahmen und Abweichungen. Grenzwerte (Überlauf, negative Werte, 0 ms) sind getestet.
2. **IDs und Ordnung.** Monotone, deterministische World-/Entity-/Player-/Command-/Event-/Timer-IDs
   (Entity-ID-Vergabe und ungültige ID 0 gegen `GameLogic.cpp` prüfen). Kanonische
   Update-Reihenfolge unabhängig von Hash- oder Threadreihenfolge. Prüfe das „sleepy update“-
   Modell des Originals (`UpdateModule` mit Wake-Frame, `UPDATE_SLEEP_FOREVER`, Sortierung nach
   Wake-Frame dann Objekt-ID) und übernimm es oder begründe die Abweichung.
3. **RNG.** Benannte Streams (mindestens `logic` und ein für die Logik unbeachtlicher
   `cosmetic`-Stream) mit festem Algorithmus, dokumentierter Seedableitung, serialisierbarem
   Zustand und Golden-Sequenzen. Prüfe `RandomValue.cpp` der Referenz und implementiere den
   `logic`-Stream originalgetreu oder begründe eine bewusste Abweichung. Golden-Werte unabhängig
   nachrechnen (kompiliere die Referenz nur, wenn ein Compiler verfügbar ist; andernfalls
   dokumentierte, unabhängige Handrechnung). Kein `Random.Shared`.
4. **World und Lebenszyklus.** Entityspeicher, Spawn/Destroy-Transaktionen, Modulbesitz, Events,
   Timer und klar getrennte Tickphasen (z. B. Befehle → Updates → Events → Destroy → Spawn →
   Hash). Mutationen während einer Iteration laufen über deterministische Queues; ein am Tickende
   zerstörtes Objekt empfängt in diesem Tick keine weiteren Updates, aber seine Events werden
   definiert zugestellt. Dokumentiere die Phasenreihenfolge.
5. **Module.** Aus der Registrierung (0.2) die minimalen Lebenszyklusmodule implementieren, die ein
   synthetisches Objekt erzeugen, ticken, Ereignisse empfangen und zerstören: Wähle anhand der
   Häufigkeiten im Bestand (`ActiveBody`, `InactiveBody`, `ImmortalBody`, `StructureBody`,
   `LifetimeUpdate`, `DeletionUpdate`, `DestroyDie`, `InstantDeathBehavior`, … – belege die Auswahl
   mit Zahlen). Jedes implementierte Modul hat ein strenges Feldschema aus den realen INIs.
   Keine Präsentationsmodule (`W3D*Draw`, `*ClientUpdate`) in der Simulation. Erzeuge zusätzlich
   einen Audit „Modulabdeckung“: wie viele der 2.127 Objekte lassen sich heute vollständig
   instanziieren, wie viele scheitern an welchem nicht implementierten Modul (Häufigkeitsliste).
6. **Befehle.** Typisierte, tick-adressierte Befehlsqueue mit versionierter Command-Typ-Tabelle,
   validierter Spieler-/Entity-Zuordnung, stabiler Reihenfolge bei gleichem Tick (Spieler-ID,
   dann Befehlssequenz) und expliziter, protokollierter Ablehnung verspäteter, doppelter oder
   ungültiger Befehle. Phase 4 nutzt nur synthetische Befehle (z. B. Spawn, Destroy, Schaden,
   Timer setzen); Gameplaybefehle folgen in Phase 5.
7. **Snapshots und Hashes.** Kanonischer, versionierter, maschinenlesbarer World-Snapshot ohne
   absolute Pfade, Zeitstempel oder ungeordnete Collections, plus schneller Zustands-Hash pro Tick
   (begründe den Algorithmus; neue Paketabhängigkeit nur mit Begründung, bevorzugt BCL/
   `System.IO.Hashing`) und SHA-256 für vollständige Snapshots. Restore nur, wenn der Vertrag
   ohne Vorgriff auf Phase 9 stabil ist; der Fortsetzungslauf (Punkt 8) braucht ihn jedoch – halte
   Restore dann minimal und intern.
8. **Replay-Harness.** Versioniertes Replay-Format: Kopf (Schemaversion, Tickrate,
   Fingerabdruck des Definitionsgraphen, Fixture-/Kartenkennung, Seed) + tick-adressierte
   Befehle + periodische Checkpoint-Hashes. Der Fingerabdruck der Definitionen muss aus dem
   kanonischen Snapshot oder einem streamenden Hash stammen; ein Replay auf abweichenden
   Definitionen wird abgelehnt. Zwei frische Läufe und ein Snapshot-/Fortsetzungslauf liefern
   identische Tickhashes über mindestens 216.000 Ticks (zwei Stunden Spielzeit). Beschädigte,
   abgeschnittene oder versionsfremde Snapshots/Replays werden abgewiesen.
9. **Speicher und Stress.** Headless-Stresstest mit mindestens 10.000 gleichzeitig lebenden
   Entities, mindestens 100.000 Spawn/Destroy-Operationen, vielen Timern, Events und Befehlen
   über mindestens 216.000 Ticks. Prüfe Leaks (Entity-/Modul-/Timer-/Eventtabellen kehren auf den
   Ausgangszustand zurück, Allokationen pro Tick im Beharrungszustand gemessen), Deadlocks und
   Nichtdeterminismus. Lege ein begründetes Laufzeit- und Speicherbudget fest und dokumentiere die
   gemessenen Werte samt Hardware.
10. **Doku.** README, Architektur, Roadmap, Implementierungsplan aktualisieren; neues
    `docs/SIMULATION-CORE.md` mit Tickphasen, Numerik, RNG, ID-/Queueordnung, Snapshot- und
    Replayformat, Modulbindung, Modulabdeckung, Budgets und Abnahmebefehlen; Kompatibilitäts-
    Basislinie und Block-0-Ergebnisse dokumentieren; `CLAUDE.md` bei Bedarf nachführen.

## Mindesttests

- Block 0: alle Punkte aus 0.1–0.5.
- Tickrate, Zeitkonvertierung, Fixed-Point-Rundung und Grenzwerte;
- ID-Stabilität und kanonische Iterationsreihenfolge, insbesondere bei permutierter
  Einfügereihenfolge;
- Golden-RNG-Sequenzen, getrennte Streams, Seedableitung, Zustands-Roundtrip;
- Spawn, Tick, Event, Timer, Destroy und Mutation während eines Ticks;
- Bindungstabelle: Factory, falscher Besitzer, Duplikat, unbekanntes und nicht implementiertes Modul;
- Command-Reihenfolge, Duplikate, verspätete und ungültige Befehle;
- identische Snapshots/Hashes bei unterschiedlichen Einfügereihenfolgen;
- Replaygleichheit über lange Läufe und nach Snapshotfortsetzung;
- beschädigte Snapshots/Replays, Versions- und Definitionsfingerabdruckfehler;
- Stresstest ohne Leaks, Deadlocks oder Abweichung;
- ein optionaler Realdatentest (`--content-dir .local/phase2-import-a`), der nur über
  `CnCNew.Data` kompilierte Bestandsobjekte instanziiert.

Fixtures sind klein, synthetisch und rechtlich unbedenklich; Tests der Simulation kompilieren
synthetische INIs über eine In-Memory-`IDefinitionSource`. Neue Warnungen und stille Fallbacks
gelten bis zur Erklärung als Fehler.

## Definition of Done

Phase 4 ist erst abgeschlossen, wenn:

- Solution-Build und alle bestehenden und neuen Tests grün sind (Warnungen: nur die bekannten
  NU1901/NU1903);
- Block 0 vollständig umgesetzt ist, `compat-check` gegen die Basislinie besteht und der strikte
  Modulmodus auf den Bestandsdaten besteht;
- identische Definitionen/Fixture/Seed/Befehle über lange Läufe bitidentische Tickhashes liefern,
  auch nach Snapshotfortsetzung;
- World-/Entity-/Command-/Eventreihenfolgen spezifiziert und getestet sind;
- der Modulvertrag ohne Reflection und ohne Engine-Typen gebunden ist und `CnCNew.Simulation`
  nur von `CnCNew.Data` abhängt;
- Speicher- und Headless-Stresstest bestehen und Budgets dokumentiert sind;
- Dokumentation und Code übereinstimmen und der Implementierungsplan bekannte Grenzen ehrlich benennt;
- der Working Tree sauber ist, alle Änderungen in mehreren fachlich geschlossenen Commits vorliegen,
  keine Originalassets, lokalen Snapshots, Manifeste, Captures, Buildoutputs oder IDE-Daten
  committed wurden und kein Remote angelegt oder etwas gepusht wurde.

## Abschlussbericht und N+1

Berichte: Block-0-Ergebnisse (mit neuen Zahlen und Referenzhash), Implementierung,
Determinismusregeln, Test-/Stress-/Replayergebnisse samt Hashwerten, Modulabdeckung und
Objektinstanziierungsquote, bekannte Grenzen und technische Schulden, alle Commit-Hashes,
finalen Git-Status sowie Bestätigung „kein Remote, nichts gepusht“.

Erzeuge danach als `docs/handoffs/HANDOFF-PHASE5.md` einen eigenständigen N+1-Handoff für Phase 5
„Spielregeln, Kampf, Wirtschaft und Produktion“ – inklusive der verbleibenden Schuld an
domänenspezifischen Definitionstypen – und committe ihn. Falls Phase 4 nicht vollständig ist,
erzeuge stattdessen einen präzisen Fortsetzungsprompt für Phase 4.
