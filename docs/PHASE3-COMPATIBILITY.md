# Phase-3-Kompatibilitätsliste

Stand: 27. September 2026

Der Compiler ignoriert unbekannte Semantik nie. Nicht für Phase 3 benötigte Definitionen und
noch nicht implementierte Simulations-/Präsentationsmodule werden als `CompatibilityEntry` mit
Name, Häufigkeit, Besitzerart und geplantem Ziel in jedem kanonischen Snapshot geführt. Ein
strikter Compilerlauf kann unbekannte Definitionen, Module oder Referenzen als Fehler behandeln.

## Definitionsdomänen außerhalb des Phase-3-Vertrags

41 Typen mit 2.801 Definitionen sind vollständig syntaktisch und werttypisiert erhalten, ihre
fachspezifischen Verträge folgen in den angegebenen späteren Phasen:

- Simulation/Welt/KI (Phase 4–6): AIData, Bridge, Campaign, ChallengeGenerals, CrateData,
  MapCache, ObjectCreationList, Rank, Road, ScriptAction, ScriptCondition, Terrain, WaterSet,
  WaterTransparency und Weather.
- Präsentation/UI (Phase 7–8): Animation, AudioSettings, ButtonSet, CommandMap,
  ControlBarResizer, ControlBarScheme, Credits, DrawGroupInfo, DynamicGameLOD, EvaEvent,
  HeaderTemplate, InGameUI, Language, MappedImage, MiscAudio, Mouse, MouseCursor,
  MultiplayerColor, MultiplayerSettings, MultiplayerStartingMoneyChoice, OnlineChatColors,
  ShellMenuScheme, StaticGameLOD, Video, WebpageURL und WindowTransition.

## Modul-/Behaviorbestand

Der Bestand enthält 206 eindeutige Modultypen in 16.360 Verwendungen. Alle gehören derzeit zur
expliziten Kompatibilitätsstufe und tragen als Implementierungsziel Phase 4/5 (Simulation und
Spielregeln). Die maschinenlesbare Liste im Snapshot enthält die exakte Häufigkeit und Besitzerart.
Sie umfasst:

