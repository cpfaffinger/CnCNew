# Objektdarstellung: Modellzustände, Zeichenspezifikationen, Frame-Projektor (Paket C2a)

Dieses Dokument beschreibt `src/CnCNew.Presentation/Objects/**`: die rendererneutrale Schicht, die aus dem
`CompiledDefinitionGraph` und dem laufenden `SimulationWorld` die Frage beantwortet, *welches Modell mit welcher
Animation wo* gezeichnet wird. Sie kennt weder Stride noch W3D-Dateien; Dateiexistenz prüft nur das Werkzeug
(`PresentationTool object-audit`) über `ContentFileSystem.Exists`.

Abhängigkeiten: `CnCNew.Presentation` → `CnCNew.Data`, `CnCNew.Simulation` (öffentliche API plus die additive
Leseansicht `src/CnCNew.Simulation/Views/EntityPresentationView.cs`). Gleitkomma ist hier erlaubt; die Simulation
bleibt Festkomma und unverändert.

## 1. Öffentliche Einstiegspunkte

| Typ | Aufgabe |
|---|---|
| `ModelConditions`, `ModelConditionFlag`, `ModelConditionSet` | Flagtabelle (117 Einträge, stabile Ids), Parser, 128-Bit-Menge |
| `DrawSpecCompiler.Compile(ModuleConfiguration, diagnostics)` | ein `Draw`-Modul → `DrawSpec` |
| `ObjectVisualCatalog.Build(graph)` | `ObjectVisual` je Objektvorlage (Zeichenmodule, Maßstab, Schatten, Auswahlradius, `KindOf`, `ModelConditionUpgrade`s) plus alle Diagnosen |
| `BestStateSelector.FindBest(spec, conditions)` / `FindTransition` | Zustandsauswahl nach `findBestInfo`, Übergangszustände |
| `ModelStateTracker` | optionale Clientlogik für Übergänge und „warten bis fertig“ |
| `ConditionDeriver.Derive(entity, world, visual, context)` | Flags aus dem Simulationszustand |
| `FrameProjector` (`Advance(world)` je Tick, `Project(alpha)` je Frame) → `RenderFrame` | interpolierte, nebelgefilterte Zeichenliste |
| `StaticScenery.Build(mapObjects, catalog, groundHeight)` | Kartenobjekte als statische Kulisse |
| `TeamColors.Resolve(players, graph, overrides)` | Hausfarben |
| `ObjectAudit.Run(graph, fileExists)` | Bestandsaudit (vom Werkzeug genutzt) |

## 2. Modellzustandsflags

Quelle der Tabelle: `ModelState.h` (`ModelConditionFlagType`), Namen aus `BitFlags.cpp`
(`ModelConditionFlags::s_bitNameList`), Zero Hour, ohne `ALLOW_SURRENDER` (dort ist `SURRENDER` ausgeblendet; der
Retail-Bestand nutzt es nicht). Die Id ist die Tabellenposition und ändert sich nie. Namen werden wie von
`INI::scanIndexList` ohne Beachtung der Groß-/Kleinschreibung gesucht. Listen folgen `BitFlags::parse`
(`BitFlagsIO.h`): `NONE` leert und beendet die Liste, `+X`/`-X` ändern den Startwert, eine reine Namensliste ersetzt
ihn; Mischung und unbekannte Namen sind ausdrückliche Fehler (die Referenz wirft).

Spalte „Ableitung“: wie `ConditionDeriver` das Flag setzt, oder – bei „nicht modelliert“ – was die Simulation liefern
müsste. Die Referenz setzt die Bits imperativ an vielen Stellen; hier ist jedes Bit eine reine Funktion des Zustands.

