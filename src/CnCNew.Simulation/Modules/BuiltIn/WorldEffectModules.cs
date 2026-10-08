using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

internal static class WorldEffectModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<OclUpdateData>("OCLUpdate", ModuleOwnerKind.Object, (configuration, _) => OclUpdateData.Parse(configuration), data => new OCLUpdate(data)),
        new ModuleBinding<DynamicShroudData>("DynamicShroudClearingRangeUpdate", ModuleOwnerKind.Object, (configuration, _) => DynamicShroudData.Parse(configuration), data => new DynamicShroudClearingRangeUpdate(data)),
        new ModuleBinding<FireOclAfterCooldownData>("FireOCLAfterWeaponCooldownUpdate", ModuleOwnerKind.Object, FireOclAfterCooldownData.Parse, data => new FireOCLAfterWeaponCooldownUpdate(data)),
        new ModuleBinding<WaveGuideData>("WaveGuideUpdate", ModuleOwnerKind.Object, (configuration, _) => WaveGuideData.Parse(configuration), data => new WaveGuideUpdate(data)),
        new ModuleBinding<PlainDieData>("DamDie", ModuleOwnerKind.Object, (configuration, _) => PlainDieData.Parse(configuration), data => new DamDie(data)),
        new ModuleBinding<ScanData>("CheckpointUpdate", ModuleOwnerKind.Object, (configuration, _) => ScanData.Parse(configuration), data => new CheckpointUpdate(data)),
        new ModuleBinding<ScanData>("EnemyNearUpdate", ModuleOwnerKind.Object, (configuration, _) => ScanData.Parse(configuration), data => new EnemyNearUpdate(data)),
        new ModuleBinding<FirestormData>("FirestormDynamicGeometryInfoUpdate", ModuleOwnerKind.Object, (configuration, _) => FirestormData.Parse(configuration), data => new FirestormDynamicGeometryInfoUpdate(data)),
        new ModuleBinding<WeaponBonusUpdateData>("WeaponBonusUpdate", ModuleOwnerKind.Object, (configuration, _) => WeaponBonusUpdateData.Parse(configuration), data => new WeaponBonusUpdate(data)),
        new ModuleBinding<CostModifierData>("CostModifierUpgrade", ModuleOwnerKind.Object, CostModifierData.Parse, data => new CostModifierUpgrade(data)),
        new ModuleBinding<NoFieldsData>("BoneFXDamage", ModuleOwnerKind.Object, (_, _) => NoFieldsData.Instance, _ => new BoneFXDamage()),
        new ModuleBinding<NoFieldsData>("BoneFXUpdate", ModuleOwnerKind.Object, (configuration, _) => BoneFXUpdate.Check(configuration), _ => new BoneFXUpdate()),
    ];

    /// <summary>Distance between an object and a point or another object measured like the reference's <c>FROM_BOUNDINGSPHERE_2D</c>: centre distance on the ground plane minus the bounding radius of each object, not below zero.</summary>
    public static Fixed EdgeDistance2D(Entity from, Entity to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var gap = Fixed.Sqrt(dx * dx + dy * dy) - from.Template.BoundingCircleRadius - to.Template.BoundingCircleRadius;
        return gap < Fixed.Zero ? Fixed.Zero : gap;
    }

    /// <summary>
    /// <c>AI::findClosestEnemy</c> / <c>findClosestAlly</c> reduced to the filters that exist here: a living, uncontained object of the wanted relationship within <paramref name="range"/> of
    /// <paramref name="me"/> (<c>FROM_BOUNDINGSPHERE_2D</c>), buildings only when armed, enemies not while stealthed and undetected. No line of sight, no attackability check. Ties go to the lower id.
    /// </summary>
    public static Entity? Closest(IWorldServices world, Entity me, Fixed range, bool enemies)
    {
        // The relationship is one between the two owners (RelationshipOf), so the candidates are the objects of the players that have the wanted one, read
        // from the world's owner lists instead of every object of the world. The pick is the minimum of (distance, id), which does not depend on the order
        // the candidates come in. The system player is never an enemy and is left out for allies; an owner the world does not know is neutral to everybody
        // but itself.
        var wanted = enemies ? Relationship.Enemies : Relationship.Allies;
        Entity? best = null;
        var bestDistance = Fixed.MaxValue;
        var ownerListed = me.Owner.IsSystem;
        foreach (var player in world.Configuration.Players)
        {
            if (player.Id == me.Owner) ownerListed = true;
            if (player.Id.IsSystem || world.RelationshipOf(me.Owner, player.Id) != wanted) continue;
            ClosestOf(world.EntitiesOwnedBy(player.Id), me, range, enemies, ref best, ref bestDistance);
        }
        if (!enemies && !ownerListed) ClosestOf(world.EntitiesOwnedBy(me.Owner), me, range, enemies, ref best, ref bestDistance);
        return best;
    }

    private static readonly int ClosestStealthed = ObjectStatusBits.Index("STEALTHED");
    private static readonly int ClosestDetected = ObjectStatusBits.Index("DETECTED");

    /// <summary>
    /// Not less than the template's <c>BoundingCircleRadius</c>, without the square root a box needs for it: the corner distance of a box is at most the
    /// sum of its two half extents, and one unit covers the rounding of the products and of the root.
    /// </summary>
    private static Fixed RadiusBound(ObjectTemplate template) => template.GeometryMajorRadius.Abs() + template.GeometryMinorRadius.Abs() + Fixed.One;

    private static void ClosestOf(EntityList candidates, Entity me, Fixed range, bool enemies, ref Entity? best, ref Fixed bestDistance)
    {
        var myReach = range + RadiusBound(me.Template) + Fixed.One;
        foreach (var other in candidates.AsSpan())
        {
            if (other.Id == me.Id || other.EffectivelyDead || other.DestroyRequested || other.ContainedBy.IsValid) continue;
            // Further away on one axis than the range plus both radii (bounded from above) plus one unit: the edge distance is then above the range
            // whatever the other axis says, and the object is passed over before the three square roots the edge distance of two boxes costs. One unit
            // is far more than the rounding of the squares and of the root can take away.
            var reach = myReach + RadiusBound(other.Template);
            var dx = other.X - me.X;
            var dy = other.Y - me.Y;
            if (range >= Fixed.Zero && (dx > reach || dy > reach || -dx > reach || -dy > reach)) continue;
            if (other.Template.IsKindOf("STRUCTURE") && SimulationWorld.ActiveWeaponSet(other) is not { WeaponCount: > 0 }) continue;
            if (enemies && other.HasStatus(ClosestStealthed) && !other.HasStatus(ClosestDetected)) continue;
            var distance = EdgeDistance2D(me, other);
            if (distance > range || distance > bestDistance || (distance == bestDistance && best is not null && other.Id.Value > best.Id.Value)) continue;
            best = other;
            bestDistance = distance;
        }
    }
}

// ================================================================================================================ OCLUpdate

