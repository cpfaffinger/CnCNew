using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// <c>SpecialAbilityUpdateModuleData</c>. Absent fields use the constructor defaults of the reference (<c>StartAbilityRange</c>/<c>AbilityAbortRange</c> =
/// <c>SPECIAL_ABILITY_HUGE_DISTANCE</c>, times 0, <c>NeedToFaceTarget</c> = Yes, <c>PackUnpackVariationFactor</c> 0, <c>MaxSpecialObjects</c> 1). The sounds and the particle system belong to
/// presentation and stay unread.
/// </summary>
public sealed record SpecialAbilityUpdateData(
    SpecialPowerBaseData Base, Fixed StartAbilityRange, Fixed AbilityAbortRange, long PreparationTicks, long PackTicks, long UnpackTicks,
    Fixed PackUnpackVariation, bool DoCaptureFx, int AwardXp, bool NeedToFaceTarget, bool ApproachRequiresLos, string SpecialObject, int MaxSpecialObjects, bool SpecialObjectsPersistent,
    bool SpecialObjectsPersistWhenOwnerDies, bool SkipPackingWithNoTarget, bool LoseStealthOnTrigger, Fixed FleeRange, long EffectTicks, long PersistentPrepTicks,
    bool PersistenceRequiresRecharge) : ModuleData
{
    /// <summary><c>SPECIAL_ABILITY_HUGE_DISTANCE</c>: "no limit". Compared, never squared (the square would not fit a <see cref="Fixed"/>).</summary>
    public static readonly Fixed HugeDistance = Fixed.FromInt(10000000);

    public static SpecialAbilityUpdateData Parse(RatedModuleConfiguration configuration) => new(
        SpecialPowerBaseData.Parse(configuration),
        ModuleConfigReader.Real(configuration, "StartAbilityRange", HugeDistance),
        ModuleConfigReader.Real(configuration, "AbilityAbortRange", HugeDistance),
        ModuleConfigReader.Ticks(configuration, "PreparationTime", 0),
        ModuleConfigReader.Ticks(configuration, "PackTime", 0),
        ModuleConfigReader.Ticks(configuration, "UnpackTime", 0),
        ModuleConfigReader.Real(configuration, "PackUnpackVariationFactor", Fixed.Zero),
        ModuleConfigReader.Bool(configuration, "DoCaptureFX", false),
        checked((int)ModuleConfigReader.Int(configuration, "AwardXPForTriggering", 0)),
        ModuleConfigReader.Bool(configuration, "NeedToFaceTarget", true),
        ModuleConfigReader.Bool(configuration, "ApproachRequiresLOS", true),
        ModuleConfigReader.Text(configuration, "SpecialObject"),
        checked((int)ModuleConfigReader.Int(configuration, "MaxSpecialObjects", 1)),
        ModuleConfigReader.Bool(configuration, "SpecialObjectsPersistent", false),
        ModuleConfigReader.Bool(configuration, "SpecialObjectsPersistWhenOwnerDies", false),
        ModuleConfigReader.Bool(configuration, "SkipPackingWithNoTarget", false),
        ModuleConfigReader.Bool(configuration, "LoseStealthOnTrigger", false),
        ModuleConfigReader.Real(configuration, "FleeRangeAfterCompletion", Fixed.Zero),
        ModuleConfigReader.Ticks(configuration, "EffectDuration", 0),
        ModuleConfigReader.Ticks(configuration, "PersistentPrepTime", 0),
        ModuleConfigReader.Bool(configuration, "PersistenceRequiresRecharge", false));
}

/// <summary>The part of a staged capture the player sees, in the order the ability goes through them.</summary>
public enum AbilityStage : byte
{
    None = 0,
    /// <summary>Walking to the target, or turning to face it once within <c>StartAbilityRange</c>.</summary>
    Approaching = 1,
    /// <summary><c>MODELCONDITION_UNPACKING</c>: <c>UnpackTime</c> long.</summary>
    Unpacking = 2,
    /// <summary><c>PreparationTime</c>: the flag goes up (<c>RAISING_FLAG</c>) or the hack runs (<c>FIRING_A</c>); the target changes owner when it ends.</summary>
    Preparing = 3,
    /// <summary><c>MODELCONDITION_PACKING</c>: <c>PackTime</c> long, after the capture.</summary>
    Packing = 4,
}

