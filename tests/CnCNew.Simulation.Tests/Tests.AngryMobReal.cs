using System;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// Issue 392: the angry mob's members left to themselves (MobMemberSlavedUpdate::update, SpawnBehavior::createSpawn), on real data.
static partial class Tests
{
    private static Entity[] MobMembersOf(SimulationWorld world, Entity nexus)
        => world.Entities.Where(value => value != nexus && value.Modules.OfType<MobMemberSlavedUpdate>().FirstOrDefault()?.Slaver == nexus.Id && !value.EffectivelyDead)
            .OrderBy(value => value.Id.Value).ToArray();

    /// <summary>
    /// #392: "members of a new mob were seen standing on one spot until a move". The nexus buds its members onto itself or the member closest to it, and
    /// <c>exitObjectByBudding</c> orders each onto the place it appeared on - a move whose goal <c>AIInternalMoveToState::onEnter</c> adjusts to a place nobody holds
    /// (<c>Pathfinder::adjustDestination</c>), so the mob spreads around its nexus. Ten seconds after the GLA Angry Mob appears its seven members stand each on a place of its own close
    /// to the nexus, at the same places at 30, 60 and 120 Hz.
    /// </summary>
    public static void RealAngryMobSpreadsWhenItBuds(string directory)
    {
        var real = new RealTargets(directory);
        string? places = null;
        foreach (var rate in TargetRates)
        {
            var world = real.WorldWith(rate, RuleProfile.Product);
            var nexus = SpawnReal(world, ("GLAInfantryAngryMobNexus", 1, 400, 400)).Single();
            for (var step = 0; step < world.Frames(30 * 12); step++) world.Step();
            var members = MobMembersOf(world, nexus);
            Check.That(members.Length >= 6, $"{rate}: the nexus budded its members ({members.Length}).");
            Check.That(members.Select(member => (member.X, member.Y)).Distinct().Count() == members.Length, $"{rate}: every member stands on a place of its own.");
            Check.That(members.All(member => !member.HasMoveTarget), $"{rate}: the members have arrived.");
            Check.That(members.All(member => (member.X - nexus.X).Abs() < Fixed.FromInt(40) && (member.Y - nexus.Y).Abs() < Fixed.FromInt(40)), $"{rate}: the members stand close to their nexus.");
            var here = string.Join(" ", members.Select(member => $"{member.X}/{member.Y}"));
            Check.That(places is null || places == here, $"{rate}: the members stand where they stand at 30 Hz ({here} against {places}).");
            places ??= here;
        }
    }

    /// <summary>
    /// #392, the idle branch of <c>MobMemberSlavedUpdate::update</c>: a member that stands near its nexus attacks the nexus' victim ("I remember the last target"), and when the nexus is idle
    /// ("if controlling player has pressed stop, we stop!") it goes idle and forgets it. A computer player's mob whose members are passive (they pick no victim by themselves): its nexus
    /// takes an enemy power plant in its sight and the members attack it; put to sleep, the nexus drops it, and the members stand, forget the victim and fire no more; awake again, the
    /// nexus takes the plant again and the members with it - each first shot in the same frame of the original at 30, 60 and 120 Hz.
    /// </summary>
    public static void RealAngryMobMembersFollowTheNexusVictim(string directory)
    {
        var real = new RealTargets(directory);
        string? frames = null;
        foreach (var rate in TargetRates)
        {
            var world = real.World(rate, withFix: true, computer: true);
            var nexus = SpawnReal(world, ("GLAInfantryAngryMobNexus", 1, 400, 400)).Single();
            for (var step = 0; step < world.Frames(30 * 12); step++) world.Step();
            var members = MobMembersOf(world, nexus);
            Check.That(members.Length >= 6, $"{rate}: the nexus budded its members ({members.Length}).");
            foreach (var member in members) world.SetAttitude(member, (int)Attitude.Passive);
            var ids = members.Select(member => member.Id).ToHashSet();
            int MemberShots(TickResult result, Entity victim) => result.Events.Count(value => value.Kind == SimEventKind.WeaponFired && ids.Contains(value.Entity) && value.Other == victim.Id);
            int AnyMemberShots(TickResult result) => result.Events.Count(value => value.Kind == SimEventKind.WeaponFired && ids.Contains(value.Entity));

            var plant = SpawnReal(world, ("ChinaPowerPlant", 2, 490, 400)).Single();
            long firstShot = -1;
            var shots = 0;
            for (var step = 0; step < world.Frames(30 * 20) && shots < 4; step++)
            {
                var result = world.Step();
                var fired = MemberShots(result, plant);
                if (fired > 0 && firstShot < 0) firstShot = world.Time.ToOriginalFrames(result.Tick);
                shots += fired;
            }
            Check.That(shots >= 4, $"{rate}: the members attack the plant their nexus attacks ({shots} shots).");
            Check.That(members.Any(member => world.AcquiredVictimOf(member) == plant.Id), $"{rate}: the members' victim is the nexus' victim.");

            // The nexus is put to sleep: it drops the plant and is idle, and its members stand, forget the victim and fire no more.
            world.SetAttitude(nexus, (int)Attitude.Sleep);
            for (var step = 0; step < world.Frames(30 * 3); step++) world.Step();
            Check.That(world.IsAiIdle(nexus), $"{rate}: the sleeping nexus is idle.");
            Check.That(members.All(member => !world.AcquiredVictimOf(member).IsValid), $"{rate}: with their nexus idle the members attack nothing.");
            Check.That(members.All(member => !member.Modules.OfType<MobMemberSlavedUpdate>().Single().PrimaryVictim.IsValid), $"{rate}: the members forgot the victim.");
            var idleShots = 0;
            for (var step = 0; step < world.Frames(30 * 10); step++) idleShots += AnyMemberShots(world.Step());
            Check.That(idleShots == 0, $"{rate}: the members fire no more while their nexus is idle ({idleShots} shots).");

            // Awake again, the nexus takes the plant, and the members with it.
            world.SetAttitude(nexus, (int)Attitude.Normal);
            long againShot = -1;
            for (var step = 0; step < world.Frames(30 * 20) && againShot < 0; step++)
            {
                var result = world.Step();
                if (MemberShots(result, plant) > 0) againShot = world.Time.ToOriginalFrames(result.Tick);
            }
            Check.That(againShot >= 0, $"{rate}: the members attack the plant again with their nexus.");
            var here = $"{firstShot}/{againShot}";
            Check.That(frames is null || frames == here, $"{rate}: the members' first shots fall in frames {here}, at 30 Hz in {frames}.");
            frames ??= here;
        }
    }

