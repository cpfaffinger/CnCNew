N+1-Handoff (Phase 28): Serverbetrieb und Sicherheit – offener Port, Missbrauchsgrenzen, Verschlüsselung, Paket, Dauerlast

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`. Lies zuerst `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, `docs/NETWORK-PROTOCOL.md`, `docs/DEDICATED-SERVER.md`, `docs/NETWORK-DIAGNOSTICS.md` und den Abschlussbericht der Phase 27.
Voraussetzung: Phase 27 ist abgeschlossen.

## Ehrlicher Status (Ausgangslage)

Der Dedicated Server funktioniert unter gutwilligen Bedingungen. Ein Programm mit offenem UDP-Port im Internet wird aber von Fremden angesprochen, bevor der erste Spieler kommt. Bisher gibt es: einen Cookie-Handshake und einen strikten Parser (Phase 26). Es fehlt: jede Begrenzung gegen Überlast, jede Authentisierung der Pakete nach dem Handshake, jeder Schutz eines Raumpassworts, ein Paket, das jemand ohne Entwicklungsumgebung starten kann, und jede Aussage darüber, wie viele Räume ein Server trägt.

## Entscheidungen, die feststehen

- **Kein Anti-Cheat gegen Kartenaufdeckung.** Im Lockstep-Modell kennt jeder Client den vollen Zustand; das ist dokumentierte Grenze des Modells (`docs/MULTIPLAYER-PLAN.md` §2), kein Auftrag dieser Phase. Was der Server leisten kann: kein Client kann für einen anderen Spieler Befehle senden (Spieler-ID vergibt der Server nach Verbindung), die Spielgeschwindigkeit bestimmt der Server, ungültige Befehle lehnt jede Simulation gleich ab, veränderter Zustand fällt als Desync auf.
- **Nur BCL-Kryptografie** (`ECDiffieHellman`, `AesGcm` bzw. `ChaCha20Poly1305`, `HMACSHA256`). Keine eigene Kryptografie, keine neue Abhängigkeit.
- **Der Server verteilt keine Originaldaten** und keine Karten. Ein Raum nennt Karte und Fingerabdrücke; wer sie nicht hat, kann nicht beitreten.

## Auftrag (in dieser Reihenfolge)

