using System.Collections.Immutable;

namespace CnCNew.Game.Live;

/// <summary>
/// The staged scenes of the acceptance catalogue (<c>scripts/acceptance/catalogue.json</c>, <c>docs/ACCEPTANCE.md</c>) that no older demo shows: each isolates one thing a person has to
/// look at - a march to one point, wheels and suspension, boarding, arcing projectiles, a repair, a fire fight, a construction - close to the camera, and
/// <c>PresentationTool scenario-audit</c> checks each of them without a window. They reference retail templates by name only.
/// </summary>
public static class AcceptanceScenes
{
    public static ImmutableArray<DemoScenario> All => [March, Wheels, Board, Artillery, Repair, Firefight, Build, Flames, Paradrop, Lotus, CombatDrop, Bodies, Subdual, Firestorm, Reverse];

    /// <summary>The two ends of the march and the frames its two legs start at; the audit reads them.</summary>
    public const float MarchEast = 90f, MarchWest = -90f;
    public static ImmutableArray<long> MarchLegs { get; } = [30, 750];
    public const long MarchLegFrames = 720;

    /// <summary>Tanks, a Humvee and a squad are sent to one point and back: driving off, turning, arriving, and everybody ending on a place of his own.</summary>
    public static DemoScenario March { get; } = BuildMarch();

    private static DemoScenario BuildMarch()
    {
        var units = new List<ScenarioUnit>
        {
            new("tank1", "AmericaTankCrusader", 1, -90, -35, 0f),
            new("tank2", "AmericaTankCrusader", 1, -90, 0, 0f),
            new("tank3", "AmericaTankCrusader", 1, -90, 35, 0f),
            new("humvee", "AmericaVehicleHumvee", 1, -90, 70, 0f),
        };
        for (var index = 0; index < 6; index++) units.Add(new ScenarioUnit($"ranger{index + 1}", "AmericaInfantryRanger", 1, -125, -50 + 20 * index, 0f));
        var orders = new List<ScenarioOrder>();
        // One order to the selected group per leg, as a player gives it (MSG_DO_MOVETO to a group).
        for (var leg = 0; leg < MarchLegs.Length; leg++)
            orders.Add(new ScenarioOrder(MarchLegs[leg], units[0].Tag, ScenarioOrderKind.MoveGroup, string.Join(',', units.Skip(1).Select(unit => unit.Tag)), X: leg % 2 == 0 ? MarchEast : MarchWest, Y: 0));
        return new DemoScenario(
            "acc-march", "March to one point",
            "Three Crusaders, a Humvee and six Rangers are sent to the same point and back after 24 seconds. To see: starting, turning, braking, arriving without a twitch, and everybody ending on a place of his own beside the goal instead of circling the others.",
            MarchLegs[^1] + MarchLegFrames, [..units], [..orders], 0, 0, 0f, 42f, 290f);
    }

    /// <summary>The corners of the wheel scene's course and the frames between two corners; the audit reads them.</summary>
    public static ImmutableArray<(float X, float Y)> WheelCourse { get; } = [(70, 35), (0, 70), (-70, 35), (0, 0), (70, 35), (0, 70), (-70, 35), (0, 0)];
    public const long WheelFirstFrame = 30, WheelLegFrames = 120;

    /// <summary>A Humvee drives a zig-zag close to the camera: the wheels steer in and back, the body nods when it starts and brakes.</summary>
    public static DemoScenario Wheels { get; } = new(
        "acc-wheels", "Wheels, steering, suspension",
        "A Humvee drives a zig-zag course twice close to the camera; beside it a dozer drives a straight line there and back. To see: the wheels turn with the distance, steer into the corners and back, and the dozer nods when it starts and brakes and settles without trembling.",
        WheelFirstFrame + WheelCourse.Length * WheelLegFrames + 60,
        [
            new("humvee", "AmericaVehicleHumvee", 1, 0, 0, 0f),
            new("dozer", "AmericaVehicleDozer", 1, -70, 105, 0f),
        ],
        [
            ..WheelCourse.Select((corner, index) => new ScenarioOrder(WheelFirstFrame + index * WheelLegFrames, "humvee", ScenarioOrderKind.MoveTo, X: corner.X, Y: corner.Y)),
            ..Enumerable.Range(0, 4).Select(index => new ScenarioOrder(WheelFirstFrame + index * 2 * WheelLegFrames, "dozer", ScenarioOrderKind.MoveTo, X: index % 2 == 0 ? 70 : -70, Y: 105)),
        ],
        0, 15, 15f, 38f, 190f);

