# Leistung der Spielschicht, Messzusammenfassungen und der Weg zu einem Simulations-Thread

Stand: Phase 19, Punkte 6, 7 und 8 (`docs/handoffs/HANDOFF-PHASE19.md`). Ergänzt `docs/PERFORMANCE.md` (Messwerkzeug der Simulation, ruhige
Ausgangsmessung); dort steht, wie `SimTool perf` misst. Dieses Dokument beschreibt

1. die maschinenlesbare Zusammenfassung der Messläufe und den Vergleich zweier Läufe (`--json`, `SimTool perf-compare`, §1 und §2),
2. die Messung der Spielschicht je Tick (`PresentationTool sync-perf`, §3), die gemessenen Werte (§4) und was daran abgetragen wurde (§5),
3. was dem im Weg steht, die Simulation auf einen eigenen Thread zu legen (§6).

**Alle Zeiten in diesem Dokument sind nicht ruhig gemessen.** Der Rechner wurde während der Läufe von vier weiteren Agenten benutzt
(CPU-Auslastung vor den Läufen 70–85 %). Belastbar sind die Verhältnisse innerhalb eines Laufs, die Zählgrößen (Allokation, GC-Läufe,
Objektzahlen) und die Prüfsummen; die absoluten Zeiten gehören auf einem ruhigen Rechner nachgemessen (Befehle in §7).

## 1. Maschinenlesbare Zusammenfassung (`--json`)

`SimTool perf`, `SimTool perf-mission` und `PresentationTool sync-perf` schreiben mit `--json <datei>` eine Zusammenfassung des Laufs.
`--label <text>` setzt einen Freitext hinein, üblicherweise den Commit; **das Werkzeug fragt git nicht** (`scripts/perf-profile.ps1`
übergibt ohne `-Label` den Kurz-Hash von `HEAD`).

Die Datei ist ein `ManifestDocument` (`tools/CnCNew.SimTool/ManifestDocument.cs`, dieselbe Form wie die Referenzmanifeste unter
`baselines/`): UTF-8 ohne BOM, LF, zwei Leerzeichen je Ebene, Schlüssel in ordinaler Reihenfolge, nur Texte, ganze Zahlen,
Wahrheitswerte und Objekte. Derselbe Lauf ergibt bis auf die Messwerte dieselben Bytes, auf jedem Rechner und unter jeder Kultur. Zeiten
sind deshalb **ganze Nanosekunden** (Feldname endet auf `Ns`), Größen ganze Bytes. Die reine Logik liegt in
`tools/CnCNew.SimTool/PerfSummary.cs` und wird als Quelltext in `CnCNew.PresentationTool` und in `CnCNew.Simulation.Tests`
eingebunden (keine neue Abhängigkeit).

Schema 1:

| Feld | Inhalt |
|---|---|
| `kind`, `schema` | `"perf-summary"`, `1`; alles andere wird abgelehnt |
| `tool` | `perf`, `perf-mission` oder `sync-perf` |
| `label` | Freitext des Aufrufers (Commit) |
| `hardware` | die Hardwarezeile der Ausgabe (Prozessor, logische Prozessoren, Speicher, Betriebssystem, Laufzeit, GC-Art, Uhrauflösung) |
| `parameters` | alles, was bestimmt, welcher Lauf das ist: `map`, `ticks` (Budget), `observer`, `modules`; bei `perf` zusätzlich `factions`, `aiSeed`, `worldSeed`; bei `perf-mission` `difficulty`, `worldSeed`; bei `sync-perf` `factions`, `seed`, `viewport`, `hud`, `digest` |
| `results` | `ticks` (tatsächlich gelaufen) und `finalHash` immer; bei `perf` und `perf-mission` `chainHash` und `incrementalEqualsFull`, bei `perf` dazu `winner`, `victoryTick`, bei `perf-mission` `outcome`; bei `sync-perf` `winner` und mit `--digest` `presentationDigest` |
| `tick` | `meanNs`, `medianNs`, `p95Ns`, `p99Ns`, `maxNs` des ganzen Schritts |
| `phases.<Name>` | dieselben fünf Werte je Phase (`TickPhase` bzw. `PresentationPhase`); dazu `(unattributed).meanNs` und `(world).meanNs` bzw. `(game layer).meanNs` |
| `modules.<Name>` | die teuersten Modultypen der Update-Phase (`--top`): `calls`, `totalNs`, `meanNs`, `maxNs` |
| `allocation` | `bytesPerTickMean`, `bytesPerTickMedian`, `bytesPerTickMax`, `gen0`, `gen1`, `gen2` |
| `objects` | `min`, `max`, `end`; bei `sync-perf` zusätzlich `drawablesMax`, `drawablesEnd`, `particlesMax`, `soundCues` |
| `replay` | nur mit `--replay`: `perTickNs`, `ticksPerSecond` (Aufholtempo = Ticks je Sekunde / Tickrate), `bytesPerTickMean`, `gen0..2`, `hashEqualsLive` |
| `snapshot` | `bytes`, `bytesPerObject`, `writeNs`, `restoreNs`, `restoredHashEqual` |

Getestet in `tests/CnCNew.Simulation.Tests/Tests.PerfSummary.cs` (kanonische Form, Hin- und Rückweg über eine Datei, strenge Prüfung
von `kind`/`schema`).

## 2. Vergleich zweier Läufe (`SimTool perf-compare`)

```powershell
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- perf-compare <alt.json> <neu.json> [--threshold-percent N] [--min-us N] [--all] [--allow-different-hash] [--out <datei>]
pwsh scripts/perf-profile.ps1 -Label "<commit>" -OutDir <verzeichnis>     # schreibt je Lauf <name>.txt, <name>.csv, <name>.json
pwsh scripts/perf-compare.ps1 <altesVerzeichnis> <neuesVerzeichnis> [-ThresholdPercent N] [-AllowDifferentHash]
```

`perf-compare` druckt eine Tabelle alt / neu / Änderung je Kennzahl und markiert Änderungen über der Schwelle (Standard 10 %) als
`REGRESSION` oder `better`:

- **Niedriger ist besser** für alle Zeiten, Allokationen, GC-Läufe und Snapshotgrößen; **höher ist besser** für `ticksPerSecond`.
  Reine Zählgrößen (`objects`, `results`, `calls`) werden gezeigt, aber nie bewertet.
- Zeiten, die auf **beiden** Seiten unter 1 µs liegen (`--min-us`), werden nicht bewertet: die Uhr löst 100 ns auf, eine Phase von
  0,3 µs verdoppelt sich zufällig.