/// <summary><c>OCLUpdate.cpp</c>'s fields: the creation list, the per-side lists (<c>FactionOCL = Faction:&lt;side&gt; OCL:&lt;name&gt;</c>), the delay range (ticks), whether it is created at the map edge and whether the lists depend on the owner's faction.</summary>
public sealed record OclUpdateData(string Ocl, ImmutableArray<(string Side, string Ocl)> FactionOcls, long MinDelay, long MaxDelay, bool CreateAtEdge, bool FactionTriggered) : ModuleData
{
    public static OclUpdateData Parse(RatedModuleConfiguration configuration)
    {
        var factions = ImmutableArray.CreateBuilder<(string, string)>();
        foreach (var field in ModuleConfigReader.All(configuration, "FactionOCL"))
        {
            // parseFactionObjectCreationList: Faction:<side> OCL:<name>, separated by colons and blanks.
            var tokens = field.Value.OriginalText.Split([' ', '\t', ':'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 4 || !tokens[0].Equals("Faction", StringComparison.OrdinalIgnoreCase) || !tokens[2].Equals("OCL", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{field.Location.Source}({field.Location.Line}): OCLUpdate FactionOCL expects 'Faction:<side> OCL:<name>', got '{field.Value.OriginalText.Trim()}'.");
            factions.Add((tokens[1], tokens[3]));
        }
        return new OclUpdateData(ModuleConfigReader.Text(configuration, "OCL"), factions.ToImmutable(), ModuleConfigReader.Ticks(configuration, "MinDelay", 0), ModuleConfigReader.Ticks(configuration, "MaxDelay", 0),
            ModuleConfigReader.Bool(configuration, "CreateAtEdge", false), ModuleConfigReader.Bool(configuration, "FactionTriggered", false));
    }
}

/// <summary>
/// <c>OCLUpdate::update</c>: an object (a supply drop zone, a reinforcement pad) that runs a creation list every random <c>MinDelay..MaxDelay</c>, at its own position or the nearest map edge
/// (<c>CreateAtEdge</c>), not while it is under construction. The first time it is allowed to create it only starts the timer ("you don't get to actually spread the first try"). A
/// <c>FactionTriggered</c> object (the reinforcement pads) does nothing while its owner is not a playable side; when one captures it the timer starts, a change of owner restarts it, and the list used
/// is the <c>FactionOCL</c> whose side equals the owner's <c>PlayerTemplate</c> side. Not modeled: the object being disabled (the simulation has no disabled states), the secondary creation point
/// (the object's own position, the target of a parachute delivery — <c>CreateFromList</c> has no such parameter).
/// </summary>
public sealed class OCLUpdate(OclUpdateData data) : SimModule, IUpdateModule
{
    private long _nextWake = -1;
    private long _nextCreation;
    private long _timerStarted;
    private bool _factionNeutral = true;
    private int _currentPlayer;

    public override string ModuleName => "OCLUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public long NextCreationTick => _nextCreation;

    public long Update(IWorldServices world)
    {
        if (data.FactionTriggered)
        {
            var owner = Entity.Owner;
            if (_factionNeutral)
            {
                if (world.IsPlayableSide(owner))
                {
                    _currentPlayer = owner.Value;
                    _factionNeutral = false;
                    SetNextCreation(world);
                }
            }
            else if (!world.IsPlayableSide(owner)) _factionNeutral = true;
            else if (owner.Value != _currentPlayer)
            {
                _currentPlayer = owner.Value;
                SetNextCreation(world);
            }
            if (_factionNeutral) return SimulationTiming.SleepNone;
        }

        if (world.Tick >= _nextCreation && !Entity.HasStatus(ObjectStatusBits.UnderConstruction))
        {
            if (_nextCreation == 0)
            {
                SetNextCreation(world);
                return SimulationTiming.SleepNone;
            }
            SetNextCreation(world);
            var (x, y) = data.CreateAtEdge ? FlightModules.ClosestEdgePoint(world, Entity.X, Entity.Y) : (Entity.X, Entity.Y);
            if (data.FactionTriggered)
            {
                var side = world.FactionSideOf(Entity.Owner);
                if (side.Length == 0) return SimulationTiming.SleepNone;
                foreach (var (factionSide, ocl) in data.FactionOcls)
                {
                    if (!factionSide.Equals(side, StringComparison.Ordinal)) continue;
                    world.CreateFromList(Entity, ocl, EntityId.Invalid, false, x, y, Entity.Z);
                    break;
                }
            }
            else if (data.Ocl.Length > 0 && !data.Ocl.Equals("None", StringComparison.OrdinalIgnoreCase)) world.CreateFromList(Entity, data.Ocl, EntityId.Invalid, false, x, y, Entity.Z);
        }
        return SimulationTiming.SleepNone;
    }

    private void SetNextCreation(IWorldServices world)
    {
        var delay = world.Time.DrawFrames(world.LogicRandom, data.MinDelay, data.MaxDelay);
        _timerStarted = world.Tick;
        _nextCreation = _timerStarted + delay;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("nextCreation", _nextCreation);
        writer.Int("timerStarted", _timerStarted);
        writer.Bool("factionNeutral", _factionNeutral);
        writer.Int("currentPlayer", _currentPlayer);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _nextCreation = reader.Int("nextCreation");
        _timerStarted = reader.Int("timerStarted");
        _factionNeutral = reader.Bool("factionNeutral");
        _currentPlayer = checked((int)reader.Int("currentPlayer"));
    }
}

// ================================================================================================================ DynamicShroudClearingRangeUpdate

/// <summary><c>DynamicShroudClearingRangeUpdate.cpp</c>'s fields: when and how long the clearing range shrinks to <c>FinalVision</c> and grows back, and how often the range is applied (ticks). The decal template is presentation.</summary>
public sealed record DynamicShroudData(long ChangeInterval, long GrowInterval, long ShrinkDelay, long ShrinkTime, long GrowDelay, long GrowTime, Fixed FinalVision) : ModuleData
{
    public static DynamicShroudData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "ChangeInterval", 0), ModuleConfigReader.Ticks(configuration, "GrowInterval", 0), ModuleConfigReader.Ticks(configuration, "ShrinkDelay", 0),
        ModuleConfigReader.Ticks(configuration, "ShrinkTime", 0), ModuleConfigReader.Ticks(configuration, "GrowDelay", 0), ModuleConfigReader.Ticks(configuration, "GrowTime", 0),
        ModuleConfigReader.Real(configuration, "FinalVision", Fixed.Zero));
}

/// <summary>
/// <c>DynamicShroudClearingRangeUpdate::update</c>: the shroud clearing range of an object (a spy drone, a reveal beacon) follows a timeline — it starts at nothing, grows to the
/// template's range during the last <c>GrowTime</c> before <c>GrowDelay</c> remains, holds, and shrinks to <c>FinalVision</c> over the last <c>ShrinkTime</c> after <c>ShrinkDelay</c> —
/// counted down from <c>ShrinkDelay + ShrinkTime</c> ticks. The range is handed to the vision map (<see cref="IShroudRangeProvider"/>; <c>Object::setShroudClearingRange</c>) every
/// <c>ChangeInterval</c> ticks (<c>GrowInterval</c> while growing), and the module sleeps once the final value is applied. The grid decals are presentation.
/// </summary>
public sealed class DynamicShroudClearingRangeUpdate(DynamicShroudData data) : SimModule, IUpdateModule, IShroudRangeProvider
{
    private enum Phase : byte { NotStartedYet, Growing, Sustaining, Shrinking, DoneForever, Sleeping }

    private long _nextWake = -1;
    private Phase _phase;
    private long _stateCountdown;
    private long _doneForeverTick;
    private long _changeIntervalCountdown;
    private Fixed _native;
    private Fixed _current;
    private bool _applied;
    private Fixed _appliedRange;

    public override string ModuleName => "DynamicShroudClearingRangeUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    // The deadlines of the countdown, all derived from the data.
    private long TotalTicks => data.ShrinkDelay + data.ShrinkTime;
    private long ShrinkStartDeadline => TotalTicks - data.ShrinkDelay;
    private long GrowStartDeadline => TotalTicks - data.GrowDelay;
    private long SustainDeadline => GrowStartDeadline - data.GrowTime;

    public bool TryGetShroudRange(out Fixed range)
    {
        range = _appliedRange;
        return _applied;
    }

