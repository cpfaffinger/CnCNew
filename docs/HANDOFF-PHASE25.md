N+1-Handoff (Phase 25): Protokoll, Raum (Lobby) und Relay-Server im selben Prozess – das lokale Gefecht läuft über den Netzpfad, KI-Slots inklusive

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`. Lies zuerst `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`** (Entscheidungen, Zielarchitektur, Register der Engstellen, Regeln für alle Phasen), `docs/ARCHITECTURE.md`, `docs/SIMULATION-CORE.md` (Sitzungszustand, Replay), `docs/PRESENTATION-INPUT.md` (Befehlsabsicht), `docs/GAME-SHELL.md` (Gefechtssetup) und den Abschlussbericht der Phase 24.
Voraussetzung: Phase 24 ist abgeschlossen – KI-Zustand im Sitzungshash, Speicherstand, `MatchDescription`, zweistufiger Befehlsweg, `ITickDriver`, Lockstep-Probe mit künstlicher Verzögerung.

## Ehrlicher Status (Ausgangslage)

Es gibt im Repo **keinen Netzwerkcode**: kein Projekt, kein Protokoll, keine Abhängigkeit. Nach Phase 24 ist die Sitzung so gebaut, dass sie von Tickbündeln getrieben werden kann, aber der einzige Treiber mit Verzögerung ist eine Probe im selben Objekt. Das Gefechtssetup der Shell baut die Partie direkt (`LiveMatch.Create`); es gibt keinen Raum, dem jemand beitreten könnte.

## Entscheidungen, die feststehen (`docs/MULTIPLAYER-PLAN.md` §2)

- **Ein Matchpfad.** Das lokale Gefecht (Mensch gegen KI) läuft über dasselbe Modul wie ein Netzwerkmatch: eingebetteter Server, Loopback-Transport, ein Client. Der alte direkte Weg wird am Ende dieser Phase entfernt.
- **Relay.** Der Server ordnet Befehlsabsichten Ticks zu und verteilt Bündel; er simuliert nicht und braucht keine Spieldaten.
- **KI-Slots** stehen in der Match-Beschreibung; jeder Client lässt die KIs in seiner Simulation mitlaufen (Phase 24). Der Server weiß von einer KI nur, dass der Slot belegt ist.
- **Neue Projekte und ihre Abhängigkeiten** (Namen dürfen abweichen, die Regeln nicht):
  - `src/CnCNew.Net` – Nachrichten, Kodierung, `ITransport`, Loopback-Transport, Lockstep-Treiber des Clients. Referenziert nur `CnCNew.Simulation` (Befehls-Codec, Match-Beschreibung, Replay). Kein Stride, kein `CnCNew.Content`, kein `CnCNew.Game`.
  - `src/CnCNew.Server` – Raum- und Relay-Logik als Bibliothek. Referenziert nur `CnCNew.Net`. Lädt nie Spieldaten.
  - `tests/CnCNew.Net.Tests`, `tools/CnCNew.NetTool`.
  - `CnCNew.Game` referenziert `CnCNew.Net` und `CnCNew.Server` (für den eingebetteten Server). `CnCNew.Simulation` referenziert weiterhin nur `CnCNew.Data`.
  - Ein Quelltext-/Referenztest erzwingt diese Regeln und dass Wandzeit in `CnCNew.Server`/`CnCNew.Net` nur über eine injizierte Uhr gelesen wird.

## Auftrag (in dieser Reihenfolge)

1. **Ausgangszustand reproduzieren**, Referenzwerte der Phase 24 neu messen.
2. **Simulationsversion.** Zwei Clients mit denselben Daten, aber verschiedenem Simulationscode laufen sicher auseinander. Eine Kennung der Simulation (beim Build eingebetteter Commit-Hash samt „verändert“-Kennzeichen als Assembly-Metadatum, zusätzlich die Formatversionen) gehört in Match-Beschreibung und Handshake. Lokale Entwicklungsstände mit „verändert“ dürfen nur über Loopback spielen, außer ein ausdrücklicher Entwicklerschalter erlaubt es (gezählt, im Protokoll sichtbar).
3. **Protokoll (`docs/NETWORK-PROTOCOL.md` zuerst schreiben, dann bauen).** Versionierte binäre Nachrichten, Little Endian, längenpräfigiert, jede mit Obergrenze:
   - Handshake: Protokollversion, Simulationsversion, `CommandTypes.Version`, Replay-Format, Definitions-Fingerabdruck; Ablehnung mit Ursache.
   - Raum: Zustand des Raums (maßgeblich ist immer der Server), Slotwunsch, Bereit, Start mit `MatchDescription` (Startwert vom Server gewählt), Ladefortschritt, „alle geladen“, Chat.
   - Match: Befehlsabsicht (Quittungsnummer des Clients, Nutzlast im Befehls-Codec der Simulation), Tickbündel (Tick, Befehle mit vom Server gestempelter Spieler-ID und Sequenz, auch leer), Hashbericht (Tick, Sitzungshash), Pause/Fortsetzen, Spieler ausgeschieden, Desync, Ende.
   - Unbekannte oder zu große Nachrichten werden gezählt und verworfen; nichts fällt still auf einen Standard zurück.
4. **Transport-Schnittstelle und Loopback.** `ITransport` liefert Nachrichten zuverlässig und geordnet je Verbindung. `LoopbackTransport` im Prozess mit einstellbarer Laufzeit, Schwankung und Umsortierung zwischen *verschiedenen* Verbindungen, aus einem eigenen Test-Startwert (niemals der Logik-Zufallsstrom).
5. **Relay- und Raumlogik (`CnCNew.Server`).** Zustandsautomat Raum → Laden → Match → Ende. Tickuhr über eine injizierte Uhr; zwei Betriebsarten:
   - **servergetaktet** (mehr als ein Mensch): der Server schließt Ticks nach seiner Uhr mit Tickrate × Geschwindigkeit und wartet auf niemanden;
   - **clientgetaktet** (genau ein Mensch, lokales Gefecht): der Tick wird geschlossen, sobald der eine Client ihn anfordert. Damit hat das Einzelspieler-Gefecht keine zusätzliche Verzögerung, und Pause, Spielgeschwindigkeit und der schnelle Vorlauf der Tests bleiben möglich.
   Der Server stempelt die Spieler-ID aus der Verbindung (niemand kann für einen anderen Befehle senden), begrenzt Absichten je Tick und Spieler, vergleicht Hashberichte und schreibt alle Bündel mit.
6. **Lockstep-Treiber des Clients (`ITickDriver` aus Phase 24).** Sammelt Bündel, gibt Tick N frei, wenn Bündel N vorliegt, reicht dessen Befehle unmittelbar vor dem Schritt ein, sendet Hashberichte, ordnet Ergebnisse den Quittungsnummern zu. In dieser Phase fester Abspielpuffer (einstellbar); die adaptive Regelung kommt in Phase 26.
7. **Lokales Gefecht über den Netzpfad.** `LiveMatch` startet eingebetteten Server, Loopback und Treiber. Das Gefechtssetup der Shell wird der Raum (lokal, ein Mensch): Slots mit Fraktion, Team, Farbe, Startposition, KI-Slots – alles wandert über die Match-Beschreibung in die Partie. Speichern/Laden (Phase 24) bleibt für das lokale Gefecht erhalten: ein Raum kann aus einem Speicherstand starten (Beschreibung + Sitzungssnapshot + Hash).
8. **Mehr-Client-Probe im selben Prozess.** `tools/CnCNew.NetTool loopback-match --map … --humans H --ais A --ticks T [--delay … --jitter …]`: H Clients mit je eigener Welt gegen einen Server über Loopback. Die „Menschen“ sind Befehlsgeber: eine `SkirmishAi`, die nur auf diesem einen Client rechnet und ihre Befehle als Absichten über den Server schickt – so wird der echte Befehlsweg belastet. Nach jedem Tick werden die Sitzungshashes aller H Welten verglichen.
   - Läufe: 2, 4 und 8 Clients; Mischungen 1+7, 2+6, 4+4, 8+0 (Menschen+KI) auf mindestens drei 8-Spieler-Karten; mit und ohne Laufzeit/Schwankung; je zweimal (Wiederholbarkeit).
   - Jeder Lauf: alle Hashes identisch über alle Ticks, Server-Mitschrift als Replay abspielbar mit demselben Endhash.
9. **Alten Weg entfernen.** `SkirmishSessionCommandSink` mit direktem Zugriff und jeder zweite Startweg einer Partie entfallen; Werkzeuge (`SimTool ai-match`, `reference-match`, `skirmish-sweep`) dürfen headless ohne Server direkt auf der Sitzung laufen (sie sind die Referenz), der Client nicht.
10. **Doku:** `docs/NETWORK-PROTOCOL.md` (neu), `docs/MULTIPLAYER-PLAN.md`, `docs/GAME-SHELL.md`, `docs/ARCHITECTURE.md` (neue Schichten und Regeln), README, ROADMAP, IMPLEMENTATION-PLAN, `CLAUDE.md` (Schichtenregeln, Befehle, Referenzwerte).

## Engstellen und Showstopper dieser Phase

- **Einreichungszeitpunkt.** Wartende Befehle gehen in den Hash ein; ein Bündel darf erst unmittelbar vor seinem Tick in die Welt (Test: Bündel kommen bei verschiedenen Clients zu verschiedenen Zeiten an, Hashes bleiben gleich).
- **Reihenfolge innerhalb eines Bündels.** Die Welt führt Befehle eines Ticks in fester Reihenfolge aus (Spieler, Sequenz); KI-Befehle (lokal erzeugt) und Menschenbefehle (aus dem Bündel) müssen auf jedem Client in derselben Gesamtreihenfolge landen. Regel festlegen und testen (ein Mensch und eine KI befehlen im selben Tick dasselbe Objekt bzw. konkurrieren um dasselbe Geld).
- **Start bei Tick 0.** Alles, was vor dem ersten Tick geschieht (Startobjekte, Lagerhäuser, zufällige Fraktion/Position), muss allein aus der Match-Beschreibung folgen – kein Client-Zustand, keine Aufzählungsreihenfolge, keine Wandzeit.
- **Lokales Gefecht darf nicht schlechter werden.** Messung vorher/nachher: Zeit von Befehlsabsicht bis Ausführung im clientgetakteten Betrieb (Soll: derselbe Tick wie vor dieser Phase), Tickkosten mit und ohne Netzpfad.

## Nicht Gegenstand

Echte Sockets, Zeitabgleich, adaptive Puffer, eigenständiger Serverprozess (Phase 26); Wiedereinstieg, Zuschauer (Phase 27); Oberfläche für Beitritt über das Netz (Phase 29).

## Abnahme

Das lokale Gefecht läuft ausschließlich über `CnCNew.Net`/`CnCNew.Server`, mit Referenzwerten identisch zu Phase 24 (clientgetaktet, ohne Verzögerung). Die Mehr-Client-Probe ist für alle genannten Mischungen hashgleich. Die Abhängigkeitsregeln sind durch einen Test erzwungen; der Server läuft ohne Spieldaten. Der Abschlussbericht nennt Zahlen mit Commit-Hashes und was nicht belegt ist. Am Ende `docs/HANDOFF-PHASE26.md` prüfen und die „Ausgangslage“ aktualisieren.
