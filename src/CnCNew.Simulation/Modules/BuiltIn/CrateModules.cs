using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The <c>CrateCollide</c> family (<c>CrateCollide.cpp</c>) and the units that use it: the money and veterancy crates, the terrorist's car bomb conversion, the
/// hijacker (<c>ConvertToHijackedVehicleCrateCollide</c> + <c>HijackerUpdate</c>), the five building saboteurs and the pilot that looks for a vehicle to enter
/// (<c>PilotFindVehicleUpdate</c>). The reference fires them from the physics collision; here <see cref="ICollideModule"/> is driven by the footprint collision
/// of the world or, for an order that names a goal object, by <c>AIUpdateInterface.OrderApproachGoal</c> (see <c>docs/SIMULATION-CORE.md</c>).
/// </summary>
internal static class CrateModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<MoneyCrateData>("MoneyCrateCollide", ModuleOwnerKind.Object, MoneyCrateData.Parse, data => new MoneyCrateCollide(data)),
        new ModuleBinding<VeterancyCrateData>("VeterancyCrateCollide", ModuleOwnerKind.Object, (configuration, _) => VeterancyCrateData.Parse(configuration), data => new VeterancyCrateCollide(data)),
        new ModuleBinding<ConvertToCarBombData>("ConvertToCarBombCrateCollide", ModuleOwnerKind.Object, (configuration, _) => ConvertToCarBombData.Parse(configuration), data => new ConvertToCarBombCrateCollide(data)),
        new ModuleBinding<CrateCollideData>("ConvertToHijackedVehicleCrateCollide", ModuleOwnerKind.Object, (configuration, _) => CrateCollideData.Parse(configuration), data => new ConvertToHijackedVehicleCrateCollide(data)),
        new ModuleBinding<HijackerUpdateData>("HijackerUpdate", ModuleOwnerKind.Object, (configuration, _) => HijackerUpdateData.Parse(configuration), data => new HijackerUpdate(data)),
        new ModuleBinding<CrateCollideData>("SabotageCommandCenterCrateCollide", ModuleOwnerKind.Object, (configuration, _) => CrateCollideData.Parse(configuration), data => new SabotageCommandCenterCrateCollide(data)),
        new ModuleBinding<CrateCollideData>("SabotageSuperweaponCrateCollide", ModuleOwnerKind.Object, (configuration, _) => CrateCollideData.Parse(configuration), data => new SabotageSuperweaponCrateCollide(data)),
        new ModuleBinding<SabotageDurationData>("SabotageMilitaryFactoryCrateCollide", ModuleOwnerKind.Object, (configuration, _) => SabotageDurationData.Parse(configuration, "SabotageDuration"), data => new SabotageMilitaryFactoryCrateCollide(data)),
        new ModuleBinding<SabotageDurationData>("SabotagePowerPlantCrateCollide", ModuleOwnerKind.Object, (configuration, _) => SabotageDurationData.Parse(configuration, "SabotagePowerDuration"), data => new SabotagePowerPlantCrateCollide(data)),
        new ModuleBinding<SabotageSupplyCenterData>("SabotageSupplyCenterCrateCollide", ModuleOwnerKind.Object, (configuration, _) => SabotageSupplyCenterData.Parse(configuration), data => new SabotageSupplyCenterCrateCollide(data)),
        new ModuleBinding<PilotFindVehicleData>("PilotFindVehicleUpdate", ModuleOwnerKind.Object, (configuration, _) => PilotFindVehicleData.Parse(configuration), data => new PilotFindVehicleUpdate(data)),
    ];

    /// <summary>The goal-object check every purposeful crate performs (<c>ai->getGoalObject() != other</c>): a unit without AI (a crate) never objects.</summary>
    public static bool GoalAllows(Entity self, Entity other)
        => AiOf(self) is not { } ai || ai.GoalObject == other.Id;

    public static AIUpdateInterface? AiOf(Entity entity)
    {
        foreach (var module in entity.ModuleArray)
            if (module is AIUpdateInterface ai) return ai;
        return null;
    }

    /// <summary>Whether the object moves by an airborne locomotor (<c>Object::isUsingAirborneLocomotor</c>), judged by its normal locomotor set.</summary>
    public static bool UsesAirborneLocomotor(Entity entity) => entity.Template.Locomotor is { } locomotor && (locomotor.Surfaces & LocomotorSurfaces.Air) != 0;

    /// <summary><c>UpgradeType:&lt;name&gt; Boost:&lt;int&gt;</c> (<c>INI::parseUpgradePair</c>), every occurrence in file order; an unknown upgrade name is an error, never a silent zero.</summary>
    public static ImmutableArray<(string Upgrade, int Boost)> UpgradePairs(RatedModuleConfiguration configuration, string key, UpgradeCatalog upgrades)
    {
        var pairs = ImmutableArray.CreateBuilder<(string, int)>();
        foreach (var field in ModuleConfigReader.All(configuration, key))
        {
            string? upgrade = null;
            int? boost = null;
            foreach (var token in field.Value.OriginalText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = token.IndexOf(':');
                if (colon <= 0) throw new InvalidDataException($"{configuration.Name} {key} token '{token}' is not 'Key:Value'.");
                var name = token[..colon];
                var value = token[(colon + 1)..];
                if (name.Equals("UpgradeType", StringComparison.OrdinalIgnoreCase)) upgrade = value;
                else if (name.Equals("Boost", StringComparison.OrdinalIgnoreCase)) boost = int.Parse(value, CultureInfo.InvariantCulture);
                else throw new InvalidDataException($"{configuration.Name} {key} has the unknown key '{name}'.");
            }
            if (upgrade is null || boost is null) throw new InvalidDataException($"{configuration.Name} {key} '{field.Value.OriginalText}' needs UpgradeType and Boost.");
            if (!upgrades.TryGet(upgrade, out _)) throw new InvalidDataException($"{configuration.Name} {key} names the unknown upgrade '{upgrade}'.");
            pairs.Add((upgrade, boost.Value));
        }
        return pairs.ToImmutable();
    }

    /// <summary>
    /// <c>Object::defect</c> for a change of controlling player (<c>ConvertToCarBombCrateCollide</c>): a contained, under-construction or sold object cannot defect, a
    /// producer's queue is cancelled and refunded to the old owner, the object changes hands and its AI goes idle. The defector flash and voice are presentation; the
    /// kick-out of a cave's passengers, parked aircraft and owned mines are not modelled.
    /// </summary>
    public static bool Defect(IWorldServices world, Entity entity, PlayerId newOwner)
    {
        if (entity.ContainedBy.IsValid || entity.Owner == newOwner) return false;
        if (entity.HasStatus(ObjectStatusBits.UnderConstruction) || entity.HasStatus(ObjectStatusBits.Sold)) return false;
        var oldOwner = entity.Owner;
        foreach (var module in entity.ModuleArray)
            if (module is ProductionUpdate production)
            {
                production.CancelAll(out var refund);
                if (refund > Fixed.Zero) world.AddMoney(oldOwner, refund);
                world.MarkDirty(entity);
            }
        if (!world.TransferOwnership(entity.Id, newOwner)) return false;
        AiOf(entity)?.OrderIdle(world);
        return true;
    }
}

