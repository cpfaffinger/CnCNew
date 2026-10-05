N+1-Handoff (Phase 29): Mehrspieler im Client – Shell, Lobby, Anzeigen im Spiel, Latenzwahrnehmung, Abnahme mit Menschen

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`. Lies zuerst `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, `docs/GAME-SHELL.md`, `docs/GAME-HUD.md`, `docs/PRESENTATION-INPUT.md`, `docs/PRESENTATION-OBJECTS.md`, `docs/NETWORK-PROTOCOL.md`, `docs/DEDICATED-SERVER.md` und den Abschlussbericht der Phase 28.
Voraussetzung: Phase 28 ist abgeschlossen. Diese Phase ist die erste des Programms, deren Abnahme **ohne Menschen nicht möglich** ist.

## Ehrlicher Status (Ausgangslage)

Alles Bisherige ist headless belegt. Ein Mensch kann ein Netzwerkmatch noch nicht starten: der Knopf „Mehrspieler“ im Hauptmenü ist sichtbar, aber deaktiviert (`docs/GAME-SHELL.md`). Das Gefechtssetup zeigte am 5. Oktober 2026 Team- und Farbauswahl, gab sie aber nicht weiter (`MatchSetup` kannte nur Karte, eigene Fraktion und Gegnerliste); die Phasen 24 und 25 haben die Match-Beschreibung um Teams, Farben und Startpositionen erweitert und das Setup zum lokalen Raum gemacht – zuerst prüfen, was davon in der Oberfläche wirklich ankommt. Im Spiel gibt es keine Anzeige für Verbindungszustand, wartende Spieler, ausgeschiedene Spieler oder einen Desync.

Vorhandene Original-Layouts im importierten Bestand (`Window/Menus/`, nur lesen, nichts ins Repo): `LanLobbyMenu.wnd`, `LanGameOptionsMenu.wnd`, `LanMapSelectMenu.wnd`, `NetworkDirectConnect.wnd`, `DisconnectScreen.wnd`, `ObserverQuit.wnd` sowie die GameSpy-/WOL-Layouts (nicht verwenden: kein GameSpy im Scope).

Zur Latenz: der Befehl eines Spielers wird erst im Tick ausgeführt, den der Server zuteilt. Die Verzögerung ist in Phase 26 gemessen. Wie sie sich *anfühlt*, hängt davon ab, was der Client sofort zeigt – und das ist bisher an die Ausführung gekoppelt (Bestätigungston, Zielmarke und Ablehnung kamen mit der synchronen Antwort der Simulation; Phase 24 hat den Weg zweistufig gemacht).

## Auftrag (in dieser Reihenfolge)

1. **Ausgangszustand reproduzieren**, Referenzwerte neu messen; mit dem Menschen klären, wann er für die Abnahme verfügbar ist und ob ein zweiter Rechner bereitsteht.
2. **Shell.**
   - Hauptmenü → Mehrspieler → Direktverbindung (`NetworkDirectConnect.wnd`: Adresse, Port, Name, Passwort; Fingerabdruck des Servers beim ersten Kontakt) → Raumliste des Servers → Raum (`LanLobbyMenu.wnd`/`LanGameOptionsMenu.wnd`: Slots Mensch/KI/offen/geschlossen/Zuschauer, Fraktion, Team, Farbe, Startposition auf der Kartenvorschau, Karte, Startgeld, Bereit, Start durch den Raumersteller, Chat) → gemeinsamer Ladeschirm mit Fortschritt aller Spieler → Match → Ergebnis für alle Spieler → zurück in den Raum.
   - „Spiel hosten“ startet den eingebetteten Server im Client (derselbe Code wie der Dedicated Server) und öffnet denselben Raum.
   - Das lokale Gefecht bleibt derselbe Raum mit einem Menschen; es gibt keinen zweiten Setup-Bildschirm mit eigener Logik.
   - Jede Abweichung beim Beitritt (Version, Fingerabdruck, voll, Passwort) wird dem Menschen mit Ursache gezeigt; jeder Fall ist gezählt und getestet. Fehlende Zeichenketten fallen nie still auf einen Standard zurück.
   - Golden-Hashes der neuen Screens in allen sechs Auflösungen wie bei den bestehenden (`docs/GAME-SHELL.md`).
