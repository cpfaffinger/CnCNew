using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Presentation;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.World;

namespace CnCNew.PresentationTool;

/// <summary>
/// The checks of the factory-exit scenes (<see cref="FactoryExits"/>), measured once per original frame: where a produced unit appears relative to its building (the building's own
/// frame: x towards the exit side, y to its left), where it is during its first three seconds, when and on which side it leaves the building's box, where it comes to rest, and what
/// the door of the building shows meanwhile. The yardstick is the original (<c>ProductionUpdate::update</c>, <c>DefaultProductionExitUpdate::exitObjectViaDoor</c>,
/// <c>QueueProductionExitUpdate::exitObjectViaDoor</c>): the unit is created at <c>UnitCreatePoint</c> once the door is fully open and then follows its exit path to
/// <c>NaturalRallyPoint</c> pushed two pathfinding cells (20 units) further out, ignoring its own factory as an obstacle.
/// </summary>
internal sealed class FactoryExitAudit
{
    /// <summary><c>getNaturalRallyPoint(…, offset = TRUE)</c>: the point is moved 2 * PATHFIND_CELL_SIZE_F along its own direction.</summary>
    private const float RallyOffset = 20f;

    private const float SpawnTolerance = 1.5f;
    private const float RestTolerance = 30f;
    private const float LaneTolerance = 20f;

    private sealed class Track(EntityId id, string template, long born)
    {
        public EntityId Id { get; } = id;
        public string Template { get; } = template;
        public long Born { get; } = born;
        public List<(long Frame, Vector2 Local, bool Drawn, float Speed)> Path { get; } = [];
        public string DoorAtBirth { get; set; } = string.Empty;
    }

    private sealed class Site(FactoryExitSite definition, EntityId building, Vector2 create, Vector2 rally, float major, float minor, ProductionData? production)
    {
        public FactoryExitSite Definition { get; } = definition;
        public EntityId Building { get; } = building;
        public Vector2 Create { get; } = create;
        public Vector2 Rally { get; } = rally;
        public float Major { get; } = major;
        public float Minor { get; } = minor;
        public ProductionData? Production { get; } = production;
        public List<Track> Units { get; } = [];
        public List<(long Frame, string Door)> Doors { get; } = [];
        public string ExitModule { get; init; } = string.Empty;

        public bool Inside(Vector2 local) => MathF.Abs(local.X) <= Major && MathF.Abs(local.Y) <= Minor;
    }

    private readonly List<Site> _sites = [];
    private readonly HashSet<uint> _known = [];
    private readonly LiveScenario _live;

    private readonly long _firstOrder;

    private FactoryExitAudit(LiveScenario live)
    {
        _live = live;
        _firstOrder = live.Scenario.Orders.IsEmpty ? 0 : live.Scenario.Orders.Min(order => order.Frame);
    }

