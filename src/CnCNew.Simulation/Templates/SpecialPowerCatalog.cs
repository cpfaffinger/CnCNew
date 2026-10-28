using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Templates;

/// <summary>
/// One <c>SpecialPower</c> definition (<c>SpecialPowerTemplate</c>): <see cref="ReloadTicks"/> is <c>ReloadTime</c>, <see cref="RequiredScience"/>
/// the <see cref="ScienceInfo.Index"/> the controlling player must own (-1: none), <see cref="SharedSynced"/> the <c>SharedSyncedTimer</c> flag.
/// </summary>
public sealed record SpecialPowerInfo(int Index, string Name, string Type, long ReloadTicks, int RequiredScience, bool SharedSynced, bool PublicTimer)
{
    /// <summary><c>RadiusCursorRadius</c>: the area the power affects, as far as the script AI aims it (<c>SKIRMISH_FIRE_SPECIAL_POWER_AT_MOST_COST</c>); zero when the definition declares none.</summary>
    public Fixed RadiusCursorRadius { get; init; }

    /// <summary><c>ViewObjectDuration</c> in ticks: how long the view a trigger opens at the target stays (0: none).</summary>
    public long ViewObjectTicks { get; init; }

    /// <summary><c>ViewObjectRange</c>: the shroud clearing range of that view (0: none).</summary>
    public Fixed ViewObjectRange { get; init; }
}

/// <summary>
/// <c>SpecialPowerStore</c> equivalent: every <c>SpecialPower</c> definition with a stable ordinal index (ordinal name order).
/// An absent <c>ReloadTime</c> uses the 1000 ms of <c>Default/SpecialPower.ini</c> (<c>DefaultSpecialPower</c>): the compiler has no
/// default-inheritance for this definition kind, so the value is carried here and counted by the real-data test.
/// </summary>
public sealed class SpecialPowerCatalog
{
    private const long DefaultReloadMilliseconds = 1000;
    private readonly ImmutableDictionary<string, SpecialPowerInfo> _byName;

    private SpecialPowerCatalog(ImmutableArray<SpecialPowerInfo> powers, int defaultedReload)
    {
        Powers = powers;
        DefaultedReloadCount = defaultedReload;
        _byName = powers.ToImmutableDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
    }

    public ImmutableArray<SpecialPowerInfo> Powers { get; }

    /// <summary>How many definitions took the <c>DefaultSpecialPower</c> reload time because they declare none.</summary>
    public int DefaultedReloadCount { get; }

    public SpecialPowerInfo this[int index] => Powers[index];
    public bool TryGet(string name, out SpecialPowerInfo info) => _byName.TryGetValue(name, out info!);

    public static SpecialPowerCatalog Build(CompiledDefinitionGraph graph, ScienceCatalog sciences, SimulationTime timing)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(sciences);
        var powers = ImmutableArray.CreateBuilder<SpecialPowerInfo>();
        var defaulted = 0;
        foreach (var definition in graph.Definitions.OfType<SpecialPowerDefinition>().OrderBy(value => value.Id.ToString(), StringComparer.Ordinal))
        {
            DefinitionField? Field(string name) => definition.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var reload = Field("ReloadTime")?.Value.Value;
            if (reload is null) defaulted++;
            var reloadTicks = timing.DurationMillisecondsToTicks(reload is GameTime time ? time.Milliseconds : DefaultReloadMilliseconds);
            var science = -1;
            if (Field("RequiredScience")?.Value.Value is DefinitionReference { IsNone: false } reference)
            {
                if (!sciences.TryGet(reference.Name, out var info))
                    throw new TemplateBuildException($"SpecialPower '{definition.OriginalName}': RequiredScience '{reference.Name}' is not a Science definition.");
                science = info.Index;
            }
            var sharedSynced = Field("SharedSyncedTimer")?.Value.Value is true;
            var publicTimer = Field("PublicTimer")?.Value.Value is true;
            var type = Field("Enum")?.Value.OriginalText.Trim() ?? string.Empty;
            var radius = Field("RadiusCursorRadius")?.Value.Value switch
            {
                Distance distance => Fixed.FromDecimal(distance.Value),
                CompatibilityValue compatibility when decimal.TryParse(compatibility.NormalizedValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => Fixed.FromDecimal(parsed),
                long whole => Fixed.FromInt(whole),
                _ => Fixed.Zero,
            };
            var viewTicks = Field("ViewObjectDuration")?.Value.Value is GameTime view ? timing.DurationMillisecondsToTicks(view.Milliseconds) : 0;
            var viewRange = Field("ViewObjectRange")?.Value.Value is decimal range ? Fixed.FromDecimal(range) : Fixed.Zero;
            powers.Add(new SpecialPowerInfo(powers.Count, definition.OriginalName, type, reloadTicks, science, sharedSynced, publicTimer)
            {
                RadiusCursorRadius = radius, ViewObjectTicks = viewTicks, ViewObjectRange = viewRange,
            });
        }
        return new SpecialPowerCatalog(powers.ToImmutable(), defaulted);
    }
}
