# Präsentation: Effekte (FXList, Partikel, Tracer, Licht, Kamera, Decals, Soundcues)

Stand: 30. September 2026 (Paket C2b)

`CnCNew.Presentation.Fx` führt die Effektanforderungen der Simulation rendererneutral aus. Eingaben sind der
`CompiledDefinitionGraph` (über `FxCatalog`) und die `SimEvent`s eines Ticks; Ausgaben sind reine Daten, die ein
Renderer beziehungsweise die Audio-Schicht pro Frame abholt. Es gibt keine Rückrufe in Renderer oder Audio, keine
Stride-, W3D-, Content- oder Formats-Abhängigkeit. Gleitkomma ist hier erlaubt; die Reproduzierbarkeit kommt aus einem
eigenen Zufallsgenerator, festen Schritten und kanonischer Reihenfolge.

## Einstiegspunkte für die Integration

| Typ | Aufgabe |
|---|---|
| `FxCatalog.Build(graph)` | FXLists, Partikelsystem-Vorlagen, Waffen-/Objekt-/DamageFX-Effektbezüge, Soundtabellen, GameData-Werte, `Diagnostics` |
| `FxExecutor(catalog, seed, budget?)` | `ExecuteEvents(tick.Events, view)` nach jedem `SimulationWorld.Step()`, `Update(view)` einmal pro Logikframe, `Capture(cull)` → `FxFrame`, `DrainSoundCues()` |
| `IFxWorldView` / `SimulationFxWorldView` | Pose, Knochen, Mündung, Bodenhöhe, Shroud; der Adapter über `SimulationWorld` braucht `Refresh()` nach jedem Step |
| `ParticleWorld` | Partikelsimulation (`Spawn`, `Destroy`, `Step`, `Capture`), auch ohne Executor nutzbar |
| `CameraShake` | Verrechnung von `ViewShakeRequest`s mit der Kameraposition (Kamera-Agent) |
| `SoundCueRateLimiter` | Deterministische Drosselung der Soundcues vor der Audio-Schicht |
| `FxAudit.Run` / `FxAudit.ExecuteAll` | Inventur und Ausführungsdurchlauf (Tool `fx-audit`, Realdatentest) |

`FxFrame` enthält `ParticleFrame` (Instanzen plus Batch-Schlüssel), aktive Tracer, Lichtpulse, alle Decals sowie die
einmalig ausgelieferten Ray- und ViewShake-Anforderungen.

## Datenmodell und Parser

Die FX-Domänen haben in `CnCNew.Data` kein typisiertes Schema; der Compiler hält die Felder heuristisch getypt, bewahrt aber
den Originaltext (`DefinitionValue.OriginalText`). `FxValueReader` wendet darauf die Parsefunktionen der Referenz an
(`INI::scanReal` mit Zahlpräfix, `scanBool`, `parseCoord3D`, `parseRGBColor` mit Division durch 255, `parseIndexList`
ohne Groß-/Kleinschreibung, `parseDurationUnsignedInt` mit Aufrunden in Single-Precision, `parseVelocityReal` pro
Logikframe, `parseAngleReal`, `parsePercentToReal`). Unbekannte Nuggets, Schlüssel und Enumwerte sowie ungültige Werte werden
nie ersetzt, sondern als `FxDiagnostic` gemeldet.

Wiederholte `FXList`-Blöcke: Der Compiler hängt die Nuggets an, `FXListStore::parseFXListDefinition` (FXList.cpp) leert die
Liste dagegen vor dem Parsen. `FxCatalog` behält deshalb nur die Abschnitte des letzten Blocks (gleiche Datei, nach dessen
Deklarationszeile). Betroffen im Retail-Bestand: `FX_GattlingExplosionOneFinal` und `Nuke_WeaponFX_NukeCannon`.

Namen werden wie überall in `CnCNew.Data` ohne Beachtung der Groß-/Kleinschreibung aufgelöst. Die Referenz
(`NameKeyGenerator::nameToKey`, Template-Map des `ParticleSystemManager`) vergleicht exakt; jede nur so auflösbare Referenz
wird als `case-mismatch` gemeldet (Retail: 0).

### Nuggets (`FXList.cpp TheFXListFieldParse`), gemessen mit `fx-audit`

