# Festkomma-Auflösung: von Q47.16 auf Q39.24 (Phase 22)

Stand: 6. Oktober 2026, Zweig der Phase 22, Umstellung in Commit `d7fca91`. Auftrag: `docs/handoffs/HANDOFF-PHASE22.md`; Rahmen: `docs/MULTIPLAYER-PLAN.md` §2.6 und §4 (E8). Alles hier ist bei **30 Hz** gemessen
(Release, Retaildaten `.local/content`, ein Rechner: AMD Ryzen 7 PRO 6850U, 16 logische Prozessoren, Windows 11, .NET 10.0.12). Wo eine Zahl nicht ruhig gemessen ist, steht es dabei.

`Fixed` ist ein `long` mit jetzt **24 Nachkommabits** (Auflösung 2⁻²⁴ ≈ 6 · 10⁻⁸, Bereich ± 5,5 · 10¹¹) statt 16 (2⁻¹⁶ ≈ 1,5 · 10⁻⁵, ± 1,4 · 10¹⁴). Die Umstellung ändert jeden Hash, jeden Snapshot und jedes Replay und
geschah deshalb allein: erst eine Vorarbeit, die kein Ergebnis ändert, dann ein Commit mit einer Ursache.

**Zwei Befunde stehen in §5 und §6:** (1) das Quadrat einiger Reichweiten aus den Daten (1.000.000) verlässt den neuen Bereich und warf – in keinem der gemessenen Läufe, aber
auf von einem Spieler erreichbaren Wegen. Die Umstellung hat das berichtet und nicht umgangen; **es ist seither behoben (§5a, Nachtrag vom 7. Oktober 2026), ohne einen Referenzwert zu ändern.** (2) Das Referenzmatch ist ein Zermürbungskrieg, dessen Ende von den Produktionswürfen abhängt; sein Startwert wurde in
einem eigenen Commit gewechselt.

## 1. Commits

| Commit | Was | Referenzwerte |
|---|---|---|
| `659db1d` | `SimTool reference-manifest results`: die Größen eines Laufs, die keine Hashes sind | unverändert |
| `6596310` | `FixedMath`: Produkt, Quotient, Wurzel und Winkelfunktionen für eine Bitzahl hergeleitet | unverändert |
| `756e978` | Rohwertrechnungen der Simulation ohne Annahme von 16 Bit | unverändert |
| `1c07699` | eine Umrechnung `Fixed` ↔ Fließkomma außerhalb der Simulation (`FixedConvert`) | unverändert |
| `d770cd4` | Quelltextregel „die Skala steht einmal“ für alle Projekte; Tests ohne Rohzahlen | unverändert |
| `065067a` | Tests in Einheiten; `reference-match --world-seed/--driver-seed/--record-only`; `rate-audit` listet den Wertebereich | unverändert |
| `5d1a09f` | Bau-Gesundheit in 128 Bit; `results --commands` (Befehlsströme als Text) | unverändert (`--set full`) |
| `41c3bc0` | **Referenzänderung, eine Ursache:** Startwert des Referenzmatchs `5EED0005` → `5EED0007` | nur `reference-match` |
| `a0e7b23` | Sichtkreis-Test in Maschinenworten bei 16 Nachkommabits | unverändert |
| **`d7fca91`** | **DIE UMSTELLUNG:** `Fixed.FractionBits` 16 → 24, Formatversionen, beide Manifeste neu | alle 50 Läufe |

Nach jedem Commit der Vorarbeit bestanden `input-manifest check` und `reference-manifest check --set quick`, am Ende (`5d1a09f`) `--set full` (50 Läufe identisch); nach `a0e7b23` wieder der Schnellsatz. Die Manifeste der Umstellung stehen im
Umstellungs-Commit selbst.

## 2. Die Vorarbeit (ohne Ergebnisänderung)

### 2.1 Arithmetik für eine Bitzahl (`src/CnCNew.Simulation/Numerics/FixedMath.cs`)

`FixedMath` rechnet auf Rohwerten mit der Zahl der Nachkommabits als Argument; `Fixed` und `FixedTrig` rufen es mit `Fixed.FractionBits`, die Tests auch mit anderen Bitzahlen. Jede Routine ist exakte
Ganzzahlarithmetik: Produkte und Quotienten sind die der 128-Bit-Formel (zur nächsten Zahl, Halbwerte weg von null), die Wurzel ist abgerundet, Überlauf wirft.

- **Produkt** und **Quotient mit kleinem Dividenden**: die Routinen der Phase 19, die Schiebeweiten folgen der Bitzahl.
- **Quotient mit großem Dividenden** (`|a| · 2^F` passt nicht in ein Maschinenwort): schriftliche Division auf Maschinenworten – erst `|a| / |b|`, dann werden die F Nachkommabits in den größten Stücken nachgezogen,
  die `Rest · 2^Stück` im Wort halten (der Rest ist kleiner als der Divisor, dessen führende Nullen sind der Platz). Statt einer 128-Bit-Softwaredivision zwei bis drei 64-Bit-Divisionen. Bei 16 Bit begann „groß“
  bei 2³¹ Einheiten und kam nicht vor; bei 24 Bit beginnt es bei 32.768 Einheiten (jedes Abstandsquadrat über 181 Einheiten).
- **Wurzel eines großen Werts**: die 64-Bit-Wurzel von so vielen Nachkommabits, wie in ein Wort passen (eine gerade Zahl weniger als alle), danach die restlichen Wurzelbits einzeln aus dem Vergleich des
  Kandidatenquadrats mit dem exakten 128-Bit-Radikanden. Höchstens zwölf Schritte bei 24 Bit.
- **Winkel:** π, 2π, π/2 und die Arkustangens-Tabelle sind fest einkodierte Konstanten mit 61 Nachkommabits, auf die Auflösung gerundet (nie zur Laufzeit aus `Math.*`). Bei 16 Bit ergibt das exakt die Zahlen, die
  vorher ausgeschrieben waren (205.887, 411.775, 102.944 und die 16 Tabelleneinträge). Der CORDIC (`Atan2`) macht einen Schritt je Nachkommabit und skaliert den Vektor um die Nachkommabits – um weniger, wenn der
  Vektor sonst das Wort verließe (über 2⁴⁵ roh lieferte die alte Routine still Unsinn). `Sin`/`Cos` behalten die Taylor-Polynome (bis x¹³ bzw. x¹²: Abbruchfehler 7 · 10⁻¹⁰ bzw. 7 · 10⁻⁹, unter jeder unterstützten
  Auflösung); was bleibt, ist die Rundung der sieben Schritte, und die schrumpft mit der Auflösung. Die Divisionen durch die Reihenglieder sind Ganzzahldivisionen (bitgleich zum Festkommaquotienten, weil die
  Glieder gerade Zahlen sind).

**Äquivalenzbeweise** (`tests/CnCNew.Simulation.Tests/Tests.FixedResolution.cs`):

| Test | Was | Umfang |
|---|---|---|
| `FixedMathMatchesWideArithmeticAtEveryResolution` | Produkt, Quotient, Wurzel gegen `BigInteger`-Formeln (Wurzel per Bisektion, unabhängig von der Routine) bei **16, 24, 12 und 32** Bit | je Bitzahl über 1 Million Paare (Ränder, zufällige Bitlängen, Koordinaten, Abstandsquadrate bis 10⁸, Projektionen „großes Quadrat durch großes Quadrat“) und über 150.000 Wurzeln; dazu `Fixed` selbst |
| `FixedTrigReproducesTheSixteenBitResults` | die Winkelfunktionen bei 16 Bit gegen die **frühere Implementierung**, die wortgleich als Orakel im Test steht | `Sin`/`Cos`: jeder Rohwinkel von drei Umdrehungen in beide Richtungen (2,47 Millionen) plus 300.000 beliebig große; `Atan2`: jeder Vektor eines Rohgitters ±260, eines Gitters in ganzen Einheiten und 6 Millionen Vektoren jeder Länge bis 2⁴⁴ roh |
| `FixedTrigPrecisionFollowsTheResolution` | Fehler gegen `Math.Sin/Cos/Atan2` je Auflösung, Konstanten bei 24 Bit, sehr lange Vektoren | Zahlen unten |

Gemessener größter Fehler der Winkelfunktionen gegen die exakten:

| Auflösung | `Sin` | `Cos` | `Atan2` (rad) |
|---|---|---|---|
| 16 Bit (ein Schritt 1,5 · 10⁻⁵) | 2,59 · 10⁻⁵ | 2,42 · 10⁻⁵ | 5,94 · 10⁻⁵ |
| 24 Bit (ein Schritt 6,0 · 10⁻⁸) | 1,00 · 10⁻⁷ | 1,15 · 10⁻⁷ | 2,69 · 10⁻⁷ |

Die Genauigkeit folgt also der Auflösung (Faktor 210 bis 260 bei Faktor 256), ohne höhere Polynomordnung und ohne Schutzbits; das Dokument der Phase 6 nannte für `Atan2` nur die Schranke 10⁻³.

### 2.2 Rohwertrechnungen in der Simulation

Alles, was direkt auf Rohwerten rechnet, wurde gelesen (`.Raw`, `FromRaw`, Schiebeoperationen). Die Simulation ist **nicht** insgesamt `checked` – nur die Operatoren von `Fixed` –, eine Rohwertrechnung, die den
Bereich verlässt, wickelt also still. Geändert:

| Stelle | Vorher | Jetzt |
|---|---|---|
| `VisionMap` (drei Kreistests: `Trace`, `RevealReference`, `ForCircle`) | Quadrate von Rohdifferenzen in `long`, ungeprüft; Literal `65536` für die halbe Zelle | der Kreistest ist **bei 16 Nachkommabits definiert**, gleich welche Auflösung `Fixed` hat: Mittelpunkt und Radius werden darauf gekürzt, die Zellmitten (ganze Einheiten) sind darin exakt. Bei 24 Bit bräuchte das Quadrat eines Radius von 300 Einheiten 67 Bit; ein 128-Bit-Test war ein Drittel langsamer (§8.2). Die Rechnung ist jetzt `checked`: ein Radius über 46.000 Einheiten wirft, statt wie bisher still zu wickeln. Eine Zelle ist zehn Einheiten breit. |
| `StructureToppleUpdate` | Produkt dreier Rohwerte „exakt bei 48 Nachkommabits“, Startwinkel als Literal | Produkt in 128 Bit, auf die feine Auflösung (Nachkommabits + 32) gekürzt; der Startwinkel 0,001 folgt aus der Bitzahl. Bei 16 Bit wird nichts gekürzt. |
| Leerlauf-Scan der Türme (`DrawScanFraction`) | `Bits % 65536` | die niedrigsten 16 Bit des Zugs sind die 16 führenden Binärstellen, weitere Stellen kommen aus den Bits darüber: derselbe Zug ist bei jeder Auflösung dieselbe Zahl, soweit deren Stellen reichen. |
| `JetAIUpdate`, `AIUpdateInterface` | `FromRaw(34315)` (π/6), `FromRaw(1024)` (zweimal) | `FixedTrig.PiFraction(1, 6)`, `Fixed.FromFraction(1, 64)` |
| `ConstructionHealthTarget` | Rohgesundheit × Arbeitsschritte in `long`, ungeprüft | in 128 Bit (die Daten kennen eine Gesundheit von 99.999.999; bei 24 Bit wickelte das Produkt bei einem langen Bau) |

**Bewusst rohwertabhängig und unverändert:** der Zustandshash (`HashStateWriter` mischt Rohwerte), Snapshot (`JsonState` schreibt Rohwerte), Replay und Befehle (Rohkoordinaten), die Ereignismengen (`SimEvent.Value`
trägt Schaden und Geld als Rohwert), der Fingerabdruck von Gelände, Katalog und Knochentabelle (Rohwerte als Text bzw. Bytes), der Zeit-Messfühler („unter acht signifikanten Bits“ ist eine Aussage über Rohwerte).
Diese Stellen sind der Grund für die neuen Formatversionen (§3). **Geprüft und von der Bitzahl unabhängig:** `decimal` → `Fixed` beim Laden (`FromDecimal` rundet auf die Auflösung; Werte aus den Daten behalten jetzt
mehr Stellen), Geländehöhen und Kartenobjekte (`FromInt`/`FromDecimal`), die Knochentabelle (Schritte von 1/16 Einheit und 1/1024 rad sind in beiden Auflösungen exakt), die Zellumrechnung der Navigation
(`Fixed`-Operationen), `SageRandom.Range` (128-Bit-Zwischenwert), der Stimmungsversatz (Ganzzahlen in Bildern), Flugzeiten (Verhältnis zweier Rohwerte).

### 2.3 Außerhalb der Simulation: eine Umrechnung

`CnCNew.Presentation.FixedConvert` (`ToDouble`, `ToSingle`, `RawToSingle`, `FromDouble`, `FromSingle`, `FromSingleTruncated`) ist die einzige Stelle außerhalb der Simulation, die die Skala benutzt. 15 von Hand
geschriebene Umrechnungen in 12 Dateien rufen sie jetzt (`x.Raw / 65536f`, `value.Raw / (double)Fixed.ScaleRaw`, `(long)(x * 65536f)`, `Math.Round(value * ScaleRaw)` in `LiveMatch`, `LiveScenario`, `LiveMission`,
`FxExecutor`, `FxWorldView`, `MinimapImage`, `SimulationQueries`, `TurretTracker`, `Suspension`, `FrameProjector`, `SimTool`, `PresentationTool`); jede liefert die Bits des ersetzten Ausdrucks (Division und Produkt
mit einer Zweierpotenz sind exakt, jede Variante rundete dieselbe Zahl einmal). Die Toleranz des `UnitConditionTracker` ist 1/8192 statt roh 8. Außerhalb der Simulation wurden nur diese Zeilen angefasst.
Daneben gibt es 21 Stellen, die über `ToDecimal()` nach `float`/`double` gehen; sie hängen nicht von der Skala ab und blieben, wie sie sind.

### 2.4 Quelltextregel (`Tests.FixedScaleSourceRules`)

Dritter lexikalischer Regelsatz neben `SourceRules` und `FeederSourceRules`, über `src`, `tools` und `tests`:

1. **Die Skala nie als Zahl oder Verschiebung**, weder 2¹⁶ noch 2²⁴: `65536`, `16777216`, `0x10000`, `0x1000000`, `<< 16`, `>> 16`, `<< 24`, `>> 24` (auch mit Suffix oder Unterstrichen). 33 benannte Ausnahmen
   für Treffer, die etwas anderes sind: Farbkanäle und Bytes eines Worts (12), Dateiformate (9: WAV-24-Bit-Probe, RefPack-Länge, vier CSF-Marken, drei W3D-Felder), sonstige (7: log₁₀ 2 in `SingleBits`, ein
   Optionsbit, eine Puffergröße, Testwürfel, Singles um 2²⁴) und das 16-Bit-Orakel der Winkelfunktionen (5 Zeilen).
2. **Außerhalb der Simulation keine Verwendung von `Fixed.ScaleRaw`, `Fixed.FractionBits` oder `FixedMath`**: 4 Ausnahmen (die drei Zeilen von `FixedConvert`, ein Zufallswert des Wegsuche-Benchmarks).
3. **Kein Rohwert als Zahl** (`FromRaw(1024)`) in Produktcode, auch nicht in der Simulation: keine Ausnahme.

