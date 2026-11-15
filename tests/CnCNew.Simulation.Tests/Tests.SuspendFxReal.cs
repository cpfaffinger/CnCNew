using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Real data, #355: <c>NukeRadiationFieldWeapon</c> holds its fire effect back for <c>SuspendFXDelay = 10000</c> ms (300 frames of the original) after the radiation field
/// appears, so the field's own puddles (<c>WeaponFX_LargeRadiationFieldWeapon</c>) come about ten seconds after the blast, not on top of <c>NukeRadiationInitial</c>. The field's
/// pulses before that still hurt; its second weapon (<c>NukeMissileRadiationHazardFieldWeapon</c>) has no delay and is never held back.
/// </summary>
static partial class Tests
{
    public static void RealNukeRadiationFieldHoldsItsPuddlesBack(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "America"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(64, 64, 1, (_, _) => 0), "radiation");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "radiation", fingerprint, terrain.Fingerprint), catalog, terrain);
            var feeder = new Feeder(world);
            Check.That(world.Weapons.TryGet("NukeRadiationFieldWeapon", out var weapon) && weapon.SuspendFxDelayTicks == world.Frames(300),
                $"{rate}: SuspendFXDelay 10000 ms is 300 frames of the original.");
            Check.That(feeder.Spawn(1, "NukeRadiationFieldWeapon", x: 300, y: 300).Accepted, $"{rate}: the radiation field spawns.");
            world.Step();
            var field = world.Entities.Single(value => value.Template.Name == "NukeRadiationFieldWeapon");
            var perFrame = world.Time.TicksPerOriginalFrame;
            var quiet = new List<long>();
            var shown = new List<long>();
            var hazardQuiet = 0;
            for (var index = 0; index < world.Frames(600); index++)
                foreach (var item in world.Step().Events)
                {
                    if (item.Kind != SimEventKind.WeaponFired || item.Entity != field.Id) continue;
                    if (item.Text != "NukeRadiationFieldWeapon") { if (item.FireFxSuspended) hazardQuiet++; continue; }
                    (item.FireFxSuspended ? quiet : shown).Add((item.Tick - field.SpawnTick) / perFrame);
                }
            Check.That(quiet.Count > 0 && quiet.All(frame => frame < 300), $"{rate}: the pulses of the first ten seconds play no puddles ({string.Join(" ", quiet)}).");
            Check.That(shown.Count > 0 && shown.All(frame => frame >= 300), $"{rate}: from ten seconds on the pulses play their puddles ({string.Join(" ", shown)}).");
            Check.That(shown[0] == 300, $"{rate}: the first puddles come at frame 300 of the field, got {shown[0]}.");
            Check.That(hazardQuiet == 0, $"{rate}: the hazard weapon, without the key, is never held back.");
        }
    }
}
