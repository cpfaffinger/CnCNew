using System.Collections.Immutable;

namespace CnCNew.Game.Live;

/// <summary>
/// The staged scenes of the acceptance catalogue (<c>scripts/acceptance/catalogue.json</c>, <c>docs/ACCEPTANCE.md</c>) that no older demo shows: each isolates one thing a person has to
/// look at - a march to one point, wheels and suspension, boarding, arcing projectiles, a repair, a fire fight, a construction - close to the camera, and
/// <c>PresentationTool scenario-audit</c> checks each of them without a window. They reference retail templates by name only.
/// </summary>
public static class AcceptanceScenes
{
    public static ImmutableArray<DemoScenario> All => [March, Wheels, Board, Artillery, Repair, Firefight, Build];

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

    /// <summary>Frames the two construction orders of the build scene are given at.</summary>
    public const long BuildFirstOrder = 20, BuildSecondOrder = 420;

    /// <summary>A dozer builds a power plant and then a barracks: the scaffold, the rising building, the finished one.</summary>
    public static DemoScenario Build { get; } = new(
        "acc-build", "Construction",
        "A dozer builds a power plant and then a barracks. To see: the dozer drives to the site, the building rises (construction progress), and stands complete in the player's colour afterwards; to hear: the construction sound and the announcement of the finished building.",
        960,
        [new("dozer", "AmericaVehicleDozer", 1, -90, 0, 0f)],
        [
            new(BuildFirstOrder, "dozer", ScenarioOrderKind.Build, "plant", X: 30, Y: -70, Template: "AmericaPowerPlant"),
            new(BuildSecondOrder, "dozer", ScenarioOrderKind.Build, "barracks", X: 30, Y: 90, Template: "AmericaBarracks"),
        ],
        20, -15, 20f, 42f, 330f)
    {
        StartMoney = 5000,
    };
}