Wie jede Regel dort ist sie ein Stolperdraht (reguläre Ausdrücke über Codezeilen); eine Ausnahme, die nichts mehr trifft, lässt den Test scheitern. Der Selbsttest der Regel fand beim Schreiben ihr erstes Loch
(`65536f` mit Suffix).

## 3. Die Umstellung (`d7fca91`)

Eine Ursache: `Fixed.FractionBits = 24`. Keine andere Verhaltensänderung im Commit.

| Format | alt | neu | Ablehnung eines alten Stands |
|---|---|---|---|
| `WorldSnapshot.Version` | 9 | **10** | `Unsupported snapshot version 9 (expected 10).` |
| `ReplayFormat.CurrentVersion` | 11 | **12** | `Unsupported replay format version 11 (expected 12).` |
| `CommandTypes.Version` | 14 | **15** | `The replay uses command table version 14; this build implements 15.` |
| `MissionSnapshot.Version` | 2 | **3** | `Unsupported mission state version 2 (expected 3).` |
| `SaveGame.Version` | 2 (seit Phase 21: Rate in der Hülle; im Zweig dieser Phase noch 1) | **3** (nach dem Merge mit Phase 21) | `Unsupported savegame version 2 (this game reads version 3).` |

Ein alter Stand wird an seiner Version abgelehnt und nie mit der neuen Skala gelesen; Tests: `Tests.StatesOfSixteenFractionBitsAreRejected` (Snapshot, Replay-Container, Befehlstabelle), `Tests.Scripting`
(Missionszustand), `CampaignTests` (Speicherstand).

Im selben Commit: `baselines/reference-manifest.json` (alle 50 Läufe neu, danach im vollen Satz gegengeprüft) und `baselines/input-manifest.json` (Kataloge, Gelände und Knochentabelle werden als Rohwerte gehasht;
Welt-Fingerabdruck `49A55E310F780F60`). Testerwartungen, die Rohwerte oder alte Versionen einkodierten: Versionszahlen in drei Tests; die Winkelgeschwindigkeit von 180° je Tick wird mit `FixedTrig.Pi` verglichen;
der Wert, der bei 120 Hz ganz verloren geht, ist jetzt 0,0004 je s² (die 0,1 des `NapalmBombLocomotor`, die bei 16 Bit verloren gingen, sind 117 Rohschritte); der Golden-Hash der Knochentabelle (als Rohwerte
gehasht, dieselben Posen); die Schranke des Fahrwerk-Tests (§9).

## 4. Referenzwerte alt und neu

| Größe | bis Phase 21 (16 Bit, Startwert `5EED0005`) | `41c3bc0` (16 Bit, Startwert `5EED0007`) | `d7fca91` (24 Bit) |
|---|---|---|---|
| Referenzmatch | FactionGLA bei Tick 14.490 | FactionGLA bei Tick 14.055 | **FactionGLA bei Tick 17.805** |
| – Endhash / Kette | `23B4B25DF62765D8` / `DE5849AF9F8493C4` | `AE9C688309DA37E4` / `FC6DFA759B1DCAF0` | **`D0CA5F4EE456D6CE` / `C5EE1090E1C24D7B`** |
| Stresstest Endhash / Kette | `56882F763EABEF67` / `B8E663493F2C73D5` | unverändert | **`56882F763EABEF67`** (der leere Endzustand, unverändert) / **`754FA4DCB9A41DF7`** |
| 16 KI-Läufe entschieden | 12 | 12 | **12** (fünf mit anderem Ausgang, §7.2) |
| Formate | Snapshot 9, Replay 11, Befehle 14, Mission 2, Savegame 1 | unverändert | **Snapshot 10, Replay 12, Befehle 15, Mission 3, Savegame 2** |

Die Endhashes und Ketten aller 50 Läufe stehen in `baselines/reference-manifest.json`; die Tabelle in Anhang B stellt alt und neu nebeneinander.

## 5. Befund: Werte, deren Quadrat den Bereich verlässt (Stand der Umstellung; behoben in §5a)

Der Bereich schrumpft um den Faktor 256 auf ± 5,5 · 10¹¹. Die Wurzel der größten Zahl ist **741.455**: ein größerer Wert darf nicht quadriert werden, sein Quadrat wirft `OverflowException` (bei 16 Bit lag die
Grenze bei 11,9 Millionen). `SimTool rate-audit <content>` listet seit der Vorarbeit jeden Katalogwert über dieser Grenze:

| Feld | Werte über 741.455 | größter | Wird der Wert quadriert? |
|---|---:|---|---|
| `WeaponInfo.MinWeaponSpeed` | 360 | 999.999 (Vorgabe „sofort“) | nein (nur Vergleiche und Quotienten) |
| `SpecialAbilityUpdateData.AbilityAbortRange` | 86 | 10.000.000 (Vorgabe „nie“) | nein: `SpecialAbilityStaging` fragt vorher `< HugeDistance` |
| `BodyData.MaxHealth` / `InitialHealth` | je 62 | 99.999.999 (`CargoTruckNuke`) | nein; Gesundheit × 100 = 10¹⁰ bleibt im Bereich (Abstand Faktor 55) |
| `WeaponInfo.WeaponSpeed` | 52 | 999.999 | nein |
| **`WeaponInfo.AttackRange`** | **8** | 1.000.000 (`AircraftCarrierOrderLaunch`), 999.999 (`ScudStormWeapon` und seine drei Generalsvarianten, `CINE_RangerAdvancedCombatRifle`, `CINE_GLAJarmenKellRifle`, `CINE_USAPathfinderSniperRifle`) | **ja**: jede Reichweitenprüfung rechnet `reach * reach` (`SimulationWorld.WithinWeaponRange`, `Turrets.cs`, `Combat.cs`, `AirWeaponModules`, die Szenarien) – rund zehn Stellen |
| **`SpecialAbilityUpdateData.StartAbilityRange`** | **6** | 1.000.000 (die sechs Bombenlaster: `GLAVehicleBombTruck` und Generalsvarianten) | **ja**: `SpecialAbilityUpdate.WithinSpheres` rechnet `reach * reach`, sobald die Fähigkeit ein Zielobjekt hat |
| `WeaponInfo.PrimaryDamage` | 3 | 999.999 | nein (Schaden × Bonus) |

Bei 16 Bit lagen nur die zehn Gesundheitswerte von 99.999.999 über der Grenze.

**Was gemessen ist:** kein Lauf des Manifests, kein Kampagnenlauf (Audit und Zielfahrer, je 24 Karten), kein Test und keiner der Probeläufe bei 60 und 120 Hz hat einen Überlauf geworfen (§7, §10, §11). **Was
daraus nicht folgt:** dass diese Wege nicht erreichbar wären. Die Verkleidung des Bombenlasters mit einem Fahrzeug als Ziel ist ein gewöhnlicher Spielerbefehl; kein gemessener Lauf gibt ihn. Bei 16 Bit rechnete
derselbe Weg (10¹² lag im Bereich), bei 24 Bit wirft er. **Vor einer Veröffentlichung muss das behoben sein.** Der Auftrag verlangt, einen solchen Zwischenwert zu melden, bevor er umgangen wird; deshalb ist hier
nichts umgangen.

**Vorschlag (nicht umgesetzt):** an den Stellen, die eine Reichweite aus den Daten quadrieren, um mit einem Abstandsquadrat zu vergleichen, das Quadrat über eine benannte Funktion bilden, die bei einem Wert über
der Grenze die größte Zahl liefert (`Fixed.SquaredBound(reach)` o. ä.). Ein Abstandsquadrat, das selbst darstellbar ist, ist nie größer; der Vergleich hat dann für jeden darstellbaren Abstand dasselbe Ergebnis wie
mit dem exakten Quadrat, und bei 16 Bit ändert sich kein Ergebnis. Es ist keine stille Sättigung: die Funktion heißt so und steht nur in Vergleichen. Betroffen sind die Reichweitenprüfungen der Waffen (über
`RangeWith`/`MaxWeaponRange`) und `WithinSpheres`; die übrigen rund 30 Stellen der Form `range * range` nehmen Werte, die in den Daten klein sind (`ScanRange`, `TriggerRange` …) – `rate-audit` hält das fest. Jede
ergebnisneutrale Lösung lässt die hier gemessenen Referenzwerte unberührt (sie betrifft nur Wege, die heute werfen). Die Alternative, eine andere Auflösung, würde sie ändern: bei 20 Nachkommabits läge die Grenze
bei 2,97 Millionen (die Reichweiten 10⁶ passten, `AbilityAbortRange` 10⁷ weiterhin nicht), eine Beschleunigung von 1 Einheit/s² hätte bei 60 Hz 291 statt 4.660 Rohschritte.

**Größte gemessene Zwischenwerte** (Wegwerf-Build mit einem Messfühler in jedem Operator von `Fixed`, über alle 50 Läufe des Manifests und den Zielfahrer aller 24 Kampagnenkarten; die Sentinels `MaxValue` als „noch
kein bester Wert“ ausgenommen):

| Operator | größtes Ergebnis | Abstand zum Rand | wo |
|---|---:|---:|---|
| Produkt | 1,0 · 10¹⁰ (100.000 × 100.000) | Faktor **55** | `SkirmishAi.UseTunnels`: die Konstante 100.000 als „überall“, quadriert |
| Summe | 5,86 · 10⁷ | Faktor 9.400 | Abstandsquadrate (`DealDamageCore`; im Gefecht `SkirmishAi.DistanceSquared` mit 5,45 · 10⁷) – Abstände bis rund 7.600 Einheiten |
| Differenz | 5,0 · 10⁷ | Faktor 11.000 | Geld (`SimulationWorld.Execute`, 50.000.000 − 1.000) |
| Quotient | 1,2 · 10⁵ | Faktor 4,6 · 10⁶ | `ActiveBody.SetInitialHealth` |
| `Fixed * long` | 3,0 · 10⁵ | Faktor 1,8 · 10⁶ | Skriptbedingung (3.000 × 100) |

Nicht vom Messfühler erfasst: die Rohwertrechnungen außerhalb der Operatoren (Sicht, Topple, Bau – in §2.2 auf `checked` bzw. 128 Bit gestellt) und Wege, die kein Lauf betritt. `StructureToppleUpdate` hält Winkel
und Winkelgeschwindigkeit mit 56 Nachkommabits; ein Viertelkreis ist dort 1,1 · 10¹⁷, Abstand zum Rand des `long` Faktor 81.

## 5a. Behebung (Nachtrag, 7. Oktober 2026): Vergleiche über `SquaredReach`

Der Befund aus §5 ist behoben, ergebnisneutral: jeder Vergleich, der vorher rechnen konnte, gibt dieselbe Antwort; nur Wege, die vorher warfen, rechnen jetzt. Kein Referenzwert hat sich geändert
(`reference-manifest check --set full` und `input-manifest check` identisch, §5a.7). Der Katalogwert bleibt, was die Daten sagen – nichts wird beim Laden gekappt, kein `if (range > …)` steht an einer Aufrufstelle.

### 5a.1 Was der Befund in §5 unterschätzt und was er überschätzt hat

Systematisch gesucht (alle Produkte zweier `Fixed`-Werte der Simulation, mit einem Wegwerf-Werkzeug auf dem Syntaxbaum des Compilers typgenau aufgezählt: 857 Produkte, davon 413 eines Werts mit sich selbst;
dazu `CnCNew.Game`, `CnCNew.Presentation` und die Werkzeuge):

- **Mehr Stellen als die zwei genannten.** Rund 95 Stellen quadrierten eine Reichweite, einen Radius oder eine Grenze für einen Vergleich (jetzt 91 Zeilen mit `SquaredReach` in 38 Dateien). Die Reichweite einer Waffe läuft über `RangeWith`/`MaxWeaponRange`
  außer in `WithinWeaponRange` auch in die Zielerfassung (`AcquireRadius` → `FindMoodTarget`), die Angriffsabsicht der Türme (`KeepsAttackIntent`), `AssaultTransportAIUpdate.DetectDeployShot` (Waffen mit `DamageType = DEPLOY`, wie die des
  Trägers), `SlavedUpdate` (`MasterTarget`), die beiden Treiber (`ReferenceMatchScenario`, `ObjectiveDriver`) und – **außerhalb der Simulation** – in `SimulationQueries.InRange`, die Frage
  des Mauszeigers „ist das Ziel in Reichweite der Auswahl“ (`CommandModeController.Hover`, `StandingOrders`).
- **Der Weg, den ein Spieler wirklich erreicht, war der Mauszeiger:** ein ausgewählter Scud-Sturm (Reichweite 999.999) und der Zeiger über einem Feind – `InRange` warf. Die Simulation selbst quadriert die
  Reichweite des Scud-Sturms auf keinem Befehlsweg (`AutoChooseSources = PRIMARY NONE`: `ChooseWeapon` wählt die Waffe für keine Befehlsquelle, bevor die Reichweite geprüft wird; gefeuert wird über
  `OCLSpecialPower`). Der Flugzeugträger (`AircraftCarrierOrderLaunch`, 1.000.000) und die drei Filmsoldaten warfen in der Simulation schon beim Feuerbefehl, die Soldaten auch ohne Befehl, sobald ein Feind neben ihnen stand.
- **Die Verkleidung des Bombenlasters ist heute nicht erreichbar.** Für `SPECIAL_DISGUISE_AS_VEHICLE` gibt es keinen Handler (`SpecialAbilityUpdate.Handles` kennt nur die Übernahme-Fähigkeiten); der Befehl wird mit
  `SpecialPowerHandlerMissing` abgelehnt, bevor `StartAbilityRange` gelesen wird. Die Aussage in §5, das sei ein gewöhnlicher Spielerbefehl, der werfe, stimmte für den Befehl, nicht für das Werfen. Der Weg selbst
  (`IsWithinStartRange` → `WithinSpheres`) warf mit einer Fähigkeit, die einen Handler hat (synthetischer Test), und ist behoben. Die sechs Objekte sind fünf Bombenlaster und `GC_Slth_GLAVehicleBattleBus`.
- **Ein Produkt, das §5 als ungefährlich führte, läuft über:** `ActiveBody.AdjustMaxHealth` bildet für `PRESERVE_RATIO` `Health * newMax / oldMax`. Ab einer Gesundheit von 741.455 (62 Retail-Objekte) verlässt das
  Produkt den Bereich, sobald ein Bonus das Maximum ändert (Veteranenstufe, Kampfplan, `MaxHealthUpgrade`). Für die zehn Objekte mit 99.999.999 warf das schon bei 16 Bit (10¹⁶), für Werte bis 11,9 Millionen ist
  es eine Regression der Phase 22.
- **Sichtkreise:** seit der Vorarbeit der Phase 22 (§2.2) wirft der Kreistest der `VisionMap` für einen Radius über 46.340 Einheiten (vorher wickelte er still). `ShroudClearingRange` reicht in den Daten bis 1.000; ein Skript kann aber
  mit jedem Radius aufdecken.

### 5a.2 Entwurf

`src/CnCNew.Simulation/Numerics/SquaredReach.cs`, ein Werttyp für „das Quadrat einer Reichweite, nur zum Vergleichen“:

