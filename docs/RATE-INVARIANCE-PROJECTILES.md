# Raten-Invarianz: Geschosse (Phase 23, Paket PROJECTILES)

Stand 8. Oktober 2026, Zweig `worktree-agent-a4dece435d2badf01`, aufbauend auf `7b54374`. Dieses Dokument gehört zum Arbeitsvorrat aus
`docs/TICKRATE-MIGRATION.md` §5 und zur Raten-Äquivalenz-Suite (`docs/SIMULATION-CORE.md` §39). Es beschreibt, was an Geschossen, Raketen, der
Neutronenrakete, der Lenkbombe, dem EMP-Puls und dem freien Fall geändert wurde, damit sie bei 30, 60 und 120 Hz dasselbe tun – und was offen bleibt.
30 Hz ist die Definition des richtigen Verhaltens und blieb bytegleich (Eingangsmanifest und Referenzmanifest nach jedem Commit, voller Satz am Ende).

## 1. Prinzip

Ein Geschoss trifft seine Entscheidungen **einmal je Bild des Originals**, auf seiner eigenen Phase (gezählt ab dem Start), aus genau dem Zustand, den
es bei 30 Hz an dieser Stelle hat, und mit Größen je Bild (Tempo je Bild, Beschleunigung je Bild², Schwerkraft je Bild²). Der Schritt, den ein Bild
entscheidet, wird in gleichen Anteilen über die Ticks dieses Bilds geflogen; der letzte Anteil nimmt den Rest, sodass die Summe exakt der Schritt ist.

- **Erster Schritt ein Bild nach dem Start.** Bei 30 Hz wird das Geschoss am Ende des Schusstick F erzeugt und im Tick F + 1 zum ersten Mal bewegt. Bei
  der Rate r·30 Hz fällt der erste Schritt in den Tick F + r, jeder weitere r Ticks später. Entscheidungen und Ereignisse (Detonation, Aufschlag,
  Zündung, Treibstoffende) fallen damit auf den **ersten Tick** ihres Bilds – in Sekunden derselbe Zeitpunkt wie bei 30 Hz.
- **Zustand an der Bildgrenze.** Nach dem letzten Tick eines Bilds steht das Geschoss dort, wo es bei 30 Hz nach dem Tick dieses Bilds steht – in den
  Tests Bit für Bit. Dazwischen liegt es auf der Sehne zwischen den beiden Bildzuständen.
- **Sofort statt in Anteilen:** Ankunft am Ziel, Bodenberührung und der Sprung auf den Zwischenpunkt der Neutronenrakete setzen das Geschoss im
  entscheidenden Tick an den Ort, wie bei 30 Hz (dort ist es ebenfalls ein Sprung um höchstens einen Schritt).
- **Neuer Zustand** (`subStep`, bei der Rakete und der Lenkbombe zusätzlich der Schritt des Bilds) wird nur geschrieben, wenn er nicht 0 ist. Bei 30 Hz
  ist er immer 0: Snapshots und Hashes bleiben bytegleich. Bei 60/120 Hz ändert sich der Snapshot-Inhalt innerhalb eines Bilds.
- **Tempo je Bild wie im 30-Hz-Katalog.** `ProjectileFrameGrid.PerTickToPerOriginalFrame` (neue Datei `src/CnCNew.Simulation/Time/FrameGrid.Projectiles.cs`)
  findet aus dem Katalogwert je Tick den Wert je Sekunde mit höchstens vier Nachkommastellen, der ihn reproduziert, und rundet ihn so, wie der
  30-Hz-Katalog rundet. Ein Wert, der sich nicht reproduzieren lässt (mehr Nachkommastellen, kein Katalogtempo), wird mit dem ganzen Faktor skaliert
  (Abweichung höchstens die letzten Bits). Katalogtypen haben kein neues Feld bekommen.
- **Kosten:** bei 30 Hz kommt je Update ein Vergleich `subStep != 0` und ein Lesen von `TicksPerOriginalFrame` dazu, keine Division. Die Umrechnung des
  Tempos (Dezimalarithmetik) läuft einmal je Schuss und nur oberhalb von 30 Hz.

## 2. Ursachen und Korrekturen

Suite-Zahlen: Gruppe `projectiles`, „außerhalb der Toleranz“ von 743 Größen, in Klammern bei 60 Hz / bei 120 Hz. Ausgangsstand 106 (54 / 103).