    public override void OnSpawn(IWorldServices world)
    {
        _stateCountdown = TotalTicks;
        _doneForeverTick = world.Tick + _stateCountdown;
        _native = Entity.Template.ShroudClearingRange;
        _current = Fixed.Zero;
        _phase = Phase.NotStartedYet;
        _nextWake = world.Tick + SimulationTiming.SleepNone;
        // The range is handed on with the last tick of a logic frame of the original, when it is the range of 30 Hz: the first time with the last tick of
        // the first frame (at 30 Hz the first update), then every interval + 1 frames from there.
        _changeIntervalCountdown = world.Time.TicksPerOriginalFrame - TickOrder.Next;
    }

    public long Update(IWorldServices world)
    {
        if (_phase == Phase.Sleeping) return SimulationTiming.SleepForever;
        var now = world.Tick;
        if (_stateCountdown <= 0 || now > _doneForeverTick) _phase = Phase.DoneForever;
        else if (_stateCountdown <= ShrinkStartDeadline) _phase = Phase.Shrinking;
        else if (_stateCountdown <= SustainDeadline) _phase = Phase.Sustaining;
        else if (_stateCountdown <= GrowStartDeadline) _phase = Phase.Growing;

        // The range grows and shrinks by the original's amount per logic frame of the original (the range over the duration in frames), spread over the ticks
        // of the frame so that the shares add up to exactly that amount: at every frame boundary the range is the range of 30 Hz. The countdown starts on
        // the frame grid and falls every tick, so the tick's number inside its frame follows from it. 30 Hz: one tick, the whole amount.
        var ticks = world.Time.TicksPerOriginalFrame;
        var subTick = ticks == TickOrder.Next ? ticks : (int)((ticks - _stateCountdown % ticks) % ticks + TickOrder.Next);
        switch (_phase)
        {
            case Phase.Growing:
                _current += world.Time.FrameShare(world.Time.SharePerTick(_native, Fixed.FromInt(Math.Max(1, world.Time.ToOriginalFrames(data.GrowTime)))), subTick);
                if (_current >= _native) _phase = Phase.Sustaining;
                break;
            case Phase.Sustaining:
                _current = _native;
                break;
            case Phase.Shrinking:
                _current -= world.Time.FrameShare(world.Time.SharePerTick(_native - data.FinalVision, Fixed.FromInt(Math.Max(1, world.Time.ToOriginalFrames(data.ShrinkTime)))), subTick);
                break;
            case Phase.DoneForever:
                _current = data.FinalVision;
                break;
        }

        if (_stateCountdown > 0) _stateCountdown--;
        if (_changeIntervalCountdown > 0) _changeIntervalCountdown--;
        else
        {
            // "if (m_changeIntervalCountdown > 0) --; else { = interval; apply }": a period of interval + 1 frames of the original.
            _changeIntervalCountdown = world.Time.CountdownReload(_phase == Phase.Growing ? data.GrowInterval : data.ChangeInterval);
            _applied = true;
            _appliedRange = _current;
            if (_phase == Phase.DoneForever) _phase = Phase.Sleeping;
        }
        return _phase == Phase.Sleeping ? SimulationTiming.SleepForever : SimulationTiming.SleepNone;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("phase", (int)_phase);
        writer.Int("stateCountdown", _stateCountdown);
        writer.Int("doneForever", _doneForeverTick);
        writer.Int("changeCountdown", _changeIntervalCountdown);
        writer.Fixed("native", _native);
        writer.Fixed("current", _current);
        writer.Bool("applied", _applied);
        writer.Fixed("appliedRange", _appliedRange);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        var phase = reader.Int("phase");
        if (phase < 0 || phase > (int)Phase.Sleeping) throw new InvalidDataException("Shroud phase out of range.");
        _phase = (Phase)phase;
        _stateCountdown = reader.Int("stateCountdown");
        _doneForeverTick = reader.Int("doneForever");
        _changeIntervalCountdown = reader.Int("changeCountdown");
        _native = reader.Fixed("native");
        _current = reader.Fixed("current");
        _applied = reader.Bool("applied");
        _appliedRange = reader.Fixed("appliedRange");
    }
}

// ================================================================================================================ FireOCLAfterWeaponCooldownUpdate

/// <summary><c>FireOCLAfterWeaponCooldownUpdate.cpp</c>'s fields and the <c>UpgradeMux</c>: the weapon slot watched, the creation list, how many shots in a row it takes, and the life of the created objects per second of firing (milliseconds of life per second) up to a cap (ticks).</summary>
public sealed record FireOclAfterCooldownData(UpgradeMuxData UpgradeMux, int WeaponSlot, string Ocl, int MinShots, long LifetimePerSecond, long MaxLifetimeTicks) : ModuleData
{
    public static FireOclAfterCooldownData Parse(RatedModuleConfiguration configuration, UpgradeCatalog upgrades) => new(
        UpgradeMuxData.Parse(configuration, upgrades),
        ModuleConfigReader.Find(configuration, "WeaponSlot")?.Value.Value is EnumValue value && NameTablesPhase5.WeaponSlot.TryIndexOf(value.Name, out var slot) ? slot : 0,
        ModuleConfigReader.Text(configuration, "OCL"), checked((int)ModuleConfigReader.Int(configuration, "MinShotsToCreateOCL", 1)),
        ModuleConfigReader.Int(configuration, "OCLLifetimePerSecond", 1000), ModuleConfigReader.Ticks(configuration, "OCLLifetimeMaxCap", 1000));
}

/// <summary>
/// <c>FireOCLAfterWeaponCooldownUpdate::update</c>: counts the shots the watched weapon fires in a row (a shot in the previous tick continues the series; the first one marks the start); once the
/// weapon could have fired and did not (its reload went by), or the weapon was switched away from, and at least <c>MinShotsToCreateOCL</c> shots were fired, the creation list runs with the
/// created objects living <c>seconds fired · OCLLifetimePerSecond / 1000</c> seconds, at most <c>OCLLifetimeMaxCap</c> (the anthrax cloud a SCUD leaves). Only while the upgrade
/// conditions of the <c>UpgradeMux</c> hold (the object's and its player's upgrades together; the shipped data names the upgrades that pick the stronger list). The simulation gives an object
/// weapon slot of the weapon slot model (<c>SimulationWorld.WeaponSlots.cs</c>) it watches, with that slot's last-shot and ready ticks; "the weapon selected" is the object's current slot
/// (the one that fired last or is locked), so a SCUD Launcher that fires its toxin slot in a series and then reloads, or switches to its other weapon, creates the cloud.
/// </summary>
public sealed class FireOCLAfterWeaponCooldownUpdate(FireOclAfterCooldownData data) : SimModule, IUpdateModule, IUpgradeModule
{
    private readonly UpgradeMuxState _mux = new(data.UpgradeMux);
    private long _nextWake = -1;
    private bool _valid;
    private int _consecutiveShots;
    private long _startTick;

    public override string ModuleName => "FireOCLAfterWeaponCooldownUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    // The module is not activated by the upgrade state machine: it asks the mux whether the conditions hold each tick (testUpgradeConditions).
    public void TryActivate(IWorldServices world, UpgradeMask keyMask) { }
    public void TryReset(IWorldServices world, UpgradeMask keyMask) { }
    public bool WouldUpgrade(UpgradeMask keyMask) => false;