| Aufruf | Bedeutung |
|---|---|
| `SquaredReach.Of(reach)` | das Quadrat, wie `Fixed` es bildet (Betrag², zur nächsten Zahl gerundet, Halbwerte weg von null) – in 128 Bit |
| `.Contains(d²)` | `d² <= reach²` |
| `.ContainsStrictly(d²)` | `d² < reach²` |
| `SquaredReach.OfSquare(q)` | ein Wert, der schon ein Quadrat ist (bestes Abstandsquadrat einer Suche, „kein Radius“ = `MaxValue`) |
| `SquaredReach.ContainsStrictly(reach, d², margin)` | `d² + margin < reach²`, Summe und Quadrat exakt (die Mindestreichweite: `distSqr < minAttackRangeSqr - 0.5`) |
| `SquaredReach.Compare(a, b)` | Vorzeichen von `a² - b²` für zwei Längen, deren Quadrate verglichen wurden |
| `Fixed.MultiplyDivide(a, b, c)` | `a * b / c` mit beiden Rundungen der zwei Operatoren, das Produkt in 128 Bit |

**Warum 128 Bit und nicht „über der Grenze = immer innerhalb“ an der Aufrufstelle:** der Typ speichert das gerundete Quadrat als Maschinenwort, oder – wo es das Wort verlässt – die größte Zahl und ein Merkbit.
`Contains` ist dann ein einziger Vergleich zweier Maschinenworte (`d.Raw <= raw`): liegt das wahre Quadrat jenseits der größten Zahl, ist jedes darstellbare Abstandsquadrat kleiner, und der Vergleich mit der
größten Zahl sagt genau das. Nur `ContainsStrictly` braucht das Merkbit (für `d² = MaxValue`, das als Markierung „noch nichts gefunden“ vorkommt). Das ist keine Sättigung eines Ergebnisses: das gesättigte Wort
verlässt den Typ nie, es gibt keinen Weg, es als Zahl zu lesen. Die Bildung (`FixedMath.SquareSaturated`) ist dieselbe 64×64-Multiplikation mit derselben Rundung wie `FixedMath.Multiply`, ohne dessen Ausnahme:
der schnelle Weg kostet, was `reach * reach` kostete.

**Beweis der Ergebnisneutralität.** (a) Aus der Konstruktion: `Multiply(r, r)` liefert `(|r|² + 2²³) >> 24`, wenn das unter 2⁶³ liegt, sonst wirft es; `SquareSaturated` liefert denselben Ausdruck unter derselben
Bedingung (`high >> 23 == 0`), sonst `long.MaxValue`. Für jedes `r`, für das der alte Ausdruck rechnete, ist das gespeicherte Wort also der alte Wert, und `Contains`/`ContainsStrictly` sind die alten Vergleiche
`<=`/`<` auf denselben zwei Worten. (b) Gemessen gegen ein unabhängiges Orakel (`Tests.SquaredReachGivesTheExactComparison`, `BigInteger`): 5.328.044 Vergleiche – jeder Rohwert in ±60.000 Schritten um die alte
Überlaufgrenze (größter quadrierbarer Rohwert 12.439.554.047.901 = 741.455,2 Einheiten; einen Schritt darüber wirft das Produkt und `IsBeyondRange` kippt), die Sentinels der Daten und des Codes mit ihren
Nachbarn (999.999, 1.000.000, 10.000.000, 99.999.999, die Ränder des Bereichs), 300.000 Werte jeder Größenordnung; je Reichweite die Abstände um das exakte Quadrat (±2 Rohschritte), 0, ±1, die Ränder,
Zufallswerte. 3.885.182 davon zusätzlich gegen `d <= reach * reach` und `d < reach * reach`, wo das Produkt rechnet. Dazu 400.000 Vergleiche mit Marge (gegen `BigInteger` und gegen den ersetzten Ausdruck
`d >= reach * reach - margin`) und 784.051 Quotienten von Produkten (`MultiplyDivide` gegen die Formel mit beiden Rundungen, 485.249 davon auch gegen `a * b / c`, 241.176 mit einem Produkt, das nur 128 Bit halten).
(c) Am ganzen Programm: das volle Manifest und das Eingangsmanifest sind identisch.

**Suchen mit laufendem Bestwert** (`var best = range * range; … if (distance > best) continue; best = distance;`) trennen jetzt Grenze und Bestwert: `var limit = SquaredReach.Of(range)`, der Bestwert gilt erst, wenn
ein Kandidat gefunden ist. Für jeden Kandidaten ist die Bedingung dieselbe wie vorher (der alte Bestwert war nie größer als die Grenze).

**Sichtkreise** rechnen weiter in Maschinenworten bei 16 Nachkommabits (§2.2); `VisionMap.SquaredRadius` liefert für einen Radius, dessen Quadrat das Wort verlässt (über 46.340 Einheiten), die größte Zahl. Exakt für
jede Zelle, die höchstens 32.768 Einheiten vom Mittelpunkt liegt (dann passt die Summe zweier Differenzquadrate); die Differenzen bleiben `checked`.

### 5a.3 Audit: welches Feld wird wo quadriert

Größte Werte aus `SimTool rate-audit <content> --ranges` (neu: der größte Betrag jedes der 251 Festkommafelder des Retail-Katalogs; mit `--ranges-above N` gekürzt). „Quadriert in“ aus der Aufzählung der Produkte.

| Feld | größter Retailwert | quadriert in | Überlauf bei Q39.24 möglich? | jetzt |
|---|---:|---|---|---|
| `WeaponInfo.AttackRange` (× Reichweitenbonus: `RangeWith`, `MaxWeaponRange`) | 1.000.000 (8 Waffen über der Grenze, auf 11 Objekten) | `WithinWeaponRange`, `KeepsAttackIntent`, `AcquireRadius` → `FindMoodTarget`, `DetectDeployShot`, `MasterTarget`, beide Treiber, `SimulationQueries.InRange` (Präsentation) | **ja** | `SquaredReach` |
| `WeaponInfo.MinimumAttackRange` | 200 | `WithinWeaponRange` (`min² − 0,5`) | nein (Daten) | `SquaredReach.ContainsStrictly` mit Marge |
| `WeaponInfo.PrimaryRadius` / `SecondaryRadius` (× Radiusbonus) | 300 / 200 | `DealDamageCore`, `AirWeaponModules.WithinSphere3D` | nein (Daten) | `SquaredReach` |
| `WeaponInfo.RequestAssistRange` | 200 | `ProcessRequestAssistance` | nein | `SquaredReach` |
| `SpecialAbilityUpdateData.StartAbilityRange` | 1.000.000 (6 Objekte) | `SpecialAbilityUpdate.WithinSpheres` | **ja** (heute vor der Prüfung abgelehnt, §5a.1) | `SquaredReach` |
| `SpecialAbilityUpdateData.AbilityAbortRange` | 10.000.000 (= „nie“, 86 Objekte) | `WithinSpheres`, nur wenn unter 10.000.000 | ja für Werte zwischen 741.455 und 10⁷ | `SquaredReach` |
| `BodyData.MaxHealth` / `InitialHealth` | 99.999.999 (62 Objekte über der Grenze) | nie quadriert; `Health * newMax / oldMax` in `AdjustMaxHealth` | **ja** (Produkt) | `Fixed.MultiplyDivide` |
| `WeaponInfo.WeaponSpeed` / `MinWeaponSpeed` | 999.999 („sofort“) | nicht quadriert (Vergleiche, Quotienten, `ratio * (Speed − MinSpeed)`) | nein in Vergleichen; siehe §5a.6 | – |
| `WeaponInfo.PrimaryDamage` | 999.999 | nicht quadriert (× Bonus, × Rüstung) | nein | – |
| `ObjectTemplate.VisionRange` | 99.999 (`ChinaArtilleryCannon`) | `SightedObjects`, `EnemyInSight`, `AcquireRadius` (× Stimmungsfaktor), `StealthDetectorUpdate` (wenn `DetectionRange` fehlt) | nein (10¹⁰, Abstand Faktor 55) | `SquaredReach` |
| `ObjectTemplate.ShroudClearingRange` | 1.000 | `VisionMap` | nein | `VisionMap.SquaredRadius` |
| `ObjectTemplate.GeometryMajorRadius` / `MinorRadius` / `GeometryHeight` | 1.117 / 846 / 460 | `BoundingCircleRadius`/`BoundingSphereRadius` (Diagonale, unter der Wurzel); als Summe von Radien in Kollision, Zerquetschen, Andocken | nein | Diagonale: einfaches Produkt (benannte Ausnahme); Vergleiche: `SquaredReach` |
| Suchradien der Module (`ScanRange`, `ScanDistance`, `DetectionRange`, `TriggerRange`, `Radius`, `Range`, `RubOffRadius`, `BlastRadius`, `OuterRadius`, `DamageRadius`, `LockDistance`, `DiveStartDistance`, `DeliveryDistance`, `CloseEnoughDist` …) | höchstens 3.000 (`DeliverPayloadData.DeliveryDistance`), sonst höchstens 700 | je ein Vergleich im Modul | nein | `SquaredReach` |
| Ganzzahlfelder als Reichweite (`SlavedUpdate`: `GuardMaxRange`, `AttackRange`, `DistToTargetToGrantRangeBonus`; `MustCatchUpRadius`) | nicht im Festkomma-Audit | `SlaveModules` | – | `SquaredReach` |
| Radien aus Karten (Skriptparameter: Aufdecken, `RECRUIT_TEAM`, Spezialkraft-Radius, Sichtweite) | nicht im Katalog | `VisionMap`, `ScriptEngine.Powers`, `ScriptEngine.Extended` | je nach Karte | `SquaredReach` / `SquaredRadius` |
| Konstanten der KI und der Treiber (`DefenseRadius`, `EngageRadius`, `BoardingReach`, „überall“ = 100.000) | 100.000 | `SkirmishAi` | nein (10¹⁰) | `SquaredReach` |

Über der Grenze liegen im Retail-Katalog unverändert 639 Werte in 8 Feldern (Tabelle in §5). Kein weiteres Feld liegt über 100.000.

### 5a.4 Tests

| Test | Was | Auf dem Stand vor der Behebung (`e1687c8`) |
|---|---|---|
| `Tests.SquaredReachGivesTheExactComparison`, `Tests.MultiplyDivideEqualsTheTwoOperatorsWithoutTheirOverflow` | Orakel (§5a.2) | – (der Typ existiert dort nicht) |
| `Tests.WeaponsWithAnEverywhereRangeFire` | synthetisch: Waffen mit `AttackRange` 999.999 und 1.000.000 feuern auf Befehl und von selbst; eine Mindestreichweite von 800.000; ein Schadensradius von 1.000.000 | **wirft** `OverflowException` in `WithinWeaponRange` (beim Einreichen des Feuerbefehls) |
| `Tests.AbilityWithAnEverywhereStartRangeBeginsWhereTheUnitStands` | synthetisch: eine Fähigkeit mit Handler, `StartAbilityRange` 1.000.000, `AbilityAbortRange` 2.000.000 und einem Zielobjekt 600 Einheiten entfernt läuft durch ihre Stufen, die Einheit bewegt sich nicht | **wirft** in `SpecialAbilityUpdate.WithinSpheres` |
| `Tests.HealthRatioOfTheLargestBodiesSurvivesANewMaximum` | ein Körper mit 99.999.999 bekommt ein neues Maximum (`PRESERVE_RATIO`) | **wirft** in `ActiveBody.AdjustMaxHealth` |
| `Tests.VisionCircleWithAHugeRadiusCoversTheMap` | Radien 46.000, 46.341, 99.999, 1.000.000 decken die Karte auf (schneller Weg, Referenzweg, `ExploreCircle`) | **wirft** in `VisionMap.Trace` |
| `Tests.RealSentinelRanges` (`--content-dir`) | die elf Retail-Objekte mit einer der acht Waffen: Feuerbefehl auf Panzer und Soldat, 150 Bilder ohne Befehl; die sechs Objekte mit `StartAbilityRange` 1.000.000: die Fähigkeit auf ein Fahrzeug | **7 Schritte werfen:** Träger (Feuerbefehl), `CINE_AmericaInfantryRanger`, `CINE_AmericanInfantryPathfinder`, `CINE_GLAInfantryJarmenKell` (je Feuerbefehl und Stehen neben einem Feind) |
| `SentinelRangeTests` (Präsentation; mit `--content-dir` alle elf Retail-Objekte) | `SimulationQueries.InRange` für eine Reichweite von 999.999 | **wirft** (synthetisch und beim ersten Retail-Objekt) |
| `Tests.SquaredReachSourceRules` | Quelltextregel (§5a.5) | – |

Auf dem alten Stand wurden die Verhaltenstests unverändert in eine Kopie des Baums gelegt und ausgeführt; sie benutzen nur die öffentliche Oberfläche der Welt.

**Welche der 14 genannten Retail-Fälle wirklich durch ihre Prüfung liefen:**

| Fall | getrieben? |
|---|---|
| `AircraftCarrierOrderLaunch` (`AmericaAircraftCarrier`) | ja: Feuerbefehl angenommen, je ein Schuss auf Panzer und Soldat; warf vorher |
| `CINE_RangerAdvancedCombatRifle` | ja: feuert auf Befehl und von selbst; warf vorher |
| `CINE_USAPathfinderSniperRifle` | ja: ein Schuss (der Befehl auf den Panzer wird als ungültiges Ziel abgelehnt, die Prüfung läuft davor); warf vorher |
| `CINE_GLAJarmenKellRifle` | ja: Feuerbefehl und Stehen neben einem Feind warfen vorher und laufen jetzt durch; gefeuert wird nicht (der Befehl wird mit `NoWeapon` abgelehnt, `AutoChooseSources = NONE`) |
| `ScudStormWeapon`, `Chem_`, `Demo_`, `GC_Chem_ScudStormWeapon` (sieben Gebäude) | in der Simulation **nicht**: kein Befehlsweg quadriert ihre Reichweite (§5a.1), vorher wie nachher keine Ausnahme. **Ja** über `SimulationQueries.InRange` (Präsentationstest mit Retaildaten, alle sieben Gebäude); das warf vorher |
| `StartAbilityRange` 1.000.000 (fünf Bombenlaster, `GC_Slth_GLAVehicleBattleBus`) | **nein:** der Befehl wird mit `SpecialPowerHandlerMissing` abgelehnt, die Verkleidung ist nicht simuliert. Der Test hält genau diese Ablehnung fest und prüft die Stufen, sobald der Befehl angenommen wird |

`SimTool range-audit <content>` (neu) als Suchlauf darüber hinaus: jedes der 526 instanziierbaren Retail-Objekte mit einer Waffe steht 250 Einheiten vor einem Panzer und vor einem Soldaten, bekommt einen
Feuerbefehl und läuft 150 Bilder: 1.052 Läufe, **0 Ausnahmen**; 68 Objekte feuerten (447 Schüsse). Der Lauf hat kein Gelände und gibt keine Spezialkräfte; dass 458 Objekte dabei nicht feuern, ist kein Befund
und keine Abdeckung ihrer Schusswege.

### 5a.5 Was vor Rückfällen schützt

- **Quelltextregel `Tests.SquaredReachSourceRules`** (vierter lexikalischer Regelsatz): in `src/CnCNew.Simulation` wird kein Wert mit sich selbst multipliziert (`x * x`, `(a + b) * (a + b)`), außer sein letzter
  Bezeichner endet auf x, y oder z (eine Koordinatenkomponente: `dx * dx`, `v.X * v.X`). 21 benannte Ausnahmen mit Grund: Längen, die höchstens ein Abstand sind (3), Geometrie der Schablonen (3),
  Geschwindigkeiten je Tick und Brüche zwischen 0 und 1 (10), Ganzzahlen (5). Die Regel liest Namen, nicht Typen – ein Quadrat als `a * b` geht durch; sie ist ein Stolperdraht wie die anderen.
  Außerhalb der Simulation wäre sie zu laut (dort wird in `float` gerechnet, `radius * radius` ist dort gewöhnlich); die typgenaue Aufzählung fand dort ein einziges `Fixed`-Quadrat (`SimulationQueries.InRange`).
