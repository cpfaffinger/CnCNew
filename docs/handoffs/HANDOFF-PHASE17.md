N+1-Handoff (Phase 17): Phase 9 abschließen (Fortsetzung 7) – Spielbarkeit durch Menschen, Nur-Befehle-Agent, Client durchklicken, Leistung, Näherungen der neuen Module

Du arbeitest autonom an diesem Repository (`<repo>` = Wurzel des Arbeitsverzeichnisses). Lies zuerst `CLAUDE.md`, `README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§9 „Phase 9“ samt „Bekannte Grenzen“), `docs/SCRIPT-ENGINE.md` (§5, §8, §9), `docs/SIMULATION-CORE.md` (§29, §30), `docs/CAMPAIGN-AND-SAVEGAMES.md` und `docs/GAME-HUD.md` §14/§15.
Voraussetzung: Phase 15 (Welt-Darstellung, Sicherheit) und Phase 16 (Bedienung, Ton, Mission im Client) sind abgeschlossen; ohne sie ist „Spielbarkeit“ nicht beurteilbar (`docs/DEMO-FINDINGS.md`). Dieser Auftrag setzt `IMPLEMENTATION-PLAN.md` §9 fort. §9 ist **noch nicht abgeschlossen**: die Abnahme verlangt „jede Kampagnenmission besteht ihre wesentlichen Erfolgs- und Fehlerpfade“. Belegt ist die Skriptkette (Zielfahrer, Erreichbarkeit, Determinismus, Speichern/Laden), **nicht** dass ein Mensch mit den implementierten Einheiten gewinnen kann.

## Neu gemessen am 4. Oktober 2026 abends (ersetzt die Zahlen unten, wo sie abweichen)

- **Versionen und Referenzwerte:** `WorldSnapshot` 9, Replay 10, `CommandTypes.Version` 13, `MissionSnapshot` 2; Retail-Referenzmatch Sieger FactionGLA bei Tick 13.500, Endhash `0946FB676DEC615C`, Kette
  `EA6D12F180930D71`; Stresstest `56882F763EABEF67` / `B8E663493F2C73D5`; sauberer Solution-Build **11** Warnungen; Kompatibilitätsbasislinie 751; 183/205 Module, 2.115/2.115 Objekte.
- **Kampagne (je Karte einzeln, 24 Karten einschließlich `Training01`):** `drive` (60.000 Ticks) Sieg 24/24, Niederlage 24/24; `audit --ticks 900` 24/24 deterministisch und identisch nach Speichern/Laden,
  0 Treffer auf fehlende Typen, Ausschalt-Endproben Sieg 13/24, Niederlage 15/24 (Stand `096290b`).
- **Nur-Befehle-Agent** (`drive --commands-only --outcome victory --ticks 15000`, je 2.400 s Zeitgrenze): weiter **1 Karte** (`MD_GLA04`); 22 nicht erreicht, `MD_USA01` nach 2.400 s abgebrochen. Mit
  `--ticks 60000` auf `GC_ChemGeneral`, `GC_NukeGeneral`, `GC_TankGeneral`, `MD_CHI01`: ebenfalls kein Sieg. Die Zeitgrenze ist also nicht der Engpass (`docs/SCRIPT-ENGINE.md` §9).
- **Skirmish-KI:** Versorgungszentrum am Lager (`AIPlayer::buildBySupplies`) und Angriff in Gruppen heben die 16 KI-Läufe von 5–7 auf **12 entschiedene**; offen bleiben dreimal ChinaTankGeneral gegen
  AmericaAirForceGeneral und Bitter Winter America/China (`docs/SIMULATION-CORE.md` §32). `SimTool ai-match … --trace N` zeigt Geld, Armee, Sammler und Strecke über die Zeit – damit zuerst messen.
- **Für den Agenten folgt daraus:** er baut und greift an, erfüllt aber keine Missionsziele. Der nächste Schritt in Punkt 1 ist nicht „mehr Zeit“, sondern je Karte die erste unerfüllte Bedingung der
  Siegkette (`CampaignTool drive --explain`) als Befehl auszudrücken.
- **Aus Phase 16 hierher verschoben:** Leerlauf-Zielerwerb und Angriffsmarsch der Einheiten (`friend_checkForIdleMoodTarget`, `docs/SIM-TURRET-AIM.md` §6/§8a) – erst damit verliert eine marschierende Einheit ihr
  Ziel nicht bis zum nächsten Denkschritt des Befehlsgebers.
## Ehrlicher Status (Ausgangslage, Stand nach Phase 14, 4. Oktober 2026; Phase 16 ist hier noch nicht eingerechnet)

**Änderungen durch Phase 15, die die Zahlen unten verschieben (alle neu messen!):** `CommandTypes.Version` 12 und Replay 9 (typisierte Kampfplan-Option; `WorldSnapshot` 8 und `MissionSnapshot` 2 unverändert); Wegsuche um stehende Einheiten und ein von Gebäuden freier KI-Sammelpunkt
verschieben das Verhalten der Skirmish-KI (16 KI-gegen-KI-Läufe: 8 statt 10 entschieden, alle deterministisch); Retail-Referenzmatch weiter Sieger FactionGLA bei Tick 13.890, Endhash `76F22CB0CA79BEEF`; Stresstest `56882F763EABEF67`. Die Spielbarkeitszahlen dieses
Handoffs wurden nach den Sim-Änderungen der Phase 15 nur zum Teil neu gemessen: der Nur-Befehle-Agent (`drive --commands-only`, 15.000 Ticks, 6 Läufe parallel, je 1.500 s Zeitgrenze) erreicht den Sieg über die eigenen Skripte weiter nur auf **1 Karte** (`MD_GLA04`, Tick 1.022); 22 weitere Karten enden ohne Sieg, `MD_USA01` wurde nach 1.500 s abgebrochen (kein Ergebnis). **Nicht neu gemessen:** Zielfahrer 24/24, Audit-Endproben 13/24 und 15/24 – vor jeder Aussage neu erheben. Die Darstellung (Phase 15) ist technisch fertig, aber von keinem Menschen abgenommen
(Checkliste in `docs/CLIENT-OBJECTS.md`).

Phase 14 (`git log 5428d89..HEAD` bzw. die Merge-Commits „air modules“, „crate, special and collide modules“, „structures and world modules“, „last nine blocker modules“, „weapon slot model“) hat den Handoff-14-Auftrag zu einem großen Teil erledigt:

- **Modulabdeckung geschlossen:** alle 183 Simulationsmodule implementiert, **2.115 von 2.115** Retail-Objekten instanziierbar, Modulverwendungen 16.558/19.553 (84,7 %; der Rest sind die 22 Präsentationsmodule), Kompatibilitätsbasislinie 751 (`docs/SIMULATION-CORE.md` §30 mit Tabellen je Modul, Quelle und Näherung).
- **Waffenslot-Modell:** Primär/Sekundär/Tertiär, Clips und Nachladen je Slot, Sperren, Satzauswahl (Veteran/`PLAYER_UPGRADE`/`CRATEUPGRADE_*`/`CARBOMB`), Beste-Waffe-Wahl (reduziert), `FireOCLAfterWeaponCooldownUpdate` wirkt erstmals (§30.6). Versionen: `WorldSnapshot` 8, Replay 8, `CommandTypes.Version` 11, `MissionSnapshot` 2.
- **Skriptseite:** Formationen (`asTeam`), Pfadflug des Partikelstrahls, OCL-Lieferungen (`DeliverPayload`, Spezialkräfte am Kartenrand), Todeswaffen, `NAMED_SET_HELD`, `PLAYER_DISABLE/ENABLE_FACTORIES`, `SET_TRAIN_HELD`, `NAMED_SET_EVAC_LEFT_OR_RIGHT`, Sprengfallen, `BRIDGE_BROKEN/_REPAIRED`, mitwandernde Skriptnamen; die VM führt **109/109 Bedingungen** und 187 Aktionstypen aus (129 weitergereicht, 19 aufgezeichnet, **9 fehlend, keiner in einer Kampagnenkarte benutzt**: `SKIRMISH_BUILD_BUILDING`, `SKIRMISH_BUILD_BASE_DEFENSE_FRONT/_FLANK`, `SKIRMISH_BUILD_STRUCTURE_FRONT/_FLANK`, `SKIRMISH_ATTACK_NEAREST_GROUP_WITH_VALUE`, `SKIRMISH_PERFORM_COMMANDBUTTON_ON_MOST_VALUABLE_OBJECT`, `AI_PLAYER_BUILD_TYPE_NEAREST_TEAM`, `NAMED_FIRE_WEAPON_FOLLOWING_WAYPOINT_PATH`).
- **Zielfahrer:** Sieg 24/24 und Niederlage 24/24 auf dem eigenen Skriptweg (60.000 Ticks); `audit --ticks 900` 24/24 deterministisch und identisch nach Speichern/Laden, 0 Treffer auf fehlende Typen, Ausschalt-Endproben Sieg 13/24, Niederlage 15/24; `reachability` sauber 24/24 und 24/24. Zwei Rückschritte durch die neuen Gegnermodule (`MD_GLA03` Sieg, `MD_GLA04` Niederlage) sind im Fahrer behoben (`docs/SCRIPT-ENGINE.md` §9): Anwesenheitsbedingungen erst bei live geschaltetem Skript, Reparatur eroberter Träger/platzierter Einheiten, Planer meidet bei Gleichstand Bereichsplatzierungen (`Leaf.Risky`).
- **Zahlen (neu messen):** Retail-Referenzmatch Sieger FactionGLA bei Tick 13.890, Endhash `76F22CB0CA79BEEF`, Kettenhash `35F54CAD40A43E6E`; Stresstest `56882F763EABEF67` (Kette `B8E663493F2C73D5`); sauberer Solution-Build 21 erklärte Warnungen; alle Testprogramme grün.

**Bewusst nicht erledigt (jeweils mit Grund):**

- *Spielbarkeit durch einen Menschen:* der Nur-Befehle-Agent (`CampaignTool drive --commands-only`, `SkirmishAi` als Wirtschafts-/Heer-Agent) schreibt nichts in die Welt und erreicht den Sieg über die eigenen Skripte nur auf wenigen Karten (Zahl und Karten: `docs/SCRIPT-ENGINE.md` §9, Abschnitt „Nur-Befehle-Modus“, Stand Phase 14). Ein Mensch hat die Shell und eine Mission nie interaktiv gespielt; die Rauchtests (`--smoke-test`, `--match`, `--mission`) laufen headless.
- *Neun fehlende Skriptaktionen* (siehe oben): die ersten sechs gehören zu `AISkirmishPlayer` (Bauliste, Basisradius, `AIData`), das die Simulation durch den `SkirmishAi`-Ersatz nicht teilt; in keiner Kampagnenkarte benutzt.
- *Näherungen der neuen Module* (alle in `docs/SIMULATION-CORE.md` §30 benannt): Flugplatz-/Jet-Geometrie ohne Modellknochen und ohne Physik; (Atomraketen-Start am Silo: seit Phase 15 durch Echtdatentest belegt, kein Handlungsbedarf); Brückenterrain, Brückenschichten und Gerüstbau fehlen; Fähre (`RailedTransport*`) ohne Befehlstyp (`EXECUTE_RAILED_TRANSPORT` würde das Replayformat ändern) und ohne Retail-Karte; `DemoTrapUpdate` ohne Waffenslot-Befehl; `PowerSabotage` ohne Verbraucher; Waffenwahl nur `PREFER_MOST_DAMAGE`; Kollisionen nur für Bewegende; (der Jet-`IsIdle`-Verdacht ist seit Phase 15 durch einen Echtdatentest widerlegt).
- *Leistung:* unter paralleler Last dauerten `audit --ticks 900` je Karte 2–10 Minuten (Gesamtlauf über eine Stunde, deshalb je Kampagne einzeln); der Stresstest zeigte 150 µs/Tick unter Last gegenüber 70–80 µs in ruhigen Läufen. Eine ruhige Vorher/Nachher-Messung (`SimTool stress`, `mission-run` auf `MD_USA05`, `MD_GLA05`) mit Profil fehlt.

## Auftrag

1. **Spielbarkeit statt Welteingriffe (Kern).** Neu messen: `CampaignTool drive --commands-only --outcome victory --ticks 15000`, je Kampagne einzeln (`--campaign NAME`, parallel; die USA-Karten brauchen über eine Stunde), und die Tabelle je Karte in `SCRIPT-ENGINE.md` §9 fortschreiben. Dann die häufigsten Ursachen für „nicht erreicht“ je Karte abtragen (Befehlsagent, nicht Fahrer-Welteingriffe): `SkirmishAi` für den Betrachter besser machen (Angriffsschwelle `--agent-threshold`, Verteidigung der Basis, Nachschub), Luft- und Spezialkräfte über echte Befehle, Brückenübergänge/Fähren, Missionsziele, die Befehle statt Teleport brauchen. Jeder Schritt mit Messung auf allen Karten; kein Wert ohne Lauf.
2. **Client durchklicken (Abschlussdurchgang; die Darstellungs- und Bedienfehler der Tech-Demo sind Gegenstand von Phase 15 und 16, `docs/DEMO-FINDINGS.md`).** `--shell` interaktiv (Kampagne wählen, Briefing, Mission, Speichern/Laden, Abschluss), `--mission` und `--match` mit Maus und Hotkeys; gefundene Fehler beheben; Ton und Film anschließen, falls gewünscht. Ergebnisse als ehrlicher Bericht (was ging, was nicht), nicht nur Rauchtest.
3. **Leistung.** Ruhige Messung (keine parallelen Läufe): `SimTool stress`, `mission-run` auf den größten Karten, Profil der heißesten Module (neu: Jet-/Parkplatz-, Waffenslot-Wahl, Kollisionsverteilung); Ziel: kein Schwund gegenüber Phase 13 ohne Hash-Änderung.
4. **Näherungen abtragen**, nach Nutzen (Silo-Start, Jet-`IsIdle` und das typisierte Kampfplan-Feld sind seit Phase 15 erledigt): `PowerSabotage`-Verbraucher (Unterstromzustand), Brückenterrain nur wenn eine Karte es braucht.
5. **Optional:** die sechs `AISkirmishPlayer`-Aktionen portieren (Bauliste/Basisradius/`AIData`), falls Punkt 1 zeigt, dass die Skirmish-KI-Skripte (`SkirmishScripts.scb`) gebraucht werden.
6. Nach jeder Gruppe: `ScriptCoverage` aktualisieren (Test `ScriptSupportTableIsConsistent`), `SCRIPT-ENGINE.md` §5/§8/§9 und `SIMULATION-CORE.md` neu schreiben, `CampaignTool audit --ticks 900` (je Kampagne einzeln), `drive` (60.000 Ticks, je Kampagne parallel), `reachability`.

## Nicht verhandelbare Regeln (unverändert, siehe `CLAUDE.md`)

- Unbekannte Schlüssel/Module/Enums/Referenzen fallen nie still auf Standardwerte zurück; jeder Fall ist explizit, gezählt, dokumentiert und gegen `baselines/compatibility.json` abgesichert (neu erzeugen mit `IniTool compat-baseline`, Änderung begründen).
- Determinismus: sortierte Sammlungen, keine Wandzeit, kein `float`/`double`, kein `Random.Shared`; `SourceRules` gilt für `CnCNew.Simulation`. Jeder neue Zustand gehört in `Describe`/`Load` und wird nur geschrieben, wenn er vom Standard abweicht.
- `TreatWarningsAsErrors=true`; 21 erklärte Warnungen bei sauberem Build (siehe `CLAUDE.md`), jede andere ist ein Fehler.
- Versionen: inkompatible Änderungen erhöhen `WorldSnapshot` (8), Replay (8), `MissionSnapshot` (2), `CommandTypes.Version` (11).
- Fremder Code nur lesen, nicht kopieren; jede portierte Aktion/Bedingung/jedes Modul einzeln mit Datei und Funktion zitieren.
- Bestandszahlen immer neu messen. Nur lokal, kein Remote, nichts pushen; mehrere fachlich geschlossene Commits; Working Tree am Ende sauber. **Geändert am 5. Oktober 2026:** `docs/handoffs/HANDOFF-PHASE18.md` existiert bereits (Beginn des Programms Tickrate/Festkomma/Mehrspieler, `docs/MULTIPLAYER-PLAN.md`) und wird am Ende dieser Phase **nicht neu geschrieben**, sondern in ihrem Abschnitt „Ausgangslage“ mit den dann gemessenen Referenzwerten fortgeschrieben. Was von diesem Auftrag offen bleibt (insbesondere alles, was einen Menschen braucht), kommt als Abschnitt „Rest der Phase 17“ an das Ende dieses Dokuments und als Verweis in `docs/handoffs/HANDOFF-PHASE30.md` Punkt 2.
- **Zeilenenden erhalten:** pro Datei `git ls-files --eol <datei>`; CRLF im Index haben `README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md`, alles andere ist LF (`Entity.cs` hat gemischte Enden: bei Merges nimmt Git die ganze Datei als Konflikt – dann die Fassung von `main` nehmen und die Hunks von Hand anwenden). Für die CRLF-Dateien PowerShell 7 (`pwsh`) mit `[IO.File]::ReadAllText/WriteAllText` (UTF-8 ohne BOM) benutzen; Skriptdateien mit Umlauten nie mit Windows PowerShell 5 starten (liest ANSI) – oder das Edit-Werkzeug verwenden.
- **Hintergrundläufe immer mit begrenztem Timeout** (`timeout` am Prozess, Wartezeiten 3–10 Minuten). Lange Läufe je Kampagne parallel (`--campaign NAME`); beim Neubauen eines Werkzeugs während eines Laufs die Ausgabe in ein anderes Verzeichnis legen (`dotnet build -c Release … -o <scratchpad>/ct2`), sonst ist die Datei gesperrt. Eine Release-`SimTool`/`CampaignTool`-Binärdatei ist nach Quelländerungen **veraltet**, bis sie neu gebaut wurde (`--no-build` zeigt sonst alte Zahlen).
- Unterlagen im Scratchpad der Session, nichts Generiertes ins Repo.

## Abschlussbericht

Zahlen mit Commit-Hashes, die Tabelle der Nur-Befehle-Läufe je Karte (vorher/nachher), Ergebnisse des Client-Durchklickens, Leistungsmessung (ruhig), geänderte/neu abgesicherte Näherungen mit Quellverweisen, Determinismus-/Speichern-Laden-Ergebnisse, aktualisierte Doku (README, ARCHITECTURE, ROADMAP, IMPLEMENTATION-PLAN, SCRIPT-ENGINE, SIMULATION-CORE, CLAUDE.md) und die fortgeschriebene „Ausgangslage“ in `docs/handoffs/HANDOFF-PHASE18.md`. Der Abschlussbericht nennt ausdrücklich, was *nicht* belegt ist.

## Hinweis zur Planänderung vom 5. Oktober 2026

Der Projektinhaber hat Mehrspieler über Dedicated Server, 60 Hz und eine parametrisierte Tickrate in den Scope genommen (`docs/MULTIPLAYER-PLAN.md`, Phasen 18–29). Für diesen Auftrag folgt daraus: (1) Das Programm vergleicht ab Phase 18 „vorher/nachher“ über Hashes; jede Regelkorrektur dieser Phase sollte deshalb *vor* dem Start von Phase 18 gemergt sein. (2) Punkt 3 (Leistung) wird in Phase 18/19 mit einem Profil echter Matches fortgeführt; hier genügt die ruhige Vorher/Nachher-Messung. (3) Der frühere Folgeschritt (Patch-Integration, Qualität, Paket) ist jetzt Phase 30.

## Rest der Phase 17 (Stand 6. Oktober 2026, nach der Abschluss-Sitzung)

Phase 17 ist mit begrenztem Umfang **beendet, ihr Abnahmeziel ist nicht erreicht**: die Spielbarkeit der Missionen durch einen Menschen ist weiter nicht belegt. Dieser Abschnitt ersetzt die Zahlen weiter oben, wo
sie abweichen. Ab `docs/handoffs/HANDOFF-PHASE18.md` ändert niemand mehr das Verhalten der Simulation, bis das Programm aus `docs/MULTIPLAYER-PLAN.md` es freigibt; alles unten, was die Simulation ändert, liegt deshalb
hinter dem Programm (`docs/handoffs/HANDOFF-PHASE30.md` Punkt 2).

### Gemessener Stand (`6eb7ae4`, ein Rechner, Release)

- Sauberer Build 0 Warnungen; alle Testprogramme grün (`Game.Tests` Release); `compat-check` 751; 183/205 Module, 2.115/2.115 Objekte; drei Szenen-Audits grün.
- `CommandTypes.Version` 14, Replay 11, `WorldSnapshot` 9, `MissionSnapshot` 2. Referenzmatch Sieger FactionGLA bei Tick 14.490, `23B4B25DF62765D8`, Kette `DE5849AF9F8493C4`; Stresstest `56882F763EABEF67` / `B8E663493F2C73D5`.
- 16 KI-Läufe: **12 entschieden** (vorher 11), alle deterministisch; Tabelle mit Endhashes und Ketten: `docs/SIMULATION-CORE.md` §36.2.
- `drive` (60.000 Ticks) Sieg 24/24, Niederlage 24/24; `audit --ticks 900` 24/24 deterministisch und identisch nach Speichern/Laden, 0 Treffer auf fehlende Typen, Endproben 13/24 und 15/24; `reachability` sauber 24/24 und 24/24.
- Nur-Befehle-Agent (0 Weltschreibungen, alle 24 Karten gemessen): 15.000 Ticks **1 von 24** (`MD_GLA04`), 60.000 Ticks **3 von 24** (`MD_GLA04`, `MD_CHI01` @18.492, `MD_CHI02` @29.605); `MD_USA01` und `MD_USA04` bei 60.000 Ticks nach 3.600 s abgebrochen.

### Was die Abschluss-Sitzung entschieden hat

- **KI-Wirtschaft:** zweites Versorgungszentrum an einer anderen, sicheren Quelle, sobald der Vorrat zur Neige geht (§36.1). Erledigt.
- **Heereszusammensetzung:** nach Wert und Reichweite – zwei Messrunden, schlechter als der Zufall, zurückgenommen (§36.3). **Offen**, mit Befund.
- **KI-Schwierigkeit:** keine belegte Wirkung gilt im Skirmish (`Player::friend_applyDifficultyBonusesForObject` nur in `GAME_SINGLE_PLAYER`); die Einrichtung bietet eine Computerarmee, `SkirmishDifficultyIgnored` entfällt (§35). Erledigt durch Zusammenlegen.

### Offen (ändert die Simulation: hinter dem Programm, `HANDOFF-PHASE30.md` Punkt 2)

1. **Ein Agent, der Gefechte gewinnt.** Kampfwert je Ziel statt Preis, Teams statt Einzelkäufe (`AIPlayer::selectTeamToBuild`, Teamlisten), danach die Missionsabläufe je Karte (Eskorte `MD_GLA01`, Abwehrwellen `MD_CHI04`, Transport über Wasser `MD_USA04`, spät übergebene Einheiten `MD_USA03`/`Training01`, Eroberung ohne Eroberungseinheit `MD_GLA02`). Tabelle je Karte: `docs/SCRIPT-ENGINE.md` §9 „Phase 17“.
2. **Die vier verbliebenen Patts** der KI-Läufe (dreimal ChinaTankGeneral gegen AmericaAirForceGeneral, Armored Fury China/GLA): dieselbe Ursache wie Punkt 1.
3. **Solo-Boni der Kampagne:** `SOLO_AI_EASY/HARD` (Feuerrate 80 %/120 %) und `HumanSoloPlayerHealthBonus_Easy/Hard` (150 %/80 %) wendet das Original in Missionen auf leicht und schwer an, diese Simulation nicht (`MissionOptions.Difficulty` schaltet nur Skriptflaggen). Missionen auf „normal“ sind unberührt.
4. **Näherungen des Zielerwerbs** (`docs/SIMULATION-CORE.md` §33.5), `PowerSabotage`-Verbraucher, Bauplatz-Ablehnungsgründe.
5. **Leistung:** Profil der Missionsläufe (`mission-run` `MD_GLA05`: 147,9 s Werkzeuglaufzeit für 3.000 Ticks) – ein Profil ohne Verhaltensänderung ist Teil von Phase 18.

### Offen (braucht einen Menschen, jederzeit möglich)

Nichts aus Phase 15, 16 und 17 ist gesehen, gehört oder gespielt: Welt-Darstellung, Ton (`waveOut`), Mauszeiger, Wegpunkte, Texte, Shell-Übergänge, Mission im Client, Meldungsliste, Letterbox, Tooltips, selbst
feuernde Einheiten, Angriffsmarsch, die Skirmish-Einrichtung mit einer Computerarmee. Die Demos (`scripts/demo-*.ps1`) warten auf Rückmeldung; Befunde gehen in `docs/DEMO-FINDINGS.md` (Nummer, Ursache, Status).
Durchklicken Shell → Kampagne → Mission → Speichern/Laden → Abschluss und Skirmish-Setup → Match → Ergebnis: Bericht je Bildschirm „ging / ging nicht / ging mit Mängeln“.

### Werkzeuge und Fallstricke

- `CampaignTool drive … --commands-only --outcome victory --explain --timeline N`, `--watch TEXT`, `--watch-flag`; je Karte einzeln mit `--map "/NAME.map"`.
- `SimTool ai-match … --trace N --trace-army` zeigt Geld, Armee nach Typ und die Selbstauskunft der KI.
- Ein Komma in einem `-File`-Argument von `pwsh` wird kein Array; `git commit -F -` mit Here-String geht in PowerShell nicht (Nachricht in eine Datei); typografische Anführungszeichen („ “) beenden in PowerShell eine doppelt quotierte Zeichenkette – deutsche Texte in einfache Anführungszeichen oder in Dateien.
- Werkzeuge für lange Läufe aus einer Kopie des `bin`-Verzeichnisses starten; ein Test aus einem `-o`-Verzeichnis findet die Repo-Wurzel nicht (`SourceRules`).
- Mehrere Sitzungen committen gleichzeitig auf `main`: vor jedem Commit `git log -3`, vor dem Schreiben einer neuen Datei prüfen, ob sie inzwischen existiert.
