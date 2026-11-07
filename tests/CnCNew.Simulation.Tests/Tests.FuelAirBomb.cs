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
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Real data, the owner's playtest of 7 November 2026: the Daisy Cutter (the fuel air bomb of the USA's rank-5 power) never did damage and never exploded. The bomb's creation list
    /// makes the <c>DaisyCutterGas</c> object <c>LIKE_EXISTING</c> in the air, and the original lets what is made in the air fall (<c>ObjectCreationNugget::reallyCreate</c>:
    /// <c>setAllowToFall</c>, and its physics module applies gravity to everything above the ground) - the port's physics module only fell what it was told to let go, so the gas hung at
    /// 49 units for ever and its <c>HeightDieUpdate</c> never saw it come down. Through the real chain: <c>UseSpecialPowerCommand</c> at the command center -> <c>OCLSpecialPower</c> ->
    /// <c>SUPERWEAPON_DaisyCutter</c> (or <c>SUPERWEAPON_MOAB</c> with the science) -> the plane with its payload -> the bomb -> the gas -> its slow death and
    /// <c>DaisyCutterDetonationWeapon</c> (the MOAB: <c>MOABDetonationWeapon</c> as the bomb dies); an enemy at the target is gone, one far outside the radius is not.
    /// </summary>
    public static void RealDaisyCutterAndFuelAirBomb(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));

        string Bomb(bool moab)
        {
            var terrain = CnCNew.Simulation.Terrain.TerrainMap.Build(SyntheticMaps.Build(400, 400, 1, (_, _) => 0), "real-bomb");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 5, players, "real-bomb", fingerprint, terrain.Fingerprint), catalog, terrain);
            ulong sequence = 0;
            SubmitResult Send(System.Func<ulong, SimCommand> make) => world.Submit(make(++sequence));
            void Spawn(string template, int owner, int x, int y)
                => Check.That(Send(next => new SpawnCommand(new PlayerId(owner), next, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, $"{template} spawns.");
            Spawn("AmericaCommandCenter", 1, 500, 500);
            Spawn("AmericaTankCrusader", 2, 2500, 2500);
            Spawn("AmericaTankCrusader", 2, 2560, 2500);
            Spawn("AmericaBarracks", 2, 2500, 2580);
            Spawn("AmericaTankCrusader", 2, 2900, 2500);
            world.Step();
            Check.That(world.GrantScience(new PlayerId(1), "SCIENCE_DaisyCutter"), "The science of the power is granted.");
            if (moab) Check.That(world.GrantScience(new PlayerId(1), "SCIENCE_MOAB"), "The science of the fuel air bomb is granted.");
            var center = world.Entities.First(value => value.Template.Name == "AmericaCommandCenter");
            var victims = world.Entities.Where(value => value.Owner == new PlayerId(2)).ToArray();
            var near = victims.Where(value => value.X < Fixed.FromInt(2800)).ToArray();
            var far = victims.Single(value => value.X > Fixed.FromInt(2800));
            // An order is given at the start of a frame of the original, at every rate (docs/SIMULATION-CORE.md section 43).
            while (world.Tick % world.Time.OriginalFrames(1) != 0) world.Step();
            Check.That(Send(next => new UseSpecialPowerCommand(new PlayerId(1), next, world.Tick, center.Id, "SuperweaponDaisyCutter", SpecialPowerTargetKind.Location, EntityId.Invalid, Fixed.FromInt(2500), Fixed.FromInt(2500))).Accepted,
                "The power is used.");
            var bombTemplate = moab ? "MOAB" : "DaisyCutterBomb";
            var seen = new HashSet<string>();
            long? goneAt = null;
            for (var tick = 0; tick < world.Frames(30 * 90) && goneAt is null; tick++)
            {
                var result = world.Step();
                foreach (var e in result.Events)
                    if (e.Kind == SimEventKind.EntitySpawned) seen.Add(e.Text);
                if (near.All(value => value.EffectivelyDead || !world.Entities.Contains(value))) goneAt = world.Tick;
            }
            Check.That(seen.Contains("AmericaJetB3") == moab && seen.Contains("AmericaJetB52") == !moab && seen.Contains(bombTemplate), $"{(moab ? "The fuel air bomb" : "The Daisy Cutter")} is delivered by its own plane ({string.Join(",", seen)}).");
            Check.That(moab || seen.Contains("DaisyCutterGas"), "The Daisy Cutter's bomb makes its gas.");
            Check.That(goneAt is not null, $"{(moab ? "The fuel air bomb" : "The Daisy Cutter")} destroys the two tanks and the barracks at the target.");
            Check.That(far.Body!.Health == far.Body.MaxHealth && !far.EffectivelyDead, "The tank 400 units from the target is untouched.");
            return $"{(moab ? "MOAB" : "Daisy Cutter")}: everything at the target gone after {TestRate.InFrames(goneAt ?? -1)} original frames";
        }
        System.Console.WriteLine("       " + Bomb(false));
        System.Console.WriteLine("       " + Bomb(true));
    }
}
