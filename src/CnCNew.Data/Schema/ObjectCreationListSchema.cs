using System.Collections.Immutable;
using CnCNew.Data.Definitions;

namespace CnCNew.Data.Schema;

/// <summary>Key schema of one <c>ObjectCreationList</c> nugget block plus the nested blocks it may contain.</summary>
public sealed record NuggetSchema(string Name, SchemaSet Fields, ImmutableArray<string> ChildBlocks);

/// <summary>
/// Nugget grammar of an <c>ObjectCreationList</c> (<c>ObjectCreationList.cpp TheObjectCreationListFieldParse</c>). Every nugget key of the
/// reference parse tables is typed; an unknown nugget or key is a compile error, never a silent default.
/// Angular-velocity keys (<c>SpinRate</c>, <c>YawRate</c>, ...) are degrees per second, angles are degrees; <c>ExtraFriction</c> is per second.
/// </summary>
public static class ObjectCreationListSchema
{
    private const string Source = "ObjectCreationList.cpp";

    public static SchemaSet RadiusDecal { get; } = new("ObjectCreationList/RadiusDecal",
    [
        new("Texture", FieldType.Text, Source: "RadiusDecal.cpp parseRadiusDecalTemplate"),
        new("Style", FieldType.Flags, Table: NameTables.Shadow, Source: "RadiusDecal.cpp parseRadiusDecalTemplate"),
        new("OpacityMin", FieldType.Percent, Source: "RadiusDecal.cpp parseRadiusDecalTemplate"),
        new("OpacityMax", FieldType.Percent, Source: "RadiusDecal.cpp parseRadiusDecalTemplate"),
        new("OpacityThrobTime", FieldType.DurationMs, Source: "RadiusDecal.cpp parseRadiusDecalTemplate"),
        new("Color", FieldType.ColorInt, Source: "RadiusDecal.cpp parseRadiusDecalTemplate"),
        new("OnlyVisibleToOwningPlayer", FieldType.Bool, Source: "RadiusDecal.cpp parseRadiusDecalTemplate"),
    ]);

    private static IEnumerable<FieldSchema> Common()
    {
        const string table = Source + " GenericObjectCreationNugget::getCommonFieldParse";
        yield return new("PutInContainer", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: table);
        yield return new("ParticleSystem", FieldType.Reference, ReferenceKind: DefinitionKind.Fx, Source: table);
        yield return new("Count", FieldType.Integer, Source: table);
        yield return new("IgnorePrimaryObstacle", FieldType.Bool, Source: table);
        yield return new("OrientInForceDirection", FieldType.Bool, Source: table);
        yield return new("ExtraBounciness", FieldType.Real, Source: table);
        yield return new("ExtraFriction", FieldType.Real, Source: table + " parseFrictionPerSec");
        yield return new("Offset", FieldType.Vector3, Source: table);
        yield return new("Disposition", FieldType.Flags, Table: NameTablesPhase5.DebrisDisposition, Source: table);
        yield return new("DispositionIntensity", FieldType.Real, Source: table);
        yield return new("SpinRate", FieldType.Angle, Source: table);
        yield return new("YawRate", FieldType.Angle, Source: table);
        yield return new("RollRate", FieldType.Angle, Source: table);
        yield return new("PitchRate", FieldType.Angle, Source: table);
        yield return new("MinForceMagnitude", FieldType.Real, Source: table);
        yield return new("MaxForceMagnitude", FieldType.Real, Source: table);
        yield return new("MinForcePitch", FieldType.Angle, Source: table);
        yield return new("MaxForcePitch", FieldType.Angle, Source: table);
        yield return new("MinLifetime", FieldType.DurationMs, Source: table);
        yield return new("MaxLifetime", FieldType.DurationMs, Source: table);
        yield return new("SpreadFormation", FieldType.Bool, Source: table);
        yield return new("MinDistanceAFormation", FieldType.Distance, Source: table);
        yield return new("MinDistanceBFormation", FieldType.Distance, Source: table);
        yield return new("MaxDistanceFormation", FieldType.Distance, Source: table);
        yield return new("FadeIn", FieldType.Bool, Source: table);
        yield return new("FadeOut", FieldType.Bool, Source: table);
        yield return new("FadeTime", FieldType.DurationMs, Source: table);
        yield return new("FadeSound", FieldType.Reference, ReferenceKind: DefinitionKind.AudioEvent, Source: table);
        yield return new("PreserveLayer", FieldType.Bool, Source: table);
        yield return new("DiesOnBadLand", FieldType.Bool, Source: table);
    }