    public const int BoardSquad = 5;
    /// <summary>Frames of the boarding scene: first boarding order, drive, evacuation, second boarding, drive back, second evacuation.</summary>
    public const long BoardFirst = 30, BoardDrive = 330, BoardOut = 540, BoardSecond = 690, BoardDriveBack = 990, BoardOutAgain = 1200;

    /// <summary>Five Rangers board a Humvee, are driven a short way and let out; then the same again in the other direction.</summary>
    public static DemoScenario Board { get; } = new(
        "acc-board", "Boarding and leaving a transport",
        "Five Rangers are told to board a Humvee. To see: they walk straight in instead of around the vehicle, the Humvee drives them a short way and lets them out, and afterwards they stand beside the vehicle, not inside it and not inside each other. Then the same on the way back.",
        BoardOutAgain + 150,
        [
            new("humvee", "AmericaVehicleHumvee", 1, 0, 0, 0f),
            ..Enumerable.Range(0, BoardSquad).Select(index => new ScenarioUnit($"ranger{index + 1}", "AmericaInfantryRanger", 1, -90, -40 + 20 * index, 0f)),
        ],
        [
            ..Enumerable.Range(0, BoardSquad).Select(index => new ScenarioOrder(BoardFirst, $"ranger{index + 1}", ScenarioOrderKind.Enter, "humvee")),
            new(BoardDrive, "humvee", ScenarioOrderKind.MoveTo, X: 110, Y: 0),
            new(BoardOut, "humvee", ScenarioOrderKind.Evacuate),
            ..Enumerable.Range(0, BoardSquad).Select(index => new ScenarioOrder(BoardSecond, $"ranger{index + 1}", ScenarioOrderKind.Enter, "humvee")),
            new(BoardDriveBack, "humvee", ScenarioOrderKind.MoveTo, X: 0, Y: 0),
            new(BoardOutAgain, "humvee", ScenarioOrderKind.Evacuate),
        ],
        40, 0, 0f, 40f, 230f);

    /// <summary>A Tomahawk launcher and an Inferno Cannon shell a command centre: a guided missile and a lobbed shell, flight, impact and detonation.</summary>
    public static DemoScenario Artillery { get; } = new(
        "acc-artillery", "Projectile paths",
        "A Tomahawk launcher and an Inferno Cannon shell an enemy command centre. To see: the missile climbs, turns in and hits; the shell flies an arc; both fly smoothly, and explosion and fire appear where and when the projectile arrives.",
        900,
        [
            new("tomahawk", "AmericaVehicleTomahawk", 1, 0, -45, 0f),
            new("inferno", "ChinaVehicleInfernoCannon", 1, 0, 45, 0f),
            new("target", "ChinaCommandCenter", 2, 330, 0, 180f),
        ],
        [
            new(20, "tomahawk", ScenarioOrderKind.Attack, "target"),
            new(20, "inferno", ScenarioOrderKind.Attack, "target"),
        ],
        175, 0, 0f, 40f, 420f);

    /// <summary>Frames of the repair scene (the hit and the repair order) and how much health the hit takes, in percent.</summary>
    public const long RepairHit = 5, RepairOrder = 30;
    public const float RepairDamagePercent = 52f;

