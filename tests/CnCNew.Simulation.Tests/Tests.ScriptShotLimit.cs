using System.Collections.Generic;
using System.Linq;
using CnCNew.Data.World;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

/// <summary>
/// #277: a script's <c>FIRE_WEAPON</c> button at an object is <c>Object::doCommandButtonAtObject</c>, which ends in <c>aiAttackObject(obj, commandButton->getMaxShotsToFire(), cmdSource)</c>.
/// <c>CommandButton::m_maxShotsToFire</c> defaults to <c>0x7fffffff</c> ("huge number", <c>NO_MAX_SHOTS_LIMIT</c>), so a button without the key attacks until the victim dies; a button with it
/// fires that many shots (<c>Weapon::m_maxShotCount</c>, counted down by every shot; <c>AIAttackState::update</c> fails at zero). <c>AIAttackState</c> also fails once the weapon that was locked
/// when it began is no longer the current one (<c>m_lockedWeaponOnEnter</c>): the shot that empties a clip releases the temporary lock, and a slot the chooser never picks by itself
/// (<c>AutoChooseSources = SECONDARY NONE</c>) is then replaced. The port fired one shot per script order.
/// </summary>
static partial class Tests
{
    private static readonly Dictionary<string, string> ShotLimitFiles = new()
    {
        ["Data/INI/Object/ShotLimit.ini"] = """
        Weapon LimitClipGun
          PrimaryDamage = 10
          AttackRange = 80.0
          DamageType = EXPLOSION
          ClipSize = 2
          ClipReloadTime = 1000
          DelayBetweenShots = 200
        End
        Weapon LimitBig
          PrimaryDamage = 50
          AttackRange = 80.0
          DamageType = EXPLOSION
          ClipSize = 2
          ClipReloadTime = 3000
          DelayBetweenShots = 400
        End
        Object LimitShooter
          Side = Alpha
          KindOf = SELECTABLE INFANTRY
          VisionRange = 300.0
          ShroudClearingRange = 300.0
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 80.0
            InitialHealth = 80.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY LimitClipGun
            Weapon = SECONDARY LimitBig
            AutoChooseSources = SECONDARY NONE
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = No
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object LimitTarget
          Side = Beta
          KindOf = SELECTABLE INFANTRY
          VisionRange = 10.0
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = No
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object LimitWeakTarget
          Side = Beta
          KindOf = SELECTABLE INFANTRY
          VisionRange = 10.0
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 30.0
            InitialHealth = 30.0
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = No
          End
          Behavior = DestroyDie Destroy01
          End
        End
        CommandButton Cmd_LimitPrimary
          Command = FIRE_WEAPON
          WeaponSlot = PRIMARY
        End
        CommandButton Cmd_LimitPrimaryThree
          Command = FIRE_WEAPON
          WeaponSlot = PRIMARY
          MaxShotsToFire = 3
        End
        CommandButton Cmd_LimitBig
          Command = FIRE_WEAPON
          WeaponSlot = SECONDARY
        End
        """,
    };

