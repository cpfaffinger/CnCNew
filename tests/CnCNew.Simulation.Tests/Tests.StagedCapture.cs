using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>The staged capture ability (<c>SpecialAbilityUpdate</c>, docs/SIM-STAGED-CAPTURE.md): approach, face, unpack, raise the flag, change owner, pack.</summary>
static partial class Tests
{
    // Unpack 1000 ms = 30 ticks, preparation 2000 ms = 60 ticks, pack 500 ms = 15 ticks.
    private const int StagedUnpack = 30;
    private const int StagedPrepare = 60;
    private const int StagedPack = 15;

    private static IReadOnlyDictionary<string, string> StagedCaptureFiles() => new Dictionary<string, string>(PowerPlantFiles())
    {
        ["Data/INI/Object/StagedCaptureFixture.ini"] = """
        Upgrade Upgrade_StagedCapture
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End
        SpecialPower SPECIAL_StagedCapture
          Enum = SPECIAL_INFANTRY_CAPTURE_BUILDING
          ReloadTime = 1000
        End
        SpecialPower SPECIAL_StagedLotus
          Enum = SPECIAL_BLACKLOTUS_CAPTURE_BUILDING
          ReloadTime = 0
        End
        SpecialPower SPECIAL_StagedCash
          Enum = SPECIAL_BLACKLOTUS_STEAL_CASH_HACK
          ReloadTime = 1000
        End
        Object StagedCapturer
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL CaptureLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SpecialAbility Cap01
            SpecialPowerTemplate = SPECIAL_StagedCapture
            UpdateModuleStartsAttack = Yes
            StartsPaused = Yes
          End
          Behavior = SpecialAbilityUpdate CapUpdate01
            SpecialPowerTemplate = SPECIAL_StagedCapture
            StartAbilityRange = 5.0
            UnpackTime = 1000
            PreparationTime = 2000
            PackTime = 500
            DoCaptureFX = Yes
          End
          Behavior = UnpauseSpecialPowerUpgrade Unpause01
            SpecialPowerTemplate = SPECIAL_StagedCapture
            TriggeredBy = Upgrade_StagedCapture
          End
        End
        Object StagedLotus
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL CaptureLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SpecialAbility Lotus01
            SpecialPowerTemplate = SPECIAL_StagedLotus
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpecialAbilityUpdate LotusUpdate01
            SpecialPowerTemplate = SPECIAL_StagedLotus
            StartAbilityRange = 150.0
            UnpackTime = 1000
            PreparationTime = 2000
            PackTime = 500
            DoCaptureFX = Yes
          End
          Behavior = SpecialAbility Cash01
            SpecialPowerTemplate = SPECIAL_StagedCash
          End
          Behavior = SpecialAbilityUpdate CashUpdate01
            SpecialPowerTemplate = SPECIAL_StagedCash
            StartAbilityRange = 150.0
            UnpackTime = 1000
            PreparationTime = 2000
            PackTime = 500
          End
        End
        """,
    };

    /// <summary>One observed tick of a staged capture run.</summary>
    private readonly record struct CaptureSample(long Tick, AbilityStage Stage, int StationOwner, bool UsingAbility, bool CapturedThisTick, Fixed X, Fixed Angle);

    private static readonly int UsingAbilityStatusBit = ObjectStatusBits.Index("USING_ABILITY");

    private static List<CaptureSample> ObserveCapture(SimulationWorld world, uint actor, uint station, int frames, Action<long>? each = null)
    {
        var samples = new List<CaptureSample>();
        for (var index = 0; index < world.Frames(frames); index++)
        {
            var result = world.Step();
            each?.Invoke(world.Tick);
            var entity = world.FindEntity(new EntityId(actor));
            var building = world.FindEntity(new EntityId(station));
            var ability = entity?.Modules.OfType<SpecialAbilityUpdate>().FirstOrDefault(value => value.IsCapture(world));
            samples.Add(new CaptureSample(world.Tick, ability?.Stage ?? AbilityStage.None, building?.Owner.Value ?? -1, entity?.HasStatus(UsingAbilityStatusBit) ?? false,
                result.Events.Any(value => value.Kind == SimEventKind.StructureCaptured), entity?.X ?? Fixed.Zero, entity?.Angle ?? Fixed.Zero));
        }
        return samples;
    }

