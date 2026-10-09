# Programm Tickrate, Festkomma und Mehrspieler (Phasen 18–29)

Stand: 5. Oktober 2026 (Planung; nichts hiervon ist gebaut). Dieses Dokument ist der gemeinsame Rahmen der Aufträge `docs/HANDOFF-PHASE18.md` bis `docs/HANDOFF-PHASE29.md`. Jeder dieser Aufträge ist für sich bearbeitbar, setzt aber die Entscheidungen (§2) und Regeln (§6) dieses Dokuments voraus. Jede Phase schreibt ihre Messergebnisse hierher zurück (§4, §5).

## 1. Ziel und Scope-Änderung

Entscheidung des Projektinhabers vom 5. Oktober 2026:

1. **Mehrspieler ist im Scope.** Gefechte (Skirmish) mit bis zu acht Spielern laufen über **Dedicated Server**.
2. **Die Simulation läuft mit 60 Hz** statt 30 Hz. Die Tickrate ist ein Parameter der Sitzung, keine Konstante des Spiels; alles, was heute fest an 30 Hz hängt, wird entkoppelt.
3. **Ein Matchpfad.** Das Mehrspieler-Modul trägt auch das lokale Gefecht (Mensch gegen KI). Es muss deshalb KI-Spieler unterstützen.
4. **Alle bisherigen Simulationen und Abnahmetests** werden geprüft und, wo sie an 30 Hz oder an Hashes aus der Doku hängen, neu entworfen.
5. Der bisher nach Phase 17 geplante Abschluss (Patch-Integration, Qualität, Paket; `IMPLEMENTATION-PLAN.md` damals §10) rückt hinter dieses Programm und ist jetzt Phase 30.

Ziel dahinter: ein modernes Spiel – direkte Befehlsannahme, flüssige Bewegung, Netzwerkspiel, das nicht vom langsamsten Mitspieler abhängt.

**Weiterhin nicht im Scope** (jede Erweiterung ist eine eigene Entscheidung): vernetzte Kampagne oder Koop-Missionen; Konten, Rangliste, Spielersuche, zentrale Serverliste; NAT-Durchdringung und Verbindungen zwischen Spielern (Clients verbinden sich ausgehend zu einem Server mit offenem Port); Kompatibilität mit dem Netzprotokoll oder den Diensten des Originals (GameSpy/WOL); Schutz gegen Kartenaufdeckung; Übertragung eigener Karten; binäre Kompatibilität zu alten Mods und Savegames.

## 2. Entscheidungen und Zielarchitektur

### 2.1 Lockstep über einen Relay-Server

Die Simulation ist deterministisch und wird nur von Befehlen getrieben. Übertragen werden deshalb Befehle, kein Zustand. Jeder Client rechnet die volle Welt.

```
 Client A ──Befehlsabsicht──▶ ┌────────────────────┐ ──Tickbündel N──▶ Client A
 Client B ──Befehlsabsicht──▶ │ Server (Relay)     │ ──Tickbündel N──▶ Client B
   …                          │ Tickuhr, Zuordnung,│        …
 Client H ──Hashbericht─────▶ │ Hashvergleich,     │ ──Tickbündel N──▶ Client H
                              │ Mitschrift (Replay)│
                              └────────────────────┘
```

- Der Server führt die Tickuhr, ordnet jede eintreffende Befehlsabsicht dem nächsten offenen Tick zu und sendet je Tick ein Bündel an alle (auch ein leeres). **Er wartet beim Schließen eines Ticks auf niemanden.**
- Ein Client rechnet Tick N, sobald er Bündel N hat. Die Verzögerung eines Spielers hängt damit an seiner eigenen Verbindung, nicht an der des langsamsten.
- Der Server **simuliert nicht** und lädt **keine Spieldaten**. Das hält ihn billig (viele Räume je Kern), rechtlich sauber (keine aus Originaldaten erzeugten Bestände auf fremden Maschinen) und portabel (Windows und Linux).
- Verworfen: *Rollback* (Zustand mehrmals je Sekunde sichern und Ticks neu rechnen ist bei tausenden Objekten und dem heutigen Snapshot um Größenordnungen zu teuer); *autoritativer Server mit Zustandsübertragung* (gibt den Hauptvorteil der Architektur auf, verlangt Spieldaten auf dem Server). Ein *mitsimulierender* Server (kennt den richtigen Zustand, kann Snapshots liefern) bleibt eine spätere Option; nichts in den Phasen 24–28 darf ihn verbauen.

### 2.2 KI-Spieler laufen in der Simulation jedes Clients

Die KI wird Teil des deterministischen, gehashten und gespeicherten Sitzungszustands (Phase 24). Jeder Client lässt alle KIs mitlaufen; ihre Befehle werden nicht übertragen. Der Server weiß von einer KI nur, dass ihr Slot belegt ist.

- Dafür spricht: keine Latenz und keine Bandbreite für KI-Befehle; der Server bleibt ein Relay ohne Daten; Wiedereinstieg und Speichern funktionieren, weil der KI-Zustand im Snapshot steht; eine Abweichung im KI-Zustand fällt sofort im Hash auf; es ist das Modell des Originals.
- Dagegen steht: die KI-Rechenzeit fällt auf jedem Client an (Phase 19 und 24 messen und begrenzen sie).
- Verworfen: KI auf *einem* Client mit Übertragung ihrer Befehle (fällt mit diesem Client aus, braucht Übergabe); KI auf dem Server (verlangt Simulation und Daten dort).

### 2.3 Ein Matchpfad

```
 Shell/HUD/Eingabe ─▶ LiveMatch ─▶ Lockstep-Treiber ─▶ ITransport ─▶ Server-Logik
                          │         (CnCNew.Net)        Loopback      (CnCNew.Server)
                          ▼                              oder UDP      eingebettet oder
                     MatchSession (Welt + KI, CnCNew.Simulation)       eigener Prozess
```