- Ohne `--all` zeigt die Tabelle je Phase das Mittel und je Modul die Summe; `--all` zeigt jede Kennzahl.
- **Abgelehnt** wird der Vergleich (Exitcode 2, Zeile `REFUSED: …`), wenn die Zusammenfassungen von verschiedenen Befehlen stammen,
  wenn sich `parameters` unterscheidet (mit Feld und beiden Werten) oder wenn der **Endhash** verschieden ist. Ein anderer Endhash heißt:
  das ist ein anderer Lauf, die Simulation hat nicht dieselbe Arbeit getan, die Zeiten sagen nichts über Geschwindigkeit. Mit
  `--allow-different-hash` kommt die Tabelle trotzdem, unter einer Zeile `WARNING: DIFFERENT FINAL HASH: …`.
- Eine andere Hardwarezeile ist eine Warnung (`not comparable`), kein Abbruch.
- **Exitcode 0, sobald eine Tabelle gedruckt wurde – auch mit Verschlechterungen.** Es gibt bewusst keinen fehlschlagenden Test aus
  Zeiten (`docs/handoffs/HANDOFF-PHASE19.md` Punkt 8): sie hängen vom Rechner und seiner Last ab. Exitcode 1 nur bei unlesbarer Eingabe.

Beispiel (zwei Läufe **desselben** Stands auf dem belasteten Rechner, gekürzt – so groß ist hier die Streuung):

```
perf-compare: perf, old "run-a" against new "run-b"; threshold 10 %, times below 1 us on both sides are not judged.
parameters: aiSeed=1592590342, factions=FactionAmerica,FactionAmericaAirForceGeneral, map=Maps/Scorched Earth/Scorched Earth.map, modules=true, observer=true, ticks=3000, worldSeed=3237998086

| metric | old | new | delta | |
|---|---:|---:|---:|---|
| tick.maxNs | 66279.9 us | 63232.5 us | -4.6 % |  |
| tick.meanNs | 1036.1 us | 1114.0 us | +7.5 % |  |
| tick.p99Ns | 3505.5 us | 4545.7 us | +29.7 % | REGRESSION |
| phases.Hash.meanNs | 142.4 us | 164.7 us | +15.7 % | REGRESSION |
| phases.Updates.meanNs | 472.0 us | 475.2 us | +0.7 % |  |
| allocation.bytesPerTickMean | 4632 | 4631 | 0.0 % |  |
| replay.ticksPerSecond | 3162 | 2992 | -5.4 % |  |
| snapshot.writeNs | 2741.9 us | 2014.7 us | -26.5 % | better |
| results.ticks | 3000 | 3000 | 0.0 % |  |
...
20 regression(s) and 2 improvement(s) beyond 10 % in 61 metrics. Times depend on the machine and its load: this is a report, not a test.
```

`scripts/perf-compare.ps1` ruft `perf-compare` für jeden Lauf auf, dessen `<name>.json` in beiden Verzeichnissen liegt, schreibt je
Lauf `compare-<name>.txt` und eine Übersicht `compare.md` (Mittel, 99 %, Maximum, Allokation, Wiedergabetempo, Zahl der markierten
Änderungen, Endhash). Läufe, die nur auf einer Seite liegen, und abgelehnte Vergleiche stehen als solche in der Tabelle. Das Skript
scheitert nie an einer Zeit. Getestet ist die Vergleichslogik in `Tests.PerfSummary.cs` (Markierung, Schwelle, Rauschgrenze, Ablehnung
bei anderen Parametern, anderem Befehl und anderem Endhash, fehlende Kennzahl, Tabellenform).

Ab jetzt gehört zum Phasenabschluss: `scripts/perf-profile.ps1` auf ruhigem Rechner, das Verzeichnis aufbewahren und mit dem der
vorigen Phase vergleichen.

## 3. Messung der Spielschicht (`PresentationTool sync-perf`)

```powershell
dotnet build -c Release tools/CnCNew.PresentationTool
dotnet run -c Release --no-build --project tools/CnCNew.PresentationTool -- sync-perf game/content --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 18000 --hud --json <datei.json> --label "<commit>"
dotnet run -c Release --no-build --project tools/CnCNew.PresentationTool -- sync-perf game/content --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 18000 --hud --digest
```

Optionen: `--map`, `--players N` (die ersten N spielbaren Fraktionen, wie `SimTool perf`) oder `--factions A,B,…`, `--ticks`, `--seed`,
`--viewport BxH` (Standard 1280x720), `--hud`, `--digest`, `--json`, `--label`.

Das Werkzeug startet ein `LiveMatch` ohne Fenster. Der Platz des Betrachters (Spieler 1) wird ebenfalls vom Computer gespielt
(`MatchSetup.AutoPlay`), damit ist es **dasselbe Match wie `SimTool perf` mit denselben Spielern** – der Endhash der Welt muss gleich
sein und ist es (Twilight Flame, 8 Spieler, 18.000 Ticks: `72CF0F846E0D9AA6` in beiden Werkzeugen). Je Schritt ruft es genau das auf,
was der Client je Bild aufruft: `LiveGame.Advance` mit der Länge eines Ticks. Das sind ein `BeforeTick`, ein Simulationsschritt, ein
`SyncAfterTick`, die `TickStepped`-Abonnenten, das Ausblenden des Nebels und **eine** `FrameProjector.Project` – ein Bild je Tick als
Stellvertreter für 30 Bilder je Sekunde. Bei 60 Hz Bildrate und 30 Hz Tickrate fallen die Bildanteile (`FogAdvance`, `Project`, HUD)
doppelt so oft an wie die Tickanteile.

Die Uhr liest das Werkzeug. `LiveGame.Advance` und `FrameProjector.Advance` melden einem optionalen Beobachter nur, wo ihre Teile
beginnen und enden (`IPresentationPhaseObserver`, `PresentationPhase` in `src/CnCNew.Presentation/PresentationPhase.cs`;
`LiveGame.PhaseObserver`). Im Spiel ist der Beobachter `null`: eine Nullprüfung je Teil, keine Allokation, kein anderer Ablauf.

Die Teile, benannt wie im Code (eingerückt = im Teil darüber enthalten):