// ============================================================================================================ CrateCollide base

/// <summary>
/// <c>CrateCollideModuleData</c>: the kind-of filter, the owner/human/science restrictions and the FX every <c>*CrateCollide</c> shares. <c>ExecuteAnimation*</c> is a
/// screen-space 2D animation (presentation) and is typed but not kept.
/// </summary>
public sealed record CrateCollideData(
    ImmutableArray<string> RequiredKindOf, ImmutableArray<string> ForbiddenKindOf, bool ForbidOwnerPlayer, bool BuildingPickup, bool HumanOnly, string PickupScience, string ExecuteFx) : ModuleData
{
    public static CrateCollideData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Flags(configuration, "RequiredKindOf")?.Names ?? [],
        ModuleConfigReader.Flags(configuration, "ForbiddenKindOf")?.Names ?? [],
        ModuleConfigReader.Bool(configuration, "ForbidOwnerPlayer", false),
        ModuleConfigReader.Bool(configuration, "BuildingPickup", false),
        ModuleConfigReader.Bool(configuration, "HumanOnly", false),
        ModuleConfigReader.Text(configuration, "PickupScience"),
        ModuleConfigReader.Text(configuration, "ExecuteFX"));
}

/// <summary>
/// <c>CrateCollide::onCollide</c>/<c>isValidToExecute</c>: when <see cref="IsValidToExecute"/> accepts the toucher and <see cref="ExecuteCrateBehavior"/> reports success,
/// the module's object is destroyed and its <c>ExecuteFX</c> plays at the toucher. The base validity: not neutral, a unit (has AI; a building only with
/// <c>BuildingPickup</c>), matching kind-of, alive, the crate itself on the ground (buildings may take airdrops), the owner/human/science restrictions, no parachute.
/// </summary>
public abstract class CrateCollideBase(CrateCollideData crate) : SimModule, ICollideModule
{
    protected CrateCollideData Crate => crate;

    public void OnCollide(IWorldServices world, Entity other)
    {
        if (!IsValidToExecute(world, other) || !ExecuteCrateBehavior(world, other)) return;
        if (crate.ExecuteFx.Length > 0) world.Emit(SimEventKind.FxRequested, other.Id, Entity.Id, crate.ExecuteFx, 0);
        world.RequestDestroy(Entity.Id);
    }

    public bool WouldLikeToCollideWith(IWorldServices world, Entity other) => IsValidToExecute(world, other);

