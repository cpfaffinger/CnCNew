N+1-Handoff (Phase 26): Dedicated Server und echter Transport – UDP, Zeitabgleich, adaptive Puffer, acht Clients über das Netz

Du arbeitest autonom an diesem Repository (`<repo>` = Wurzel des Arbeitsverzeichnisses). Lies zuerst `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`** (Entscheidungen, Zielarchitektur, Register der Engstellen, Regeln für alle Phasen des Programms), `docs/ARCHITECTURE.md`, `docs/NETWORK-PROTOCOL.md` (entsteht in Phase 25) und den Abschlussbericht der Phase 25.
Voraussetzung: Phase 25 ist abgeschlossen – ein lokales Skirmish läuft ausschließlich über `CnCNew.Net`/`CnCNew.Server` mit dem Loopback-Transport, und der Mehr-Client-Test im selben Prozess (2, 4, 8 Clients, KI-Slots gemischt) liefert auf allen Clients tickweise identische Hashes. Ist das nicht belegt, beginnt diese Phase nicht.

## Ehrlicher Status (Ausgangslage)

Nach Phase 25 existiert das Protokoll, aber noch **kein Byte hat einen Rechner verlassen**. Alles lief über Warteschlangen im selben Prozess mit einer von Hand gestellten Uhr. Unbelegt sind deshalb: das Verhalten unter Laufzeit, Schwankung und Paketverlust, der Zeitabgleich zwischen Server und Client, der Bandbreitenbedarf, das Verhalten eines Clients, der die Tickrate nicht hält, und ob acht echte Prozesse über Stunden synchron bleiben. Die Latenzzahlen in `docs/MULTIPLAYER-PLAN.md` §3 sind gerechnet, nicht gemessen.

## Entscheidungen, die feststehen (`docs/MULTIPLAYER-PLAN.md` §2)

- Der Server ist ein **Relay**: er simuliert nicht, lädt keine Spieldaten und wartet beim Schließen eines Ticks auf keinen Client.
- Transport ist **UDP mit eigener Zustellsicherung**, ausschließlich mit der BCL (`System.Net.Sockets`). Keine neue NuGet-Abhängigkeit ohne belegte Begründung.
- Wandzeit existiert nur in `CnCNew.Net`/`CnCNew.Server` (Taktung, Zeitabgleich). Sie erreicht die Simulation nie; die Simulation wird allein von Tickbündeln getrieben.

## Auftrag (in dieser Reihenfolge)

1. **Ausgangszustand reproduzieren.** Build, alle Testprogramme, die Referenzwerte der Phase 25 (lokales Skirmish über den Netzpfad, Mehr-Client-Loopback) neu messen und festhalten.
2. **UDP-Transport (`CnCNew.Net`, hinter `ITransport`).**
   - Verbindungsaufbau mit zustandslosem Cookie: der Server legt erst Zustand an, wenn der Client den Cookie zurückgeschickt hat (kein Verstärkungsangriff, kein Speicher für gefälschte Absender).
   - Zwei Kanäle über eine Verbindung: *zuverlässig geordnet* (Lobby, Steuerung, Befehlsabsichten) mit Sequenz, Bestätigung und Neuübertragung; *dringend* für Tickbündel: jedes Paket trägt alle Bündel, die der Empfänger noch nicht bestätigt hat (begrenzt, gezählt), sodass ein einzelner Verlust keine Wartezeit kostet.
   - Fragmentierung großer Nachrichten (Match-Beschreibung, später Snapshot) mit Größenobergrenze; MTU-sichere Paketgröße (Annahme 1.200 Byte Nutzlast, in der Doku begründen).
   - Lebenszeichen, Zeitüberschreitung, sauberes Trennen. Jede Zahl (Wiederholintervall, Zeitgrenzen) ist Konfiguration mit dokumentiertem Standard.
   - Der Parser ist strikt: jedes Feld mit Längen- und Wertebereichsprüfung, unbekannte Nachrichtentypen werden gezählt und verworfen, nie stillschweigend interpretiert.
3. **Netzsimulator für echte Sockets.** `tools/CnCNew.NetTool proxy`: ein UDP-Zwischenstück mit einstellbarer Laufzeit, Schwankung, Verlust, Verdopplung und Umsortierung je Richtung, aus einem Startwert reproduzierbar. Damit laufen alle Messungen dieser Phase; ein Fremdwerkzeug ist nicht nötig.
4. **Zeitabgleich und Taktung.**
   - Der Client schätzt den Versatz zur Tickuhr des Servers (Ping/Pong, gleitender Median) und spielt Tick N ab, sobald Bündel N vorliegt *und* sein Abspielzeitpunkt erreicht ist.
   - Der Abspielpuffer je Client ist adaptiv: Zielgröße aus der gemessenen Ankunftsschwankung (z. B. 99-%-Quantil), in Millisekunden geführt, nicht in ganzen Ticks. Korrekturen durch leichtes Beschleunigen oder Bremsen der Abspielrate (höchstens wenige Prozent), nie durch Sprünge.
   - Aufholen: ein Client mit Rückstand rechnet mehrere Ticks je Bild (Grenze aus Phase 23/24), bis er den Sollabstand erreicht.
