using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
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
    long InvulnerableTicks, bool DiesOnBadLand, PayloadDelivery? Delivery = null, int AttackShots = 1, int AttackSlot = 0, CreationThrow? Throw = null);

/// <summary>
/// What <c>doStuffToObj</c> gives a created object with a physics module (F-66): the <c>Mass</c> of a debris nugget, <c>DispositionIntensity</c>, the spin and the yaw, roll and pitch
/// rates (radians per logic frame of the original, below zero when absent - "calc it based on intensity"), the force range of <c>RANDOM_FORCE</c> (magnitudes, pitch in radians),
/// <c>OrientInForceDirection</c>, and how many <c>AnimationSet</c>s a debris nugget has (one is drawn for each piece). <c>ExtraBounciness</c> is not kept: the original stores it
/// on the physics module (<c>setExtraBounciness</c>) and never reads it.
/// </summary>
public sealed record CreationThrow(Fixed Mass, Fixed DispositionIntensity, Fixed SpinRate, Fixed YawRate, Fixed RollRate, Fixed PitchRate, Fixed MinForce, Fixed MaxForce,
    Fixed MinForcePitch, Fixed MaxForcePitch, bool OrientInForceDirection, int AnimationSets)
{
    public static CreationThrow None { get; } = new(Fixed.Zero, Fixed.Zero, -Fixed.One, -Fixed.One, -Fixed.One, -Fixed.One, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero, false, 0);

    /// <summary>The <c>ModelNames</c> of a debris nugget (every second word of each line, as <c>parseDebrisObjectNames</c> reads them): one is drawn for each piece (F-36).</summary>
    public ImmutableArray<string> Models { get; init; } = [];

    /// <summary><c>ExtraFriction</c> (<c>parseFrictionPerSec</c>, per logic frame of the original): added to the physics module's frictions of the thrown object (<c>setExtraFriction</c>).</summary>
    public Fixed ExtraFriction { get; init; }
}

/// <summary>
/// The data of a <c>DeliverPayload</c> nugget (<c>DeliverPayloadNugget</c>, <c>ObjectCreationList.cpp</c>): the transports (<see cref="FormationSize"/> of them, spaced
/// <see cref="FormationSpacing"/> apart), what they carry (<see cref="Payload"/>, optionally each packed into <see cref="PutInContainer"/>) and the run itself
/// (<see cref="Run"/>, <c>DeliverPayloadData</c> of <c>DeliverPayloadAIUpdate.cpp</c>).
/// </summary>
public sealed record PayloadDelivery(string Transport, bool StartAtPreferredHeight, bool StartAtMaxSpeed, int FormationSize, Fixed FormationSpacing, Fixed ConvergenceFactor, Fixed ErrorRadius,
    long DelayDeliveryMaxTicks, ImmutableArray<(string Name, int Count)> Payload, string PutInContainer, DeliveryRun Run);

