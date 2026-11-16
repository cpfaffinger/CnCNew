using System;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
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
}
