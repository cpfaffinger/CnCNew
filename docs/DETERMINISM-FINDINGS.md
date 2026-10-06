# Determinismus-Befunde D1–D9 (Phase 18, Punkt 4)

Stand 6. Oktober 2026, Ausgangscommit `e5046b7`. Dieses Dokument hält zu jedem Befund des lesenden Suchlaufs (`docs/HANDOFF-PHASE18.md`, Tabelle „Befunde des Suchlaufs“) fest: bestätigt oder widerlegt, der Test, der es zeigt, die Korrektur, ob sich Referenzwerte geändert haben, und was offen bleibt. Der Prüfmodus des Referenzmanifests (`SimTool reference-manifest`) und die Determinismus-Matrix sind nicht Teil dieses Dokuments.

Regel der Phase: keine Verhaltensänderung der Simulation außer Determinismus-Korrekturen; jeder Befund zuerst durch einen Test bestätigt oder widerlegt, dann behoben; jede Korrektur, die einen Referenzwert ändert, allein in einem Commit.

**Alles hier ist auf einem Rechner gemessen** (Windows 11, x64, .NET 10, Release). Kein Lauf auf einem zweiten Rechner oder mit einer anderen .NET-Version hat stattgefunden; unter Linux (WSL2 auf demselben Rechner) ist nur der Definitionssnapshot verglichen worden (D9). Die Korrekturen machen die betroffenen Stellen *nach Konstruktion* unabhängig von CPU, Kultur und Laufzeitroutinen; dass zwei verschiedene Rechner tatsächlich bitgleich rechnen, belegt erst die Matrix.

## 1. Übersicht

| Nr. | Urteil | Korrektur | Referenzwerte | Commit |
|---|---|---|---|---|
| D1 | Abhängigkeit im Code vorhanden, auf diesem Rechner nicht auslösbar (Tabelle unter vier JIT-Schaltern gleich) | Knochentabelle exakt gerechnet (Ganzzahl-Brüche, eigener Arkustangens) | unverändert; Tabelle bytegleich | `bef1f8c` |
| D2 | Laufzeitroutine bestätigt; Abweichung nicht messbar (ein Rechner) | `SingleBits.ToDecimal`: Ganzzahl-Nachbildung von `(decimal)float`, identisch für alle 3.923.635 Reals der 150 Karten | unverändert | `979c787` |
| D3 | Mechanismus bestätigt; Einfluss auf die geprüften Läufe nicht nachgewiesen | `EntityTable`: Aufzählung in ID-Reihenfolge nach Konstruktion | unverändert | `cfe3951` |
| D4 | bestätigt (Hash, Diagnosen und weitergereichte Argumente hingen an der Kultur) | ordinaler Vergleicher, `InvariantText` | unverändert | `f04c687` |
| D5 | bestätigt | Gelände-Fingerabdruck mit Wegpunkt-IDs, -Verbindungen und Kartenobjekten; Karten-Dateihash als Zugriff | **Gelände-Fingerabdrücke und Replay-Dateien geändert**; Tick-Hashes unverändert | `2eb7772` |
| D6 | bestätigt (Regeln lückenhaft, Zulieferer ungeprüft) | erweiterte Regeln, zweiter Regelsatz, ausdrückliche Ausnahmen | unverändert | `32ed854` |
| D7 | bestätigt; 9 Gruppen auf einer Karte (`GLA04`) | totale Ordnung | unverändert | `21c0fa8` |
| D8 | **bestätigt auf echten Daten** (19 von 45 Kampagnen-/Challenge-Karten) | `MarkDirty` an vier Stellen; Quellregel | **`mission-run` von sechs gemessenen Missionen geändert** | `99a02fe`, `df523e4` |
| D9 | **Zeilenende bestätigt** (Linux: 139.503.384 statt 144.626.133 Byte, anderer Fingerabdruck); Escaping nicht messbar | Zeilenende/Einrückung festgelegt, goldener Wert, Quellregel | unverändert; Snapshots bytegleich, unter Linux jetzt gleich | `3e75333` |

## 2. Referenzwerte vorher und nachher

Gemessen am Ausgangscommit und nach jeder Korrektur (Release; Werkzeuge nach jeder Quelländerung neu gebaut).