    private static (MapData Map, SyntheticFixture Fixture, TerrainMap Terrain) ShotLimitMission()
    {
        MapScript Fire(string name, string shooter, string button, string victim)
            => Scr.Script(name, [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("look")))], [Scr.Do("NAMED_USE_COMMANDBUTTON_ABILITY_ON_NAMED", Scr.Unit(shooter), Scr.Ability(button), Scr.Unit(victim))], oneShot: true);
        // The shooters see their victims first: an order at a target the player cannot see is refused.
        return MissionMap([
            Scr.Script("Look", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_MILLISECOND_TIMER", Scr.Counter("look"), Scr.Real(0.2m))], oneShot: true),
            Fire("Unlimited", "Unlimited", "Cmd_LimitPrimary", "UnlimitedVictim"),
            Fire("Three", "Three", "Cmd_LimitPrimaryThree", "ThreeVictim"),
            Fire("Locked", "Locked", "Cmd_LimitBig", "LockedVictim"),
            Fire("Weak", "Weak", "Cmd_LimitPrimary", "WeakVictim"),
            Fire("Far", "Far", "Cmd_LimitPrimaryThree", "FarVictim"),
            Fire("Flee", "Flee", "Cmd_LimitPrimaryThree", "FleeVictim"),
        ], objects: [
            Placed("LimitShooter", 100m, 80m, "teamA", "Unlimited"), Placed("LimitTarget", 140m, 80m, "teamB", "UnlimitedVictim"),
            Placed("LimitShooter", 100m, 300m, "teamA", "Three"), Placed("LimitTarget", 140m, 300m, "teamB", "ThreeVictim"),
            Placed("LimitShooter", 400m, 80m, "teamA", "Locked"), Placed("LimitTarget", 440m, 80m, "teamB", "LockedVictim"),
            Placed("LimitShooter", 400m, 300m, "teamA", "Weak"), Placed("LimitWeakTarget", 440m, 300m, "teamB", "WeakVictim"),
            Placed("LimitShooter", 250m, 160m, "teamA", "Far"), Placed("LimitTarget", 360m, 160m, "teamB", "FarVictim"),
            Placed("LimitShooter", 250m, 250m, "teamA", "Flee"), Placed("LimitTarget", 290m, 250m, "teamB", "FleeVictim"),
        ], extraFiles: ShotLimitFiles);
    }

    /// <summary>
    /// The four script orders of <see cref="ShotLimitMission"/> over eight seconds: a button without <c>MaxShotsToFire</c> keeps firing across its clip's reloads, one with
    /// <c>MaxShotsToFire = 3</c> fires three shots (across a reload), a button of a slot the chooser never picks fires its clip and stops when the lock goes, and an attack without a limit
    /// ends with its victim. Before #277 each fired one shot. The mission also continues identically from a save taken while the attacks run.
    /// </summary>
    public static void ScriptFireButtonCarriesItsShotLimit()
    {
        var (map, fixture, terrain) = ShotLimitMission();
        var options = MissionOptions.Standard with { CombatIntervalTicks = 0 };
        var session = MissionSession.Start(options, fixture.Templates, fixture.Fingerprint, terrain, map);
        var events = new List<SimEvent>();
        var fled = false;
        for (var tick = 0; tick < session.Frames(240) && !session.IsOver; tick++)
        {
            events.AddRange(session.Step().Events);
            // #328: the victim of the "Flee" attack leaves the weapon's range after the first shot; the attack follows it and keeps its shot counter.
            if (!fled && NamedIn(session, "Flee") is { } fleeing && events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == fleeing.Id) && NamedIn(session, "FleeVictim") is { } runner)
            {
                fled = true;
                Check.That(session.World.Submit(new SetMoveTargetCommand(runner.Owner, 9_000_001, session.World.Tick, runner.Id, runner.X + Fixed.FromInt(95), runner.Y)).Accepted, "The victim is sent away.");
            }
        }
        Check.That(fled, "The Flee attack fired a first shot.");
        int Shots(string shooter, string weapon) => events.Count(value => value.Kind == SimEventKind.WeaponFired && value.Entity == NamedIn(session, shooter)?.Id && value.Text == weapon);
        string Rejections() => string.Join(",", session.World.RejectionCounts.Select((count, index) => count > 0 ? $"{(CommandRejection)index}={count}" : "").Where(text => text.Length > 0));
        var diagnostics = string.Join("; ", session.Engine.Diagnostics.Keys);

        // 2 shots, a reload of 30 frames, 2 shots ...: about eight shots in the 230 frames after the order.
        var unlimited = Shots("Unlimited", "LimitClipGun");
        Check.That(unlimited >= 6, $"A FIRE_WEAPON button without MaxShotsToFire attacks until the victim dies: {unlimited} shots in eight seconds (diagnostics: {diagnostics}; rejections: {Rejections()}).");
        Check.That(session.World.AcquiredVictimOf(NamedIn(session, "Unlimited")!) == NamedIn(session, "UnlimitedVictim")!.Id, "...and the attack is still on.");
        var three = Shots("Three", "LimitClipGun");
        Check.That(three == 3, $"MaxShotsToFire = 3 fires three shots, across the clip's reload ({three}).");
        Check.That(!session.World.AcquiredVictimOf(NamedIn(session, "Three")!).IsValid, "...and ends the attack.");
        var locked = Shots("Locked", "LimitBig");
        Check.That(locked == 2 && Shots("Locked", "LimitClipGun") == 0,
            $"A button of a slot the chooser never picks fires its clip; the shot that empties it releases the lock, the primary becomes the current weapon and the attack ends ({locked} big, {Shots("Locked", "LimitClipGun")} primary).");
        var weak = Shots("Weak", "LimitClipGun");
        Check.That(NamedIn(session, "WeakVictim") is null && weak == 3, $"An attack without a limit ends with its victim: 30 hit points, three shots ({weak}).");
        Check.That(!session.World.AcquiredVictimOf(NamedIn(session, "Weak")!).IsValid, "...and the shooter has no victim afterwards.");

        var far = Shots("Far", "LimitClipGun");
        Check.That(far == 3, $"A limited attack on a victim out of range approaches it and fires its three shots ({far}); the script order is not refused (diagnostics: {diagnostics}; rejections: {Rejections()}).");
        var flee = Shots("Flee", "LimitClipGun");
        Check.That(flee == 3, $"A victim that leaves the range during a limited attack is followed, and the shot counter survives the walk ({flee} shots).");

        // The attacks' shot limits and locked slots are state: a save in the middle of them continues identically.
        var (original, restored, _) = MissionScenario.VerifyContinuation(options, fixture.Templates, fixture.Fingerprint, terrain, map, session.Frames(120));
        Check.That(original.Chain == restored.Chain && original.FinalHash == restored.FinalHash, "A mission whose script attacks are under way continues identically after save/restore.");
    }

    /// <summary>The fire command's shot limit is part of its encoding (replay format 27, command table 28) and of its description, and a negative one is refused.</summary>
    public static void FireCommandShotLimitRoundTrips()
    {
        var one = new PlayerId(1);
        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, TestRate.Default.TicksPerSecond, new string('A', 64), "fixture", 1, new WorldId(1), TwoPlayers, 20, 5);
        System.Collections.Immutable.ImmutableArray<SimCommand> commands =
        [
            new FireWeaponCommand(one, 1, 3, new EntityId(4), new EntityId(5)),
            new FireWeaponCommand(one, 2, 3, new EntityId(4), new EntityId(5), WeaponSlot.Secondary, FireOrigin.Script, FireWeaponCommand.NoMaxShotsLimit),
            new FireWeaponCommand(one, 3, 4, new EntityId(4), new EntityId(5), WeaponSlot.Primary, FireOrigin.Ai, 3),
        ];
        Check.That(ReplayFormat.Read(ReplayFormat.Write(new ReplayFile(header, commands, []))).Commands.SequenceEqual(commands), "The shot limit round-trips in the replay format.");
        Check.That(ReplayFormat.EncodeCommand(commands[0]).Length + 4 == ReplayFormat.EncodeCommand(commands[1]).Length, "A command without a limit keeps its size; a limit adds its four bytes.");
        foreach (var command in commands)
        {
            var stream = new System.IO.MemoryStream();
            using (var json = new System.Text.Json.Utf8JsonWriter(stream)) command.Describe(new CnCNew.Simulation.State.JsonStateWriter(json));
            var text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            using var document = System.Text.Json.JsonDocument.Parse(text);
            Check.That(SimCommand.Load(new CnCNew.Simulation.State.JsonStateReader(document.RootElement)).Equals(command), $"The description round-trips: {text}.");
            Check.That(text.Contains("maxShots") == (((FireWeaponCommand)command).MaxShots != 0), "The description names the limit only when there is one.");
        }
        Check.That(ReplayFormat.CurrentVersion == 27 && CommandTypes.Version == 28, "Replay format 27 and command table 28 carry the shot limit.");
        var negative = ReplayFormat.Write(new ReplayFile(header, [new FireWeaponCommand(one, 1, 3, new EntityId(4), new EntityId(5), MaxShots: -1)], []));
        Check.Throws<ReplayFormatException>(() => ReplayFormat.Read(negative), "shot limit");
    }
}