/// <summary>What may be captured, shared by <c>CaptureCommand</c> and the staged ability.</summary>
public static class StagedCaptureRules
{
    /// <summary>
    /// Reduced <c>ActionManager::canCaptureBuilding</c> target rules: a complete, unsold, alive <c>STRUCTURE</c> owned by an enemy, or <c>KINDOF_CAPTURABLE</c> and not an ally's; never
    /// the actor's own.
    /// </summary>
    public static bool CanCapture(IWorldServices world, Entity actor, Entity target)
    {
        if (target.DestroyRequested || target.EffectivelyDead) return false;
        if (!target.Template.IsKindOf("STRUCTURE") || target.HasStatus(ObjectStatusBits.UnderConstruction) || target.HasStatus(ObjectStatusBits.Sold)) return false;
        if (target.Owner == actor.Owner) return false;
        var relationship = world.RelationshipOf(actor.Owner, target.Owner);
        if (!(relationship == Relationship.Enemies || (target.Template.IsKindOf("CAPTURABLE") && relationship != Relationship.Allies))) return false;
        // "If the enemy unit is stealthed and not detected, then we can't capture it!"
        if (target.HasStatus(StealthedStatus) && !target.HasStatus(DetectedStatus) && !target.HasStatus(DisguisedStatus)) return false;
        // "If it's garrisoned already, we cannot capture it (unless it's just stealth-garrisoned)" (F-54).
        if (target.Container is GarrisonContain garrison && garrison.ContainCount - garrison.StealthUnitsContained(world) > 0) return false;
        // appearsToContainFriendlies: a stealth garrison that makes the building look like a non-enemy's.
        return target.Container?.ApparentOwnerFor(world, actor.Owner) is not { } apparent || world.RelationshipOf(actor.Owner, apparent) == Relationship.Enemies;
    }

    private static readonly int StealthedStatus = ObjectStatusBits.Index("STEALTHED");
    private static readonly int DetectedStatus = ObjectStatusBits.Index("DETECTED");
    private static readonly int DisguisedStatus = ObjectStatusBits.Index("DISGUISED");
}

/// <summary>
/// <c>SpecialAbilityUpdate.cpp</c>: the generic handler behind command-button-triggered unit abilities. The reference drives an approach / face /
/// unpack / prepare / trigger / pack state machine; this port stages the two <b>capture</b> powers (<c>SPECIAL_INFANTRY_CAPTURE_BUILDING</c> of the Ranger,
/// Red Guard and Rebel, <c>SPECIAL_BLACKLOTUS_CAPTURE_BUILDING</c>) completely, see <c>docs/SIM-STAGED-CAPTURE.md</c>. Every other power this module carries keeps its
/// previous behaviour: only the recharge state every special power shares (<see cref="SpecialPowerModuleBase"/>), no handler, no staging.
/// <para>
/// Order of events (<c>update</c>): approach until the target is within <c>StartAbilityRange</c> (bounding spheres), face it, <c>startUnpacking</c> (<c>UnpackTime</c>),
/// <c>startPreparation</c> (<c>PreparationTime</c>: the power's recharge starts, <c>USING_ABILITY</c> is set), when the preparation counter reaches zero
/// <c>triggerAbilityEffect</c> (experience, the target changes owner at once), then <c>startPacking</c> (<c>PackTime</c>) and <c>finishAbility</c>. The ability ends early
/// (<c>onExit</c>) when its object dies, is ordered to do something else by the player or a script, or its target dies / is captured by an ally.
/// </para>
/// </summary>
public sealed partial class SpecialAbilityUpdate(SpecialAbilityUpdateData data) : SpecialPowerModuleBase(data.Base), IUpdateModule, ISpecialPowerHandler
{
    private enum PackState : byte { None, Packing, Unpacking, Packed, Unpacked }

    /// <summary>Approach orders that ended without getting within range before the ability gives up (the reference re-issues the approach every tick forever).</summary>
    private const int MaxApproachOrders = 6;

    private static readonly int UsingAbilityBit = ObjectStatusBits.Index("USING_ABILITY");
    private static readonly int StealthedBit = ObjectStatusBits.Index("STEALTHED");
    private static readonly int DetectedBit = ObjectStatusBits.Index("DETECTED");

    private long _nextWake = SimulationTiming.SleepForever;
    private bool _active;
    private PackState _packState;
    private long _animFrames;
    private long _prepFrames;
    private EntityId _target;
    private bool _withinRange;
    private bool _facingInitiated;
    private bool _facingComplete;
    private int _approachOrders;
    private Fixed _flashPhase;

    /// <summary><c>m_noTargetCommand</c>: the order named neither an object nor a place (the detonation of remote charges).</summary>
    private bool _noTarget;

    /// <summary><c>m_targetPos</c>: the order named a place.</summary>
    private bool _hasPlace;
    private Fixed _placeX;
    private Fixed _placeY;