| Lauf | vorher (`e5046b7`) | nachher | geändert durch |
|---|---|---|---|
| `SimTool reference-match` | FactionGLA, Tick 14.490, `23B4B25DF62765D8`, Kette `DE5849AF9F8493C4` | gleich | – |
| `SimTool stress` | `56882F763EABEF67`, Kette `B8E663493F2C73D5` | gleich | – |
| `ai-match` Alpine Assault `FactionChina,FactionGLA` | China, Tick 23.370, `0BD4FE5EEB2C37E7`, Kette `BF9939E5A3B45736` | gleich | – |
| `ai-match` Alpine Assault `FactionAmerica,FactionChina` | America, Tick 16.965, `BA10AC1EB0EB8AC9`, Kette `F669459C2FA6E39C` | gleich | – |
| `ai-match` Bitter Winter `FactionAmerica,FactionChina` | America, Tick 33.750, `38380B920A732955`, Kette `5FED11D99834B1B6` | gleich | – |
| `mission-run MD_USA05 --ticks 3000` | `1CB28E6AA923BDEF`, Kette `0816C3438CB7F689` | gleich | – |
| `mission-run MD_GLA05 --ticks 3000` | `317B464E8BA98377`, Kette `6823506872EBFF9E` | **`826CCD82C8536DC2`, Kette `10330D7D179054FD`** | D8 (`99a02fe`) |
| `mission-run MD_USA01 --ticks 3000` | `C970C1640A518A58`, Kette `9074BA5AFC4B19AB` | **`8D26113C9DA64703`, Kette `945589AB7750A0CB`** | D8 (`df523e4`) |
| `mission-run MD_USA02 --ticks 3000` | `B67A4CDAC03048DA`, Kette `82C9F032E56272A4` | **`3AFE4707A243C3C7`, Kette `A60D461ED547E16F`** | D8 (`df523e4`) |
| `mission-run MD_GLA03 --ticks 3000` | `144C93AE58338C72`, Kette `5D469B2FDDFB5F3E` | **`E79236DDB44B3E30`, Kette `369AFAA71BB959AE`** | D8 (`df523e4`) |
| `mission-run GC_LaserGeneral --ticks 3000` | `124403A596021418`, Kette `4722B14F7E8A7FB8` | **`020982072960A29A`, Kette `9FBBC6F2F689BC5C`** | D8 (`df523e4`) |
| `mission-run GC_NukeGeneral --ticks 3000` | `8F3ECBAC07C3343E`, Kette `02CE97C4EBD00207` | **`652D33E7F7B4882A`, Kette `E1C65818832ABB9F`** | D8 (`df523e4`) |
| `CampaignTool audit --ticks 900 --campaign CHALLENGE_0` | 7/7 deterministisch, 7/7 identisch nach Speichern/Laden, Endproben Sieg 7/7, Niederlage 6/7 | gleich | – |
| Definitionssnapshot Retail-Basisdaten (`IniTool compile … --maps exclude`) | 144.626.133 Byte, SHA-256 `68879F19FA8DA1381D4F0513DB034D879323BFEFB990797F112E694B728E63BF` | gleich | – |
| Definitionssnapshot Vollinventar (`--maps include`) | 145.242.921 Byte, SHA-256 `6443CFA3D9E4B2B375178A62D489A4015DE02D28434588F18A0461F4C986B99B` | gleich | – |
| Knochentabelle (`ModelGeometry.Fingerprint`) | `93D5819BF231304D…` | `93D5819BF231304D112CABABBC0A69986E82CF7E353BBC8742EEBE7A88F1DA47` (gleich) | – |
| Welt-Fingerabdruck (Definitionen + Knochentabelle, erste 16 Stellen) | `63FE84D91A4EB0C6` | gleich | – |
| Gelände-Fingerabdruck `MD_USA05` / `MD_GLA05` (erste 16 Stellen) | `B3FE20CB4BE45347` / `8265FD4E188F2D61` | **`FEE2594754FE793E` / `6583781069017CBB`** | D5 (`2eb7772`) |
| Gelände-Fingerabdruck Alpine Assault / Bitter Winter | `2F2793ED2FFE1E40` / `4C42D6D18A4977B4` | **`F561E2A2F78B3108` / `EBD38F9CF58F17A9`** | D5 (`2eb7772`) |
| Replay-Datei `ai-match` Alpine Assault China/GLA (118.564 Byte, SHA-256) | `4D9FD31C…` | **`5675B0C1…`** (nur der Kopf) | D5 (`2eb7772`) |

Die „vorher“-Werte der fünf Missionen `MD_USA01`, `MD_USA02`, `MD_GLA03`, `GC_LaserGeneral` und `GC_NukeGeneral` sind am Stand `2eb7772` gemessen (unmittelbar vor `df523e4`), nicht am Ausgangscommit; keine der Korrekturen davor berührt sie nach Konstruktion, gemessen ist das für diese fünf nicht.

## 3. D4 – Kulturabhängiges im gehashten Skriptzustand

**Bestätigt.** Test `Determinism D4` (`tests/CnCNew.Simulation.Tests/Tests.Determinism.cs`, `ScriptStateIsCultureIndependent`): dieselbe synthetische Mission unter de-DE, tr-TR, ar-SA, sv-SE und einer erfundenen Kultur (Minuszeichen `~`, Dezimaltrenner `#`), verglichen mit dem Lauf unter der invarianten Kultur. Vor der Korrektur:

| Kultur | Zustands-Hash | weitergereichte Argumente | Diagnose |
|---|---|---|---|
| de-DE, tr-TR | gleich | `SPEECH_PLAY(1,5,-2,25,0\|…)` statt `1.5,-2.25,0` | gleich |
| ar-SA | **anders** | `1٫5,؜-2٫25,0` | `buildable status ؜-1 out of range` |
| sv-SE | **anders** (Minuszeichen `−` **und** Sortierung: `Ö` nach `Z`) | `1,5,−2,25,0` | `buildable status −1 out of range` |
| erfunden | **anders** | `1#5,~2#25,0` | `buildable status ~1 out of range` |

Ursachen und Korrektur:

- `_priorityDelta` war ein `SortedDictionary<string,int>` ohne Vergleicher (Kultursortierung); jetzt `StringComparer.Ordinal`.
- Diagnosen sind Teil des Hashs und wurden mit der aktuellen Kultur interpoliert. Neu: `InvariantText` (`src/CnCNew.Simulation/Scripting/InvariantText.cs`), ein Interpolations-Handler mit invarianter Kultur; `Diagnose($"…")` bindet daran, Zählerschlüssel und die Koordinaten weitergereichter Aktionen benutzen `InvariantText.Format`.
- `EntityId`/`PlayerId.ToString`, die `X16`-Hashes in den Snapshots und zwei `int.TryParse` (D6) nennen die invariante Kultur ausdrücklich.

Echte Daten: `CnCNew.Game.Tests` → `DeterminismTests`, Test „D4“: unter jeder der fünf Kulturen wird von den Dateien an neu gerechnet (Definitionen kompilieren, Knochentabelle, Katalog, `GC_ChemGeneral` dekodieren, Gelände, 900 Missionsticks). Ergebnis nach den Korrekturen: Definitions-Fingerabdruck, Welt-Fingerabdruck, Gelände-Fingerabdruck, Programm-Fingerabdruck, Kettenhash und Endhash unter allen fünf Kulturen identisch zum invarianten Lauf.

Suche nach weiterer Kulturabhängigkeit (Simulation, `CnCNew.Data`, Kartendekodierung): keine Kulturvergleicher, kein `ToUpper()`/`ToLower()`, kein `StartsWith`/`EndsWith`/`IndexOf` mit Zeichenkette ohne `StringComparison`, keine Sortierung über Zeichenketten ohne ordinalen Vergleicher. Interpolationen mit Zahlen außerhalb von Diagnosen: nur nichtnegative Indizes und IDs (Feldnamen wie `Rider{slot}`, Schlüssel wie `"tower" + index`); nichtnegative ganze Zahlen formatiert .NET in jeder Kultur mit ASCII-Ziffern, das Minuszeichen ist die einzige Kultureigenschaft.