Lokales Gefecht = eingebetteter Server + Loopback-Transport + ein Client, im Betrieb „clientgetaktet“ (der Tick schließt, sobald der eine Mensch ihn anfordert): keine zusätzliche Verzögerung, Pause und Spielgeschwindigkeit bleiben. Netzwerkmatch = derselbe Code mit UDP und „servergetaktet“. Missionen bleiben lokal, benutzen aber dieselben Schnittstellen für Befehlsweg und Tickquelle (Phase 24).

### 2.4 Schichten und Abhängigkeiten (ergänzt `CLAUDE.md`)

| Projekt | neu | darf referenzieren | darf nicht |
|---|---|---|---|
| `CnCNew.Simulation` | – | `CnCNew.Data` | alles andere; Wandzeit, Threads, `float` (unverändert) |
| `CnCNew.Net` | ja | `CnCNew.Simulation` (Befehls-Codec, Match-Beschreibung, Replay) | `CnCNew.Content`, `CnCNew.Game`, Präsentation, Stride |
| `CnCNew.Server` | ja | `CnCNew.Net` | Spieldaten laden, `CnCNew.Content`, `CnCNew.Game`, Präsentation, Stride |
| `tools/CnCNew.DedicatedServer` | ja | `CnCNew.Server` | wie oben |
| `CnCNew.Game` | – | zusätzlich `CnCNew.Net`, `CnCNew.Server` (eingebettet) | Stride (unverändert) |

Wandzeit gibt es nur in `CnCNew.Net`/`CnCNew.Server`, nur über eine injizierte Uhr, nur für Taktung und Zeitabgleich. Sie erreicht die Simulation nie. Netzwerk ausschließlich mit der BCL; jede neue NuGet-Abhängigkeit braucht eine belegte Begründung.

### 2.5 Tickrate

- Die Rate ist ein Wert der Welt (Phase 20). **Unterstützt sind 30, 60 und 120 Hz** (30 · 2ᵏ): Bildwerte des Originals skalieren exakt, multiplikative Faktoren brauchen nur Quadratwurzeln. Produktrate nach Phase 23: **60 Hz** für Gefecht und Kampagne. **Seit dem 8. Oktober 2026 (`e8632bb`) ist 60 Hz der Standard** – eine Definition, `ProductRate.TicksPerSecond` in `CnCNew.Game`; Client, Werkzeuge und Tests leiten davon ab, die Simulation selbst kennt keinen Standard.
- **Zeiten aus den Daten bleiben auf dem 30-Hz-Raster des Originals** (aufrunden auf Originalbilder, dann × Rate/30). Die Balance ist damit bei jeder Rate dieselbe wie im Original; die höhere Rate bringt feinere Bewegung und schnellere Befehlsannahme, keine anderen Feuerraten.
- Außerhalb der Simulation gibt es zwei getrennte Begriffe: die *Simulationsrate* (veränderlich) und das *Originalbild* von 1/30 s, in dem das Original Client-Inhalte verfasst hat (fest). Die Uhr der Präsentation ist Spielzeit in Sekunden (Phase 21).
- Golden-Hashes werden nach Phase 23 nur für 60 Hz gepflegt; 30 und 120 Hz bleiben lauffähig und werden über die Raten-Äquivalenz-Suite geprüft.

### 2.6 Festkomma

`Fixed` wechselt von Q47.16 auf **Q39.24** (Phase 22), allein, bei 30 Hz, vor der Umstellung der Rate. Begründung und Zahlen in `docs/HANDOFF-PHASE22.md`.

### 2.7 Kompatibilität beim Beitritt

Zwei Rechner dürfen nur gemeinsam spielen, wenn Protokollversion, Simulationsversion (Kennung des Codestands), `CommandTypes.Version`, Replay-Format, Definitions-Fingerabdruck, Karten-Dateihash und Tickrate übereinstimmen. Es gibt keinen „trotzdem beitreten“-Weg.

### 2.8 Was das Modell nicht kann

Jeder Client kennt den vollen Zustand; Kartenaufdeckung durch einen manipulierten Client ist im Lockstep nicht zu verhindern. Der Server verhindert, was er verhindern kann: Befehle unter fremder Spieler-ID, Manipulation der Spielgeschwindigkeit, veränderten Zustand (fällt als Desync auf).

## 3. Latenzmodell (gerechnet, nicht gemessen)

Überschlag für die Zeit von der Befehlsabsicht bis zur sichtbaren Wirkung bei einem Spieler mit 30 ms Laufzeit hin und zurück:

| Stufe | 30 Hz | 60 Hz |
|---|---|---|
| Laufzeit hin und zurück | 30 ms | 30 ms |
| Warten auf den nächsten offenen Tick (Mittel) | 17 ms | 8 ms |
| Abspielpuffer gegen Schwankung (1 Tick) | 33 ms | 17 ms |
| Darstellungsverzug der Interpolation (1 Tick) | 33 ms | 17 ms |
| Summe | rund 113 ms | rund 72 ms |

Die Tickrate verkürzt nur die drei Stufen, die in Ticks zählen (rund 40 ms Gewinn, unabhängig von der Laufzeit); bei 300 ms Laufzeit fällt das kaum ins Gewicht. Der größte Hebel für das *Empfinden* ist die sofortige lokale Rückmeldung (Phase 29). Gemessen wird in Phase 26 (Netz) und Phase 29 (Wahrnehmung); die Ergebnisse ersetzen diese Tabelle.

## 4. Register der Engstellen und Showstopper

Stand 5. Oktober 2026, belegt durch Lesen des Codes (`2a66661`), zwei Suchläufe und eine Probe-Messung. „Showstopper“ heißt: ohne Lösung funktioniert das Ziel nicht. Jede Phase schreibt ihr Ergebnis in die letzte Spalte.

