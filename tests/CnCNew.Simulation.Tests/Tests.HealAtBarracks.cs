using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Issue #375: a hurt computer ranger with <c>AutoFindHealingUpdate</c> looks for the closest <c>HEAL_PAD</c> and is ordered to get healed there
/// (<c>AIUpdateInterface::privateGetHealed</c>: <c>canGetHealedAt</c>, then <c>aiEnter</c>). The barracks is a <c>HealContain</c>: its own player's ranger walks in, is healed over
/// <c>TimeForFullHeal</c> and comes out. An ally's barracks passes <c>canGetHealedAt</c>, but <c>privateEnter</c> asks <c>canEnterObject</c>, which refuses another player's faction
/// structure - the order does nothing and the ranger stands where it is; it does not start to walk there.
/// </summary>
static partial class Tests
{
    private static (SimulationWorld World, Entity Ranger, Entity Barracks) HealAtBarracksWorld(TemplateCatalog catalog, string fingerprint, SimulationRate rate, int barracksOwner)
    {
        var players = ImmutableArray.Create(
            new PlayerConfiguration(new PlayerId(1), "East", Team: 1, IsComputer: true),
            new PlayerConfiguration(new PlayerId(2), "Ally", Team: 1, IsComputer: true));
        var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "heal-at-barracks");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-heal-at-barracks", fingerprint, terrain.Fingerprint), catalog, terrain);
        var sequence = 0UL;
        void Spawn(string template, int owner, int x, int y)
            => Check.That(world.Submit(new SpawnCommand(new PlayerId(owner), ++sequence, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
        Spawn("AmericaBarracks", barracksOwner, 800, 600);
        Spawn("AmericaInfantryRanger", 1, 600, 600);
        for (var i = 0; i < world.Frames(1); i++) world.Step();
        var ranger = world.Entities.Single(value => value.Template.Name == "AmericaInfantryRanger");
        var barracks = world.Entities.Single(value => value.Template.Name == "AmericaBarracks");
        // Below NeverHeal (85 %): half its health.
        Check.That(world.Submit(new DamageCommand(PlayerId.System, ++sequence, world.Tick, ranger.Id, ranger.Body!.MaxHealth / Fixed.FromInt(2), 0, 0)).Accepted, "The ranger is hurt.");
        world.Step();
        Check.That(ranger.Body.Health < ranger.Body.MaxHealth, "Setup: the ranger is hurt.");
        return (world, ranger, barracks);
    }

    public static void RealHurtRangerIsHealedInItsBarracks(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        long? enteredAt30 = null, releasedAt30 = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);

            // Its own barracks: in, healed, out.
            var (world, ranger, barracks) = HealAtBarracksWorld(catalog, fingerprint, rate, 1);
            Check.That(barracks.Container is HealContain, $"{rate}: the American barracks is a HealContain.");
            long? entered = null;
            for (var step = 0; step < world.Frames(600) && entered is null; step++)
            {
                world.Step();
                if (ranger.ContainedBy == barracks.Id) entered = world.Time.ToOriginalFrames(world.Tick);
            }
            Check.That(entered is not null, $"{rate}: the hurt ranger walks into its barracks (it stands at ({ranger.X}, {ranger.Y}), health {ranger.Body!.Health} of {ranger.Body.MaxHealth}).");
            long? released = null;
            for (var step = 0; step < world.Frames(300) && released is null; step++)
            {
                world.Step();
                if (!ranger.ContainedBy.IsValid) released = world.Time.ToOriginalFrames(world.Tick);
            }
            Check.That(released is not null, $"{rate}: the barracks lets the ranger out.");
            Check.That(ranger.Body.Health == ranger.Body.MaxHealth, $"{rate}: the barracks healed the ranger to full health ({ranger.Body.Health} of {ranger.Body.MaxHealth}).");
            if (enteredAt30 is { } in30 && releasedAt30 is { } out30)
            {
                Check.That(Math.Abs(in30 - entered!.Value) <= 1, $"{rate}: the ranger enters in frame {entered}, at 30 Hz in frame {in30}.");
                Check.That(Math.Abs(out30 - released!.Value) <= 1, $"{rate}: the ranger comes out in frame {released}, at 30 Hz in frame {out30}.");
            }
            else (enteredAt30, releasedAt30) = (entered, released);

            // An ally's barracks: canGetHealedAt passes, canEnterObject refuses another player's faction structure, and privateEnter does nothing - under the product's
            // movement and the original's, where a walk would take its step of the frame before the enter state looks at the container again.
            foreach (var rules in new[] { CnCNew.Simulation.Rules.RuleProfile.Product, CnCNew.Simulation.Rules.RuleProfile.Original })
            {
            (world, ranger, barracks) = HealAtBarracksWorld(TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: rules), fingerprint, rate, 2);
            var ai = ranger.Modules.OfType<AIUpdateInterface>().Single();
            var (startX, startY) = (ranger.X, ranger.Y);
            var aimed = false;
            var walked = false;
            for (var step = 0; step < world.Frames(300); step++)
            {
                world.Step();
                aimed |= ai.EnterTarget.IsValid;
                walked |= ranger.HasMoveTarget;
            }
            Check.That(!aimed, $"{rate} {rules.Name}: the ranger is never ordered into an ally's barracks.");
            Check.That(!walked, $"{rate} {rules.Name}: the ranger never starts to walk to an ally's barracks.");
            Check.That(ranger.X == startX && ranger.Y == startY, $"{rate} {rules.Name}: the ranger stands where it was (it is at ({ranger.X}, {ranger.Y}), it was at ({startX}, {startY})).");
            Check.That(!ranger.ContainedBy.IsValid && ranger.Body!.Health < ranger.Body.MaxHealth, $"{rate} {rules.Name}: the ranger stays outside and hurt.");
            }
        }
    }
}