5. **Dedicated Server als Programm (`tools/CnCNew.DedicatedServer`).** Ein Prozess, ein UDP-Port, mehrere gleichzeitige Räume (Lobby + Match), Verbindungen werden dem Raum zugeordnet. Konfiguration aus einer JSON-Datei und Befehlszeile (Port, maximale Räume, maximale Spieler je Raum = 8, Tickrate-Vorgabe, Zeitgrenzen). Konsolenausgabe als strukturierte Zeilen. Der Server referenziert weder `CnCNew.Content` noch `CnCNew.Game`, `CnCNew.Presentation` oder Stride; ein Quelltext-/Referenztest erzwingt das (Vorbild `SourceRules`).
6. **Regel für langsame und getrennte Clients.** Der Server schließt Ticks nach seiner Uhr. Für einen Client, der zurückfällt oder die Verbindung verliert, gilt eine dokumentierte, konfigurierbare Regel (Vorschlag als Standard, die Entscheidung liegt beim Projektinhaber, `docs/MULTIPLAYER-PLAN.md` §7): Rückstand bis 2 s nur anzeigen; darüber Spielgeschwindigkeit für alle bis zu 10 s drosseln; danach Trennung. Das Ausscheiden eines Spielers ist ein **Systembefehl im Tickbündel** (neuer Befehlstyp, `CommandTypes.Version` erhöhen), den jede Simulation identisch ausführt; was mit seinen Objekten geschieht, steht dort fest und ist getestet.
7. **Messungen (jede Zahl aus einem Lauf, ruhige Maschine).**
   - Acht echte Clientprozesse (headless, Befehle aus einem Startwert-gesteuerten Befehlsgeber) gegen den Serverprozess über den Proxy, für jede Zeile der Matrix: Laufzeit 10/40/80/150 ms einfach, Schwankung 0/10/30 ms, Verlust 0/1/3 %. Ergebnis je Zeile: Hashes aller Clients identisch (ja/nein), Zeit von Befehlsabsicht bis Ausführung (Median, 95 %, 99 %), Pufferstand, Anzahl Stockungen, Bytes je Sekunde und Client, Neuübertragungen.
   - Dauerlauf: acht Clients, 60 Minuten Spielzeit, 40 ms / 10 ms / 1 %: kein Desync, kein Speicherwachstum.
   - Zwei Rechner im LAN (braucht den Menschen und einen zweiten Rechner; nicht behaupten, wenn es nicht stattfand).
   - Vergleich mit der Rechnung in `docs/MULTIPLAYER-PLAN.md` §3: Abweichungen benennen.
8. **Doku:** `docs/NETWORK-PROTOCOL.md` (Paketformat, Kanäle, Zustandsautomaten, alle Zeitgrenzen), `docs/DEDICATED-SERVER.md` (Start, Konfiguration, Regeln), `docs/MULTIPLAYER-PLAN.md` (Register und Messwerte nachführen), README, ARCHITECTURE, ROADMAP, IMPLEMENTATION-PLAN, `CLAUDE.md` (Befehle, Referenzwerte).

## Engstellen und Showstopper dieser Phase

- **Hash umfasst wartende Befehle.** `SimulationWorld.Submit` zählt jeden angenommenen Befehl sofort in den Zustands-Hash (`_commandSum`). Ein Client darf die Befehle eines Bündels deshalb erst unmittelbar vor dem Schritt seines Ticks einreichen, nie beim Eintreffen. Der Test „Bündel treffen in beliebiger Reihenfolge und zu beliebigen Zeiten ein, Hashes bleiben identisch“ ist Pflicht.
- **Garbage Collection und Bildrate.** Ein Client, der wegen einer Speicherbereinigung oder eines langen Bildes stockt, darf niemanden bremsen; er holt auf. Das Aufholtempo aus Phase 23 ist die harte Grenze dafür.
- **Uhrendrift.** Zwei Rechner laufen nicht gleich schnell; die Abspielregelung muss Drift ausgleichen, ohne zu schwingen (Test mit künstlich verstellter Uhr).
- **Betriebssystem-Taktung.** `Thread.Sleep` hat unter Windows grobe Auflösung; die Tickuhr des Servers braucht eine dokumentierte, gemessene Lösung (Abweichung der Tickabstände als Histogramm im Bericht).

## Nicht Gegenstand

Wiedereinstieg, Zuschauer und Desync-Diagnose (Phase 27); Absicherung und Betrieb des offenen Ports (Phase 28); Oberfläche (Phase 29). Verschlüsselung gehört zu Phase 28, der Cookie-Handshake aber schon hierher.

## Abnahme

Acht Clientprozesse spielen über den Dedicated Server ein vollständiges Match mit gemischten Menschen- und KI-Slots; alle Hashes sind über die ganze Matrix identisch; die Latenztabelle ist gemessen; ein langsamer und ein getrennter Client bremsen die übrigen nicht über die dokumentierte Regel hinaus. Der Abschlussbericht nennt Zahlen mit Commit-Hashes und ausdrücklich, was nicht belegt ist (insbesondere jeder Test, der einen zweiten Rechner oder einen Menschen gebraucht hätte). Am Ende `docs/handoffs/HANDOFF-PHASE27.md` prüfen und den Abschnitt „Ausgangslage“ mit dem gemessenen Stand aktualisieren, nicht neu schreiben.