    /// <summary>Ticks of the current logic frame of the original the unit has spent turning to its target (<see cref="TurnToward"/>); always 0 at 30 Hz, written only while nonzero.</summary>
    private int _facePhase;

    public override string ModuleName => "SpecialAbilityUpdate";
    public override bool AcceptsTarget(SpecialPowerTargetKind kind) => true;
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    // ------------------------------------------------------------------------------------------------------------ read-only state

    /// <summary><c>SpecialAbilityUpdate::isActive</c>.</summary>
    public bool IsActive => _active;

    public EntityId Target => _target;

    /// <summary>Where the active ability is (presentation: <c>UNPACKING</c>, <c>RAISING_FLAG</c>/<c>FIRING_A</c>, <c>PACKING</c>).</summary>
    public AbilityStage Stage
    {
        get
        {
            if (!_active) return AbilityStage.None;
            return _packState switch
            {
                PackState.Unpacking => AbilityStage.Unpacking,
                PackState.Packing => AbilityStage.Packing,
                PackState.Unpacked when _prepFrames > 0 => AbilityStage.Preparing,
                _ => AbilityStage.Approaching,
            };
        }
    }

    /// <summary>Ticks of the current stage that are left (the unpack or pack animation, or the preparation).</summary>
    public long StageTicksLeft => Stage switch
    {
        AbilityStage.Unpacking or AbilityStage.Packing => _animFrames,
        AbilityStage.Preparing => _prepFrames,
        _ => 0,
    };

    public SpecialAbilityUpdateData Data => data;

    /// <summary>The ability is one of the two capture powers (the only staged ones).</summary>
    public bool IsCapture(IWorldServices world) => PowerIndex >= 0 && CaptureKind(world) != CaptureType.None;

    /// <summary><c>SPECIAL_BLACKLOTUS_CAPTURE_BUILDING</c>: the preparation shows the hacker's typing animation (<c>FIRING_A</c>) instead of the raised flag.</summary>
    public bool IsHackStyle(IWorldServices world) => CaptureKind(world) == CaptureType.BlackLotus;

    private enum CaptureType : byte { None, Infantry, BlackLotus }

    private CaptureType CaptureKind(IWorldServices world)
    {
        var type = world.SpecialPowers[PowerIndex].Type;
        if (type.Equals("SPECIAL_INFANTRY_CAPTURE_BUILDING", StringComparison.OrdinalIgnoreCase)) return CaptureType.Infantry;
        if (type.Equals("SPECIAL_BLACKLOTUS_CAPTURE_BUILDING", StringComparison.OrdinalIgnoreCase)) return CaptureType.BlackLotus;
        return CaptureType.None;
    }

    // ------------------------------------------------------------------------------------------------------------ handler

    public bool Handles(IWorldServices world, int powerIndex) => powerIndex == PowerIndex && KindOf(world) != AbilityKind.None;

    public bool ValidatesTarget(IWorldServices world, int powerIndex, in SpecialPowerTarget target) => TakesTarget(world, target);

    /// <summary><c>initiateIntentToDoSpecialPower</c> through <c>UseSpecialPowerCommand</c>.</summary>
    public void Initiate(IWorldServices world, int powerIndex, in SpecialPowerTarget target)
    {
        if (target.Kind == SpecialPowerTargetKind.None) Begin(world, null);
        else if (target.Kind == SpecialPowerTargetKind.Object && world.FindEntity(target.Entity) is { } victim) Begin(world, victim);
        else if (target.Kind == SpecialPowerTargetKind.Location)
        {
            Begin(world, null);
            (_noTarget, _hasPlace, _placeX, _placeY) = (false, true, target.X, target.Y);
            if (data.UnpackTicks != 0) _packState = PackState.Packed;
        }
    }

    /// <summary>
    /// <c>initiateIntentToDoSpecialPower</c>: forgets any earlier run, takes the target, goes idle (the approach starts in the next update) and starts at the unpacked state when
    /// <c>UnpackTime</c> is zero. A second staged ability of the same object is cancelled first (the reference kills the mutually exclusive ones).
    /// </summary>
    public void Begin(IWorldServices world, Entity? target)
    {
        if (CrateModules.AiOf(Entity) is not { } ai) return;
        foreach (var module in Entity.ModuleArray)
            if (module is SpecialAbilityUpdate { _active: true } other && !ReferenceEquals(other, this)) other.Exit(world);
        ClearRun();
        Entity.Status &= ~(1UL << UsingAbilityBit);
        _target = target?.Id ?? EntityId.Invalid;
        _noTarget = target is null;
        _packState = data.UnpackTicks == 0 || (_noTarget && data.SkipPackingWithNoTarget) ? PackState.Unpacked : PackState.Packed;
        _active = true;
        ai.OrderIdle(world, false);
        // setWakeFrame(UPDATE_SLEEP_NONE): the next logic frame of the original, whatever tick of a frame the order came in.
        world.WakeAt(Entity, this, world.Tick + world.Time.TicksPerOriginalFrame);
        world.MarkDirty(Entity);
    }

