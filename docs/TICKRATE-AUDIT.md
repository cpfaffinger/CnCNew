# Tickraten-Kopplung – Startinventar

Stand: 5. Oktober 2026, Commit `2a66661`. Dieses Dokument ist die Arbeitsgrundlage für die Phasen 18, 20, 21 und 23 des Programms in `docs/MULTIPLAYER-PLAN.md`.

**Herkunft und Verlässlichkeit:** Das Inventar stammt aus einem lesenden Suchlauf über `src/`, `tools/` und `tests/` (Suchmuster in §8). Es ist **nicht Zeile für Zeile von Hand geprüft** und sicher nicht vollständig. Zeilennummern verschieben sich mit jedem Commit. Phase 18 wiederholt den Suchlauf, prüft jede Stelle, ergänzt die fehlenden und schreibt dieses Dokument als geprüfte Fassung neu; erst danach gilt eine Zahl hieraus als gemessen. Abkürzungen: `Sim\` = `src/CnCNew.Simulation/`, `Mod\` = `Sim\Modules/BuiltIn/`, `Pres\` = `src/CnCNew.Presentation/`, `Game\` = `src/CnCNew.Game/`.

## 1. Die zentrale Konstante

`SimulationTiming.LogicFramesPerSecond = 30` (`Sim\Time/SimulationTiming.cs`) mit den Umrechnungen `DurationMillisecondsToTicks` (aufgerundet), `SecondsToTicks`, `PerSecondToPerTick` (÷ 30), `PerSecondSquaredToPerTickSquared` (÷ 900), `DegreesPerSecondToRadiansPerTick`. Direkte Verwendungen der Konstante:

| Projekt | Verwendungen | Dateien |
|---|---|---|
| `CnCNew.Simulation` | 61 (davon 8 in `SimulationTiming.cs`) | 32 |
| `CnCNew.Game` | 14 (4 davon über die Kopie in `CnCNew.Ui`) | 6 |
| `CnCNew.Presentation` | 1 (`SelectionFlash.cs`) | 1 |
| `CnCNew.Ui`, `CnCNew.Client.Stride`, `CnCNew.Audio`, `CnCNew.Data`, Werkzeuge | 0 | – |
| Tests | 4 | 4 |

`CnCNew.Data` speichert Dauern als Millisekunden (`GameTime`) und enthält **keine** 30er-Umrechnung; die Umrechnung geschieht beim Bau der Kataloge in der Simulation (`ModuleConfigReader.Ticks`, `ObjectTemplate`, `WeaponCatalog`, `UpgradeCatalog`, `SpecialPowerCatalog`, `TurretInfo`). Die geparsten Moduldaten sind damit ratenabhängig.

## 2. Kopien der Rate außerhalb der Konstante (Kategorie A)

„SIM“ meint den Simulationstick, „ORIGINALBILD“ die 30 Bilder je Sekunde, in denen das Original Client-Inhalte verfasst hat.

| Stelle | Inhalt | Bedeutung |
|---|---|---|
| `Sim\World/SimulationWorld.CreationLists.cs:343` | `-9m * Gravity / 900m` (900 = 30²) | SIM |
| `Pres\Objects/Suspension.cs:21` | `LogicTicksPerSecond = 30` | SIM (Federung rechnet je Tick) |
| `Pres\Objects/FrameProjector.cs:54` | `HealingIconTicks = 3 * 30` | SIM |
| `Pres\Objects/UnitConditionTracker.cs:27` | `CheerTicks = 3 * 30` | SIM |
| `Pres\Fx/SoundCues.cs:25`, `Game\Hud/HudMessageFeed.cs:35` | `UnderAttackFrames = 30 * 10` | SIM |
| `Pres\Fx/FxValueReader.cs:17` | `FramesPerSecond = 30f` | ORIGINALBILD, wird aber je Simulationstick fortgeschaltet (§6) |
| `src/CnCNew.Ui/Text/InGameUiSettings.cs:67` | `LogicFramesPerSecond = 30`, benutzt von `Game\Shell/MissionText.cs` | SIM (Uhr der Missionstexte ist der Tick) |
| `Game\Shell/MissionPresentation.cs:216` | `LetterboxFadeFrames = 30` | sollte Wandzeit sein, zählt heute Ticks |
| `Pres\Input/OrderMarkers.cs:41`, `Pres\Input/CameraInput.cs:23`, `Pres\Camera/RtsCamera.cs:22` | 30 | ORIGINALBILD, mit Wandzeit verrechnet (bereits entkoppelt) |
| `src/CnCNew.Ui/Transitions/WindowTransitionHandler.cs:130`, `TransitionEffects.cs` (`End = 30`) | 30 | ORIGINALBILD |
| `src/CnCNew.Client.Stride/Program.cs:357` u. a. | Schrittweite `1.0 / 30` im Rauchtest | fester Schritt |
| `Game\Live/LiveGame.cs:28–29` | `TickSeconds`, `MaxTicksPerFrame = 8` | SIM |
| Werkzeuge | `SimTool` (`VictoryTick / 30 / 60.0`), `PresentationTool/ScenarioAudit.cs`, `UiRenderTool` (mehrere Szenen) | gemischt |
| Tests | rund 105 Stellen, davon 97 `1.0 / 30` in 15 Dateien | fester Schritt |

Ganzzahlige Teilung der Rate, die bei 60 Hz andere Zeiten ergibt: `Mod\SlaveModules.cs:166` (30/4 = 7 Ticks = 233 ms; 60/4 = 15 Ticks = 250 ms), `Mod\AirModules.cs:79` (/5), `Mod\SpawnModules.cs:73` (/2), `Mod\WorldEffectModules.cs:475` (/2), `InGameUiSettings.cs:253` (/3), `Sim\Scripting/ScriptEngine.cs:178` (`% (2*30+1)`).

## 3. Nackte Tickzahlen (Kategorie B)

Rund 80 in der Simulation, rund 30 in `Sim\Testing`, je rund 15 in `CnCNew.Game` und `CnCNew.Presentation`. Auswahl nach Bereich:

- **Welt:** `SimulationWorld.Acquisition.cs` (`OrderedAttackGraceTicks = 45`, `ForceIdleTicks = 2` – laut Kommentar eigentlich 67 ms, `ChaseRefreshTicks = 15`); `SimulationWorld.Turrets.cs` (`TurretAimGraceTicks = 15`, `TurretReloadHoldCapTicks = 450`, `TurretSweepEnableTicks = 3`, `TurretLoseInterestTicks = 45`); `SimulationWorld.Construction.cs` (`SaleDurationTicks = 179`); `SimulationWorld.Vision.cs` (`VisionIntervalTicks = 3`, auch in `LiveGame.cs:211`).
- **Module:** `Phase5Modules.cs:1106/1109` (Neuplanung nach 30 Ticks, Vorausschau 25 Ticks); `FieldEffectModules.cs` (`Lifetime = 300`, Schlaf 30, `_life % 30`); `SlaveModules.cs` (`_framesToWait = 15`/`5`, `CheckInterval = 16`); `SpawnModules.cs` (`SPAWN_UPDATE_RATE` 15); `MatchModules.cs` (`HealRate = 3`); `VehicleModules.cs` (`GroundCheckDelay = 10`, `EmptyHulkCheckDelay = 15`); `RailroadModules.cs` (`FramesUnpulledLongEnoughToUnhitch = 2`); `CleanupAndSpyModules.cs` (`ReloadWait = 1000`); mehrere Vergleiche `<= 2`/`<= 3` Ticks in `AirModules.cs`, `AirWeaponModules.cs`, `SpecialPowerModules.cs`, `FlightModules.cs`.
- **Skripte:** `ScriptEngine.cs:72` (`EndGameDelayTicks = 120`), `ScriptEngine.Actions.cs:20` (`RetargetInterval = 30`), `ScriptEngine.Phase13.cs:341` (`tick % 30`).
- **Skirmish-KI:** `SkirmishAi.cs` – `FireInterval 5`, `ThinkInterval 15`, `RetryTicks 300`, `MemoryTicks 900`, `OrderRefreshTicks 150`, `BoardingTicks 300`, `RevisitTicks 3600` und rund ein Dutzend Literale im Rumpf (`+ 30`, `+ 1800`, `450` …); `SkirmishMatchSession.cs` (`VictoryCheckInterval = 15`).
- **Fahrer und Szenarien in `Sim\Testing`:** `ReferenceMatchScenario.cs`, `SkirmishMatchScenario.cs` (108.000/300/2.700), `StressScenario.cs` (216.000 und viele Modulo-Werte), `MissionScenario.cs`, `MissionObjectiveDriver.cs` (4.500, `spacing = 15`, 60), `ObjectiveDriver.*.cs`.
- **Spielschicht:** `LiveGame.cs` (`MaxTicksPerFrame = 8`), `DemoScenarios.cs`, `EvaCatalog.cs:140` (Standard 900/150), `MissionText.cs:102` (`BlinkFrames = 10`), `LiveMission.cs:51` (`ClientPresentationLatencyTicks`).
- **Präsentation:** `FrameProjector.cs` (`MuzzleFlashTicks = 4`, `% 150`, `SceneryFogInterval = 5`), `ConditionDeriver.cs:156` (Feuerzustand genau einen Tick), `SelectionFlash.cs` (`DecayTicks = 4`, `ScriptFlashInterval = 15`), `SoundCues.cs:179`, `StandingOrders.cs:88`.

## 4. Daten, die als rohe Bildwerte verbraucht werden (Kategorie C)

Hier hilft das Umrechnen der Konstante nicht; die *Daten* tragen die 30 Hz in sich.

- **`ModuleConfigReader.Ticks(…, fallback)`:** der Ersatzwert ist bereits in Ticks. Literale Ersatzwerte: `AirWeaponModules.cs:114/230/306/958`, `FieldEffectModules.cs:32`, `Phase5BlockerModules.cs:426`, `ProjectileModules.cs:341`, `RailroadModules.cs:36`, `StructureModules.cs:91`, `WorldEffectModules.cs:314`.
- **`Int`/`Real` je Tick ohne Umrechnung:** `RailroadModules.cs:33–35` (`SpeedMax` je Bild, `Acceleration 1.01`, `Braking 0.99`, `Friction 0.97`); `AirWeaponModules.cs:459–470` (Drehrate je Tick, `ForwardDamping`, `RelativeSpeed`); `TransportModules.cs:127` (`SpiralOrbitForwardSpeedDamping`); `ToppleAndFallModules.cs:52–54, 209` (Prozentwerte auf Geschwindigkeit je Tick, `StructuralDecay`); `VehicleModules.cs:178` (`ThrowForce/mass` als Geschwindigkeit je Tick); `SlaveModules.cs:528`.
- **Kataloge:** `WeaponCatalog.cs:33` (`InstantSpeed 999999` je Bild), `TurretInfo.cs:29` (`DefaultRate 0.01` rad je Bild); die Millisekunden-Felder der Waffen werden aufgerundet (`WeaponCatalog.cs:208–284`) – bei 60 Hz ändert sich die Quantisierung.
- **Kartenskripte:** `SET_TIMER`/`SET_RANDOM_TIMER` zählen Bilder (`ScriptEngine.Actions.cs:54`, heruntergezählt in `ScriptEngine.cs:327`); `*_FOR_FRAMECOUNT` (`ScriptEngine.Sequential.cs:81–92`); `CAMERA_BW_MODE_BEGIN/END` in Bildern (`MissionPresentation.cs:1122–1137`). Die Sekunden- und Millisekunden-Varianten werden umgerechnet.
- **Präsentationsdaten in Bildern:** `FxValueReader.DurationFrames`, `FxCatalog` (`ThrottleFrames`), `FxList.cs:116`, `Suspension.cs:34–36` (Steifigkeit und Dämpfung je Tick), `InGameUiSettings.cs:216–255`.

## 5. Integration je Tick, die eine andere Schrittweite nicht übersteht (Kategorie D)

- **Bodenbewegung** `Phase5Modules.cs:1059–1125`: Drehbegrenzung je Tick zusammen mit dem Schwellwinkel π/6, Regel „auf der Stelle drehen“ (`distance < Speed/turnRate*2`), Beschleunigen/Bremsen additiv je Tick, Ankunftsradius `max(CloseEnough, Speed)` (ein Tick Weg). **Flügelflug** `:1133–1180` nach demselben Muster.
- **Jets** `JetAIUpdate.cs:618, 783, 803–807, 911–920` (Untergrenzen je Tick, `Speed/8` als Beschleunigung benutzt).
- **Geschosse** `ProjectileModules.cs:428, 612–633, 233/255` (Beschleunigung je Tick, Neuausrichtung jeden Tick, halbimplizite Schwerkraft, Detonation einen Schritt nach dem letzten Punkt).
- **Umstürzen** `ToppleAndFallModules.cs:73, 113–166, 228, 313–319` (Euler mit Abprall, Ruheschwelle 0,01 rad je Tick, multiplikativer Zerfall je Tick).
- **Multiplikative Dämpfung je Tick:** Hubschrauber-Absturz `TransportModules.cs:215–246`, Neutronenrakete `AirWeaponModules.cs:593–599`, Zugformation `FieldEffectModules.cs:319–330` (Reibung 0,95, 7 %/93 % je Tick), Zug `RailroadModules.cs:200–252`, Schranke `WorldEffectModules.cs:640–675`.
- **Abweichende Integrationsreihenfolge:** Battle Bus `VehicleModules.cs:190–191` (expliziter Euler), Fall `PhysicsAndMinefieldModules.cs:58–61`.
- **Turmschwenk** `SimulationWorld.Turrets.cs:425–436` (Richtungswechsel hängt vom Schritt je Tick gegen 0,035 rad ab).
- Ein echter Wurf „x % je Tick“ wurde nicht gefunden; mehrere Zufallsziehungen geschehen je Prüfung (`SlaveModules.cs:611`), die Reihenfolge der Ziehungen verschiebt sich also mit der Rate.
- **Präsentation:** Federung `Suspension.cs:84–160`, Alpha-Zerfall `MissionText.cs:62`, Bildfilter `MissionPresentation.cs:656–672`.

## 6. Uhren der Präsentation (Kategorie E)

- **Am Simulationstick:** Mündungsfeuer, Heilsymbol, Auswahlblinken, Jubel, Feuerzustand (`FrameProjector`, `ConditionDeriver`, `SelectionFlash`, `UnitConditionTracker`); Tonhinweise und Drosselung (`SoundCues`); EVA (`EvaCatalog` über `AudioDirector`, einmal je Tick); HUD-Meldungen und Superwaffen-Uhren; die gesamte Missionspräsentation (`MissionPresentation.cs:131`: Uhr = Tick).
- **Partikel, Lichter, Leuchtspuren, Decals laufen am Simulationstick:** `LiveGame.cs:203–212` ruft `Fx.Update()` einmal je Tick; die FX-Daten sind in 30er-Bildern verfasst. Bei 60 Hz liefe jeder Effekt doppelt so schnell. `FxExecutor.cs:149` mischt zudem FX-Bilder mit Simulationsticks.
- **Bereits an Wandzeit:** Modell- und Skelettanimation (`ObjectLayerCore.cs:236–251`, `W3dAnimationClock`), Texturanimation, Wasser/Nebel/Minikarte, Kamera, Befehlsmarken, Shell-Übergänge, Umgebungston.
- **Interpolation:** `FrameProjector.Project(alpha)` interpoliert Position und Rumpfdrehung zwischen zwei Ticks; alles andere springt je Tick.

## 7. Formate und Tests (Kategorien F und G)

- Die Tickrate wird geschrieben und geprüft in `WorldSnapshot.cs:37/67`, `MissionSnapshot.cs:39/81`, `ReplayFormat.cs`, `ReplayRunner.cs:50`. `MissionSnapshot` speichert Optionen in Ticks (`maxTicks`, `presentationLatencyTicks`, `combatIntervalTicks`); rund 111 Zustandsfelder in 41 Dateien sind absolute Ticks (`nextWake`, `readyTick` …).
- `Tests.State.cs:153, 217–221` prüft heute ausdrücklich, dass Rate 60 **abgelehnt** wird.
- **Die Tests enthalten keine Referenz-Zustandshashes.** Die Referenzwerte (Referenzmatch, Stresstest, KI-Läufe) stehen nur in der Dokumentation (137 sechzehnstellige Hex-Werte in 26 Dateien, 79 davon in `docs/SIMULATION-CORE.md`) und in `CLAUDE.md`. Ein „hash-neutraler“ Umbau lässt sich deshalb heute nur von Hand gegen die Doku prüfen (siehe Referenzmanifest, `docs/HANDOFF-PHASE18.md`).
- Von Hand geschriebene Tickerwartungen: rund 845 Zeilen in 84 Testdateien (am dichtesten `MissionTextTests`, `TransitionTests`, `UnitConditionTests`, `Tests.Slaves`, `AudioDirectorTests`); ausdrückliche 30-Hz-Modelle in `Tests.GoldenMaster.cs` und `Tests.Basics.cs:48–88` (Tabelle Millisekunden → Ticks, binary32-Abweichung).
- Golden-Dateien: `tests/CnCNew.Game.Tests/baselines/screens.txt` (54 SHA-256), `hud-drawlist-1280x720.sha256`; ob sie von der Tickrate abhängen, ist offen.

## 8. Suchmuster (zum Wiederholen)

Über `src`, `tests`, `tools`, nur `*.cs`, ohne `.claude/worktrees`:

```
LogicFramesPerSecond
(PerSecond|FramesPerSec|TicksPerSec|FrameRate|TickRate|Fps)\w*\s*=\s*\d
(1(\.0)?[dfm]?\s*/\s*30(\.0)?[dfm]?\b|/\s*30(\.0)?[dfm]\b|\*\s*30(\.0)?[dfm]\b|30m?\s*/\s*1000|/\s*33\b|33\.33|1000\s*/\s*30)
\b30(\.0)?[fdmL]?\b        \b900m?\b|\b3600\b|\b1800\b|\b450\b
const\s+(int|long|uint|ulong|short)\s+\w+\s*=\s*[^;]*;
(long|int)\s+\w*(Tick|Ticks|Frames|Interval|Cadence|Every|Spacing)\w*\s*(\{[^}]*\})?\s*=\s*[\d_]+
(Tick|tick|_life|frame\w*|_age)\s*%\s*\d+
Tick\s*[-+]\s*[^;]*?(>=|<=|>|<|==)\s*\d+|Tick\s*[-+]\s*\d+
return\s+(world\.Tick\s*\+\s*)?\d+L?\s*;
ModuleConfigReader\.Ticks\([^)]*,\s*[^0)\s][^)]*\)
ModuleConfigReader\.(Int|Real)\(configuration,\s*"[^"]*(Time|Delay|Rate|Frame|Duration|Interval|Life|Wait|Speed|PerSecond)[^"]*"
\*=|(Speed|Velocity|Rate)\s*=\s*[^;]*\*\s*\w*(Damp|Friction|Factor|Decay|Braking|Acceleration)
LogicRandom\.Range\((0|1),\s*\d+\)
(?i)(per frame|every frame|\(frames\)|per logic frame)
writer\.Int\("\w*(Tick|Frame|Until|At|Timer|Delay|Expire)\w*"
GameTime|ElapsedInState|DeltaSeconds|TotalSeconds|W3dAnimationClock
```