| Id | Flag | Ableitung |
|---|---|---|
| 0 | TOPPLED | nicht modelliert: `ToppleUpdate` (Umfallrichtung/-zustand) |
| 1–2 | FRONTCRUSHED, BACKCRUSHED | nicht modelliert: Überrollschaden mit Trefferseite (`CrushDie`) |
| 3–5 | DAMAGED, REALLYDAMAGED, RUBBLE | genau eines je `BodyDamageType` des `ActiveBody` (`Drawable::onDamageStateChanged`, Schwellen `UnitDamagedThreshold`/`UnitReallyDamagedThreshold` aus `GameData.ini`) |
| 6 | SPECIAL_DAMAGED | nicht modelliert: Sonderschaden des `GarrisonContain` |
| 7–8 | NIGHT, SNOW | Aufrufer (`ConditionContext`, `FrameProjectorOptions.Night/Snow`) |
| 9 | PARACHUTING | Statusbit `PARACHUTING` (die Simulation setzt es noch nicht: `ParachuteContain` fehlt) |
| 10 | GARRISONED | eigener `GarrisonContain` mit mindestens einem Insassen |
| 11 | ENEMYNEAR | nicht modelliert: `EnemyNearUpdate` |
| 12–14 | WEAPONSET_VETERAN/ELITE/HERO | Veteranenstufe 1/2/3 (`Object::onVeterancyLevelChanged` → `setWeaponSetFlag`) |
| 15–16 | WEAPONSET_CRATEUPGRADE_ONE/TWO | nicht modelliert: Waffenkisten (`SalvageCrateCollide`) |
| 17 | WEAPONSET_PLAYER_UPGRADE | `Entity.HasPlayerUpgradeWeapon` (`WeaponSetUpgrade`) |
| 18–33 | DOOR_1..4_OPENING/CLOSING/WAITING_OPEN/WAITING_TO_CLOSE | nicht modelliert: Türzustände des `ProductionUpdate` beim Ausfahren eines Produkts |
| 34 | ATTACKING | Statusbit `IS_ATTACKING` (derzeit nie gesetzt: kein KI-Angriffszustand) |
| 35 | PREATTACK_A | nicht modelliert: `PreAttackDelay` |
| 36 | FIRING_A | `LastFireTick` ist der zuletzt simulierte Tick (`world.Tick - 1`) |
| 37 | BETWEEN_FIRING_SHOTS_A | `world.Tick < WeaponReadyTick` bei nicht leerem Magazin |
| 38 | RELOADING_A | `world.Tick < WeaponReadyTick` bei leerem Magazin |
| 39–46 | PREATTACK/FIRING/BETWEEN_FIRING_SHOTS/RELOADING _B und _C | nicht modelliert: die Simulation kennt nur die Primärwaffe |
| 47 | TURRET_ROTATE | `Entity.TurretOf(i).Rotating`: der Turm der Simulation dreht noch (`TurretAI::friend_turnTowardsAngle`, `docs/SIM-TURRET-AIM.md`) |
| 48 | POST_COLLAPSE | nicht modelliert: `StructureCollapseUpdate` |
| 49 | MOVING | `Speed ≠ 0` oder aktiver Bewegungsbefehl |
| 50 | DYING | `EffectivelyDead`, Körper ist kein `InactiveBody`, Objekt noch in der Welt |
| 51 | AWAITING_CONSTRUCTION | `UNDER_CONSTRUCTION` und `ConstructionWork == 0` |
| 52 | PARTIALLY_CONSTRUCTED | `UNDER_CONSTRUCTION` und `ConstructionWork > 0` |
| 53 | ACTIVELY_BEING_CONSTRUCTED | zusätzlich: Arbeit seit dem Vortick gestiegen (Projektor kennt den Vortick); ohne Vortick: der Bauer baut gerade |
| 54 | PRONE | nicht modelliert |
| 55 | FREEFALL | nicht modelliert: `ParachuteContain` |
| 56 | ACTIVELY_CONSTRUCTING | Näherung: `AIUpdateInterface.IsBuilding` und das Objekt steht; die Simulation legt Ziel/Anmarsch nicht offen |
| 57 | CONSTRUCTION_COMPLETE | nicht modelliert: `ProductionUpdate` setzt es beim Ausgang eines fertigen Produkts |
| 58–59 | RADAR_EXTENDING, RADAR_UPGRADED | `RadarUpdate.RadarActive` und `IsExtendComplete` |
| 60 | PANICKING | nicht modelliert |
| 61, 63 | AFLAME, BURNED | Statusbits `AFLAME`, `BURNED` |
| 62 | SMOLDERING | nicht modelliert: Zwischenzustand von `FlammableUpdate` ist nicht öffentlich |
| 64 | DOCKING | `AIUpdateInterface.IsDocking` |
| 65–67 | DOCKING_BEGINNING/ACTIVE/ENDING | nicht modelliert: Phase der `DockMachine` ist nicht öffentlich |
| 68 | CARRYING | getragene Versorgungskisten > 0 (`EntityPresentationView.CarriedSupplyBoxes`) |
| 69 | FLOODED | nicht modelliert |
| 70 | LOADED | eigener Container, der kein `GarrisonContain` ist (`TransportContain`), mit Insassen |
| 71–72 | JETAFTERBURNER, JETEXHAUST | nicht modelliert: `JetAIUpdate` |
| 73–74 | PACKING, UNPACKING | nicht modelliert: `DeployStyleAIUpdate`, `SpecialAbilityUpdate` |
| 75 | DEPLOYED | Statusbit `DEPLOYED` |
| 76 | OVER_WATER | nicht modelliert: Lokomotorzustand über Wasser |
| 77, 82 | POWER_PLANT_UPGRADED, POWER_PLANT_UPGRADING | `PowerPlantUpdate.Extended` und `IsExtendComplete` |
| 78 | CLIMBING | nicht modelliert |
| 79 | SOLD | Statusbit `SOLD` |
| 80–81 | RAPPELLING, ARMED | nicht modelliert |
| 83 | SPECIAL_CHEERING | nicht modelliert |
| 84–86 | CONTINUOUS_FIRE_SLOW/MEAN/FAST | nicht modelliert |
| 87–88 | RAISING_FLAG, CAPTURED | nicht modelliert |
| 89–91 | EXPLODED_FLAILING, EXPLODED_BOUNCING, SPLATTED | nicht modelliert: Todesphysik |
| 92 | USING_WEAPON_A | zusammen mit FIRING_A, BETWEEN_FIRING_SHOTS_A, RELOADING_A (`WeaponSet::getModelConditionForWeaponSlot`) |
| 93–94 | USING_WEAPON_B, USING_WEAPON_C | nicht modelliert (nur Primärwaffe) |
| 95 | PREORDER | nicht modelliert |
| 96–99 | CENTER_TO_LEFT, LEFT_TO_CENTER, CENTER_TO_RIGHT, RIGHT_TO_CENTER | nicht modelliert |
| 100–107 | RIDER1..RIDER8 | Statusbits `STATUS_RIDER1..8` |
| 108–109 | STUNNED_FLAILING, STUNNED | nicht modelliert |
| 110 | SECOND_LIFE | nicht modelliert: `UndeadBody` |
| 111 | JAMMED | nicht modelliert |
| 112–113 | ARMORSET_CRATEUPGRADE_ONE/TWO | nicht modelliert |
| 114–115 | USER_1, USER_2 | über `ModelConditionUpgrade` (siehe unten) |
| 116 | DISGUISED | Statusbit `DISGUISED` |