    public long Update(IWorldServices world)
    {
        var validThisFrame = true;
        var validToFire = true;
        var slot = data.WeaponSlot;
        // FireOCLAfterWeaponCooldownUpdate::update: the current weapon must be the watched slot's; no weapon selected means a switch or a finished shot.
        if (SimulationWorld.WeaponIndexIn(Entity, Entity.CurrentWeaponSlot) < 0 || Entity.CurrentWeaponSlot != slot) validThisFrame = false;

        var mask = world.UpgradeKeyMaskOf(Entity);
        if (validThisFrame && !_mux.TestUpgradeConditions(mask))
        {
            validThisFrame = false;
            validToFire = false;
        }

        var now = world.Tick;
        if (validThisFrame)
        {
            if (Entity.LastFireTickOf(slot) == now - TickOrder.Next)
            {
                _consecutiveShots++;
                if (_consecutiveShots == 1) _startTick = now;
            }
            else if (Entity.ReadyTickOf(slot) < now && data.MinShots <= _consecutiveShots) FireOcl(world);
        }
        else if (validToFire && SimulationWorld.WeaponIndexIn(Entity, slot) >= 0 && data.MinShots <= _consecutiveShots) FireOcl(world);

        if (validThisFrame != _valid)
        {
            _valid = validThisFrame;
            ResetStats();
        }
        return SimulationTiming.SleepNone;
    }

    private void ResetStats()
    {
        _consecutiveShots = 0;
        _startTick = 0;
    }

    private void FireOcl(IWorldServices world)
    {
        // seconds fired * (OCLLifetimePerSecond / 1000) seconds, in ticks: (ticks fired * per-second) / 1000, capped.
        // The truncating division runs on logic frames of the original, the result is scaled: the same lifetime at every rate.
        var lifetime = Math.Min(world.Time.OriginalFrames(world.Time.ToOriginalFrames(world.Tick - _startTick) * data.LifetimePerSecond / 1000), data.MaxLifetimeTicks);
        if (data.Ocl.Length > 0 && !data.Ocl.Equals("None", StringComparison.OrdinalIgnoreCase)) world.CreateFromListWithLifetime(Entity, data.Ocl, Entity.Id, lifetime);
        ResetStats();
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("valid", _valid);
        writer.Int("shots", _consecutiveShots);
        writer.Int("startTick", _startTick);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _valid = reader.Bool("valid");
        _consecutiveShots = checked((int)reader.Int("shots"));
        _startTick = reader.Int("startTick");
    }
}

// ================================================================================================================ WaveGuideUpdate + DamDie

/// <summary><c>WaveGuideUpdate.cpp</c>'s fields: the delay before the wave starts moving (ticks), the width and the spacing and bend of its front, the damage zone and amount, the topple force and the water height it brings. Sounds, particles and the shore effects are presentation; <c>WaterVelocity</c> pushes the water surface (terrain, not simulated).</summary>
public sealed record WaveGuideData(long WaveDelay, Fixed YSize, Fixed LinearSpacing, Fixed BendMagnitude, Fixed PreferredHeight, Fixed DamageRadius, Fixed DamageAmount, Fixed ToppleForce, int SplashFrequency) : ModuleData
{
    public static WaveGuideData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "WaveDelay", 0), ModuleConfigReader.Real(configuration, "YSize", Fixed.Zero), ModuleConfigReader.Real(configuration, "LinearWaveSpacing", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "WaveBendMagnitude", Fixed.Zero), ModuleConfigReader.Real(configuration, "PreferredHeight", Fixed.Zero), ModuleConfigReader.Real(configuration, "DamageRadius", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "DamageAmount", Fixed.Zero), ModuleConfigReader.Real(configuration, "ToppleForce", Fixed.Zero), checked((int)ModuleConfigReader.Int(configuration, "RandomSplashSoundFrequency", 0)));
}

/// <summary>
/// <c>WaveGuideUpdate::update</c>/<c>startMoving</c>/<c>doDamage</c>: the flood of a broken dam. The object is disabled from the start (<c>m_needDisable</c>) and stays so until <see cref="DamDie"/>
/// enables it. Then, <c>WaveDelay</c> later, it is put on the waypoint <c>WaveGuide1</c>, turned along the link out of it and moves along the linked waypoint path (at its locomotor's speed
/// in a straight line from waypoint to waypoint, where the reference lets its AI fly it); within ten pathfind cells of the last waypoint it is destroyed. Every tick, at each of the
/// points of its front (a parabola of <c>YSize/LinearWaveSpacing</c> points, <c>x = -y²/WaveBendMagnitude</c> in its own frame), every object within <c>DamageRadius</c> that is below
/// <c>PreferredHeight</c> (bridges excepted) and not a wave guide or bridge tower is, if it was not wet yet and passes the reference's "behind the wave" test (the cosine test uses the shape
/// point's world position, as the reference does), made wet, toppled with <c>ToppleForce</c> and hit with <c>DamageAmount</c> water damage (death <c>FLOODED</c>). The reference's random
/// splash sound draws once per half second from the logic stream, and so does this. Not modeled: raising the water (terrain), the shore and front effects, the broken-bridge object
/// (<c>WaterWaveBridge</c> replaces a bridge the wave hits; terrain bridges are not part of the terrain model).
/// </summary>
public sealed class WaveGuideUpdate(WaveGuideData data) : SimModule, IUpdateModule
{
    /// <summary><c>LOGICFRAMES_PER_SECOND / 2.0f</c> (WaveGuideUpdate.cpp, <c>update</c>): the splash roll comes after more than 15 logic frames of the original.</summary>
    private const long SplashFrames = SimulationRate.OriginalFramesPerSecond / 2;
    private static readonly Fixed PathExtraDistance = Fixed.FromInt(10 * TerrainMap.CellSize);
    private static readonly int WetBit = ObjectStatusBits.Index("WET");
    private static readonly int WaterDamage = NameTables.DamageType.TryIndexOf("WATER", out var water) ? water : throw new InvalidOperationException("WATER is not a damage type.");
    private static readonly int FloodedDeath = NameTables.DeathType.TryIndexOf("FLOODED", out var flooded) ? flooded : throw new InvalidOperationException("FLOODED is not a death type.");

    private long _nextWake = -1;
    private bool _disabled = true;
    private long _activeTick = -1;
    private bool _initialized;
    private int _targetId = -1;
    private Fixed _finalX;
    private Fixed _finalY;
    private long _splashTick;
    // The wave moves every tick; what its front wets and whether it has reached the end of its path is asked once per logic frame of the original, counted
    // from the tick it starts to move (the original samples the front once per frame). At rest (unwritten) between two frames, which is always at 30 Hz.
    private FramePhase _framePhase;

    public override string ModuleName => "WaveGuideUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public bool IsDisabled => _disabled;
    public bool IsMoving => _initialized;