| §5-Eintrag | Befund vorher (30 / 60 / 120 Hz) | Korrektur (Datei, Symbol, Prinzip) | nachher | Commit |
|---|---|---|---|---|
| §5.6 `SimulationWorld.Projectiles.FlightTicks` | Bilder vom Schuss bis zum Treffer, 200 Einheiten bei 100 je Sekunde: 61 / 60 / 61 (das Tempo je Tick ist je Rate für sich gerundet; „Tempo je Tick × Ticks je Bild“ verschiebt `ceil` an einer Bildgrenze um ein Bild) | `World/SimulationWorld.Projectiles.cs` `FlightTicks`, `ProjectileModules.FrameSpeed`, `Time/FrameGrid.Projectiles.cs` `PerTickToPerOriginalFrame`: gezählt wird gegen das Tempo je Bild des 30-Hz-Katalogs; der Wächterwert „sofort“ bleibt er selbst | 61 / 61 / 61; Suite 106 → 106 (kein Fall liegt auf so einer Grenze) | `b5b9fd4` |
| §5.2 `DumbProjectileBehavior.CalcFlightPath`, `Update` | Bahnpunkte je Tick statt je Bild: Battlemaster-Granate 0,3000 / 0,2667 / 0,2500 s; Bogen 14 / 27 / 53 Punkte; Detonation „ein Update später“ = ein halbes oder viertel Bild; Nachführung je Tick | `Modules/BuiltIn/ProjectileModules.cs` `DumbProjectileBehavior.Launch`/`Update`/`FlyToward`: Tempo je Bild, ein Bahnpunkt je Bild, erster Punkt ein Bild nach dem Start, Detonation ein Bild nach dem letzten Punkt, `MaxLifespan` und Nachführung (Weg eines Bilds) nur auf den Bildticks; innerhalb des Bilds in gleichen Anteilen zum Punkt des Bilds | Battlemaster 0,3000 an allen Raten, Scheitel 14,257 und Weglänge 94,928 gleich; `NukeCannonGun` 1,6000 an allen; Suite 106 → 73 (49 / 70), Größen der `DumbProjectileBehavior`-Fälle 41 → 8 | `2ed62cd` |
| §5.2 `MissileAIUpdate.Launch`, `Update`, `Move` | `_acceleration = WeaponSpeed` je Tick als Beschleunigung je Tick² (x nach einem Bild 10 / 7,5 / 6,25); Euler-Rückstand (x nach Bild 10: 56 / 50 / 47,19); Ankunft „≤ ein Tick Flug“; `_noTurnDistanceLeft` aus dem Vortick; Schwerkraft je Tick²; Tomahawk Flugzeit 1,2000 / 1,2500 / 1,2417 s, Weg 225,024 / 231,861 / 230,662; Scud-Sturm Ablage 31,773 / 34,209 / 35,442 | `ProjectileModules.cs` `MissileAIUpdate.Launch`/`Update`/`Fly`/`FlyShare`: Tempo, Höchsttempo und Beschleunigung je Bild (Bild²), Zustandsmaschine einmal je Bild (Zündverzögerung, Treibstoff, Lock, Strecke vor dem Drehen, Ankunft = ein Bild Flug, Boden), Schritt in Anteilen; `ProjectileModules.GravityPerFrameSquared`; Folge-Commit `65b9783` schreibt die Skalierung der Beschleunigung ohne Quadrat (Quelltextregel), ohne Verhaltensänderung | Tomahawk 1,2000 und 225,024 an allen Raten; Scud-Sturm 31,773 und 568,250 an allen; Suite 73 → 38 (20 / 37), Größen der `MissileAIUpdate`-Fälle 53 → 18 | `c4a0ac1`, `65b9783` |
| §5.1 `NeutronMissileData.ForwardDamping`/`RelativeSpeed`, §5.2 `NeutronMissileUpdate.Update`/`DoAttack` | gemischte Rekursion (Schub je Bild², Dämpfungsanteil je Bild) je Tick: Flugzeit 8,0667 / 6,5667 / 5,8083 s und 8,5000 / 6,8000 / 5,9167 s; Scheitel 626,581 / 636,207 / 630,016 | `Modules/BuiltIn/AirWeaponModules.cs` `NeutronMissileUpdate.Launch`/`Update`/`DoAttack`/`TurnToward`: Start ein Bild nach dem Schuss, Rekursion, Drehgrenze (eines Bilds), Sondersteigflug, Strecke vor dem Drehen, Zwischenpunkt und Boden einmal je Bild; die Geschwindigkeit des Bilds in Anteilen | 8,0667 an allen Raten; 8,5000 / 8,5000 / 8,5083 s (`HeightDieUpdate` beendet den Sturzflug und schaut je Tick); Scheitel 626,581 an allen; Suite 38 → 29 (10 / 29), Größen der beiden Fälle 11 → 2 | `41d1cbc` |
| §5.1 `SmartBombHomingData.CourseCorrectionScalar` | Mischfaktor je Bild je Tick angewendet; mit s = 0,8 nach dem ersten Bild 20 / 36 / 59,04 von 100 Einheiten (gerechnet, alter Stand nicht gemessen) | `Modules/BuiltIn/LastBlockerModules.cs` `SmartBombTargetHomingUpdate.Update`: Mischung einmal je Bild, der Weg in Anteilen | 20 / 20 / 20, über 30 Bilder Bit für Bit gleich; die Suite hat keinen Fall (§39.5) | `0f5fd14` |
| §5.1 `EmpUpdate.Update` (`_scale`) | Anteil 0,05 je Tick: Restabstand zum Zielmaß nach 20 Bildern 35,8 % / 12,9 % / 1,7 % (gerechnet); nur Zustand und Darstellung | `AirWeaponModules.cs` `EmpUpdate.Update`: der Anteil nur auf den Bildticks des Pulses (vom Todestick zurückgezählt); kein neuer Zustand | Maß nach jedem Bild Bit für Bit gleich; die EMP-Zeiten der Gruppe `timed` waren gleich und bleiben es | `9132127` |
| §5.2 `PhysicsBehavior.Update` (freier Fall) | Euler je Tick mit Schwerkraft je Tick²; Landung um Bruchteile eines Bilds verschieden (alter Stand auf der Testszene nicht gemessen) | `Modules/BuiltIn/PhysicsAndMinefieldModules.cs` `PhysicsBehavior.StartFall`/`Update`: erster Schritt ein Bild nach dem Loslassen, Schwerkraft je Bild² auf die Geschwindigkeit je Bild, Landung am ganzen Schritt beurteilt, Schritt in Anteilen | aus 300 Einheiten Höhe mit 3 Einheiten je Bild seitwärts: nach jedem Bild Bit für Bit gleich, Landung im selben Bild; die Suite hat keinen eigenen Fall | `2777f86` |