| `PresentationPhase` | Wo | Inhalt |
|---|---|---|
| `BeforeTick` | `LiveGame.Advance` | der `BeforeTick`-Rückruf: mit HUD `GameInputController.Tick` (Auswahl bereinigen, stehende Befehle), sonst leer |
| `StepSimulation` | | der Sitzungsschritt (KI plus Weltschritt) |
| `SyncAfterTick` | | alles Folgende bis `FogUpdate` |
| – `ProjectorObserve` | `FrameProjector.Observe` | Ereignisse des Ticks für Einheitenzustände und Auswahlblinken |
| – `ProjectorAdvance` | `FrameProjector.Advance` | die vier folgenden Teile plus das Aufräumen alle 150 Ticks |
| – – `ProjectorAdvanceVisibleEntities` | `SimulationWorld.EntitiesVisibleTo` | Liste der für den Betrachter sichtbaren Objekte (Code der Simulation) |
| – – `ProjectorAdvanceCapture` | `CaptureEntity` je Objekt | Modellzustände, Zustandswahl, Türme, Federung |
| – – `ProjectorAdvanceGhosts` | `UpdateGhosts` | gemerkte Feindgebäude |
| – – `ProjectorAdvanceScenery` | `UpdateSceneryExploration` | welche Szenerie erkundet ist und welche im Nebel liegt |
| – `FxViewRefresh` | `SimulationFxWorldView.Refresh` | Lage jedes Objekts für die Effekte |
| – `FxExecuteEvents` | `FxExecutor.ExecuteEvents` | Effekte und Tonanforderungen der Ereignisse |
| – `FxUpdate` | `FxExecutor.Update` | Partikel, Leuchtspuren, Lichter, Decals |
| – `DrainSoundCues` | `FxExecutor.DrainSoundCues` | Übergabe an die Liste des Spiels |
| – `FogUpdate` | `FogOfWarGrid.Update` | Zellzustände aus der Sichtkarte, jeden dritten Tick |
| `TickStepped` | `LiveGame.Advance` | die Abonnenten (mit HUD Statistik und Meldungen) |
| `FogAdvance` | `FogOfWarGrid.Advance` | Ausblenden der Helligkeitsstufen, je Bild |
| `OnAdvanced` | | Skriptkamera einer Mission, je Bild |
| `Project` | `FrameProjector.Project` | das `RenderFrame` mit Interpolation, je Bild |
| `HudUpdate`, `HudBuildDrawList` | vom Werkzeug gemessen, nur mit `--hud` | `MatchController.Update` und `BuildDrawList`, je Bild |

**Prüfsumme der Darstellung (`--digest`).** Je Tick werden außerhalb der gemessenen Zeit in ein SHA-256 gegeben: jedes `DrawableInstance`
des Bildes mit allen Feldern (Position, Winkel, Besitzer, Farbe, Zustandsbits, gewählte Zustände, Türme, Federung, Blinken …), die
Helligkeitsstufen des Nebels (`FogOfWarGrid.CopyLevels`), das Effektbild (`FxExecutor.Capture`: Partikel als rohe Bytes, Leuchtspuren,
Lichter, Decals, Strahlen, Erschütterungen) und die Tonanforderungen des Ticks. Zwei Stände mit derselben Prüfsumme haben über den
ganzen Lauf dieselbe Darstellung erzeugt. Das ist der Beleg für „ausgabeneutral“ in §5. Nicht enthalten: die Zeichenliste der HUD
(dort wurde nichts geändert) und alles, was erst die Stride-Schichten aus dem Bild machen.

**Grenzen.** Mit `--hud` hängt ein echter `MatchController` am Match, aber ohne Eingaben, ohne Auswahl und ohne Mauszeiger über der
Welt: die Befehlsleiste ist leer. Eine volle Auswahl kostet mehr (`CommandBarModel.Build` prüft jeden Knopf). Ton (`AudioDirector`), die
Stride-Schichten und die Minikarte sind nicht enthalten. Die Allokation je Tick ist die Summe aus Simulation und Spielschicht (ein
Thread).

## 4. Messwerte (nicht ruhig gemessen)

Gemessen am 6. Oktober 2026 mit `PresentationTool sync-perf … --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 18000 --hud`
(486 × 469 Zellen, 632 Kartenobjekte, Bild 1280 × 720, HUD ohne Auswahl). Rechner: AMD64 Family 25 Model 68 (Ryzen 7 PRO 6850U),
16 logische Prozessoren, 28 GiB, Windows 10.0.26200, .NET 10.0.12, Workstation-GC, Uhrauflösung 100 ns. **Nicht ruhig**: vier weitere
Agenten bauten und maßen gleichzeitig; derselbe Stand lief im Simulationsschritt zwischen 9,5 und 17,6 ms je Tick im Mittel
(ruhig laut `docs/PERFORMANCE.md` §6: rund 5 ms bei 54.000 Ticks). In allen Läufen: 18.000 Ticks, kein Sieger, Endhash der Welt
`72CF0F846E0D9AA6` (wie `SimTool perf` für dieses Match), 28 bis 268 Objekte (Ende 234), 2 bis 151 Zeichenobjekte je Bild (Ende 106),
höchstens 1.468 Partikel, 43.255 Tonanforderungen.

### 4.1 Aufteilung vor den Änderungen (Stand `5d0aac2`, der Lauf mit der geringsten Last)

Mikrosekunden je simuliertem Tick; eingerückte Zeilen sind in der Zeile darüber enthalten.

