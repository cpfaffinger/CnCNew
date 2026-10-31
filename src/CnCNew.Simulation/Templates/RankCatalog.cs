using System.Collections.Immutable;
using CnCNew.Data.Definitions;

namespace CnCNew.Simulation.Templates;

/// <summary>One level of a general (<c>RankInfo</c>, <c>Rank.ini</c>).</summary>
public sealed record RankInfo(int Level, int SkillPointsNeeded, ImmutableArray<ScienceInfo> SciencesGranted, int SciencePurchasePointsGranted);

/// <summary>The levels of a general in their order (<c>RankInfoStore</c>). A catalog of synthetic data without a <c>Rank</c> block has none, and its players have no rank.</summary>
public sealed class RankCatalog
{
    private RankCatalog(ImmutableArray<RankInfo> ranks) => Ranks = ranks;

    public ImmutableArray<RankInfo> Ranks { get; }

    /// <summary><c>RankInfoStore::getRankLevelCount</c>.</summary>
    public int LevelCount => Ranks.Length;

    /// <summary><c>RankInfoStore::getRankInfo</c>: null below the first level and above the last.</summary>
    public RankInfo? At(int level) => level >= 1 && level <= Ranks.Length ? Ranks[level - 1] : null;

    public static RankCatalog Build(CompiledDefinitionGraph graph, ScienceCatalog sciences)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(sciences);
        var ranks = new List<RankInfo>();
        foreach (var definition in graph.Definitions.OfType<RankDefinition>())
        {
            DefinitionField? Field(string name) => definition.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (!int.TryParse(definition.OriginalName, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var level))
                throw new InvalidOperationException($"A Rank is named by its level, not '{definition.OriginalName}'.");
            var names = Field("SciencesGranted")?.Value.Value switch
            {
                DefinitionReference { IsNone: false } single => ImmutableArray.Create(single.Name),
                ImmutableArray<DefinitionReference> many => [..many.Where(value => !value.IsNone).Select(value => value.Name)],
                _ => ImmutableArray<string>.Empty,
            };
            ranks.Add(new RankInfo(level, Field("SkillPointsNeeded")?.Value.Value is long needed ? checked((int)needed) : 0,
                [..names.Select(name => sciences.TryGet(name, out var science) ? science : throw new InvalidOperationException($"Rank {level} grants '{name}', which is not a Science definition."))],
                Field("SciencePurchasePointsGranted")?.Value.Value is long points ? checked((int)points) : 0));
        }
        ranks.Sort((a, b) => a.Level.CompareTo(b.Level));
        for (var index = 0; index < ranks.Count; index++)
            if (ranks[index].Level != index + 1) throw new InvalidOperationException($"The levels of Rank.ini are not 1 to {ranks.Count}: {ranks[index].Level} at place {index + 1}.");
        return new RankCatalog([..ranks]);
    }
}