Tests: `tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Projectiles.cs` (elf Tests „Rate invariance, projectiles: …“, je Ursache mindestens einer
bei drei Raten, dazu Snapshot innerhalb eines Bilds). Die vier von Hand gerechneten Flugbahnen aus `Tests.Projectiles.cs` (Klasse T Nr. 5–8 in
`docs/TICKRATE-MIGRATION.md` §15.2) sind in Bildern geschrieben (`537ce09`) und bestehen bei 30, 60 und 120 Hz **durch das Verhalten der Simulation**;
keine musste auf 30 Hz festgelegt werden. Ebenso der Lenkbomben-Test in `Tests.LastBlockers.cs` („ein Bild“ statt „ein Schritt“).

## 3. Ergebnis

| Messung | vorher | nachher |
|---|---|---|
| Suite `projectiles` (743 Größen, 91 Fälle) | 106 außerhalb (54 bei 60 Hz, 103 bei 120 Hz), 53 Fälle | **29** außerhalb (10 bei 60 Hz, 29 bei 120 Hz), 24 Fälle |
| Suite `weapons` (1.474 Größen), Nebenwirkung | 57 (43 / 57) | 47 (43 / 47): neunmal „first shot to first damage“, einmal „damage in 60 s“ |
| Suite `flight` (74), `timed` (383) | 23 und 56 | unverändert 23 und 56 |
| Simulationstests `--tick-rate 60` | 6 Ausfälle | 2 (Blitzzahl der Übernahme, ein Turm-Rohwert – beide nicht in diesem Paket) |
| Simulationstests `--tick-rate 120` | 14 Ausfälle | 10 (keiner in diesem Paket) |
| Simulationstests 30 Hz | grün | grün |