    /// <summary><c>clearDisabled(DISABLED_DEFAULT)</c> (<see cref="DamDie"/>): the wave may start.</summary>
    public void Enable(IWorldServices world)
    {
        _disabled = false;
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    public long Update(IWorldServices world)
    {
        if (_disabled) return SimulationTiming.SleepNone;
        var now = world.Tick;
        if (_activeTick < 0) _activeTick = now;
        // The original takes its first step in the update in which the delay has passed. That step is a whole frame of the original, so above 30 Hz the wave
        // sets off the ticks earlier that the frame needs and ends its first frame in that update (not possible without a delay).
        if (now - _activeTick < data.WaveDelay - Math.Min(data.WaveDelay, world.Time.TicksPerOriginalFrame - TickOrder.Next)) return SimulationTiming.SleepNone;
        if (!_initialized)
        {
            if (!StartMoving(world))
            {
                world.RequestDestroy(Entity.Id);
                return SimulationTiming.SleepNone;
            }
            _initialized = true;
            _framePhase.Reset();
        }

        // Every half second a random splash sound is rolled; the roll is state of the logic stream even though the sound is presentation.
        // "getFrame() - m_splashSoundFrame > LOGICFRAMES_PER_SECOND / 2.0f": strictly more than 15 frames of the original, so a period of 16.
        if (now - _splashTick >= world.Time.OriginalFrames(SplashFrames) + world.Time.TicksPerOriginalFrame)
        {
            _splashTick = now;
            _ = world.LogicRandom.Range(1, 100);
        }

        Advance(world);
        _framePhase.Next(world.Time);
        if (!_framePhase.AtBoundary) return SimulationTiming.SleepNone;
        var dx = _finalX - Entity.X;
        var dy = _finalY - Entity.Y;
        if (SquaredReach.Of(PathExtraDistance).Contains(dx * dx + dy * dy))
        {
            world.RequestDestroy(Entity.Id);
            return SimulationTiming.SleepNone;
        }
        DoDamage(world);
        return SimulationTiming.SleepNone;
    }

    /// <summary><c>startMoving</c>: false when the waypoint path has no second waypoint.</summary>
    private bool StartMoving(IWorldServices world)
    {
        if (world.Terrain is not { } terrain || terrain.FindWaypoint("WaveGuide1") is not { } first) return true;
        var last = first;
        for (var next = terrain.NextWaypoint(first); next is not null; next = terrain.NextWaypoint(next)) last = next;
        _finalX = last.X;
        _finalY = last.Y;
        if (terrain.NextWaypoint(first) is not { } second) return false;
        Entity.Angle = FixedTrig.Atan2(second.Y - first.Y, second.X - first.X);
        Entity.X = first.X;
        Entity.Y = first.Y;
        Entity.Z = world.GroundHeightAt(first.X, first.Y);
        _targetId = second.Id;
        return true;
    }

    /// <summary>The path follower: straight at the locomotor's speed to the current waypoint, on to the next one on arrival.</summary>
    private void Advance(IWorldServices world)
    {
        if (world.Terrain is not { } terrain || _targetId < 0 || Entity.Template.Locomotor is not { } locomotor) return;
        var target = terrain.Waypoints.FirstOrDefault(waypoint => waypoint.Id == _targetId);
        if (target is null) return;
        var step = locomotor.Speed;
        while (true)
        {
            var dx = target.X - Entity.X;
            var dy = target.Y - Entity.Y;
            var distance = Fixed.Sqrt(dx * dx + dy * dy);
            if (distance > step)
            {
                Entity.Angle = FixedTrig.Atan2(dy, dx);
                Entity.X += dx / distance * step;
                Entity.Y += dy / distance * step;
                break;
            }
            Entity.X = target.X;
            Entity.Y = target.Y;
            if (terrain.NextWaypoint(target) is not { } next) { _targetId = -1; break; }
            step -= distance;
            target = next;
            _targetId = next.Id;
            if (step <= Fixed.Zero) break;
        }
    }

    private void DoDamage(IWorldServices world)
    {
        if (data.LinearSpacing <= Fixed.Zero) return;
        var cos = FixedTrig.Cos(Entity.Angle);
        var sin = FixedTrig.Sin(Entity.Angle);
        var half = (data.YSize / Fixed.FromInt(2)).Floor();
        var limit = SquaredReach.Of(data.DamageRadius);
        var points = 0;
        for (var y = Fixed.FromInt(-half); y < Fixed.FromInt(half) && points < 64; y += data.LinearSpacing, points++)
        {
            var localX = data.BendMagnitude != Fixed.Zero ? -(y * y) / data.BendMagnitude : Fixed.Zero;
            var px = Entity.X + localX * cos - y * sin;
            var py = Entity.Y + localX * sin + y * cos;
            var hits = new List<Entity>();
            foreach (var obj in world.Entities)
            {
                var dx = obj.X - px;
                var dy = obj.Y - py;
                if (!obj.DestroyRequested && limit.Contains(dx * dx + dy * dy)) hits.Add(obj);
            }
            hits.Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));
            foreach (var obj in hits)
            {
                if (obj.Template.IsKindOf("WAVEGUIDE") || obj.Template.IsKindOf("BRIDGE_TOWER")) continue;
                // Only objects below the water level the wave brings, "bridges are an exception as their object is raised above the ground".
                if (obj.Z > data.PreferredHeight && !obj.Template.IsKindOf("BRIDGE")) continue;
                var vx = obj.X - px;
                var vy = obj.Y - py;
                var length = Fixed.Sqrt(vx * vx + vy * vy);
                if (length > Fixed.Zero) { vx /= length; vy /= length; }
                // The reference dots the unit vector with the shape point's world position (not with the wave's forward direction).
                var behind = vx * px + vy * py;
                if (behind >= Fixed.Zero || obj.HasStatus(WetBit)) continue;
                obj.Status |= 1UL << WetBit;
                world.MarkDirty(obj);
                if (obj.ModuleArray.OfType<ToppleUpdate>().FirstOrDefault() is { IsAbleToBeToppled: true } topple) topple.ApplyTopplingSpeed(world, obj.X - px, obj.Y - py, data.ToppleForce);
                var amount = world.AdjustDamageByArmor(obj, WaterDamage, data.DamageAmount);
                world.Damage(obj.Id, new DamageInfo(Entity.Id, amount, WaterDamage, FloodedDeath, false));
            }
        }
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("disabled", _disabled);
        writer.Int("activeTick", _activeTick);
        writer.Bool("initialized", _initialized);
        writer.Int("target", _targetId);
        writer.Fixed("finalX", _finalX);
        writer.Fixed("finalY", _finalY);
        writer.Int("splashTick", _splashTick);
        _framePhase.Describe(writer, "framePhase");
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _disabled = reader.Bool("disabled");
        _activeTick = reader.Int("activeTick");
        _initialized = reader.Bool("initialized");
        _targetId = checked((int)reader.Int("target"));
        _finalX = reader.Fixed("finalX");
        _finalY = reader.Fixed("finalY");
        _splashTick = reader.Int("splashTick");
        _framePhase.Load(reader, "framePhase");
    }
}

/// <summary><c>DamDie::onDie</c>: when the dam dies every wave guide on the map is enabled (<c>clearDisabled(DISABLED_DEFAULT)</c>) and starts its flood.</summary>
public sealed class DamDie(PlainDieData data) : DieModuleBase(data.Mux)
{
    public override string ModuleName => "DamDie";

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!Mux.IsApplicable(Entity, info)) return;
        foreach (var entity in world.Entities)
        {
            if (!entity.Template.IsKindOf("WAVEGUIDE")) continue;
            foreach (var module in entity.ModuleArray)
                if (module is WaveGuideUpdate wave) wave.Enable(world);
        }
    }
}

// ================================================================================================================ CheckpointUpdate + EnemyNearUpdate

/// <summary><c>CheckpointUpdate.h</c>/<c>EnemyNearUpdate.h</c>: the scan delay (ticks; default one second).</summary>
public sealed record ScanData(long ScanDelayTicks) : ModuleData
{
    public static ScanData Parse(RatedModuleConfiguration configuration) => new(ModuleConfigReader.Ticks(configuration, "ScanDelayTime", SimulationRate.OriginalFramesPerSecond));
}