    protected virtual bool IsValidToExecute(IWorldServices world, Entity other)
    {
        if (other.Owner.IsSystem) return false;
        var validBuildingAttempt = crate.BuildingPickup && other.Template.IsKindOf("STRUCTURE");
        if (!validBuildingAttempt && CrateModules.AiOf(other) is null) return false;
        if (!crate.RequiredKindOf.All(other.Template.IsKindOf) || crate.ForbiddenKindOf.Any(other.Template.IsKindOf)) return false;
        if (other.EffectivelyDead) return false;
        if (!validBuildingAttempt && Entity.Z > world.GroundHeightAt(Entity.X, Entity.Y)) return false;
        if (crate.ForbidOwnerPlayer && Entity.Owner == other.Owner) return false;
        if (crate.HumanOnly && world.IsComputerPlayer(other.Owner)) return false;
        if (crate.PickupScience.Length > 0 && !(world.Sciences.TryGet(crate.PickupScience, out var science) && world.PlayerSciences(other.Owner).Has(science.Index))) return false;
        return !other.Template.IsKindOf("PARACHUTE");
    }

    /// <summary>The module's effect on <paramref name="other"/>; false leaves the module's object alone (<c>executeCrateBehavior</c> returning FALSE).</summary>
    protected abstract bool ExecuteCrateBehavior(IWorldServices world, Entity other);

    protected void Collected(IWorldServices world, Entity other, long value) => world.Emit(SimEventKind.CrateCollected, Entity.Id, other.Id, ModuleName, value);

    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

// ============================================================================================================ MoneyCrateCollide

/// <summary><c>MoneyCrateCollideModuleData</c>: <c>MoneyProvided</c> (default 0) and the repeatable <c>UpgradedBoost</c> pairs.</summary>
public sealed record MoneyCrateData(CrateCollideData Crate, int MoneyProvided, ImmutableArray<(string Upgrade, int Boost)> UpgradedBoosts) : ModuleData
{
    public static MoneyCrateData Parse(RatedModuleConfiguration configuration, UpgradeCatalog upgrades)
        => new(CrateCollideData.Parse(configuration), checked((int)ModuleConfigReader.Int(configuration, "MoneyProvided", 0)), CrateModules.UpgradePairs(configuration, "UpgradedBoost", upgrades));
}

/// <summary>
/// <c>MoneyCrateCollide::executeCrateBehavior</c>: the toucher's player receives <c>MoneyProvided</c> plus the boost of the first <c>UpgradedBoost</c> upgrade it owns.
/// The score keeper's "money earned" and the pickup sound are not modelled.
/// </summary>
public sealed class MoneyCrateCollide(MoneyCrateData data) : CrateCollideBase(data.Crate)
{
    public override string ModuleName => "MoneyCrateCollide";

    protected override bool ExecuteCrateBehavior(IWorldServices world, Entity other)
    {
        var money = data.MoneyProvided;
        var owned = world.PlayerUpgrades(other.Owner);
        foreach (var (upgrade, boost) in data.UpgradedBoosts)
            if (world.Upgrades.TryGet(upgrade, out var info) && owned.Has(info.Index)) { money += boost; break; }
        world.AddMoney(other.Owner, Fixed.FromInt(money));
        Collected(world, other, money);
        return true;
    }
}

// ============================================================================================================ VeterancyCrateCollide

/// <summary><c>VeterancyCrateCollideModuleData</c>: <c>EffectRange</c> (0 = only the toucher), <c>AddsOwnerVeterancy</c> and <c>IsPilot</c>.</summary>
public sealed record VeterancyCrateData(CrateCollideData Crate, int EffectRange, bool AddsOwnerVeterancy, bool IsPilot) : ModuleData
{
    public static VeterancyCrateData Parse(RatedModuleConfiguration configuration) => new(CrateCollideData.Parse(configuration),
        checked((int)ModuleConfigReader.Int(configuration, "EffectRange", 0)), ModuleConfigReader.Bool(configuration, "AddsOwnerVeterancy", false), ModuleConfigReader.Bool(configuration, "IsPilot", false));
}

/// <summary>
/// <c>VeterancyCrateCollide</c>: promotes the toucher (or, with <c>EffectRange</c>, every object of the toucher's player within that range of it) by one level, or by the
/// level of the object carrying the module (<c>AddsOwnerVeterancy</c>, the ejected pilot's gift to a vehicle it climbs into). Transcribed as the reference has it, including
/// that <c>executeCrateBehavior</c> returns at once for an object without AI and for a goal other than the toucher, so a plain level-up crate placed on the map is never
/// consumed (it has no <c>AIUpdateInterface</c>); only a unit with an approach order, like the pilot, can use it. A pilot also requires the vehicle to belong to its player and
/// to be a ground vehicle, and hands its script name to it (<see cref="SimEventKind.ObjectNameTransferred"/>).
/// </summary>
public sealed class VeterancyCrateCollide(VeterancyCrateData data) : CrateCollideBase(data.Crate)
{
    public override string ModuleName => "VeterancyCrateCollide";