    /// <summary>A dozer repairs a damaged power plant: the damaged look of the building and the wrench symbol over it.</summary>
    public static DemoScenario Repair { get; } = new(
        "acc-repair", "Repair and the wrench symbol",
        "A power plant is damaged (damaged model, below half health); a dozer drives up and repairs it at 2 % per second. To see: the wrench stands over the building while it is repaired; from half health on the building looks whole again, and when it is fully repaired the symbol disappears.",
        990,
        [
            new("plant", "AmericaPowerPlant", 1, 60, 0, 0f),
            new("dozer", "AmericaVehicleDozer", 1, -70, -20, 0f),
        ],
        [
            new(RepairHit, "plant", ScenarioOrderKind.Damage, X: RepairDamagePercent),
            new(RepairOrder, "dozer", ScenarioOrderKind.Repair, "plant"),
        ],
        20, 0, 20f, 40f, 240f);

    public const int FirefightRebels = 6, FirefightRangers = 3;

    /// <summary>A Gattling tank and three Rangers against six Rebels: rifle and Gattling fire, muzzle flashes, tracers, hits and deaths.</summary>
    public static DemoScenario Firefight { get; } = new(
        "acc-firefight", "Fire fight",
        "A Gattling tank and three Rangers fire at six Rebels, who fire back. To see: a muzzle flash with every shot (no constant glow), tracers from the barrel to the target, the Gattling spinning up and firing faster, the hit falling with a death animation; to hear: one sound per shot, none doubled.",
        600,
        [
            new("gattling", "ChinaTankGattling", 1, 0, 0, 0f),
            ..Enumerable.Range(0, FirefightRangers).Select(index => new ScenarioUnit($"ranger{index + 1}", "AmericaInfantryRanger", 1, -10, -50 + 50 * index + (index == 1 ? 25 : 0), 0f)),
            ..Enumerable.Range(0, FirefightRebels).Select(index => new ScenarioUnit($"rebel{index + 1}", "GLAInfantryRebel", 2, 150, -50 + 20 * index, 180f)),
        ],
        [
            new(20, "gattling", ScenarioOrderKind.Attack, "rebel3"),
            ..Enumerable.Range(0, FirefightRangers).Select(index => new ScenarioOrder(20, $"ranger{index + 1}", ScenarioOrderKind.Attack, $"rebel{index + 1}")),
            ..Enumerable.Range(0, FirefightRebels).Select(index => new ScenarioOrder(20, $"rebel{index + 1}", ScenarioOrderKind.Attack, index < 3 ? "gattling" : $"ranger{index - 2}")),
        ],
        75, 0, 0f, 40f, 240f);

    /// <summary>
    /// The effects objects carry (docs/PRESENTATION-FX.md, "Attached effects"): a Dragon tank burns a barracks and three Rangers, a power plant beside them is damaged twice,
    /// a rocket buggy drives past.
    /// </summary>
    public static DemoScenario Flames { get; } = new(
        "acc-flames", "Flames, smoke, burning",
        "A Dragon tank sets a barracks on fire and burns three Rangers; a power plant is damaged twice; a rocket buggy drives past. To see: a flame stream and flame trail from the Dragon tank to its target (no box flying), fire on the barracks while it burns, smoke and fire at the damaged power plant, dust behind the buggy and the exhaust of rockets.",
        720,
        [
            new("dragon", "ChinaTankDragon", 1, 0, 0, 0f),
            new("barracks", "AmericaBarracks", 2, 100, 30, 180f),
            ..Enumerable.Range(0, 3).Select(index => new ScenarioUnit($"ranger{index + 1}", "AmericaInfantryRanger", 2, 80, -40 + 12 * index, 180f)),
            new("plant", "AmericaPowerPlant", 2, 60, -140, 180f),
            new("buggy", "GLAVehicleRocketBuggy", 1, -60, 80, 0f),
            new("hunter", "ChinaInfantryTankHunter", 1, -20, -60, 0f),
        ],
        [
            new(20, "dragon", ScenarioOrderKind.Attack, "ranger1"),
            new(220, "dragon", ScenarioOrderKind.Attack, "barracks"),
            new(30, "plant", ScenarioOrderKind.Damage, X: 45f),
            new(200, "plant", ScenarioOrderKind.Damage, X: 30f),
            new(60, "buggy", ScenarioOrderKind.MoveTo, X: 160f, Y: 80f),
            new(40, "hunter", ScenarioOrderKind.Attack, "plant"),
        ],
        40, -20, 20f, 42f, 330f);