- **Die Tests aus §5a.4**, darunter zwei mit Retaildaten, die die Liste der Waffen und Objekte über der Grenze festhalten: ein neuer Wert über der Grenze in diesen Feldern lässt sie scheitern.
- **`SimTool rate-audit --ranges`** und **`SimTool range-audit`** für jede neue Datenquelle.

### 5a.6 Nur berichtet, nicht geändert

- **Abstandsquadrate selbst** (`dx * dx + dy * dy`, 110-mal `dx`/`dy`) sind einfache Produkte geblieben: zwei Positionen, die auf einer Achse mehr als 741.455 oder auf zwei Achsen mehr als rund 524.000 Einheiten
  auseinanderliegen, werfen. Karten sind wenige tausend Einheiten groß. **Aber: ein Befehl mit Zielkoordinaten wird beim Einreichen nicht gegen die Karte geprüft** (`SetMoveTargetCommand` und andere tragen
  Rohkoordinaten) – ein Bewegungsziel in 600.000 Einheiten Entfernung lässt die Bewegung der Einheit werfen (bei 16 Bit ab 8,4 Millionen). Für Mehrspieler ist das eine Frage der Befehlsprüfung (Phasen 27/28),
  kein Vergleich, den ein Helfer neutral beantworten könnte.
- **Gespeichert und gehasht** wird genau ein Quadrat: `DeliverPayloadAIUpdate._previousDistance2` (das Abstandsquadrat zum Ziel im vorigen Tick, im Snapshot als `previousDistance2`). Es ist ein Abstandsquadrat
  zweier Positionen (voriger Punkt), kein Quadrat eines Katalogwerts; unverändert.
- **`DeliverPayloadAIUpdate.MinTurnRadius`** liefert 999.999, wenn der Locomotor keine Drehrate hat, und `ConsiderNewApproach` setzt damit ein Bewegungsziel in 2,2 Millionen Einheiten Entfernung – danach wirft die
  Bewegung (voriger Punkt). Ob ein Retail-Lieferflugzeug ohne Drehrate existiert, ist nicht geprüft; kein gemessener Lauf betritt den Weg. Das Ziel ist gespeicherter Zustand und wurde nicht angefasst.
- **Geschosse mit `WeaponSpeed` 999.999** (52 Waffen): nicht quadriert; ein `MissileAIUpdate` mit `UseWeaponSpeed` bekäme diese Geschwindigkeit je Tick. `range-audit` feuerte, was es zum Feuern brachte, ohne
  Ausnahme; die Abdeckung dieser Waffen ist damit nicht belegt.
- **`VisionMap`:** ein Kreis, dessen Mittelpunkt weiter als 32.768 Einheiten von einer Zelle der Karte liegt, wirft weiterhin (geprüfte Differenzen), statt wie vor Phase 22 still zu wickeln.

### 5a.7 Prüfung

Commits: `b274df5` (Behebung), `7e93401` (Tests und Quelltextregel), `2bd08d2` (Werkzeuge). Gemessen am Quelltextstand von `b274df5` (die beiden folgenden Commits ändern keinen Produktcode der Simulation):

| Prüfung | Ergebnis |
|---|---|
| `SimTool input-manifest check` | identisch |
| `SimTool reference-manifest check --set quick` | 9 Läufe identisch (zweimal: vor und nach den letzten Kommentaränderungen) |
| `SimTool reference-manifest check --set full` | **50 Läufe identisch** |
| Simulationstests (Debug) ohne und mit `--content-dir` | bestanden (414 bzw. 429 Tests; das Referenzmatch im Test weiter FactionGLA bei Tick 17.805) |
| Simulationstests `-c Release -- --long` | bestanden |
| `CnCNew.Game.Tests` (Release, `--content-dir`) | bestanden |
| `CnCNew.Presentation.Tests` (ohne und mit `--content-dir`) | bestanden |
| Sauberer Build `dotnet build CnCNew.slnx` (`bin`/`obj` von `CnCNew.Client.Stride` gelöscht) | 0 Warnungen, 0 Fehler |
| `SimTool range-audit` | 1.052 Läufe, 0 Ausnahmen |

Nicht wiederholt: die Determinismus-Matrix (§8.1), `CampaignTool audit`/`drive`, die Szenen-Audits, `tools/CnCNew.UiRenderTool` – das volle Manifest (27 Missionsläufe eingeschlossen) ist identisch, weitere Läufe gab es dafür nicht.

**Leistung, Rücken an Rücken** (`SimTool perf <content> --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 12000`, alter Stand `e1687c8` und neuer Stand abwechselnd, je dreimal, ohne eigene Läufe
daneben; ein anderer Prüflauf arbeitete auf demselben Rechner). Mittel je Tick in Mikrosekunden ohne die ersten 300 Ticks; in allen sechs Läufen Endhash `F78F071AF5EC8A6E`, Kette `D9975439375F5DF7`:

| | Lauf 1 | Lauf 2 | Lauf 3 |
|---|---:|---:|---:|
| vorher: ganzer Tick | 2.088,3 | 2.115,6 | 2.070,3 |
| nachher: ganzer Tick | 2.089,7 | 2.080,9 | 2.071,7 |
| vorher: Phase „Updates“ (Zielerfassung, Waffenreichweite, Bewegung) | 807,5 | 832,2 | 805,0 |
| nachher: Phase „Updates“ | 821,7 | 814,4 | 811,2 |

Die Unterschiede liegen in der Streuung der Läufe desselben Programms (vorher 2.070 bis 2.116); ein Anstieg ist nicht messbar. Die absoluten Zahlen sind höher als in §8.2, weil der Rechner nicht ruhig war.

## 6. Befund: das Referenzmatch und das Dreiermatch der Tests sind metastabil

### 6.1 Was geschah

Bei 24 Bit endete das Referenzmatch mit dem bisherigen Startwert nicht mehr (kein Sieger in 45.000 Ticks; das volle Budget von 216.000 Ticks dauert über eine Stunde je Lauf), und der Test
„Phase 5: reference match scenario“ (drei Seiten der synthetischen Fixture) fand in 200.000 Ticks keinen Sieger, wo er bei 16 Bit bei Tick 13.080 endete.

### 6.2 Ursache, mit Beleg

Beide Szenarien sind Zermürbungskriege zwischen gleich starken Seiten mit laufender Produktion; sie enden, wenn eine Nächstes-Ziel-Entscheidung zwischen zwei fast gleich weit entfernten Einheiten das Gleichgewicht
kippt.

- **Dreiermatch (kein Zufallszug in der Fixture; Welt- und Treiber-Startwert haben nachweislich keinen Einfluss):** bis Tick 3.000 sind die Verläufe statistisch gleich (643 Schüsse, 121 Tode in beiden
  Auflösungen), die Positionen laufen stetig auseinander: bei 16 Bit hält eine beschleunigende Einheit die Geschwindigkeit 0,50011, wo der exakte Wert 0,5 ist und bei 24 Bit 0,50000 steht (die Quantisierung der
  Beschleunigung je Tick – der Grund für diese Phase); bei Tick 2.145 liegt dieselbe Einheit in den beiden Auflösungen 0,7 Einheiten auseinander. **Erste abweichende Entscheidung: Tick 2.160**, Einheit 100 verfolgt bei 16 Bit das Ziel
  88, bei 24 Bit bleibt sie beim Ziel 87. Wie empfindlich das Ende ist, zeigt der Arenaradius (16 Bit / 24 Bit, Siegtick): 300: 2.895 / 2.895; 350: 10.980 / 13.230; 380: 49.035 / 45.720; 390: 5.640 / 11.325;
  398: **10.200 / 10.200**; 399: 30.870 / 4.050; 400: 13.080 / kein Sieger in 200.000; 401: 12.060 / 12.435; 402: 40.275 / 31.470; 410: 19.860 / 76.605; 420: 16.425 / 29.865; 450: 10.380 / 15.825;
  500: 40.020 / 10.335. Ein Schritt des Radius um eine Einheit verschiebt das Ende um eine Größenordnung, in beiden Auflösungen. Der Test benutzt seit `065067a` den Radius 398, dessen Match in beiden Auflösungen
  denselben Verlauf nimmt.
- **Referenzmatch (Retaildaten, 13 Fraktionen auf einem Kreis):** die **erste abweichende Entscheidung fällt in Tick 15**, beim ersten Befehl: Einheit 41 verfolgt bei 16 Bit das Ziel 73, bei 24 Bit das Ziel 71.
  Die Basen stehen auf einem Kreis; die beiden Ziele stehen bei den zwei Nachbarbasen und sind von der Basis der Einheit praktisch gleich weit entfernt (aus den Zielpositionen der Befehle: 700,2 und 700,3 Einheiten). Welches „näher“ ist, entscheiden die letzten Stellen der Positionen. Von da an ist es ein
  anderes Match. Ob es endet, hängt von den Produktionswürfen ab – Erhebung über 36 Treiber-Startwerte (`5EED0005` + 0…35, 45.000 Ticks, `SimTool reference-match --driver-seed … --record-only`):

  | | 16 Bit | 24 Bit |
  |---|---|---|
  | entschieden | 32 von 36 | 31 von 36 |
  | Siegtick kleinster / Median / größter | 11.955 / 15.158 / 24.495 | 11.910 / 16.995 / 29.745 |
  | Sieger | GLA 29, AmericaSuperWeaponGeneral 2, America 1 | GLA 24, America 4, GLAToxinGeneral 1, GLAStealthGeneral 1, AmericaSuperWeaponGeneral 1 |
  | nicht entschieden (Startwert + …) | 9, 17, 30, 34 | 0, 1, 6, 7, 31 |

  Ein nicht entschiedenes Match hat in beiden Auflösungen dieselbe Anatomie (je eines untersucht, 16 Bit +9 und 24 Bit +6): eine US-Fraktion ist übrig und hält mehrere hundert Einheiten, die das, was von den
  anderen bleibt, nicht angreifen können (GLA-Löcher, Chinooks). Das ist eine Schwäche des Szenarios, keine der Umstellung. Die Verteilungen sind mit 36 Startwerten nicht zu unterscheiden (Rangsummentest über
  die Dauer, zweiseitig p = 0,10); eine Verschiebung der Größenordnung zehn Prozent ist damit **nicht ausgeschlossen** und wäre, soweit sie besteht, eine Folge exakterer Raten (bei 16 Bit lagen zwei
  Katalogumrechnungen um 3,9 % und 4,9 % daneben, §11).
- Mit dem bisherigen Treiber-Startwert und zwölf anderen Welt-Startwerten dauern die Matches bei 24 Bit durchweg länger (9 von 12 entschieden, Median 25.635; bei 16 Bit 12 von 12, Median 15.300): die
  Produktionsfolge dieses einen Startwerts ist bei 24 Bit eine andere als bei 16 (der Treiber zieht je Produktionsgebäude und Versuch; sobald ein Gebäude einen Tick früher frei wird, verschiebt sich die Folge)
  und eine langsame. Die zwölf Läufe sind deshalb keine unabhängigen Stichproben.

### 6.3 Was daraus gemacht wurde

`41c3bc0` (eigener Commit, eine Ursache, bei 16 Bit): `ReferenceMatchOptions.Standard` benutzt den Treiber-Startwert **`5EED0007`**. Dessen Match endet in beiden Auflösungen mit demselben Sieger (FactionGLA bei
Tick 14.055 bzw. 17.805). Im Manifest änderte sich dabei nur der Lauf `reference-match`; die 49 anderen wurden neu gerechnet und sind byteidentisch. Das Referenzmatch bleibt ein Lauf, dessen Ende bei der nächsten
gewollten Ergebnisänderung (Phase 23, 60 Hz) wieder kippen kann; `--driver-seed` und `--record-only` sind dafür da.

## 7. Vergleich der Ergebnisgrößen (16 Bit gegen 24 Bit)

Vergleichsstand alt: `41c3bc0` (16 Bit, neuer Startwert); neu: `d7fca91`. Erhoben mit `SimTool reference-manifest results <content> --out <json> --commands <dir> --set full` auf beiden Ständen; die vollständigen
Tabellen stehen in Anhang A. Mengen sind in Einheiten, nicht roh.

### 7.1 Zusammenfassung

| Läufe | Ergebnis |
|---|---|
| Stresstest (216.000 Ticks, synthetisch) | Befehlsstrom identisch (914.394 Befehle), jede Ereigniszahl identisch (238.273 Tode), Endzustand leer wie zuvor |
| 27 Missionsläufe (3.000 Ticks) | Ausgang, Objektzahl, ausgeführte Skripte und Aktionen in **allen 27 identisch**; Schüsse in 26 identisch, `mission_MD_GLA05` 568 → 564 (Tode 162 → 158) |
| Skirmish-Übersicht (64 Karten, 3.000 Ticks) | 64 von 64 mit demselben Ausgang (alle am Tickbudget) |
| drei 8-Spieler-Läufe (9.000 Ticks) und der kurze | alle am Tickbudget wie zuvor; Objektzahlen 173 → 165, 194 → 226, 179 → 170, 178 → 177 |
| 16 KI-Läufe (bis 45.000 Ticks) | 12 entschieden wie zuvor; **11 mit demselben Ausgang, 5 mit anderem** (§7.2) |
| Referenzmatch | derselbe Sieger, Tick 14.055 → 17.805 (§6) |
| Kampagne: Zielfahrer (24 Karten, 60.000 Ticks) | Sieg 24/24 und Niederlage 24/24 wie zuvor; **auf allen 24 Karten dasselbe Endskript im selben Tick**, dieselbe Zahl von Eingriffen (Anhang C) |
| Kampagne: Audit (24 Karten, 900 Ticks) | 24/24 deterministisch, 24/24 nach Speichern/Laden identisch, Endproben Sieg 13/24, Niederlage 15/24, 0 Treffer auf fehlende Typen – Karte für Karte wie zuvor |
| `CampaignTool reachability` | Sieg sauber 24/24, Niederlage sauber 24/24; die Ausgabe ist byteidentisch zu der bei 16 Bit (statische Analyse, hängt nicht an der Auflösung) |

### 7.2 Die 16 KI-Läufe: jede Abweichung und ihre Ursache

In **jedem** der 16 Läufe (und der vier 8-Spieler-Läufe) ist der Befehlsstrom der KI über den ganzen Aufbau identisch – 149 bis 467 Befehle (Bauplätze mit Position, Produktion, Versorgung, Upgrades), 2.890 bis
5.670 Ticks. Die Umrechnung trägt also Wirtschaft, Bau und Produktion unverändert. Der erste abweichende Befehl ist in jedem Lauf ein Befehl des Gefechts; danach ist es ein anderes Gefecht. Erster abweichender
Befehl je Lauf (`results --commands`, Positionen in ganzen Einheiten, eine Einheit Differenz zählt nicht):