    // ------------------------------------------------------------------------------------------------------------ update

    public override void OnSpawn(IWorldServices world)
    {
        base.OnSpawn(world);
        _nextWake = SimulationTiming.SleepForever;
    }

    public long Update(IWorldServices world)
    {
        // "Validation of special objects makes sure that they still exist": a planted charge keeps the module awake, for the end of its owner too.
        if (!_active)
        {
            if (Entity.EffectivelyDead) Exit(world, cleanup: true);
            ValidateSpecialObjects(world);
            return _specialObjects.Count > 0 ? SimulationTiming.SleepNone : SimulationTiming.SleepForever;
        }
        Step(world);
        return _active || _specialObjects.Count > 0 ? SimulationTiming.SleepNone : SimulationTiming.SleepForever;
    }

    private void Step(IWorldServices world)
    {
        ValidateSpecialObjects(world);
        if (Entity.EffectivelyDead || Entity.DestroyRequested) { Exit(world, cleanup: true); return; }
        var ai = CrateModules.AiOf(Entity);
        // "ai->getLastCommandSource() != CMD_FROM_AI": the player or a script ordered something else.
        if (ai is null || ai.LastCommandFromPlayer) { Exit(world); return; }
        // Movement breaks the capture once it has started, except while the unit turns to face its target.
        if (Entity.HasMoveTarget && _withinRange && !_facingInitiated) { Exit(world); return; }
        if (HandlePacking(world)) return;

        var target = _target.IsValid ? world.FindEntity(_target) : null;
        if (target is not null && target.DestroyRequested) target = null;
        var abort = false;
        if (target is not null)
        {
            // A dead target ends the ability at once; a stealthed, undetected target as long as the preparation has not completed; one a colleague already captured.
            var kind = KindOf(world);
            if (target.EffectivelyDead) abort = true;
            else
            {
                if (kind is AbilityKind.InfantryCapture or AbilityKind.BlackLotusCapture or AbilityKind.HackerDisable && target.Owner == Entity.Owner) abort = true;
                if (kind == AbilityKind.LaserMissiles && target.Template.IsKindOf("STRUCTURE")) abort = true;
                var hidden = target.HasStatus(StealthedBit) && !target.HasStatus(DetectedBit);
                if (kind is AbilityKind.LaserMissiles or AbilityKind.VehicleHack) abort |= hidden;
                else
                {
                    var checksStealth = kind switch
                    {
                        AbilityKind.TankHunterTnt => false,
                        AbilityKind.TimedCharges or AbilityKind.RemoteCharges => !NeedToUnpack,
                        _ => true,
                    };
                    if (checksStealth && hidden && _prepFrames != 0) abort = true;
                }
            }
        }

        var power = SpecialPowerRouting.FindModule(Entity, PowerIndex);
        if (abort || power is null)
        {
            ai.OrderIdle(world, false);
            Exit(world);
            return;
        }

        if (_prepFrames != 0)
        {
            // "If power requires recharging, lets freeze prep countdown until power is ready".
            if (!IsPersistent || !data.PersistenceRequiresRecharge || power.IsReady(world)) _prepFrames--;
            if (_prepFrames == 0)
            {
                TriggerEffect(world, target, power);
                if (IsPersistent)
                {
                    // The ability repeats until it is told otherwise or its target ends.
                    _prepFrames = data.PersistentPrepTicks;
                    if (data.PersistenceRequiresRecharge) power.Trigger(world);
                    return;
                }
                EndPreparation(world);
                if (NeedToPack) StartPacking(world); else FinishAbility(world);
            }
            else if (!ContinuePreparation(world, target, power))
            {
                EndPreparation(world);
                if (NeedToPack) StartPacking(world); else FinishAbility(world);
            }
            return;
        }

        if (IsWithinStartRange(target, ai))
        {
            if (!_withinRange)
            {
                _withinRange = true;
                // The approach move ends where the unit is (aiBusy / aiIdle in the reference's first state change).
                if (Entity.HasMoveTarget) ai.OrderIdle(world, false);
            }
            if (data.NeedToFaceTarget && !_noTarget && !_facingComplete)
            {
                _facingInitiated = true;
                if (target is not null ? !TurnToward(world, target.X, target.Y) : _hasPlace && !TurnToward(world, _placeX, _placeY)) return;
                _facingComplete = true;
            }
            if (NeedToUnpack)
            {
                StartUnpacking(world);
                return;
            }
            if (_packState != PackState.Unpacked) return;
            StartPreparation(world, target, power);
            if (_prepFrames != 0) return;
            // PreparationTime 0: trigger at once.
            TriggerEffect(world, target, power);
            if (IsPersistent && data.PersistenceRequiresRecharge)
            {
                _prepFrames = data.PersistentPrepTicks;
                power.Trigger(world);
                return;
            }
            EndPreparation(world);
            if (NeedToPack) StartPacking(world); else FinishAbility(world);
            return;
        }

        if (ai.IsIdle)
        {
            if (_hasPlace) ai.OrderMove(world, _placeX, _placeY, false);
            else ApproachTarget(world, ai, target);
        }
    }

