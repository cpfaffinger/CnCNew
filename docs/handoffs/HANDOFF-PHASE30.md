N+1-Handoff (Phase 30): Patch-Integration, Qualität und auslieferbarer Abschluss (verschoben; ursprünglich der Schritt nach Phase 17)

Du arbeitest autonom an diesem Repository (`<repo>` = Wurzel des Arbeitsverzeichnisses). Lies zuerst `CLAUDE.md`, `README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§11 „Patch-Integration, Qualität und auslieferbarer Abschluss“ samt „Reihenfolge und Änderungsregel“), `docs/MULTIPLAYER-PLAN.md`, `docs/MULTIPLAYER-FINDINGS.md`, `docs/DEMO-FINDINGS.md` und den Abschlussbericht der Phase 29.

## Warum dieser Auftrag hier steht

Bis zum 5. Oktober 2026 war dies der Schritt unmittelbar nach Phase 17 (`IMPLEMENTATION-PLAN.md`, damals §10; `ROADMAP.md`, damals §5). Mit der Scope-Änderung vom 5. Oktober 2026 (Mehrspieler über Dedicated Server, 60 Hz, parametrisierte Tickrate) liegt das Programm der Phasen 18–29 davor: Tickrate, Festkomma-Auflösung und Formate ändern jeden Referenzwert und machen Speicherstände und Replays unlesbar. Ein Paket, das vor diesen Änderungen ausgeliefert würde, müsste danach gebrochen werden. Deshalb kommt der Abschluss zuletzt und umfasst jetzt auch den Server.

## Ehrlicher Status (Ausgangslage – beim Start dieser Phase neu erheben)

Dieser Abschnitt ist ein Platzhalter mit dem Wissen vom 5. Oktober 2026 und **wird von Phase 29 mit dem gemessenen Stand überschrieben**. Bekannt ist heute:

- Aus Phase 15–17 ist vieles gebaut, aber nicht von einem Menschen abgenommen (Sichtprüfung der Welt, Ton, Mauszeiger, Texte, Durchklicken der Shell, Spielbarkeit der Missionen; Nur-Befehle-Agent 1 von 24). Was davon beim Start dieser Phase noch offen ist, gehört an den Anfang dieses Auftrags – ein auslieferbarer Abschluss setzt die Abnahme voraus.
- Kein Community-Patch ist integriert; `references/` enthält GeneralsGameCode und GeneralsGamePatch nur zum Lesen.
- Es gibt kein Installationspaket, kein Crash-Reporting, keine Lizenz-/Attributionsübersicht.

## Auftrag (in dieser Reihenfolge)

1. **Ausgangszustand reproduzieren** und diesen Abschnitt „Ausgangslage“ mit gemessenen Zahlen neu schreiben: Build (0 Warnungen), alle Testprogramme, alle Audits, die Referenzwerte bei 60 Hz, der Stand der Abnahmen durch Menschen (Einzelspieler *und* Mehrspieler).
2. **Offene Abnahmen aus den Phasen 15–17 und 29 schließen** (mit dem Menschen): jede nicht abgehakte Zeile in `docs/CLIENT-OBJECTS.md`, `docs/DEMO-FINDINGS.md`, `docs/MULTIPLAYER-FINDINGS.md` wird abgenommen, behoben oder mit Grund als bekannte Grenze benannt.
   Dazu gehört der „Rest der Phase 17“ am Ende von `docs/handoffs/HANDOFF-PHASE17.md` (Agent, der Gefechte gewinnt, KI-Wirtschaft und -Schwierigkeit, Durchklicken), soweit er nicht vor Phase 18 erledigt wurde.
3. **GeneralsGameCode-Fixes** nach Subsystem katalogisieren (Simulation, KI, Pfadsuche, Skripte, Darstellung, UI); jeden übernommenen Fix verstehen, neu schreiben und mit Regressionstest und Quellverweis (Datei, Funktion) belegen. Jeder Fix, der die Simulation ändert, ist eine vermessene Hash-Änderung mit neuen Referenzwerten und – weil alle Mitspieler dieselbe Simulation brauchen – einer neuen Simulationsversion im Handshake.
4. **GeneralsGamePatch als eigene Overlay-Schicht** importieren (über `CnCNew.Import`, eigener Definitions-Fingerabdruck); Balanceänderungen getrennt schaltbar. Im Mehrspieler ist die gewählte Schicht Teil der Match-Beschreibung: ein Raum nennt Original oder Patch, und nur Clients mit demselben Fingerabdruck treten bei.
5. **Diagnose:** Crash-Bericht mit Diagnosepaket (Client und Server), strukturierte Protokolle, Profiler-Schalter, Support-Bündel. Nichts davon enthält Originaldaten oder verlässt den Rechner ohne ausdrückliche Handlung des Menschen.
6. **Abhängigkeiten und Recht:** `scripts/audit-vulnerable.ps1` für alle Projekte einschließlich Server, Lizenzen und Attributionen dokumentieren, Trennung von Originaldaten, generierten Artefakten und eigenem Code im Paket belegen.
7. **Reproduzierbarer Abschluss:** Clean-Room-Build auf frischem Rechner, frischer Assetimport, vollständige Testmatrix (einschließlich der Raten-Äquivalenz-Suite aus Phase 23 und der Netzmatrix aus Phase 26), Langzeitläufe, Paket für den Client (Windows) und den Dedicated Server (Windows, Linux) – automatisiert und dokumentiert.
8. **Dokumentation für Menschen:** Installation, Import der eigenen Originalinstallation, Betrieb eines Servers, Fehlersuche (einschließlich „mein Match ist auseinandergelaufen“), Versions- und Kompatibilitätsregel für Speicherstände, Replays und das Netzprotokoll ab der ersten Veröffentlichung.

## Nicht verhandelbare Regeln

Unverändert `CLAUDE.md` und `docs/MULTIPLAYER-PLAN.md` §6. Zusätzlich: ab der ersten Veröffentlichung ist jede Änderung an Tickrate, Festkomma-Format, Snapshot-, Replay- oder Protokollformat ein Bruch für Spieler und braucht eine eigene, begründete Entscheidung des Projektinhabers.

## Abnahme

Ein frischer Rechner überführt aus dokumentierten Voraussetzungen Quellcode und lokale Originalinstallation reproduzierbar in ein startbares Client- und Serverpaket; die gesamte Testmatrix ist grün; keine ungeklärten hochprioren Defekte sind offen; Einzelspieler (Skirmish, Kampagne) und Mehrspieler (Skirmish über Dedicated Server, bis acht Spieler mit KI-Slots) sind von Menschen end-to-end abgenommen. Der Abschlussbericht nennt Zahlen mit Commit-Hashes und ausdrücklich, was nicht belegt ist.