    private int LevelsToGain => data.AddsOwnerVeterancy ? Entity.VeterancyLevel : 1;

    protected override bool IsValidToExecute(IWorldServices world, Entity other)
    {
        if (!base.IsValidToExecute(world, other) || other.EffectivelyDead || world.IsSignificantlyAboveTerrain(other)) return false;
        var levels = LevelsToGain;
        if (levels <= 0 || !other.Template.IsTrainable) return false;
        if (Math.Min(other.VeterancyLevel + levels, 3) <= other.VeterancyLevel) return false; // canGainExpForLevel
        if (!data.IsPilot) return true;
        return other.Owner == Entity.Owner && !CrateModules.UsesAirborneLocomotor(other);
    }

    protected override bool ExecuteCrateBehavior(IWorldServices world, Entity other)
    {
        if (CrateModules.AiOf(Entity) is not { } ai || ai.GoalObject != other.Id) return false;
        var levels = LevelsToGain;
        if (data.EffectRange == 0) world.GainExperienceForLevels(other, levels, !data.IsPilot);
        else
        {
            var range = Fixed.FromInt(data.EffectRange);
            foreach (var candidate in world.Entities)
            {
                if (candidate.Owner != other.Owner || candidate.EffectivelyDead || candidate.DestroyRequested) continue;
                var dx = candidate.X - other.X;
                var dy = candidate.Y - other.Y;
                if (SquaredReach.Of(range).Contains(dx * dx + dy * dy)) world.GainExperienceForLevels(candidate, levels, !data.IsPilot);
            }
        }
        if (data.IsPilot) world.Emit(SimEventKind.ObjectNameTransferred, Entity.Id, other.Id, string.Empty, 0);
        Collected(world, other, levels);
        return true;
    }
}

// ============================================================================================================ ConvertToCarBombCrateCollide

/// <summary><c>ConvertToCarBombCrateCollideModuleData</c>: the FX played at the converted vehicle (<c>FXList</c>).</summary>
public sealed record ConvertToCarBombData(CrateCollideData Crate, string Fx) : ModuleData
{
    public static ConvertToCarBombData Parse(RatedModuleConfiguration configuration) => new(CrateCollideData.Parse(configuration), ModuleConfigReader.Text(configuration, "FXList"));
}

/// <summary>
/// <c>ConvertToCarBombCrateCollide</c> (the terrorist): converts the vehicle it was sent to into a suicide car bomb. Valid for a non-aircraft, non-boat vehicle that
/// has a <c>WeaponSet Conditions = CARBOMB</c> and is not a car bomb yet. On execution the vehicle takes the <c>CARBOMB</c> weapon set (the world selects it from the
/// <c>IS_CARBOMB</c> status), defects to the terrorist's player (the terrorist is spent), inherits its veterancy level and its script name
/// (<see cref="SimEventKind.ObjectNameTransferred"/>); a booby-trapped vehicle detonates its trap first and the conversion is abandoned if either dies. Not modelled:
/// the vehicle taking over the terrorist's vision/shroud range (vision comes from the template) and the radar blip refresh.
/// </summary>
public sealed class ConvertToCarBombCrateCollide(ConvertToCarBombData data) : CrateCollideBase(data.Crate)
{
    private static readonly int CarBombBit = ObjectStatusBits.Index("IS_CARBOMB");
    private static readonly uint CarBombSetMask = 1u << (NameTablesPhase5.WeaponSetCondition.TryIndexOf("CARBOMB", out var carBombBit) ? carBombBit : throw new InvalidOperationException("CARBOMB is not a weapon set condition."));

    /// <summary>The vehicle declares a <c>WeaponSet</c> that names <c>CARBOMB</c> and holds a weapon (<c>ThingTemplate::findWeaponTemplateSet</c> picks it once the flag is raised).</summary>
    private static bool HasCarBombWeaponSet(ObjectTemplate template) => template.WeaponSets.Any(set => (set.Conditions & CarBombSetMask) != 0 && set.WeaponCount > 0);

    public override string ModuleName => "ConvertToCarBombCrateCollide";

    protected override bool IsValidToExecute(IWorldServices world, Entity other)
        => base.IsValidToExecute(world, other) && !other.EffectivelyDead && !other.Template.IsKindOf("AIRCRAFT") && !other.Template.IsKindOf("BOAT")
           && !other.HasStatus(CarBombBit) && HasCarBombWeaponSet(other.Template);