80 Größen der Gruppe `projectiles` sind behoben, drei sind neu außerhalb der Toleranz (vorher zufällig innerhalb): `BattleshipUSSBenderGun`
„scatter: mean impact distance“ (26,318 / 26,899 / 26,899, vorher 26,769), und bei 120 Hz „moving victim: impact distance“ von `ArtilleryPlatformGun`
(10,846 / 10,561 / 10,344) und `StrategyCenterGun` (14,089 / 13,800 / 13,578).

## 4. Verwendungen des Auswegs „Originalraster“

Keine Bewegung, keine Befehlsannahme und kein Zielerwerb läuft nur auf jedem (Rate/30)-ten Tick: jedes Geschoss bewegt sich in jedem Tick. Einmal je
Bild des Originals (auf der Phase des Objekts) laufen:

1. **Die Entscheidungen der Geschosse** – `DumbProjectileBehavior` (Bahnpunkt, `MaxLifespan`, Nachführung des Endpunkts, Detonation),
   `MissileAIUpdate` (Zustandsmaschine: Zündung, Treibstoff, Lock, Richtung zum Ziel, Ankunft, Boden, Umlenkung auf eine Leuchtkugel),
   `NeutronMissileUpdate` (Rekursion, Drehung, Zwischenpunkt, Boden), `PhysicsBehavior` (Schwerkraft, Landung), `SmartBombTargetHomingUpdate`
   (Mischung). Grund: das ist die beschlossene Unterteilung – Größen zweiter Ordnung und gemischte Rekursionen ändern sich einmal je Bild, die Lage
   jeden Tick. Die Richtung einer lenkenden Rakete zählt dazu: sie ist Teil des Integrationsschritts.
2. **Der Wachstumsanteil des EMP-Maßes** (`EmpUpdate`): ein multiplikativer Faktor je Bild, einmal je Bild angewendet (bevorzugte, exakte Form); das Maß
   wird zwischen den Bildern nicht interpoliert, weil es keine Spielwirkung hat.

Die Umlenkung einer Rakete auf eine Leuchtkugel (`_decoyTick`) wird dadurch erst im nächsten Bildtick der Rakete wirksam – höchstens ein Bild später
als gesetzt, bei 30 Hz unverändert.

## 5. Offen, und warum

Die 29 verbleibenden Größen der Gruppe `projectiles`:

1. **21 × „moving victim: impact distance from the victim“** (19 nur bei 120 Hz, 2 bei 60 und 120 Hz), Abweichung 0,2 bis 1,0 Einheiten. Das Geschoss
   landet bei jeder Rate am selben Ort im selben Bild; verschieden ist, wo das fahrende Opfer in diesem Tick steht. Der Aufschlag fällt auf den ersten
   Tick eines Bilds; in diesem Tick hat das Opfer bei 30 Hz seinen ganzen Bildschritt hinter sich, bei 60 Hz die Hälfte, bei 120 Hz ein Viertel
   (0,33 und 0,5 Einheiten für einen Panzer mit 20 Einheiten je Sekunde, bei der Toleranz von 0,5 Einheiten). Das folgt aus der Unterteilung selbst und
   lässt sich in den Geschossen nicht beheben. Ein Messversuch (das Opfer erst an der Bildgrenze lesen) machte es schlechter (54 statt 29), weil eine
   lenkende Rakete dann auf dem halben Schritt des Opfers gelandet ist; er wurde verworfen. Bei den zwei Neutronenraketen-Fällen (119,250 / 118,621 /
   118,228) kommt die Fahrt des Opfers über acht Sekunden dazu (Paket LOCOMOTION).
2. **4 × Inferno-Granate** (`InfernoCannonGun`, `…Upgraded`: Ablage 1,258 / 0,629 / 0,314, Weglänge 247,837 / 252,731 / 255,178). Die Granate trägt
   `HeightDieUpdate` (`TargetHeight = 1`, nur im Sinkflug); das Modul schaut jeden Tick und sieht die Granate jetzt innerhalb eines Bilds unter der
   Höhe, also früher und an einem Zwischenpunkt. `HeightDieUpdate` gehört nicht zu diesem Paket. Exakt gleich wird es nur, wenn das Modul den Zustand
   an der Bildgrenze beurteilt (letzter Tick des Bilds) – eine Festlegung, die alle Pakete betrifft (§6).
