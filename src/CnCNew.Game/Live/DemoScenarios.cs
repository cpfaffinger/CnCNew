using System.Collections.Immutable;

namespace CnCNew.Game.Live;

/// <summary>
/// The staged demonstrations (<see cref="DemoScenario"/>): small fights on a real map that isolate one behaviour each so it can be watched and audited
/// (<c>scripts/demo-scenario.ps1</c>, <c>PresentationTool scenario-audit</c>).
/// </summary>
public static class DemoScenarios
{
    public static ImmutableArray<DemoScenario> All => [CaptureBuilding, TankDuel, JetStrafe, ..FactoryExits.All, ..AcceptanceScenes.All];

    public static DemoScenario? Find(string name) => All.FirstOrDefault(scenario => scenario.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A Ranger walks up to an enemy barracks, plants the flag (unpack, raise flag, pack) and the building changes sides.</summary>
    public static DemoScenario CaptureBuilding { get; } = new(
        "capture-building", "Infantry captures a building",
        "A Ranger walks to the enemy barracks, unpacks, raises the flag (RAISING_FLAG) and packs up again; after that the building is his.",
        1500,
        [
            new("ranger", "AmericaInfantryRanger", 1, 0, 0, 0f),
            new("barracks", "ChinaBarracks", 2, 150, 0, 180f),
        ],
        [new(10, "ranger", ScenarioOrderKind.Capture, "barracks")],
        75, 0, 0f, 30f, 140f)
    {
        // The ability is paused until the upgrade is researched, and a fresh Ranger has to recharge it first.
        Upgrades = ["Upgrade_InfantryCaptureBuilding"],
        PowersReady = true,
    };

    /// <summary>
    /// Two tanks start at an angle to each other: each turret has to turn to the other tank before the first shot, keeps its aim through every reload until the victim dies and then turns back
    /// after its recentre time. A Sentry Drone stands far outside everybody's weapon range and glances around while idle (<c>TurretAIIdleScanState</c>).
    /// </summary>
    public static DemoScenario TankDuel { get; } = new(
        "tank-duel", "Tank against tank",
        "A Crusader and a Battlemaster stand at an angle to each other. Both turrets have to turn to the opponent before a shot is fired, and stay aimed at it until the opponent dies (also between the shots); after that they turn back after the recentre time. The Sentry Drone far off to the side turns its turret around at random while idle. The audit measures the angle error in every tick.",
        900,
        [
            new("crusader", "AmericaTankCrusader", 1, 0, 0, 0f),
            new("battlemaster", "ChinaTankBattleMaster", 2, 85, 85, 225f),
            new("sentinel", "AmericaVehicleSentryDrone", 1, 30, -190, 0f),
        ],
        [
            new(5, "crusader", ScenarioOrderKind.Attack, "battlemaster"),
            new(5, "battlemaster", ScenarioOrderKind.Attack, "crusader"),
        ],
        42, 42, 20f, 40f, 200f);

    /// <summary>Raptors are built at an airfield, take off on the attack order, fly to two Missile Defenders and are shot at; what survives lands and reloads.</summary>
    public static DemoScenario JetStrafe { get; } = new(
        "jet-strafe", "Jet against missile troopers",
        "The airfield, powered by a plant beside it, builds two Raptors. They take off on the attack order, fly to two missile troopers (Missile Defender), are shot at, fire their missiles, fly back, land and reload.",
        3600,
        [
            new("airfield", "AmericaAirfield", 1, 0, 0, 0f),
            // The airfield consumes power: without a plant the Raptors are built at half speed (ThingTemplate::calcTimeToBuild).
            new("power", "AmericaPowerPlant", 1, -220, 0, 0f),
            new("trooper1", "AmericaInfantryMissileDefender", 2, 700, 20, 180f),
            new("trooper2", "AmericaInfantryMissileDefender", 2, 705, -15, 180f),
        ],
        [
            new(1, "airfield", ScenarioOrderKind.Produce, "raptor1", Template: "AmericaJetRaptor"),
            new(1, "airfield", ScenarioOrderKind.Produce, "raptor2", Template: "AmericaJetRaptor"),
            new(1, "raptor1", ScenarioOrderKind.Attack, "trooper1"),
            new(1, "raptor2", ScenarioOrderKind.Attack, "trooper2"),
            new(1, "trooper1", ScenarioOrderKind.Attack, "raptor1"),
            new(1, "trooper2", ScenarioOrderKind.Attack, "raptor2"),
        ],
        350, 0, 0f, 45f, 520f)
    {
        StartMoney = 5000,
    };
}