**Offen:** Eine lexikalische Regel sieht keine Typen. Eine negative Zahl oder ein `decimal` in einer Interpolation oder Verkettung, die in Zustand gelangt, bemerkt nur der Kulturtest – und nur auf den Pfaden, die er durchläuft. Die volle Kulturzeile der Matrix (alle Läufe des Manifests) steht aus.

## 4. D7 – nicht totaler Sortiervergleich der Wegpunkte

**Bestätigt.** Test `Determinism D7` (`WaypointOrderIsTotal`): 48 gestapelte Wegpunkte eines Namens, die sich nur in der Höhe unterscheiden, in zwei Dateireihenfolgen. Vor der Korrektur zwei verschiedene Wegpunktreihenfolgen (`24 31 38 45 4 11 …` gegen `44 37 30 23 16 9 …`): die Sortierung ist nicht stabil, und der Vergleich ließ Z, Id und NextId aus.

Korrektur: `TerrainMap.CompareWaypoints` vergleicht Name, X, Y, Z, Id, NextId. Wegpunkte, die danach noch gleich sind, sind in allem gleich.

Echte Karten (gezählt über alle 150, Test „D2 and D7“ der `DeterminismTests`): 25.460 benannte Wegpunkte; **9 Gruppen (18 Wegpunkte)** stimmen in Name, X und Y überein, alle auf `Maps/GLA04/GLA04.map`, und alle 9 unterscheiden sich in Höhe oder Id – ihre Reihenfolge war dort undefiniert. Keine Karte der Referenzläufe ist betroffen; die Referenzwerte sind unverändert. Der Gelände-Fingerabdruck von `GLA04` ist nicht vorher/nachher verglichen worden (er ändert sich mit D5 ohnehin).

## 5. D6 – Quellregeln

**Bestätigt:** `SourceRules` prüfte nur `src/CnCNew.Simulation` und kannte die im Befund genannten Konstrukte nicht.

Neu (`tests/CnCNew.Simulation.Tests/Tests.SourceRules.cs`):

- Ein Scanner (Zeichenketten geleert, Kommentare und mehrzeilige Raw-Strings entfernt), Regeln als benannte reguläre Ausdrücke, **ausdrückliche Ausnahmen** (Datei, Regel, Zeilentext, Begründung). Eine Ausnahme, die nichts mehr trifft, lässt den Test scheitern. Die Regeln prüfen sich an einer Liste von Zeilen, die sie fangen müssen, und einer, die sie nicht fangen dürfen.
- **Regelsatz Simulation** (`Determinism rules hold in the simulation sources`): Wandzeit; ambienter Zufall; Gleitkomma (Typnamen, `MathF`, `Math.Sin/Cos/Tan/Asin/Acos/Atan/Atan2/Sqrt/Pow/Exp/Log/Round/Floor/Ceiling/Truncate/…`, Real-Literale ohne `m` wie `var x = 1.5`, `1e5`, `2f`); `System.Numerics` (außer `BitOperations`, `BigInteger`), `System.Runtime.Intrinsics`, `Vector64/128/256/512`, `Vector<T>`, `Matrix4x4`, `Quaternion`; Threads, `Task`, `async`/`await`, `Parallel`, `Interlocked`, `volatile`, Concurrent-Sammlungen; `File`/`Directory`/`Process`/`Environment`/`AppContext`/`AppDomain`/`RuntimeInformation`; Reflection; kulturabhängige Textoperationen; Zahlen als Text ohne invariante Kultur (`ToString()`, `ToString("…")`, `string.Format`, `Parse`/`TryParse`); `GetHashCode()`/`HashCode.`; Zeilenende und Pfadtrenner der Plattform (`Environment.NewLine`, `AppendLine`, `WriteLine`, `Path.Combine` …, D9); von Hand gesetztes Dirty-Flag (D8).
- **Regelsatz Zulieferer** (`… in the sources that feed the simulation (D6)`): `src/CnCNew.Data`, `SageMapDecoder.cs`, `LegacyAssetReaders.cs`, `ModelGeometryLoader.cs`, `ExactBoneMath.cs`. Dieselben Regeln; Gleitkomma nur als Dateiformat: kein `float`/`double`, keine Gleitkomma-Funktion, kein `(decimal)`-Cast, kein Lesen eines Singles als Zahl (`Int32BitsToSingle`, `ReadSingle`, …). Der eine Weg ist `SingleBits.ToDecimal` (Karten) bzw. `Dyadic.FromSingleBits` (Knochentabelle).

Gefundene Verstöße – behoben (alle hash-neutral): `EntityId`/`PlayerId.ToString`, `ToString("X16")` in `WorldSnapshot`, `MissionSnapshot`, `ScriptProgram`, `int.TryParse` der Befehlssatz-Slots (`DefinitionCompiler`) und der WND-Version (`LegacyAssetReaders`), die Knochentabelle (D1), die Kartendekodierung (D2), das Dirty-Flag (D8).

Gefundene Verstöße – als Ausnahme begründet (18 in der Simulation, 12 bei den Zulieferern): `ToString()` von Enum-Namen, `DefinitionId` und `StringBuilder`; Schlüssel-Hashes (`Fixed.GetHashCode`, `HashCode.Combine` der Wissenschafts-/Upgrade-Bitmengen und eines Vergleichers – nur für Gleichheitssuche, kein Wert und keine Reihenfolge leitet sich daraus ab); ein `decimal.TryParse`, dessen Kulturargument auf der Folgezeile steht; `MarkDirty` selbst; `ValidateWak` (Importprüfung auf Endlichkeit); der Parametertyp des einen Eintrittspunkts der Knochentabelle.