    private static (long Start, int Length) StageRun(List<CaptureSample> samples, AbilityStage stage)
    {
        var first = samples.FindIndex(value => value.Stage == stage);
        if (first < 0) return (-1, 0);
        var length = samples.Skip(first).TakeWhile(value => value.Stage == stage).Count();
        return (samples[first].Tick, length);
    }

    private static SimulationWorld StagedWorld(out SyntheticFixture fixture, out Feeder feeder, int stationX = 200, int stationY = 50, string capturer = "StagedCapturer", bool upgrade = true)
    {
        fixture = new SyntheticFixture(TestRate.Default, files: StagedCaptureFiles());
        var world = NewWorld(fixture);
        feeder = new Feeder(world);
        feeder.Spawn(1, capturer, owner: 1, x: 100, y: 50);                         // id 1
        feeder.Spawn(2, "PlainStation", owner: 2, x: stationX, y: stationY);        // id 2
        feeder.SetRelationship(1, 2, Relationship.Enemies);
        world.Step();
        if (upgrade) world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_StagedCapture");
        // The power recharges (1000 ms) from the moment the unit appears.
        for (var index = 0; index < world.Frames(40); index++) world.Step();
        return world;
    }

    public static void StagedCaptureRunsThroughItsStages()
    {
        var world = StagedWorld(out _, out var feeder, upgrade: false);
        var capturer = world.FindEntity(new EntityId(1))!;
        var station = world.FindEntity(new EntityId(2))!;
        Check.That(feeder.Capture(1, 1, 2).Reason == CommandRejection.SpecialPowerNotReady, "The capture power is paused until its upgrade (UnpauseSpecialPowerUpgrade): the command is refused.");
        world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_StagedCapture");
        Check.That(feeder.Capture(1, 1, 2).Reason == CommandRejection.SpecialPowerNotReady, "Releasing the pause adds the paused time to the recharge: the power is not ready the same tick.");
        for (var index = 0; index < world.Frames(35); index++) world.Step();
        Check.That(feeder.Capture(1, 1, 2).Accepted, "With the upgrade (and the recharge over) the capture is accepted although the Capturer stands 100 units away: it walks there itself.");
        var ability = capturer.Modules.OfType<SpecialAbilityUpdate>().Single();
        var power = capturer.Modules.OfType<SpecialAbility>().Single();

        var samples = ObserveCapture(world, 1, 2, 600);
        var approach = StageRun(samples, AbilityStage.Approaching);
        var unpack = StageRun(samples, AbilityStage.Unpacking);
        var prepare = StageRun(samples, AbilityStage.Preparing);
        var pack = StageRun(samples, AbilityStage.Packing);
        Check.That(approach.Start > 0 && approach.Length > 10, $"The Capturer first walks toward the building (approach for {approach.Length} ticks).");
        Check.That(unpack.Length == world.Frames(StagedUnpack), $"Unpacking lasts UnpackTime = {StagedUnpack} ticks, took {unpack.Length}.");
        Check.That(prepare.Length == world.Frames(StagedPrepare), $"Raising the flag lasts PreparationTime = {StagedPrepare} ticks, took {prepare.Length}.");
        Check.That(pack.Length == world.Frames(StagedPack), $"Packing lasts PackTime = {StagedPack} ticks, took {pack.Length}.");
        Check.That(approach.Start < unpack.Start && unpack.Start + unpack.Length == prepare.Start && prepare.Start + prepare.Length == pack.Start, "The stages follow each other without gaps: approach, unpack, prepare, pack.");
        Check.That(samples[^1].Stage == AbilityStage.None && !ability.IsActive, "The ability is over afterwards.");

        var captured = samples.Where(value => value.CapturedThisTick).ToArray();
        Check.That(captured.Length == 1, "Exactly one StructureCaptured event.");
        Check.That(captured[0].Tick == pack.Start, $"The building changes hands when the preparation ends (tick {captured[0].Tick}), the first tick of packing is {pack.Start}.");
        Check.That(samples.Where(value => value.Tick < captured[0].Tick).All(value => value.StationOwner == 2) && samples.Where(value => value.Tick >= captured[0].Tick).All(value => value.StationOwner == 1),
            "The building belongs to the enemy until the flag is up and to the Capturer's player from then on.");
        Check.That(samples.Where(value => value.UsingAbility).Select(value => value.Tick).SequenceEqual(samples.Where(value => value.Stage == AbilityStage.Preparing).Select(value => value.Tick)),
            "USING_ABILITY is set exactly while the flag goes up.");
        Check.That(station.Owner == new PlayerId(1), "The station is the Capturer's.");

        // The Capturer stopped within StartAbilityRange (5) of the building and stood still while it worked.
        var gap = (station.X - capturer.X).Abs();
        var reach = Fixed.FromDecimal(2.5m) + station.Template.BoundingCircleRadius + capturer.Template.BoundingCircleRadius;
        Check.That(gap <= reach && gap > reach - Fixed.FromInt(3) && capturer.X > Fixed.FromInt(150), $"The Capturer stops as soon as the bounding spheres are within StartAbilityRange minus the line-of-sight undersize of 2.5 (gap {gap}, reach {reach}).");
        Check.That(samples.Where(value => value.Stage is AbilityStage.Unpacking or AbilityStage.Preparing or AbilityStage.Packing).Select(value => value.X).Distinct().Count() == 1, "It does not move while unpacking, raising the flag or packing.");

        // The infantry capture restarts the recharge on every preparation tick: not ready right after, ready again 1000 ms (30 ticks) later.
        Check.That(!power.IsReady(world) || world.Tick - pack.Start >= world.Frames(StagedPack + 30), "The power recharges from the end of the preparation on.");
        Check.That(feeder.Capture(1, 1, 2).Reason == CommandRejection.CannotCapture, "The building is already the Capturer's: nothing left to capture.");
    }