/// <summary>
/// <c>CheckpointUpdate::update</c>: a border checkpoint opens for its allies and closes for its enemies. Every tick it looks within its vision range for the closest enemy and ally
/// (<c>AI::findClosestEnemy</c>/<c>findClosestAlly</c> as far as they exist here: living, uncontained, armed buildings only, no line of sight; the reference's scan-delay test is
/// <c>|| TRUE</c>, so it scans every frame too). It is open when an ally and no enemy is near; its gate (the object's minor radius) then shrinks by 1/3 per tick to nothing and grows back to
/// the template's when closed. The gate radius is state; the reference changes the geometry of the object, which the pathfinding and collision of the simulation (template geometry) do
/// not see, and the door animation (<c>MODELCONDITION_DOOR_1_OPENING</c>) is presentation.
/// </summary>
public sealed class CheckpointUpdate(ScanData data) : SimModule, IUpdateModule
{
    /// <summary><c>radius - 0.333f</c> / <c>radius + 0.333f</c> (CheckpointUpdate.cpp, <c>update</c>): the gate moves by a third of a unit per logic frame of the original.</summary>
    private static readonly Fixed GateStepPerFrame = Fixed.FromDecimal(0.333m);

    private long _nextWake = -1;
    private bool _allyNear;
    private bool _enemyNear;
    private Fixed _gateRadius;
    private long _scanDelay;

    public override string ModuleName => "CheckpointUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public bool IsOpen => !_enemyNear && _allyNear;
    public Fixed GateRadius => _gateRadius;

    /// <summary><c>m_allyNear</c>/<c>m_enemyNear</c> of the last scan: the reference changes the door model condition only when one of them changes, so presentation needs both, not just <see cref="IsOpen"/>.</summary>
    public bool AllyNear => _allyNear;

    public bool EnemyNear => _enemyNear;

    public override void OnSpawn(IWorldServices world)
    {
        _gateRadius = Entity.Template.GeometryMinorRadius;
        // "bias a random amount so everyone doesn't spike at once": the draw is kept although the scan runs every tick.
        _scanDelay = world.Time.DrawFrames(world.LogicRandom, 0, data.ScanDelayTicks);
    }

    public long Update(IWorldServices world)
    {
        var range = Entity.Template.VisionRange;
        _enemyNear = WorldEffectModules.Closest(world, Entity, range, enemies: true) is not null;
        _allyNear = WorldEffectModules.Closest(world, Entity, range, enemies: false) is not null;
        var max = Entity.Template.GeometryMinorRadius;
        var step = world.Time.PerOriginalFrameToPerTick(GateStepPerFrame);
        if (IsOpen)
        {
            if (_gateRadius > Fixed.Zero) _gateRadius -= step;
        }
        else if (_gateRadius < max) _gateRadius += step;
        return SimulationTiming.SleepNone;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("allyNear", _allyNear);
        writer.Bool("enemyNear", _enemyNear);
        writer.Fixed("gateRadius", _gateRadius);
        writer.Int("scanDelay", _scanDelay);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _allyNear = reader.Bool("allyNear");
        _enemyNear = reader.Bool("enemyNear");
        _gateRadius = reader.Fixed("gateRadius");
        _scanDelay = reader.Int("scanDelay");
    }
}

/// <summary>
/// <c>EnemyNearUpdate::update</c>: every <c>ScanDelayTime</c> (a random share of it at first, "so everyone doesn't spike at once") the object looks for the closest enemy within its vision range
/// (<c>AI::findClosestEnemy</c> as far as it exists here, see <see cref="CheckpointUpdate"/>); <see cref="IsEnemyNear"/> is what the reference turns into <c>MODELCONDITION_ENEMYNEAR</c> (an
/// alarm animation), so presentation reads it from here.
/// </summary>
public sealed class EnemyNearUpdate(ScanData data) : SimModule, IUpdateModule
{
    private long _nextWake = -1;
    private bool _enemyNear;
    private long _scanDelay;

    public override string ModuleName => "EnemyNearUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public bool IsEnemyNear => _enemyNear;

    public override void OnSpawn(IWorldServices world) => _scanDelay = world.Time.CountdownReload(world.Time.DrawFrames(world.LogicRandom, 0, data.ScanDelayTicks));

    public long Update(IWorldServices world)
    {
        if (_scanDelay == 0)
        {
            // "if (m_enemyScanDelay == 0) { = m_enemyScanDelayTime; scan } else --" (EnemyNearUpdate.cpp): a period of delay + 1 frames of the original.
            _scanDelay = world.Time.CountdownReload(data.ScanDelayTicks);
            _enemyNear = WorldEffectModules.Closest(world, Entity, Entity.Template.VisionRange, enemies: true) is not null;
        }
        else _scanDelay--;
        return SimulationTiming.SleepNone;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("enemyNear", _enemyNear);
        writer.Int("scanDelay", _scanDelay);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _enemyNear = reader.Bool("enemyNear");
        _scanDelay = reader.Int("scanDelay");
    }
}

// ================================================================================================================ FirestormDynamicGeometryInfoUpdate

/// <summary><c>DynamicGeometryInfoUpdate.cpp</c> and <c>FirestormDynamicGeometryInfoUpdate.cpp</c> fields (times in ticks): the geometry the object grows from and to, the delay and transition time, and the damage scan.</summary>
public sealed record FirestormData(long InitialDelay, Fixed InitialHeight, Fixed InitialMajor, Fixed InitialMinor, Fixed FinalHeight, Fixed FinalMajor, Fixed FinalMinor, long TransitionTime,
    bool ReverseAtTransitionTime, long DamageDelay, Fixed DamageAmount, Fixed MaxHeightForDamage, string FxList) : ModuleData
{
    public static FirestormData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "InitialDelay", 0), ModuleConfigReader.Real(configuration, "InitialHeight", Fixed.Zero), ModuleConfigReader.Real(configuration, "InitialMajorRadius", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "InitialMinorRadius", Fixed.Zero), ModuleConfigReader.Real(configuration, "FinalHeight", Fixed.Zero), ModuleConfigReader.Real(configuration, "FinalMajorRadius", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "FinalMinorRadius", Fixed.Zero), ModuleConfigReader.Ticks(configuration, "TransitionTime", 0), ModuleConfigReader.Bool(configuration, "ReverseAtTransitionTime", false),
        ModuleConfigReader.Ticks(configuration, "DelayBetweenDamageFrames", 0), ModuleConfigReader.Real(configuration, "DamageAmount", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "MaxHeightForDamage", Fixed.FromInt(20)), ModuleConfigReader.Text(configuration, "FXList"));
}

/// <summary>
/// <c>DynamicGeometryInfoUpdate::update</c> + <c>FirestormDynamicGeometryInfoUpdate::update</c>: a firestorm (napalm, black napalm) waits <c>InitialDelay</c>, then its geometry (height, major and
/// minor radius) moves linearly from the initial to the final values over <c>TransitionTime</c> ticks and, with <c>ReverseAtTransitionTime</c>, back again; the geometry is state here (the object's
/// template geometry is fixed in the simulation). Once running it plays its effect list once and, every <c>DelayBetweenDamageFrames</c> ticks, burns (<c>DAMAGE_FLAME</c>, death
/// <c>BURNED</c>, <c>DamageAmount</c> after armor) everything whose bounding circle touches the storm's current bounding circle (the major radius for a cylinder; for a box the corner distance) and
/// that is not more than <c>MaxHeightForDamage</c> above it — everybody, allies included, as in the reference. The particle systems, the scorch mark and the academy statistics are presentation.
/// </summary>
public sealed class FirestormDynamicGeometryInfoUpdate(FirestormData data) : SimModule, IUpdateModule
{
    private long _nextWake = -1;
    private long _startDelay;
    private long _timeActive;
    private bool _started;
    private bool _finished;
    private bool _reverse;
    private bool _switched;
    private (Fixed Height, Fixed Major, Fixed Minor) _from;
    private (Fixed Height, Fixed Major, Fixed Minor) _to;
    private (Fixed Height, Fixed Major, Fixed Minor) _current;
    private bool _effectsFired;
    private long _lastDamage;

