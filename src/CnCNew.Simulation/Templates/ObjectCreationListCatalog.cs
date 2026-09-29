using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Templates;

public enum CreationNuggetKind { CreateObject, CreateDebris, ApplyRandomForce, FireWeapon, Attack, DeliverPayload }

/// <summary>Bit set over the <c>DebrisDisposition</c> name table (<c>ObjectCreationList.cpp dispositionNames</c>).</summary>
[Flags]
public enum CreationDisposition
{
    None = 0,
    LikeExisting = 1 << 0,
    OnGroundAligned = 1 << 1,
    SendItFlying = 1 << 2,
    SendItUp = 1 << 3,
    SendItOut = 1 << 4,
    RandomForce = 1 << 5,
    Floating = 1 << 6,
    InheritVelocity = 1 << 7,
    Whirling = 1 << 8,
}

/// <summary>
/// One nugget of an <c>ObjectCreationList</c>, with the defaults of the <c>GenericObjectCreationNugget</c> constructor
/// (count 1, disposition <c>ON_GROUND_ALIGNED</c>, health 100 %..100 %, no lifetime override, container-less).
/// <see cref="Objects"/> lists the pickable template names (<c>CreateObject</c> only); <see cref="Weapon"/> is the <c>FireWeapon</c> weapon.
/// Object, container and weapon names are not validated here: an unknown name is reported by the executing world as an explicit
/// <c>CreationSkipped</c> event, never dropped silently.
/// </summary>
public sealed record CreationNugget(CreationNuggetKind Kind, int Count, CreationDisposition Disposition, bool SpreadFormation,
    Fixed MinDistanceA, Fixed MinDistanceB, Fixed MaxDistance, Fixed OffsetX, Fixed OffsetY, Fixed OffsetZ,
    long MinLifetimeTicks, long MaxLifetimeTicks, ImmutableArray<string> Objects, string PutInContainer, bool ContainInsideSourceObject,
    bool InheritsVeterancy, bool SkipIfSignificantlyAirborne, Fixed MinHealth, Fixed MaxHealth, bool RequiresLivePlayer, string Weapon,
    long InvulnerableTicks, bool DiesOnBadLand);

public sealed record CreationListInfo(string Name, ImmutableArray<CreationNugget> Nuggets);

/// <summary><c>ObjectCreationListStore</c> equivalent: every <c>ObjectCreationList</c> definition, addressed by name.</summary>
public sealed class ObjectCreationListCatalog
{
    private readonly ImmutableDictionary<string, CreationListInfo> _byName;

    private ObjectCreationListCatalog(ImmutableArray<CreationListInfo> lists)
    {
        Lists = lists;
        _byName = lists.ToImmutableDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
    }

    public ImmutableArray<CreationListInfo> Lists { get; }
    public bool TryGet(string name, out CreationListInfo info) => _byName.TryGetValue(name, out info!);

    public static ObjectCreationListCatalog Build(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var lists = ImmutableArray.CreateBuilder<CreationListInfo>();
        foreach (var definition in graph.Definitions.OfType<ObjectCreationListDefinition>().OrderBy(value => value.Id.ToString(), StringComparer.Ordinal))
        {
            var nuggets = ImmutableArray.CreateBuilder<CreationNugget>();
            foreach (var section in definition.Sections)
            {
                if (!Enum.TryParse<CreationNuggetKind>(section.Declaration, true, out var kind))
                    throw new TemplateBuildException($"ObjectCreationList '{definition.OriginalName}': nugget '{section.Declaration}' is not known.");
                nuggets.Add(BuildNugget(kind, section));
            }
            lists.Add(new CreationListInfo(definition.OriginalName, nuggets.ToImmutable()));
        }
        return new ObjectCreationListCatalog(lists.ToImmutable());
    }

    private static CreationNugget BuildNugget(CreationNuggetKind kind, DefinitionSection section)
    {
        DefinitionField? Field(string name) => section.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Fixed Real(string name, Fixed fallback) => Field(name)?.Value.Value switch
        {
            decimal value => Fixed.FromDecimal(value),
            Distance distance => Fixed.FromDecimal(distance.Value),
            _ => fallback,
        };
        bool Bool(string name) => Field(name)?.Value.Value is true;
        long Ticks(string name) => Field(name)?.Value.Value is GameTime time ? SimulationTiming.DurationMillisecondsToTicks(time.Milliseconds) : 0;
        string Reference(string name) => Field(name)?.Value.Value is DefinitionReference { IsNone: false } reference ? reference.Name : string.Empty;

        var disposition = CreationDisposition.OnGroundAligned;
        if (Field("Disposition")?.Value.Value is FlagsValue flags)
        {
            disposition = CreationDisposition.None;
            foreach (var name in flags.Names)
                disposition |= name.ToUpperInvariant() switch
                {
                    "LIKE_EXISTING" => CreationDisposition.LikeExisting,
                    "ON_GROUND_ALIGNED" => CreationDisposition.OnGroundAligned,
                    "SEND_IT_FLYING" => CreationDisposition.SendItFlying,
                    "SEND_IT_UP" => CreationDisposition.SendItUp,
                    "SEND_IT_OUT" => CreationDisposition.SendItOut,
                    "RANDOM_FORCE" => CreationDisposition.RandomForce,
                    "FLOATING" => CreationDisposition.Floating,
                    "INHERIT_VELOCITY" => CreationDisposition.InheritVelocity,
                    "WHIRLING" => CreationDisposition.Whirling,
                    _ => throw new TemplateBuildException($"Disposition '{name}' is not known."),
                };
        }

        var offset = Field("Offset")?.Value.Value is Vector3Value vector
            ? (Fixed.FromDecimal(vector.X), Fixed.FromDecimal(vector.Y), Fixed.FromDecimal(vector.Z))
            : (Fixed.Zero, Fixed.Zero, Fixed.Zero);
        var objects = ImmutableArray.CreateBuilder<string>();
        foreach (var field in section.Fields.Where(field => field.Name.Equals("ObjectNames", StringComparison.OrdinalIgnoreCase)))
            if (field.Value.Value is ImmutableArray<DefinitionReference> references)
                foreach (var reference in references) objects.Add(reference.Name);
        var count = Field("Count")?.Value.Value is long declared ? checked((int)declared) : 1;
        var health = Field("MinHealth")?.Value.Value is Percentage min ? Fixed.FromDecimal(min.Ratio) : Fixed.One;
        var maxHealth = Field("MaxHealth")?.Value.Value is Percentage max ? Fixed.FromDecimal(max.Ratio) : Fixed.One;

        return new CreationNugget(kind, count, disposition, Bool("SpreadFormation"),
            Real("MinDistanceAFormation", Fixed.Zero), Real("MinDistanceBFormation", Fixed.Zero), Real("MaxDistanceFormation", Fixed.Zero),
            offset.Item1, offset.Item2, offset.Item3, Ticks("MinLifetime"), Ticks("MaxLifetime"), objects.ToImmutable(), Reference("PutInContainer"),
            Bool("ContainInsideSourceObject"), Bool("InheritsVeterancy"), Bool("SkipIfSignificantlyAirborne"), health, maxHealth,
            Bool("RequiresLivePlayer"), Reference("Weapon"), Ticks("InvulnerableTime"), Bool("DiesOnBadLand"));
    }
}