    /// <summary><c>DoCaptureFX</c>: the building flashes while the flag goes up, faster and faster (<c>m_captureFlashPhase</c>).</summary>
    public static void StagedCaptureFlashesTheBuilding()
    {
        var world = StagedWorld(out var fixture, out var feeder);
        Check.That(feeder.Capture(1, 1, 2).Accepted, "The capture is accepted.");
        var flashes = new List<long>();
        var preparing = new List<long>();
        SimulationWorld? restored = null;
        for (var index = 0; index < world.Frames(600); index++)
        {
            var result = world.Step();
            if (restored is not null) Check.That(restored.Step().StateHash == result.StateHash, "The restored capture flashes identically at tick " + result.Tick + ".");
            foreach (var flash in result.Events.Where(value => value.Kind == SimEventKind.CaptureFlash))
            {
                Check.That(flash.Entity == new EntityId(2) && flash.Other == new EntityId(1), "The flash names the building and the capturing unit.");
                flashes.Add(result.Tick);
            }
            if (world.FindEntity(new EntityId(1))!.Modules.OfType<SpecialAbilityUpdate>().Single().Stage == AbilityStage.Preparing) preparing.Add(result.Tick);
            if (restored is null && preparing.Count == 20) restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        }
        // The phase grows by (elapsed fraction) / 3 per tick: over 60 ticks it reaches 59 * 60 / 2 / 60 / 3 = 9.83, crossing 2, 4, 6 and 8.
        Check.That(flashes.Count == 4, "Four flashes during a preparation of 60 ticks: " + string.Join(",", flashes));
        Check.That(flashes.All(tick => tick > preparing[0] && tick <= preparing[^1] + 1), "All of them while the flag goes up.");
        var gaps = flashes.Zip(flashes.Skip(1), (a, b) => b - a).ToArray();
        Check.That(gaps[0] >= gaps[1] && gaps[1] >= gaps[2] && gaps[0] > gaps[2], "The flashes come faster toward the end: gaps " + string.Join(",", gaps));
    }