    protected override bool ExecuteCrateBehavior(IWorldServices world, Entity other)
    {
        if (!CrateModules.GoalAllows(Entity, other)) return false;
        if (StickyBombUpdate.CheckAndDetonateBoobyTrap(world, other, Entity) && (other.EffectivelyDead || Entity.EffectivelyDead)) return false;
        other.Status |= 1UL << CarBombBit;
        world.WeaponSetFlagsChanged(other);
        if (data.Fx.Length > 0) world.Emit(SimEventKind.FxRequested, other.Id, Entity.Id, data.Fx, 0);
        CrateModules.Defect(world, other, Entity.Owner);
        world.Emit(SimEventKind.ObjectNameTransferred, Entity.Id, other.Id, string.Empty, 0);
        world.SetVeterancy(other, Entity.VeterancyLevel);
        world.MarkDirty(other);
        world.Emit(SimEventKind.ObjectConverted, other.Id, Entity.Id, "CARBOMB", 0);
        Collected(world, other, 0);
        return true;
    }
}

// ============================================================================================================ ConvertToHijackedVehicleCrateCollide / HijackerUpdate

/// <summary><c>HijackerUpdateModuleData</c>: <c>ParachuteName</c> (the container the hijacker falls out of an airborne wreck in); <c>AttachToTargetBone</c> is a model detail.</summary>
public sealed record HijackerUpdateData(string ParachuteName) : ModuleData
{
    public static HijackerUpdateData Parse(RatedModuleConfiguration configuration) => new(ModuleConfigReader.Text(configuration, "ParachuteName"));
}

/// <summary>
/// <c>ConvertToHijackedVehicleCrateCollide</c> (the GLA hijacker): takes over an <em>enemy</em> ground vehicle it was sent to — not an immune-to-capture one (battle bus), an
/// aircraft, boat or drone, nor a vehicle already hijacked, nor a transport that carries somebody. The vehicle changes hands and is marked <c>HIJACKED</c>, stops whatever it
/// was doing (a dozer's tasks included), and both share the higher veterancy level. A vehicle without an <c>EjectPilotDie</c> simply swallows the hijacker; otherwise the
/// hijacker hides in it (<c>NO_COLLISIONS</c>, <c>MASKED</c>, <c>UNSELECTABLE</c>) and rides along until it is gone (<see cref="HijackerUpdate"/>). The "vehicle stolen" EVA line
/// and the driver's death are presentation/not modelled; the hijacker's script name moves to the vehicle (<see cref="SimEventKind.ObjectNameTransferred"/>).
/// </summary>
public sealed class ConvertToHijackedVehicleCrateCollide(CrateCollideData crate) : CrateCollideBase(crate)
{
    private static readonly int HijackedBit = ObjectStatusBits.Index("HIJACKED");
    private static readonly ulong HiddenMask = (1UL << ObjectStatusBits.Index("NO_COLLISIONS")) | (1UL << ObjectStatusBits.Index("MASKED")) | (1UL << ObjectStatusBits.Index("UNSELECTABLE"));

    public override string ModuleName => "ConvertToHijackedVehicleCrateCollide";

    protected override bool IsValidToExecute(IWorldServices world, Entity other)
    {
        if (!base.IsValidToExecute(world, other) || other.EffectivelyDead) return false;
        var kind = other.Template;
        if (kind.IsKindOf("IMMUNE_TO_CAPTURE") || kind.IsKindOf("AIRCRAFT") || kind.IsKindOf("BOAT") || kind.IsKindOf("DRONE")) return false;
        if (other.HasStatus(HijackedBit)) return false;
        if (world.RelationshipOf(Entity.Owner, other.Owner) != Relationship.Enemies) return false;
        return !(kind.IsKindOf("TRANSPORT") && other.Container is { ContainCount: > 0 });
    }

    protected override bool ExecuteCrateBehavior(IWorldServices world, Entity other)
    {
        if (!CrateModules.GoalAllows(Entity, other)) return false;
        world.TransferOwnership(other.Id, Entity.Owner);
        other.Status |= 1UL << HijackedBit;
        CrateModules.AiOf(other)?.OrderIdle(world);
        world.Emit(SimEventKind.ObjectNameTransferred, Entity.Id, other.Id, string.Empty, 0);
        var highest = Math.Max(other.VeterancyLevel, Entity.VeterancyLevel);
        world.SetMinVeterancyLevel(Entity, highest);
        world.SetMinVeterancyLevel(other, highest);
        world.Emit(SimEventKind.ObjectConverted, other.Id, Entity.Id, "HIJACKED", 0);
        world.MarkDirty(other);
        Collected(world, other, 0);

        var canEject = other.Modules.Any(module => module is EjectPilotDie);
        if (!canEject)
        {
            world.RequestDestroy(Entity.Id);
            return false; // the object is already gone; CrateCollide must not run the FX/destroy a second time
        }
        var rider = Entity.Modules.OfType<HijackerUpdate>().FirstOrDefault();
        if (rider is not null)
        {
            rider.StartRiding(world, other);
            Entity.Status |= HiddenMask;
        }
        CrateModules.AiOf(Entity)?.OrderIdle(world);
        world.MarkDirty(Entity);
        return false; // "By returning FALSE, we will not remove the object (Hijacker)"
    }
}

/// <summary>
/// <c>HijackerUpdate</c>: while the hijacker is inside its vehicle it follows it every tick (keeping the higher veterancy of the two, and remembering whether the vehicle
/// was airborne and where). When the vehicle is gone the hijacker reappears there — visible and collidable again, idle — and, if the vehicle was airborne, drops from a
/// <c>ParachuteName</c> container. The reference checks for a missing vehicle with <c>findObjectByID</c>, so a destroyed wreck ends the ride, a dead hulk that still exists
/// does not; this module does the same with <c>FindEntity</c>.
/// </summary>
public sealed class HijackerUpdate(HijackerUpdateData data) : SimModule, IUpdateModule
{
    private static readonly ulong HiddenMask = (1UL << ObjectStatusBits.Index("NO_COLLISIONS")) | (1UL << ObjectStatusBits.Index("MASKED")) | (1UL << ObjectStatusBits.Index("UNSELECTABLE"));