1. **Ausgangszustand reproduzieren**, Referenzwerte der Phase 27 neu messen.
2. **Bedrohungsmodell schreiben** (`docs/DEDICATED-SERVER.md`, eigener Abschnitt): Angreifer ohne Verbindung (Flut, gefälschte Absender, Verstärkung), Angreifer mit Verbindung (Nachrichtenflut, übergroße oder fehlerhafte Nachrichten, Lobby-Missbrauch, absichtliches Stocken), Mitleser im Netz. Je Zeile: Maßnahme, Test, Restrisiko.
3. **Grenzen und Drosselung.** Pakete je Sekunde und Absender vor dem Handshake; Nachrichten und Bytes je Sekunde je Verbindung; Befehlsabsichten je Tick und Spieler; maximale Nachrichten- und Fragmentgrößen; Verbindungen je Adresse; Räume und Spieler je Server; Speicherobergrenze je Raum (Bündelpuffer für Zuschauer und Wiedereinstieg). Jede Grenze ist Konfiguration mit dokumentiertem Standard; jede Überschreitung wird gezählt und führt zu einer definierten Reaktion (verwerfen, trennen), nie zu unbegrenztem Speicher- oder Rechenaufwand.
4. **Fuzzing des Parsers.** Ein Testprogramm (`tests/CnCNew.Net.Tests`, Suite `Fuzz`) schickt aus einem Startwert erzeugte zufällige und gezielt verfälschte Pakete (abgeschnitten, Längenfelder über der Grenze, vertauschte Typen, alte Sequenzen, Wiederholungen) gegen Server- und Clientparser: keine Ausnahme verlässt den Parser, kein Zustand ändert sich durch ein ungültiges Paket, der Speicher wächst nicht. Ein Korpus der gefundenen Fälle bleibt als Regressionstest.
5. **Authentisierte und verschlüsselte Verbindung.** Sitzungsschlüssel je Verbindung aus einem ECDH-Austausch im Handshake; alle Pakete danach mit AEAD (Sequenznummer als Nonce, Schutz gegen Wiedereinspielen). Raumpasswort nur als Challenge-Response (`HMACSHA256` über eine Server-Nonce), nie im Klartext. Der Server besitzt ein dauerhaftes Schlüsselpaar; der Client zeigt beim ersten Kontakt dessen Fingerabdruck und merkt ihn sich (Vertrauen beim ersten Kontakt). Ehrlich dokumentieren, wogegen das schützt (Mitlesen, Fälschen von außerhalb des Pfads) und wogegen nicht (aktiver Angreifer beim allerersten Kontakt).
6. **Verwaltung.** Konfigurationsdatei, Sperrliste (Adresse/Schlüssel), Raumpasswort, Hinauswerfen durch den Raumersteller; Protokoll als strukturierte Zeilen mit einstellbarer Aufbewahrung. IP-Adressen sind personenbezogene Daten: Standard ist gekürzte Speicherung, die Doku nennt das.
7. **Paket.** `dotnet publish` des Servers eigenständig (self-contained) für `win-x64` und `linux-x64`; der Server hat keine Windows- oder Stride-Abhängigkeit (Test über die Projektreferenzen). Start- und Konfigurationsanleitung in `docs/DEDICATED-SERVER.md`. Linux-Lauf belegen (WSL oder Container genügt; nicht behaupten, wenn er nicht stattfand).
8. **Kapazität und Dauerlast.** Headless-Clients (Befehlsgeber aus Startwert) füllen N Räume zu je acht Spielern: CPU, Arbeitsspeicher und Bandbreite je Raum messen, daraus eine Kapazitätstabelle; 24-Stunden-Lauf mit ständig neu startenden Matches, Trennungen und Wiedereinstiegen: kein Speicherwachstum, kein Absturz, kein hängender Raum. Der Lauf braucht einen begrenzten Timeout und Zwischenstände (siehe `CLAUDE.md`, Hintergrundläufe).
9. **Versionsregel im Betrieb.** Was beim Beitritt übereinstimmen muss (Protokollversion, `CommandTypes.Version`, Replay-Format, Simulationsversion aus Phase 25, Definitions- und Karten-Fingerabdruck) und wie der Client eine Abweichung dem Menschen erklärt, steht in der Doku; der Server bietet nie einen „trotzdem beitreten“-Weg an.
10. **Doku:** `docs/DEDICATED-SERVER.md`, `docs/NETWORK-PROTOCOL.md`, `docs/MULTIPLAYER-PLAN.md`, README, ROADMAP, IMPLEMENTATION-PLAN, `CLAUDE.md`; `scripts/audit-vulnerable.ps1` um das Serverprojekt erweitern.

## Engstellen und Showstopper dieser Phase

- **Kosten der Verschlüsselung** je Paket bei acht Clients und 60 Bündeln je Sekunde: messen, nicht annehmen.
- **Nonce-Wiederverwendung** bei Neuübertragung: jede Übertragung braucht eine frische Paketsequenz, auch wenn der Inhalt derselbe ist (Test).
- **Zeitgrenzen als Angriffsfläche:** ein Client, der genau unter jeder Grenze bleibt, darf einen Raum nicht dauerhaft aufhalten (Test „böswillig langsamer Client“ gegen die Regel aus Phase 26).

## Nicht Gegenstand

Rangliste, Konten, Spielersuche, zentrale Serverliste, NAT-Durchdringung (Clients verbinden sich ausgehend zu einem Server mit offenem Port), Kernel-Anti-Cheat. Wer das will, entscheidet es als eigene Scope-Änderung.

## Abnahme

Der Fuzz-Lauf findet keinen Absturz und kein Speicherwachstum; die Grenzen greifen nachweislich; die Verbindung ist authentisiert und verschlüsselt; der Server läuft als eigenständiges Paket unter Windows und Linux; die Kapazitätstabelle und der 24-Stunden-Lauf sind gemessen. Der Abschlussbericht nennt Zahlen mit Commit-Hashes, das Restrisiko je Zeile des Bedrohungsmodells und was nicht belegt ist. Am Ende `docs/HANDOFF-PHASE29.md` prüfen und die „Ausgangslage“ aktualisieren.