    public static void StagedCaptureFacesItsTargetAndLotusStartsFromAfar()
    {
        var world = StagedWorld(out _, out var feeder, stationX: 100, stationY: 190, capturer: "StagedLotus", upgrade: false);
        var lotus = world.FindEntity(new EntityId(1))!;
        Check.That(lotus.Angle == Fixed.Zero, "The Lotus starts facing east.");
        Check.That(feeder.Capture(1, 1, 2).Accepted, "The Black Lotus power needs no upgrade (not paused, reload 0).");
        var samples = ObserveCapture(world, 1, 2, 400);
        var unpack = StageRun(samples, AbilityStage.Unpacking);
        var prepare = StageRun(samples, AbilityStage.Preparing);
        Check.That(unpack.Length == world.Frames(StagedUnpack) && prepare.Length == world.Frames(StagedPrepare), "Unpack and preparation take their configured times.");
        Check.That(lotus.X == Fixed.FromInt(100) && lotus.Y == Fixed.FromInt(50), "The station is 140 units away, within StartAbilityRange 150: the Lotus does not move at all.");
        Check.That(samples.First(value => value.Stage == AbilityStage.Unpacking).Angle.Raw is var raw && Math.Abs(raw - FixedTrig.Atan2(Fixed.FromInt(140), Fixed.Zero).Raw) < 100,
            "She has turned to face the building (north) before she starts to unpack.");
        Check.That(unpack.Start >= world.Frames(8), $"Turning 90 degrees at 360 degrees per second takes about 8 ticks, unpacking started at tick {unpack.Start}.");
        Check.That(world.FindEntity(new EntityId(2))!.Owner == new PlayerId(1), "The building is captured.");
        Check.That(lotus.Modules.OfType<SpecialAbilityUpdate>().Count(value => value.IsCapture(world)) == 1, "Only the capture ability of the Lotus is staged; her cash hack keeps the plain recharge behaviour.");
        Check.That(lotus.Modules.OfType<SpecialAbilityUpdate>().Where(value => !value.IsCapture(world)).All(value => !value.IsActive), "The unstaged ability never becomes active.");
        Check.That(lotus.Modules.OfType<SpecialAbilityUpdate>().First(value => value.IsCapture(world)).IsHackStyle(world), "The Lotus's preparation is the hacker's typing animation, not the raised flag.");
    }

    /// <summary>A fresh staged world in which the capture was ordered and has run until it reached <paramref name="stage"/>, plus <paramref name="extra"/> more ticks.</summary>
    private static SimulationWorld StagedWorldAt(AbilityStage stage, int extra, out SyntheticFixture fixture, out Feeder feeder)
    {
        var world = StagedWorld(out fixture, out feeder);
        Check.That(feeder.Capture(1, 1, 2).Accepted, "Capture accepted.");
        var ability = world.FindEntity(new EntityId(1))!.Modules.OfType<SpecialAbilityUpdate>().Single();
        for (var index = 0; index < world.Frames(600) && ability.Stage != stage; index++) world.Step();
        Check.That(ability.Stage == stage, $"The ability reached {stage}.");
        for (var index = 0; index < world.Frames(extra); index++) world.Step();
        return world;
    }