**Grenzen:** Die Regeln sind Stolperdrähte, kein Beweis. Sie sehen keine Typen (Interpolationslöcher, Verkettungen, `var x = pivot.X * 2`), keinen Code in Interpolationslöchern (die Zeichenkette wird als Ganzes geleert) und keine ungeordnete Aufzählung. `src/CnCNew.Game/Live/LiveScenario.cs` (gestellte Demo-Szenen, `(decimal)float` und `Math.PI`) gehört nicht zum Regelsatz: die Szenen sind lokale Werkzeuge, kein Matchpfad – wer sie in ein Netzwerkmatch übernimmt, muss sie über `SingleBits` führen.

## 6. D8 – Zustandsänderung ohne `MarkDirty`

**Bestätigt, auf echten Daten.** Der inkrementelle Hash wurde in den Werkzeugen nur alle 5.000 bzw. 20.000 Ticks gegen den vollen geprüft.

Fund: `MD_GLA05`, Tick 855. Die Skriptaktion `WAREHOUSE_SET_VALUE` änderte den Bestand von fünf `SupplyDock`-Objekten (`boxes` 400 → 740). `SupplyWarehouseDockUpdate.SetCashValue` und `DockUpdateBase.SetOpen` (Fähre) setzten `Entity.Dirty = true` von Hand statt `MarkDirty` aufzurufen. Das Flag ohne den Eintrag in der Dirty-Liste der Welt ist schlimmer als kein Flag: jedes spätere `MarkDirty` kehrt früh zurück, der zwischengespeicherte Objekt-Hash bleibt dauerhaft veraltet, und der Tick-Hash wird erst richtig, wenn etwas ihn vollständig neu berechnet. Das tut das Schreiben eines Snapshots (`WorldSnapshot.Write` → `ComputeFullHash`). Folgen:

- Eine Mission hashte verschieden, je nachdem, ob und wann ein Speicherstand geschrieben worden war.
- Eine aus Snapshot wiederhergestellte Welt (die voll hasht) lief von der durchlaufenden weg: `MD_GLA05`, bei Tick 150 wiederhergestellt, wich bei Tick 900 ab.
- Zwei Rechner hätten sich auf einen Hash geeinigt, der den Vorratsbestand nicht mehr beschrieb.
- `mission-run` meldete „continuation identical: True“, weil dort beide verglichenen Welten den Snapshot geschrieben hatten.

Tests:

- `Determinism D8: the incremental hash equals the full hash in every tick of the stress scenario` – der synthetische Stresstest (6.000 Ticks, 600 Objekte), in jedem Tick geprüft; der geprüfte Lauf hasht wie der ungeprüfte. Bestand vor und nach der Korrektur (der Stresstest enthält den Fehler nicht).
- `Determinism D8: a scripted warehouse value reaches the tick hash; writing a save does not change it` – scheiterte vor der Korrektur.
- Synthetisches Gefecht und synthetischer Referenzmatch prüfen jetzt in jedem Tick (`VerifyIncrementalEvery = 1`).
- Echte Daten (`DeterminismTests`, Test „D8“): Referenzmatch 6.000 Ticks, KI-Match Alpine Assault 9.000 Ticks, `MD_USA05` 600, `GC_ChemGeneral` 1.500, `MD_GLA05` 1.200 Ticks, jeweils in jedem Tick geprüft und mit dem ungeprüften Lauf verglichen. `--determinism-full`: Referenzmatch und KI-Match ganz, dazu alle Kampagnen- und Challenge-Karten (Ergebnis unten).

Korrektur: beide Methoden nehmen die Welt und rufen `MarkDirty`; Quellregel „dirty flag set without MarkDirty“; `VerifyIncrementalHash` nennt jetzt die veralteten Objekte und den Tick.

**Referenzwert geändert** (allein in Commit `99a02fe`): `mission-run MD_GLA05 --ticks 3000` Endhash `317B464E8BA98377` → `826CCD82C8536DC2`, Kette `6823506872EBFF9E` → `10330D7D179054FD`. Das Verhalten der Simulation ist unverändert; nur der Hash beschreibt jetzt den Zustand. Alle anderen Referenzwerte unverändert (§2).

### Zweiter Fund: Container, Partikelkanone, Spezialkräfte (Commit `df523e4`)

Der volle Lauf (`DeterminismTests --determinism-full --determinism-only D8`: alle 45 Karten `MD_*`/`GC_*`, je 1.500 Ticks, jeder Tick geprüft) fand nach der ersten Korrektur **18 weitere Karten** mit veraltetem Hash:

| Stelle | Feld | zuerst gesehen | Ursache |
|---|---|---|---|
| `ContainModuleBase.Add`/`Remove` (Garnison, Transport, Zelt) | Insassenliste und belegte Plätze des Containers, `ContainedBy` des Insassen | `GC_NukeGeneral` Tick 40 (`GuardTower`), `MD_USA01` Tick 59 (`AmericaVehicleChinook`), `MD_USA04` Tick 71 (`Tent02`), insgesamt 18 Karten | das Markieren war Sache der Aufrufer; ein geskripteter Garnisonseintritt bei Ankunft tat es nicht. Die Basis markiert jetzt beide Enden selbst |
| `ParticleUplinkCannonUpdate.PrepareScriptedPath`/`Initiate` | `pendingPath[]`, `pendingPathTick` | `GC_LaserGeneral` Tick 361, `GC_SuperWeaponsGeneral` Tick 541 (nach der Container-Korrektur) | Skriptpfad vorgemerkt ohne `MarkDirty` |
| `SpecialPowerModuleBase.StartRecharge`/`OnSpecialPowerCreation`, `NAMED_SET/ADD_SPECIAL_POWER_COUNTDOWN`, Zielfahrer | `availableOnFrame` | vom Manifest-Agenten gemeldet (`MD_USA02` ab Tick 1.338, `AmericaCommandCenter`) | Bereitschafts-Tick gesetzt ohne `MarkDirty`; im eigenen 1.500-Tick-Lauf nach der Container-Korrektur nicht mehr aufgetreten, nach Lektüre des Quelltexts trotzdem markiert |