| Lauf | Ausgang 16 Bit | Ausgang 24 Bit | erster abweichender Befehl (Nr., Tick) | worin (16 Bit → 24 Bit) |
|---|---|---|---|---|
| AlpineAssault America–China | America @16.965 | America @16.665 | 250, Tick 3.613 | bei 24 Bit kommt ein Angriffsmarsch (Tick 3.613) vor der Spezialkraft der Gegenseite (Tick 3.614) |
| AlpineAssault China–GLA | China @23.370 | China @10.710 | 149, Tick 2.938 | Angriffsmarsch derselben Einheit, Ziel (1308, 1697) → (1311, 1698) |
| AlpineAssault ChinaTank–AirForce | **Patt** | **AirForce @14.610** | 154, Tick 3.553 | Angriffsmarsch derselben Einheit, Ziel (1314, 1775) → (1334, 1770) |
| AlpineAssault GLA–America | America @8.895 | America @7.815 | 317, Tick 2.969 | Angriffsmarsch derselben Einheit, Ziel (1129, 884) → (1122, 886) |
| ArmoredFury America–China | **America @19.095** | **Patt** | 216, Tick 2.893 | Angriffsmarsch, Einheit 65 → 84 |
| ArmoredFury China–GLA | **Patt** | **China @42.840** | 226, Tick 2.999 | Angriffsmarsch derselben Einheit, Ziel (1897, 2786) → (1888, 2784) |
| ArmoredFury ChinaTank–AirForce | Patt | Patt | 220, Tick 3.299 | Angriffsmarsch, Einheit 124 → 125 |
| ArmoredFury GLA–America | America @29.865 | America @28.800 | 386, Tick 3.794 | derselbe Einstiegsbefehl, Tick 3.809 → 3.794 |
| BitterWinter America–China | **America @33.750** | **Patt** | 311, Tick 4.258 | Angriffsmarsch, Einheit 129 → 130 |
| BitterWinter China–GLA | China @17.220 | China @7.950 | 293, Tick 3.644 | der Angriffsmarsch von Tick 3.644 entfällt bei 24 Bit |
| BitterWinter ChinaTank–AirForce | Patt | Patt | 357, Tick 4.679 | Angriffsmarsch, Einheit 128 → 127 |
| BitterWinter GLA–America | America @8.895 | America @9.075 | 426, Tick 3.583 | Angriffsmarsch derselben Einheit, Ziel (998, 1533) → (1010, 1544) |
| DeathValley America–China | America @9.330 | America @9.390 | 326, Tick 4.078 | bei 24 Bit kommt ein Angriffsmarsch (Tick 4.078) vor einer Spezialfähigkeit (Tick 4.079) |
| DeathValley China–GLA | China @8.565 | China @8.580 | 382, Tick 3.659 | Angriffsmarsch, Einheit 166 → 165 |
| DeathValley ChinaTank–AirForce | **ChinaTank @31.770** | **AirForce @10.635** | 466, Tick 5.594 | Angriffsmarsch, Tick 5.669 → 5.594, Einheit 432 → 388 |
| DeathValley GLA–America | America @8.175 | America @8.475 | 467, Tick 3.343 | Angriffsmarsch, Einheit 44 → 41 |

Die Abweichungen, die über „Endtick in der Nähe“ hinausgehen – fünf andere Ausgänge (zwei Patts werden Siege, zwei Siege werden Patts, **ein Sieger wechselt**: DeathValley ChinaTank–AirForce) und zwei Siege zum
halben Tick (China–GLA auf Alpine Assault und Bitter Winter) – haben alle dieselbe benannte Ursache: **chaotische Verstärkung einer Rundung.** Beleg ist die Tabelle: bis zum genannten Tick sind die Befehle gleich,
der erste Unterschied ist ein Angriffsbefehl, dessen Ziel (die Position eines gegnerischen Objekts zu diesem Tick) um wenige Einheiten anders liegt oder dessen Einheit eine andere ist (die Einheiten verlassen die
Fabriken in anderer Reihenfolge oder einen Tick versetzt). Solche Unterschiede entstehen, weil Beträge je Tick bei 24 Bit exakter sind (im synthetischen Fall gemessen: 0,6 Einheiten Positionsunterschied nach
2.100 Ticks). Die Richtung ist nicht einheitlich: von den neun Läufen, die in beiden Auflösungen mit demselben Sieger enden, enden fünf früher und vier später, die Ereignisraten je 1.000 Ticks (Schüsse, Treffer, Tode, Produktionen) schwanken in beide Richtungen. Die Zahl der
entschiedenen Läufe bleibt 12 von 16. Ein Fehler der Umstellung hätte sich im Aufbau gezeigt, in den Missionen oder im Stresstest; dort ist alles gleich. **Nicht geprüft** ist, ob die neuen Ausgänge „besser“ sind
(Balance); die KI-Tabelle in `docs/SIMULATION-CORE.md` §19/§36 ist damit überholt.

## 8. Determinismus und Leistung

### 8.1 Matrix dieses Rechners mit dem neuen Manifest

Jede Zeile: `input-manifest check` und `reference-manifest check --set full` gegen die eingecheckten Dateien von `d7fca91`.

| Zeile | Einstellung | Eingänge | Rechnung | Ergebnis |
|---|---|---|---|---|
| Release, Lauf 1 | – | identisch | 50/50 (full) | **identisch** |
| Release, Lauf 2 | derselbe Aufruf noch einmal | identisch | 50/50 (full) | **identisch** |
| Debug-Build | `dotnet build -c Debug tools/CnCNew.SimTool` | identisch | 9/9 (**quick**) | **identisch** |
| Tiered Compilation aus | `DOTNET_TieredCompilation=0` | identisch | 50/50 (full) | **identisch** |
| Tiered PGO aus | `DOTNET_TieredPGO=0` | identisch | 50/50 (full) | **identisch** |
| ReadyToRun aus | `DOTNET_ReadyToRun=0` | identisch | 50/50 (full) | **identisch** |
| Hardware-Intrinsics aus | `DOTNET_EnableHWIntrinsic=0` | identisch | 50/50 (full) | **identisch** |
| AVX2 aus | `DOTNET_EnableAVX2=0` | identisch | 50/50 (full) | **identisch** |
| Server-GC | `DOTNET_gcServer=1` | identisch | 50/50 (full) | **identisch** |
| Kultur `de-DE` | `CNCNEW_TEST_CULTURE=de-DE` | identisch | 50/50 (full) | **identisch** |
| Kultur `tr-TR` | `CNCNEW_TEST_CULTURE=tr-TR` | identisch | 50/50 (full) | **identisch** |
| Kultur `ar-SA` | `CNCNEW_TEST_CULTURE=ar-SA` | identisch | 50/50 (full) | **identisch** |
| Linux auf demselben Rechner | WSL2 (Ubuntu, Kernel 6.6.87), `dotnet publish -r linux-x64 --self-contained`, Inhalt über `/mnt/c` | identisch | 50/50 (full) | **identisch** |

Dreizehn Zeilen, keine Abweichung in einem Feld. Das neue Manifest trägt kein falsches Wiederholungs-Flag (jeder Lauf: Lauf 2 identisch, Fortsetzung aus dem Snapshot identisch; jede Mission zusätzlich nach
Speichern/Laden identisch zum ununterbrochenen Lauf). Laufzeiten des vollen Satzes: 439 s allein mit sechs Arbeitern, 585 bis 1.586 s mit vier Arbeitern neben anderen eigenen Läufen, 2.251 s unter WSL.

Offen wie in `docs/DETERMINISM.md`: ein zweiter physischer Rechner mit anderer CPU, eine andere .NET-Version.

### 8.2 Leistung, Rücken an Rücken

Absolute Zahlen anderer Tage sind auf diesem Rechner nicht vergleichbar; verglichen wird deshalb im selben Zeitfenster, abwechselnd, je zweimal, ohne eigene Läufe daneben (ein zweiter Agent arbeitete auf demselben
Rechner – die Messung ist ruhig, soweit ich es steuern konnte, nicht darüber hinaus). `SimTool perf … --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 12000` und
`perf-mission … --map "Maps/MD_GLA05/MD_GLA05.map" --ticks 3000`; Mittel je Tick in Mikrosekunden, mit Beobachter.

**Vorarbeit gegen den Stand der Phase 20** (beide 16 Bit, dieselben Endhashes `ADC3E2661AF15B6D` und `826CCD82C8536DC2`, ruhiges Fenster, je zwei Läufe):

| Lauf | Phase 20 (`4c45764`) | Vorarbeit (`a0e7b23`) |
|---|---|---|
| 8 Spieler, Twilight Flame, 12.000 Ticks: ganzer Tick | 1.670 / 1.649 | 1.666 / 1.647 |
| – Phase „Vision“ (Sichtkreise) | 147 / 140 | 146 / 135 |
| `MD_GLA05`, 3.000 Ticks | 4.583 / 4.569 | 4.547 / 4.601 |

Mit den 128-Bit-Quadraten aus `756e978` hatte die Sichtphase 187 / 171 gegen 132 / 135 gebraucht (ganzer Tick 1.742 / 1.642 gegen 1.563 / 1.587); deshalb der Test in Maschinenworten (`a0e7b23`).

**Umstellung gegen den Stand der Phase 20** (16 Bit gegen 24 Bit):

| Lauf | gleiche Arbeit? | Phase 20, 16 Bit | Umstellung, 24 Bit |
|---|---|---|---|
| Stresstest, 216.000 Ticks, µs je Tick im ersten Lauf (`SimTool stress`) | **ja** (Befehlsstrom und jede Ereigniszahl identisch) | 57,6 / 57,7 / 58,3 | **57,1 / 56,7 / 56,7** |
| Stresstest gekürzt, 40.000 Ticks, vier Läufe | ja | 44,0 – 51,4 (Median 44,9) | 44,0 – 44,8 (Median 44,1) |
| `MD_USA05`, 3.000 Ticks, sechs Läufe | ja (alle Zähler identisch) | 2.582 – 3.899, Mittel 3.233 | 2.820 – 3.894, Mittel 3.330 |
| `MD_GLA05`, 3.000 Ticks, fünf Läufe | fast (564 statt 568 Schüsse) | 4.537 – 5.865, Mittel 4.957 | 4.261 – 6.068, Mittel 5.106 |
| 8 Spieler, Twilight Flame, 12.000 Ticks, fünf Läufe | nein (ab Tick 2.773 ein anderes Gefecht) | 1.597 – 1.944, Mittel 1.765 | 1.612 – 1.940, Mittel 1.772 |

Der sauberste Vergleich ist der Stresstest: dieselbe Arbeit, drei abwechselnde Läufe in einem ruhigen Fenster, 24 Bit ein bis zwei Prozent **schneller**. In den Missionen und im Gefecht streuen die Läufe
desselben Programms um ± 15 % (ein zweiter Agent rechnete zeitweise mit), die Mittel liegen bei + 0,4 % (Gefecht) und + 3 % (Missionen) – kleiner als die Streuung. **Belegt ist: kein messbarer Anstieg der
Rechenkosten; nicht belegt ist eine Aussage auf wenige Prozent genau für Missionen.** Die Sichtphase kostet bei 24 Bit, was sie bei 16 Bit kostete (138 – 176 µs gegen 137 – 176 µs im selben Fenster).
Voraussetzung dafür waren die für die Bitzahl hergeleiteten Routinen (§2.1): mit den Routinen der Phase 19 hätte bei 24 Bit jedes Abstandsquadrat über 181 Einheiten die 128-Bit-Softwarepfade genommen.

**Snapshotgröße:** über alle Snapshots des Manifests, die alt und neu im selben Tick liegen (Missionen, 8-Spieler-Läufe, Stresstest), 118.737.497 → 119.710.407 Byte, **+0,82 %**; Stresstest bei Tick 108.000
11.862.560 → 12.095.586 Byte (+2,0 %), `mission_MD_GLA05` 9.038.371 → 9.071.782 Byte (+0,4 %). Die Zahlen werden länger, der Anteil der Festkommawerte an einem Snapshot ist klein.

## 9. Golden-Hashes und Tests außerhalb der Simulation

- **Geändert, als Folge der einen Ursache** (im Umstellungs-Commit): der Golden-Hash der Knochentabelle der Retail-Flugfelder (`DeterminismTests.BoneTableGolden`, `93D5819B…F1DA47` → `EA1915EB…DAC858`; die
  Tabelle wird als Rohwerte gehasht, die Posen sind dieselben Zahlen). Die Schranke des Fahrwerk-Tests (`ObjectTests.ChassisFollowsMotion`): das Wanken überschwingt seine Grenze um einen Schritt, drei Schritte von
  4 · (1/30) sind exakt 0,4 = Grenze + 0,1; seit die Beschleunigung je Tick auf sieben Stellen 1/30 ist, trifft die Spitze diesen Wert auf das letzte Bit eines `float` (0,4000001), die Schranke lässt das jetzt zu.
- **Unverändert:** alle übrigen Tests der Präsentation und der Spielschicht (UI-Golden-Hashes, Terrain, FX, Eingabe, HUD, Shell, Kampagne), die drei Szenen-Audits (`scripts/audit-scenarios.ps1`: `capture-building`,
  `tank-duel`, `jet-strafe` – alle Prüfungen bestanden).
- Kein Mensch hat eine Darstellung dieses Stands angesehen. Positionen sind feiner; dass nichts sichtbar anders ist, ist nicht belegt.

## 10. Prüfung am Ende

Alles am Stand `d7fca91` (die Dokumentation kam danach und ändert keinen Code):

| Prüfung | Ergebnis |
|---|---|
| `SimTool input-manifest check`, `reference-manifest check --set full` | identisch, zweimal hintereinander, dazu die Matrix (§8.1) |
| Simulationstests (Debug) ohne und mit `--content-dir` | bestanden (407 Tests) |
| Simulationstests `-c Release -- --long` (216.000 Ticks) | bestanden |
| `SimTool stress` | Endhash `56882F763EABEF67`, Kette `754FA4DCB9A41DF7`, Lauf 2/3 und Fortsetzung identisch |
| `CnCNew.Game.Tests` (Release, `--content-dir`) | bestanden (der erste volle Lauf scheiterte am Golden-Hash der Knochentabelle, §9; nach der Korrektur im Umstellungs-Commit voller Lauf bestanden) |
| `CnCNew.Presentation.Tests` | bestanden (nach der Schranke aus §9) |
| Archives, Content, Data (ohne und mit Inhalt), IO, Images, Formats, Import, Ui, W3D (mit Inhalt), Audio (mit Inhalt) | alle bestanden |
| `IniTool compat-check … baselines/compatibility.json` | 751 Einträge, stimmt (die Definitionsschicht kennt `Fixed` nicht; der Definitions-Snapshot ist von der Umstellung nicht berührt) |
| `SimTool audit-modules` / `audit-coverage` | 183 von 205 Modulen, 16.558 von 19.553 Verwendungen; 2.115 von 2.115 Objekten erzeugt |
| `scripts/audit-scenarios.ps1` | alle drei Szenen bestanden |
| `CampaignTool audit --ticks 900`, `drive --ticks 60000 --explain` je Karte (24), `reachability` | §7.1; keine Ausnahme, kein Zeitlimit |
| Sauberer Build `dotnet build CnCNew.slnx` (`bin`/`obj` von `CnCNew.Client.Stride` gelöscht), `dotnet build tools/CnCNew.UiRenderTool` | je 0 Warnungen, 0 Fehler |
| `scripts/audit-vulnerable.ps1` | keine bekannten Anfälligkeiten |

## 11. Probelauf bei 60 und 120 Hz

`SimTool rate-audit <content> --tick-rate N` am Stand der Vorarbeit (16 Bit, bestätigt die Zahlen der Phase 20) und am Stand der Umstellung (24 Bit):