    /// <summary><c>handlePackingProcessing</c>: counts the unpack/pack animation down; true while a stage still runs (the rest of the update is skipped).</summary>
    private bool HandlePacking(IWorldServices world)
    {
        if (_animFrames <= 0) return false;
        _animFrames--;
        if (_animFrames > 0) return true;
        if (_packState == PackState.Unpacking)
        {
            _packState = PackState.Unpacked;
            return false;
        }
        if (_packState == PackState.Packing)
        {
            _packState = PackState.Packed;
            FinishAbility(world);
            return true;
        }
        return false;
    }

    /// <summary><c>isPersistentAbility</c>: the preparation repeats after every trigger.</summary>
    private bool IsPersistent => data.PersistentPrepTicks > 0;

    private bool NeedToPack => _packState == PackState.Unpacked && data.PackTicks > 0 && !(_noTarget && data.SkipPackingWithNoTarget);
    private bool NeedToUnpack => _packState == PackState.Packed && data.UnpackTicks > 0;

    private long Varied(IWorldServices world, long ticks)
    {
        if (data.PackUnpackVariation <= Fixed.Zero) return ticks;
        var factor = world.LogicRandom.Range(Fixed.One - data.PackUnpackVariation, Fixed.One + data.PackUnpackVariation);
        // The variation is applied to logic frames of the original (floored, at least one frame) and the result scaled: the same duration at every rate.
        return world.Time.OriginalFrames(Math.Max(1, (Fixed.FromInt(world.Time.ToOriginalFrames(ticks)) * factor).Floor()));
    }

    private void StartUnpacking(IWorldServices world)
    {
        _packState = PackState.Unpacking;
        _animFrames = Varied(world, data.UnpackTicks);
    }

    private void StartPacking(IWorldServices world)
    {
        _packState = PackState.Packing;
        _animFrames = Varied(world, data.PackTicks);
    }

    /// <summary>
    /// <c>startPreparation</c> for the capture powers: the counter is set; a target a colleague already captured stops here (the next update aborts); otherwise the power's recharge
    /// starts ("Trigger the reset timer on the special power because we're officially starting it now"), the unit goes idle and <c>USING_ABILITY</c> is set. "Whoops, it was mined": an infantry capture sets a booby trap off here.
    /// </summary>
    private void StartPreparation(IWorldServices world, Entity? target, ISpecialPowerModule power)
    {
        _prepFrames = data.PreparationTicks;
        if (target is not null && IsCapture(world))
        {
            if (target.Owner == Entity.Owner) return;
            if (CaptureKind(world) == CaptureType.Infantry && StickyBombUpdate.CheckAndDetonateBoobyTrap(world, target, Entity) && target.EffectivelyDead) return;
            // Black Lotus: "Relationship r == ALLIES: return" (an ally of any kind, not only the own team).
            if (CaptureKind(world) == CaptureType.BlackLotus && world.RelationshipOf(Entity.Owner, target.Owner) == Relationship.Allies) return;
        }
        if (!PrepareKind(world, target)) return;
        power.Trigger(world);
        if (CrateModules.AiOf(Entity) is { } ai) ai.OrderIdle(world, false);
        Entity.Status |= 1UL << UsingAbilityBit;
    }

    /// <summary>
    /// <c>continuePreparation</c>: false aborts the preparation (outside <c>AbilityAbortRange</c>, the target is gone, or became an ally's). The infantry capture restarts the power's
    /// recharge on every tick of the preparation, so it only starts counting down when the capture is over; the Black Lotus recharges in <see cref="TriggerEffect"/>.
    /// </summary>
    private bool ContinuePreparation(IWorldServices world, Entity? target, ISpecialPowerModule power)
    {
        if (data.AbilityAbortRange < SpecialAbilityUpdateData.HugeDistance && target is not null && !WithinSpheres(target, data.AbilityAbortRange)) return false;
        if (!IsCapture(world)) return ContinueKind(world, target);
        if (target is null) return false;
        if (world.RelationshipOf(Entity.Owner, target.Owner) == Relationship.Allies) return false;
        if (data.DoCaptureFx) StepCaptureFlash(world, target);
        if (CaptureKind(world) == CaptureType.Infantry) power.Trigger(world);
        return true;
    }