    public override string ModuleName => "FirestormDynamicGeometryInfoUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public Fixed CurrentMajorRadius => _current.Major;
    public Fixed CurrentHeight => _current.Height;
    public bool Finished => _finished;

    public override void OnSpawn(IWorldServices world)
    {
        _startDelay = world.Time.AtLeastOneFrame(data.InitialDelay);
        _reverse = data.ReverseAtTransitionTime;
        _from = (data.InitialHeight, data.InitialMajor, data.InitialMinor);
        _to = (data.FinalHeight, data.FinalMajor, data.FinalMinor);
        _current = (Entity.Template.GeometryHeight, Entity.Template.GeometryMajorRadius, Entity.Template.GeometryMinorRadius);
        _nextWake = world.Tick + SimulationTiming.SleepNone;
    }

    public long Update(IWorldServices world)
    {
        // DynamicGeometryInfoUpdate::update
        if (!_finished)
        {
            if (!_started)
            {
                _startDelay--;
                if (_startDelay > 0) return SimulationTiming.SleepNone;
                _started = true;
            }
            var ratio = data.TransitionTime == 0 ? Fixed.One : Fixed.FromInt(_timeActive) / Fixed.FromInt(data.TransitionTime);
            _current = (_from.Height + ratio * (_to.Height - _from.Height), _from.Major + ratio * (_to.Major - _from.Major), _from.Minor + ratio * (_to.Minor - _from.Minor));
            _timeActive++;
            if (_timeActive > data.TransitionTime)
            {
                if (_reverse)
                {
                    _switched = true;
                    _timeActive = 0;
                    _reverse = false;
                    _from = (data.FinalHeight, data.FinalMajor, data.FinalMinor);
                    _to = (data.InitialHeight, data.InitialMajor, data.InitialMinor);
                }
                else _finished = true;
            }
        }
        if (_startDelay > 0) return SimulationTiming.SleepNone;

        if (!_effectsFired)
        {
            if (data.FxList.Length > 0 && !data.FxList.Equals("None", StringComparison.OrdinalIgnoreCase)) world.Emit(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, data.FxList, 0);
            _effectsFired = true;
        }
        if (world.Tick - _lastDamage >= world.Time.AtLeastOneFrame(data.DamageDelay))
        {
            DoDamageScan(world);
            _lastDamage = world.Tick;
        }
        return SimulationTiming.SleepNone;
    }

    private void DoDamageScan(IWorldServices world)
    {
        // The current bounding circle of the geometry: the corner distance of a box, the major radius of a cylinder or sphere.
        var radius = Entity.Template.Geometry.Equals("BOX", StringComparison.OrdinalIgnoreCase) ? Fixed.Sqrt(_current.Major * _current.Major + _current.Minor * _current.Minor) : _current.Major;
        if (radius == Fixed.Zero) return;
        var flame = DamageTypes.Flame;
        var targets = new List<Entity>();
        foreach (var other in world.Entities)
        {
            if (other.DestroyRequested) continue;
            var dx = other.X - Entity.X;
            var dy = other.Y - Entity.Y;
            var gap = Fixed.Sqrt(dx * dx + dy * dy) - other.Template.BoundingCircleRadius;
            if (gap > radius) continue;
            if (other.Z > Entity.Z + data.MaxHeightForDamage) continue;
            targets.Add(other);
        }
        targets.Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));
        foreach (var other in targets)
            world.Damage(other.Id, new DamageInfo(Entity.Id, world.AdjustDamageByArmor(other, flame, data.DamageAmount), flame, DeathTypes.Burned, false));
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("startDelay", _startDelay);
        writer.Int("timeActive", _timeActive);
        writer.Bool("started", _started);
        writer.Bool("finished", _finished);
        writer.Bool("reverse", _reverse);
        writer.Bool("switched", _switched);
        DescribeGeometry(writer, "from", _from);
        DescribeGeometry(writer, "to", _to);
        DescribeGeometry(writer, "current", _current);
        writer.Bool("effectsFired", _effectsFired);
        writer.Int("lastDamage", _lastDamage);
    }

    private static void DescribeGeometry(IStateWriter writer, string name, (Fixed Height, Fixed Major, Fixed Minor) geometry)
    {
        writer.BeginObject(name);
        writer.Fixed("height", geometry.Height);
        writer.Fixed("major", geometry.Major);
        writer.Fixed("minor", geometry.Minor);
        writer.EndObject();
    }

    private static (Fixed, Fixed, Fixed) LoadGeometry(IStateReader reader, string name)
    {
        var item = reader.Object(name);
        return (item.Fixed("height"), item.Fixed("major"), item.Fixed("minor"));
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _startDelay = reader.Int("startDelay");
        _timeActive = reader.Int("timeActive");
        _started = reader.Bool("started");
        _finished = reader.Bool("finished");
        _reverse = reader.Bool("reverse");
        _switched = reader.Bool("switched");
        _from = LoadGeometry(reader, "from");
        _to = LoadGeometry(reader, "to");
        _current = LoadGeometry(reader, "current");
        _effectsFired = reader.Bool("effectsFired");
        _lastDamage = reader.Int("lastDamage");
    }
}

// ================================================================================================================ WeaponBonusUpdate

/// <summary><c>WeaponBonusUpdate.cpp</c>'s fields: the kinds an affected object must have and must not have, the duration of the bonus and the pulse interval (ticks), the range and the bonus condition given.</summary>
public sealed record WeaponBonusUpdateData(ImmutableArray<string> RequiredKinds, ImmutableArray<string> ForbiddenKinds, long Duration, long Delay, Fixed Range, int Condition) : ModuleData
{
    public static WeaponBonusUpdateData Parse(RatedModuleConfiguration configuration)
    {
        var condition = ModuleConfigReader.Find(configuration, "BonusConditionType")?.Value.Value is EnumValue value && NameTablesPhase5.WeaponBonusCondition.TryIndexOf(value.Name, out var index) ? index : -1;
        return new WeaponBonusUpdateData(ModuleConfigReader.Flags(configuration, "RequiredAffectKindOf")?.Names ?? [], ModuleConfigReader.Flags(configuration, "ForbiddenAffectKindOf")?.Names ?? [],
            ModuleConfigReader.Ticks(configuration, "BonusDuration", 0), ModuleConfigReader.Ticks(configuration, "BonusDelay", 0), ModuleConfigReader.Real(configuration, "BonusRange", Fixed.Zero), condition);
    }
}

/// <summary>
/// <c>WeaponBonusUpdate::update</c> (a frenzy beacon): every <c>BonusDelay</c> ticks every living ally within <c>BonusRange</c> (the object itself included) that has all of the required and none of the
/// forbidden kinds, and the passengers of such a container, receive the weapon bonus condition for <c>BonusDuration</c> ticks (<c>doTempWeaponBonus</c>: an object holds one such condition; a
/// different one replaces it, the same one restarts its timer). The reference keeps the countdown in a helper module of every object and the bonus outlives the beacon; here the beacon keeps the
/// countdowns of the objects it gave a condition to and clears them when they run out — or at once when the beacon itself is removed.
/// </summary>
public sealed class WeaponBonusUpdate(WeaponBonusUpdateData data) : SimModule, IUpdateModule
{
    private long _nextWake = -1;
    private readonly List<(EntityId Target, long Expiry)> _active = [];

    public override string ModuleName => "WeaponBonusUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public int ActiveCount => _active.Count;

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    public override void OnRemove(IWorldServices world)
    {
        foreach (var (target, _) in _active)
            if (world.FindEntity(target) is { } entity) Clear(world, entity);
        _active.Clear();
    }

    private long _nextPulse = -1;

