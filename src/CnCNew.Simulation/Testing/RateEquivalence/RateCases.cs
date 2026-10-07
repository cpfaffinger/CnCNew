using System.Collections.Immutable;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.Testing;

/// <summary>The cases of one group with what its generator has to say: how many candidates it merged, and what it could not stage.</summary>
public sealed record RateCaseGroup(string Name, ImmutableArray<RateCase> Cases, ImmutableArray<string> Notes, ImmutableArray<string> NotCovered);

/// <summary>
/// The rate-equivalence suite (<c>docs/SIMULATION-CORE.md</c> §39): fixed cases and cases generated from the 30 Hz catalog (one per distinct locomotor, one
/// per distinct weapon), found again by template or weapon name in the catalogs of the other rates. The suite measures; it does not decide what is right.
/// </summary>
public static partial class RateCases
{
    public const string Locomotion = "locomotion", Flight = "flight", Weapons = "weapons", Projectiles = "projectiles", Turrets = "turrets", OverTime = "overtime",
        Economy = "economy", Timed = "timed", Scripts = "scripts", Ai = "ai";

    public static ImmutableArray<string> Groups { get; } = [Locomotion, Flight, Weapons, Projectiles, Turrets, OverTime, Economy, Timed, Scripts, Ai];

    public static RateCaseGroup Group(RateCatalogs catalogs, string name) => name switch
    {
        Locomotion => LocomotionCases(catalogs),
        Flight => FlightCases(catalogs),
        Weapons => WeaponCases(catalogs),
        Projectiles => ProjectileCases(catalogs),
        Turrets => TurretCases(catalogs),
        OverTime => OverTimeCases(catalogs),
        Economy => EconomyCases(catalogs),
        Timed => TimedCases(catalogs),
        Scripts => ScriptCases(catalogs),
        Ai => AiCases(catalogs),
        _ => throw new ArgumentException($"Unknown group {name}; the groups are {string.Join(", ", Groups)}."),
    };

    public static ImmutableArray<RateCaseGroup> AllGroups(RateCatalogs catalogs) => [..Groups.Select(name => Group(catalogs, name))];

    public static ImmutableArray<RateCase> All(RateCatalogs catalogs) => [..AllGroups(catalogs).SelectMany(group => group.Cases)];

    /// <summary>
    /// A count of steps since an order (1: the step that executed it) as the ticks that passed since that step: the convention of every time of the suite,
    /// <see cref="RateScene.StepUntil(Func{bool}, long)"/>. Negative (never happened) stays negative.
    /// </summary>
    private static long Elapsed(long steps) => steps > 0 ? steps - 1 : steps;

    /// <summary>Collects the cases of a group; a case whose templates are missing from the catalog is listed as not covered instead of failing at every rate.</summary>
    private sealed class GroupBuilder(RateCatalogs catalogs, string group)
    {
        private readonly ImmutableArray<RateCase>.Builder _cases = ImmutableArray.CreateBuilder<RateCase>();
        private readonly ImmutableArray<string>.Builder _notes = ImmutableArray.CreateBuilder<string>();
        private readonly ImmutableArray<string>.Builder _notCovered = ImmutableArray.CreateBuilder<string>();

        public TemplateCatalog Reference => catalogs.Reference;

        public void Add(string id, Func<RateScene, ImmutableArray<RateQuantity>> measure, params string[] templates)
        {
            var missing = templates.Where(name => !catalogs.Reference.TryGet(name, out var template) || !template.IsInstantiable).ToArray();
            if (missing.Length > 0)
            {
                _notCovered.Add($"{group}/{id}: the catalog has no instantiable {string.Join(", ", missing)}");
                return;
            }
            _cases.Add(new RateCase($"{group}/{id}", group, measure));
        }

        public void Note(string text) => _notes.Add(text);
        public void NotCovered(string text) => _notCovered.Add(text);
        public RateCaseGroup Done() => new(group, _cases.ToImmutable(), _notes.ToImmutable(), _notCovered.ToImmutable());
    }
}