| Nugget | Anzahl Retail | Record | Wirkung |
|---|---:|---|---|
| `ParticleSystem` | 860 | `ParticleSystemFxNugget` | Partikelsysteme, `Count`, `Offset` (mit Objektmatrix gedreht), `Radius`/`Height`/`InitialDelay` als Zufallsvariable, `RotateX/Y/Z`, `OrientToObject`, `Ricochet`, `AttachToObject`, `CreateAtGroundHeight`, `UseCallersRadius` |
| `Sound` | 320 | `SoundFxNugget` | `SoundCue` (Effect) an der Primärposition |
| `ViewShake` | 139 | `ViewShakeFxNugget` | `ViewShakeRequest` (SUBTLE … CINE_INSANE) |
| `LightPulse` | 65 | `LightPulseFxNugget` | `LightPulseInstance` |
| `TerrainScorch` | 46 | `TerrainScorchFxNugget` | Scorch-Decal (SCORCH_1..4, SHADOW_SCORCH, RANDOM) |
| `Tracer` | 18 | `TracerFxNugget` | `TracerInstance` |
| `FXListAtBonePos` | 5 | `FxListAtBonePosFxNugget` | FXList an Knochen `Name`, `Name01`, `Name02`, … |
| `RayEffect` | 0 | `RayEffectFxNugget` | `RayEffectRequest` (im Retail-Bestand ungenutzt, implementiert) |

Retail (`--maps exclude`): 429 FXLists mit 1.453 Nuggets, 1.087 Partikelsysteme (1.052 PARTICLE, 30 STREAK,
4 VOLUME_PARTICLE, 1 DRAWABLE; Shader 589 ADDITIVE, 470 ALPHA, 26 ALPHA_TEST, 1 MULTIPLY, 1 NONE), 60 mit `SlaveSystem`,
119 mit `PerParticleAttachedSystem`, 363 Waffen (284 mit FireFX/ProjectileDetonationFX, 221 mit FireSound),
11 DamageFX-Tabellen, 1.079 Objekte mit Standard-DamageFX.

### Partikelsystem-Vorlagen (`ParticleSys.cpp ParticleSystemTemplate::m_fieldParseTable`)

`ParticleSystemTemplate` typisiert alle 62 Schlüssel: Priorität (13 Stufen), Shader, Typ, Textur-/Drawablename, Winkel,
Winkelgeschwindigkeit, Dämpfungen, Schwerkraft, Slave-System und -Offset, Per-Particle-Attached-System, Lebensdauer
(Partikel und System, in Frames), Größe, Start-Größenrate, Größenrate und -dämpfung, acht Alpha- und acht Farb-Keyframes,
ColorScale, Burst-Verzögerung und -Anzahl, Initialverzögerung, Driftgeschwindigkeit, Geschwindigkeitstyp (ORTHO, SPHERICAL,
HEMISPHERICAL, CYLINDRICAL, OUTWARD), Volumentyp (POINT, LINE, BOX, SPHERE, CYLINDER), `IsHollow`, `IsGroundAligned`,
`IsEmitAboveGroundOnly`, `IsParticleUpTowardsEmitter` und die Windparameter. Standardwerte folgen
`ParticleSystemInfo::ParticleSystemInfo`; Zufallsvariablen ohne Schlüssel sind wie in der Referenz null-initialisiert.

Geschwindigkeits- und Volumenparameter liegen wie in der Referenz in Unions (`m_emissionVelocity`, `m_emissionVolume`);
`VelocitySlots`/`VolumeSlots` bilden das exakt ab (letzter Schreibzugriff gewinnt), benannte Accessoren lesen sie.

`GameClientRandomVariable` (`RandomValue.cpp getValue`, `INI::parseGameClientRandomVariable`): `low high [Verteilung]`,
ohne Verteilung UNIFORM. CONSTANT mit `low == high` liefert `low` ohne Zufallszug, CONSTANT mit `low != high` verhält sich
wie UNIFORM, UNIFORM zieht auch bei `low == high`. GAUSSIAN, TRIANGULAR, LOW_BIAS und HIGH_BIAS sind in der Referenz nicht
implementiert und liefern 0; der Parser meldet sie als unerklärte Diagnose (Retail: keine).

## Partikelsimulation (`ParticleWorld`)