Tests: `Determinism D8: entering and leaving a container reaches the tick hash` (Garnison per Skript, verlassen, Teamgarnison, räumen – in jedem Tick geprüft; ohne die Korrektur scheitert er bei Tick 73 am `AlphaBunker`). Der volle Lauf nach allen Korrekturen: alle 45 Karten, der ganze Referenzmatch (14.791 Ticks) und das KI-Match Alpine Assault China/GLA (23.671 Ticks) in jedem Tick geprüft, kein veralteter Hash, Tick-Hashes gleich den ungeprüften Läufen.

**Referenzwerte geändert** (allein in Commit `df523e4`; `mission-run … --ticks 3000`, jeweils zwei frische Läufe identisch und Fortsetzung nach Speichern/Laden identisch, vorher wie nachher): `MD_USA01` `C970C1640A518A58` → `8D26113C9DA64703`, `MD_USA02` `B67A4CDAC03048DA` → `3AFE4707A243C3C7`, `MD_GLA03` `144C93AE58338C72` → `E79236DDB44B3E30`, `GC_LaserGeneral` `124403A596021418` → `020982072960A29A`, `GC_NukeGeneral` `8F3ECBAC07C3343E` → `652D33E7F7B4882A` (Ketten in §2). Unverändert gemessen: Referenzmatch, Stresstest, die drei KI-Läufe, `MD_USA05`, `MD_GLA05` (Wert seit `99a02fe`), Audit `CHALLENGE_0`. Jede der 18 Karten hasht jetzt anders; gemessen sind nur die genannten fünf.

**Offen:** Geprüft sind begrenzte Läufe (1.500 Ticks je Karte, normale Schwierigkeit, ohne Spielereingaben). Eine Änderung ohne `MarkDirty` auf einem Pfad, den kein geprüfter Lauf durchläuft, bleibt unentdeckt, bis der Prüfmodus des Manifests breiter läuft. Der inkrementelle Hash deckt Objekte, Zeitgeber und Befehle ab; der Skriptzustand wird ohnehin jedes Mal vollständig beschrieben.

## 7. D3 – ungeordnete Aufzählung der Objekte

**Mechanismus bestätigt, Einfluss auf die geprüften Läufe nicht nachgewiesen.**

- Test `Determinism D3` (`EntityEnumerationIsOrderedById`): nach drei Toden und vier Geburten zählte eine laufende Welt `1,11,3,4,10,9,7,8,12` auf, dieselbe Welt aus Snapshot `1,3,4,7,8,9,10,11,12`.
- Echte Daten (`DeterminismTests`, Tests „D3“, `--determinism-full`): die Welt an **jedem** Prüfpunkt aus Snapshot wiederhergestellt und bis zum Ende neben der durchlaufenden gerechnet, jeder Tick-Hash verglichen. Auf dem alten Dictionary: Referenzmatch (14 Wiederherstellungen, 102.074 Ticks), KI-Match Alpine Assault China/GLA (23, 268.433), America/China (17, 140.522) und `MD_USA05` (20, 28.500) identisch; `MD_GLA05` wich ab – Ursache war D8, nicht die Reihenfolge. Nach D8 und mit geordneter Tabelle: alle fünf identisch (`MD_GLA05` 20 Wiederherstellungen, 28.500 Ticks). Der Lauf „altes Dictionary mit D8-Korrektur“ auf `MD_GLA05` ist nicht gemessen.

Korrektur: `EntityTable` (`src/CnCNew.Simulation/World/EntityTable.cs`) – Suche über die ID wie bisher, Aufzählung in aufsteigender ID-Reihenfolge nach Konstruktion. IDs wachsen nur, ein neues Objekt wird angehängt; ein entferntes hinterlässt eine Lücke, die Aufzählungen überspringen; nach der Zerstörungsphase eines Ticks werden die Lücken geschlossen. Entfernen während der Aufzählung bleibt erlaubt; eine Aufzählung, die eine Umordnung überlebt, wirft. Kein Aufrufer wurde geändert. Referenzwerte unverändert; Kosten im Stresstest (`SimTool stress --ticks 108000`, 13.076 gleichzeitig lebende Objekte, zwei Builds abwechselnd, je drei Läufe, Rechner nicht ruhig): Dictionary 96,4 / 71,1 / 84,6 µs je Tick, `EntityTable` 72,4 / 92,8 / 81,6 µs je Tick – kein Unterschied außerhalb der Streuung, keine Leistungsaussage darüber hinaus.

Inventar der Aufzählungen über ungeordnete Sammlungen in `src/CnCNew.Simulation` (ohne `Testing/`):

| Sammlung | Aufzählungen | Urteil |
|---|---|---|
| Objekte: `_entities.Values` (13 Stellen in `SimulationWorld*.cs`: Erfassung, Schlachtplan, Flächenschaden, Bau, Kisten, Sicht, Upgrades, Hash) und `IWorldServices.Entities`/`SimulationWorld.Entities` (rund 60 Schleifen ohne eigene Sortierung in Modulen, Skript-VM, Kollision, `SkirmishAi`; rund 30 weitere sortieren selbst mit `OrderBy(Id)`) | mit Nebenwirkung: u. a. `ResolveFootprintCollisions`/`DeliverCollisions` (Überfahren, Umstürzen, Kollisionsmodule in Folge), `StepBlocked`/`StationaryBlockersAhead` (erster Treffer), `ReevaluateUpgrades`, Kisten- und Feldeffekte, Skriptaktionen mit `FirstOrDefault` | **jetzt geordnet** (ID-Reihenfolge nach Konstruktion; die eigenen `OrderBy` sind überflüssig, aber unschädlich und unverändert) |
| `_timerById` (`Dictionary<ulong, SimTimer>`) | `ComputeFullHash` (Summe), Snapshot (sortiert nach ID) | reihenfolgeunabhängig, weil kommutative Summe bzw. sortiert |
| `_dirty` (Liste der geänderten Objekte) | `ComputeIncrementalHash` | reihenfolgeunabhängig, weil Summe je Objekt |
| `SkirmishAi._boarding` | `Where(abgelaufen).ToList()` → Entfernen | reihenfolgeunabhängig, weil jede Entfernung für sich steht |
| `SkirmishAi._roles`, `_producible`, `_structureCandidates`, `_retryAt`, `_powerRetry`, `_scienceRetry`, `_orders`, `_idleSince`, `_siteWatch`, `_rallySet`, `_clearedHints`, `_seenAt`, `_unreachable` | keine Aufzählung, nur Suche | ohne Belang |
| `ContainModules._containedSinceTick`, `RetailMatchModules._containedSinceTick`, `ScriptEngine._busyThisTick`, `_entityTeams`, `NavigationGrid._escape`/`_temporary`, `TerrainMap._waypointsById`, `_playerSlots`, `_humanSequence`, die Namenstabellen von `ScriptProgram` | keine Aufzählung, nur Suche | ohne Belang |
| lokale Mengen (`visited`, `used`, `seen`, `reserved`, `links`, `byId`, `labels`) | `byId` wird sortiert aufgezählt, die übrigen nur befragt | ohne Belang |
| `ScriptCoverage` (`ImmutableHashSet<string>`) | `Validate()` | Reihenfolge der Meldungen hängt am Zeichenketten-Hash des Prozesses; nur Testausgabe, kein Zustand |
| `SortedDictionary`/`SortedSet` (Spieler, Befehle je Tick, alle Tabellen der Skript-VM, `SkirmishAi._enemies`) | viele | geordnet nach Typ (Zeichenketten ordinal, seit D4 ausnahmslos) |