| Teil | Mittel | Median | 95 % | 99 % | Maximum | Anteil am Tick |
|---|---:|---:|---:|---:|---:|---:|
| Tick insgesamt (`Advance` plus HUD) | 11.567,1 | 9.782,1 | 19.737,1 | 38.119,8 | 701.284 | 100,0 % |
| `BeforeTick` | 3,8 | 2,8 | 5,8 | 10,3 | 5.515 | 0,0 % |
| `StepSimulation` | 9.507,9 | 7.564,2 | 17.403,0 | 33.758,3 | 698.674 | 82,2 % |
| `SyncAfterTick` | **907,7** | 725,0 | 1.766,3 | 2.178,0 | 35.240 | 7,8 % |
| – `ProjectorObserve` | 2,4 | 2,3 | 4,5 | 6,0 | 1.654 | 0,0 % |
| – `ProjectorAdvance` | 374,8 | 343,3 | 610,7 | 844,0 | 28.157 | 3,2 % |
| – – `ProjectorAdvanceVisibleEntities` | 35,9 | 34,4 | 59,3 | 79,2 | 6.230 | 0,3 % |
| – – `ProjectorAdvanceCapture` | 226,5 | 199,9 | 412,2 | 545,5 | 26.656 | 2,0 % |
| – – `ProjectorAdvanceGhosts` | 10,7 | 9,1 | 20,3 | 27,7 | 3.228 | 0,1 % |
| – – `ProjectorAdvanceScenery` | 98,6 | 95,5 | 126,2 | 200,2 | 7.259 | 0,9 % |
| – `FxViewRefresh` | 36,8 | 32,2 | 66,7 | 92,1 | 1.994 | 0,3 % |
| – `FxExecuteEvents` | 59,6 | 56,6 | 137,9 | 177,5 | 11.340 | 0,5 % |
| – `FxUpdate` | 125,8 | 107,6 | 286,7 | 389,2 | 16.675 | 1,1 % |
| – `DrainSoundCues` | 1,7 | 1,8 | 3,2 | 4,3 | 48 | 0,0 % |
| – `FogUpdate` (jeden dritten Tick) | 305,8 | 0,1 | 1.050,0 | 1.202,1 | 20.064 | 2,6 % |
| `TickStepped` | 6,2 | 4,3 | 12,8 | 19,3 | 3.448 | 0,1 % |
| `FogAdvance` (je Bild) | **733,7** | 738,3 | 903,6 | 1.033,7 | 15.336 | 6,3 % |
| `OnAdvanced` (je Bild) | 0,2 | 0,1 | 0,5 | 1,0 | 60 | 0,0 % |
| `Project` (je Bild) | 29,3 | 24,4 | 45,5 | 67,5 | 10.963 | 0,3 % |
| `HudUpdate` (je Bild) | 111,5 | 100,9 | 174,8 | 227,2 | 16.847 | 1,0 % |
| `HudBuildDrawList` (je Bild) | 265,3 | 238,0 | 407,7 | 573,0 | 35.258 | 2,3 % |
| **Spielschicht allein** (Tick ohne `StepSimulation`) | **2.059,3** | | | | | 17,8 % |

Allokation (Simulation und Spielschicht zusammen): 173.881 Byte je Tick im Mittel, 180.440 im Median, 570.320 im Maximum; GC 377/58/2.
Zum Vergleich: `SimTool perf` maß für dasselbe Match 81.898 Byte je Tick für die Simulation allein (`docs/PERFORMANCE.md` §7.3, älterer
Stand, ebenfalls nicht ruhig) – die Spielschicht allokiert demnach etwa ebenso viel wie die Simulation, rund 90 KB je Tick.

Was die Tabelle sagt:

1. `SyncAfterTick` kostet 0,9 ms je Tick, die Spielschicht insgesamt 2,1 ms: im selben Lauf 22 % des Simulationsschritts obendrauf.
   Erreicht die Simulation das Ziel der Phase 19 (1 ms je Tick), wäre die unveränderte Spielschicht **so teuer wie die Simulation
   oder teurer**.
2. Die beiden größten Posten hingen an der **Kartenfläche**, nicht am Geschehen: `FogAdvance` (jede Zelle je Bild) und `FogUpdate` (jede
   Zelle jeden dritten Tick), zusammen 1,04 ms = die Hälfte der Spielschicht.
3. Der Rest verteilt sich: HUD 377 µs je Bild schon ohne Auswahl, Aufnahme der Objekte 227 µs, Effekte 222 µs, Szenerie 99 µs.
4. Die Maxima (15 bis 35 ms in einzelnen Teilen) fallen mit Speicherbereinigungen und der Last des Rechners zusammen und sind hier
   nicht auswertbar.

### 4.2 Vorher und nachher, unmittelbar nacheinander gemessen

Stand `5d0aac2` (vor den Änderungen) und `b989808` (danach), zwei Läufe direkt hintereinander; trotzdem lag die Last im zweiten Lauf
niedriger: die **unveränderten** Teile liefen 15 bis 27 % schneller (`StepSimulation` −21,5 %, `FxViewRefresh` −24,2 %, `HudUpdate`
−23,9 %). Dagegen sind die geänderten Teile zu lesen. Mittel in Mikrosekunden je Tick (`SimTool perf-compare ab-base.json ab-final.json`):

| Teil | vorher | nachher | Änderung | Bemerkung |
|---|---:|---:|---:|---|
| `StepSimulation` | 15.337,8 | 12.033,9 | −21,5 % | unverändert: Maß für die Last |
| `FxViewRefresh` | 69,0 | 52,3 | −24,2 % | unverändert |
| `HudUpdate` + `HudBuildDrawList` | 653,2 | 505,5 | −22,6 % | unverändert |
| `FogAdvance` | 1.095,9 | **10,0** | **−99,1 %** | §5.1 |
| `ProjectorAdvanceScenery` | 147,0 | **67,2** | **−54,3 %** | §5.2 |
| `ProjectorAdvanceCapture` | 367,0 | **176,7** | **−51,8 %** | §5.3 |
| `FogUpdate` | 434,7 | 472,7 | +8,7 % | siehe unten |
| `SyncAfterTick` | 1.380,0 | 1.052,0 | −23,8 % | |
| **Spielschicht allein** | **3.193,5** | **1.617,9** | **−49,3 %** | 17,2 % → 11,9 % des Ticks |
| Allokation je Tick (Byte, exakt) | 173.885 | **165.950** | −4,6 % | GC 377/75/3 → 359/58/2 |

Auf die Last des ersten Laufs aus §4.1 umgerechnet (Faktor aus den unveränderten Teilen) liegt die Spielschicht nach den Änderungen
bei grob **1,1 bis 1,2 ms je Tick statt 2,06 ms**; das ist eine Schätzung, kein Messwert.

**`FogUpdate`** hat durch §5.1 einen Vergleich je Zelle mehr. Im Laufpaar oben steigt es um 8,7 %, während die Last sank. Drei
abwechselnde Kurzläufe (2.400 Ticks, je Stand) zeigen aber keinen Unterschied, der aus der Streuung herausragt: das 95-%-Quantil –
die Kosten einer Auffrischung – lag vorher bei 1.161 / 7.870 / 1.266 µs und nachher bei 1.417 / 1.229 / 1.262 µs. Ein Versuch, die
Schleife umzustellen (Zustand nur bei Änderung schreiben, seltener Pfad ausgelagert), brachte in denselben Läufen 1.163 / 1.222 /
8.916 µs, also nichts Messbares, und wurde nicht übernommen. Ob `FogUpdate` um einige Prozent teurer geworden ist, ist auf diesem
Rechner **nicht entscheidbar**; gegen die 0,7 bis 1,1 ms, die `FogAdvance` je Bild spart, fällt es nicht ins Gewicht.