`ModelConditionUpgrade` (`ModelConditionUpgrade.cpp`) kann jedes Flag setzen; der Katalog liest `ConditionFlag`,
`TriggeredBy`, `ConflictsWith`, `RequiresAllTriggers` aus dem Graphen. Aktiv ist es, wenn die Upgrademaske des
Objekts (`world.UpgradeKeyMaskOf`) die Auslöser erfüllt und keine Konfliktbits trägt. Grenze: das `Executed`-Gedächtnis
der Referenz (`UpgradeMux`) liegt geschützt im Simulationsmodul; entfernte Upgrades wirken hier sofort.

Wiederholt: Die Simulation müsste für die fehlenden Flags liefern: Türphasen der Produktion, zweite/dritte Waffe mit
eigenem Zustand, Vorbereitungszeit und Dauerfeuer, Turmwinkel, Dockphase, Fallschirm-/Freifall-, Topple-, Crush-,
Einsturz- und Todesphysikzustände, Jet-/Deploy-/Pack-Zustände, Wasser-/Kletterzustand, Kistenupgrades,
`SMOLDERING`, Stun/Jam/Second-Life sowie `CONSTRUCTION_COMPLETE` am Produzenten.

## 3. Zeichenmodule und `DrawSpec`

Der Datencompiler katalogisiert die 18 Retail-Zeichenmodule, typisiert sie aber nicht: Schlüssel und Blöcke kommen als
generische Felder/`DefinitionSection`s. `DrawSpecCompiler` liest den Originaltext (`DefinitionValue.OriginalText`) mit
der Referenzgrammatik neu und spielt Modulschlüssel und Blöcke in Quellreihenfolge (Zeile, Spalte) ab, weil
`AliasConditionState` sich auf den zuvor deklarierten Zustand bezieht.

Schlüsseltabellen (`DrawKeyCatalog`, abgeschrieben aus den `buildFieldParse`-Tabellen):

| Modul | Art | Schlüssel über `W3DModelDraw` hinaus |
|---|---|---|
| `W3DModelDraw`, `W3DOverlordAircraftDraw` | Model | – |
| `W3DTankDraw`, `W3DOverlordTankDraw` | Model | `TreadDebrisLeft/Right`, `TreadAnimationRate`, `TreadPivotSpeedFraction`, `TreadDriveSpeedFraction` |
| `W3DTruckDraw`, `W3DOverlordTruckDraw`, `W3DPoliceCarDraw` | Model | Staub/Spritzer-Partikel, zwölf Reifenknochen, Reifen-/Kabinen-/Anhängerfaktoren (`W3DTruckDraw.cpp`) |
| `W3DTankTruckDraw` | Model | Truck-Schlüssel ohne Mittelreifen/Kabine/Anhänger plus Kettenschlüssel |
| `W3DDependencyModelDraw` | Model | `AttachToBoneInContainer` |
| `W3DSupplyDraw` | Model | `SupplyBonePrefix` |
| `W3DScienceModelDraw` | Model | `RequiredScience` |
| `W3DTreeDraw` | Tree | `ModelName`, `TextureName`, Wiegen/Umfallen (18 Schlüssel) |
| `W3DPropDraw` | Prop | `ModelName` (nicht im Retail-Bestand und nicht im Modulkatalog) |
| `W3DLaserDraw` | Laser | 14 Strahlschlüssel |
| `W3DProjectileStreamDraw` | ProjectileStream | `Texture`, `Width`, `TileFactor`, `ScrollRate`, `MaxSegments` |
| `W3DRopeDraw`, `W3DDebrisDraw`, `W3DTracerDraw`, `W3DDefaultDraw` | – | keine (Basis-`ModuleData`) |