    public static void StagedCaptureAbortRules()
    {
        // Target destroyed during the preparation: the ability ends at once, nothing is captured, nothing is left behind.
        {
            var world = StagedWorldAt(AbilityStage.Preparing, 10, out _, out var feeder);
            feeder.Kill(2);
            var after = ObserveCapture(world, 1, 2, StagedPack + 10);
            // A destroyed target is gone from the world (continuePreparation: "Target is dead, stop"): the preparation is cancelled, the unit packs up and the ability ends.
            Check.That(after.All(value => !value.UsingAbility && !value.CapturedThisTick), "A destroyed target cancels the preparation: no capture, USING_ABILITY cleared.");
            Check.That(StageRun(after, AbilityStage.Packing).Length == world.Frames(StagedPack) && after[^1].Stage == AbilityStage.None, "The unit packs up, then the ability is over.");
            Check.That(world.FindEntity(new EntityId(1))!.Modules.OfType<SpecialAbilityUpdate>().Single().Target == EntityId.Invalid, "The ability forgot its target.");
        }

        // The player orders the Capturer elsewhere while it unpacks.
        {
            var world = StagedWorldAt(AbilityStage.Unpacking, 5, out _, out var feeder);
            Check.That(feeder.SetMoveTarget(1, 1, 120, 50).Accepted, "A move order.");
            var after = ObserveCapture(world, 1, 2, 200);
            Check.That(after.All(value => value.Stage == AbilityStage.None && !value.CapturedThisTick) && world.FindEntity(new EntityId(2))!.Owner == new PlayerId(2),
                "An order from the player cancels the ability; the building stays the enemy's.");
        }

        // ... or while it walks to the building.
        {
            var world = StagedWorldAt(AbilityStage.Approaching, 5, out _, out var feeder);
            Check.That(feeder.SetMoveTarget(1, 1, 60, 50).Accepted, "A move order.");
            var after = ObserveCapture(world, 1, 2, 200);
            Check.That(after.All(value => value.Stage == AbilityStage.None) && world.FindEntity(new EntityId(1))!.X < Fixed.FromInt(100), "The new order replaces the approach; the Capturer walks away.");
        }

        // The Capturer dies during the preparation.
        {
            var world = StagedWorldAt(AbilityStage.Preparing, 20, out _, out var feeder);
            feeder.Kill(1);
            ObserveCapture(world, 1, 2, 5);
            Check.That(world.FindEntity(new EntityId(1)) is null || !world.FindEntity(new EntityId(1))!.Modules.OfType<SpecialAbilityUpdate>().Single().IsActive, "A dead Capturer is not capturing.");
            ObserveCapture(world, 1, 2, 200);
            Check.That(world.FindEntity(new EntityId(2))!.Owner == new PlayerId(2), "Nothing was captured.");
        }

        // A colleague (same player) captured the building first.
        {
            var world = StagedWorldAt(AbilityStage.Preparing, 20, out _, out _);
            Check.That(world.TransferOwnership(new EntityId(2), new PlayerId(1)), "Somebody else hands the building to player 1.");
            var after = ObserveCapture(world, 1, 2, 5);
            Check.That(after.All(value => value.Stage == AbilityStage.None && !value.CapturedThisTick), "The ability stops when the building is already ours; no second capture.");
        }

        // A target that is no structure and uses of the power without a proper target are refused.
        {
            var world = StagedWorld(out _, out var feeder);
            feeder.Spawn(2, "StagedCapturer", owner: 2, x: 300, y: 50);   // id 3, a unit
            world.Step();
            Check.That(feeder.Capture(1, 1, 3).Reason == CommandRejection.CannotCapture, "A unit cannot be captured.");
            Check.That(feeder.UseSpecialPower(1, 1, "SPECIAL_StagedCapture", SpecialPowerTargetKind.Object, target: 3).Reason == CommandRejection.InvalidSpecialPowerTarget, "The power refuses a unit as its target.");
            Check.That(feeder.UseSpecialPower(1, 1, "SPECIAL_StagedCapture", SpecialPowerTargetKind.Location, x: 150, y: 50).Reason == CommandRejection.InvalidSpecialPowerTarget, "The power refuses a location.");
            Check.That(feeder.UseSpecialPower(1, 1, "SPECIAL_StagedCapture", SpecialPowerTargetKind.None).Reason == CommandRejection.InvalidSpecialPowerTarget, "The power refuses a use without a target.");
        }

        // A second capture order while one runs restarts it cleanly.
        {
            var world = StagedWorldAt(AbilityStage.Unpacking, 5, out _, out var feeder);
            feeder.Spawn(2, "PlainStation", owner: 2, x: 200, y: 90);     // id 3
            world.Step();
            var ability = world.FindEntity(new EntityId(1))!.Modules.OfType<SpecialAbilityUpdate>().Single();
            Check.That(feeder.Capture(1, 1, 3).Accepted, "The power is still ready while the first capture only unpacks, so a second order is accepted.");
            world.Step();
            Check.That(ability.Target == new EntityId(3) && ability.Stage == AbilityStage.Approaching, "The new order replaces the old run: new target, back to the approach.");
        }
    }

