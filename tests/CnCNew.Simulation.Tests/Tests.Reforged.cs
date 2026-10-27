using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;

/// <summary>
/// Phase 40 (docs/REFORGED-FIXES.md): the places where Zero Hour Reforged's changelog led to a difference of this port from the original (kind C), each tested against the
/// original's source.
/// </summary>
static partial class Tests
{
    /// <summary>
    /// F-46 (R-21): <c>CountermeasuresBehavior::calculateCountermeasureToDivertTo</c> walks back from the newest flare and steps back only past a flare that is gone, so the
    /// newest flare that exists is chosen even when an older one is nearer; each vanished flare costs one of the <c>VolleySize</c> passes. The port had taken the nearest of
    /// the last <c>VolleySize</c> flares (Reforged's change, not the original).
    /// </summary>
    public static void ReforgedCountermeasureTakesTheNewestFlare()
    {
        EntityId[] flares = [new(11), new(12), new(13)];
        Fixed? Near(EntityId id) => id.Value == 13 ? Fixed.FromInt(100) : Fixed.FromInt(1);
        Check.That(CountermeasuresBehavior.PickFlare(flares, 2, Near) == new EntityId(13), "The newest flare is taken, though the one before it is nearer.");
        Fixed? NewestGone(EntityId id) => id.Value == 13 ? null : Fixed.FromInt(5);
        Check.That(CountermeasuresBehavior.PickFlare(flares, 2, NewestGone) == new EntityId(12), "A flare that is gone is stepped over at the cost of a pass.");
        Fixed? TwoGone(EntityId id) => id.Value == 11 ? Fixed.FromInt(5) : null;
        Check.That(!CountermeasuresBehavior.PickFlare(flares, 2, TwoGone).IsValid, "Two passes spent on two vanished flares find nothing.");
        Check.That(CountermeasuresBehavior.PickFlare(flares, 3, TwoGone) == new EntityId(11), "A third pass reaches the oldest flare.");
        Check.That(!CountermeasuresBehavior.PickFlare([], 2, Near).IsValid, "No flare, no diversion.");
        Fixed? AllGone(EntityId id) => null;
        Check.That(!CountermeasuresBehavior.PickFlare(flares, 9, AllGone).IsValid, "The walk stops at the oldest flare instead of reading before it.");
    }
}