    /// <summary>
    /// The <c>DoCaptureFX</c> part of <c>continuePreparation</c>: the flash phase grows every logic frame of the original by a third of the fraction of the preparation that is over,
    /// so it grows faster and faster; each time its integer part goes from odd to even the target flashes (and ticks). The simulation only reports the moment
    /// (<see cref="SimEventKind.CaptureFlash"/>); colour and sound are the presentation's. The reference never resets the phase; here every capture starts it at zero (it is part of
    /// the run's state).
    /// <para>
    /// The phase is a recursion per frame (its increment grows from frame to frame), so above 30 Hz it is stepped on the ticks that complete a frame of the preparation - the
    /// counter is a whole number of frames then, and both the fraction and the number of steps are those of 30 Hz. Stepped every tick the phase ran twice (four times) as fast.
    /// </para>
    /// </summary>
    private void StepCaptureFlash(IWorldServices world, Entity target)
    {
        var frame = world.Time.TicksPerOriginalFrame;
        if (frame != 1 && _prepFrames % frame != 0) return;
        var wasOdd = (_flashPhase.Floor() & 1) != 0;
        var denominator = Math.Max(1, data.PreparationTicks);
        _flashPhase += (Fixed.One - Fixed.FromInt(_prepFrames) / Fixed.FromInt(denominator)) / Fixed.FromInt(3);
        if (wasOdd && (_flashPhase.Floor() & 1) == 0) world.Emit(SimEventKind.CaptureFlash, target.Id, Entity.Id, string.Empty, 0);
    }

    /// <summary>
    /// <c>triggerAbilityEffect</c>: experience for the unit, then the capture. A garrisonable target is emptied instead ("we do not want to set a neutral building to our team if we are
    /// not in it"); otherwise the target changes to the capturer's player (<see cref="IWorldServices.CaptureStructure"/>) and the Black Lotus's recharge starts.
    /// </summary>
    private void TriggerEffect(IWorldServices world, Entity? target, ISpecialPowerModule power)
    {
        if (data.AwardXp > 0) world.AddExperiencePoints(Entity, data.AwardXp);
        if (!IsCapture(world))
        {
            if (TriggerKind(world, target, KindOf(world)) && data.LoseStealthOnTrigger) StealthUpdate.Expose(world, Entity);
            return;
        }
        if (target is null || target.Owner == Entity.Owner || target.EffectivelyDead) return;
        if (target.Container is { IsGarrisonable: true })
        {
            // removeAllContained(TRUE): the stealth riders thrown out are detected.
            foreach (var riderId in target.Container.Contained.ToArray())
                if (world.FindEntity(riderId) is { } rider) StealthUpdate.Expose(world, rider);
            world.EvacuateContainer(target);
            return;
        }
        world.CaptureStructure(Entity, target);
        if (CaptureKind(world) == CaptureType.BlackLotus) power.Trigger(world);
    }

    /// <summary><c>endPreparation</c>: the placed charges stay, the beam of an ability that aims or hacks goes.</summary>
    private void EndPreparation(IWorldServices world)
    {
        Entity.Status &= ~(1UL << UsingAbilityBit);
        _prepFrames = 0;
        if (KindOf(world) is AbilityKind.LaserMissiles or AbilityKind.HackerDisable or AbilityKind.VehicleHack or AbilityKind.StealCash) KillSpecialObjects(world);
    }

    private void FinishAbility(IWorldServices world)
    {
        _withinRange = false;
        _packState = PackState.None;
        // "He will plant the charge then flee; without a target he simply detonates it without fleeing": away from what the unit faces, around its own mines.
        if (data.FleeRange > Fixed.Zero && !_noTarget && CrateModules.AiOf(Entity) is { } ai)
        {
            var (fleeX, fleeY) = (Entity.X - FixedTrig.Cos(Entity.Angle) * data.FleeRange, Entity.Y - FixedTrig.Sin(Entity.Angle) * data.FleeRange);
            foreach (var mine in world.EntitiesOfKind("MINE").AsSpan())
            {
                if (mine.Owner != Entity.Owner) continue;
                var (dx, dy) = (fleeX - mine.X, fleeY - mine.Y);
                var distance = Fixed.Sqrt(dx * dx + dy * dy);
                if (distance > data.FleeRange + mine.Template.BoundingCircleRadius || distance == Fixed.Zero) continue;
                (fleeX, fleeY) = (mine.X + dx / distance * data.FleeRange, mine.Y + dy / distance * data.FleeRange);
                break;
            }
            ai.OrderMove(world, fleeX, fleeY, false);
        }
        Exit(world);
    }