`W3DModelDraw`-Modulschlüssel: `InitialRecoilSpeed`, `MaxRecoilDistance`, `RecoilDamping`, `RecoilSettleSpeed`,
`OkToChangeModelColor`, `AnimationsRequirePower`, `ParticlesAttachedToAnimatedBones`, `MinLODRequired`,
`ProjectileBoneFeedbackEnabledSlots`, `TrackMarks`, `ExtraPublicBone`, `AttachToBoneInAnotherModule`,
`IgnoreConditionStates`, `ReceivesDynamicLights` sowie die Blöcke `DefaultConditionState`, `ConditionState`,
`TransitionState` und der Schlüssel `AliasConditionState`. Zustandsschlüssel (`parseConditionState`): `Model`,
`Turret`, `TurretArtAngle`, `TurretPitch`, `TurretArtPitch`, `AltTurret…` (4), `ShowSubObject`, `HideSubObject`,
`WeaponFireFXBone`, `WeaponRecoilBone`, `WeaponMuzzleFlash`, `WeaponLaunchBone`, `WeaponHideShowBone`, `Animation`,
`IdleAnimation`, `AnimationMode`, `TransitionKey`, `WaitForStateToFinishIfPossible`, `Flags`, `ParticleSysBone`,
`AnimationSpeedFactorRange`. Typisiert werden, was die Präsentation braucht; übrige Modulschlüssel stehen als Text in
`DrawSpec.Settings` (Quellreihenfolge).

Semantik (`W3DModelDraw.cpp W3DModelDrawModuleData::parseConditionState`):

- `DefaultConditionState` muss der erste Zustand sein, höchstens einmal, ohne Argumente; er trägt die leere Bitmenge.
- Ein `ConditionState` beginnt als Kopie des Default-Zustands (Modell, Türme, Knochen, Sichtbarkeiten,
  Partikelknochen, Flags, Modus, Übergangsschlüssel). Die erste `Animation`/`IdleAnimation` ersetzt die kopierten
  Animationen (`ANIMS_COPIED_FROM_DEFAULT_STATE`), `ParticleSysBone` hängt an, `Show/HideSubObject` verschmilzt nach
  Namen, `Flags` ersetzt (reine Liste) oder ändert (`+/-`) wie `INI::parseBitString32`.
- **Eine Kopfzeile ist eine konjunktive Bitmenge** (`ConditionState = REALLYDAMAGED RUBBLE` verlangt beide Bits im
  Sinne der Bestauswahl). **Jede `AliasConditionState`-Zeile fügt dem vorherigen Zustand eine weitere Alternative hinzu**
  (`m_conditionsYesVec`); Alternativen sind „any-of“. Das ist geprüft an `parseConditionState` (`PARSE_ALIAS` hängt an
  `prevState.m_conditionsYesVec`, `PARSE_NORMAL` legt genau eine Menge an).
- `TransitionState = a b` kopiert den Default ohne Schlüssel und wird unter dem Paar (a, b) abgelegt (spätere Deklaration
  gewinnt wie `m_transitionMap[sig] = info`).
- Animationen `hierarchie.animation [Weg] [Wiederholungen]` (`parseAnimation`): Name klein, die Wiederholungszahl
  dupliziert den Eintrag; die Datei ist `<animation>.w3d` (`WW3DAssetManager::Get_HAnim`), die Hierarchie das Skelett.
- Werte, die mit `"` beginnen, werden wie `INI::getNextAsciiString` bis zum nächsten Anführungszeichen verbunden
  (`WeaponFireFXBone = TERTIARY "UIMOB03 R HAND"` ergibt den Knochen `uimob03 r hand`).
- Bäume und Requisiten erhalten einen synthetischen Default-Zustand mit ihrem `ModelName`, damit die Auswahl einheitlich ist.

Diagnosecodes (`DrawDiagnostic`): `unknown-draw-module`, `unknown-draw-key` (Modulschlüssel, fremde Blöcke,
`ConditionState/<Schlüssel>`), `unknown-condition`, `unknown-enum`, `invalid-value`, `state-rule` (die `throw
INI_INVALID_DATA`-Regeln der Referenz: doppelter/späterer Default, erster Zustand ohne Default nicht NONE, doppelte
Bedingungsmenge, Default neben NONE, Bit aus `IgnoreConditionStates`, fehlendes Modell, Idle- und Normalanimation
gemischt, falscher Modus für Idle-/Übergangsanimation, Übergang mit Schlüsseln oder gleichen Zuständen, Alias ohne
Vorgänger) sowie die Warnungen `ignored-tokens` (Tokens, die die Referenz überliest) und `animation-without-dot`.

