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

    /// <summary>
    /// F-47 (R-22): <c>StructureToppleUpdate::doDamageLine</c> fires every 25 units from <c>-facingWidth</c> while below <c>facingWidth</c> and once at <c>facingWidth</c>, each shot
    /// offset by <c>(i * Sin(toppleAngle), i * Cos(toppleAngle))</c>: across the fall at 0 and 90 degrees, along the fall at 45 degrees - kept as the original writes it.
    /// The port fired one shot at the leading edge and no line at all.
    /// </summary>
    public static void ReforgedToppleCrushLine()
    {
        var width = Fixed.FromInt(30);
        var along = Fixed.FromInt(20);
        (Fixed, Fixed)[] East = [..StructureToppleUpdate.DamageLineTargets(Fixed.Zero, Fixed.Zero, Fixed.One, Fixed.Zero, along, width)];
        Check.That(East.Length == 4 && East.All(point => point.Item1 == along) && East.Select(point => point.Item2.Round()).SequenceEqual(new long[] { -30, -5, 20, 30 }),
            $"Falling east (0 degrees): shots at -30, -5, 20 and the edge 30 across the fall ({string.Join(" ", East)}).");
        (Fixed, Fixed)[] North = [..StructureToppleUpdate.DamageLineTargets(Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.One, along, width)];
        Check.That(North.All(point => point.Item2 == along) && North.Select(point => point.Item1.Round()).SequenceEqual(new long[] { -30, -5, 20, 30 }),
            "Falling north (90 degrees): the same shots across the fall.");
        var half = Fixed.Sqrt(Fixed.One / Fixed.FromInt(2));
        (Fixed, Fixed)[] Diagonal = [..StructureToppleUpdate.DamageLineTargets(Fixed.Zero, Fixed.Zero, half, half, along, width)];
        Check.That(Diagonal.All(point => point.Item1 == point.Item2), "Falling north-east (45 degrees): every shot lies on the line of fall itself, as the original writes the offset.");
    }
}