### 4.3 Hochrechnung

Bei 60 Ticks je Sekunde und einem Bild je Tick: Spielschicht vorher 2,06 ms × 60 = 12,4 % eines Kerns (Lauf §4.1), nachher nach der
Schätzung oben rund 7 %. Bei 30 Hz Tickrate und 60 Bildern je Sekunde fallen die Bildanteile doppelt an: vorher
`FogAdvance` + `Project` + HUD = 1,14 ms je Bild = 6,8 % eines Kerns allein dafür, nachher rund 0,4 ms = 2,5 %.

## 5. Was abgetragen wurde und warum es ausgabeneutral ist

Jede Änderung ist ein eigener Commit mit einer Ursache. Beleg der Neutralität für alle drei: die Prüfsumme der Darstellung über
18.000 Ticks (Twilight Flame, 8 Spieler, `--hud --digest`) ist vor und nach jeder Änderung
`65AE95BEE88937CA2F903C39AED954C814A3523619BB51C35D7A85D49B7DAF44`, der Endhash der Welt `72CF0F846E0D9AA6`. Dazu auf dem Endstand
gegen den Stand davor: die drei Szenen von `scenario-audit` (`capture-building`, `tank-duel`, `jet-strafe`) bestehen und drucken
**byteidentische** Ausgaben (der ganze Zeitverlauf, nicht nur das Ergebnis); `object-audit` und `fx-audit` drucken byteidentische
Ausgaben (SHA-256 der Ausgabe `032CF63D…B3CFEABE` bzw. `5504C152…CE4BEDF59`), `terrain-audit` bis auf die eine Zeile
`build time total (not deterministic)`; `tests/CnCNew.Presentation.Tests` und `tests/CnCNew.Game.Tests` (Release, mit Inhalt)
bestehen (`All game tests passed.`, Exitcode 0). Die Simulation wurde nicht angefasst.

Die Vorher/Nachher-Zeiten je Änderung stammen aus Läufen unter wechselnder Last; daneben steht jeweils eine unveränderte Phase
desselben Laufpaars als Maß für die Last.

### 5.1 Der Nebel blendet nur Zellen aus, die noch unterwegs sind (`a15dc3b`)

`FogOfWarGrid.Advance` lief **je Bild** über jede Zelle der Karte (Twilight Flame: 486 × 469 = 227.934 Zellen) und rechnete für jede
die Ausblendregel. Eine Zelle, deren Stufe die Stufe ihres Zustands erreicht hat, ist ein Fixpunkt dieser Regel
(`Math.Max(ziel, ziel - schritt) == ziel`). Das Gitter führt deshalb eine Liste der Zellen, die noch unterwegs sind: eine Zelle
kommt hinein, wenn ihr Zustand auf einen mit anderer Stufe wechselt (`SetState`), und fällt heraus, wenn ihre Stufe ankommt.
Die Reihenfolge spielt keine Rolle (Zellen sind unabhängig), `Snap` leert die Liste.

- `FogAdvance`: Mittel **733,7 µs → 15,4 µs** je Bild (Median 738,3 → 8,5), obwohl der zweite Lauf unter etwa doppelter Last lief
  (`StepSimulation` 9.508 → 17.647 µs, `FxViewRefresh` 36,8 → 75,3 µs).
- `FogUpdate` hat einen Vergleich je Zelle mehr (alter gegen neuer Zustand): 305,8 → 649,2 µs im selben Laufpaar, also im Rahmen
  der Last (Faktor 2,1 gegenüber 1,9 bis 2,0 der unveränderten Phasen); das Laufpaar unter gleicher Last steht in §4.
- Neutral, weil für ruhende Zellen derselbe Wert herauskäme und für die übrigen dieselbe Rechnung mit denselben Zahlen läuft. Neuer
  Test `VisionTests.FadingEqualsTheFullPass`: das Gitter gegen die schlichte Regel über alle Zellen, 400 Runden mit Zustandswechseln
  mitten im Ausblenden, Schritten der Länge 0, `Snap` und dem Weg über die `VisionMap`, für drei Einstellungen (darunter gleiche Stufen
  für zwei Zustände).

### 5.2 Szeneriepositionen werden einmal in Festkomma gewandelt (`9c77402`)

`UpdateSceneryExploration` wandelte **in jedem Tick** die Position jedes nicht übernommenen Szenerieobjekts von `float` über `decimal`
nach `Fixed` (zwei `decimal`-Wandlungen je Objekt; Twilight Flame hat 632 Kartenobjekte), bevor es die Sichtkarte fragte. Szenerie
bewegt sich nie: die beiden Koordinaten werden einmal im Konstruktor gewandelt (wie bisher nur mit Betrachter) und aus einem Feld
gelesen.

- `ProjectorAdvanceScenery`: Mittel **199,4 → 82,0 µs**, Median 122,1 → 57,8 (zwei Läufe unter gleicher Last).
- Neutral, weil dieselbe Wandlung desselben `float` denselben `Fixed` ergibt.

### 5.3 Die gewählten Modellzustände bleiben, solange Visual und Zustandsbits bleiben (`b989808`)

`FrameProjector.CaptureEntity` wählte in jedem Tick für jedes sichtbare Objekt je Zeichenmodul den besten Modellzustand
(`BestStateSelector.FindBest` über alle Zustände, über LINQ mit einem neuen Array). Die Wahl ist eine reine Funktion des `ObjectVisual`
und der Zustandsbits. Ein Objekt, dessen Bits sich seit der vorigen Aufnahme nicht geändert haben, übernimmt die Wahl der vorigen
Aufnahme; die Wahl selbst ist jetzt eine einfache Schleife.

- `ProjectorAdvanceCapture`: Mittel **377,7 → 307,2 µs**, Median 264,3 → 176,3 (zwei Läufe unter etwa gleicher Last).
- Allokation je Tick (Simulation und Spielschicht zusammen, exakte Zählung): **174.039 → 166.161 Byte**.
- Neutral, weil für dieselben Eingaben dasselbe unveränderliche Array herauskommt (gleiche Instanz des Visuals, gleiche Bits).

### 5.4 Was nicht abgetragen wurde und warum