Freigabeliste: `DrawKeyCatalog.AllowList` – jeder Eintrag nennt Modultyp, Schlüssel und Grund. Der Retail-Bestand
braucht keinen Eintrag; die Liste ist leer.

## 4. Zustandsauswahl

`BestStateSelector.FindBest` ist eine Neufassung von `W3DModelDrawModuleData::findBestInfo` und
`SparseMatchFinder::findBestInfoSlow` (`SparseMatchFinder.h`):

1. Bits aus `IgnoreConditionStates` werden aus der aktuellen Menge gelöscht.
2. Alle Zustände in Deklarationsreihenfolge, je Zustand die Alternativen von hinten nach vorn.
3. `yesMatch` = gesetzte Bits, die die Alternative verlangt (`countIntersection`); `extraneous` = verlangte Bits, die
   nicht gesetzt sind (`countInverseIntersection`).
4. Eine Alternative gewinnt bei `yesMatch > best` oder bei `yesMatch ≥ best` und `extraneous < bestExtraneous`
   (Start `0`/`999`). Volle Gleichstände behalten den früheren Kandidaten.

Das ist eine *dünnbesetzte* Suche: Ein Zustand kann gewinnen, obwohl er ein Bit verlangt, das nicht gesetzt ist
(`MOVING DAMAGED` passt auf `MOVING`, wenn es keinen reinen `MOVING`-Zustand gibt). Ohne Zustände liefert die Suche
`DefaultStateIndex` bzw. -1. Der Cache der Referenz (`m_bestMatches`) ist für das Ergebnis bedeutungslos und entfällt.

Übergänge (`W3DModelDraw::setModelState`): Wechselt ein realer Zustand mit `TransitionKey` in einen anderen mit
`TransitionKey`, spielt zuerst der `TransitionState` des Schlüsselpaars (`FindTransition`); trägt der neue Zustand
`WaitForStateToFinishIfPossible` gleich dem Übergangsschlüssel des laufenden Zustands und läuft dessen Animation noch,
wird er vorgemerkt. `ModelStateTracker` bildet das nach; die Animationszeit gehört dem Renderer, der Abschluss meldet
(`AnimationCompleted`). Der Frame-Projektor liefert nur den Zielzustand (keine „klebrigen“ Übergänge im Frame);
Übergänge sind Sache des Clients.

## 5. Frame-Projektor

Vertrag:

- `Advance(world)` wird genau einmal nach jedem `world.Step()` aufgerufen. Es erfasst je gezeichnetem Objekt Position,
  Gierwinkel (Radiant wie die Simulation), Besitzer, Gesundheit, Baufortschritt, Flags und den gewählten Zustand je
  Zeichenmodul. `Tick` ist der zuletzt simulierte Tick.
- `Project(alpha)` greift nicht auf die Welt zu: es interpoliert linear zwischen vorletzter und letzter Erfassung
  (Winkel über den kürzeren Bogen), `alpha` wird auf [0, 1] begrenzt. Neue Objekte erscheinen ohne Interpolation,
  Sprünge über `SnapDistance` (Standard 100) auch. Objekte, die die Welt nicht mehr hält, fehlen ab der nächsten
  Erfassung; sterbende Objekte bleiben mit `DYING`, solange die Simulation sie hält. Insassen werden nicht gezeichnet.
- Reihenfolge im `RenderFrame`: Objekte nach aufsteigender Id (Geister eingereiht), danach die Kulisse nach Index.
- `DrawableInstance` trägt `Visual` (Vorlage, Maßstab, Schatten, Auswahlradius, `KindOf`, `IsStructure`, `IsUnit`),
  `Selections` parallel zu `Visual.Draws`, Hausfarbe, `IsStatic`, `IsGhost`. `TurretYaw/TurretPitch` (und `AltTurret…`) kommen aus dem
  Turmzustand der Simulation (`Entity.TurretOf`, `docs/SIM-TURRET-AIM.md`) und sind `null` nur bei Objekten ohne Turm.
- Nebel (`FrameProjectorOptions.Viewer`): gezeichnet wird, was `world.EntitiesVisibleTo(viewer)` liefert (eigene,
  verbündete und sichtbare fremde Objekte). Fremde Gebäude (`STRUCTURE`, nicht eigen, nicht verbündet), die aus der Sicht
  fallen, bleiben mit ihrer letzten Erfassung als Geist (`IsGhost`), bis der Betrachter ihre Position wieder sieht –
  auch wenn sie inzwischen zerstört wurden (wie die Geisterobjekte der Referenz). Ohne Betrachter wird alles gezeichnet.