| Nr. | Art | Befund | Beleg | Phase | Ergebnis |
|---|---|---|---|---|---|
| E1 | Showstopper | Determinismus ist nur auf einem Rechner belegt | alle bisherigen Vergleichsläufe | 18 | **teilweise belegt (Phase 18):** ein Rechner, 13 Zeilen der Matrix identisch, darunter Linux unter WSL2, Kulturen, JIT-/GC-Schalter (`docs/DETERMINISM.md` §2); **offen:** zweiter physischer Rechner mit anderer CPU, andere .NET-Version |
| E2 | Risiko | Knochentabelle und Kartenkoordinaten entstehen über `float`/`MathF`/`(decimal)float`; Dictionary-Aufzählung mit Nebenwirkung; kulturabhängiger Skript-Hash; Lücken in Fingerabdrücken und `SourceRules` | `docs/HANDOFF-PHASE18.md` D1–D9 | 18 | **behoben (Phase 18):** D1–D9 je mit Test bestätigt oder widerlegt und korrigiert (`docs/DETERMINISM-FINDINGS.md`); real waren D4 (Kultur), D5 (Fingerabdruck), D8 (fehlendes `MarkDirty` auf 19 von 45 Missionskarten), D9 (Zeilenenden: Linux lehnte Windows ab); Rest: eigenes Escaping des Fingerabdrucks, Kennung des Codestands (Phase 25) |
| E3 | Engstelle | Referenzwerte stehen nur in der Doku, nicht in Tests | `docs/TICKRATE-AUDIT.md` §7 | 18 | **behoben (Phase 18):** `baselines/reference-manifest.json` (50 Läufe) und `baselines/input-manifest.json`, `SimTool reference-manifest check` (`docs/SIMULATION-CORE.md` §37) |
| E4 | Showstopper | Echtes 8-Spieler-Match kostet rund 7–9 ms je Tick bei wenigen hundert Objekten (nicht ruhige Probe); bei 60 Hz rund ein halber Kern, Aufholtempo etwa 2-fach | `docs/HANDOFF-PHASE18.md` Punkt 4 | 18 (Profil), 19, 23 (Tor) | **vermessen (Phase 18, ruhig):** 8 Spieler 4,98 ms je Tick (99 %: 13,6 ms, Maximum 547 ms), bei 60 Hz 30 % eines Kerns und 3,4-faches Aufholtempo; `MD_GLA05` 12,6–13,5 ms, bei 60 Hz 76–81 % und 1,2-fach; 81 % der Update-Zeit `AIUpdateInterface`, ein Drittel Sicht und Hash (`docs/PERFORMANCE.md` §6); Abtragen in Phase 19 **Phase 19 (ruhig gemessen, alle Endhashes unverändert):** 8 Spieler 1,14 ms je Tick (99 %: 3,2 ms, Maximum 94 ms), bei 60 Hz 6,8 % eines Kerns und 16,2-faches Aufholtempo; `MD_GLA05` 2,9–3,4 ms (17–21 % eines Kerns bei 60 Hz). Sollwerte Mittel und 99 % knapp verfehlt, Maximum verfehlt (viele gleichzeitige lange Wegsuchen; nur mit Verhaltensänderung senkbar, `docs/PERFORMANCE.md` §6a.4). **Phase 23, Leistungstor bei 60 Hz (9. Oktober 2026, ruhig, zwei Läufe, `docs/PERFORMANCE.md` §6c):** 8 Spieler, 30 Spielminuten (108.000 Ticks): Mittel 683 / 694 µs (Soll ≤ 1,0 ms: erreicht), 99 % 1.615 / 1.793 µs (≤ 3 ms: erreicht), Aufholtempo 26,7- / 26,5-fach (≥ 8-fach: erreicht), 4,1–4,2 % eines Kerns; **Maximum 141 / 143 ms (≤ 8 ms: verfehlt** – ein Tick mit vielen gleichzeitigen langen Wegsuchen; die Verhaltensänderung, die es senkt, hat sich der Projektinhaber vorbehalten, §7a Nr. 3). `MD_GLA05` bei 60 Hz 2,1–2,4 ms je Tick, 13–14 % eines Kerns, Aufholen live 7- bis 8-fach. Bei gleicher Arbeit kostet ein Tick bei 30 Hz nach der Phase rund 5 % mehr (Stresstest). |
| E5 | Showstopper | Tickrate ist eine `const`; 61 Verwendungen, rund 110 nackte Tickzahlen in der Simulation, Kataloge rechnen beim Bau um | `docs/TICKRATE-AUDIT.md` §1–§3 | 20 | offen **erledigt für die Simulation (Phase 20, Stand `9299a2d`):** Rate als Wert der Welt, Kataloge je Rate, 262 + 188 + 11 Stellen umgestellt, Quelltextregel gegen nackte Tickzahlen; bei 30 Hz volles Manifest unter Windows und Linux identisch; bei 60/120 Hz deterministische Durchläufe ohne Überlauf, Verhalten unbewertet. Außerhalb der Simulation offen (Phase 21, 16 Punkte in `docs/TICKRATE-MIGRATION.md` §6). |
| E6 | Showstopper | Daten als rohe Bildwerte (Zug, Dämpfungen, Skript-Bildzähler, Ersatzwerte) | `docs/TICKRATE-AUDIT.md` §4 | 20, 23 | offen **Phase 20:** additive Bildwerte, Skript-Bildzähler und Ersatzwerte sind umgerechnet; multiplikative Faktoren, Integration je Tick, N+1-Zähler und Abtastrhythmen stehen unverändert auf der Liste für Phase 23 (95 Einträge, `docs/TICKRATE-MIGRATION.md` §5). **Phase 23 (9. Oktober 2026, Stand `f3acc2e`): erledigt mit benanntem Rest.** Jede der 95 Zeilen trägt ihren Verbleib: 78 erledigt, 13 bewusst belassen, 4 offen (`Register`/erstes Bild für alle Module, `ObjectiveDriver.Commands`, Katalogwerte `je Tick × r` an einigen Bewegern, Beträge je Tick aus Zustand). Zug, Dämpfungen und gemischte Rekursionen laufen einmal je Originalbild; die Skript-VM zählt wieder Bilder des Originals. |
| E7 | Showstopper | Partikeleffekte laufen am Simulationstick; Hüllkurven, EVA, HUD und Missionspräsentation zählen Ticks; Kopien der 30 außerhalb der Simulation | `docs/TICKRATE-AUDIT.md` §2, §6 | 21 | offen **erledigt (Phase 21, Stand `2a0a6f5`):** Effekte, Hüllkurven, Eva, HUD und Missionspräsentation laufen auf dem Originalbild-Raster an der Spieluhr (`GameClock`); eine Ratenkonstante außerhalb der Simulation, Quelltexttest mit 3 benannten Ausnahmen; bei 30 Hz alle reproduzierbaren Prüfsummen unverändert; Zeitmaß bei 30/60/120 Hz für alle geprüften Elemente gleich. Nicht von einem Menschen gesehen oder gehört. |
| E8 | Engstelle | Q47.16 ist für Größen je Tick² bei 60 Hz zu grob; `FixedTrig` gröber als die Zielauflösung; rund 170 Rohwert-Stellen, feste `65536` außerhalb der Simulation | `docs/HANDOFF-PHASE22.md` | 22 | offen **Messlatte aus Phase 20 (`SimTool rate-audit`):** kein benutzter Katalogwert rundet bei 60 oder 120 Hz auf null; über 1 % Quantisierungsfehler 1 Wert (60 Hz) bzw. 41 Werte (120 Hz, schlechtester `LocomotorInfo.Braking` 9,59 %); unter acht signifikanten Bits 46 bzw. 321 Werte. **erledigt bei 30 Hz (Phase 22, Stand `8d74ef8`):** Q39.24, `FixedTrig` auf rund 10⁻⁷ genau, Quelltextregel gegen Maßstabsliterale, alle Formate mit neuer Version, Manifest neu vermessen (5 von 16 KI-Läufen mit anderem Ausgang durch Rundungsverstärkung, Balance unbewertet). Bei 60 und 120 Hz rundet beim Katalogbau nichts mehr auf null, kein Wert über 1 % Fehler. Überlauf beim Quadrieren von Platzhalter-Reichweiten behoben (`SquaredReach`); **offen:** ungeprüfte Zielkoordinaten in Befehlen (ab rund 524.000 Einheiten Überlauf in der Bewegung) – Befehlsprüfung gegen die Kartengrenzen vor dem Lockstep (Phase 27/28). |
| E9 | Showstopper | Integration je Tick ist nicht schrittweiten-invariant (Bewegung, Flug, Geschosse, Umstürzen, multiplikative Dämpfung) | `docs/TICKRATE-AUDIT.md` §5 | 23 | offen **Phase 23 (9. Oktober 2026, Stand `f3acc2e`): erledigt mit benanntem Rest, 60 Hz ist Standard.** Prinzip: der 30-Hz-Schritt ist die Definition, höhere Raten unterteilen ihn (entschieden im ersten Tick des Originalbilds eines Objekts, die 30-Hz-Lage mit dem letzten erreicht, Beobachter lesen das Bildende; `docs/SIMULATION-CORE.md` §43). Raten-Äquivalenz-Suite von 479 / 625 Größen außerhalb der Toleranz (60 / 120 Hz) über 63 / 64 und 35 / 33 auf **0 / 0** von 4.388, ohne eine Toleranz zu ändern; das Retail-Referenzmatch ist bei 30, 60 und 120 Hz dasselbe Spiel (Bild 14.130, 26.626 Befehle). **Rest, gemessen:** 2 von 16 Paarungen auf ausgelieferten Karten enden bei 60 Hz in einem anderen Bild (7 s und 84,5 s, derselbe Sieger); 33 von 320 Gefechten der Erhebung enden mehr als eine Sekunde neben dem 30-Hz-Gefecht, 3 mit anderem Ausgang; 120 Hz ist für Paarungen und Erhebung nicht gemessen; einige Beweger rechnen weiter `Wert je Tick × r` (`docs/TICKRATE-MIGRATION.md` §15.7.6). Die Gleichheit ganzer Gefechte ist mit einem Selbsterwerb je Originalbild der Einheit erkauft, den der Auftrag dem Wortlaut nach verbot (§7a Nr. 1, Bestätigung offen). Drei Fehler bei 30 Hz wurden auf dem Weg behoben, je mit eigenem Manifest (`6a99102`, `8a6e5b7`, `cca6f92`). Nicht von einem Menschen gesehen. |
| E10 | Showstopper | Kein Test vergleicht zwei Raten; alle Abnahmen sind Hashes und Tickbudgets bei 30 Hz; rund 845 Testzeilen mit Tickerwartungen | `docs/TICKRATE-AUDIT.md` §7 | 21, 23 | offen **Phase 21:** Zeitmaß-Tests vergleichen 30, 60 und 120 Hz; 142 Testdateien bewertet (keine offen), Zeiterwartungen in Spielzeit; die Simulationstests laufen probeweise bei 60 Hz (396 von 402, sechs benannte Ausfälle für Phase 23). Der Vergleich der Spielergebnisse zwischen Raten bleibt Phase 23. **Phase 23 (9. Oktober 2026): erledigt.** Raten-Äquivalenz-Suite (686 Fälle, 4.388 Größen in zehn Gruppen, Toleranzen vor dem ersten Lauf festgelegt, `docs/SIMULATION-CORE.md` §39) und Verteilungsvergleich über 320 Gefechte je Rate (§40) vergleichen Spielgrößen zwischen 30, 60 und 120 Hz; `rate-compare --ai-diff/--reference-diff/--map-diff` nennt das erste abweichende Bild eines ganzen Gefechts. Alle Testprogramme nehmen `--tick-rate` und bestehen bei 30, 60 und 120 Hz (Simulation 513 von 513, mit Realdaten 543 von 543, am Stand `1b82e45`); Erwartungen stehen in Bildern. Das Referenzmanifest steht bei 60 Hz (`ea52f87`) und wird mit der Rate seiner Datei geprüft. **Offen:** die Determinismus-Matrix und der Linux-Lauf mit den 60-Hz-Manifesten (`docs/DETERMINISM.md` §2c). |
| E11 | Showstopper | KI-Zustand liegt außerhalb von Snapshot und Hash; `CreateSave()` wirft; KI-Startwert steht in keinem Kopf | `SkirmishMatchSession.cs`, `SkirmishAi.cs` | 24 | offen **Phase 23 (Vorgriff, ändert den Befund nicht):** die als Option gebaute Original-KI (`ScriptedSkirmishSession`, `docs/SKIRMISH-AI-ORIGINAL.md`) hält ihren ganzen Zustand von Anfang an in Zustand, Hash und Speicherstand ihrer Sitzung; für die eingebaute `SkirmishAi` – den Standard – ist E11 unverändert offen, und ihr Verhalten ist in Phase 23 geändert worden (stehender Heeresauftrag, Tauschwert-Regel, Arbeit für ausfahrende Bauarbeiter) und hat dabei neuen Zustand bekommen (`_armyOrder`, `_sightings`), der wie der übrige außerhalb von Snapshot und Hash liegt. |
| E12 | Showstopper | Befehle werden beim Einreichen gegen die aktuelle Welt geprüft und sofort gehasht; Einreichungszeitpunkt und -reihenfolge müssen auf allen Rechnern gleich sein | `SimulationWorld.Submit`/`Validate` | 24, 25 | offen |
| E13 | Showstopper | Befehlsweg antwortet synchron und vergibt den aktuellen Tick; Anzeige der Ablehnung hängt daran | `ICommandSink`, `CommandModeController` | 24 | offen |
| E14 | Showstopper | `LiveGame.Advance` besitzt die Zeit (Akkumulator, 8 Ticks je Bild, lokale Pause/Geschwindigkeit) | `LiveGame.cs` | 24 | offen |
| E15 | Engstelle | Match-Eingabe unvollständig: keine Teams/Farben/Startpositionen/KI-Stärke in `MatchSetup`, Mensch fest Spieler 1, Zufallsfraktion aus Shell-Zufall | `LiveMatch.cs`, `SkirmishScreens.cs` | 24, 25 | offen |
| E16 | Showstopper | Kein Netzwerkcode, kein Protokoll, keine Kennung des Simulationsstands | – | 25, 26 | offen |
| E17 | Engstelle | Snapshot ist JSON, 485–776 Byte je Objekt (10–12 MB im Stresstest), für Übertragung unvermessen; Wiederherstellung fügt Objekte in anderer Reihenfolge ein als das laufende Spiel | `docs/SIMULATION-CORE.md`, D3 | 18, 27 | **teilweise (Phase 18):** Aufzählung jetzt in ID-Reihenfolge (`EntityTable`, D3); Snapshot vermessen: 3,2 MB bei 155 Objekten (Twilight Flame, 8 Spieler), 9,0 MB bei 2.249 Objekten (`MD_GLA05`), Schreiben 9–43 ms, Lesen 23–164 ms; Übertragung offen (Phase 27) |
| E18 | Engstelle | Ohne Werkzeug ist ein Desync nicht auf Objekt und Feld zurückführbar | – | 27 | offen |
| E19 | Risiko | Offener UDP-Port: Flut, gefälschte Absender, fehlerhafte Pakete, Mitlesen | – | 26 (Cookie, Parser), 28 | offen |
| E20 | Engstelle | Speicherbereinigung und lange Bilder lassen einen Client stocken; im Lockstep muss er aufholen können | E4 | 19, 23, 26 | **vermessen (Phase 18):** 95 KB Allokation je Tick und 2 Gen-2-Läufe in 54.000 Ticks (8 Spieler); `MD_GLA05` 4 MB je Tick, 7 Gen-2-Läufe in 9.000 Ticks; Abtragen in Phase 19 **Phase 19:** 52 KB je Tick, keine Gen-2-Läufe in 54.000 Ticks (8 Spieler); `MD_GLA05` 10 KB je Tick; Ausreißer bis 94 ms (Gefecht) bzw. 152 ms (erster Missionstick) bleiben. **Phase 23 (Leistungstor bei 60 Hz):** rund 8 KB je Tick (8 Spieler; bei 30 Hz 17 KB), keine Gen-2-Läufe in 108.000 Ticks; Ausreißer bis 141–143 ms im Gefecht, 101–128 ms in den Missionen. Ein Client, der einen solchen Tick trifft, fällt bei 60 Hz um rund acht Ticks zurück (gerechnet: 141 ms gegen 16,7 ms je Tick) und muss aufholen (Aufholtempo 26-fach). |
| E21 | Grenze | Mission: Präsentation meldet an die Skript-VM zurück (`CompletePresentation`, `SelectNamed`, `SignalUiInteraction`), `<Local Player>` je Rechner, `_selected` nicht gehasht | `MissionPresentation.cs`, `ScriptEngine*.cs` | – (Kampagne bleibt lokal; in Phase 24 dokumentiert) | bewusst |
| E22 | Grenze | Kartenaufdeckung im Lockstep nicht verhinderbar | §2.8 | – | bewusst |
| E23 | Engstelle | Oberfläche: Mehrspieler-Knopf deaktiviert, kein Raum, keine Anzeigen im Spiel; Interpolation nur für Position und Rumpfdrehung | `docs/GAME-SHELL.md`, `FrameProjector.cs` | 29 | offen |
| E24 | Risiko | Nichts aus Phase 15–17 ist von einem Menschen abgenommen; ein Netzwerkspiel auf einem Spiel mit unbelegter Spielbarkeit verdeckt Ursachen | `CLAUDE.md` | 17, 29, 30 | offen **Phase 23:** unverändert offen. Der Projektinhaber hat am 8. Oktober 2026 ein KI-Gefecht im Zuschauermodus bei 30 Hz angesehen (vier Beobachtungen, daraus drei Korrekturen, `docs/AI-OBSERVED-2026-10-08.md`); bei 60 Hz hat kein Mensch das Spiel gesehen oder gehört. |