**Offen:** Das Inventar ist durch Suche über Deklarationen und `foreach`/LINQ-Verwendungen entstanden, nicht durch ein Werkzeug mit Typinformation. Der Wiederherstellungstest deckt fünf Läufe ab, nicht das volle Manifest.

## 8. D2 – Kartenkoordinaten über `(decimal)float`

**Laufzeitroutine bestätigt; eine Abweichung ist auf einem Rechner nicht messbar.** Jede Koordinate, jeder Winkel und jede Skriptzahl einer Karte ging durch `(decimal)float` (`Decimal.DecCalc.VarDecFromR4`, sieben signifikante Stellen). Es gab genau eine Stelle: `SageMapDecoder.ChunkReader.ReadReal`; `TerrainMap`, `ScriptProgram`, `SkirmishMatchSession` und die Skript-VM wandeln die so entstandenen `decimal` mit `Fixed.FromDecimal` (reine Dezimalarithmetik) weiter.

Korrektur: `CnCNew.Data.World.SingleBits.ToDecimal(int bits)` bildet die Routine mit ganzen Zahlen aus dem Bitmuster nach – einschließlich ihrer Zwischenprodukte in doppelter Genauigkeit (als korrekt gerundete 53-Bit-Mantissen, wie IEEE 754 es für eine einzelne Multiplikation oder Division vorschreibt), der Skalierungsschritte, der Rundung zur geraden Zahl und des Entfernens nachgestellter Nullen. Wert **und** Skalierung des Ergebnisses sind die des Casts (die Skalierung zählt, weil Koordinaten als Text an die Präsentation weitergereicht werden). `ReadReal` benutzt sie; eine zweite Wandlung gibt es nicht, und der Zulieferer-Regelsatz (D6) verbietet sie.

Nachweis der Gleichheit (kein hash-neutraler Umbau auf Verdacht):

- `CnCNew.Data.Tests` (`SingleBitsMatchTheRuntimeCast`): 9.310.191 Bitmuster – jeder Exponent, beide Vorzeichen, eine feste Pseudozufallsfolge über den ganzen 32-Bit-Raum, eine auf Kartengrößen konzentrierte, dazu Sonderfälle (Nullen, Subnormale, Dezimalgrenzen, halbe siebte Stellen) – identisch in Wert und Skalierung; 763.065 überlaufende Muster werfen in beiden Wegen.
- `DeterminismTests`, Test „D2 and D7“: **alle 150 Karten, 3.923.635 Reals, 788.421 verschiedene Bitmuster, 0 Abweichungen** (über den Beobachter `SageMapDecoder.Decode(stream, realBits)`).

Referenzwerte unverändert. Der Test vergleicht gegen den Cast *dieser* Laufzeit: auf einer Laufzeit, deren Cast sich ändert, scheitert er und zeigt die Änderung an; die Routine bleibt, was sie ist.

**Offen:** `Fixed.FromDecimal` rechnet mit `System.Decimal` der Laufzeit (exakte Ganzzahlarithmetik nach Spezifikation, aber nicht eigener Code). `LiveScenario` (Demo-Szenen) wandelt weiter über den Cast (§5).

## 9. D1 – Knochentabelle in Gleitkomma

**Abhängigkeit im Code vorhanden; auf diesem Rechner nicht auslösbar.** Die Tabelle (Park-, Hangar-, Vorfeld-, Startbahn- und Helipad-Posen der neun Retail-Flugfelder) ist Teil des Welt-Fingerabdrucks. Sie wurde aus den Ruhematrizen des Skeletts gelesen (`Matrix4x4`-Produkte in einfacher Genauigkeit, deren Befehlsauswahl – Vektorbreite, Fused Multiply-Add – Prozessor und Laufzeit gehört) und ihre Richtungen aus `MathF.Atan2` (C-Laufzeit), danach auf 1/16 Einheit und 1/1024 rad gerundet. π/2 liegt 4,6·10⁻⁶ rad neben einer Rundungsgrenze.

Versuch, den Befund auszulösen: der Tabellenhash der alten Berechnung unter `DOTNET_EnableFMA=0`, `DOTNET_EnableHWIntrinsic=0`, `DOTNET_EnableAVX=0` und `DOTNET_TieredCompilation=0` – jedes Mal `93D5819BF231304D…`, wie ohne Schalter. Auf dieser CPU verschluckt die Rundung die Unterschiede. Ob eine andere CPU, ein ARM-Prozessor oder eine andere C-Laufzeit eine Grenze kreuzt, lässt sich hier nicht messen. Die Linux-Messung des Koordinators (WSL, derselbe Rechner) ergab für die alte Tabelle denselben Wert.