## 6. Kulisse aus Kartenobjekten

Regel von `StaticScenery.Build`: Jedes Kartenobjekt, das kein Wegpunkt und kein Straßen-/Brückenendpunkt ist, dessen
Vorlage existiert und dessen Zeichenmodule unter der leeren Bedingungsmenge ein Modell wählen, wird ein `SceneryItem`
(Position aus der Karte, Höhe über den optionalen `groundHeight`-Delegaten, sonst Karten-Z; Winkel der Karte in
Radiant; Maßstab der Vorlage). Unbekannte Vorlagen und Vorlagen ohne Modell werden gezählt (`SceneryReport`).

Die Simulation erzeugt manche Kartenobjekte selbst (derzeit die Versorgungsdepots in `SkirmishMatchSession`). Der
Projektor verdeckt ein Kulissenobjekt **dauerhaft**, sobald ein neu erfasstes Objekt derselben Vorlage innerhalb von
`FrameProjector.ClaimRadius` (1 Einheit) steht; stirbt das Objekt später, kehrt die Kulisse nicht zurück. Die Kulisse
ignoriert den Nebel wie das Gelände und sieht Nacht/Schnee aus den Projektoroptionen.

## 7. Hausfarben

`TeamColors.Resolve`: Überschreibung, sonst `PreferredColor` des `PlayerTemplate` der Fraktion (aus dem Graphen), sofern
noch kein früherer Spieler diese Farbe trägt, sonst der nächste freie Eintrag der eigenen Palette (acht Farben, nicht aus
den Spieldaten). System und unbekannte Besitzer: neutral.

## 8. `PresentationTool object-audit`

```powershell
dotnet run --project tools/CnCNew.PresentationTool -- object-audit game/content
```

Kompiliert die Retail-Basisdaten (`--maps exclude`-Äquivalent, strikt), baut den Katalog und schreibt deterministische
Zeilen (Zähler, Modultypen, Zustände, Animationsmodi, Diagnosen je Code/Schlüssel mit Fundstellen, Datei-Auflösung
unter `Art/W3D` mit fehlenden Namen, Turm-/Waffen-/Partikelknochen). Exitcode 1, wenn ein unbekannter Flagname oder ein
unbekannter Zeichenschlüssel nicht in der Freigabeliste steht.

Gemessen am 30. September 2026 (zwei Läufe byteidentisch, 247 Zeilen):

| Größe | Wert |
|---|---|
| Objekte / mit Zeichenmodul / mit Modell / mit mehreren Zeichenmodulen | 2.115 / 2.115 / 1.847 / 320 |
| Zeichenmodule | 2.928 (W3DModelDraw 2.328, W3DDefaultDraw 180, W3DTreeDraw 128, W3DTruckDraw 118, W3DTankDraw 75, W3DDependencyModelDraw 26, W3DLaserDraw 25, W3DOverlordAircraftDraw 12, W3DProjectileStreamDraw 7, W3DTankTruckDraw 7, W3DOverlordTankDraw 5, W3DOverlordTruckDraw 5, W3DSupplyDraw 5, W3DPoliceCarDraw 3, W3DDebrisDraw/W3DRopeDraw/W3DScienceModelDraw/W3DTracerDraw je 1) |
| Zustände / davon Default / Alias-Alternativen / Übergänge | 23.170 / 1.777 / 15.400 / 4.774 |
| Animationsmodi (Zustände und Übergänge) | ONCE 15.034, LOOP 7.508, MANUAL 2.748, ONCE_BACKWARDS 2.648, LOOP_BACKWARDS 6, LOOP_PINGPONG 0 |
| genutzte Flags | 106 von 117 |
| unbekannte Flagnamen / unbekannte Schlüssel / nicht klassifiziert | 0 / 0 / 0 |
| Regelverstöße | 45 × `missing-model` (siehe 9.) |
| Warnungen | 4 × `ignored-tokens` bei `TransitionState = UP_SNOW NIGHT DOWN_DEFAULT` (die Referenz liest nur `up_snow`/`night`) |
| Modelldateien | 6.041 verschieden, 5.970 vorhanden, 71 fehlen |
| Animationsdateien | 3.146 verschieden, 3.108 vorhanden, 38 fehlen |
| Skelette (Animationshierarchien) | 2.431 verschieden, 2.406 vorhanden, 25 fehlen |
| Zustände mit Turm / Objekte mit Turm / mit Zweitturm | 1.680 / 180 / 4 |
| Waffenknochen-Verwendungen (FX / Rückstoß / Mündungsfeuer / Abschuss / Ein-Aus) | 5.825 / 485 / 5.177 / 2.973 / 26 |
| Partikelknochen | 22.062 Einträge, 311 Knochen, 240 Partikelsysteme |
| `ModelConditionUpgrade`s | 55 |