    public static void StagedCaptureViaTheSpecialPowerCommand()
    {
        var world = StagedWorld(out _, out var feeder);
        Check.That(feeder.UseSpecialPower(1, 1, "SPECIAL_StagedCapture", SpecialPowerTargetKind.Object, target: 2).Accepted,
            "The capture power was refused with SpecialPowerHandlerMissing before the staging existed; as a command button it is now a handler use.");
        var samples = ObserveCapture(world, 1, 2, 600);
        Check.That(StageRun(samples, AbilityStage.Unpacking).Length == world.Frames(StagedUnpack) && StageRun(samples, AbilityStage.Preparing).Length == world.Frames(StagedPrepare) && StageRun(samples, AbilityStage.Packing).Length == world.Frames(StagedPack),
            "The power command runs the same stages as the capture command.");
        Check.That(world.FindEntity(new EntityId(2))!.Owner == new PlayerId(1), "The building is captured.");
    }

    public static void StagedCaptureSnapshotAndDeterminism()
    {
        // Snapshot in the middle of every stage and compare the continuation with the live run tick by tick.
        foreach (var stage in new[] { AbilityStage.Approaching, AbilityStage.Unpacking, AbilityStage.Preparing, AbilityStage.Packing })
        {
            var world = StagedWorldAt(stage, 3, out var fixture, out _);
            var cut = world.Tick;
            var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
            Check.That(restored.StateHash == world.StateHash, $"A snapshot at tick {cut} restores to the same state hash.");
            for (var index = 0; index < world.Frames(200); index++)
            {
                world.Step();
                restored.Step();
                Check.That(restored.StateHash == world.StateHash, $"The restored run stays identical (cut {cut}, tick {world.Tick}).");
            }
            Check.That(restored.FindEntity(new EntityId(2))!.Owner == new PlayerId(1), $"The restored run completes the capture (cut {cut}).");
        }

        // An object that never uses the ability saves exactly what it saved before the staging existed.
        var idleFixture = new SyntheticFixture(TestRate.Default, files: StagedCaptureFiles());
        var idle = NewWorld(idleFixture);
        new Feeder(idle).Spawn(1, "StagedCapturer", owner: 1, x: 100, y: 50);
        for (var index = 0; index < idle.Frames(50); index++) idle.Step();
        var text = System.Text.Encoding.UTF8.GetString(WorldSnapshot.Write(idle));
        Check.That(!text.Contains("abilityActive", StringComparison.Ordinal), "An idle SpecialAbilityUpdate writes nothing of the staging into the snapshot.");
        var busy = StagedWorldAt(AbilityStage.Preparing, 1, out _, out _);
        Check.That(System.Text.Encoding.UTF8.GetString(WorldSnapshot.Write(busy)).Contains("abilityActive", StringComparison.Ordinal), "A running ability does.");

        // Two independent runs agree.
        string Run()
        {
            var world = StagedWorld(out _, out var runFeeder);
            runFeeder.Capture(1, 1, 2);
            for (var index = 0; index < world.Frames(300); index++) world.Step();
            return world.StateHash.ToString("X16");
        }
        Check.That(Run() == Run(), "Two independent runs end with the same state hash.");
    }


    // ---------------------------------------------------------------------------------------------------------------- real data