    public long Update(IWorldServices world)
    {
        var now = world.Tick;
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            if (_active[index].Expiry > now) continue;
            if (world.FindEntity(_active[index].Target) is { } entity) Clear(world, entity);
            _active.RemoveAt(index);
        }
        if (_nextPulse < 0) _nextPulse = now;
        if (now >= _nextPulse)
        {
            Pulse(world);
            _nextPulse = now + world.Time.AtLeastOneFrame(data.Delay);
        }
        var soonest = _nextPulse;
        foreach (var (_, expiry) in _active) soonest = Math.Min(soonest, expiry);
        return Math.Max(soonest - now, SimulationTiming.SleepNone);
    }

    private bool Affects(Entity entity)
        => !entity.EffectivelyDead && data.RequiredKinds.All(entity.Template.IsKindOf) && !data.ForbiddenKinds.Any(entity.Template.IsKindOf);

    private void Pulse(IWorldServices world)
    {
        var limit = SquaredReach.Of(data.Range);
        var near = new List<Entity>();
        foreach (var other in world.Entities)
        {
            if (other.DestroyRequested || other.EffectivelyDead || other.ContainedBy.IsValid) continue;
            if (other.Owner != Entity.Owner && world.RelationshipOf(Entity.Owner, other.Owner) != Relationship.Allies) continue;
            var dx = other.X - Entity.X;
            var dy = other.Y - Entity.Y;
            if (limit.Contains(dx * dx + dy * dy)) near.Add(other);
        }
        near.Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));
        foreach (var other in near)
        {
            if (Affects(other)) Give(world, other);
            if (other.Container is { } container)
                foreach (var passengerId in container.Contained.ToArray())
                    if (world.FindEntity(passengerId) is { } passenger && Affects(passenger)) Give(world, passenger);
        }
    }

    /// <summary><c>TempWeaponBonusHelper::doTempWeaponBonus</c>.</summary>
    private void Give(IWorldServices world, Entity target)
    {
        if (data.Condition < 0) return;
        // An object holds one temporary condition: a different one given by another beacon is replaced (the same one just restarts its timer).
        foreach (var other in world.Entities)
            foreach (var module in other.ModuleArray)
                if (module is WeaponBonusUpdate beacon && !ReferenceEquals(beacon, this) && beacon.Condition != data.Condition && beacon.Holds(target.Id)) beacon.Release(world, target);
        target.WeaponBonusConditions |= 1u << data.Condition;
        var expiry = world.Tick + data.Duration;
        var index = _active.FindIndex(entry => entry.Target == target.Id);
        if (index >= 0) _active[index] = (target.Id, expiry);
        else _active.Add((target.Id, expiry));
        world.MarkDirty(target);
    }

    private int Condition => data.Condition;

    private bool Holds(EntityId target) => _active.Exists(entry => entry.Target == target);

    private void Release(IWorldServices world, Entity target)
    {
        _active.RemoveAll(entry => entry.Target == target.Id);
        Clear(world, target);
    }

    private void Clear(IWorldServices world, Entity target)
    {
        if (data.Condition < 0) return;
        target.WeaponBonusConditions &= ~(1u << data.Condition);
        world.MarkDirty(target);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("nextPulse", _nextPulse);
        writer.BeginArray("active");
        foreach (var (target, expiry) in _active)
        {
            writer.BeginObject(null);
            writer.UInt("target", target.Value);
            writer.Int("expiry", expiry);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _nextPulse = reader.Int("nextPulse");
        _active.Clear();
        foreach (var item in reader.Array("active")) _active.Add((new EntityId(checked((uint)item.UInt("target"))), item.Int("expiry")));
    }
}

// ================================================================================================================ CostModifierUpgrade

/// <summary><c>CostModifierUpgrade.cpp</c>: the <c>UpgradeMux</c>, the kinds the discount applies to (<c>EffectKindOf</c>) and the change (<c>Percentage</c>, negative: cheaper).</summary>
public sealed record CostModifierData(UpgradeMuxData Mux, ImmutableArray<string> Kinds, Fixed Percentage) : ModuleData
{
    public static CostModifierData Parse(RatedModuleConfiguration configuration, UpgradeCatalog upgrades) => new(
        UpgradeMuxData.Parse(configuration, upgrades), ModuleConfigReader.Flags(configuration, "EffectKindOf")?.Names ?? [], ModuleConfigReader.Real(configuration, "Percentage", Fixed.Zero));
}

/// <summary>
/// <c>CostModifierUpgrade</c>: once its upgrade completes, everything its owner builds that has all of <c>EffectKindOf</c> costs <c>1 + Percentage</c> times as much
/// (<c>Player::addKindOfProductionCostChange</c>; the modifiers of a player multiply). The change follows the object: it is taken back when the object is destroyed (<c>onDelete</c>) and moves
/// to the new owner when the object is captured (<c>onCapture</c>). The world applies it where it takes money for production and construction (<c>SimulationWorld.ProductionCostOf</c>).
/// </summary>
public sealed class CostModifierUpgrade(CostModifierData data) : UpgradeMuxModule(data.Mux), IOwnerChangeReceiver
{
    public override string ModuleName => "CostModifierUpgrade";

    protected override void Activate(IWorldServices world) => world.AddProductionCostChange(Entity.Owner, data.Kinds, data.Percentage);

    public override void OnRemove(IWorldServices world)
    {
        if (Executed) world.RemoveProductionCostChange(Entity.Owner, data.Kinds, data.Percentage);
    }

    public void OnOwnerChanged(IWorldServices world, PlayerId oldOwner, PlayerId newOwner)
    {
        if (!Executed) return;
        world.RemoveProductionCostChange(oldOwner, data.Kinds, data.Percentage);
        world.AddProductionCostChange(newOwner, data.Kinds, data.Percentage);
    }
}

// ================================================================================================================ BoneFXDamage + BoneFXUpdate

/// <summary>
/// <c>BoneFXDamage.cpp</c> only forwards body-damage-state changes to the object's <c>BoneFXUpdate</c>, which plays effect lists, particle systems and creation lists at named bones of the model
/// (<c>BoneFXUpdate::changeBodyDamageState</c>). Everything it does happens at model bones — presentation. The simulation module exists so objects that carry the pair (the civilian and tech
/// buildings) can be created; it has no state and no gameplay effect (<c>docs/SIMULATION-CORE.md</c>).
/// </summary>
public sealed class BoneFXDamage : SimModule
{
    public override string ModuleName => "BoneFXDamage";
    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

/// <summary>
/// <c>BoneFXUpdate.cpp</c>: the effects of <see cref="BoneFXDamage"/>'s damage states — effect lists and particle systems at model bones, restarted every few frames while a state lasts, and
/// <c>OCL</c> slots that create objects at bones. Presentation, and so a shell without state like <c>FlightDeckBehavior</c>: bone positions do not exist in the simulation. An object whose
/// data fills an <c>OCL</c> slot (which would create objects) is refused (<see cref="UnsupportedModuleConfigurationException"/>) instead of silently losing them; no shipped object does.
/// </summary>
public sealed class BoneFXUpdate : SimModule
{
    public override string ModuleName => "BoneFXUpdate";
    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }

    /// <summary>Rejects a configuration that would create objects at bones.</summary>
    public static NoFieldsData Check(RatedModuleConfiguration configuration)
    {
        foreach (var state in new[] { "Pristine", "Damaged", "ReallyDamaged", "Rubble" })
            for (var slot = 1; slot <= 8; slot++)
                if (ModuleConfigReader.Find(configuration, $"{state}OCL{slot}") is not null) throw new UnsupportedModuleConfigurationException($"BoneFXUpdate {state}OCL{slot}");
        return NoFieldsData.Instance;
    }
}