Kulisse „Alpine Assault“ (Realdatentest): 1.702 Kartenobjekte → 588 Kulissenobjekte, 252 Wegpunkte, 786
Straßen-/Brückenpunkte, 76 ohne Modell, 0 unbekannt.

Die fehlenden Dateien (zum Beispiel `aiblocktile`, `avbattship_d`, Schnee-/Nachtvarianten einiger Zivilgebäude) fehlen
auch in den extrahierten Originalarchiven (`.local/assets/by-archive`, stichprobenartig geprüft); die Referenz
registriert solche Assets nur als fehlend (`WW3DAssetManager::Get_HAnim`, `Register_Missing`). Sie werden gelistet, nicht
als Fehler gewertet; der Renderer muss einen Zustand ohne ladbares Modell überspringen.

## 9. Bekannte Grenzen

- **Datenlücke im Compiler:** `DefinitionCompiler.CompileSection` verwirft Zeilen ohne `=` in verschachtelten Blöcken
  (`IniStatement`). Die Referenz trennt Tokens auch an Leerzeichen (`INI::m_seps`), liest `Model ABPWRPLANT_A1` also als
  Schlüssel/Wert. Betroffen sind 45 Zustände der Kraftwerks-Steuerstäbe (je neun in `AirforceGeneral.ini`,
  `BossGeneral.ini`, `FactionBuilding.ini`, `LaserGeneral.ini`, `SuperWeaponGeneral.ini`); sie erscheinen als
  `missing-model` und zeichnen kein Modell, bis der Datencompiler solche Zeilen als Eigenschaften führt.
- Partikelsystemnamen aus `ParticleSysBone` werden nicht gegen Partikeldefinitionen geprüft (Sache des FX-Pakets).
- Modulschlüssel außerhalb des Präsentationsbedarfs (Reifen, Ketten, Laser, Baumphysik) bleiben Text in `Settings`.
- Zweite/dritte Waffe, Türen, Dockphasen und die übrigen „nicht modelliert“-Flags aus Abschnitt 2 fehlen
  in der Simulation; das zugehörige Verhalten der Modelle bleibt bis dahin aus.
- `ACTIVELY_CONSTRUCTING` ist eine Näherung (Bauaufgabe und Stillstand).

## 10. Tests

`tests/CnCNew.Presentation.Tests/ObjectTests.cs` (synthetische Fixtures, keine Originalauszüge): Flagtabelle und
Parser, Auswahl einschließlich des FRONTCRUSHED-Beispiels der Referenz, Gleichstände, Aliase und Ignorier-Bits,
Übergänge/Warten im `ModelStateTracker`, `DrawSpec`-Kopiersemantik, Diagnosen, Anführungszeichen-Tokens,
Flagableitung an einer echten `SimulationWorld` (Feuern/Nachladen, Schaden, Bewegung, Veteranen, Upgrades, Bau,
Verkauf), Interpolation, Nebel mit Geistern, Kulisse mit Verdeckungsregel, Hausfarben. Mit
`--content-dir game/content` zusätzlich: Crusader und Kaserne gegen die Retaildaten, Audit ohne
Unklassifiziertes und deterministisch, Projektion echter Vorlagen, Kulisse von „Alpine Assault“.

## 11. Zeit und Tickrate (Phase 21)

Die Objektdarstellung nimmt nirgends mehr an, dass ein Tick 1/30 s dauert. Zwei Begriffe, zwei Namen: die **Simulationsrate** kommt aus der Welt (`world.Time`), das **Originalbild** (1/30 s) ist die Einheit, in der das Original seine Client-Werte verfasst hat – außerhalb der Simulation genau eine Konstante, `CnCNew.Data.OriginalFrame.PerSecond`. Bei 30 Hz ist ein Originalbild ein Tick; deshalb ist dort jeder Wert und jede Prüfsumme unverändert (Beleg: `docs/TICKRATE-MIGRATION.md` §14).