    public static void RealStagedCapture(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));

        (long Unpack, long Prepare, long Pack, bool Captured, long CaptureTick, long PackStart, bool HackStyle, string Hash) Run(string unitTemplate, string buildingTemplate, string? upgrade, int unpack, int prepare, int pack)
        {
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "capture");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 7, players, "real-capture", fingerprint, terrain.Fingerprint), catalog, terrain);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, unitTemplate, owner: 1, x: 300, y: 600).Accepted, unitTemplate + " spawns.");
            Check.That(feeder.Spawn(2, buildingTemplate, owner: 2, x: 700, y: 600).Accepted, buildingTemplate + " spawns.");
            feeder.SetRelationship(1, 2, Relationship.Enemies);
            world.RevealMapPermanently(new PlayerId(1), true);
            world.Step();
            if (upgrade is not null)
            {
                Check.That(feeder.Capture(1, 1, 2).Reason == CommandRejection.SpecialPowerNotReady, $"{unitTemplate}: the capture ability is paused until {upgrade}.");
                world.CompleteUpgrade(new PlayerId(1), null, upgrade);
            }
            // A fresh unit recharges its power first (Ranger: ReloadTime 15 s).
            foreach (var module in world.FindEntity(new EntityId(1))!.Modules.OfType<ISpecialPowerModule>()) module.SetReadyFrame(world.Tick);
            var accepted = feeder.Capture(1, 1, 2);
            Check.That(accepted.Accepted, $"{unitTemplate}: a capture command on {buildingTemplate} 400 units away is accepted ({accepted.Reason}).");
            var samples = ObserveCapture(world, 1, 2, 2400);
            var runUnpack = StageRun(samples, AbilityStage.Unpacking);
            var runPrepare = StageRun(samples, AbilityStage.Preparing);
            var runPack = StageRun(samples, AbilityStage.Packing);
            var capture = samples.FirstOrDefault(value => value.CapturedThisTick);
            var ability = world.FindEntity(new EntityId(1))!.Modules.OfType<SpecialAbilityUpdate>().First(value => value.IsCapture(world));
            Console.WriteLine($"       {unitTemplate} on {buildingTemplate}: unpack {runUnpack.Length} (data {unpack}), flag {runPrepare.Length} (data {prepare}), pack {runPack.Length} (data {pack}), captured at tick {capture.Tick}, gap {(world.FindEntity(new EntityId(2))!.X - world.FindEntity(new EntityId(1))!.X).ToDecimal():F1}");
            return (runUnpack.Length, runPrepare.Length, runPack.Length, capture.Tick != 0, capture.Tick, runPack.Start, ability.IsHackStyle(world), world.StateHash.ToString("X16"));
        }

        foreach (var (unit, upgrade, unpack, prepare, pack) in new[]
        {
            ("AmericaInfantryRanger", "Upgrade_InfantryCaptureBuilding", 90, 600, 60),
            ("ChinaInfantryRedguard", "Upgrade_InfantryCaptureBuilding", 90, 600, 60),
            ("GLAInfantryRebel", "Upgrade_InfantryCaptureBuilding", 90, 600, 60),
        })
        {
            Check.That(catalog.TryGet(unit, out _), unit + " is a retail template.");
            var first = Run(unit, "ChinaBarracks", upgrade, unpack, prepare, pack);
            Check.That(first.Unpack == TestRate.Frames(unpack) && first.Prepare == TestRate.Frames(prepare) && first.Pack == TestRate.Frames(pack), $"{unit}: the stages take UnpackTime/PreparationTime/PackTime of the data ({first.Unpack}/{first.Prepare}/{first.Pack}).");
            Check.That(first.Captured && first.CaptureTick == first.PackStart, $"{unit}: the barracks changes hands when the preparation ends.");
            Check.That(!first.HackStyle, $"{unit}: the infantry capture raises a flag.");
            Check.That(Run(unit, "ChinaBarracks", upgrade, unpack, prepare, pack).Hash == first.Hash, $"{unit}: two runs end identically.");
        }

        var lotus = Run("ChinaInfantryBlackLotus", "AmericaBarracks", null, 202, 180, 84);
        Check.That(lotus.Unpack == TestRate.Frames(202) && lotus.Prepare == TestRate.Frames(180) && lotus.Pack == TestRate.Frames(84), $"Black Lotus: UnpackTime 6730 ms, PreparationTime 6000 ms, PackTime 2800 ms ({lotus.Unpack}/{lotus.Prepare}/{lotus.Pack} ticks).");
        Check.That(lotus.Captured && lotus.HackStyle, "Black Lotus: captures the barracks and types.");
    }
}
