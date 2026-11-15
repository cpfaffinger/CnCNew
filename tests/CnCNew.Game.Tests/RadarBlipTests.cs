using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Tests;

/// <summary>
/// Issue 387: the radar must tell a player nothing his view does not (<c>W3DRadar::renderObjectList</c>: an object whose shrouded status for the local player is fogged or
/// shrouded is skipped, so is an enemy's undetected stealthed object, and a <c>LOCAL_UNIT_ONLY</c> object of somebody else; <c>Radar::addObject</c> takes only a visible
/// <c>RadarPriority</c>). The client drew every unit and structure of the frame, the buildings remembered in the fog with them - the supply docks and the map's buildings across
/// a map the player had never looked at, as white and coloured blips.
/// </summary>
internal static class RadarBlipTests
{
    private static readonly int StealthedBit = ObjectStatusBits.Index("STEALTHED");
    private static readonly int DetectedBit = ObjectStatusBits.Index("DETECTED");
    private static readonly int DisguisedBit = ObjectStatusBits.Index("DISGUISED");

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(RadarBlipTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP RadarBlipTests (pass --content-dir <dir>)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Real data: on Alpine Assault the radar shows only what the player sees now - no building remembered in the fog, no unit out of sight, no stranger's car (#387)",
            () => NothingHidden(data, "Alpine Assault", "FactionAmerica", "FactionChina"));
        harness.Run("Real data: on Tournament Desert the radar shows only what the player sees now, against a GLA computer with its stealth (#387)",
            () => NothingHidden(data, "Tournament Desert", "FactionChina", "FactionGLA"));
        harness.Run("Real data: the radar's priority - a template's RadarPriority, a garrisonable or capturable building without one, never NOT_ON_RADAR (#387)", () => Priorities(data));
        return harness.Failures;
    }

    /// <summary>
    /// A skirmish the computer plays for the viewer (so its units scout, fight and lose sight of the enemy again), checked every five seconds of game time for six minutes: every
    /// blip of another player's object is an object the viewer may see now, on the radar by its priority, not somebody else's <c>LOCAL_UNIT_ONLY</c>, no undetected enemy stealth.
    /// The old rule (every unit and structure the frame holds) is counted beside it: it showed the remembered buildings.
    /// </summary>
    private static void NothingHidden(GameData data, string map, string faction, string opponent)
    {
        var mapPath = data.Maps.First(path => path.Contains(map, StringComparison.OrdinalIgnoreCase));
        var setup = new MatchSetup(mapPath, faction, [opponent], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard) { AutoPlay = true, Seed = 11 };
        var match = LiveMatch.Create(data, setup, new Vector2(1280, 720));
        var blips = new RadarBlips(data.Graph);
        var world = match.World;
        var viewer = match.Human;
        var (samples, oldLeaks, ghostsLeftOut, foreignSeen, rememberedSeen) = (0, 0, 0, 0, 0);
        var leakKinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var everSeen = new HashSet<uint>();
        var everyFrames = CnCNew.Tests.TestTime.FramesPerSecond * 5;
        for (var frame = 1; frame <= CnCNew.Tests.TestTime.FramesPerSecond * 360; frame++)
        {
            match.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            foreach (var entity in world.EntitiesVisibleTo(viewer)) everSeen.Add(entity.Id.Value);
            if (frame % everyFrames != 0) continue;
            samples++;
            var drawn = match.Frame;
            var shown = blips.Shown(drawn, world, viewer);
            TestHarness.Assert(match.MinimapDots().Count == shown.Count, "the minimap's dots are the blips");
            foreach (var drawable in shown)
            {
                var entity = world.FindEntity(drawable.Entity)!;
                var priority = blips.PriorityOf(entity);
                TestHarness.Assert(RadarBlips.IsOnRadar(priority), $"{entity.Template.Name} is on the radar by its priority ({priority})");
                if (entity.Owner == viewer || world.AreAllies(viewer, entity.Owner)) continue;
                foreignSeen++;
                TestHarness.Assert(!drawable.IsGhost && world.CanPlayerSee(viewer, entity), $"t={world.Tick}: a blip of {entity.Template.Name} of player {entity.Owner.Value}, which the viewer does not see");
                TestHarness.Assert(priority != RadarPriority.LocalUnitOnly, $"t={world.Tick}: {entity.Template.Name} of player {entity.Owner.Value} is on the radar of its own player only");
                TestHarness.Assert(!(entity.HasStatus(StealthedBit) && world.RelationshipOf(viewer, entity.Owner) == Relationship.Enemies && !entity.HasStatus(DetectedBit) && !entity.HasStatus(DisguisedBit)),
                    $"t={world.Tick}: an undetected stealthed {entity.Template.Name}");
            }
            // The old rule of the client: every unit and structure of the frame that is an object of the logic.
            foreach (var drawable in drawn.Drawables)
            {
                if (!drawable.Entity.IsValid || !(drawable.IsUnit || drawable.IsStructure)) continue;
                var entity = world.FindEntity(drawable.Entity);
                if (drawable.IsGhost) ghostsLeftOut++;
                if (entity is not null && (entity.Owner == viewer || world.AreAllies(viewer, entity.Owner))) continue;
                var hidden = drawable.IsGhost || entity is null || !world.CanPlayerSee(viewer, entity) || (blips.PriorityOf(entity) == RadarPriority.LocalUnitOnly);
                if (!hidden) continue;
                oldLeaks++;
                var name = entity?.Template.Name ?? drawable.TemplateName;
                if (entity is not null && everSeen.Contains(entity.Id.Value)) rememberedSeen++;
                var kind = $"{name} (player {drawable.Owner.Value}{(drawable.IsGhost ? ", remembered" : string.Empty)}{(entity is not null && everSeen.Contains(entity.Id.Value) ? ", seen once" : ", never seen")}, colour {drawable.TeamColor.Red},{drawable.TeamColor.Green},{drawable.TeamColor.Blue})";
                leakKinds[kind] = leakKinds.GetValueOrDefault(kind) + 1;
            }
        }
        Console.WriteLine($"  {map}: {samples} samples, {foreignSeen} blips of others' objects in sight; the old rule drew {oldLeaks} blips of hidden objects ({rememberedSeen} of them seen once before), {ghostsLeftOut} remembered buildings left out now");
        foreach (var (kind, count) in leakKinds.OrderByDescending(pair => pair.Value).Take(25)) Console.WriteLine($"    {count,5} x {kind}");
        TestHarness.Assert(ghostsLeftOut > 0, "the match holds buildings remembered in the fog, which the radar leaves out");
        TestHarness.Assert(foreignSeen > 0, "the viewer saw other players' objects on the radar while they were in sight");
    }

    private static void Priorities(GameData data)
    {
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var match = LiveMatch.Create(data, new MatchSetup(mapPath, "FactionAmerica", ["FactionChina"], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard), new Vector2(1280, 720));
        // The patch's starting object makes the command centre and the builder in its first update.
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond; frame++) match.Advance(CnCNew.Tests.TestTime.FrameSeconds);
        var blips = new RadarBlips(data.Graph);
        var world = match.World;
        Entity First(Func<Entity, bool> where, string what) => world.Entities.FirstOrDefault(where) ?? throw new InvalidOperationException($"no {what} on the map");
        var center = First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("COMMANDCENTER"), "command centre");
        TestHarness.Assert(blips.PriorityOf(center) == RadarPriority.Structure, $"a command centre is a structure ({blips.PriorityOf(center)})");
        var dozer = First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER"), "dozer");
        TestHarness.Assert(blips.PriorityOf(dozer) == RadarPriority.Unit, $"a dozer is a unit ({blips.PriorityOf(dozer)})");
        var garrisonable = world.Entities.Where(entity => entity.Modules.OfType<IContainerModule>().FirstOrDefault() is { IsGarrisonable: true }).ToArray();
        TestHarness.Assert(garrisonable.Length > 0 && garrisonable.All(entity => RadarBlips.IsOnRadar(blips.PriorityOf(entity))), $"every garrisonable building of the map is on the radar ({garrisonable.Length})");
        var notOnRadar = world.Entities.Where(entity => blips.PriorityOf(entity) is RadarPriority.Invalid or RadarPriority.NotOnRadar).Select(entity => entity.Template.Name).Distinct().ToArray();
        TestHarness.Assert(notOnRadar.Length > 0, "the map has objects the radar never shows");
        var cars = world.Entities.Where(entity => blips.PriorityOf(entity) == RadarPriority.LocalUnitOnly).Select(entity => entity.Template.Name).Distinct().ToArray();
        Console.WriteLine($"  never on the radar: {string.Join(", ", notOnRadar.Take(12))}; only its own player's: {string.Join(", ", cars.Take(12))}");
    }
}