    private long _nextWake = SimulationTiming.SleepForever;
    private EntityId _target;
    private bool _wasAirborne;
    private Fixed _ejectX;
    private Fixed _ejectY;
    private Fixed _ejectZ;
    private EntityId _chute;

    public override string ModuleName => "HijackerUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary>True while the hijacker rides in a vehicle.</summary>
    public bool InVehicle => _target.IsValid;

    /// <summary><c>HijackerUpdate::setTargetObject</c> + <c>setIsInVehicle(TRUE)</c> + <c>setUpdate(TRUE)</c>.</summary>
    public void StartRiding(IWorldServices world, Entity vehicle)
    {
        _target = vehicle.Id;
        _wasAirborne = false;
        _ejectX = vehicle.X;
        _ejectY = vehicle.Y;
        _ejectZ = vehicle.Z;
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
        world.MarkDirty(Entity);
    }

    public long Update(IWorldServices world)
    {
        if (_chute.IsValid)
        {
            if (world.FindEntity(_chute) is { } chute && !chute.DestroyRequested) world.TryEnterContainer(Entity, chute);
            _chute = EntityId.Invalid;
            return SimulationTiming.SleepForever;
        }
        if (!_target.IsValid) return SimulationTiming.SleepForever;
        var target = world.FindEntity(_target);
        if (target is not null && !target.DestroyRequested)
        {
            Entity.X = target.X;
            Entity.Y = target.Y;
            Entity.Z = target.Z;
            _wasAirborne = world.IsSignificantlyAboveTerrain(target);
            _ejectX = target.X;
            _ejectY = target.Y;
            _ejectZ = target.Z;
            var highest = Math.Max(target.VeterancyLevel, Entity.VeterancyLevel);
            world.SetMinVeterancyLevel(Entity, highest);
            world.SetMinVeterancyLevel(target, highest);
            return SimulationTiming.SleepNone;
        }
        Eject(world);
        return _chute.IsValid ? SimulationTiming.SleepNone : SimulationTiming.SleepForever;
    }

    private void Eject(IWorldServices world)
    {
        Entity.Status &= ~HiddenMask;
        Entity.X = _ejectX;
        Entity.Y = _ejectY;
        Entity.Z = _ejectZ;
        CrateModules.AiOf(Entity)?.OrderIdle(world);
        if (_wasAirborne && data.ParachuteName.Length > 0 && world.Templates.TryGet(data.ParachuteName, out var parachute) && parachute.IsInstantiable)
            _chute = world.RequestSpawn(parachute.Index, Entity.Owner, _ejectX, _ejectY, _ejectZ, Entity.Angle);
        _target = EntityId.Invalid;
        _wasAirborne = false;
        world.MarkDirty(Entity);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        if (!_target.IsValid && !_chute.IsValid) return;
        writer.UInt("target", _target.Value);
        writer.Bool("wasAirborne", _wasAirborne);
        writer.Fixed("ejectX", _ejectX);
        writer.Fixed("ejectY", _ejectY);
        writer.Fixed("ejectZ", _ejectZ);
        writer.UInt("chute", _chute.Value);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        if (!reader.Has("target")) return;
        _target = new EntityId(checked((uint)reader.UInt("target")));
        _wasAirborne = reader.Bool("wasAirborne");
        _ejectX = reader.Fixed("ejectX");
        _ejectY = reader.Fixed("ejectY");
        _ejectZ = reader.Fixed("ejectZ");
        _chute = new EntityId(checked((uint)reader.UInt("chute")));
    }
}

// ============================================================================================================ Sabotage*CrateCollide

/// <summary>A sabotage module's duration in ticks (<c>SabotageDuration</c> of the military factory, <c>SabotagePowerDuration</c> of the power plant; default 0 like the reference).</summary>
public sealed record SabotageDurationData(CrateCollideData Crate, long DurationTicks) : ModuleData
{
    public static SabotageDurationData Parse(RatedModuleConfiguration configuration, string key) => new(CrateCollideData.Parse(configuration), ModuleConfigReader.Ticks(configuration, key, 0));
}

/// <summary><c>SabotageSupplyCenterCrateCollideModuleData</c>: <c>StealCashAmount</c> (default 0).</summary>
public sealed record SabotageSupplyCenterData(CrateCollideData Crate, int StealCashAmount) : ModuleData
{
    public static SabotageSupplyCenterData Parse(RatedModuleConfiguration configuration) => new(CrateCollideData.Parse(configuration), checked((int)ModuleConfigReader.Int(configuration, "StealCashAmount", 0)));
}

/// <summary>
/// The common shape of the <c>Sabotage*CrateCollide</c> modules (a spy or hacker walking into a building): the building must be alive, an enemy's and of the module's
/// kind, and it must be the unit's goal object. The radar infiltration blip, EVA line, sound and flash are presentation.
/// </summary>
public abstract class SabotageCollideBase(CrateCollideData crate) : CrateCollideBase(crate)
{
    protected abstract bool IsTargetKind(Entity building);