### 4a. Freigabe nach Phase 18 (6. Oktober 2026)

**Das Programm wird wie geplant fortgesetzt**, mit zwei Auflagen.

- *Dafür spricht:* die Rechnung ist auf dem gemessenen Rechner von JIT, GC, Kultur und Betriebssystem unabhängig (13 Matrixzeilen identisch, `docs/DETERMINISM.md`); die Messlatte existiert und besteht zweimal hintereinander; alle neun Befunde des Suchlaufs sind abgearbeitet; das Tickraten-Inventar ist geprüft (1.959 Stellen in acht Klassen, `docs/TICKRATE-AUDIT.md`).
- *Auflage 1 (E1):* der Lauf auf einem zweiten physischen Rechner mit anderer CPU steht aus (`docs/DETERMINISM.md` §4). Er ist vor Phase 26 (erstes Netzwerkspiel zwischen Rechnern) nachzuholen; bis dahin gilt der Determinismus über Rechnergrenzen als *wahrscheinlich, nicht belegt*.
- *Auflage 2 (E4):* die Leistung ist der eigentliche Engpass. Ohne Phase 19 kostet ein 8-Spieler-Gefecht bei 60 Hz 30 % eines Kerns mit Ausreißern bis 547 ms je Tick, und eine große Mission 76–81 %. Phase 19 bleibt deshalb vor Phase 20; ihr Sollwert „Mittel ≤ 1,0 ms“ verlangt rund den Faktor 5 und ist nach dem Profil nur erreichbar, wenn Bewegung/Wegsuche (`AIUpdateInterface`, 81 % der Update-Zeit) sinkt – Sicht und Hash allein bringen höchstens ein Drittel.
- *Aus dem Inventar für die Phasen 20–23 (Widersprüche zum Plan, `docs/TICKRATE-AUDIT.md` Zusammenfassung):* „gleiche Balance bei jeder Rate“ gilt nur für die Katalogrundung, nicht für Abrundungen, Ein-Tick-Minima und N+1-Zähler zur Laufzeit; Literale, die Reihenfolge statt Zeit bedeuten, dürfen nicht skaliert werden; gemischte Rekurrenzen (Zug, Zugformation, MOAB) brauchen mehr als Quadratwurzeln; die Präsentation greift heute über `StandingOrders` und den Missions-Director in die Simulation ein; `SaveGame` schreibt keine Rate.