Korrektur (`src/CnCNew.Game/Data/ExactBoneMath.cs`, `ModelGeometryLoader.RestPose`, `W3dSkeleton.RestPivots`): Ein Single ist eine ganze Zahl mal eine Zweierpotenz; eine Ruhepose ist nichts als Summen und Produkte solcher Zahlen. `Dyadic` (`BigInteger`-Mantisse, binärer Exponent) und `ExactTransform` rechnen die Kette **ohne jede Rundung** aus den Pivot-Zahlen, wie die Datei sie nennt (dieselben Formeln wie `CreateFromQuaternion * CreateTranslation` und `local * parent`). Die Richtung ist ein Ganzzahl-Arkustangens auf 2⁻¹⁹² (alternierende Reihe, π nach Machin), gerundet auf den Tabellenschritt; liegt eine Richtung so nah an einer Rundungsgrenze, dass sie nicht entscheidbar ist, ist das ein Fehler, keine Schätzung. Positionen runden zur geraden Zahl wie bisher. Im Lader bleibt keine Gleitkomma-Arithmetik; der Zulieferer-Regelsatz prüft beide Dateien.

Tests (`CnCNew.Game.Tests`, `DeterminismTests`):

- „D1: a synthetic skeleton …“ (läuft ohne Inhalt): bekannte Posen (Vierteldrehung, 30°, exakte Hälften eines Sechzehntels, halbe Drehung, eine gekippte Kette), jeder Knochen gegen den Weg in einfacher Genauigkeit, der Arkustangens gegen `Math.Atan2` über 20.000 Vektoren aller Quadranten.
- „D1: the bone table of the retail airfields has its golden hash“: SHA-256 `93D5819BF231304D112CABABBC0A69986E82CF7E353BBC8742EEBE7A88F1DA47`.

**Die Tabelle hat sich nicht geändert** (derselbe SHA-256 vorher und nachher), damit auch der Welt-Fingerabdruck (`63FE84D91A4EB0C6…`) und alle Referenzwerte nicht (gemessen, §2).

**Offen:** Der Lauf auf einer zweiten CPU. Die Skelette der Präsentation (`W3dSkeleton.RestWorldTransforms`) bleiben Gleitkomma; sie erreichen die Simulation nicht.

## 10. D5 – Lücken der Fingerabdrücke

**Bestätigt.** Test `Determinism D5` (`TerrainFingerprintCoversWaypointLinksAndMapObjects`): sieben Varianten einer Karte – eine Wegpunktverbindung führt woanders hin, Wegpunkt-IDs vertauscht, ein Kartenobjekt verschoben, gedreht, von anderem Typ, mit anderem Besitzer, eines mehr – hatten vor der Korrektur alle den Fingerabdruck der Ausgangskarte.

Korrektur:

- `TerrainMap.ComputeFingerprint`: Kennung `terrain-fingerprint-2`, je Wegpunkt zusätzlich `Id` und `NextId`, danach alle Kartenobjekte in Dateireihenfolge (Name, Position, Winkel, Flags, Eigenschaften).
- `ContentFileSystem.FileHash(path)` und `GameData.MapFileHash(path)`: der SHA-256 der Kartendatei aus dem Content-Manifest (64 Hex-Stellen, Großbuchstaben) – die Identität einer ganzen Karte. Der Gelände-Fingerabdruck deckt, was das Gelände der Simulation liest; Seiten, Teams, Skripte und Auslöseflächen deckt nur der Dateihash. Test „D5“ der `DeterminismTests`: 150 Karten, die Manifest-Hashes dreier Karten gegen die Dateien nachgerechnet.

**Fingerabdruckwerte geändert** (allein in Commit `2eb7772`): jeder Gelände-Fingerabdruck und damit die Replay-Dateien, die auf einer Karte aufgezeichnet werden (der Kopf speichert ihn); Werte in §2. **Kein Tick-Hash, kein Sieger und kein Endtick ändert sich** (gemessen: alle Läufe aus §2; die Replay-Datei des Referenzmatches, der kein Gelände hat, ist bytegleich).

**Was eine spätere Phase (25, Protokoll und Beitritt) ergänzen muss** – hier bewusst nicht erfunden:

- Eine **Kennung des Codestands** der Simulation. Der Definitions-Fingerabdruck beschreibt Daten, nicht Code: zwei Rechner mit denselben Daten und verschiedenem Simulationscode halten sich heute für kompatibel. Es gibt kein Schema dafür (Buildnummer, Hash der Assemblies, von Hand gepflegte Version); das ist eine Entscheidung der Phase 25, zusammen mit `CommandTypes.Version` und dem Replay-Format.
- Den **Karten-Dateihash** im Beitrittsvergleich (der Zugriff ist da, das Protokoll nicht) und im Replay-Kopf, wenn Replays Karten eindeutig benennen sollen.
- Den **KI-Startwert** und die übrigen Matchparameter (Slots, Fraktionen, Startpositionen) in einer Match-Beschreibung, die beide Seiten vergleichen (`docs/MULTIPLAYER-PLAN.md` E11, E15).
- Der Programm-Fingerabdruck der Skript-VM (`ScriptProgram.Fingerprint`, 64 Bit über Namen und Typnummern, ohne Parameter) ist schwach und steht im gehashten Missionszustand; für Missionen genügt bis dahin der Dateihash. Ihn zu verstärken ändert jeden Missions-Hash und wurde hier nicht angefasst.

## 11. D9 – Kodierung des Definitions-Fingerabdrucks

**Zeilenende bestätigt, über Betriebssysteme gemessen.** Der Definitions-Fingerabdruck ist der SHA-256 eines eingerückten JSON-Texts aus `System.Text.Json`. Das Zeilenende eines eingerückten Dokuments ist dort `Environment.NewLine`. Messung des Koordinators vor der Korrektur (Linux/WSL, derselbe Inhalt): Snapshot 139.503.384 Byte statt 144.626.133, Welt-Fingerabdruck `8E92E383…` statt `63FE84D9…` – zwei Rechner mit verschiedenem Betriebssystem hätten sich abgelehnt.