| Stelle | Kosten (Lauf vor den Änderungen, §4) | Warum nicht |
|---|---:|---|
| `FogUpdate` | 306 µs im Mittel, rund 1,05 ms an jedem dritten Tick | Die Sichtkarte bietet nur Abfragen je Zelle (`VisionMap.IsVisible`/`IsExplored`); 227.934 Zellen × bis zu zwei Aufrufe. Schneller ginge es nur mit einer Sammelabfrage oder einem Änderungszähler in `VisionMap` – das ist Code der Simulation und in dieser Phase Sache des Sicht-Auftrags. Vorschlag: `VisionMap` gibt die Zellbytes eines Spielers als `ReadOnlySpan<byte>` heraus oder führt die Menge der seit der letzten Auffrischung geänderten Zellen; `FogOfWarGrid.SetState` ist dafür schon vorbereitet. |
| `ProjectorAdvanceVisibleEntities` | 36 µs | `SimulationWorld.EntitiesVisibleTo` (neue Liste und Sortierung je Tick): Code der Simulation. |
| `ProjectorAdvanceCapture` (Rest) | rund 150–200 µs | `ConditionDeriver.Derive` liest je Objekt Status, Module, Waffen, Upgrades und Gelände; die Werte können sich in jedem Tick ändern, eine Zwischenspeicherung bräuchte eine Änderungsmeldung der Simulation je Objekt. Die Aufnahme (`Capture`, eine Instanz je Objekt und Tick) ist Teil der Interpolation zwischen zwei Ticks. |
| `FxUpdate`, `FxExecuteEvents` | 126 µs und 60 µs | Partikelsimulation (bis 1.468 Partikel) und Effekte der Ereignisse; die Reihenfolge der Zufallszahlen legt die Ausgabe fest. Kein wiederholter Aufwand gefunden. |
| `FxViewRefresh` | 37 µs | Die Lage jedes Objekts wird je Tick kopiert, damit Todeseffekte die letzte Lage eines schon entfernten Objekts finden. Welche Objekte im nächsten Tick sterben, ist vorher nicht bekannt. |
| `HudUpdate`, `HudBuildDrawList` | 112 µs und 265 µs je Bild, bei leerer Auswahl | Die HUD baut je Bild Befehlsleiste, Spielerwerte und die ganze Zeichenliste des Fensterbaums neu. Ein Zwischenspeicher mit genauer Invalidierung wäre möglich, ist aber ein eigener Auftrag: die Prüfsumme dieses Werkzeugs deckt die Zeichenliste nicht ab, und der teure Fall (volle Auswahl, Zeiger über der Welt) wird hier nicht gemessen. |
| `Project` | 29 µs je Bild | Ein `DrawableInstance` je Objekt und Bild ist der Vertrag des `RenderFrame` (unveränderlich). |

## 6. Was einem eigenen Simulations-Thread im Weg steht

Nicht gebaut; `SourceRules` verbietet Threads im Kern, und das bleibt so. Die Frage ist, ob der **Client** `StepSimulation` auf einem
zweiten Thread laufen lassen könnte, während der erste zeichnet. Heute läuft alles auf einem Thread, in dieser Reihenfolge je Bild
(`ClientRuntime.Update` → `RunningGame.Step`):

1. Eingabeereignisse → `MatchController.Process` (lesen die Welt, reihen Befehle ein).
2. `LiveGame.Advance`: bis zu acht Ticks, je Tick `BeforeTick` → `StepSimulation` → `SyncAfterTick` → `TickStepped`; danach
   `Fog.Advance`, `OnAdvanced`, `Project`.
3. `MatchController.Update` (HUD), dann die Schichten Gelände, Objekte, Effekte mit `LiveGame.ObjectFrame`.
4. Eingabeskript, Minikartenpunkte, `AudioDirector.Update`.
5. Beim Zeichnen `MatchController.BuildDrawList` (Tooltips, Superwaffen-Zähler, Missionsoverlay).

### 6.1 Was schon unveränderlich ist

- `RenderFrame`/`DrawableInstance` (`FrameProjector.Project`): die Stride-Schichten `ObjectLayerCore`, `FxLayer` und `TerrainLayer`
  lesen **nie** die Welt, nur `ObjectFrame`, `Fx.Capture`, `Fog`, `Camera`, `Heights`, `TerrainModel`.
- `TickResult.Events` für Statistik (`MatchStatistics.Record`).
- `FrameProjector` hält zwischen `Advance` und `Project` nur eigene Aufnahmen (`Capture`: Zahlen, `Fixed`, unveränderliche `ObjectVisual`),
  keine `Entity`. Außerhalb der Simulation hält **niemand** eine `Entity` über einen Aufruf hinaus (Auswahl, stehende Befehle,
  Befehlsquellen: nur Ids).
- `CnCNew.Audio` und `CnCNew.Ui` kennen die Simulation gar nicht.

### 6.2 Lesezugriffe auf die laufende, veränderliche Welt

Zählung (grep über `src`, Muster `world.`/`World.` mit Member, `FindEntity`, `EntitiesVisibleTo`, `IsPositionVisibleTo`,
`IsPositionExploredBy`, `SimulationQueries.`, `Session.`, `.Engine.`; Kommentarzeilen und ein paar gleichnamige Parameter eingeschlossen,
also eine Obergrenze): **`CnCNew.Presentation` 213 Stellen in 19 Dateien, `CnCNew.Game` 113 in 20, `CnCNew.Client.Stride` 22 in 4,
zusammen rund 350 Stellen in 43 Dateien.** `SimulationQueries` allein hat rund 80 Aufrufstellen in 10 Dateien.