    /// <summary>
    /// #392, <c>Squirrelliness</c>: a member that its player commanded last may pick a victim of its own while its nexus fights one it picked itself, as long as fewer than that share of the
    /// mob is self-tasking (<c>maySpawnSelfTaskAI</c>) - the real mob's 0.05 lets exactly one of its members at a time. A computer player's mob is moved by its player towards a dozen
    /// Red Guards and left to itself: one member at a time, never two, takes a victim of its own, at 30, 60 and 120 Hz; a world saved during the fight plays on to the live world's hashes.
    /// <para>#402: the fight is the same game at the three rates - every shot of the mob and of the Red Guards falls in the same frame of the original, by the same shooter at the same
    /// victim. The order and the Red Guards come in the first tick of a frame: <c>SpawnReal</c> steps the world once, which leaves it at the start of frame 1 at 30 Hz but in the second tick
    /// of frame 0 above it, so twelve seconds later was frame 361 at 30 Hz and the second tick of frame 360 above it. A command in a frame's second tick acts on the mob from the next
    /// frame (its members' frames begin in the world's first ticks), but the Red Guards made in that tick begin their own frames there - half a frame earlier than any 30 Hz world can
    /// make them - and their first shot fell 22 frames earlier; the two halves of that game exist at 30 Hz only apart (an order in frame 361, Red Guards made in frame 360).</para>
    /// </summary>
    public static void RealAngryMobOneMemberSelfTasks(string directory)
    {
        var real = new RealTargets(directory);
        string? shotsAt30 = null;
        foreach (var rate in TargetRates)
        {
            var world = real.World(rate, withFix: true, computer: true);
            var nexus = SpawnReal(world, ("GLAInfantryAngryMobNexus", 1, 400, 400)).Single();
            for (var step = 0; step < world.Frames(30 * 12); step++) world.Step();
            while (world.Tick % world.Time.TicksPerOriginalFrame != 0) world.Step();
            var members = MobMembersOf(world, nexus);
            foreach (var unit in members.Prepend(nexus))
                Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++_sequence, world.Tick, unit.Id, Fixed.FromInt(500), Fixed.FromInt(400))).Accepted, $"{rate}: the mob is moved.");
            _ = SpawnReal(world, Enumerable.Range(0, 12).Select(index => ("ChinaInfantryRedguard", 2, 600 + 25 * (index % 4), 330 + 60 * (index / 4))).ToArray());
            var most = 0;
            var ever = 0;
            SimulationWorld? restored = null;
            var total = world.Frames(30 * 60);
            var shots = new System.Text.StringBuilder();
            for (var step = 0; step < total; step++)
            {
                foreach (var shot in world.Step().Events.Where(value => value.Kind == SimEventKind.WeaponFired))
                    shots.Append($"{world.Time.ToOriginalFrames(shot.Tick)}:{shot.Entity.Value}>{shot.Other.Value} ");
                if (restored is not null)
                {
                    restored.Step();
                    Check.That(restored.StateHash == world.StateHash, $"{rate}: the restored world agrees at step {step}.");
                }
                if (step == total / 3) restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), world.Templates, world.Configuration.DefinitionFingerprint, world.Terrain);
                var now = MobMembersOf(world, nexus).Count(member => member.Modules.OfType<MobMemberSlavedUpdate>().Single().IsSelfTasking);
                most = Math.Max(most, now);
                if (now > 0) ever++;
            }
            Check.That(ever > 0 && most == 1, $"{rate}: one member at a time picks a victim of its own ({most}).");
            var here = shots.ToString();
            Check.That(here.Length > 0, $"{rate}: the mob and the Red Guards fight.");
            if (shotsAt30 is not null && here != shotsAt30)
            {
                var (left, right) = (shotsAt30.Split(' '), here.Split(' '));
                var first = Enumerable.Range(0, Math.Min(left.Length, right.Length)).FirstOrDefault(index => left[index] != right[index], Math.Min(left.Length, right.Length));
                Check.That(false, $"{rate}: shot {first} (frame:shooter>victim) is {(first < right.Length ? right[first] : "none")}, at 30 Hz {(first < left.Length ? left[first] : "none")}.");
            }
            shotsAt30 ??= here;
        }
    }
}
