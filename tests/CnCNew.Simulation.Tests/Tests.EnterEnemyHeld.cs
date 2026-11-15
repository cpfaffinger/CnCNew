using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Issue #372: a ranger is sent into a neutral building, and an enemy garrisons it first. <c>AIEnterState::update</c> finds that it may no longer enter (<c>canEnterObject</c>: another player's
/// building with riders inside) and, the building being an enemy's now, attacks it instead (<c>aiAttackObject</c>, the fix of "units stop instead of attacking if building they were trying to
/// garrison is taken by enemy units first") - it does not stop where it is.
/// </summary>
static partial class Tests
{
    public static void RealEnterOrderTurnsIntoAttackOnAnEnemyGarrison(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        long? switchedAt30 = null;
        long? firedAt30 = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "enemy-garrison");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-enemy-garrison", fingerprint, terrain.Fingerprint), catalog, terrain);
            var sequence = 0UL;
            void Spawn(string template, int owner, int x, int y)
                => Check.That(world.Submit(new SpawnCommand(new PlayerId(owner == 0 ? 1 : owner), ++sequence, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
            Spawn("StanHotel02", 0, 600, 600);
            Spawn("AmericaInfantryRanger", 1, 150, 600);
            Spawn("AmericaInfantryRanger", 2, 600, 740);
            for (var i = 0; i < world.Frames(1); i++) world.Step();
            var hotel = world.Entities.First(value => value.Template.Name == "StanHotel02");
            var ours = world.Entities.First(value => value.Template.Name == "AmericaInfantryRanger" && value.Owner == new PlayerId(1));
            var theirs = world.Entities.First(value => value.Template.Name == "AmericaInfantryRanger" && value.Owner == new PlayerId(2));
            Check.That(world.RelationshipOf(new PlayerId(1), new PlayerId(2)) == Relationship.Enemies, "Setup: the two players are enemies.");
            Check.That(world.RelationshipOf(new PlayerId(1), hotel.Owner) != Relationship.Enemies, "Setup: the hotel belongs to nobody.");
            // Both are sent in at the start of a logic frame of the original; the enemy is much closer.
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            Check.That(world.Submit(new MoveAndEnterCommand(new PlayerId(1), ++sequence, world.Tick, ours.Id, hotel.Id)).Accepted, "Our ranger is sent in.");
            Check.That(world.Submit(new MoveAndEnterCommand(new PlayerId(2), ++sequence, world.Tick, theirs.Id, hotel.Id)).Accepted, "The enemy ranger is sent in.");
            var ai = ours.Modules.OfType<AIUpdateInterface>().Single();
            long? taken = null;
            for (var step = 0; step < world.Frames(900) && taken is null; step++)
            {
                world.Step();
                if (theirs.ContainedBy == hotel.Id) taken = world.Time.ToOriginalFrames(world.Tick);
            }
            Check.That(taken is not null && hotel.Owner == new PlayerId(2), $"{rate}: the enemy garrisons the hotel first (it belongs to player {hotel.Owner.Value}).");
            Check.That(!ours.ContainedBy.IsValid && ai.EnterTarget == hotel.Id, $"{rate}: our ranger is still on its way in.");
            // At its next frame of the original our ranger finds the hotel an enemy's and attacks it.
            long? switched = null;
            for (var step = 0; step < world.Frames(3) && switched is null; step++)
            {
                world.Step();
                if (world.AcquiredVictimOf(ours) == hotel.Id) switched = world.Time.ToOriginalFrames(world.Tick);
            }
            Check.That(switched is not null, $"{rate}: our ranger attacks the hotel the enemy took (its victim is {world.AcquiredVictimOf(ours).Value}, enter target {ai.EnterTarget.Value}, moving {ours.HasMoveTarget}).");
            Check.That(!ai.EnterTarget.IsValid, $"{rate}: the order to enter is over.");
            if (switchedAt30 is { } frame30) Check.That(Math.Abs(frame30 - switched!.Value) <= 1, $"{rate}: the attack begins in frame {switched}, at 30 Hz in frame {frame30}.");
            else switchedAt30 = switched;
            // It was still out of range: it walks on, now to attack, and fires at the hotel.
            long? fired = null;
            for (var step = 0; step < world.Frames(900) && fired is null && !ours.EffectivelyDead; step++)
            {
                var result = world.Step();
                if (result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == ours.Id && value.Other == hotel.Id)) fired = world.Time.ToOriginalFrames(world.Tick);
            }
            Check.That(fired is not null, $"{rate}: our ranger fires at the hotel (dead: {ours.EffectivelyDead}, at ({ours.X}, {ours.Y})).");
            Check.That(!ours.ContainedBy.IsValid, $"{rate}: our ranger did not get in.");
            if (firedAt30 is { } fired30) Check.That(Math.Abs(fired30 - fired!.Value) <= 2, $"{rate}: the first shot in frame {fired}, at 30 Hz in frame {fired30}.");
            else firedAt30 = fired;
        }
    }
}