| Wann | Wer | Was gelesen wird | Stellen (grobe Aufteilung der Zählung nach dem Ort, an dem der Code der Datei läuft) |
|---|---|---|---:|
| **im Tick**, vor dem Schritt (`BeforeTick`) | `GameInputController.Tick` → `SelectionModel.Prune`, `CommandModeController.Update`, `StandingOrders.Update` | `FindEntity`, Besitzer, Status, `CanPlayerSee`, `IsAttackMoving`, `AttackTargetOf`, `ChooseWeapon`, Reichweite, Winkel, Tick; **reiht dabei Befehle ein** | 35 |
| **im Tick**, nach dem Schritt (`SyncAfterTick`) | `FrameProjector.Advance` mit `ConditionDeriver`, `StructureConditions`, `SuspensionTracker`, `UnitConditionTracker`, `TurretTracker`, `CaptureConditions`; `FrameProjector.Observe` | `EntitiesVisibleTo`, je Objekt Lage, Körper, Status, Module, Waffenzustand, Türme, Baufortschritt, Upgrades, Docks, Geländehöhe; `IsPositionVisibleTo`/`IsPositionExploredBy` für Geister und Szenerie | 50 |
| | `SimulationFxWorldView.Refresh` und `GetPose`/`GroundHeight` (aus `FxExecutor.ExecuteEvents`, `ParticleWorld.Step`, `SoundCues`) | alle Objekte (Lage, Besitzer, Rang), `FindEntity`, Geländehöhe | 4 |
| | `FogOfWarGrid.Update` | jede Zelle der Sichtkarte des Betrachters | 2 |
| **im Tick** (`TickStepped`) | `MatchStatsTracker.Observe`, `HudMessageFeed.Observe`, `InputScript` | `FindEntity` zu Opfer, Täter, Einzahler; Spielerliste; im Testskript ein voller Objektdurchlauf | 15 |
| **im Tick**, nur Missionen | `MissionPresentationDirector.BeforeStep`/`AfterStep` | `Engine.DrainOutputs`, `Engine.NamedObjects`, `TeamInstances`, `FindEntity`, Lage; steuert HUD, Blinken, Ton | 20 |
| **je Bild** nach `Advance` | `MatchController.Update`: `CommandModeController.Hover`/`Evaluate`, `EntityPicker.Pick`, `CommandBarModel.Build`/`Hud`, `ComputeOverlay`, `OrderMarkers` | Objekt unter dem Zeiger (voller Durchlauf mit Sichtprüfung), je ausgewähltem Objekt und Knopf: Module, Produktionswarteschlange über `Describe`, Voraussetzungen (voller Durchlauf), Geld, Strom, Wissenschaften, Upgrades, Spezialkräfte, Bereitschaftsticks; Sammelpunkte; Gesundheitsbalken; Platzierprüfung (Navigationsgitter, Erkundung) | 110 |
| **je Bild**, beim Zeichnen | `MatchController.BuildDrawList`: `SuperweaponTimers.Collect`, `CommandButtonDescription.Build`, `RefreshDynamicWindows` | voller Objektdurchlauf je Tick, Spezialkraft-Bereitschaft, Preise, fehlende Voraussetzungen | 15 |
| **je Bild** | `AudioDirector` über `LiveAudioView` | `EnergyProductionOf`, `RelationshipOf`, `Tick` (alles andere kommt aus `Frame`, `DrainSoundCues`, Kamera) | 3 |
| **je Bild**, Mission | `MissionPresentationDirector.ApplyCameraTo`, `BuildOverlayCommands` | Lage eines benannten Objekts für die Kamera, `Engine.CounterValue` für Zähler | 5 |
| **bei Eingabe** | `GameInputController.Process`/`DispatchMeta`, `SelectionModel` (Klick, Rahmen, Doppelklick, Gruppen), `EntityPicker`, `CommandModeController.IssueContext`/`Activate`/`CompleteTarget`/`PlacementRelease`, `GameAudio.OnSelectionChanged` | volle Objektdurchläufe mit Sichtprüfung und Projektion, Beziehungen, Container, Waffenwahl; **reiht Befehle ein und braucht die Antwort sofort** | 75 |
| nur Aufbau und Ende | Konstruktoren, `LiveScenario`-Bühne, Speichern (`SaveGame.Write` liest die ganze Welt), Matchergebnis | – | 20 |

Geteilte veränderliche Objekte der Darstellung selbst (heute unkritisch, weil ein Thread): `FogOfWarGrid` (Zustände im Tick,
Stufen je Bild, gelesen von `TerrainLayer` und der Minikarte), `FxExecutor`/`ParticleWorld` (Schritt im Tick, `Capture` je Bild gibt
Strahlen und Erschütterungen einmalig heraus), `FrameProjector` (Aufnahme im Tick, `Project` je Bild; Skripte ändern Blinken und
Hausfarbe im Tick), `LiveGame._soundCues`, `SelectionModel`/`CommandModeController`/`StandingOrders` (im Tick bereinigt, bei Eingabe
und je Bild benutzt), `HudMessageFeed`, `MissionPresentationDirector`, `AudioDirector` (Skripte spielen Töne im Tick).

### 6.3 Schreibzugriffe von außen

1. **Befehle** über `ICommandSink.Issue` (`SkirmishSessionCommandSink`, `MissionCommandSink`): 16 Befehlsarten aus
   `CommandModeController`, dazu `StandingOrders` in jedem Tick. `Issue` ist **synchron** und liefert `SubmitResult`; der Aufrufer
   benutzt `Accepted` sofort für Ton, Rückmeldung und Moduswechsel und liest dabei `world.Tick`.
2. **Direkt in die Skript-VM**, nicht über Befehle: `ScriptEngine.CompletePresentation` (vor dem Schritt), `SelectNamed` (bei
   Auswahländerung), `SignalUiInteraction`, `DrainOutputs` (verbrauchendes Lesen).
3. `LiveScenario` (gestellte Demos): `World.Submit`, `World.Step`, `RevealMapPermanently`, `CompleteUpgrade`, `SetReadyFrame` direkt.
4. Laden eines Spielstands ersetzt die ganze Sitzung.

### 6.4 Die vier eigentlichen Hindernisse

1. **Die HUD fragt die Welt, statt ein Bild zu bekommen.** Rund 110 Stellen je Bild und 75 bei Eingabe rechnen Verfügbarkeit,
   Voraussetzungen, Warteschlangen, Zeiger-Kontext und Platzierbarkeit direkt auf lebenden Objekten aus, zum Teil über
   `SimModule.Describe` (ein vollständiger Zustandsbaum des Moduls, nur um die Warteschlange zu lesen). Das ist der größte Posten.
2. **Stehende Befehle sind ein clientseitiger Automat im Tick.** `StandingOrders.Update` muss den Zustand von genau Tick N sehen und
   seine Befehle in Tick N bringen; die Befehle stehen im Replay. Läuft die Simulation voraus, ändert sich das Ergebnis. Für das
   Netzwerkspiel ist das ohnehin zu klären (`docs/MULTIPLAYER-PLAN.md` §2.3: ein Matchpfad über den Lockstep-Treiber) – am saubersten
   wandert die Logik als Befehl mit Dauer in die Simulation.
3. **Befehle erwarten eine sofortige Antwort.** Mit einem Lockstep-Treiber wird ein Befehl ohnehin für einen späteren Tick eingereiht;
   die Vorprüfung („darf ich das?“) muss dann gegen das letzte Bild laufen, das Ergebnis des Ticks kommt als Ereignis zurück.