Neu geschrieben nach `ParticleSystem::update`, `Particle::update`, `generateParticleInfo`, `computeParticlePosition`,
`computeParticleVelocity`, `computePointOnUnitSphere`, `mergeRelatedParticleSystems`, `updateWindMotion`,
`Particle::doWindMotion`, `Particle::isInvisible`, `ParticleSystemManager::update/createParticle/removeOldestParticles`
(alle `ParticleSys.cpp` der EA-Quellen, GeneralsMD):

- Ein `Step` ist ein Logikframe (1/30 s); die Referenz aktualisiert Partikel einmal pro Logikframe
  (`ParticleSystemManager::update` prüft `m_lastLogicFrameUpdate`).
- Systeme laufen in Erzeugungsreihenfolge; ein Slave wird wie im Konstruktor der Referenz vor seinem Master eingereiht,
  während eines Steps erzeugte Systeme (Per-Particle-Attached) werden im selben Step aktualisiert.
- Initialverzögerung hält das ganze System an (auch die Partikelaktualisierung); danach Wind, Transformation (Objekt
  angehängt: Objektmatrix mal lokaler Matrix; von einem Partikel gesteuert: dessen Position), Emission von
  `(int)BurstCount` Partikeln mit Zwischenframe-Korrektur der Position, Slave-Partikel pro Masterpartikel, dann
  Partikelupdate: Beschleunigung (Schwerkraft), Dämpfung, Position plus Drift, Wind, Winkel, Größe, Alpha (nicht bei
  ADDITIVE; beim Erreichen eines Keyframes wird auf den Keywert gesetzt), Farbe (ohne Einrasten, danach ColorScale),
  Lebensdauer, Unsichtbarkeitstest je Shader.
- Die Farbklemmung übernimmt bewusst den Tippfehler der Referenz (`if (m_color.red < 0) m_color.green = 0`), damit
  Tönungen identisch bleiben.
- Positionen und Geschwindigkeiten werden mit `0.5 + ParticleScale/2`, Größen mit `ParticleScale` skaliert.
- Der hohle BOX-Fall von `computeParticlePosition` fällt in der Referenz ohne `break` in den LINE-Fall durch; das
  Ergebnis liegt deshalb auf der Unionslinie (Halbgröße bis Linienende). Nachgebildet, einschließlich der Zufallszüge.
- `mergeRelatedParticleSystems` addiert den `SlavePosOffset` der Slave-Vorlage (nicht des Masters); alle 60 Retail-Werte
  sind ohnehin null.

Abweichungen (bewusst, dokumentiert):

- Stirbt ein Master, zerstört `ParticleWorld` den Slave. `~ParticleSystem` entkoppelt ihn nur, worauf er in der Referenz
  selbst am Weltursprung emittieren würde.
- OUTWARD mit Volumen NONE lässt in der Referenz ein statisches Ergebnis unverändert (veralteter Wert); hier Null.
- Negative Stichproben bei `(UnsignedInt)`-Casts (undefiniert in C++) werden 0.

### Budgets und Culling

`ParticleBudget`: `MaxParticles` (`GameData.ini MaxParticleCount`, Retail 2.500; `GlobalData`-Standard 0 = keine
Partikel), `MaxFieldParticles` (`MaxFieldParticleCount`, 30; gezählt werden sichtbare AREA_EFFECT-Partikel mit
`IsGroundAligned` des letzten `Capture`), `MinPriority` (LOD-Mindestpriorität aus `GameLOD.ini MinParticlePriority`;
die Detailstufe wählt der Aufrufer), `MaxParticlesPerSystem` 512 (`W3DParticleSystemManager::MAX_POINTS_PER_GROUP`,
hier als Simulationsgrenze) und `MaxSystems` 4.096 (eigene Sicherheitsgrenze). ALWAYS_RENDER ist von allen Zählgrenzen
ausgenommen. Ist das Gesamtbudget erreicht, wird das älteste Partikel der niedrigsten, echt kleineren Priorität verdrängt;
gibt es keines, wird das neue abgelehnt. Das glättet den Off-by-one von `removeOldestParticles` (dessen Rückgabewert
nach vollständigem Durchlauf nie der angeforderten Zahl entspricht). Alle Ablehnungen und Verdrängungen stehen in
`ParticleWorldStats`.

`Capture(isVisible)` ist der Culling-Haken (Position, Größe); verworfene Partikel zählen in `ParticleFrame.Culled`.