ActiveBody, AIUpdateInterface, AnimatedParticleSysBoneClientUpdate, AnimationSteeringUpdate,
ArmorSet, ArmorUpgrade, AssaultTransportAIUpdate, AssistedTargetingUpdate, AutoDepositUpdate,
AutoFindHealingUpdate, AutoHealBehavior, BaikonurLaunchPower, BaseRegenerateUpdate,
BattleBusSlowDeathBehavior, BattlePlanUpdate, BeaconClientUpdate, BoneFXDamage, BoneFXUpdate,
BridgeBehavior, BridgeScaffoldBehavior, BridgeTowerBehavior, BunkerBusterBehavior,
CashBountyPower, CashHackSpecialPower, CheckpointUpdate, ChinookAIUpdate, CleanupAreaPower,
CleanupHazardUpdate, CommandButtonHuntUpdate, CommandSetUpgrade, ConvertToCarBombCrateCollide,
ConvertToHijackedVehicleCrateCollide, CostModifierUpgrade, CountermeasuresBehavior,
CreateCrateDie, CreateObjectDie, CrushDie, DamDie, DefaultProductionExitUpdate, DeletionUpdate,
DeliverPayloadAIUpdate, DemoTrapUpdate, DeployStyleAIUpdate, DestroyDie, DozerAIUpdate,
DumbProjectileBehavior, DynamicShroudClearingRangeUpdate, EjectPilotDie, EMPUpdate,
EnemyNearUpdate, ExperienceScalarUpgrade, FireOCLAfterWeaponCooldownUpdate, FireSpreadUpdate,
FirestormDynamicGeometryInfoUpdate, FireWeaponCollide, FireWeaponPower, FireWeaponUpdate,
FireWeaponWhenDamagedBehavior, FireWeaponWhenDeadBehavior, FlammableUpdate, FlightDeckBehavior,
FloatUpdate, FXListDie, GarrisonContain, GenerateMinefieldBehavior, GrantScienceUpgrade,
GrantStealthBehavior, GrantUpgradeCreate, HackInternetAIUpdate, HealContain, HeightDieUpdate,
HelicopterSlowDeathBehavior, HelixContain, HighlanderBody, HijackerUpdate, HiveStructureBody,
HordeUpdate, ImmortalBody, InactiveBody, InstantDeathBehavior, InternetHackContain, JetAIUpdate,
JetSlowDeathBehavior, KeepObjectDie, LaserUpdate, LeafletDropBehavior, LifetimeUpdate,
LockWeaponCreate, LocomotorSetUpgrade, MaxHealthUpgrade, MinefieldBehavior, MissileAIUpdate,
MissileLauncherBuildingUpdate, MobMemberSlavedUpdate, ModelConditionUpgrade, MoneyCrateCollide,
NeutronBlastBehavior, NeutronMissileSlowDeathBehavior, NeutronMissileUpdate,
ObjectCreationUpgrade, OCLSpecialPower, OCLUpdate, OverchargeBehavior, OverlordContain,
ParachuteContain, ParkingPlaceBehavior, ParticleUplinkCannonUpdate, PassengersFireUpgrade,
PhysicsBehavior, PilotFindVehicleUpdate, PointDefenseLaserUpdate, PoisonedBehavior,
PowerPlantUpdate, PowerPlantUpgrade, PreorderCreate, ProductionUpdate, ProjectileStreamUpdate,
PropagandaTowerBehavior, QueueProductionExitUpdate, RadarUpdate, RadarUpgrade, RadiusDecalUpdate,
RailedTransportAIUpdate, RailedTransportContain, RailedTransportDockUpdate, RailroadBehavior,
RebuildHoleBehavior, RebuildHoleExposeDie, RepairDockUpdate, ReplaceObjectUpgrade,
RiderChangeContain, SabotageCommandCenterCrateCollide, SabotageFakeBuildingCrateCollide,
SabotageInternetCenterCrateCollide, SabotageMilitaryFactoryCrateCollide,
SabotagePowerPlantCrateCollide, SabotageSuperweaponCrateCollide,
SabotageSupplyCenterCrateCollide, SalvageCrateCollide, SlavedUpdate, SlowDeathBehavior,
SmartBombTargetHomingUpdate, SpawnBehavior, SpawnPointProductionExitUpdate, SpecialAbility,
SpecialAbilityUpdate, SpecialPowerCompletionDie, SpecialPowerCreate,
SpectreGunshipDeploymentUpdate, SpectreGunshipUpdate, SpyVisionSpecialPower, SpyVisionUpdate,
SquishCollide, StealthDetectorUpdate, StealthUpdate, StealthUpgrade, StickyBombUpdate,
StructureBody, StructureCollapseUpdate, StructureToppleUpdate, SubObjectsUpgrade,
SupplyCenterCreate, SupplyCenterDockUpdate, SupplyCenterProductionExitUpdate,
SupplyTruckAIUpdate, SupplyWarehouseCreate, SupplyWarehouseCripplingBehavior,
SupplyWarehouseDockUpdate, SwayClientUpdate, TechBuildingBehavior, TensileFormationUpdate,
ToppleUpdate, TransitionDamageFX, TransportAIUpdate, TransportContain, TunnelContain, UndeadBody,
UnitCrateCollide, UnpauseSpecialPowerUpgrade, UpgradeDie, VeterancyCrateCollide,
VeterancyGainCreate, W3DDebrisDraw, W3DDefaultDraw, W3DDependencyModelDraw, W3DLaserDraw,
W3DModelDraw, W3DOverlordAircraftDraw, W3DOverlordTankDraw, W3DOverlordTruckDraw,
W3DPoliceCarDraw, W3DProjectileStreamDraw, W3DRopeDraw, W3DScienceModelDraw, W3DSupplyDraw,
W3DTankDraw, W3DTankTruckDraw, W3DTracerDraw, W3DTreeDraw, W3DTruckDraw, WanderAIUpdate,
WaveGuideUpdate, WeaponBonusUpdate, WeaponBonusUpgrade, WeaponSetUpgrade und WorkerAIUpdate.

## Sichtbare Sonderfälle

- `GeometryMajorRadius = 18.5.0` wird genau einmal über
  `shipped-double-decimal-radius` als 18.5 normalisiert.
- 27 Referenzverwendungen bleiben als `compatibility-unresolved-reference` sichtbar: 25 Verwendungen
  der engineinternen Veterancy-Pseudoupgrades `Upgrade_Veterancy_ELITE` beziehungsweise
  `Upgrade_Veterancy_HEROIC`, ein fehlendes FXList-Ziel `DieByFireUSA` und das nicht definierte
  Object `Boss_VehicleSentryDrone`. Es gibt keinen stillen Ersatz durch ein anderes Ziel.
- Das gemeinsam ausgeführte Vollinventar kann ReplaceModule-Ziele aus voneinander unabhängigen
  Kartenoverlays nicht in jedem Aggregatzustand vorfinden. Jeder Fall erzeugt
  `compatibility-missing-replace-target`; ein einzelnes Kartenoverlay behält korrekte
  Replace-Semantik.
- Das ausgelieferte `Maps/MD_GLA03_CINE/map.ini` enthält ein leeres `AddModule` mit nur
  `BuildTime`. Es wird als `compatibility-empty-add-module` diagnostiziert.