    /// <summary>Four Rangers board a Chinook, which flies to a place and lets them down its ropes.</summary>
    public static DemoScenario CombatDrop { get; } = new(
        "acc-combat-drop", "Combat drop",
        "Four Rangers board a Chinook. It flies over the open ground and hovers. To see: ropes come down from the helicopter, the soldiers slide down them one after the other and stand on the ground.",
        1500,
        [
            new("chinook", "AmericaVehicleChinook", 1, -120, 0, 0f),
            ..Enumerable.Range(0, 4).Select(index => new ScenarioUnit($"ranger{index + 1}", "AmericaInfantryRanger", 1, -120, 40 + 12 * index, 270f)),
        ],
        [
            ..Enumerable.Range(0, 4).Select(index => new ScenarioOrder(10, $"ranger{index + 1}", ScenarioOrderKind.Enter, "chinook")),
            new(300, "chinook", ScenarioOrderKind.CombatDrop, X: 60f, Y: 0f),
        ],
        0, 0, 20f, 42f, 420f);

    /// <summary>Tomahawks fire at a squad from beyond its rifles: a rocket throws a body in the air, and every body goes into the ground before it is gone.</summary>
    public static DemoScenario Bodies { get; } = new(
        "acc-bodies", "Bodies flung and sinking",
        "Two Tomahawk launchers fire at a squad of Rangers from far away. To see: a soldier killed by a rocket is thrown through the air and lands; the dead lie for a few seconds, then sink slowly into the ground and are gone.",
        900,
        [
            new("rocket1", "AmericaVehicleTomahawk", 1, -200, -30, 0f),
            new("rocket2", "AmericaVehicleTomahawk", 1, -200, 30, 0f),
            ..Enumerable.Range(0, 4).Select(index => new ScenarioUnit($"ranger{index + 1}", "AmericaInfantryRanger", 2, 70, -24 + 16 * index, 180f)),
        ],
        [
            new(20, "rocket1", ScenarioOrderKind.Attack, "ranger1"),
            new(20, "rocket2", ScenarioOrderKind.Attack, "ranger3"),
            new(220, "rocket1", ScenarioOrderKind.Attack, "ranger2"),
            new(220, "rocket2", ScenarioOrderKind.Attack, "ranger4"),
        ],
        40, 0, 20f, 42f, 300f);

    /// <summary>The microwave tank and the ECM tank disable without killing.</summary>
    public static DemoScenario Subdual { get; } = new(
        "acc-subdual", "Microwave and ECM",
        "A microwave tank beams at a power plant and an ECM tank at a Crusader. To see: the beam of each; after some seconds the Crusader stops and stands blue, the building goes dark; neither loses health, and when the beam stops they come back.",
        1500,
        [
            new("microwave", "AmericaTankMicrowave", 1, -80, -60, 0f),
            new("plant", "AmericaPowerPlant", 2, 60, -80, 180f),
            new("ecm", "ChinaTankECM", 1, -80, 60, 0f),
            new("crusader", "AmericaTankCrusader", 2, 60, 60, 180f),
        ],
        [
            new(20, "microwave", ScenarioOrderKind.Attack, "plant"),
            new(20, "ecm", ScenarioOrderKind.Attack, "crusader"),
        ],
        0, 0, 20f, 42f, 330f);