### Ausgabe und Reihenfolge

`ParticleInstance(Position, Size, Color RGBA 0..1, Rotation, Batch, SystemId, Personality)`, sortiert nach System-Id
(Erzeugungsreihenfolge) und Personality (Erzeugungsreihenfolge im System). `ParticleBatchKey(Texture, Shader, Kind,
GroundAligned)` in Reihenfolge des ersten Auftretens. Renderhinweise aus `W3DParticleSys.cpp`: ADDITIVE, ALPHA,
ALPHA_TEST, MULTIPLY entsprechen den gleichnamigen Sprite-Presets; `GroundAligned` bedeutet kein Billboard
(`shouldBillboard`); STREAK-Systeme mit mindestens zwei Partikeln werden als Linienzug in Instanzreihenfolge gezeichnet;
die Referenz quantisiert den Winkel auf 8 Bit.

### Determinismus

Alle Zufallswerte stammen aus `FxRandom` (SplitMix64, Seed vom Aufrufer, `NextUnit` mit 24 Bit, Grenzen inklusive wie
`GameClientRandomValueReal`/`GameClientRandomValue`). Keine Wandzeit, kein `Random.Shared`, keine Hash-Reihenfolge in
Ausgaben (Listen in Erzeugungsreihenfolge, sortierte Wörterbücher). Zwei Läufe mit gleichem Seed sind byteidentisch
(Test und `fx-audit`-Prüfsumme); die Garantie gilt für dieselbe Plattform/Laufzeit, da `MathF.Sin/Cos/Acos` die CRT nutzen.

## FX-Ausführung (`FxExecutor`)

- `FXList::doFXPos`/`doFXObj` getrennt: Objektform für `FxRequested` mit `Value == 1` (OrientToObject der Die-Module),
  DamageFX und Knochen; Positionsform für FireFX, Detonationseffekte und `FxRequested` mit `Value == 0` an der Objektpose.
  Shroud wird über `IFxWorldView.IsShrouded` geprüft (Adapter: noch kein Shroudmodell, immer sichtbar).
- `WeaponFired`: FireFX des Veteranenlevels an der Waffenmündung (`GetWeaponMuzzle`), sonst an der Quelle, bei
  Kontaktwaffen (`AttackRange - 2.5 < 10`, `WeaponTemplate::isContactWeapon` mit `RATIONALIZE_ATTACK_RANGE`) am Ziel;
  Geschwindigkeit = Waffengeschwindigkeit, Sekundärposition = Ziel, Radius = PrimaryDamageRadius. Da die Simulation
  Schüsse sofort auflöst (noch keine Projektilobjekte), spielt `ProjectileDetonationFX` sofort am Ziel (gezählt als
  `projectile-detonation-immediate`).
- `EntityDamaged`: `ActiveBody::doDamageFX` mit dem DamageFX der bedingungslosen ArmorSet (`Conditions = None`); der
  Schadenstyp kommt aus dem `WeaponFired` desselben Ticks (Paar Quelle/Ziel, sonst Quelle), das Veteranenlevel von der
  Quelle, Major bei `Schaden >= AmountForMajorFX`, Drosselung pro Opfer und Schadenstyp mit `ThrottleTime` in Ticks.
  Ohne Schuss im selben Tick ist der Typ unbekannt (`damagefx-without-weapon`).
- `ParticleSystem`: `reallyDoFX` mit Offset × Matrix, Zufallsradius/-winkel, Höhe oder Bodenhöhe, lokaler Drehung,
  Anhängen an das Objekt, Initialverzögerung in Frames (`ceil(ms · 30/1000)`), `UseCallersRadius` für SPHERE/CYLINDER;
  `Ricochet` richtet vom Verursacher weg aus.
- `Tracer` (`TracerFXNugget::doFXPos`, `W3DTracerDraw`): Wahrscheinlichkeitszug, Lebensdauer
  `ceil((Distanz − Länge)/Geschwindigkeit · DecayAt)` Frames, Deckkraft fällt linear auf 0, Kopf wandert je Frame um die
  Geschwindigkeit. Geschwindigkeit 0 (Division durch null in der Referenz) ergibt einen Frame.
