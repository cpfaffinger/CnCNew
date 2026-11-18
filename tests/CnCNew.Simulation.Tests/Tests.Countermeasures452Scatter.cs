using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// #452: a scattered small missile is reported to its victim's countermeasures, on real data.
static partial class Tests
{
    /// <summary>The real definitions with one weapon's <c>ScatterRadius</c> changed, as a map's <c>map.ini</c> may change it (the shipped data has no scattering small missile).</summary>
    private sealed class ScatteredWeaponSource(ContentFileSystem content, string weapon, int radius) : IDefinitionSource
    {
        public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        public bool Exists(string virtualPath) => content.Exists(virtualPath);

        public Stream OpenRead(string virtualPath)
        {
            if (!virtualPath.EndsWith("INI/Weapon.ini", StringComparison.OrdinalIgnoreCase)) return content.OpenRead(virtualPath);
            string text;
            using (var reader = new StreamReader(content.OpenRead(virtualPath), Encoding.Latin1)) text = reader.ReadToEnd();
            var block = new Regex($@"^Weapon\s+{Regex.Escape(weapon)}\s*$.*?^End", RegexOptions.Multiline | RegexOptions.Singleline).Match(text);
            Check.That(block.Success, $"Weapon.ini has the weapon {weapon}.");
            var edited = new Regex(@"^(\s*)ScatterRadius\s*=\s*[0-9.]+", RegexOptions.Multiline).Replace(block.Value, $"$1ScatterRadius = {radius}", 1);
            Check.That(edited != block.Value, $"{weapon} names its ScatterRadius.");
            return new MemoryStream(Encoding.Latin1.GetBytes(text[..block.Index] + edited + text[(block.Index + block.Length)..]));
        }
    }

    /// <summary>
    /// #452, the scatter: <c>WeaponTemplate::fireWeaponTemplate</c> reports a small missile to its victim's countermeasures after the launch with <c>victimObj</c>, which a scatter leaves set -
    /// only the projectile is told to fly at the scattered place. With the Tank Hunter's missile launcher given a <c>ScatterRadius</c>, a Comanche with <c>Upgrade_AmericaCountermeasures</c> hovering
    /// over Tank Hunters hears of their missiles and launches its flares, as it does with the shipped weapon - before, a scattered shot was never reported. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealScatteredMissilesAreReportedToCountermeasures(string directory)
    {
        const string Launcher = "ChinaInfantryTankHunterMissileLauncher";
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new ScatteredWeaponSource(content, Launcher, 40), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        foreach (var rate in TargetRates)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            Check.That(catalog.Weapons.TryGet(Launcher, out var weapon) && weapon!.ScatterRadius == Fixed.FromInt(40), $"{rate}: the launcher scatters.");
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "scattered-missiles");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-scattered-missiles", fingerprint, terrain.Fingerprint), catalog, terrain);
            world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_AmericaCountermeasures");
            var air = SpawnReal(world, ("AmericaVehicleComanche", 1, 300, 400)).Single();
            var countermeasures = air.Modules.OfType<CountermeasuresBehavior>().Single();
            _ = SpawnReal(world, ("ChinaInfantryTankHunter", 2, 560, 380), ("ChinaInfantryTankHunter", 2, 560, 400), ("ChinaInfantryTankHunter", 2, 560, 420),
                ("ChinaInfantryTankHunter", 2, 580, 390), ("ChinaInfantryTankHunter", 2, 580, 410));
            Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++_sequence, world.Tick, air.Id, Fixed.FromInt(420), Fixed.FromInt(400))).Accepted, $"{rate}: the Comanche flies out.");
            var known = world.Entities.Select(value => value.Id).ToHashSet();
            var (missiles, flares) = (0, 0);
            for (var step = 0; step < world.Frames(30 * 20); step++)
            {
                world.Step();
                if (air.Body!.Health < air.Body.MaxHealth)
                    world.Submit(new DamageCommand(PlayerId.System, ++_sequence, world.Tick, air.Id, air.Body.MaxHealth, DamageTypes.Healing, 0));
                foreach (var entity in world.Entities)
                {
                    if (!known.Add(entity.Id)) continue;
                    if (entity.Template.Name == "TankHunterMissile") missiles++;
                    if (entity.Template.Name == "CountermeasureFlare") flares++;
                }
            }
            var (incoming, diverted) = countermeasures.MissileCounts;
            Check.That(missiles > 0, $"{rate}: the Tank Hunters fire at the Comanche ({missiles} missiles).");
            Check.That(incoming == missiles, $"{rate}: every scattered missile is reported to the Comanche's countermeasures ({incoming} of {missiles}).");
            Check.That(diverted > 0 && flares > 0, $"{rate}: some are taken by flares, and it launches them ({diverted} diverted, {flares} flares).");
        }
    }
}