| Zähler | 60 Hz, 16 Bit | 60 Hz, 24 Bit | 120 Hz, 16 Bit | 120 Hz, 24 Bit |
|---|---|---|---|---|
| Umrechnungen beim Katalogbau, die auf null runden | 0 | **0** | 1 (`NapalmBombLocomotor`, 0,1/s²) | **0** |
| Umrechnungen mit über 1 % / über 10 % Fehler | 5 / 0 | **0 / 0** | 39 / 1 | **0 / 0** |
| schlechtester Fehler einer Umrechnung | 9,863 % | **0,007 %** | 100 % | **0,422 %** (dieselbe 0,1/s²: 117 Rohschritte) |
| Katalogwerte: bei 30 Hz ≠ 0, bei der Rate 0 | 0 | 0 | 0 | 0 |
| Katalogwerte mit über 1 % bis 10 % Fehler gegen den skalierten 30-Hz-Wert | 1 | **0** | 41 in 5 Feldern | **0** |
| Katalogwerte mit über 10 % | 0 | 0 | 0 | 0 |
| Katalogwerte unter acht signifikanten Bits | 46 in 5 Feldern | **0** | 321 in 7 Feldern | **0** |

Bei 30 Hz selbst: 16 Bit hatte zwei Umrechnungen über 1 % (4,912 % bei `DegreesPerSecondToRadiansPerTick`, 3,870 % bei `PerSecondSquaredToPerTickSquared`), 24 Bit hat keine (schlechtester Fall 0,007 %). Das Ziel
der Phase – null Fälle bei 60 Hz – ist erreicht; bei 120 Hz ebenfalls.

Laufzeitzähler (`--time-probe`, erster Lauf jedes Befehls, 24 Bit):

Zwölf Läufe (Referenzmatch, zwei KI-Läufe auf Alpine Assault, `MD_USA05`, `MD_GLA05`, `GC_ChemGeneral`, je bei 60 und 120 Hz); die Tabelle steht in `docs/TICKRATE-MIGRATION.md` §11.5.

| Zähler | Summe der Aufrufe über die zwölf Läufe | Treffer |
|---|---:|---:|
| `RunTimeRoundedToZero` (Betrag je Tick wird null) | 579.635 | **0** |
| `MinimumOneFrame` (Untergrenze „ein Bild“ greift) | 21.570 | 48 (dieselben Stellen wie in Phase 20: Dauer 0 wird ein Bild) |
| `OffGrid` (Dauer abseits des Rasters) | 454.979 | **0** |

Jeder Lauf ist deterministisch (zweiter frischer Lauf und Fortsetzung gleich; beim Referenzmatch nur aufgenommen), keiner wirft. Sollwert und Ergebnis: **bei 60 Hz null Fälle von Rundung auf null** – beim
Katalogbau und zur Laufzeit; es bleibt kein Fall zu benennen. Nicht gezählt bleibt, was Module ohne den Zeitdienst je Tick bilden.

## 12. Was nicht belegt ist

- Die Überlaufwege aus §5 sind behoben (§5a). Offen bleibt, was dort unter „nur berichtet“ steht: Abstände zwischen Positionen, die weiter als rund 524.000 Einheiten auseinanderliegen
  (ein Befehl mit beliebigen Zielkoordinaten wird nicht geprüft), und zwei Stellvertreterwerte im Code.
- Ein zweiter physischer Rechner, eine andere CPU, eine andere .NET-Version (wie in `docs/DETERMINISM.md`).
- Ob die neuen KI-Ausgänge und das längere Referenzmatch die Balance verschieben; die Erhebung über 36 Startwerte schließt eine Verschiebung der Dauer um rund zehn Prozent nicht aus.
- Jede Darstellung: kein Mensch hat diesen Stand gesehen oder gespielt.
- Die Leistung ist im selben Zeitfenster verglichen, nicht auf einer nachweislich ruhigen Maschine.

## 13. Fallstricke

- **Die Simulation ist nicht `checked`.** Nur `Fixed` wirft. Wer auf `.Raw` rechnet, schreibt `checked(…)` oder rechnet in 128 Bit; ein Quadrat zweier Rohwerte passt bei 24 Bit schon für 180 Einheiten nicht mehr
  in ein `long`.
- **Eine Reichweite, ein Radius oder eine Grenze wird nie mit `*` quadriert**, sondern über `SquaredReach` verglichen (§5a); die Quelltextregel `Tests.SquaredReachSourceRules` hält das fest. Das Quadrat eines
  Werts über 741.455 passt nicht. `SimTool rate-audit <content> --ranges` listet den größten Wert jedes Felds; Sentinels im Code („überall“ = 100.000) haben quadriert nur den Faktor 55 Abstand.
- **`Fixed.ToDecimal()` ist über 65.536 Einheiten nicht mehr exakt** (24 Binärstellen brauchen 24 Dezimalstellen, ein `decimal` hat 28 bis 29 gültige); die Rundung liegt weit unter einem halben Rohschritt,
  `FromDecimal(ToDecimal(x))` gibt weiter `x`.
- **Szenarien mit gleich starken Seiten sind kein Maßstab für „nur Rundung“.** Ihr Ende ist eine Kippentscheidung; der Vergleich braucht eine Erhebung über Startwerte oder Größen, die vor dem Kippen liegen
  (Befehlsstrom des Aufbaus, Ereignisraten).
- Vergleich zweier Auflösungen oder Raten: `reference-manifest results --commands <dir>` auf beiden Ständen, dann die erste abweichende Zeile je Lauf – nicht die Hashes.
- Ein Probe-Build mit anderer Bitzahl: die Konstante ändern, in ein eigenes Verzeichnis bauen (`dotnet build -o`), zurückstellen. Alte Replays und Snapshots liest ein solcher Build nicht (Versionen).

## Anhang A: Ergebnisgrößen aller Läufe des Manifests, 16 Bit (`41c3bc0`) gegen 24 Bit (`d7fca91`)

`SimTool reference-manifest results`. „Ausgang“: Sieger@Siegtick, `stalemate` = kein Sieger im Tickbudget, `none` = Mission ohne Endskript in 3.000 Ticks bzw. Stresstest. Spieler in der Reihenfolge ihrer Ids
(ohne den neutralen Spieler); Geld in ganzen Einheiten. „Schüsse (Skriptfeuer)“ ist der Zähler der Missionssitzung, die Ereigniszahl `WeaponFired` steht in der ersten Tabelle.

| Lauf | Ausgang alt | Ausgang neu | Endtick alt | neu | Objekte alt | neu | Schüsse/Produktionen/Bauten/Tode alt | neu |
|---|---|---|---:|---:|---:|---:|---|---|
| AlpineAssault_America-China | America@16965 | America@16665 | 17266 | 16966 | 75 | 85 | 3197/135/16/92 | 5711/139/15/85 |
| AlpineAssault_China-GLA | China@23370 | China@10710 | 23671 | 11011 | 65 | 56 | 5869/204/15/180 | 3668/77/13/60 |
| AlpineAssault_ChinaTankGeneral-AmericaAirForceGeneral | stalemate | AmericaAirForceGeneral@14610 | 45000 | 14911 | 38 | 93 | 7392/271/21/267 | 5245/143/17/97 |
| AlpineAssault_GLA-America | America@8895 | America@7815 | 9196 | 8116 | 49 | 39 | 4715/68/10/49 | 2218/64/9/53 |
| ArmoredFury_America-China | America@19095 | stalemate | 19396 | 45000 | 101 | 87 | 6014/214/19/181 | 10742/535/21/512 |
| ArmoredFury_China-GLA | stalemate | China@42840 | 45000 | 43141 | 71 | 90 | 9754/369/20/390 | 10685/314/19/320 |
| ArmoredFury_ChinaTankGeneral-AmericaAirForceGeneral | stalemate | stalemate | 45000 | 45000 | 113 | 74 | 9971/409/27/411 | 7602/267/25/540 |
| ArmoredFury_GLA-America | America@29865 | America@28800 | 30166 | 29101 | 131 | 116 | 13083/388/22/366 | 15100/348/20/324 |
| BitterWinter_America-China | America@33750 | stalemate | 34051 | 45000 | 98 | 44 | 10154/354/26/311 | 9236/410/24/416 |
| BitterWinter_China-GLA | China@17220 | China@7950 | 17521 | 8251 | 60 | 56 | 2221/99/14/86 | 1454/66/10/49 |
| BitterWinter_ChinaTankGeneral-AmericaAirForceGeneral | stalemate | stalemate | 45000 | 45000 | 48 | 84 | 4650/222/18/228 | 7392/280/28/261 |
| BitterWinter_GLA-America | America@8895 | America@9075 | 9196 | 9376 | 71 | 71 | 3086/81/11/49 | 3336/81/11/50 |
| DeathValley_America-China | America@9330 | America@9390 | 9631 | 9691 | 61 | 62 | 2616/52/12/38 | 2767/53/12/38 |
| DeathValley_China-GLA | China@8565 | China@8580 | 8866 | 8881 | 61 | 63 | 1607/69/9/54 | 1746/69/9/53 |
| DeathValley_ChinaTankGeneral-AmericaAirForceGeneral | ChinaTankGeneral@31770 | AmericaAirForceGeneral@10635 | 32071 | 10936 | 42 | 54 | 9446/157/17/209 | 2990/52/12/52 |
| DeathValley_GLA-America | America@8175 | America@8475 | 8476 | 8776 | 60 | 60 | 3005/69/9/53 | 3104/69/10/53 |
| ai8_FortressAvalanche | stalemate | stalemate | 9000 | 9000 | 173 | 165 | 10822/204/45/393 | 9945/201/44/334 |
| ai8_IronDragon | stalemate | stalemate | 9000 | 9000 | 194 | 226 | 9035/238/57/455 | 8945/250/52/564 |
| ai8_TwilightFlame | stalemate | stalemate | 9000 | 9000 | 179 | 170 | 7943/213/44/189 | 9202/208/43/259 |
| ai8_TwilightFlame_short | stalemate | stalemate | 3000 | 3000 | 178 | 177 | 15/88/32/0 | 11/88/32/0 |
| mission_GC_AirGeneral_AmericaLaserGeneral | none | none | 3000 | 3000 | 1049 | 1049 | 0/1/0/0 | 0/1/0/0 |
| mission_GC_AirGeneral_ChinaInfantryGeneral | none | none | 3000 | 3000 | 1049 | 1049 | 0/1/0/0 | 0/1/0/0 |
| mission_GC_AirGeneral_GLAStealthGeneral | none | none | 3000 | 3000 | 1049 | 1049 | 0/1/0/0 | 0/1/0/0 |
| mission_GC_ChemGeneral_AmericaAirForceGeneral | none | none | 3000 | 3000 | 787 | 787 | 0/0/0/3 | 0/0/0/3 |
| mission_GC_ChemGeneral_ChinaTankGeneral | none | none | 3000 | 3000 | 787 | 787 | 0/0/0/3 | 0/0/0/3 |
| mission_GC_ChinaBoss_AmericaAirForceGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_AmericaLaserGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_AmericaSuperWeaponGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_ChinaInfantryGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_ChinaNukeGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_ChinaTankGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_GLADemolitionGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_GLAStealthGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_GLAToxinGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_LaserGeneral_ChinaNukeGeneral | none | none | 3000 | 3000 | 2109 | 2109 | 0/23/0/0 | 0/23/0/0 |
| mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral | none | none | 3000 | 3000 | 689 | 689 | 0/0/0/0 | 0/0/0/0 |
| mission_GC_TankGeneral_AmericaSuperWeaponGeneral | none | none | 3000 | 3000 | 1624 | 1624 | 0/0/0/0 | 0/0/0/0 |
| mission_GC_TankGeneral_GLAToxinGeneral | none | none | 3000 | 3000 | 1624 | 1624 | 0/0/0/0 | 0/0/0/0 |
| mission_MD_CHI01 | none | none | 3000 | 3000 | 1133 | 1133 | 0/0/0/15 | 0/0/0/15 |
| mission_MD_CHI05 | none | none | 3000 | 3000 | 2109 | 2109 | 2926/0/0/12 | 2926/0/0/12 |
| mission_MD_GLA01 | none | none | 3000 | 3000 | 1093 | 1093 | 1231/0/0/40 | 1231/0/0/40 |
| mission_MD_GLA03 | none | none | 3000 | 3000 | 1312 | 1312 | 0/0/0/4 | 0/0/0/4 |
| mission_MD_GLA05 | none | none | 3000 | 3000 | 2249 | 2249 | 568/0/0/162 | 564/0/0/158 |
| mission_MD_USA01 | none | none | 3000 | 3000 | 1986 | 1986 | 127/0/0/70 | 127/0/0/70 |
| mission_MD_USA02 | none | none | 3000 | 3000 | 1780 | 1780 | 39/0/0/35 | 39/0/0/35 |
| mission_MD_USA05 | none | none | 3000 | 3000 | 1869 | 1869 | 0/4/0/24 | 0/4/0/24 |
| mission_Training01 | none | none | 3000 | 3000 | 1901 | 1901 | 181/0/0/14 | 181/0/0/14 |
| reference-match | GLA@14055 | GLA@17805 | 14356 | 18106 | 207 | 158 | 15854/872/0/2109 | 19314/1143/0/2099 |
| skirmish-sweep | 64 maps | same outcome 64, other 0 |  |  |  |  |  |  |
| stress | none | none | 216000 | 216000 | 0 | 0 | 0/0/0/238273 | 0/0/0/238273 |