| Was | Vorher | Jetzt | Bei 60 Hz |
|---|---|---|---|
| Aufnahme und Interpolation (`FrameProjector.Advance`/`Project`) | je Tick | je Tick (unverändert) | doppelt so viele Aufnahmen, halb so große Interpolationsschritte – gewollt |
| Mündungsfeuer (`FrameProjector.MuzzleFlashFrames = 4`) | `world.Tick - lastFire <= 4` | `<= world.Time.OriginalFrames(4)` | 9 statt 5 Ticks, gleiche Spielzeit |
| `FIRING_A/B/C` (`ConditionDeriver.Weapon`) | „im letzten Tick gefeuert“ | „innerhalb des letzten Originalbilds gefeuert“ | zwei Ticks lang, 1/30 s wie bei 30 Hz |
| Heilsymbol (`HealingIconFrames = 3 * OriginalFrame.PerSecond`) | `3 * 30` Ticks | in Ticks der Welt umgerechnet (`_healingIconTicks`) | 3 s |
| Jubel (`UnitConditionTracker.CheerFrames`) | `3 * 30` Ticks | `FrameProjector.Cheer(entities, world)` rechnet um | 3 s |
| Auswahl-/Übernahmeblitz (`SelectionFlashTracker`) | Alter in Ticks | Alter in Originalbildern (`GameClock.OriginalFrameOf` des Ereignisticks); die Hüllkurve (zwei Bilder Spitze, dann 3/4, 1/2, 1/4) ist eine Tabelle je Originalbild | gleiche Dauer; die Stufen wechseln alle zwei Ticks |
| Skriptblitz (`ScriptFlashInterval = 15`, `StartScripted`) | `tick % 15`, `seconds * 30 / 15` | `Advance(frame)` einmal je Originalbild, Zahl der Blitze aus `OriginalFrame.PerSecond` | alle 0,5 s, vier Blitze in zwei Sekunden |
| Fahrwerk (`SuspensionTracker`) | ein Schritt je Tick | ein Schritt je **Originalbild** (auf den Ticks, die eines beginnen); dazwischen wird die Pose gehalten | dieselbe Schwingung; die Pose ändert sich 30-mal je Sekunde |
| `FREEFALL` der Lawinenfelsen | `Life < 100`, Fall je Tick > 0,2 | `Life < OriginalFrames(100)`, Fall je Tick mal Ticks je Originalbild > 0,2 | gleiche Schwelle je Spielzeit |
| Aufräumen der Tabellen, Nebel der Szenerie | alle 150 bzw. 5 Ticks | alle 150 bzw. 5 Originalbilder (`PruneIntervalFrames`, `SceneryFogIntervalFrames`) | gleiche Spielzeit |

**Fahrwerk: warum Bildraster und nicht Schrittweite.** Der Auftrag der Phase 21 nennt „Feder und Dämpfer mit der Schrittweite rechnen“. Das ist bewusst nicht so gebaut: die Rekursion aus `Drawable::calcPhysicsXformTreads/Wheels` ist eine Abbildung je Bild mit Faktoren je Bild (Dämpfung 0,9, steigende Nase halbiert, Räder folgen zur Hälfte, Lenkung um ein Zehntel, dazu Klemmen und der Bodenstoß), keine diskretisierte Differentialgleichung; mit halber Schrittweite gerechnet schwingt sie anders aus. Sie läuft deshalb bei jeder Rate einmal je Originalbild, Geschwindigkeit und Beschleunigung sind Differenzen über ein Originalbild (die Höchstgeschwindigkeit des Locomotors – je Tick der Welt – wird dafür auf das Originalbild umgerechnet), und der Zufall des Bodenstoßes hängt an der Bildnummer statt am Tick. Gemessen (`ObjectTests.TimeScale.cs`): Ausschwingen auf einen Hang 1,2667 s bei 30 und 60 Hz, 1,2833 s bei 120 Hz (ein Tick Versatz beim Erscheinen), Ruhelage auf fünf Nachkommastellen gleich. Die Pose wird weiterhin nicht interpoliert (Phase 29).

**Bewusst in Ticks geblieben:** die Türuhr der Produktionsgebäude und `ConstructionComplete` (`StructureConditionTracker`: Stempel und Katalogdauern sind beide Ticks der Welt), die Mindesthaltezeit der Lenkzustände (`AnimationSteeringUpdate.MinTransitionTicks`), die Drehrichtung aus der Kursänderung eines Ticks gegen `Locomotor.TurnRatePerTick`, `ACTIVELY_BEING_CONSTRUCTED` (Bauarbeit gegenüber der vorigen Aufnahme) und die Teleport-Schwelle der Interpolation (`SnapDistance`, Weg zwischen zwei Aufnahmen).

**Zeitmaß-Test** (`tests/CnCNew.Presentation.Tests/ObjectTests.TimeScale.cs`, je bei 30, 60 und 120 Hz auf der synthetischen Fixture der Rate, Toleranz ein Originalbild): Mündungsfeuer nach einem Schuss, `FIRING_A` (genau 1/30 s bei jeder Rate), Heilsymbol nach der letzten Heilung (3,0333 / 3,0167 / 3,0083 s), Skriptblitz (vier Blitze, Ende des letzten 1,6667 s nach der Anforderung, ein Blitz 0,1667 s), Ausschwingen des Fahrwerks.

**Nicht belegt:** ob Mündungsfeuer, Blitz und Federung bei 60 Hz richtig *aussehen*. Gemessen sind Dauern, gesehen hat es kein Mensch (Sichtprüfliste: `docs/TICKRATE-MIGRATION.md` §14.7).