    /// <summary>
    /// <c>onExit</c>: clears the run (the model conditions follow the stage, which becomes <see cref="AbilityStage.None"/>) and the status. Special objects that do not persist go, and with
    /// <paramref name="cleanup"/> (the unit is dead) those that do not outlive their owner.
    /// </summary>
    private void Exit(IWorldServices world, bool cleanup = false)
    {
        Entity.Status &= ~(1UL << UsingAbilityBit);
        if (!data.SpecialObjectsPersistent || (cleanup && !data.SpecialObjectsPersistWhenOwnerDies)) KillSpecialObjects(world);
        ReleaseLaserLock(world);
        ClearRun();
        world.MarkDirty(Entity);
    }

    private void ClearRun()
    {
        _active = false;
        _withinRange = false;
        _packState = PackState.None;
        _animFrames = 0;
        _prepFrames = 0;
        _target = EntityId.Invalid;
        _facingInitiated = false;
        _facingComplete = false;
        _approachOrders = 0;
        _flashPhase = Fixed.Zero;
        _facePhase = 0;
        _noTarget = false;
        _hasPlace = false;
        _placeX = Fixed.Zero;
        _placeY = Fixed.Zero;
    }

    // ------------------------------------------------------------------------------------------------------------ approach and facing

    /// <summary>
    /// <c>isWithinStartAbilityRange</c>: the distance between the two bounding spheres (<c>FROM_BOUNDINGSPHERE_2D</c>) is within <c>StartAbilityRange</c>; once reached it stays
    /// reached. A range of "huge" (or no target) needs no approach. With <c>ApproachRequiresLOS</c> (the default) the reference then looks for the target in a range of
    /// <c>StartAbilityRange - UNDERSIZE</c> (a quarter of a pathfinding cell) with a line-of-sight filter, so the effective range is that smaller one (the line of sight itself is not
    /// modelled; the collision requirement of a range under 2.5 units neither). When an approach has ended without reaching it (the grid lets the unit get no closer), a gap of
    /// up to two cells counts as arrived instead of re-ordering the same approach for ever.
    /// </summary>
    private bool IsWithinStartRange(Entity? target, AIUpdateInterface ai)
    {
        if (_withinRange) return true;
        if (data.StartAbilityRange >= SpecialAbilityUpdateData.HugeDistance) return true;
        if (_hasPlace)
        {
            var (dx, dy) = (_placeX - Entity.X, _placeY - Entity.Y);
            if (SquaredReach.Of(data.StartAbilityRange + Entity.Template.BoundingCircleRadius).Contains(dx * dx + dy * dy)) return true;
            return ai.IsIdle && _approachOrders > 0;
        }
        if (target is null) return true;
        var range = data.ApproachRequiresLos ? Fixed.Max(Fixed.Zero, data.StartAbilityRange - Undersize) : data.StartAbilityRange;
        if (WithinSpheres(target, range)) return true;
        return ai.IsIdle && _approachOrders > 0 && WithinSpheres(target, range + ArrivalSlack);
    }

    /// <summary><c>UNDERSIZE</c> of <c>isWithinStartAbilityRange</c>: <c>PATHFIND_CELL_SIZE_F * 0.25</c>.</summary>
    private static readonly Fixed Undersize = Fixed.FromInt(TerrainMap.CellSize) / Fixed.FromInt(4);

    private static readonly Fixed ArrivalSlack = Fixed.FromInt(2 * TerrainMap.CellSize);

    private bool WithinSpheres(Entity target, Fixed range)
    {
        var reach = range + target.Template.BoundingCircleRadius + Entity.Template.BoundingCircleRadius;
        var dx = target.X - Entity.X;
        var dy = target.Y - Entity.Y;
        return SquaredReach.Of(reach).Contains(dx * dx + dy * dy);
    }

    /// <summary><c>approachTarget</c>: <c>aiMoveToObject</c>. The path ends at the nearest cell the grid allows; a few failed approaches in a row end the ability (see <see cref="MaxApproachOrders"/>).</summary>
    private void ApproachTarget(IWorldServices world, AIUpdateInterface ai, Entity? target)
    {
        if (target is null) return;
        if (++_approachOrders > MaxApproachOrders)
        {
            Exit(world);
            return;
        }
        ai.OrderMove(world, target.X, target.Y, false);
    }