## 5. Phasenübersicht

| Phase | Inhalt | Ändert Referenzwerte? | Tor am Ende |
|---|---|---|---|
| 17 | Spielbarkeit (läuft, unverändert `docs/HANDOFF-PHASE17.md`) | ja (Regelkorrekturen) | – |
| 18 | Messbasis: Referenzmanifest, Determinismus über Rechnergrenzen, Leistungsprofil, geprüftes Inventar | nur benannte Determinismus-Korrekturen | Freigabe des Programms |
| 19 | Leistung: Tickkosten echter Matches | nein | Sollwerte erreicht oder Rest benannt |
| 20 | Tickrate als Parameter – Simulation | nein (bytegleich bei 30 Hz) | Manifest unverändert |
| 21 | Tickrate als Parameter – Präsentation, Spielschicht, UI, Ton, Werkzeuge, Tests | nein (bildgleich bei 30 Hz) | Prüfsummen unverändert |
| 22 | Festkomma Q39.24 | **ja, einmal** | Ergebnisvergleich erklärt |
| 23 | Zeitschritt-Invarianz, neue Abnahmetests, 60 Hz als Standard, Neuerhebung | **ja** (60 Hz) | Raten-Äquivalenz und Leistungstor – **erledigt am 9. Oktober 2026 (`e8632bb`, `ea52f87`)**: Suite 0 / 0, Manifeste bei 60 Hz; das Leistungstor bis auf Maximum und Allokation; dazu drei Verhaltensänderungen bei 30 Hz mit eigenen Manifesten; Rest und offene Entscheidungen in §7a |
| 24 | Sitzungskern: KI im Zustand, Speicherstand, Match-Beschreibung, Befehlsweg, Tickquelle | nein (bei Verzögerung 0) | Referenzwerte unverändert |
| 25 | Protokoll, Raum, Relay im Prozess; lokales Gefecht über den Netzpfad; KI-Slots | nein | Mehr-Client-Probe hashgleich |
| 26 | Dedicated Server, UDP, Zeitabgleich, adaptive Puffer, 8 Clients | nein | Netzmatrix gemessen |
| 27 | Desync-Diagnose, Wiedereinstieg, Zuschauer, Server-Replay | nein | Wiedereinstieg hashgleich |
| 28 | Serverbetrieb und Sicherheit | nein | Fuzzing, Kapazität, Paket |
| 29 | Mehrspieler im Client, Latenzwahrnehmung, Abnahme mit Menschen | nein | Checkliste durch Menschen |
| 30 | Patch-Integration, Qualität, auslieferbarer Abschluss (verschoben) | ja (übernommene Fixes) | Paket, Abnahme |