3. **Anzeigen im Spiel.** Verbindungsqualität (Laufzeit, Puffer, Aufholen), „warte auf Spieler“ mit Namen und Restfrist, Spieler ausgeschieden/wieder da, Pause mit Anforderer, Desync-Bildschirm mit Hinweis auf das Diagnosepaket (`DisconnectScreen.wnd`), Chat an alle/Verbündete, Zuschaueransicht (kein eigener Spieler: Nebel wählbar je Spieler oder aufgedeckt, keine Befehle, `ObserverQuit.wnd`). `--net-stats` schreibt dieselben Werte in die Konsole.
4. **Latenzwahrnehmung.**
   - **Sofortige lokale Rückmeldung:** Auswahl, Mauszeiger, Zielmarke, Wegpunktlinie, Bestätigungsstimme und Platzier-Vorschau reagieren im Bild des Klicks. Die lokale Vorprüfung (Phase 24) entscheidet, ob sofort „geht nicht“ kommt; die endgültige Antwort der Simulation kommt mit der Ausführung und korrigiert nur im seltenen Abweichungsfall (gezählt).
   - **Mehr Interpolation:** `FrameProjector` interpoliert heute nur Position und Drehung des Rumpfes. Turmwinkel, Rohrneigung, Baufortschritt, Lebensbalken und Federung zwischen zwei Ticks interpolieren, damit die Darstellung bei jeder Bildrate gleichmäßig ist.
   - **Darstellungsverzug:** die Interpolation zeigt den Zustand bis zu einem Tick verspätet. Messen, ob eine Extrapolation eigener, sich bewegender Einheiten (ein Tick voraus, mit Rücknahme bei Abweichung) spürbar hilft; nur einbauen, wenn die Messung und der Mensch es bestätigen, und abschaltbar halten.
   - **Messung:** Zeit von Klick bis Ausführung und von Klick bis erste sichtbare Reaktion als Histogramm im Client (`--net-stats`), für 0/20/40/80 ms Laufzeit über den Proxy, vor und nach diesem Punkt.
5. **Abnahme mit Menschen** (Checkliste in `docs/MULTIPLAYER-FINDINGS.md`, Form wie `docs/DEMO-FINDINGS.md`: je Zeile „ging / ging nicht / ging mit Mängeln“ und Ursache):
   - ein Mensch gegen KI über den eingebetteten Server (das neue lokale Gefecht) – fühlt es sich an wie vorher oder besser?
   - zwei Menschen im LAN über „Spiel hosten“;
   - zwei bis acht Menschen über einen Dedicated Server im Internet, mit KI-Slots gemischt;
   - Trennen und Wiedereinstieg, Zuschauer, Pause, ein absichtlich langsamer Rechner.
   Jeder Befund wird reproduziert, auf seine Ursache zurückgeführt und behoben oder mit Grund benannt.
6. **Doku:** `docs/GAME-SHELL.md`, `docs/GAME-HUD.md`, `docs/PRESENTATION-INPUT.md`, `docs/PRESENTATION-OBJECTS.md`, `docs/MULTIPLAYER-FINDINGS.md`, `docs/MULTIPLAYER-PLAN.md` (Abschluss des Programms: was gemessen ist, was offen bleibt), README, ARCHITECTURE, ROADMAP, IMPLEMENTATION-PLAN, `CLAUDE.md`.

## Engstellen und Showstopper dieser Phase

- **Präsentation liest die laufende Welt.** HUD und Eingabe fragen die Simulation direkt ab (`SimulationQueries`, `CommandModeController`). Sofortige Rückmeldung darf nie einen Zustand zeigen oder verändern, den nur die Ausführung kennen kann; sie ist reine Darstellung und beeinflusst keinen Hash (bei jeder Messung belegen).
- **Stehende Befehle** (`StandingOrders`, Wegpunktfolgen) werden von der Präsentation Tick für Tick eingereicht; mit Verzögerung dürfen sie weder doppelt noch zu spät kommen (Test mit 6 Ticks Verzögerung).
- **Rauchtests beweisen keine Bedienbarkeit.** Kein Punkt dieser Phase gilt als erledigt, weil ein `--smoke-test` grün ist.

## Abnahme

Menschen haben die Zeilen der Checkliste abgearbeitet; die Befunde stehen in `docs/MULTIPLAYER-FINDINGS.md`; die Latenzhistogramme vor und nach Punkt 4 sind gemessen. Der Abschlussbericht nennt ausdrücklich, was kein Mensch gesehen hat. Am Ende `docs/HANDOFF-PHASE30.md` prüfen und die „Ausgangslage“ aktualisieren.