    /// <summary>
    /// <c>aiFaceObject</c>: turns on the spot at the locomotor's turn rate; true when the unit faces the target.
    /// <para>
    /// "Within one step of the target: face it and go on" is decided once per logic frame of the original against a frame of turn, as at 30 Hz, in the first tick of the frame
    /// (<see cref="_facePhase"/> counts the ticks of the unit's own frame); in between the unit turns by its rate per tick. The facing therefore ends in the same frame at every
    /// rate, where a snap-in against one tick of turn ended it up to a frame early.
    /// </para>
    /// </summary>
    private bool TurnToward(IWorldServices world, Fixed x, Fixed y)
    {
        var desired = FixedTrig.Atan2(y - Entity.Y, x - Entity.X);
        var difference = FixedTrig.AngleDifference(Entity.Angle, desired);
        var turnRate = Entity.Template.Locomotor?.TurnRatePerTick ?? Fixed.Zero;
        var frame = world.Time.TicksPerOriginalFrame;
        var decides = _facePhase == 0;
        if (frame != 1) _facePhase = _facePhase + 1 == frame ? 0 : _facePhase + 1;
        if (turnRate <= Fixed.Zero || (decides && difference.Abs() <= (frame == 1 ? turnRate : turnRate * frame)))
        {
            Entity.Angle = desired;
            _facePhase = 0;
            return true;
        }
        // Inside a frame the step never passes the target (it cannot at 30 Hz: the frame's decision has just found the target further away than one step).
        Entity.Angle = difference.Abs() <= turnRate ? desired : FixedTrig.Normalize(Entity.Angle + (difference.IsNegative ? -turnRate : turnRate));
        return false;
    }

    // ------------------------------------------------------------------------------------------------------------ state

    /// <summary>Written only while an ability runs, so objects that never use it (and every saved state before the staging existed) keep their exact state.</summary>
    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        if (!_active)
        {
            if (_specialObjects.Count == 0) return;
            DescribeSpecialObjects(writer);
            writer.Int("nextWake", _nextWake);
            return;
        }
        DescribeSpecialObjects(writer);
        if (_noTarget) writer.Bool("abilityNoTarget", true);
        if (_hasPlace)
        {
            writer.Fixed("abilityPlaceX", _placeX);
            writer.Fixed("abilityPlaceY", _placeY);
        }
        writer.Bool("abilityActive", true);
        writer.Int("abilityState", (int)_packState);
        writer.Int("abilityAnim", _animFrames);
        writer.Int("abilityPrep", _prepFrames);
        writer.UInt("abilityTarget", _target.Value);
        writer.Bool("abilityRange", _withinRange);
        writer.Bool("abilityFacingStarted", _facingInitiated);
        writer.Bool("abilityFacingDone", _facingComplete);
        writer.Int("abilityApproaches", _approachOrders);
        if (_flashPhase != Fixed.Zero) writer.Fixed("abilityFlash", _flashPhase);
        if (_facePhase != 0) writer.Int("abilityFacePhase", _facePhase);
        writer.Int("nextWake", _nextWake);
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        ClearRun();
        _nextWake = SimulationTiming.SleepForever;
        LoadSpecialObjects(reader);
        if (!reader.Has("abilityActive"))
        {
            if (_specialObjects.Count > 0) _nextWake = reader.Int("nextWake");
            return;
        }
        _active = true;
        _noTarget = reader.Has("abilityNoTarget");
        if (reader.Has("abilityPlaceX")) (_hasPlace, _placeX, _placeY) = (true, reader.Fixed("abilityPlaceX"), reader.Fixed("abilityPlaceY"));
        var state = reader.Int("abilityState");
        if (state < 0 || state > (int)PackState.Unpacked) throw new InvalidDataException("Special ability pack state out of range.");
        _packState = (PackState)state;
        _animFrames = reader.Int("abilityAnim");
        _prepFrames = reader.Int("abilityPrep");
        _target = new EntityId(checked((uint)reader.UInt("abilityTarget")));
        _withinRange = reader.Bool("abilityRange");
        _facingInitiated = reader.Bool("abilityFacingStarted");
        _facingComplete = reader.Bool("abilityFacingDone");
        _approachOrders = checked((int)reader.Int("abilityApproaches"));
        _flashPhase = reader.Has("abilityFlash") ? reader.Fixed("abilityFlash") : Fixed.Zero;
        _facePhase = reader.Has("abilityFacePhase") ? checked((int)reader.Int("abilityFacePhase")) : 0;
        _nextWake = reader.Int("nextWake");
    }
}
