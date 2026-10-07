using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.SimTool;

/// <summary>
/// <c>range-audit &lt;content-dir&gt; [--frames N] [--distance UNITS]</c>: every retail object that carries a weapon is put next to an enemy tank and an
/// enemy soldier, is ordered to fire at each and then left alone, for <c>N</c> logic frames of the original. Whatever the data hold as a range, a
/// radius or a speed passes through the comparisons of the order, of target acquisition, of the turrets and of the shot; an exception of any step is
/// caught and listed with the place that threw. The weapons whose data hold a value beyond the square limit of the fixed-point numbers
/// (docs/FIXED-POINT-Q39-24.md §5) are listed with the objects that carry them and the shots they fired.
/// A sweep for arithmetic that leaves the range, not a test of behaviour: an object that never fires is not a finding.
/// </summary>
internal static class RangeAudit
{
    private static readonly string[] Victims = ["ChinaTankBattleMaster", "ChinaInfantryRedguard"];

    public static int Run(string[] args, CompileShipped compile)
    {
        var (_, catalog, fingerprint) = compile(args[1]);
        var frames = Number(args, "--frames") ?? 150;
        var distance = (int)(Number(args, "--distance") ?? 250);
        var limit = Fixed.Sqrt(Fixed.MaxValue);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        foreach (var victim in Victims)
            if (!catalog.TryGet(victim, out _)) throw new InvalidOperationException($"The victim template '{victim}' is not in the catalog.");

        bool Beyond(WeaponInfo weapon) => weapon.AttackRange > limit || weapon.MinimumAttackRange > limit || weapon.WeaponSpeed > limit || weapon.MinWeaponSpeed > limit
                                          || weapon.PrimaryDamage > limit || weapon.PrimaryRadius > limit || weapon.SecondaryRadius > limit;

        var failures = new List<string>();
        var shotsOf = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var carriersOf = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        int objects = 0, runs = 0, firing = 0;
        long shots = 0;
        foreach (var template in catalog.Templates.OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            var weapons = template.WeaponSets.SelectMany(set => set.Weapons).Where(index => index >= 0).Distinct().Select(index => catalog.Weapons[index]).ToArray();
            if (weapons.Length == 0 || !template.IsInstantiable) continue;
            objects++;
            foreach (var weapon in weapons.Where(Beyond))
            {
                if (!carriersOf.TryGetValue(weapon.Name, out var carriers)) carriersOf[weapon.Name] = carriers = new SortedSet<string>(StringComparer.Ordinal);
                carriers.Add(template.Name);
            }
            var fired = false;
            foreach (var victimTemplate in Victims)
            {
                runs++;
                var step = "spawn";
                try
                {
                    var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), ToolRate.Current, 7, players, "range-audit", fingerprint), catalog);
                    ulong system = 0, own = 0;
                    void Must(SimCommand command)
                    {
                        var result = world.Submit(command);
                        if (!result.Accepted) throw new InvalidOperationException($"{command.GetType().Name} refused: {result.Reason}");
                    }
                    Must(new SpawnCommand(PlayerId.System, ++system, world.Tick, template.Name, new PlayerId(1), Fixed.FromInt(300), Fixed.FromInt(600), Fixed.Zero, Fixed.Zero));
                    Must(new SpawnCommand(PlayerId.System, ++system, world.Tick, victimTemplate, new PlayerId(2), Fixed.FromInt(300 + distance), Fixed.FromInt(600), Fixed.Zero, Fixed.Zero));
                    Must(new SetRelationshipCommand(PlayerId.System, ++system, world.Tick, new PlayerId(1), new PlayerId(2), Relationship.Enemies));
                    Must(new SetRelationshipCommand(PlayerId.System, ++system, world.Tick, new PlayerId(2), new PlayerId(1), Relationship.Enemies));
                    world.RevealMapPermanently(new PlayerId(1), true);
                    world.Step();
                    if (world.FindEntity(new EntityId(1)) is not { } shooter || world.FindEntity(new EntityId(2)) is not { } victim) continue;
                    step = "fire order";
                    world.Submit(new FireWeaponCommand(new PlayerId(1), ++own, world.Tick, shooter.Id, victim.Id));
                    step = "ticks after the order";
                    for (var tick = 0; tick < world.Time.OriginalFrames(frames); tick++)
                    {
                        foreach (var item in world.Step().Events)
                        {
                            if (item.Kind != SimEventKind.WeaponFired || item.Entity != shooter.Id) continue;
                            shots++;
                            fired = true;
                            shotsOf[item.Text ?? string.Empty] = shotsOf.GetValueOrDefault(item.Text ?? string.Empty) + 1;
                        }
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    var place = exception.StackTrace?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Contains("CnCNew.Simulation.", StringComparison.Ordinal) && !line.Contains(".Numerics.", StringComparison.Ordinal)) ?? "?";
                    var cut = place.IndexOf(" in ", StringComparison.Ordinal);
                    failures.Add($"{template.Name} against {victimTemplate}, {step}: {exception.GetType().Name}: {exception.Message} [{(cut > 0 ? place[..cut] : place)}]");
                }
            }
            if (fired) firing++;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Range audit at {ToolRate.Current}: {objects} objects with a weapon, {runs} runs of {frames} frames against {string.Join(" and ", Victims)} at {distance} units; {firing} objects fired, {shots} shots; {failures.Count} runs threw."));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Weapons with a value beyond the square limit ({limit.ToDecimal():F0}): {carriersOf.Count} carried by an object."));
        Console.WriteLine($"  {"weapon",-44} {"AttackRange",12} {"WeaponSpeed",12} {"MinSpeed",10} {"objects",8} {"shots",7}  first carriers");
        foreach (var (name, carriers) in carriersOf)
        {
            var weapon = catalog.Weapons.Weapons.Single(value => value.Name == name);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {name,-44} {weapon.AttackRange.ToDecimal(),12:G9} {weapon.WeaponSpeed.ToDecimal(),12:G9} {weapon.MinWeaponSpeed.ToDecimal(),10:G9} {carriers.Count,8} {shotsOf.GetValueOrDefault(name),7}  {string.Join(", ", carriers.Take(3))}"));
        }
        foreach (var failure in failures) Console.WriteLine("  THREW  " + failure);
        return failures.Count == 0 ? 0 : 1;
    }

    private static long? Number(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? long.Parse(args[index + 1], CultureInfo.InvariantCulture) : null;
    }
}