    protected override bool IsValidToExecute(IWorldServices world, Entity other)
        => base.IsValidToExecute(world, other) && !other.EffectivelyDead && IsTargetKind(other) && world.RelationshipOf(Entity.Owner, other.Owner) == Relationship.Enemies;

    protected override bool ExecuteCrateBehavior(IWorldServices world, Entity other)
    {
        if (!CrateModules.GoalAllows(Entity, other)) return false;
        var value = Sabotage(world, other);
        world.Emit(SimEventKind.Sabotaged, other.Id, Entity.Id, ModuleName, value);
        Collected(world, other, value);
        return true;
    }

    /// <summary>The sabotage itself; returns the stolen cash or the disabling ticks for the event (0 when neither applies).</summary>
    protected abstract long Sabotage(IWorldServices world, Entity building);

    /// <summary><c>SpecialPowerModuleInterface::startPowerRecharge</c> on every power module of the building.</summary>
    protected static void RestartEveryPower(IWorldServices world, Entity building)
    {
        foreach (var module in building.Modules)
            if (module is ISpecialPowerModule power) power.Trigger(world);
        world.MarkDirty(building);
    }
}

/// <summary><c>SabotageCommandCenterCrateCollide</c>: every special power of an enemy command center starts its recharge from zero.</summary>
public sealed class SabotageCommandCenterCrateCollide(CrateCollideData crate) : SabotageCollideBase(crate)
{
    public override string ModuleName => "SabotageCommandCenterCrateCollide";
    protected override bool IsTargetKind(Entity building) => building.Template.IsKindOf("COMMANDCENTER");

    protected override long Sabotage(IWorldServices world, Entity building)
    {
        RestartEveryPower(world, building);
        return 0;
    }
}

/// <summary><c>SabotageSuperweaponCrateCollide</c>: every special power of an enemy superweapon or strategy center starts its recharge from zero.</summary>
public sealed class SabotageSuperweaponCrateCollide(CrateCollideData crate) : SabotageCollideBase(crate)
{
    public override string ModuleName => "SabotageSuperweaponCrateCollide";
    protected override bool IsTargetKind(Entity building) => building.Template.IsKindOf("FS_SUPERWEAPON") || building.Template.IsKindOf("FS_STRATEGY_CENTER");

    protected override long Sabotage(IWorldServices world, Entity building)
    {
        RestartEveryPower(world, building);
        return 0;
    }
}

/// <summary>
/// <c>SabotageMilitaryFactoryCrateCollide</c>: an enemy barracks, war factory or airfield (not an aircraft carrier) is disabled (<c>DISABLED_HACKED</c>) for
/// <c>SabotageDuration</c>. The disabled state has one simulated effect: the factory's production queue stands still (<see cref="ProductionUpdate.DisableUntil"/>).
/// </summary>
public sealed class SabotageMilitaryFactoryCrateCollide(SabotageDurationData data) : SabotageCollideBase(data.Crate)
{
    public override string ModuleName => "SabotageMilitaryFactoryCrateCollide";

    protected override bool IsTargetKind(Entity building)
        => !building.Template.IsKindOf("AIRCRAFT_CARRIER")
           && (building.Template.IsKindOf("FS_BARRACKS") || building.Template.IsKindOf("FS_WARFACTORY") || building.Template.IsKindOf("FS_AIRFIELD"));

    protected override long Sabotage(IWorldServices world, Entity building)
    {
        foreach (var module in building.Modules)
            if (module is ProductionUpdate production) production.DisableUntil(world, world.Tick + data.DurationTicks);
        return data.DurationTicks;
    }
}

/// <summary>
/// <c>SabotagePowerPlantCrateCollide</c>: the enemy player's power counts as sabotaged for <c>SabotagePowerDuration</c> (<c>Energy::setPowerSabotagedTillFrame</c>,
/// <see cref="IWorldServices.SetPowerSabotagedUntil"/>). The simulation has no consumer of "insufficient power" yet (no underpowered state for radar, defenses or
/// production), so the tick is recorded and exposed, nothing slows down.
/// </summary>
public sealed class SabotagePowerPlantCrateCollide(SabotageDurationData data) : SabotageCollideBase(data.Crate)
{
    public override string ModuleName => "SabotagePowerPlantCrateCollide";
    protected override bool IsTargetKind(Entity building) => building.Template.IsKindOf("FS_POWER");