Abhängigkeiten: 18 → 19 → 20 → {21 ∥ 22} → 23 → 24 → 25 → 26 → 27 → 28 → 29 → 30. Parallel dürfen nur 21 und 22 laufen (getrennte Worktrees, Merge 21 vor 22). Phase 24 hängt fachlich nicht an 60 Hz, kommt aber danach, damit ihre Referenzwerte nicht zweimal erhoben werden.

Warum die Tickrate vor dem Netz: Rate und Festkomma ändern jeden Hash und jedes Format. Netzwerkcode gegen eine Simulation zu entwickeln, deren Referenzwerte noch dreimal wechseln, hieße, jede Desync-Suche gegen ein bewegliches Ziel zu führen.

## 6. Regeln für alle Phasen des Programms (zusätzlich zu `CLAUDE.md`)

1. **Erst messen, dann ändern.** Jede Phase beginnt mit dem reproduzierten Ausgangszustand; keine Zahl aus diesem Dokument oder einem Handoff gilt als gemessen.
2. **Das Referenzmanifest ist die Messlatte** (`baselines/`, ab Phase 18). Eine Phase, die als ergebnisgleich angekündigt ist, endet mit unverändertem Manifest. Wer es ändern muss, tut das in einem eigenen Commit mit einer einzigen Ursache, Vorher/Nachher-Werten und Begründung.
3. **Eine Ursache je Referenzänderung.** Nie zwei ergebnisändernde Dinge in einem Commit; sonst ist keine Verschiebung mehr zuzuordnen.
4. **Determinismus gilt über Rechnergrenzen.** Neue Simulationseingänge laufen über die geprüften Wandlungsroutinen; nichts Kultur-, Laufzeit- oder CPU-abhängiges erreicht den Zustand.
5. **Keine Wandzeit in der Simulation**, auch nicht mittelbar: keine Lastverteilung nach Zeit, keine Rückmeldung der Präsentation in ein Netzwerkmatch.
6. **Nichts fällt still zurück.** Unbekannte Nachrichten, Versionen, Fingerabdrücke, Raten: gezählt, gemeldet, abgelehnt.
7. **Formate:** inkompatible Änderungen erhöhen die Version; alte Stände werden mit klarer Meldung abgelehnt. Bis zur ersten Veröffentlichung gibt es keine Migrationspflicht, danach ist jeder Formatbruch eine Entscheidung des Projektinhabers.
8. **Headless belegt ist nicht abgenommen.** Was nur ein Mensch beurteilen kann (Aussehen bei 60 Hz, Spielgefühl mit Verzögerung, Netzwerkspiel mit echten Spielern, ein zweiter Rechner), wird als offen berichtet, bis es stattgefunden hat.
9. **Vorhandene Handoffs fortschreiben.** Am Ende jeder Phase wird der Auftrag der nächsten *nicht neu geschrieben*, sondern sein Abschnitt „Ausgangslage“ mit dem gemessenen Stand aktualisiert; Abweichungen vom Plan (verschobene oder neue Punkte) werden dort und in §5 benannt.
10. Zeilenenden, Git-Regeln, Hintergrundläufe mit Timeout, Release-Werkzeuge neu bauen: wie `CLAUDE.md`.