Korrektur (`DefinitionSnapshotWriter`): die Optionen nennen Zeilenende (`\r\n`, was der Snapshot unter Windows immer hatte – bestehende Fingerabdrücke bleiben gültig), Einrückzeichen und Einrückbreite ausdrücklich.

Nachweise:

- Bytegleichheit auf echten Daten vorher/nachher unter Windows (`IniTool compile`, Release): Retail-Basisdaten 144.626.133 Byte `68879F19…63BF`, Vollinventar 145.242.921 Byte `6443CFA3…B99B` (§2).
- **Unter Linux nach der Korrektur** (WSL2 Ubuntu, `dotnet publish tools/CnCNew.IniTool -r linux-x64 --self-contained`, `compile … --maps exclude`): 144.626.133 Byte, SHA-256 `68879F19FA8DA1381D4F0513DB034D879323BFEFB990797F112E694B728E63BF` – identisch zu Windows. Das Vollinventar ist unter Linux nicht gemessen.
- `CnCNew.Data.Tests` (`FingerprintEncodingIsPinned`): ein Snapshot mit jeder Klasse besonders behandelter Zeichen (HTML-empfindliches ASCII, Anführungszeichen, Backslash, Nicht-ASCII) – geprüft werden die erwarteten Escapes, CR LF an jedem Zeilenende und der goldene SHA-256 `024D8B21…BA43` (1.884 Byte), aufgenommen vor der Festlegung und danach unverändert.
- Quellregel „platform line end or path separator“ (D6, beide Regelsätze): `Environment.NewLine`, `AppendLine`, `WriteLine`, `Path.DirectorySeparatorChar`/`Combine`/`Join`/… – keine Fundstelle in der Simulation und ihren Zulieferern. Die Knochentabelle schreibt `\n` ausdrücklich; der Snapshot ersetzt `\` in Quellpfaden durch `/`.

**Offen:** Das **Escaping** ist weiter der Standard-Encoder der Laufzeit; ob es sich zwischen .NET-Versionen unterscheidet, ist auf einer Version nicht messbar. Der goldene Wert macht eine abweichende Laufzeit zu einem Testfehler statt zu einer stillen gegenseitigen Ablehnung. Eine eigene, festgelegte Kodierung des Fingerabdruck-Eingangs (eigener Schreiber statt Serialisierer; dann gehören auch Eigenschaftsreihenfolge und Zahlentext uns) braucht entweder unsicheren Code in `CnCNew.Data` (ein eigener `JavaScriptEncoder`) oder einen Neubau des Snapshot-Schreibers und ist nicht gemacht.

## 12. Tests und wie sie laufen

| Test | Ort | Aufruf |
|---|---|---|
| `Determinism D3/D4/D5/D7/D8 …`, `Determinism rules …` | `tests/CnCNew.Simulation.Tests` (`Tests.Determinism.cs`, `Tests.SourceRules.cs`) | `dotnet run --project tests/CnCNew.Simulation.Tests [-- --only "Determinism"]` |
| `SingleBitsMatchTheRuntimeCast`, `FingerprintEncodingIsPinned` | `tests/CnCNew.Data.Tests` | `dotnet run --project tests/CnCNew.Data.Tests` |
| `DeterminismTests` (D1 synthetisch ohne Inhalt; D1 golden, D2/D7, D3, D4, D5, D8 auf echten Daten) | `tests/CnCNew.Game.Tests` | `dotnet build -c Release tests/CnCNew.Game.Tests`, dann `dotnet run -c Release --no-build --project tests/CnCNew.Game.Tests -- --only DeterminismTests --content-dir .local/phase2-import-a` |
| dasselbe, volle Läufe (jede wiederhergestellte Welt bis zum Ende; alle 45 Kampagnen-/Challenge-Karten in jedem Tick geprüft) | | `… --determinism-full` (rund eine Stunde); `--determinism-only D8` wählt die Tests eines Befunds |

## 13. Was nach diesem Punkt offen ist

1. **Zweiter Rechner, andere CPU, andere .NET-Version:** nichts davon ist gelaufen. Unter Linux ist nur der Definitionssnapshot verglichen.
2. **D8 in der Breite:** geprüft sind 1.500 Ticks je Kampagnenkarte ohne Spielereingaben, Referenzmatch, ein KI-Match und der Stresstest. Der Fehlertyp (Zustand geändert, Hash nicht) ist damit an vier Stellen gefunden worden, die niemand vermutet hatte; weitere auf ungeprüften Pfaden (Gefechte mit Spezialkräften, späte Missionsphasen, Spielerbefehle) sind wahrscheinlich, bis der Prüfmodus des Manifests breit läuft. Strukturell wäre `MarkDirty` im Setter jedes gehashten Felds die Lösung; das ist ein Umbau, keine Korrektur.
3. **D3:** Inventar ohne Typinformation; der Wiederherstellungstest deckt fünf Läufe.
4. **D4/D6:** Zahlen in Interpolationen und Verkettungen sieht keine Regel; die Kulturzeile der Matrix über das volle Manifest steht aus.
5. **D9:** eigenes Escaping (siehe oben).
6. **D5:** Kennung des Codestands, Dateihash im Protokoll, Match-Beschreibung (Phase 25); schwacher Programm-Fingerabdruck der Skript-VM.
7. **Nicht angefasst:** `LiveScenario` (Demo-Szenen über `float`), `Fixed.FromDecimal` über `System.Decimal`, Snapshot-JSON (`JsonStateWriter`) mit dem Standard-Encoder der Laufzeit (Format, kein Hash).
8. **Doku-Folgen** (nicht Teil dieses Punkts, die Dateien waren gesperrt): die Referenzwerte der sechs Missionen und die Gelände-Fingerabdrücke in `CLAUDE.md`, `docs/HANDOFF-PHASE18.md`, `docs/SIMULATION-CORE.md` und im Referenz-/Eingangsmanifest (`baselines/`) sind nach dem Merge neu zu schreiben.