/// <summary>
/// <c>DeliverPayloadData</c>: <see cref="DeliveryDistance"/> (<c>m_distToTarget</c>), <see cref="PreOpenDistance"/>, <see cref="MaxAttempts"/>, the drop (<see cref="DropDelayTicks"/>,
/// <see cref="DropOffset"/>, <see cref="DropVariance"/>, <see cref="ParachuteDirectly"/>), the visible payload that is created when it is dropped (<see cref="VisiblePayloadTemplate"/> with
/// <see cref="VisiblePayloadWeapon"/> for missiles, <see cref="VisibleNumBones"/> items, <see cref="VisibleItemsDroppedPerInterval"/> at a time), <see cref="FireWeapon"/>,
/// <see cref="SelfDestructObject"/> and the dive/strafe of bombers. Pitch rate, bone names and the velocity inheritance are presentation/physics and not modelled.
/// </summary>
public sealed record DeliveryRun(Fixed DeliveryDistance, Fixed PreOpenDistance, int MaxAttempts, long DropDelayTicks, Fixed DropOffsetX, Fixed DropOffsetY, Fixed DropOffsetZ,
    Fixed DropVarianceX, Fixed DropVarianceY, Fixed DropVarianceZ, bool ParachuteDirectly, int VisibleItemsDroppedPerInterval, int VisibleNumBones, string VisiblePayloadTemplate,
    string VisiblePayloadWeapon, bool SelfDestructObject, bool FireWeapon, Fixed DiveStartDistance, Fixed DiveEndDistance, bool Strafes, Fixed StrafeLength)
{
    /// <summary>Written only for runs that exist (a delivery in progress, a queued spawn), so worlds without deliveries keep their state.</summary>
    public void Describe(IStateWriter writer)
    {
        writer.Fixed("runDistance", DeliveryDistance);
        writer.Fixed("runPreOpen", PreOpenDistance);
        writer.Int("runAttempts", MaxAttempts);
        writer.Int("runDropDelay", DropDelayTicks);
        writer.Fixed("runOffsetX", DropOffsetX);
        writer.Fixed("runOffsetY", DropOffsetY);
        writer.Fixed("runOffsetZ", DropOffsetZ);
        writer.Fixed("runVarianceX", DropVarianceX);
        writer.Fixed("runVarianceY", DropVarianceY);
        writer.Fixed("runVarianceZ", DropVarianceZ);
        writer.Bool("runParachute", ParachuteDirectly);
        writer.Int("runItemsPerInterval", VisibleItemsDroppedPerInterval);
        writer.Int("runBones", VisibleNumBones);
        writer.Text("runVisibleTemplate", VisiblePayloadTemplate);
        writer.Text("runVisibleWeapon", VisiblePayloadWeapon);
        writer.Bool("runSelfDestruct", SelfDestructObject);
        writer.Bool("runFireWeapon", FireWeapon);
        writer.Fixed("runDiveStart", DiveStartDistance);
        writer.Fixed("runDiveEnd", DiveEndDistance);
        writer.Bool("runStrafes", Strafes);
        writer.Fixed("runStrafeLength", StrafeLength);
    }

    public static DeliveryRun Load(IStateReader reader) => new(reader.Fixed("runDistance"), reader.Fixed("runPreOpen"), checked((int)reader.Int("runAttempts")), reader.Int("runDropDelay"),
        reader.Fixed("runOffsetX"), reader.Fixed("runOffsetY"), reader.Fixed("runOffsetZ"), reader.Fixed("runVarianceX"), reader.Fixed("runVarianceY"), reader.Fixed("runVarianceZ"),
        reader.Bool("runParachute"), checked((int)reader.Int("runItemsPerInterval")), checked((int)reader.Int("runBones")), reader.Text("runVisibleTemplate"), reader.Text("runVisibleWeapon"),
        reader.Bool("runSelfDestruct"), reader.Bool("runFireWeapon"), reader.Fixed("runDiveStart"), reader.Fixed("runDiveEnd"), reader.Bool("runStrafes"), reader.Fixed("runStrafeLength"));
}

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

    public static ObjectCreationListCatalog Build(CompiledDefinitionGraph graph, SimulationTime timing)
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
                nuggets.Add(BuildNugget(kind, section, timing));
            }
            lists.Add(new CreationListInfo(definition.OriginalName, nuggets.ToImmutable()));
        }
        return new ObjectCreationListCatalog(lists.ToImmutable());
    }

    private static CreationNugget BuildNugget(CreationNuggetKind kind, DefinitionSection section, SimulationTime timing)
    {
        DefinitionField? Field(string name) => section.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Fixed Real(string name, Fixed fallback) => Field(name)?.Value.Value switch
        {
            decimal value => Fixed.FromDecimal(value),
            Distance distance => Fixed.FromDecimal(distance.Value),
            _ => fallback,
        };
        bool Bool(string name) => Field(name)?.Value.Value is true;
        long Ticks(string name) => Field(name)?.Value.Value is GameTime time ? timing.DurationMillisecondsToTicks(time.Milliseconds) : 0;
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
            Bool("RequiresLivePlayer"), Reference("Weapon"), Ticks("InvulnerableTime"), Bool("DiesOnBadLand"), kind == CreationNuggetKind.DeliverPayload ? BuildDelivery(section, timing) : null,
            // AttackNugget: NumberOfShots (1) and WeaponSlot (PRIMARY) of the source's attack on the second point.
            Field("NumberOfShots")?.Value.Value is long shots ? checked((int)shots) : 1,
            WeaponSlot.TryParse(Field("WeaponSlot")?.Value.OriginalText ?? string.Empty, out var slot) ? slot : WeaponSlot.Primary,
            BuildThrow(section));
    }

    /// <summary>The throw of a created object (<see cref="CreationThrow"/>): rates from degrees per second (<c>parseAngularVelocityReal</c>), pitches from degrees.</summary>
    private static CreationThrow BuildThrow(DefinitionSection section)
    {
        DefinitionField? Field(string name) => section.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Fixed Real(string name) => Field(name)?.Value.Value switch
        {
            decimal value => Fixed.FromDecimal(value),
            Distance distance => Fixed.FromDecimal(distance.Value),
            _ => Fixed.Zero,
        };
        Fixed Radians(string name) => Field(name)?.Value.Value is Angle angle ? SimulationTiming.DegreesToRadians(angle.Degrees) : Fixed.Zero;
        Fixed Rate(string name) => Field(name)?.Value.Value is Angle angle ? SimulationTiming.DegreesToRadians(angle.Degrees / CnCNew.Data.OriginalFrame.PerSecond) : -Fixed.One;
        var sets = section.Fields.Count(field => field.Name.Equals("AnimationSet", StringComparison.OrdinalIgnoreCase));
        var models = section.Fields.Where(field => field.Name.Equals("ModelNames", StringComparison.OrdinalIgnoreCase))
            .SelectMany(field => field.Value.Value is ImmutableArray<string> names ? names : []).ToImmutableArray();
        return new CreationThrow(Real("Mass"), Real("DispositionIntensity"), Rate("SpinRate"), Rate("YawRate"), Rate("RollRate"), Rate("PitchRate"),
            Real("MinForceMagnitude"), Real("MaxForceMagnitude"), Radians("MinForcePitch"), Radians("MaxForcePitch"), Field("OrientInForceDirection")?.Value.Value is true, sets)
        {
            Models = models,
            ExtraFriction = Real("ExtraFriction") / Fixed.FromInt(CnCNew.Data.OriginalFrame.PerSecond),
        };
    }

    private static PayloadDelivery BuildDelivery(DefinitionSection section, SimulationTime timing)
    {
        DefinitionField? Field(string name) => section.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Fixed Real(string name) => Field(name)?.Value.Value switch
        {
            decimal value => Fixed.FromDecimal(value),
            Distance distance => Fixed.FromDecimal(distance.Value),
            _ => Fixed.Zero,
        };
        bool Bool(string name, bool fallback = false) => Field(name)?.Value.Value is bool flag ? flag : fallback;
        int Int(string name, int fallback = 0) => Field(name)?.Value.Value is long number ? checked((int)number) : fallback;
        long Ticks(string name) => Field(name)?.Value.Value is GameTime time ? timing.DurationMillisecondsToTicks(time.Milliseconds) : 0;
        string Reference(string name) => Field(name)?.Value.Value is DefinitionReference { IsNone: false } reference ? reference.Name : string.Empty;
        (Fixed X, Fixed Y, Fixed Z) Vector(string name) => Field(name)?.Value.Value is Vector3Value vector ? (Fixed.FromDecimal(vector.X), Fixed.FromDecimal(vector.Y), Fixed.FromDecimal(vector.Z)) : (Fixed.Zero, Fixed.Zero, Fixed.Zero);

        var payload = ImmutableArray.CreateBuilder<(string Name, int Count)>();
        foreach (var field in section.Fields.Where(field => field.Name.Equals("Payload", StringComparison.OrdinalIgnoreCase)))
            switch (field.Value.Value)
            {
                case CountedReference counted: payload.Add((counted.Reference.Name, counted.Count)); break;
                case ImmutableArray<CountedReference> list: foreach (var counted in list) payload.Add((counted.Reference.Name, counted.Count)); break;
            }
        var offset = Vector("DropOffset");
        var variance = Vector("DropVariance");
        var strafes = Field("StrafingWeaponSlot")?.Value.OriginalText.Trim() is { Length: > 0 } slot && !slot.Equals("None", StringComparison.OrdinalIgnoreCase);
        var run = new DeliveryRun(Real("DeliveryDistance"), Real("PreOpenDistance"), Int("MaxAttempts", 1), Ticks("DropDelay"), offset.X, offset.Y, offset.Z, variance.X, variance.Y, variance.Z,
            Bool("ParachuteDirectly"), Int("VisibleItemsDroppedPerInterval"), Int("VisibleNumBones"), Reference("VisiblePayloadTemplateName"), Reference("VisiblePayloadWeaponTemplate"),
            Bool("SelfDestructObject"), Bool("FireWeapon"), Real("DiveStartDistance"), Real("DiveEndDistance"), strafes, Real("StrafeLength"));
        return new PayloadDelivery(Reference("Transport"), Bool("StartAtPreferredHeight", true), Bool("StartAtMaxSpeed"), Int("FormationSize", 1), Field("FormationSpacing") is null ? Fixed.FromInt(25) : Real("FormationSpacing"),
            Real("WeaponConvergenceFactor"), Real("WeaponErrorRadius"), Ticks("DelayDeliveryMax"), payload.ToImmutable(), Reference("PutInContainer"), run);
    }
}