- `LightPulse` (`W3DDisplay::createLightPulse`, `W3DDynamicLight::On_Frame_Update`): Innenradius 1, Außenradius
  `1 + Radius`; unter `2 · 10 + 1` wird wie in der Referenz kein Licht erzeugt (Retail-Durchlauf: 52 von 130
  Ausführungen in beiden Formen); Anstieg/Abklingen in Frames; höchstens 64 gleichzeitige Lichter (eigene Grenze, älteste zuerst).
- `ViewShake` (`W3DView::shake`, `updateView`): Anforderungen gehen an die Kamera; `CameraShake` addiert
  `Intensität · (1 − Distanz/MaxShakeRange)` (2D, nichts jenseits der Reichweite) in zufälliger Richtung, dämpft je Frame
  mit 0,75, kehrt die Richtung um und stoppt unter 0,01. Übersteigt die Summe `MaxShakeIntensity`, setzt die Referenz sie
  auf 3,0 zurück – übernommen.
- `TerrainScorch` (`BaseHeightMapRenderObjClass::addScorch/updateScorches`): Decal mit Atlas `EXScorch01.tga`
  (3×3 Zellen im Abstand 1,5 auf 4 Einheiten), RANDOM = SCORCH_1..4, Ringpuffer 500, Duplikat bei gleichem Typ und
  Abweichung unter einem Viertel des Radius. Die Terrain-Einfärbung und das Anschmiegen an das Höhenfeld macht der
  Renderer.
- `FXListAtBonePos`: Knochen aus `IFxWorldView.GetBonePosition` (Objektschicht C2a) wie
  `W3DModelDraw::getCurrentBonePositions` (erst der Name, dann `Name01` bis zum ersten fehlenden, höchstens 40); die
  Knochenmatrix fehlt, stattdessen dient die Objektdrehung (`OrientToBone` wird auch von der Referenz nicht ausgewertet).
  Die Positionsform ist wie in der Referenz wirkungslos (`bonepos-without-object`).
- Decals dieses Pakets sind ausschließlich Scorches und explizite FX-Decals (`DecalBuffer.Add` mit Lebensdauer); Gebäude-
  schatten, Radius- und Auswahldecals gehören der Objekt- beziehungsweise Eingabeschicht.

Alle übersprungenen oder angenäherten Fälle zählt `FxExecutor.Counters` (sortiert).

## Soundcues

`SoundCue(Tick, EventName, Kind, Position?, Entity, Player, Volume, Priority, Origin)` mit `Kind` Effect, Voice, Eva,
Music oder UiSound. Lautstärke und Priorität stammen aus dem `AudioEvent` (`Volume` in Prozent, `Priority`
LOWEST..CRITICAL), sonst 1 und NORMAL. Die Audio-Schicht löst die Namen auf und entscheidet über die Hörbarkeit für den
lokalen Spieler; `Player` ist der Spieler, den die Referenz adressiert. `NoSound` und leere Namen erzeugen keinen Cue.

| Ereignis | Cue | Referenz |
|---|---|---|
| FXList-`Sound` | Effect an der Primärposition | `SoundFXNugget` |
| `WeaponFired` | Waffen-`FireSound` (Effect, Quelle) | `FiringTracker::shotFired` |
| `VoiceFearRequested` | Objekt-`VoiceFear` (Voice) | `ActiveBody::attemptDamage` |
| `ProductionCompleted` + `EntitySpawned` im selben Tick | `VoiceCreated` und `UnitSpecificSounds.VoiceCreate` (Voice) | `ProductionUpdate::update` |
| `ConstructionCompleted` | `VoiceTaskComplete` des Bauers (Voice) | `DozerAIUpdate` |
| `ConstructionStarted` | MiscAudio `MoneyWithdrawSound` (UiSound) | `Money::withdraw` |
| `SupplyDeposited`, `StructureSold`, `ConstructionCancelled` | MiscAudio `MoneyDepositSound` (UiSound) | `Money::deposit` |
| `UpgradeCompleted` | `ResearchSound`, sonst EVA `UpgradeComplete`; zusätzlich `UnitSpecificSound` | `ProductionUpdate::update` |
| `SpecialPowerUsed` | `InitiateSound` an der Quelle, `InitiateAtLocationSound` am Zielobjekt | `SpecialPowerModule::aboutToDoSpecialPower` |
| `EntityContained` / `EntityUncontained` | `SoundEnter` / `SoundExit` des Containers | `OpenContain::onContaining/onRemoving` |
| `EntityDied` (nicht selbst verursacht) | EVA `BuildingLost` (STRUCTURE + MP_COUNT_FOR_VICTORY) oder `UnitLost` (INFANTRY/VEHICLE) | `Object::onDie` |
| `EntityDamaged` durch Fremde | EVA `BaseUnderAttack` + Radar-Strukturton, Einheiten Radar-Struktur-/Harvesterton, sonst allgemeiner Radarton; je Spieler höchstens einmal pro 250 Einheiten und 10 s | `Radar::tryUnderAttackEvent`, `Radar::tryEvent` |
| `CommandRejected` | MiscAudio `NoCanDoSound` (eigene Regel) | – |
| `CaptureFlash` | MiscAudio `DefectorTimerTickSound` als Weltklang an der Position des Gebäudes, das gerade eingenommen wird (kein Spielerindex); fehlt der Schlüssel, trägt der Cue den Schlüsselnamen und wird von der Audioschicht als unbekanntes Event gezählt | `SpecialAbilityUpdate::continuePreparation` (`SpecialAbilityUpdate.cpp`) |