| Lauf | Objekte je Spieler alt | neu | Geld je Spieler alt | neu |
|---|---|---|---|---|
| AlpineAssault_America-China | 69/0 | 79/0 | 625/200 | 1350/200 |
| AlpineAssault_China-GLA | 53/6 | 45/5 | 600/375 | 1500/300 |
| AlpineAssault_ChinaTankGeneral-AmericaAirForceGeneral | 10/23 | 0/87 | 150/25 | 1300/1925 |
| AlpineAssault_GLA-America | 3/40 | 3/30 | 0/1250 | 0/1975 |
| ArmoredFury_America-China | 62/0 | 46/12 | 775/100 | 75/100 |
| ArmoredFury_China-GLA | 10/25 | 47/8 | 700/37 | 950/19 |
| ArmoredFury_ChinaTankGeneral-AmericaAirForceGeneral | 21/62 | 28/14 | 225/100 | 0/150 |
| ArmoredFury_GLA-America | 9/88 | 8/73 | 825/750 | 75/600 |
| BitterWinter_America-China | 94/0 | 26/14 | 750/0 | 100/200 |
| BitterWinter_China-GLA | 41/3 | 37/3 | 300/50 | 1700/50 |
| BitterWinter_ChinaTankGeneral-AmericaAirForceGeneral | 35/5 | 24/52 | 150/350 | 200/350 |
| BitterWinter_GLA-America | 3/52 | 3/52 | 0/1350 | 0/1950 |
| DeathValley_America-China | 39/0 | 40/0 | 650/500 | 650/500 |
| DeathValley_China-GLA | 36/3 | 38/3 | 1000/50 | 1000/50 |
| DeathValley_ChinaTankGeneral-AmericaAirForceGeneral | 18/2 | 0/32 | 400/11400 | 1300/1250 |
| DeathValley_GLA-America | 3/35 | 3/35 | 0/775 | 0/1975 |
| ai8_FortressAvalanche | 26/26/20/27/9/3/34/13 | 23/32/19/20/9/3/31/13 | 625/750/150/200/0/1400/50/50 | 100/1200/150/500/1050/1400/50/50 |
| ai8_IronDragon | 28/21/31/24/30/4/36/0 | 35/25/41/26/40/0/39/0 | 600/200/0/50/725/0/0/600 | 1275/0/175/75/0/200/0/900 |
| ai8_TwilightFlame | 19/17/47/30/8/3/20/23 | 20/23/41/23/8/3/21/19 | 575/675/75/200/150/3800/1200/1350 | 575/1075/75/2150/150/3500/1200/300 |
| ai8_TwilightFlame_short | 22/22/23/21/35/15/14/14 | 22/22/22/21/35/15/14/14 | 750/1200/50/175/150/200/50/0 | 750/1200/50/175/150/200/50/0 |
| mission_GC_AirGeneral_AmericaLaserGeneral | 36/2/306 | 36/2/306 | 22000/13000/0 | 22000/13000/0 |
| mission_GC_AirGeneral_ChinaInfantryGeneral | 36/2/306 | 36/2/306 | 22000/13000/0 | 22000/13000/0 |
| mission_GC_AirGeneral_GLAStealthGeneral | 36/2/306 | 36/2/306 | 22000/13000/0 | 22000/13000/0 |
| mission_GC_ChemGeneral_AmericaAirForceGeneral | 425/134/2 | 425/134/2 | 6400/499180/13000 | 6400/499180/13000 |
| mission_GC_ChemGeneral_ChinaTankGeneral | 425/134/2 | 425/134/2 | 6400/499180/13000 | 6400/499180/13000 |
| mission_GC_ChinaBoss_AmericaAirForceGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_AmericaLaserGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_AmericaSuperWeaponGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_ChinaInfantryGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_ChinaNukeGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_ChinaTankGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_GLADemolitionGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_GLAStealthGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_GLAToxinGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_LaserGeneral_ChinaNukeGeneral | 1455/96/11/11/11/11/11/11/11/11/23/11/2 | 1455/96/11/11/11/11/11/11/11/11/23/11/2 | 6400/12200/20000/20000/20000/20000/20000/20000/20000/20000/20000/20000/13000 | 6400/12200/20000/20000/20000/20000/20000/20000/20000/20000/20000/20000/13000 |
| mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral | 243/138/2/32 | 243/138/2/32 | 0/116100/13000/3200 | 0/116100/13000/3200 |
| mission_GC_TankGeneral_AmericaSuperWeaponGeneral | 447/2/94 | 447/2/94 | 0/13000/34200 | 0/13000/34200 |
| mission_GC_TankGeneral_GLAToxinGeneral | 447/2/94 | 447/2/94 | 0/13000/34200 | 0/13000/34200 |
| mission_MD_CHI01 | 736/65/13/12/0/0/0/0/18 | 736/65/13/12/0/0/0/0/18 | 3200/101900/18000/0/0/0/0/0/0 | 3200/101900/18000/0/0/0/0/0/0 |
| mission_MD_CHI05 | 1741/29/183/103/0/5/10 | 1741/29/183/103/0/5/10 | 4800/15400/49994465/49977120/0/0/0 | 4800/15400/49994465/49977120/0/0/0 |
| mission_MD_GLA01 | 42/37/464/6/15/0/0/3/11/1 | 42/37/464/6/15/0/0/3/11/1 | 0/0/0/600/0/0/0/0/0/0 | 0/0/0/600/0/0/0/0/0/0 |
| mission_MD_GLA03 | 191/23/863/6/3/0/2/1/7/19 | 191/23/863/6/3/0/2/1/7/19 | 3000/5000/0/30000/0/0/0/525/0/980 | 3000/5000/0/30000/0/0/0/525/0/980 |
| mission_MD_GLA05 | 1488/0/154/20/20/20/3/0/43/1/0/2 | 1488/0/154/20/20/20/3/0/43/1/0/2 | 0/4300/419800/3500/0/49400/0/0/0/0/74/0 | 0/4300/419800/3500/0/49400/0/0/0/0/74/0 |
| mission_MD_USA01 | 995/25/259/5/8/12/43/5/1/1/0/3/0/4 | 995/25/259/5/8/12/43/5/1/1/0/3/0/4 | 1600/0/980/0/0/2000/0/0/0/0/0/0/0/0 | 1600/0/980/0/0/2000/0/0/0/0/0/0/0/0 |
| mission_MD_USA02 | 3/1246/44/31/64/30/0/78/1/0/0/6/6/10 | 3/1246/44/31/64/30/0/78/1/0/0/6/6/10 | 0/3200/800/0/0/102630/0/0/0/0/0/1600/0/0 | 0/3200/800/0/0/102630/0/0/0/0/0/1600/0/0 |
| mission_MD_USA05 | 1092/6/88/30/156/42/0/0/0/4 | 1092/6/88/30/156/42/0/0/0/4 | 8000/18000/193670/0/225/199470/0/0/0/0 | 8000/18000/193670/0/225/199470/0/0/0/0 |
| mission_Training01 | 0/168/213/5/33/0/0 | 0/168/213/5/33/0/0 | 0/0/0/0/0/0/0 | 0/0/0/0/0/0/0 |
| reference-match | 0/2/0/0/0/0/0/0/0/186/2/2/2 | 0/0/0/4/0/0/0/0/0/135/2/2/2 | 1800/0/69300/1600/43500/45000/44400/45000/16500/509/1725/600/1800 | 2000/400/87300/0/34500/32400/34500/23100/13500/468/5475/3825/2925 |
| stress | 0/0 | 0/0 | 0/0 | 0/0 |

| Mission | Skripte alt | neu | Aktionen alt | neu | Schüsse (Skriptfeuer) alt | neu | Endskript alt | neu |
|---|---:|---:|---:|---:|---:|---:|---|---|
| mission_GC_AirGeneral_AmericaLaserGeneral | 58 | 58 | 588 | 588 | 0 | 0 | - | - |
| mission_GC_AirGeneral_ChinaInfantryGeneral | 58 | 58 | 588 | 588 | 0 | 0 | - | - |
| mission_GC_AirGeneral_GLAStealthGeneral | 58 | 58 | 588 | 588 | 0 | 0 | - | - |
| mission_GC_ChemGeneral_AmericaAirForceGeneral | 83 | 83 | 783 | 783 | 0 | 0 | - | - |
| mission_GC_ChemGeneral_ChinaTankGeneral | 83 | 83 | 783 | 783 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_AmericaAirForceGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_AmericaLaserGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_AmericaSuperWeaponGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_ChinaInfantryGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_ChinaNukeGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_ChinaTankGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_GLADemolitionGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_GLAStealthGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_GLAToxinGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_LaserGeneral_ChinaNukeGeneral | 122 | 122 | 581 | 581 | 0 | 0 | - | - |
| mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral | 97 | 97 | 593 | 593 | 0 | 0 | - | - |
| mission_GC_TankGeneral_AmericaSuperWeaponGeneral | 3035 | 3035 | 550 | 550 | 0 | 0 | - | - |
| mission_GC_TankGeneral_GLAToxinGeneral | 3035 | 3035 | 550 | 550 | 0 | 0 | - | - |
| mission_MD_CHI01 | 65 | 65 | 3357 | 3357 | 0 | 0 | - | - |
| mission_MD_CHI05 | 151 | 151 | 487 | 487 | 2926 | 2926 | - | - |
| mission_MD_GLA01 | 133 | 133 | 275 | 275 | 1231 | 1231 | - | - |
| mission_MD_GLA03 | 101 | 101 | 485 | 485 | 0 | 0 | - | - |
| mission_MD_GLA05 | 102 | 102 | 384 | 384 | 568 | 564 | - | - |
| mission_MD_USA01 | 107 | 107 | 225 | 225 | 127 | 127 | - | - |
| mission_MD_USA02 | 79 | 79 | 196 | 196 | 39 | 39 | - | - |
| mission_MD_USA05 | 109 | 109 | 396 | 396 | 0 | 0 | - | - |
| mission_Training01 | 46 | 46 | 48 | 48 | 181 | 181 | - | - |

| Karte (Sweep) | Endtick alt | neu | Ausgang alt | neu | Schüsse alt | neu | Objekte alt | neu |
|---|---:|---:|---|---|---:|---:|---:|---:|
| skirmish-sweep Maps/Alpine Assault/Alpine Assault.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 49 | 49 |
| skirmish-sweep Maps/BarrenBadlands/BarrenBadlands.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 50 | 50 |
| skirmish-sweep Maps/Bear Town Beatdown/Bear Town Beatdown.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 62 | 62 |
| skirmish-sweep Maps/Bitter Winter/Bitter Winter.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 59 | 59 |
| skirmish-sweep Maps/Bombardment Beach/Bombardment Beach.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 63 | 63 |
| skirmish-sweep Maps/Cairo Commandos/Cairo Commandos.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 47 | 47 |
| skirmish-sweep Maps/Dark Mountain/Dark Mountain.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 55 | 55 |
| skirmish-sweep Maps/Dark Night/Dark Night.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 62 | 62 |
| skirmish-sweep Maps/Death Valley/Death Valley.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 63 | 63 |
| skirmish-sweep Maps/Defcon6/Defcon6.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 85 | 85 |
| skirmish-sweep Maps/Desert Fury/Desert Fury.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep Maps/Destruction Station/Destruction Station.map | 3000 | 3000 | stalemate | stalemate | 6 | 9 | 73 | 75 |
| skirmish-sweep Maps/DogsOfWar/DogsOfWar.map | 3000 | 3000 | stalemate | stalemate | 90 | 89 | 54 | 54 |
| skirmish-sweep Maps/Dust Devil/Dust Devil.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 47 | 47 |
| skirmish-sweep Maps/Eastern Everglades/Eastern Everglades.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 61 | 61 |
| skirmish-sweep Maps/El Scorcho/El Scorcho.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 77 | 77 |
| skirmish-sweep Maps/Fallen Empire/Fallen Empire.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 50 | 50 |
| skirmish-sweep Maps/Final Crusade/Final Crusade.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep Maps/Flash Effect/Flash Effect.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 47 | 47 |
| skirmish-sweep Maps/Flash Fire/Flash Fire.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 54 | 54 |
| skirmish-sweep Maps/FloodedPlains/FloodedPlains.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep Maps/ForgottenForestZH/ForgottenForestZH.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 59 | 59 |
| skirmish-sweep Maps/Fortress Avalanche/Fortress Avalanche.map | 3000 | 3000 | stalemate | stalemate | 17 | 16 | 64 | 64 |
| skirmish-sweep Maps/Free Fire Zone/Free Fire Zone.map | 3000 | 3000 | stalemate | stalemate | 191 | 185 | 64 | 62 |
| skirmish-sweep Maps/Golden Oasis/Golden Oasis.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 54 | 54 |
| skirmish-sweep Maps/Green Pastures/Green Pastures.map | 3000 | 3000 | stalemate | stalemate | 156 | 155 | 61 | 61 |
| skirmish-sweep Maps/Heartland Shield/Heartland Shield.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 48 | 48 |
| skirmish-sweep Maps/Homeland Alliance/Homeland Alliance.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 52 | 52 |
| skirmish-sweep Maps/Hostile Dawn/Hostile Dawn.map | 3000 | 3000 | stalemate | stalemate | 159 | 159 | 55 | 55 |
| skirmish-sweep Maps/Iron Dragon/Iron Dragon.map | 3000 | 3000 | stalemate | stalemate | 50 | 49 | 66 | 66 |
| skirmish-sweep Maps/Killing Fields/Killing Fields.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 62 | 62 |
| skirmish-sweep Maps/Leipzig Lowlands/Leipzig Lowlands.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep Maps/Lights Out/Lights Out.map | 3000 | 3000 | stalemate | stalemate | 46 | 46 | 56 | 56 |
| skirmish-sweep Maps/Lone Eagle/Lone Eagle.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 60 | 60 |
| skirmish-sweep Maps/Manic Aggression/Manic Aggression.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 47 | 47 |
| skirmish-sweep Maps/Mountain Guns/Mountain Guns.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 54 | 54 |
| skirmish-sweep Maps/MountainFox/MountainFox.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 50 | 50 |
| skirmish-sweep Maps/NorthAmerica/NorthAmerica.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 44 | 44 |
| skirmish-sweep Maps/Overland/Overland.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 63 | 63 |
| skirmish-sweep Maps/Red Rock/Red Rock.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 53 | 53 |
| skirmish-sweep Maps/Rocky Rampage/Rocky Rampage.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep Maps/Rogue Agent/Rogue Agent.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 47 | 47 |
| skirmish-sweep Maps/Sand Serpent/Sand Serpent.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 50 | 50 |
| skirmish-sweep Maps/Seaside Mutiny/Seaside Mutiny.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 53 | 53 |
| skirmish-sweep Maps/Silent River/Silent River.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep Maps/TheFrontline/TheFrontline.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 56 | 56 |
| skirmish-sweep Maps/Tournament City/Tournament City.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 74 | 74 |
| skirmish-sweep Maps/Tournament Continent/Tournament Continent.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 67 | 67 |
| skirmish-sweep Maps/Tournament Desert/Tournament Desert.map | 3000 | 3000 | stalemate | stalemate | 5 | 5 | 61 | 61 |
| skirmish-sweep Maps/Tournament Island/Tournament Island.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 60 | 60 |
| skirmish-sweep Maps/Tournament Lake/Tournament Lake.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 71 | 71 |
| skirmish-sweep Maps/Tournament Plains/Tournament Plains.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep Maps/Tournament Tundra/Tournament Tundra.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 56 | 56 |
| skirmish-sweep Maps/Tournament Urban/Tournament Urban.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 47 | 47 |
| skirmish-sweep Maps/TournamentA/TournamentA.map | 3000 | 3000 | stalemate | stalemate | 156 | 158 | 84 | 84 |
| skirmish-sweep Maps/TournamentB/TournamentB.map | 3000 | 3000 | stalemate | stalemate | 325 | 343 | 82 | 80 |
| skirmish-sweep Maps/Twilight Flame/Twilight Flame.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 55 | 55 |
| skirmish-sweep Maps/Victory Valley/Victory Valley.map | 3000 | 3000 | stalemate | stalemate | 99 | 96 | 56 | 57 |
| skirmish-sweep Maps/Wasteland Warlords/Wasteland Warlords.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 45 | 45 |
| skirmish-sweep Maps/Whiteout/Whiteout.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 56 | 56 |
| skirmish-sweep Maps/Winding River/Winding River.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 52 | 52 |
| skirmish-sweep Maps/Winter Wolf/Winter Wolf.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep maps/Armored Fury/Armored Fury.map | 3000 | 3000 | stalemate | stalemate | 170 | 159 | 86 | 86 |
| skirmish-sweep maps/Scorched Earth/Scorched Earth.map | 3000 | 3000 | stalemate | stalemate | 80 | 64 | 51 | 53 |

## Anhang B: Endhash, Kette und Snapshotgröße je Lauf, 16 Bit (`41c3bc0`) gegen 24 Bit (`d7fca91`)

Aus `baselines/reference-manifest.json` der beiden Stände. Die Snapshotgröße ist nur vergleichbar, wo der Snapshot im selben Tick liegt (Missionen, 8-Spieler-Läufe, Stresstest); bei den KI-Läufen und dem
Referenzmatch liegt er in der Mitte eines anders langen Laufs.