    public static FactoryExitAudit? Create(GameData data, LiveScenario live)
    {
        var sites = FactoryExits.SitesOf(live.Scenario);
        if (sites.IsEmpty) return null;
        var audit = new FactoryExitAudit(live);
        foreach (var entity in live.World.Entities) audit._known.Add(entity.Id.Value);
        foreach (var site in sites)
        {
            var building = live.World.FindEntity(live.Tags[site.Tag]) ?? throw new InvalidOperationException($"'{site.Tag}' is not in the world.");
            var definition = data.Graph.Definitions.OfType<ObjectDefinition>().Single(candidate => candidate.OriginalName.Equals(site.Template, StringComparison.OrdinalIgnoreCase));
            var exit = definition.Modules.LastOrDefault(module => module.Name.EndsWith("ProductionExitUpdate", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"{site.Template} has no production exit module.");
            audit._sites.Add(new Site(site, building.Id, Point(exit, "UnitCreatePoint"), Point(exit, "NaturalRallyPoint"),
                ToFloat(building.Template.GeometryMajorRadius), ToFloat(building.Template.GeometryMinorRadius), building.Modules.OfType<ProductionUpdate>().FirstOrDefault()?.Data)
            {
                ExitModule = exit.Name,
            });
        }
        return audit;
    }

    private static Vector2 Point(ModuleConfiguration module, string name)
    {
        var text = module.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.OriginalText ?? string.Empty;
        float Axis(string axis) => Regex.Match(text, axis + @"\s*:\s*(-?[0-9.]+)") is { Success: true } match ? float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0f;
        return new Vector2(Axis("X"), Axis("Y"));
    }

    private static float ToFloat(CnCNew.Simulation.Numerics.Fixed value) => FixedConvert.ToSingle(value);

    /// <summary>Called once per original frame, after the step.</summary>
    public void Sample(RenderFrame frame)
    {
        var now = _live.Clock.OriginalFrames;
        var world = _live.World;
        foreach (var site in _sites)
        {
            if (world.FindEntity(site.Building) is not { } building) continue;
            var door = DoorOf(frame.Drawables.FirstOrDefault(value => value.Entity == site.Building)?.Conditions.ToString() ?? string.Empty);
            if (site.Doors.Count == 0 || site.Doors[^1].Door != door) site.Doors.Add((now, door));

            foreach (var entity in world.Entities)
            {
                if (entity.Owner != building.Owner || !entity.Template.Name.Equals(site.Definition.Produces, StringComparison.OrdinalIgnoreCase) || !_known.Add(entity.Id.Value)) continue;
                // What appears before the first order was not built here (the free worker of a GLA supply stash).
                if (now < _firstOrder) continue;
                site.Units.Add(new Track(entity.Id, entity.Template.Name, now) { DoorAtBirth = door });
            }

            var cos = MathF.Cos(ToFloat(building.Angle));
            var sin = MathF.Sin(ToFloat(building.Angle));
            foreach (var track in site.Units)
            {
                if (world.FindEntity(track.Id) is not { } unit) continue;
                var delta = new Vector2(ToFloat(unit.X - building.X), ToFloat(unit.Y - building.Y));
                var local = new Vector2(delta.X * cos + delta.Y * sin, -delta.X * sin + delta.Y * cos);
                track.Path.Add((now, local, frame.Drawables.Any(value => value.Entity == track.Id), ToFloat(unit.Speed) * world.Time.TicksPerSecond));
            }
        }
    }

    private static string DoorOf(string conditions)
    {
        foreach (var state in new[] { "DOOR_1_OPENING", "DOOR_1_WAITING_OPEN", "DOOR_1_WAITING_TO_CLOSE", "DOOR_1_CLOSING" })
            if (conditions.Contains(state, StringComparison.Ordinal)) return state["DOOR_1_".Length..];
        return "closed";
    }

    public List<string> Check()
    {
        var failures = new List<string>();
        var time = _live.World.Time;
        foreach (var site in _sites)
        {
            var name = site.Definition.Template;
            var doors = site.Production is { NumDoorAnimations: > 0 };
            var rallyOut = site.Rally + (site.Rally.LengthSquared() > 0 ? Vector2.Normalize(site.Rally) * RallyOffset : Vector2.Zero);
            Console.WriteLine();
            Console.WriteLine($"{name} ({site.Definition.Tag}) builds {site.Definition.Produces}: {site.ExitModule}, box {site.Major:F0} x {site.Minor:F0} (half extents), UnitCreatePoint ({site.Create.X:F0},{site.Create.Y:F0}), " +
                              $"NaturalRallyPoint ({site.Rally.X:F0},{site.Rally.Y:F0}), exit path ends at ({rallyOut.X:F0},{rallyOut.Y:F0})");
            Console.WriteLine(doors
                ? $"  door: opening {time.ToOriginalFrames(site.Production!.DoorOpeningTicks)}, waiting open {time.ToOriginalFrames(site.Production.DoorWaitOpenTicks)}, closing {time.ToOriginalFrames(site.Production.DoorCloseTicks)} frames; " +
                  $"timeline {string.Join(", ", site.Doors.Select(entry => $"{entry.Frame}:{entry.Door}"))}"
                : "  door: the building has no door animation (NumDoorAnimations 0)");
            if (site.Units.Count < FactoryExits.OrdersPerBuilding)
                failures.Add($"{name}: only {site.Units.Count} unit(s) appeared for {FactoryExits.OrdersPerBuilding} orders");

            foreach (var (track, number) in site.Units.Select((track, index) => (track, index + 1)))
            {
                if (track.Path.Count == 0) continue;
                var who = $"{name} unit {number} ({track.Template}#{track.Id.Value})";
                var spawn = track.Path[0].Local;
                var spawnError = Vector2.Distance(spawn, site.Create);
                var firstOutside = track.Path.FirstOrDefault(entry => !site.Inside(entry.Local));
                var left = firstOutside.Frame != 0;
                var last = track.Path[^1];
                var restFrom = track.Path.LastOrDefault(entry => entry.Speed > 0.01f).Frame;
                var moved = Vector2.Distance(last.Local, spawn);
                var drawn = track.Path.Count(entry => entry.Drawn);
                Console.WriteLine($"  unit {number} {track.Template}#{track.Id.Value}: appears in frame {track.Born} at ({spawn.X:F1},{spawn.Y:F1}), {spawnError:F1} from UnitCreatePoint, {(site.Inside(spawn) ? "inside" : "outside")} the box, door {track.DoorAtBirth}");
                Console.WriteLine($"    first 3 s: {string.Join(" ", track.Path.Where(entry => (entry.Frame - track.Born) % 15 == 0 && entry.Frame - track.Born <= 90).Select(entry => $"+{entry.Frame - track.Born}:({entry.Local.X:F0},{entry.Local.Y:F0})"))}");
                Console.WriteLine($"    {(left ? $"outside the box from frame {firstOutside.Frame} (+{firstOutside.Frame - track.Born}) at ({firstOutside.Local.X:F1},{firstOutside.Local.Y:F1})" : "never leaves the box")}; " +
                                  $"last moves in frame {restFrom}; rests at ({last.Local.X:F1},{last.Local.Y:F1}), {Vector2.Distance(last.Local, rallyOut):F1} from the end of the exit path, {moved:F1} from where it appeared; drawn in {drawn} of {track.Path.Count} frames");

                if (spawnError > SpawnTolerance) failures.Add($"{who} appears {spawnError:F1} away from UnitCreatePoint");
                if (doors && track.DoorAtBirth != "WAITING_OPEN") failures.Add($"{who} appears while the door is {track.DoorAtBirth} (the original creates it once the door is fully open)");
                if (!left) failures.Add($"{who} never leaves the building's box (it stays at ({last.Local.X:F0},{last.Local.Y:F0}); the original sends it to NaturalRallyPoint)");
                else if (firstOutside.Local.X < site.Major || MathF.Abs(firstOutside.Local.Y - site.Rally.Y) > LaneTolerance)
                    failures.Add($"{who} leaves the box at ({firstOutside.Local.X:F0},{firstOutside.Local.Y:F0}), not through the exit side at ({site.Major:F0},{site.Rally.Y:F0})");
                if (left && Vector2.Distance(last.Local, rallyOut) > RestTolerance) failures.Add($"{who} rests {Vector2.Distance(last.Local, rallyOut):F0} away from the end of its exit path ({rallyOut.X:F0},{rallyOut.Y:F0})");
                if (drawn < track.Path.Count) failures.Add($"{who} is not drawn in {track.Path.Count - drawn} of {track.Path.Count} frames");
            }

            if (doors && site.Units.Count > 0)
            {
                var lastBorn = site.Units.Max(track => track.Born);
                var closing = site.Doors.FirstOrDefault(entry => entry.Frame > lastBorn && entry.Door == "CLOSING");
                var closed = site.Doors.FirstOrDefault(entry => entry.Frame > lastBorn && entry.Door == "closed");
                if (closing.Frame == 0 || closed.Frame == 0) failures.Add($"{name}: the door does not close after the last unit (timeline ends with {site.Doors[^1].Door})");
                else Console.WriteLine($"  door after the last unit (frame {lastBorn}): closing from {closing.Frame}, closed from {closed.Frame}");
                if (!site.Doors.Any(entry => entry.Door == "OPENING")) failures.Add($"{name}: the door never shows DOOR_1_OPENING");
            }
        }
        return failures;
    }
}