    /// <summary>Eight napalm hits within three seconds make a firestorm (<c>HistoricBonusCount</c>, <c>HistoricBonusTime</c>): five MiGs fly in side by side.</summary>
    public static DemoScenario Firestorm { get; } = new(
        "acc-firestorm", "Firestorm",
        "Five MiGs fire napalm at one spot. To see: the first hits burn on the ground, and when enough of them have fallen at one place a tall firestorm rises there and burns what stands around it.",
        1500,
        [
            ..Enumerable.Range(0, 5).Select(index => new ScenarioUnit($"mig{index + 1}", "ChinaJetMIG", 1, -260, -40 + 20 * index, 0f)),
            new("plant", "AmericaPowerPlant", 2, 80, 0, 180f),
            new("barracks", "AmericaBarracks", 2, 120, 90, 180f),
        ],
        [
            ..Enumerable.Range(0, 5).Select(index => new ScenarioOrder(30, $"mig{index + 1}", ScenarioOrderKind.Attack, "plant")),
        ],
        60, 20, 20f, 42f, 420f);

    /// <summary>A Humvee backs up to a goal close behind it, and turns for one far behind it.</summary>
    public static DemoScenario Reverse { get; } = new(
        "acc-reverse", "Reversing",
        "A Humvee stands still, then is sent a car's length behind itself, and later far behind itself. To see: for the short way it backs up without turning round; for the long way it turns.",
        900,
        [new("humvee", "AmericaVehicleHumvee", 1, 60, 0, 0f)],
        [
            new(40, "humvee", ScenarioOrderKind.MoveTo, X: 20f, Y: 0f),
            new(300, "humvee", ScenarioOrderKind.MoveTo, X: -120f, Y: 0f),
        ],
        0, 0, 20f, 42f, 240f);

    /// <summary>Frames the two construction orders of the build scene are given at.</summary>
    public const long BuildFirstOrder = 20, BuildSecondOrder = 420;

    /// <summary>A dozer builds a power plant and then a barracks: the scaffold, the rising building, the finished one.</summary>
    public static DemoScenario Build { get; } = new(
        "acc-build", "Construction",
        "A dozer builds a power plant and then a barracks. To see: the dozer drives to the site, the building rises (construction progress), and stands complete in the player's colour afterwards; to hear: the construction sound and the announcement of the finished building.",
        2700,
        [new("dozer", "AmericaVehicleDozer", 1, -90, 0, 0f)],
        [
            new(BuildFirstOrder, "dozer", ScenarioOrderKind.Build, "plant", X: 30, Y: -70, Template: "AmericaPowerPlant"),
            new(BuildSecondOrder, "dozer", ScenarioOrderKind.Build, "barracks", X: 30, Y: 90, Template: "AmericaBarracks"),
        ],
        20, -15, 20f, 42f, 330f)
    {
        StartMoney = 5000,
    };

    /// <summary>The paratroopers of the Air Force general at once: a person should not have to earn the rank to judge a chute.</summary>
    public static DemoScenario Paradrop { get; } = new(
        "acc-paradrop", "Paratroopers",
        "A plane drops paratroopers on open ground, ninety seconds after the start at the latest. To see: each soldier hangs under his chute and comes down with it, none drops as a chute alone, none stands inside the plane.",
        1800,
        [new("centre", "AmericaCommandCenter", 1, -260, -80, 0f)],
        [new(30, "centre", ScenarioOrderKind.UsePower, X: 40f, Y: 0f, Template: "SuperweaponParadropAmerica")],
        40, 0, 20f, 42f, 420f)
    {
        PowersReady = true,
        Sciences = ["SCIENCE_Paradrop1"],
    };

    /// <summary>The Black Lotus at work at once: a person should not have to build up to her to judge the arc of data.</summary>
    public static DemoScenario Lotus { get; } = new(
        "acc-lotus", "The Black Lotus",
        "The Black Lotus captures a building. To see: while she works an arc of data runs from her to the building, does not go through the ground, and ends when she does.",
        1500,
        [
            new("lotus", "ChinaInfantryBlackLotus", 1, -70, 0, 0f),
            new("barracks", "AmericaBarracks", 2, 60, 0, 180f),
        ],
        [new(30, "lotus", ScenarioOrderKind.Capture, "barracks")],
        0, 0, 20f, 42f, 300f)
    {
        HumanFaction = "FactionChina",
        OpponentFaction = "FactionAmerica",
        PowersReady = true,
    };
}