| Lauf | Satz | Endhash 16 Bit | Endhash 24 Bit | Kette 16 Bit | Kette 24 Bit | Snapshot-Byte 16 Bit | 24 Bit |
|---|---|---|---|---|---|---:|---:|
| `AlpineAssault_America-China` | full | `BA10AC1EB0EB8AC9` | `8FA1F3E86560055A` | `F669459C2FA6E39C` | `C07E2CACB84DFDA0` | 380668 | 386926 |
| `AlpineAssault_China-GLA` | full | `0BD4FE5EEB2C37E7` | `BAABE02976F3F436` | `BF9939E5A3B45736` | `37763867679EF7A9` | 380984 | 380496 |
| `AlpineAssault_ChinaTankGeneral-AmericaAirForceGeneral` | full | `D99B60905846A6B1` | `472B3E1147C53711` | `80EDA8449C86CEE6` | `E1E50480088C008B` | 410180 | 383971 |
| `AlpineAssault_GLA-America` | quick | `23A93C58C775A691` | `73CB68FC2A23A303` | `B29CF881F6B1CC59` | `E9D96D818C6F18DC` | 390287 | 391625 |
| `ArmoredFury_America-China` | full | `0E0E07558397DB62` | `11A1BC4E365AE06F` | `E41C4EF3D1DFBEF4` | `BFF01CBE3E0889B2` | 704547 | 716172 |
| `ArmoredFury_China-GLA` | full | `7196F712342A3BDA` | `360DBEB6A4A5A5F3` | `FF370FDA722BFA3B` | `98705009BFAB3C9A` | 692867 | 693950 |
| `ArmoredFury_ChinaTankGeneral-AmericaAirForceGeneral` | full | `7AC8B5574DE21417` | `104B58733F51FE60` | `68F33BE69B8EDF6D` | `2268AEEB66E1B7FF` | 714914 | 692984 |
| `ArmoredFury_GLA-America` | full | `58B066C7284C21B2` | `97CDBA96BD00F063` | `64E8453DEE5297E4` | `D6440448F9630ED4` | 727580 | 770737 |
| `BitterWinter_America-China` | full | `38380B920A732955` | `51BD14F53E358F43` | `5FED11D99834B1B6` | `D4992C5FE99A68D3` | 564691 | 497002 |
| `BitterWinter_China-GLA` | full | `26CD35AC8AA660F5` | `14DF904BEC8D311D` | `2B86E2038EDFAAB7` | `7B7F706B3E35D58C` | 436151 | 496018 |
| `BitterWinter_ChinaTankGeneral-AmericaAirForceGeneral` | full | `9D040D6ACB8E269D` | `34E6C959E12715E6` | `24F626B9E8F8CCA2` | `389BDF8604E76EB4` | 429385 | 537814 |
| `BitterWinter_GLA-America` | quick | `AB725263AEF8E3B7` | `092F2CF08F0E03A5` | `CDFCDF66730FF07B` | `8FC07F7A5B002530` | 481693 | 477416 |
| `DeathValley_America-China` | quick | `E98A532C90F89642` | `7F0A365871E47399` | `AC321A0CFA9C00E1` | `87C0A807F1C1276E` | 1023942 | 1028691 |
| `DeathValley_China-GLA` | quick | `9F0F8D7D28E8654D` | `B22B302FDE59E05E` | `90CDEBE18E7C6B01` | `C973A67C747213DD` | 1053704 | 1056258 |
| `DeathValley_ChinaTankGeneral-AmericaAirForceGeneral` | full | `FDAD90E9E139AFE9` | `6C73FC4D1714033F` | `6E2EE5C9DA09EFD1` | `DB7A55C079DFE16B` | 997490 | 1033478 |
| `DeathValley_GLA-America` | full | `3F33A4FEDEBA85C1` | `652C6C59B131BD01` | `3B00774A84E69278` | `F2C075335AB011FD` | 1055608 | 1055215 |
| `ai8_FortressAvalanche` | full | `90A0A2020CD9D943` | `2ADF0836978AAB3E` | `4FE3E6F70CF223B5` | `B93FBEB518AA06F3` | 2632596 | 2644361 |
| `ai8_IronDragon` | full | `C894A034D7911797` | `35F3E682D2F22B01` | `E816C1A74BAF7FE7` | `97B543DFCA1B259F` | 2393895 | 2400731 |
| `ai8_TwilightFlame` | full | `538D104E2A8FE934` | `E26CC739356ED921` | `BDB8F56ADD63170C` | `493EDF98BFBEE062` | 3379330 | 3386275 |
| `ai8_TwilightFlame_short` | quick | `518B62C47106C2AB` | `6231F002D205EE8F` | `4FAF848E76ECFEBC` | `EF6322ABDB48B522` | 2954218 | 2956611 |
| `mission_GC_AirGeneral_AmericaLaserGeneral` | full | `A7A5EFC754196678` | `9073D81C99DAB53A` | `8D0EEB0393C52136` | `E77FEF52AEA951B8` | 1630191 | 1645167 |
| `mission_GC_AirGeneral_ChinaInfantryGeneral` | full | `983315D08BABBBD1` | `0E5984962FEB4224` | `27DCB81F7419F0C4` | `E936B06AAB2B4B72` | 1630187 | 1645165 |
| `mission_GC_AirGeneral_GLAStealthGeneral` | full | `F95DDC63B877815C` | `A61D03B983CE7F25` | `048B1906AAA3BEB2` | `0A5A54628495E5A6` | 1630972 | 1645951 |
| `mission_GC_ChemGeneral_AmericaAirForceGeneral` | full | `E66023A1A7713080` | `3BDA3E3B47773BC7` | `495DAE0E030EEE3B` | `7A38E61AC62F5C28` | 1491769 | 1503399 |
| `mission_GC_ChemGeneral_ChinaTankGeneral` | full | `FDB19B02BC40D94A` | `BC014352B0BEBFBC` | `84709824D111DAAC` | `1DE3D0FB97A73AFF` | 1491244 | 1502874 |
| `mission_GC_ChinaBoss_AmericaAirForceGeneral` | full | `1DE21B847AB3F8BC` | `062B1C2BAE34D4DA` | `5A1F9964030EEEFE` | `0CEBE28BD2E0C38E` | 2887235 | 2910523 |
| `mission_GC_ChinaBoss_AmericaLaserGeneral` | full | `E788E126D7F981B2` | `D670E49EEC0CDD21` | `0FB25AE6821A211C` | `F01D5B632AD87F99` | 2887061 | 2910349 |
| `mission_GC_ChinaBoss_AmericaSuperWeaponGeneral` | full | `F1D2D7CBE8DEA529` | `31B0CFDA74B43EF5` | `4D1000E1F11DD6CB` | `8ABF8E3348CA946C` | 2887244 | 2910532 |
| `mission_GC_ChinaBoss_ChinaInfantryGeneral` | full | `87C0C8262EC13734` | `72B050F101ECE333` | `1CB2C6566F167085` | `11602E34420E2C6A` | 2876796 | 2900085 |
| `mission_GC_ChinaBoss_ChinaNukeGeneral` | full | `28AF6CD9ABDDB40A` | `957AF3FD726C96FA` | `EDAB910A2ED5DE69` | `3D32038BACA67AE1` | 2876457 | 2899746 |
| `mission_GC_ChinaBoss_ChinaTankGeneral` | full | `52FE4778A34A594C` | `F10C5125CCCEC477` | `9A23CADEAC6A1676` | `E17DA5585A01C4C1` | 2876618 | 2899907 |
| `mission_GC_ChinaBoss_GLADemolitionGeneral` | full | `20CF8D24D6249EB7` | `C58DC738480A588D` | `C78B57065FD8A868` | `60C8230A34A0A1CC` | 2881820 | 2905112 |
| `mission_GC_ChinaBoss_GLAStealthGeneral` | full | `D4D2228048E89A5D` | `BE851DDCADE9A6D6` | `4EBCAF824A441758` | `2EE1CC7E02AA7D9B` | 2881299 | 2904591 |
| `mission_GC_ChinaBoss_GLAToxinGeneral` | full | `EFA6C01C342601DE` | `8A4BBA469F426E72` | `234CF97434610B57` | `B1A6E3A154885DEF` | 2881427 | 2904719 |
| `mission_GC_LaserGeneral_ChinaNukeGeneral` | full | `231523828930AD5B` | `DA6E37150843479A` | `05F16683E5E71578` | `80C0A73E7767C341` | 4495609 | 4530853 |
| `mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral` | full | `5E436B3BD943DFC2` | `CAF9F1C8FA87CB05` | `ADCCB4A1B93CBAD7` | `97EE826F82D4DC34` | 1519548 | 1530153 |
| `mission_GC_TankGeneral_AmericaSuperWeaponGeneral` | full | `33D616C9E3B835D8` | `064718BF1C6C0FD0` | `5A5B8107630E51F7` | `469B8AA67790DCD9` | 2656652 | 2679307 |
| `mission_GC_TankGeneral_GLAToxinGeneral` | full | `BB6D04A6D5FD4759` | `C6B0DBCC757AFFDB` | `68169DCF5C83C7BD` | `46759DD417DE7123` | 2657356 | 2680014 |
| `mission_MD_CHI01` | full | `8E5774AE10BE1225` | `3F64C8F8D64B81C6` | `6BA18B9F50AE1B8E` | `656362D9C4763058` | 3514104 | 3533078 |
| `mission_MD_CHI05` | full | `19954A666CCCF947` | `9709FBD1429C8206` | `CCC309C2F84C1A87` | `A1F70E2AC2AC4639` | 5266371 | 5300450 |
| `mission_MD_GLA01` | full | `D1E3DB52348F9898` | `960B2D1660A1279E` | `5E3469F37CC49737` | `E764DA1050962851` | 4429967 | 4445902 |
| `mission_MD_GLA03` | full | `E79236DDB44B3E30` | `2BB0C71748DA9B5F` | `369AFAA71BB959AE` | `3B189E12ECA1853E` | 4090830 | 4111757 |
| `mission_MD_GLA05` | quick | `826CCD82C8536DC2` | `5A7711E275ECB644` | `10330D7D179054FD` | `D79C856A73783150` | 9038371 | 9071782 |
| `mission_MD_USA01` | full | `8D26113C9DA64703` | `8CFD326B5B0D5D84` | `945589AB7750A0CB` | `283468E78538EDE8` | 7799358 | 7840194 |
| `mission_MD_USA02` | full | `3AFE4707A243C3C7` | `E3FB0B6FE97E8B5E` | `A60D461ED547E16F` | `2993A73F066BEDDE` | 4986767 | 5018032 |
| `mission_MD_USA05` | quick | `1CB28E6AA923BDEF` | `F21A211382F17CD7` | `0816C3438CB7F689` | `628D7208A23B10AB` | 6549194 | 6580158 |
| `mission_Training01` | full | `160EB89088698B16` | `F59CB7888AECB13C` | `315CAEABA92DD3C2` | `C370CEAEF084D019` | 3556152 | 3586245 |
| `reference-match` | quick | `AE9C688309DA37E4` | `D0CA5F4EE456D6CE` | `FC6DFA759B1DCAF0` | `C5EE1090E1C24D7B` | 438099 | 357825 |
| `skirmish-sweep` | full | `DB5B0EA0F386D5D7…` (SHA-256 über 64 Karten) | `CBB37B33A4F1ABD8…` | – | – | – | – |
| `stress` | quick | `56882F763EABEF67` | `56882F763EABEF67` | `B8E663493F2C73D5` | `754FA4DCB9A41DF7` | 11862560 | 12095586 |

## Anhang C: Zielfahrer je Kampagnenkarte, 16 Bit gegen 24 Bit

`CampaignTool drive <content> --map "/NAME.map" --ticks 60000 --explain`, je Karte ein Prozess. In Klammern die Zahl der Eingriffe des Fahrers.

| Karte | Sieg alt (Skript @Tick, Eingriffe) | Sieg neu | Niederlage alt | Niederlage neu |
|---|---|---|---|---|
| `Training01` | Player Wins @420 (1) | gleich | Player Loses @2655 (2) | gleich |
| `GC_ChemGeneral` | END_Final_USA @270 (1) | gleich | Lose_END_Final_USA @150 (1) | gleich |
| `GC_NukeGeneral` | END_Final_USA @240 (1) | gleich | Lose_END_Final_USA @150 (1) | gleich |
| `GC_SuperWeaponsGeneral` | END_Final_USA @240 (1) | gleich | Lose_END_Final_USA @150 (1) | gleich |
| `GC_TankGeneral` | END_Final_USA @240 (1) | gleich | Lose_END_Final_USA @150 (1) | gleich |
| `GC_Stealth` | END_Final_USA @240 (2) | gleich | Lose_END_Final_USA @150 (1) | gleich |
| `GC_LaserGeneral` | END_Final_USA @240 (1) | gleich | Lose_END_Final_USA @150 (1) | gleich |
| `GC_ChinaBoss` | End the mission @360 (5) | gleich | Lose_END_Final_USA @2431 (2) | gleich |
| `GC_AirGeneral` | END_Final_GLA @270 (1) | gleich | Lose_END_Final_GLA @150 (1) | gleich |
| `MD_USA01` | END_Final_USA @785 (1) | gleich | Lose_END_Final_USA @211 (1) | gleich |
| `MD_USA02` | END_Final_ @2718 (4) | gleich | Lose_END_Final_ @12361 (30) | gleich |
| `MD_USA03` | CINE_AVVictory @1509 (3) | gleich | Lose_END_Final_ @241 (1) | gleich |
| `MD_USA04` | END_Final_USA @303 (9) | gleich | Lose_END_Final_USA @617 (20) | gleich |
| `MD_USA05` | END_Fade 04 @1443 (4) | gleich | Lose_END_Final_ @1280 (6) | gleich |
| `MD_GLA01` | Cheer1Done @526 (3) | gleich | Lose_END_Final_ @241 (1) | gleich |
| `MD_GLA02` | END_Final_ @240 (1) | gleich | Lose_END_Final_ @572 (5) | gleich |
| `MD_GLA03` | CINE_ENDVictory @4033 (12) | gleich | Lose_END_Final_ @242 (1) | gleich |
| `MD_GLA04` | CINE_END @1022 (1) | gleich | Lose_END_Final_ @241 (2) | gleich |
| `MD_GLA05` | CINE_END04END @1394 (1) | gleich | Lose_END_Final_ @1036 (1) | gleich |
| `MD_CHI01` | END_Final_ @301 (1) | gleich | Lose_END_Final_ @241 (1) | gleich |
| `MD_CHI02` | END_Final_ @301 (4) | gleich | Lose_END_Final_ @301 (1) | gleich |
| `MD_CHI03` | Cheer1Done @1722 (6) | gleich | Lose_END_Final_ @4471 (1) | gleich |
| `MD_CHI04` | END_Final_ @48258 (10) | gleich | Lose_END_Final_ @273 (1) | gleich |
| `MD_CHI05` | CINE_ENDVictory @2662 (23) | gleich | Lose_END_Final_ @241 (1) | gleich |

Zielfahrer: alt Sieg 24/24, Niederlage 24/24; neu Sieg 24/24, Niederlage 24/24; unterschiedliche Zeilen: 0 (keine).
Audit (900 Ticks): alt deterministisch 24/24, nach Speichern/Laden identisch 24/24, Endproben Sieg 13/24, Niederlage 15/24, Treffer auf fehlende Aktionen 0; neu 24/24, 24/24, Sieg 13/24, Niederlage 15/24, Treffer 0; Karten mit anderem Auditergebnis: 0 .
