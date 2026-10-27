using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

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


static partial class Tests
{
    private static IReadOnlyDictionary<string, string> LastDamageFiles() => new Dictionary<string, string>(Phase5Files())
    {
        ["Data/INI/Object/LastDamageFixture.ini"] = """
        Object ShyNinja
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = StealthUpdate Stealth01
            StealthDelay = 1000
            StealthForbiddenConditions = TAKING_DAMAGE
            InnateStealth = Yes
          End
        End
        """,
    };

    /// <summary>
    /// F-52 (R-30): <c>ActiveBody::attemptDamage</c> records the frame and the source of the last damage, and <c>StealthUpdate</c> refuses to hide a unit with
    /// <c>TAKING_DAMAGE</c> that was damaged in this frame or the one before (a heal does not count). Within that window only a damage from a vehicle, infantry or faction
    /// structure replaces a record whose source exists, and a damage whose source exists tells the victim's player it was attacked (<c>Player::setAttackedBy</c>).
    /// The port wrote no record at all.
    /// </summary>
    public static void ReforgedLastDamageIsRecorded()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: LastDamageFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        var stealthed = ObjectStatusBits.Index("STEALTHED");
        feeder.Spawn(1, "ShyNinja", owner: 1, x: 0, y: 0);
        world.Step();
        var ninja = world.Entities.Single(entity => entity.Template.Name == "ShyNinja");
        for (var i = 0; i < world.Frames(35); i++) world.Step();
        Check.That(ninja.HasStatus(stealthed), "The ninja hides after its delay.");
        feeder.Damage(ninja.Id.Value, 1);
        for (var i = 0; i < world.Frames(2); i++) world.Step();
        Check.That(ninja.LastDamageTick >= 0 && !ninja.LastDamageHealing, $"The damage is recorded (tick {ninja.LastDamageTick}).");
        Check.That(!ninja.HasStatus(stealthed), "A unit that may not hide while taking damage shows itself in the frame after the hit.");

        feeder.Spawn(2, "Factory", owner: 2, x: 200, y: 0);
        feeder.Spawn(2, "Visible", owner: 2, x: 200, y: 100);
        feeder.Spawn(1, "Visible", owner: 1, x: 0, y: 200);
        for (var i = 0; i < world.Frames(5); i++) world.Step();
        var factory = world.Entities.Single(entity => entity.Template.Name == "Factory");
        var soldier = world.Entities.Single(entity => entity.Template.Name == "Visible" && entity.Owner.Value == 2);
        var victim = world.Entities.Single(entity => entity.Template.Name == "Visible" && entity.Owner.Value == 1);
        var tick = world.Tick;
        Damage(factory);
        Check.That(victim.LastDamageSource == factory.Id && victim.LastDamageTick == tick, "The first damage is recorded with its source.");
        Check.That(world.AttackedTick(victim.Owner) == tick, "Its existing source tells the victim's player it was attacked.");
        Damage(soldier);
        Check.That(victim.LastDamageSource == soldier.Id, "Within the frame, a damage from infantry replaces a structure that is no faction structure.");
        Damage(factory);
        Check.That(victim.LastDamageSource == soldier.Id, "Within the frame, a damage from that structure does not replace the infantry.");
        Damage(null);
        Check.That(victim.LastDamageSource == soldier.Id, "Within the frame, a damage without a source replaces nothing.");
        for (var i = 0; i < world.Frames(3); i++) world.Step();
        Damage(factory);
        Check.That(victim.LastDamageSource == factory.Id && victim.LastDamageTick == world.Tick, "Two frames later any damage replaces the record.");
        world.Damage(victim.Id, new DamageInfo(soldier.Id, Fixed.One, DamageTypes.Healing, DeathTypes.None, false));
        Check.That(victim.LastDamageHealing && victim.LastDamageSource == soldier.Id, "A heal is recorded too, and marked as one.");

        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == world.StateHash && restored.AttackedTick(victim.Owner) == world.AttackedTick(victim.Owner), "The record and the attacked frame survive a snapshot.");

        void Damage(Entity? source) => world.Damage(victim.Id, new DamageInfo(source?.Id ?? EntityId.Invalid, Fixed.One, DamageTypes.Explosion, DeathTypes.Normal, false));
    }

    /// <summary>
    /// F-52: <c>SKIRMISH_SUPPLY_SOURCE_ATTACKED</c> (<c>AIPlayer::isSupplySourceAttacked</c>) holds while a harvester, dozer or cash generator of the player carries a damage record of the
    /// last ten frames and the player was attacked - by a source that exists - in those frames; a damage without a source records, but attacks nobody. It never held in the port.
    /// </summary>
    public static void ReforgedSupplySourceAttacked()
    {
        var harvester = new Dictionary<string, string>
        {
            ["Data/INI/Object/HarvesterFixture.ini"] = """
            Object ScriptHarvester
              KindOf = SELECTABLE VEHICLE HARVESTER
              Body = ActiveBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
              Behavior = DestroyDie Destroy01
              End
            End
            """,
        };
        foreach (var withSource in new[] { true, false })
        {
            var (map, fixture, terrain) = MissionMap([
                Scr.Script("attacked", [Scr.And(Scr.If("SUPPLY_SOURCE_ATTACKED", Scr.Side(PlayerA)))], [Scr.Do("SET_FLAG", Scr.Flag("attacked"), Scr.Bool(true))], oneShot: true),
            ], objects: [Placed("ScriptHarvester", 100m, 100m, "teamA", "Truck"), Placed("ScriptHarvester", 140m, 100m, "teamB", "Raider")], extraFiles: harvester);
            var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
            Run(session, session.Frames(30));
            Check.That(!session.Engine.FlagValue("attacked"), "Nothing is attacked before the hit.");
            var truck = NamedIn(session, "Truck")!;
            var raider = NamedIn(session, "Raider")!;
            session.World.Damage(truck.Id, new DamageInfo(withSource ? raider.Id : EntityId.Invalid, Fixed.FromInt(5), DamageTypes.Explosion, DeathTypes.Normal, false));
            Run(session, session.Frames(12));
            Check.That(session.Engine.FlagValue("attacked") == withSource,
                withSource ? "A hit on the harvester by an existing enemy is seen within the next scan." : "A hit without a source attacks nobody: the player's attacked frame stays old.");
        }
    }
}
