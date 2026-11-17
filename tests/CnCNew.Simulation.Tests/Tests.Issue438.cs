using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>Issue #438: the healing auras of <c>AutoHealBehavior</c> - whom they heal, whether two add up, the map's edge, a missing delay and a single burst.</summary>
static partial class Tests
{
    private const string HealAura438Ini = """
        Object AuraPatient438
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object RadiusHealer438
          KindOf = SELECTABLE STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AutoHealBehavior Heal01
            StartsActive = Yes
            HealingAmount = 2
            HealingDelay = 1000
            Radius = 100.0
          End
        End
        Object WholePlayerHealer438
          KindOf = SELECTABLE STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AutoHealBehavior Heal01
            StartsActive = Yes
            HealingAmount = 2
            HealingDelay = 1000
            AffectsWholePlayer = Yes
          End
        End
        Object UndelayedHealer438
          KindOf = SELECTABLE STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AutoHealBehavior Heal01
            StartsActive = Yes
            HealingAmount = 2
            Radius = 100.0
          End
        End
        Object UpgradedHealer438
          KindOf = SELECTABLE STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AutoHealBehavior Heal01
            TriggeredBy = Upgrade_Detonator
            HealingAmount = 2
            Radius = 100.0
          End
        End
        """;

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) HealAura438World(SimulationRate rate, TerrainMap? terrain = null)
    {
        var files = new Dictionary<string, string>(Phase5Files()) { ["Data/INI/Object/HealAuraFixture438.ini"] = HealAura438Ini };
        var fixture = new SyntheticFixture(rate, files: files);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain?.Fingerprint ?? ""), fixture.Templates, terrain);
        return (world, new Feeder(world), fixture);
    }

    private static Entity HealAura438Find(SimulationWorld world, string template) => world.Entities.Single(entity => entity.Template.Name == template);

    /// <summary>The ticks within <paramref name="frames"/> frames of the original at which the object's health rose, and by how much in all.</summary>
    private static (List<long> Ticks, Fixed Gain) HealAura438Heals(SimulationWorld world, Entity patient, int frames)
    {
        var ticks = new List<long>();
        var start = patient.Body!.Health;
        var health = start;
        for (var step = 0; step < world.Frames(frames); step++)
        {
            world.Step();
            if (patient.Body!.Health > health) ticks.Add(world.Tick - 1);
            health = patient.Body.Health;
        }
        return (ticks, health - start);
    }

    /// <summary>
    /// <c>AutoHealBehavior::update</c> with a radius: the scan takes the healer's allies (<c>PartitionFilterRelationship::ALLOW_ALLIES</c>), not only its own player's objects, and
    /// an enemy's object not. The port healed its own player's objects only. At 30, 60 and 120 Hz, the same heals at every rate.
    /// </summary>
    public static void ARadiusAuraHealsItsAllies()
    {
        var gains = new List<Fixed>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            foreach (var allied in new[] { true, false })
            {
                var (world, feeder, _) = HealAura438World(rate);
                if (allied)
                {
                    feeder.SetRelationship(1, 2, Relationship.Allies);
                    feeder.SetRelationship(2, 1, Relationship.Allies);
                }
                feeder.Spawn(2, "AuraPatient438", owner: 2, x: 10, y: 0);
                StepFrames(world, 1);
                var patient = HealAura438Find(world, "AuraPatient438");
                feeder.Damage(patient.Id.Value, 50);
                StepFrames(world, 1);
                feeder.Spawn(1, "RadiusHealer438", owner: 1, x: 0, y: 0);
                var (ticks, gain) = HealAura438Heals(world, patient, 300);
                if (allied)
                {
                    Check.That(ticks.Count is 10 or 11 && gain == Fixed.FromInt(2 * ticks.Count), $"{rate}: the aura heals its ally every second ({ticks.Count} heals, {gain}).");
                    gains.Add(gain);
                }
                else Check.That(ticks.Count == 0, $"{rate}: the aura heals no enemy ({ticks.Count} heals).");
            }
        }
        Check.That(gains.Distinct().Count() == 1, $"The same heals at every rate ({string.Join(" ", gains)}).");
    }

    /// <summary>
    /// <c>pulseHealObject</c> with a radius heals through <c>Object::attemptHealingFromSoleBenefactor</c>: an object takes the heal of one aura at a time, the one that healed it
    /// first, for as long as that one renews its claim every <c>HealingDelay</c>, so two auras over one object heal it as much as one. The claim is kept across a save. The port
    /// healed the object by every aura. At 30, 60 and 120 Hz.
    /// </summary>
    public static void TwoHealingAurasDoNotStack()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, feeder, fixture) = HealAura438World(rate);
            feeder.Spawn(1, "AuraPatient438", owner: 1, x: 10, y: 0);
            StepFrames(world, 1);
            var patient = HealAura438Find(world, "AuraPatient438");
            feeder.Damage(patient.Id.Value, 80);
            StepFrames(world, 1);
            feeder.Spawn(1, "RadiusHealer438", owner: 1, x: 0, y: 0);
            feeder.Spawn(1, "RadiusHealer438", owner: 1, x: 20, y: 0);
            var (early, earlyGain) = HealAura438Heals(world, patient, 31);
            Check.That(early.Count == 1 && earlyGain == Fixed.FromInt(2), $"{rate}: one heal of one aura in the first second ({early.Count} heals, {earlyGain}).");
            var holder = patient.SoleHealingBenefactor;
            Check.That(holder.IsValid, $"{rate}: one aura holds the patient after the first second.");

            var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, world.Terrain);
            Check.That(restored.StateHash == world.StateHash, $"{rate}: a restored world has the same hash.");
            var restoredPatient = restored.FindEntity(patient.Id)!;
            Check.That(restoredPatient.SoleHealingBenefactor == holder && restoredPatient.SoleHealingBenefactorUntil == patient.SoleHealingBenefactorUntil,
                $"{rate}: the claim is kept across a save.");

            var (ticks, gain) = HealAura438Heals(world, patient, 300);
            var (restoredTicks, _) = HealAura438Heals(restored, restoredPatient, 300);
            Check.That(ticks.Count is 10 or 11 && gain == Fixed.FromInt(2 * ticks.Count), $"{rate}: two auras heal as much as one ({ticks.Count} heals, {gain}).");
            Check.That(ticks.Zip(ticks.Skip(1), (a, b) => b - a).All(span => span == world.Frames(30)), $"{rate}: one heal a second ({string.Join(" ", ticks.Zip(ticks.Skip(1), (a, b) => b - a))}).");
            Check.That(patient.SoleHealingBenefactor == holder, $"{rate}: the aura that holds the patient keeps it.");
            Check.That(restoredTicks.SequenceEqual(ticks) && restored.StateHash == world.StateHash, $"{rate}: the restored world heals the same.");
        }
    }

    /// <summary>
    /// <c>checkForAutoHeal</c> skips an object off the map (<c>Object::isOffMap</c>), and the radius scan takes only what is as much on or off the map as the healer
    /// (<c>PartitionFilterSameMapStatus</c>): an off-map patient is healed by neither aura on the map, and by a radius aura off the map. The port healed it. At 30, 60 and 120 Hz.
    /// </summary>
    public static void AHealingAuraKeepsToItsSideOfTheMapEdge()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            foreach (var (healer, healerX, patientX, heals) in new[]
            {
                ("WholePlayerHealer438", 10, -20, false), ("WholePlayerHealer438", 10, 20, true),
                ("RadiusHealer438", 10, -20, false), ("RadiusHealer438", 10, 40, true), ("RadiusHealer438", -40, -20, true),
            })
            {
                var (world, feeder, _) = HealAura438World(rate, NavTerrain());
                feeder.Spawn(1, "AuraPatient438", owner: 1, x: patientX, y: 20);
                StepFrames(world, 1);
                var patient = HealAura438Find(world, "AuraPatient438");
                feeder.Damage(patient.Id.Value, 50);
                StepFrames(world, 1);
                feeder.Spawn(1, healer, owner: 1, x: healerX, y: 20);
                var (ticks, _) = HealAura438Heals(world, patient, 100);
                Check.That(heals ? ticks.Count >= 3 : ticks.Count == 0,
                    $"{rate}: {healer} at x {healerX} {(heals ? "heals" : "does not heal")} a patient at x {patientX} ({ticks.Count} heals).");
            }
        }
    }

    /// <summary>
    /// <c>HealingDelay</c> defaults to <c>UINT_MAX</c>. The constructor of an aura that starts active draws <c>GameLogicRandomValue(1, UINT_MAX)</c>, which is <c>(1, -1)</c>: any
    /// value but zero added to the frame without sign, beyond <c>UPDATE_SLEEP_FOREVER</c> forever - one draw, and the aura does not wake in any match. The port woke it within a
    /// frame and healed every frame. At 30, 60 and 120 Hz.
    /// </summary>
    public static void AnAuraWithoutADelayThatStartsActiveDoesNotWake()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, feeder, _) = HealAura438World(rate);
            feeder.Spawn(1, "AuraPatient438", owner: 1, x: 10, y: 0);
            StepFrames(world, 1);
            var patient = HealAura438Find(world, "AuraPatient438");
            feeder.Damage(patient.Id.Value, 50);
            StepFrames(world, 1);
            var state = new uint[6];
            world.LogicRandom.CopyState(state);
            var expected = new CnCNew.Simulation.Random.SageRandom(0);
            expected.SetState(state);
            var drawn = expected.Range(1, -1);
            var now = (uint)world.Time.ToOriginalFrames(world.Tick);
            feeder.Spawn(1, "UndelayedHealer438", owner: 1, x: 0, y: 0);
            world.Step();
            var after = new uint[6];
            world.LogicRandom.CopyState(after);
            var stateAfterOneDraw = new uint[6];
            expected.CopyState(stateAfterOneDraw);
            Check.That(after.SequenceEqual(stateAfterOneDraw), $"{rate}: the healer's birth draws once from the logic stream.");
            Check.That(unchecked(now + (uint)drawn) > SimulationTiming.SleepForever, $"{rate}: the draw {drawn} wakes the aura beyond forever.");
            var module = HealAura438Find(world, "UndelayedHealer438").Modules.OfType<AutoHealBehavior>().Single();
            Check.That(module.NextWake >= SimulationTiming.SleepForever, $"{rate}: the aura sleeps for good ({module.NextWake}).");
            var (ticks, _) = HealAura438Heals(world, patient, 300);
            Check.That(ticks.Count == 0, $"{rate}: and heals nothing ({ticks.Count} heals).");
        }
    }

    /// <summary>
    /// An aura without a <c>HealingDelay</c> that an upgrade wakes pulses every frame (<c>UPDATE_SLEEP(UINT_MAX)</c> is the signed enum's -1, which <c>GameLogic::update</c> makes
    /// <c>UPDATE_SLEEP_NONE</c>), and its non-stacking claim ends a frame before it begins (<c>now + UINT_MAX</c>), so two such auras over one object both heal it every frame. On
    /// the code before, the claim held for the frame and only one healed. At 30, 60 and 120 Hz.
    /// </summary>
    public static void AurasWithoutADelayHealEveryFrameAndAddUp()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, feeder, _) = HealAura438World(rate);
            feeder.Spawn(1, "AuraPatient438", owner: 1, x: 10, y: 0);
            feeder.Spawn(1, "UpgradedHealer438", owner: 1, x: 0, y: 0);
            feeder.Spawn(1, "UpgradedHealer438", owner: 1, x: 20, y: 0);
            StepFrames(world, 1);
            var patient = HealAura438Find(world, "AuraPatient438");
            feeder.Damage(patient.Id.Value, 80);
            StepFrames(world, 1);
            foreach (var healer in world.Entities.Where(entity => entity.Template.Name == "UpgradedHealer438").ToList())
                feeder.GrantUpgrade(1, "Upgrade_Detonator", target: healer.Id.Value);
            StepFrames(world, 3);
            var (_, gain) = HealAura438Heals(world, patient, 10);
            Check.That(gain == Fixed.FromInt(40), $"{rate}: two auras without a delay heal 2 each every frame ({gain} in 10 frames).");
        }
    }
}