4. **Missionen verschränken Skript-VM und Darstellung im Tick** (Ausgaben abholen, Abschluss zurückmelden, HUD/Ton/Blinken schalten).
   `CompletePresentation` ist eine Rückmeldung der Darstellung in die Simulation; `docs/MULTIPLAYER-PLAN.md` §6 Regel 5 verbietet sie
   für Netzwerkmatches, Missionen bleiben lokal.

### 6.5 Was ein unveränderliches Bild je Tick enthalten müsste

Damit ein Client-Thread die laufende Welt nie liest, müsste der Simulations-Thread nach jedem Tick ein Paket übergeben
(Größenordnung: was `FrameProjector.Advance` heute schon aufnimmt, plus das, was die HUD je Bild erfragt):

| Teil | Inhalt | Heutige Quelle |
|---|---|---|
| Kopf | Tick, `IsOver`, Sieger, Zustands-Hash | `LiveGame`, Sitzung |
| Ereignisse | `TickResult.Events`, **ergänzt um die Felder, die Abonnenten heute nachschlagen** (Besitzer und Vorlage von Opfer, Täter, Einzahler) | `MatchStatsTracker`, `HudMessageFeed`, `SelectionFlash`, `UnitConditionTracker` |
| Objekte | je Objekt, das der Betrachter sehen **oder auswählen** darf: Id, Vorlage, Besitzer, Lage und Winkel (auch als `Fixed` für Rückfragen), Geschwindigkeit, Gesundheit, Statusbits, Rang, Container (Inhalt, Kapazität) und Träger, Baufortschritt und Erbauer, die Zustandsbits des Modells (das Ergebnis von `ConditionDeriver`, nicht seine Eingaben), Turm- und Fahrwerkswerte, letzte Schussticks, Auswählbarkeit, Sichtbarkeit/Tarnung für den Betrachter | `FrameProjector.CaptureEntity`, `EntityPicker`, `SelectionModel` |
| Befehlssicht je eigenem Objekt | aktiver Befehlssatz, je Knopf verfügbar/Grund, Produktionswarteschlange mit Fortschritt, Sammelpunkt, Upgrades, Spezialkräfte mit Bereitschaftstick, Waffenreichweite, Bewegungs-/Angriffs-/Bauziel, ob es bauen, andocken, reparieren, einsteigen kann | `CommandBarModel`, `SimulationQueries`, `SuperweaponTimers`, `ComputeOverlay` |
| Spieler | Geld, Strom (Erzeugung/Verbrauch), Wissenschaften und Punkte, Upgrades, Beziehungen, besiegt | `CommandBarModel.Hud`, `LiveAudioView`, `MatchResult` |
| Sicht | Zellzustände des Betrachters – oder besser nur die geänderten Zellen seit dem letzten Bild | `FogOfWarGrid.Update` |
| Platzierung | eine Abfrage, die nicht über das Bild geht: Fußabdruck gegen das Navigationsgitter. Entweder das Gitter (oder seine Änderungen) im Bild, oder die Prüfung als Anfrage an den Simulations-Thread mit Antwort im nächsten Bild | `SimulationQueries.CheckPlacement` |
| Effekte | die Lagen aller Objekte für die Effekte (heute `SimulationFxWorldView`, eine Kopie je Tick existiert bereits) und die Geländehöhe (unveränderlich, kann der Client selbst halten) | `FxWorldView` |
| Mission | Skriptausgaben des Ticks, benannte Objekte und Teams mit Lage, Zählerstände | `MissionPresentationDirector` |

Reihenfolge, falls es je gebaut wird: (1) `FrameProjector`, `FxWorldView` und `FogOfWarGrid` lesen heute schon ausschließlich **im
Tick** – sie können unverändert auf dem Simulations-Thread laufen und ihr Ergebnis übergeben (das sind die rund 55 Stellen aus
`SyncAfterTick`); nötig ist nur die Übergabe von `Capture`-Liste, Nebelzuständen und Effektzustand statt geteilter Objekte.
(2) Ereignisse um die nachgeschlagenen Felder ergänzen (15 Stellen). (3) Die HUD auf eine Befehlssicht umstellen (rund 200 Stellen;
der eigentliche Aufwand, und derselbe, den ein Zuschauer- oder Wiedergabemodus bräuchte). (4) Stehende Befehle in die Simulation.
Der Gewinn wäre begrenzt, sobald die Ziele der Phase 19 erreicht sind (ein Tick ist dann klein gegen ein Bild, und im Lockstep
muss der Client ohnehin auf den Tick warten); ein eigener Thread hilft vor allem gegen einzelne lange Ticks (das Maximum in
`docs/PERFORMANCE.md` §6) und beim Aufholen nach einem Wiedereinstieg.

## 7. Befehle

Für `CLAUDE.md` (Abschnitt „Build, Tests, Audits“):

```powershell
dotnet run -c Release --project tools/CnCNew.SimTool -- perf game/content --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 18000 --replay --json <datei.json> --label <commit>
dotnet run -c Release --project tools/CnCNew.SimTool -- perf-mission game/content --map "Maps/MD_USA05/MD_USA05.map" --ticks 3000 --json <datei.json> --label <commit>
dotnet run -c Release --project tools/CnCNew.SimTool -- perf-compare <alt.json> <neu.json> [--threshold-percent N] [--min-us N] [--all] [--allow-different-hash] [--out <datei>]
dotnet run -c Release --project tools/CnCNew.PresentationTool -- sync-perf game/content --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 18000 --hud [--digest] [--json <datei.json>] [--label <commit>]
pwsh scripts/perf-profile.ps1 [-Label <commit>] [-OutDir <verzeichnis>] [-Ticks 54000] [-Only 'large-*']
pwsh scripts/perf-compare.ps1 <altesVerzeichnis> <neuesVerzeichnis> [-ThresholdPercent 10] [-AllowDifferentHash]
```

Ruhige Nachmessung der Spielschicht: nichts sonst auf dem Rechner, `dotnet build -c Release tools/CnCNew.PresentationTool`, dann den
`sync-perf`-Lauf oben zweimal (die Endhashes und – mit `--digest` – die Prüfsummen müssen gleich sein, die Mittel sollten um wenige
Prozent streuen) und die Tabelle in §4 ersetzen. Vor einer Änderung an `FrameProjector`, `FogOfWarGrid`, `FxExecutor` oder
`LiveGame.SyncAfterTick`: `--digest` vorher und nachher; eine andere Prüfsumme ist eine andere Darstellung.