## 7. Offene Entscheidungen des Projektinhabers

Die Aufträge nennen jeweils einen Standard, damit niemand blockiert ist; die Entscheidung bleibt beim Projektinhaber.

| Frage | Standard im Plan | fällig vor |
|---|---|---|
| Soll das Programm vor dem Abschluss von Phase 17 beginnen? | nein; Phase 18 (reine Messung) darf vorgezogen werden | 18 |
| Regel für langsame Clients: wie lange drosseln, wann trennen? | bis 2 s anzeigen, bis 10 s drosseln, dann trennen | 26 |
| Was geschieht mit den Objekten eines ausgeschiedenen Spielers? | Verhalten des Originals im Referenzquelltext nachlesen, vorschlagen, entscheiden lassen | 26 |
| Wie lange wird ein Platz für den Wiedereinstieg gehalten? | 5 Minuten | 27 |
| Zuschauer-Verzögerung | 120 s | 27 |
| KI-Stärken (das Original kennt drei, die KI hier eine) | eine Stärke, Wahl gezählt statt befolgt | 24 |
| Server unter Linux als Lieferziel? | ja (kostet wenig, weil der Server keine Daten und kein Stride braucht) | 28 |
| Soll 30 Hz im fertigen Spiel wählbar bleiben? | nein (nur Testrate) | 23 |
| Bleibt der Standard bei 30 Hz, wenn die Raten-Äquivalenz-Suite nach zwei Messrunden nicht besteht? | **entschieden am 8. Oktober 2026: nein.** Das Abbruchkriterium der Phase 23 ist aufgehoben; die Suite ist zum Bestehen zu bringen und 60 Hz wird Standard. Was auf dem Weg liegt, wird behoben, auch Fehler bei 30 Hz (je eine Ursache, eigener Commit, neu geschriebenes Manifest). Stand und Restursachen: `docs/TICKRATE-MIGRATION.md` §15.4 | 23 |
| Zweiter Rechner und Mitspieler für die Abnahmen | vom Projektinhaber zu stellen | 18, 26, 29 |
| *Aus Phase 23 (ausgeschrieben in §7a):* Selbsterwerb einmal je Originalbild der Einheit, ebenso `PointDefenseLaserUpdate`, `CleanupHazardUpdate`, `DeployStyleAIUpdate.AllowsFire` (§7a Nr. 1) | vorläufig so gebaut und gemergt; damit sind ganze Gefechte zwischen den Raten gleich | 24 (bevor Phase 24 Referenzwerte erhebt) |
| Fabrikausfahrt „immer durch das Tor“ (§7a Nr. 2) | nicht gemergt; eine Einheit mit sofortigem Befehl verlässt das Gebäude auf dem kürzesten Weg | – |
| Tickmaximum mit einer Verhaltensänderung senken (§7a Nr. 3) | nicht gemacht; 141–143 ms bei 60 Hz | 26 |
| Die zwei schnelleren Hash-Konstruktionen (§7a Nr. 4) | nicht umgesetzt | – |
| Befehlsprüfung gegen die Kartengrenzen (§7a Nr. 5) | nicht gebaut | 27/28 |
| Solo-Boni der Kampagne (§7a Nr. 6) | nicht umgesetzt | – |
| Zeichenreihenfolge der Straßen (§7a Nr. 7) | unverändert, hängt an der Rate | – |
| Obergrenze eines Standardgefechts von einer Stunde Spielzeit (§7a Nr. 8) | unverändert so, auch für einen Menschen | – |
| Original-Skirmish-KI als Standard und ihre Auswahl im Client (§7a Nr. 9) | nein: Standard ist die eingebaute KI, die Original-KI ist eine Option der Werkzeuge (`ai-match --ai original --difficulty …`) | 24 |
| Balance nach den KI-Korrekturen bewerten (§7a Nr. 10) | nicht bewertet | – |
| Zweiter physischer Rechner (§7a Nr. 11) | offen | 26 |

