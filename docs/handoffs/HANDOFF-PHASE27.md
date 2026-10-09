N+1-Handoff (Phase 27): Robustheit im Netz – Desync-Diagnose, Wiedereinstieg, Zuschauer, Server-Replay

Du arbeitest autonom an diesem Repository (`<repo>` = Wurzel des Arbeitsverzeichnisses). Lies zuerst `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, `docs/NETWORK-PROTOCOL.md`, `docs/DEDICATED-SERVER.md`, `docs/SIMULATION-CORE.md` (Snapshot, Zustands-Hash, Replay) und den Abschlussbericht der Phase 26.
Voraussetzung: Phase 26 ist abgeschlossen (acht Clientprozesse über den Dedicated Server, Latenztabelle gemessen).

## Ehrlicher Status (Ausgangslage)

Nach Phase 26 läuft ein Match, solange nichts schiefgeht. Für die drei Fälle, die im Betrieb sicher eintreten, gibt es noch nichts:

- **Desync.** Der Server vergleicht Hashes und stellt eine Abweichung fest, aber niemand kann sagen, *welches Objekt auf welchem Rechner ab welchem Tick* abweicht. Ohne dieses Werkzeug ist jeder Desync-Bericht eines Spielers wertlos.
- **Verbindungsabbruch.** Ein getrennter Spieler scheidet aus (Phase 26); zurückkommen kann er nicht.
- **Zuschauer und Aufzeichnung.** Der Server hat alle Tickbündel, schreibt sie aber nicht als Replay; Zuschauer gibt es nicht.

Belegte Zahlen zum Snapshot (alt, neu messen): der Weltsnapshot ist JSON (`JsonState`), rund 485–776 Byte je Objekt, im Stresstest 10–12 MB bei 13.220 Objekten, Wiederherstellung 0,7 s (`docs/SIMULATION-CORE.md`). Für die Übertragung an einen wiedereinsteigenden Client ist das unvermessen.

## Auftrag (in dieser Reihenfolge)

1. **Ausgangszustand reproduzieren**, Referenzwerte der Phase 26 neu messen.
2. **Desync-Diagnose.**
   - Der Hashbericht der Clients trägt neben dem Gesamthash die Teilsummen, die die Welt ohnehin getrennt führt (Objekte, Zeitgeber, wartende Befehle, Zufallsströme, je KI-Spieler der KI-Zustand aus Phase 24, Skriptzustand). Der Server meldet beim ersten abweichenden Tick, *welche Teilsumme* bei *welchem Client* abweicht.
   - Bei einer Desync-Meldung schreibt jeder Client sofort ein Diagnosepaket: Match-Beschreibung, alle empfangenen Tickbündel als Replay, seine Hashkette, einen Snapshot des aktuellen Ticks und eine Tabelle „Objekt-ID → Objekthash“. Nichts davon enthält Originaldaten.
   - `tools/CnCNew.NetTool desync-diff <paket-a> <paket-b>`: spielt das Replay lokal nach, findet den ersten abweichenden Tick gegen beide Hashketten und nennt die abweichenden Objekte und Felder (Snapshotvergleich Feld für Feld).
   - Test: ein absichtlich eingebauter Fehler (Testschalter, der auf einem Client ein Feld eines Objekts verändert) wird vom Werkzeug auf Tick, Objekt und Feld genau gefunden.
3. **Snapshot für die Übertragung.** Erst messen: Größe und Schreib-/Lesezeit des Sitzungssnapshots (Welt + KI) im späten 8-Spieler-Match, roh und mit `BrotliStream` (BCL). Liegt der komprimierte JSON-Snapshot unter den im Plan gesetzten Grenzen (Vorschlag: ≤ 2 MB, Schreiben + Komprimieren ≤ 150 ms, Lesen ≤ 1 s), bleibt das Format; sonst ein kompaktes Binärformat mit eigenem Versionsfeld und denselben Feldern (Test: JSON- und Binärsnapshot stellen denselben Hash her).
4. **Wiedereinstieg.**
   - Der Server kennt an Prüfpunkt-Ticks den von allen bestätigten Hash. Ein Client, der wieder einsteigt, erhält den Snapshot eines Prüfpunkts **von einem Mitspieler über den Server**, lädt ihn, berechnet den Hash und vergleicht ihn mit dem des Servers – ein falscher oder manipulierter Snapshot wird so erkannt, ohne dass der Server simulieren muss. Danach spielt der Client die Bündel seit dem Prüfpunkt im Aufholtempo nach.
   - Rückfallweg ohne Spender: Nachspielen aller Bündel ab Tick 0 (nur sinnvoll, solange das Aufholtempo aus Phase 23 es in vertretbarer Zeit schafft; die Grenze steht in der Doku).
   - Wiedererkennung über ein Sitzungstoken aus dem Handshake; die Frist, in der ein Platz frei gehalten wird, ist Konfiguration. Die Regel für den ausgeschiedenen Spieler aus Phase 26 wird entsprechend erweitert (Platz gehalten / endgültig ausgeschieden), als Systembefehle im Bündel.
   - Tests: Trennung und Wiedereinstieg zu drei Zeitpunkten (früh, Mitte, spät) mit Proxy-Störungen; Hashes danach identisch; ein verfälschter Spender-Snapshot wird abgelehnt.
5. **Zuschauer.** Ein Zuschauer ist ein Client ohne Spielerplatz: er erhält dieselben Bündel um eine einstellbare Frist verzögert (Standard z. B. 120 s, gegen das Mitlesen durch Mitspieler), steigt über denselben Snapshot-Weg ein und sendet keine Befehle (der Server verwirft sie, gezählt). Die Darstellung ohne eigenen Spieler (Nebel, Auswahl) ist Gegenstand von Phase 29; hier genügt der headless Nachweis.
6. **Server-Replay.** Am Ende eines Matches (auch nach Abbruch) schreibt der Server die Match-Beschreibung und alle Bündel im Replay-Format; `SimTool` und der Client spielen die Datei ab und erreichen die Hashes der Clients. KI-Befehle sind nicht im Bündel (die KI läuft in der Simulation, Phase 24); das Replay enthält deshalb die KI-Slots der Match-Beschreibung, und das Abspielen lässt die KI mitlaufen.
7. **Pause und Abstimmungen.** Pause auf Anforderung (Anzahl und Dauer begrenzt), Fortsetzen; alles als Systembefehle bzw. Serverzustand, nichts davon berührt die Simulation außerhalb der Bündel.
8. **Doku:** `docs/NETWORK-PROTOCOL.md`, `docs/DEDICATED-SERVER.md`, neu `docs/NETWORK-DIAGNOSTICS.md` (Diagnosepaket, Werkzeug, Vorgehen bei einem Desync-Bericht), `docs/MULTIPLAYER-PLAN.md`, README, ROADMAP, IMPLEMENTATION-PLAN, `CLAUDE.md`.

## Engstellen und Showstopper dieser Phase

- **Vollständigkeit des Snapshots.** Ein Wiedereinstieg deckt jede Lücke im Snapshot auf, die ein durchlaufendes Match nie zeigt (Zustand, der nur im Speicher lebt). Der Test „Snapshot an jedem Prüfpunkt eines 60-Minuten-Matches laden und weiterspielen, Hash bleibt identisch“ läuft für alle Fraktionen.
- **Kosten des Prüfpunkts.** Das Schreiben eines Snapshots darf den Spender nicht sichtbar stocken lassen; Zeit je Prüfpunkt messen und gegen das Tickbudget stellen. Wenn nötig, den Snapshot über mehrere Ticks verteilt aus einer unveränderlichen Kopie schreiben – nie aus der laufenden Welt.
- **Hashbericht-Rhythmus.** Zu selten: ein Desync wird spät bemerkt. Zu oft: Bandbreite. Der Rhythmus ist gemessen und dokumentiert.

## Abnahme

Ein eingebauter Desync wird auf Tick, Objekt und Feld genau gefunden. Ein getrennter Client steigt wieder ein und ist danach hashgleich. Ein Zuschauer verfolgt ein Match verzögert. Der Server schreibt ein Replay, das die Hashes reproduziert. Der Abschlussbericht nennt Zahlen (Snapshotgrößen und -zeiten, Wiedereinstiegsdauer je Zeitpunkt, Bandbreite der Hashberichte) mit Commit-Hashes und was nicht belegt ist. Am Ende `docs/handoffs/HANDOFF-PHASE28.md` prüfen und die „Ausgangslage“ aktualisieren.