3. **4 × Streusalve** (`ArtilleryPlatformGun` 8 / 7 / 7 von 40 Aufschlägen, `BattleshipUSSBenderGun` 21 / 20 / 20, dazu die mittlere Ablage). Vierzig
   Geschosse starten im selben Tick; die meisten schlagen schon bei 30 Hz nie auf. **Nicht nachverfolgt**, plausibel ist: die erste Detonation zerstört
   Nachbarn im Flug, und im Detonationstick stehen die Nachbarn mit kleinerer Kennung bei 30 Hz einen ganzen Bildschritt weiter, bei höherer Rate
   einen Anteil davon. Die Zahl der Zufallszüge je Schuss ist an jeder Rate gleich (zwei in `FireWeaponAt`, drei bei `TumbleRandomly`), die mittlere
   Ablage unterscheidet sich nur über die fehlenden Aufschläge.

Bewusst nicht geändert:

- **§5.4 `PointDefenseLaserUpdate.FireWhenReady`** (Reichweitenprüfung und Schussversuch je Tick). Das ist Zielerwerb und soll jeden Tick laufen; die
  Zähler liegen seit Phase 20 im Raster. Der erste Schuss kann bei höherer Rate um weniger als ein Bild früher fallen. Kein Fall der Suite schlägt
  deswegen fehl; nicht einzeln vermessen.
- **„Patriot anti-air damage +1,2 % bei 120 Hz“**: als eigener Befund nicht nachgemessen; in der Gruppe `weapons` ist nach den Änderungen eine Größe
  „damage in 60 s“ weniger außerhalb der Toleranz (4 → 3), welche Waffe das ist, wurde nicht zugeordnet.
- **`MissileData.KillSelfDelay`** und die übrigen Dauern liegen seit Phase 20 im Raster und brauchten keine Änderung.

## 6. Neue Befunde

1. **Beobachter im ersten Tick eines Bilds sehen einen Teilschritt.** Die Unterteilung legt Entscheidungen auf den ersten Tick des Bilds und verteilt
   die Bewegung auf alle Ticks. Ein Modul, das jeden Tick die Lage eines anderen Objekts beurteilt (`HeightDieUpdate`, Radius-Schaden einer Detonation,
   der Abstand zu einem fahrenden Opfer), sieht im Entscheidungstick 1/r des Schritts, bei 30 Hz den ganzen. Der Fehler ist auf einen Bildschritt des
   beobachteten Objekts begrenzt, lässt sich aber mit keiner Wahl der Phase ganz vermeiden (Objekte mit kleinerer Kennung sind im selben Tick schon
   bewegt, die anderen noch nicht). Die Toleranz „0,5 %, mindestens 0,5 Einheiten“ reicht dafür bei 120 Hz nicht immer.
2. **Die Aufladezeit „recharge from creation (ready tick)“** ist bei allen Superwaffen mit Silo oder Türen um einen Tick länger (240,0000 / 240,0167 /
   240,0083 s; Neutronenrakete, Partikelkanone, Scud-Sturm, Marschflugkörper, Laserkanone – sechs Größen der Gruppe `timed`). Das ist nicht die
   Neutronenrakete selbst, sondern die Spezialkraft beziehungsweise `MissileLauncherBuildingUpdate` (Paket TIMING); hier nicht angefasst.
3. **`ProjectileFrameGrid.PerTickToPerOriginalFrame` ist eine Rekonstruktion**: exakt für Tempi mit höchstens vier Nachkommastellen je Sekunde (alle
   geprüften Werte des Tests), sonst auf die letzten Bits genau. Sauberer wäre ein Tempo je Bild im Katalog; das verbietet das Eingangsmanifest in
   dieser Phase.
4. **`StartFall` nimmt die Seitwärtsgeschwindigkeit je Bild** (vorher je Tick); der einzige Aufrufer übergibt null.
5. **Die Quelltextregel „kein Wert mit sich selbst multipliziert“ schlug zwischen `c4a0ac1` und `65b9783` fehl** (ein `perFrame * perFrame`); in
   dieser Spanne waren Manifest und die gezielten Tests grün, der volle Testlauf aber nicht.