Der Radar-Abstandstest der Referenz hat einen Klammerfehler; implementiert ist der beabsichtigte Abstand. Die Referenz
spielt für angegriffene Nicht-Harvester-Einheiten den Strukturton; übernommen.

EVA-Ereignisse aus `Eva.ini`, die die Simulation noch nicht auslöst: `LowPower`, `InsufficientFunds` (entsteht in der
Befehlsschicht), alle `Superweapon*`-Ereignisse, `BeaconDetected`, `Enemy/Own*Detected` (Helden), `AllyUnderAttack`
(braucht den lokalen Spieler und Bündnisse), `GeneralLevelUp`, `VehicleStolen`, `BuildingStolen`, `BuildingBeingStolen`,
`BuildingSabotaged`, `CashStolen`. Ebenfalls nicht ableitbar: `InitiateAtLocationSound` bei Ortszielen (das Ereignis
trägt keinen Ort), Produktionsstart-Abbuchungen (kein Ereignis), Einheitenansagen bei Befehlen (`VoiceMove`,
`VoiceAttack`, `VoiceSelect` – Aufgabe der Eingabeschicht C3 anhand von `ObjectFxInfo.Sounds`).

`SoundCueRateLimiter(SoundCueLimits)`: je Ereignisname höchstens `MaxPerEventName` Cues pro `WindowTicks` (Standard 3
pro 10 Ticks), höchstens `MaxPerTick` (48) pro Tick, positionierte Effect-/Voice-Cues jenseits `CutoffDistance` (2D,
0 = aus) vom Hörer entfallen. Rein von Reihenfolge und Ticks abhängig; Zähler für jede Verwerfungsart.

## Audit (`PresentationTool fx-audit <content-dir>`)

Kompiliert den Retail-Bestand (`--maps exclude`), baut den Katalog und gibt deterministisch aus: Anzahlen der FXLists,
Nuggets je Art, Partikelsysteme je Typ, Shader und Priorität, Texturen (Name, dann `.dds`, dann `.tga` unter
`Art/Textures/`; Smudge-Systeme ausgenommen, weil der Hitzeschleier-Renderer die Textur nie lädt), fehlende FXLists aus
Waffen, DamageFX und FXListAtBonePos, fehlende Partikelsysteme, unaufgelöste Graphreferenzen auf FXLists, alle
Diagnosen nach Code (erklärt mit Begründung beziehungsweise UNERKLÄRT) und einen Ausführungsdurchlauf. Exitcode 1 bei
unerklärten Diagnosen.

Stand Retail (gemessen): 0 unerklärte, 13 erklärte Diagnosen:

- `fxlist-redefined` 2 (siehe oben);
- `missing-particle-system` 6: `BattleMasterTransitionDebrisSmall` (2×), `BuggyTurretExplosion`, `CraterDebris`,
  `FireFieldSmall`, `BlackFireFieldSmall` – `reallyDoFX` tut in der Release-Referenz nichts;
- `missing-attached-system` 1: `ScudMissleLauncherSmoke` (Schreibfehler im Bestand);
- `particle-shader-none`/`particle-drawable-type` je 1: `CarCrushGlassDebris` (DRAWABLE, Shader NONE – in der Referenz
  unsichtbar und vom W3D-Partikelrenderer übersprungen);