### 7a. Entscheidungen aus Phase 23 (9. Oktober 2026)

Dieselbe Liste steht in `docs/TICKRATE-MIGRATION.md` §15.7.7, in `CLAUDE.md` (Absatz „Phase 23“) und im Nachtrag von `docs/HANDOFF-PHASE24.md`. „Vorläufig“ heißt: so ist der Stand auf `main`; die Entscheidung liegt beim Projektinhaber.

1. **Selbsterwerb einmal je Originalbild der Einheit.** Eine Einheit entscheidet ihren eigenen Angriff im ersten Tick ihres eigenen Bildes. Der Auftrag der Phase 23 verbot den Ausweg „Originalraster“ für den Zielerwerb; es ist vorläufig so gemacht, weil die Gleichheit ganzer Gefechte anders nicht erreichbar war. Kosten: eine Einheit, die mitten im Bild durch einen Befehl frei wird, beginnt ihre eigene Zielsuche bis zu 1 Tick (60 Hz) bzw. 3 Ticks (120 Hz) später. Alternative: je Tick entscheiden – dann unterscheiden sich Gefechte zwischen den Raten, und es gilt nur der Verteilungsvergleich. Dieselbe Frage für `PointDefenseLaserUpdate`, `CleanupHazardUpdate` und `DeployStyleAIUpdate.AllowsFire`.
2. **Fabrikausfahrt „immer durch das Tor, auch bei sofortigem Befehl“.** Gebaut und gemessen, nicht gemergt: verschiebt die Balance (America 46 statt 61 Siege von 80, China 32 statt 23; damals 10 statt 12 der 16 Läufe entschieden); der Flicken liegt im Scratchpad der Sitzung.
3. **Das Tickmaximum senken** (141–143 ms bei 60 Hz im Leistungstor): braucht eine Verhaltensänderung (gemeinsamer Gruppenweg, Zellbudget je Tick oder eine andere Suche) – vom Projektinhaber vorbehalten.
4. **Die zwei schnelleren Hash-Konstruktionen** (Sicht zeilenweise; Objekt-Hash mit Modul-Hashes) – vorbehalten, unverändert.
5. **Befehlsprüfung gegen die Kartengrenzen** (Ziele jenseits von rund 524.000 Einheiten lassen die Bewegung überlaufen) – für Phase 27/28 vorbehalten, unverändert.
6. **Solo-Boni der Kampagne** – vorbehalten, unverändert.
7. **Die Zeichenreihenfolge der Straßen hängt an der Rate** (`TerrainLayer.CreateRoads` gibt jedem Straßen-Mesh dieselbe Hülle; keine Korrektur gibt jedes 30-Hz-Bild wieder).
8. **Ein Standardgefecht endet nach einer Stunde Spielzeit**, auch für einen Menschen – ist das gewollt?
9. **Die Original-Skirmish-KI (drei Schwierigkeitsgrade) als Standard-KI** und ihre Auswahl im Client und im Zuschauermodus: sie kostet je Tick rund das 27-fache der eingebauten KI (1,1 ms für 8 Spieler), und ihr fehlen Teile des C++-Kerns; Standard ist die eingebaute KI.
10. **Die Balance nach den KI-Korrekturen ist nicht bewertet** (ChinaTankGeneral gewinnt in der 30-Hz-Erhebung der Runde 3 70 von 80 Gefechten gegen AirForce).
11. **Der zweite physische Rechner** (vor Phase 26) – unverändert offen.

## 8. Begriffe

- **Tick:** ein Schritt der Simulation; Dauer 1/Rate.
- **Originalbild:** 1/30 s, die Zeiteinheit des Originals; Einheit vieler Daten und Konstanten.
- **Befehlsabsicht:** was ein Spieler will, ohne Tick und Sequenz; der Server (oder lokal die Sitzung) macht daraus einen Befehl für einen Tick.
- **Tickbündel:** alle Befehle der Menschen und des Systems für einen Tick, vom Server an alle verteilt.
- **Lockstep:** alle Rechner führen dieselben Befehle im selben Tick aus und erreichen denselben Zustand.
- **Relay:** Server, der Befehle ordnet und verteilt, ohne zu simulieren.
- **Sitzungshash:** Zustands-Hash der Welt plus der KI-Zustände; Grundlage der Hashberichte.
- **Desync:** zwei Rechner haben für denselben Tick verschiedene Sitzungshashes.
- **Referenzmanifest:** Datei mit den Soll-Ergebnissen fester Läufe; ersetzt die Hashes in der Doku als Messlatte.
- **Raten-Äquivalenz:** dieselbe Szene ergibt bei 30, 60 und 120 Hz dieselben Spielgrößen innerhalb erklärter Toleranzen.
- **Aufholtempo:** wie viel schneller als Echtzeit ein Client rechnen kann; begrenzt Wiedereinstieg und Erholung nach einem Stocken.