    protected override long Sabotage(IWorldServices world, Entity building)
    {
        world.SetPowerSabotagedUntil(building.Owner, world.Tick + data.DurationTicks);
        return data.DurationTicks;
    }
}

/// <summary><c>SabotageSupplyCenterCrateCollide</c>: moves up to <c>StealCashAmount</c> from the enemy player's balance to the saboteur's player.</summary>
public sealed class SabotageSupplyCenterCrateCollide(SabotageSupplyCenterData data) : SabotageCollideBase(data.Crate)
{
    public override string ModuleName => "SabotageSupplyCenterCrateCollide";
    protected override bool IsTargetKind(Entity building) => building.Template.IsKindOf("FS_SUPPLY_CENTER");

    protected override long Sabotage(IWorldServices world, Entity building)
    {
        var cash = Fixed.Min(Fixed.FromInt(data.StealCashAmount), world.MoneyOf(building.Owner));
        if (cash <= Fixed.Zero) return 0;
        var stolen = world.WithdrawMoney(building.Owner, cash);
        world.AddMoney(Entity.Owner, stolen);
        return (long)stolen.Floor();
    }
}

// ============================================================================================================ PilotFindVehicleUpdate

/// <summary><c>PilotFindVehicleUpdateModuleData</c>: <c>ScanRate</c> (default 0), <c>ScanRange</c> (default 0), <c>MinHealth</c> (default 0.5).</summary>
public sealed record PilotFindVehicleData(long ScanTicks, Fixed ScanRange, Fixed MinHealth) : ModuleData
{
    public static PilotFindVehicleData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "ScanRate", 0), ModuleConfigReader.Real(configuration, "ScanRange", Fixed.Zero), ModuleConfigReader.Real(configuration, "MinHealth", Fixed.FromDecimal(0.5m)));
}

/// <summary>
/// <c>PilotFindVehicleUpdate::update</c> (AI-controlled pilots only; a human player's pilot sleeps for good): when the pilot is idle it scans, every <c>ScanRate</c>, for the
/// nearest living vehicle of its own player within <c>ScanRange</c> that is healthy enough (<c>MinHealth</c> of its maximum) and that the pilot's collide modules would like
/// to touch (the <c>IsPilot</c> <c>VeterancyCrateCollide</c>), and walks to it (<see cref="AIUpdateInterface.OrderApproachGoal"/>, the stand-in for <c>aiEnter</c>). The
/// reference's one-time walk back to the AI base centre when nothing is found is not modelled (the simulation has no per-player AI base centre).
/// </summary>
public sealed class PilotFindVehicleUpdate(PilotFindVehicleData data) : SimModule, IUpdateModule
{
    private long _nextWake = -1;

    public override string ModuleName => "PilotFindVehicleUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    public long Update(IWorldServices world)
    {
        if (!world.IsComputerPlayer(Entity.Owner) || CrateModules.AiOf(Entity) is not { } ai) return SimulationTiming.SleepForever;
        var scan = world.Time.AtLeastOneFrame(data.ScanTicks); // a ScanRate of 0 scans once per frame of the original, not once per tick
        if (!ai.IsIdle) return scan;
        if (Scan(world) is { } vehicle) ai.OrderApproachGoal(world, vehicle, false);
        return scan;
    }

    private Entity? Scan(IWorldServices world)
    {
        Entity? best = null;
        var limit = SquaredReach.Of(data.ScanRange);
        var bestDistance = Fixed.Zero;
        foreach (var other in world.Entities)
        {
            if (other.Id == Entity.Id || other.Owner != Entity.Owner || other.EffectivelyDead || other.DestroyRequested || !other.Template.IsKindOf("VEHICLE")) continue;
            var dx = other.X - Entity.X;
            var dy = other.Y - Entity.Y;
            var distance = dx * dx + dy * dy;
            if (!limit.Contains(distance)) continue;
            if (best is not null && (distance > bestDistance || (distance == bestDistance && other.Id.Value > best.Id.Value))) continue;
            if (other.Body is not { } body || body.Health < body.MaxHealth * data.MinHealth) continue;
            if (!Entity.Modules.Any(module => module is ICollideModule collide && collide.WouldLikeToCollideWith(world, other))) continue;
            best = other;
            bestDistance = distance;
        }
        return best;
    }

    public override void Describe(IStateWriter writer) => writer.Int("nextWake", _nextWake);
    public override void Load(IStateReader reader) => _nextWake = reader.Int("nextWake");
}