    private static readonly ImmutableArray<string> DecalChild = ["DeliveryDecal"];

    public static NuggetSchema CreateObject { get; } = new("CreateObject", new("ObjectCreationList/CreateObject",
        Common().Concat(
        [
            new("ContainInsideSourceObject", FieldType.Bool, Source: Source + " parseObject"),
            new("ObjectNames", FieldType.RetailAlternatingNames, Repeat: RepeatPolicy.Accumulate, ReferenceKind: DefinitionKind.Object, Source: Source + " parseDebrisObjectNames"),
            new("ObjectCount", FieldType.Integer, Source: Source + " parseObject"),
            new("InheritsVeterancy", FieldType.Bool, Source: Source + " parseObject"),
            new("SkipIfSignificantlyAirborne", FieldType.Bool, Source: Source + " parseObject"),
            new("InvulnerableTime", FieldType.DurationMs, Source: Source + " parseObject"),
            new("MinHealth", FieldType.Percent, Source: Source + " parseObject"),
            new("MaxHealth", FieldType.Percent, Source: Source + " parseObject"),
            new("RequiresLivePlayer", FieldType.Bool, Source: Source + " parseObject"),
        ])), []);

    public static NuggetSchema CreateDebris { get; } = new("CreateDebris", new("ObjectCreationList/CreateDebris",
        Common().Concat(
        [
            new("ModelNames", FieldType.RetailAlternatingNames, Repeat: RepeatPolicy.Accumulate, Source: Source + " parseDebrisObjectNames"),
            new("Mass", FieldType.Real, Source: Source + " parseDebris"),
            new("AnimationSet", FieldType.TextList, Repeat: RepeatPolicy.Accumulate, Count: 3, Source: Source + " parseAnimSet"),
            new("FXFinal", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: Source + " parseDebris"),
            new("OkToChangeModelColor", FieldType.Bool, Source: Source + " parseDebris"),
            new("MinLODRequired", FieldType.Enum, Table: NameTablesPhase5.StaticGameLod, Source: Source + " parseDebris"),
            new("Shadow", FieldType.Flags, Table: NameTables.Shadow, Source: Source + " parseDebris"),
            new("BounceSound", FieldType.Reference, ReferenceKind: DefinitionKind.AudioEvent, Source: Source + " parseDebris"),
        ])), []);

    public static NuggetSchema ApplyRandomForce { get; } = new("ApplyRandomForce", new("ObjectCreationList/ApplyRandomForce",
    [
        new("SpinRate", FieldType.Angle, Source: Source + " ApplyRandomForceNugget::parse"),
        new("MinForceMagnitude", FieldType.Real, Source: Source + " ApplyRandomForceNugget::parse"),
        new("MaxForceMagnitude", FieldType.Real, Source: Source + " ApplyRandomForceNugget::parse"),
        new("MinForcePitch", FieldType.Angle, Source: Source + " ApplyRandomForceNugget::parse"),
        new("MaxForcePitch", FieldType.Angle, Source: Source + " ApplyRandomForceNugget::parse"),
    ]), []);

    public static NuggetSchema FireWeapon { get; } = new("FireWeapon", new("ObjectCreationList/FireWeapon",
    [
        new("Weapon", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: Source + " FireWeaponNugget::parse"),
    ]), []);

    public static NuggetSchema Attack { get; } = new("Attack", new("ObjectCreationList/Attack",
    [
        new("NumberOfShots", FieldType.Integer, Source: Source + " AttackNugget::parse"),
        new("WeaponSlot", FieldType.Enum, Table: NameTablesPhase5.WeaponSlot, Source: Source + " AttackNugget::parse"),
        new("DeliveryDecalRadius", FieldType.Distance, Source: Source + " AttackNugget::parse"),
    ]), DecalChild);