- `particle-velocity-none`/`particle-volume-none` je 1: `TankStruckFlameSlave` (als Slave ohnehin ohne eigene Emission).

Fehlende Texturen (5, erklärt: WW3D2 `missingtexture.cpp` ersetzt sie, die Partikel werden trotzdem gezeichnet):
`EXDots.tga`, `EXRedSmokePuff.tga`, `EXSateliteHack1.tga`, `EXthunderRadar.tga`, `EXthunderWave.tga`. 74 von 79
Texturnamen lösen nur über die `.dds`-Ersetzung auf. Keine Waffe und keine DamageFX-Tabelle verweist auf eine fehlende
FXList. Die drei unaufgelösten Graphreferenzen auf FXLists (`DieByFireUSA`, `FINAL` 2×) sind Typisierungsreste von
Modulfeldern im Datenlayer und gehören zu dessen Referenzinventur.

Ausführungsdurchlauf (`FxAudit.ExecuteAll`, Seed 1): alle 429 FXLists in Objekt- und Positionsform, alle 1.087
Partikelsysteme einzeln, 1.816 Frames, Spitze 2.509 Partikel (davon ALWAYS_RENDER über dem Budget), 141.780 erzeugte
Partikel, 612 Soundcues, Prüfsumme `4269BF8DD70C4247`, zwei Läufe identisch. Laufzeit im Realdatentest etwa 3 s
(1,6–1,9 ms pro Frame bei voll ausgeschöpftem Budget, Debug-Build).

## Tests

`tests/CnCNew.Presentation.Tests` (`FxTests`): Zufallssemantik, synthetisches Parsen aller Nuggetarten und
Vorlagenfelder, unerklärte Diagnosen, handgerechnete Keyframes (Alpha 0→1→0 über 10/20 Frames, Farbe, Größe mit
Dämpfung, Bewegung mit Schwerkraft/Dämpfung/Drift), Emission/Volumina/Lebensdauer/Initialverzögerung, Slave und
Attached-Systeme, alle Budgets, Byte-Determinismus, Executor für alle Nuggets und Ereignisse inklusive DamageFX-Drosselung,
Kamerawackeln, Decalpuffer, Soundcue-Regeln und Ratenbegrenzer. Mit `--content-dir` zusätzlich der Realdatentest
(0 unerklärte Diagnosen, jede Definition im Katalog, Audit sauber, Durchlauf zweimal identisch).

## Grenzen

- DRAWABLE-Partikel (1 System) werden als Instanzen mit `ParticleKind.Drawable` ausgegeben; die Referenz zeichnet sie
  nicht. SMUDGE-Texturen (`SMUD…`, 2 Systeme) brauchen einen Hitzeschleier-Renderer (Opazität aus Alpha, Größe);
  VOLUME_PARTICLE (4) rendert die Referenz mit Tiefe 1, also wie normale Partikel.
- Keine Knochenmatrizen: Mündungen und Knochen liefert die Objektschicht als Positionen; Orientierung = Objekt.
- Shroud/Nebel: nur als Haken; `SimulationFxWorldView.IsShrouded` ist immer falsch. Der Shroudtest für angehängte
  Systeme beim Emittieren fehlt.
- DamageFX nutzt nur die bedingungslose ArmorSet; `DamageFXOverride` und Upgrade-abhängige ArmorSets fehlen, weil das
  Ereignis weder Schadenstyp noch ArmorSet trägt.
- Projektile sind in der Simulation sofortig; Detonationseffekte und Tracer haben keine echte Flugzeit.
- `FireSoundLoopTime` (36 Waffen, `FiringTracker::shotFired`: Schleifenton statt Einzelton) wird nicht nachgebildet;
  jeder Schuss erzeugt einen Cue, den `SoundCueRateLimiter` drosselt.
- `GameLOD.ini`-Stufen (MaxParticleCount 500/1.500/3.000, MinParticlePriority, ParticleSkipMask) werden nicht gelesen;
  der Aufrufer setzt `ParticleBudget` passend zur gewählten Detailstufe.
- Keine neuen NuGet-Abhängigkeiten (nur BCL, `System.Numerics`).