    public static NuggetSchema DeliverPayload { get; } = new("DeliverPayload", new("ObjectCreationList/DeliverPayload",
    [
        new("Transport", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: Source + " DeliverPayloadNugget::parse"),
        new("StartAtPreferredHeight", FieldType.Bool, Source: Source + " DeliverPayloadNugget::parse"),
        new("StartAtMaxSpeed", FieldType.Bool, Source: Source + " DeliverPayloadNugget::parse"),
        new("FormationSize", FieldType.Integer, Min: 0, Source: Source + " DeliverPayloadNugget::parse"),
        new("FormationSpacing", FieldType.Distance, Source: Source + " DeliverPayloadNugget::parse"),
        new("WeaponConvergenceFactor", FieldType.Real, Source: Source + " DeliverPayloadNugget::parse"),
        new("WeaponErrorRadius", FieldType.Distance, Source: Source + " DeliverPayloadNugget::parse"),
        new("DelayDeliveryMax", FieldType.DurationMs, Source: Source + " DeliverPayloadNugget::parse"),
        new("Payload", FieldType.CountedReference, Repeat: RepeatPolicy.Accumulate, ReferenceKind: DefinitionKind.Object, Source: Source + " parsePayload"),
        new("PutInContainer", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: Source + " DeliverPayloadNugget::parse"),
        // DeliverPayloadData::getFieldParse (DeliverPayloadAIUpdate.cpp)
        new("DeliveryDistance", FieldType.Distance, Source: DeliverPayloadData),
        new("PreOpenDistance", FieldType.Distance, Source: DeliverPayloadData),
        new("MaxAttempts", FieldType.Integer, Source: DeliverPayloadData),
        new("DropDelay", FieldType.DurationMs, Source: DeliverPayloadData),
        new("DropOffset", FieldType.Vector3, Source: DeliverPayloadData),
        new("DropVariance", FieldType.Vector3, Source: DeliverPayloadData),
        new("InheritTransportVelocity", FieldType.Bool, Source: DeliverPayloadData),
        new("ExitPitchRate", FieldType.Angle, Source: DeliverPayloadData),
        new("ParachuteDirectly", FieldType.Bool, Source: DeliverPayloadData),
        new("VisibleItemsDroppedPerInterval", FieldType.Integer, Source: DeliverPayloadData),
        new("VisibleDropBoneBaseName", FieldType.Text, Source: DeliverPayloadData),
        new("VisibleSubObjectBaseName", FieldType.Text, Source: DeliverPayloadData),
        new("VisibleNumBones", FieldType.Integer, Source: DeliverPayloadData),
        new("VisiblePayloadTemplateName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: DeliverPayloadData),
        new("VisiblePayloadWeaponTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: DeliverPayloadData),
        new("SelfDestructObject", FieldType.Bool, Source: DeliverPayloadData),
        new("FireWeapon", FieldType.Bool, Source: DeliverPayloadData),
        new("DiveStartDistance", FieldType.Distance, Source: DeliverPayloadData),
        new("DiveEndDistance", FieldType.Distance, Source: DeliverPayloadData),
        new("StrafingWeaponSlot", FieldType.Enum, Table: NameTablesPhase5.WeaponSlot, Source: DeliverPayloadData),
        new("StrafeWeaponFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: DeliverPayloadData),
        new("StrafeLength", FieldType.Distance, Source: DeliverPayloadData),
        new("DeliveryDecalRadius", FieldType.Distance, Source: DeliverPayloadData),
    ]), DecalChild);

    private const string DeliverPayloadData = "DeliverPayloadAIUpdate.cpp DeliverPayloadData::getFieldParse";

    private static readonly ImmutableDictionary<string, NuggetSchema> ByName = new[]
    {
        CreateObject, CreateDebris, ApplyRandomForce, DeliverPayload, FireWeapon, Attack,
    }.ToImmutableDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetNugget(string name, out NuggetSchema schema) => ByName.TryGetValue(name, out schema!);
}
