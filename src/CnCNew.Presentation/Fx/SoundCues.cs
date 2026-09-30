using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;

namespace CnCNew.Presentation.Fx;

public enum SoundCueKind { Effect, Voice, Eva, Music, UiSound }

/// <summary>
/// An audio request by name. The audio layer resolves <see cref="EventName"/> (an <c>AudioEvent</c>, or an <c>EvaEvent</c> for
/// <see cref="SoundCueKind.Eva"/>) and decides audibility for the local player; <see cref="Player"/> is the player the reference would
/// address (<c>setPlayerIndex</c>), <see cref="PlayerId.System"/> when not player-specific. <see cref="Origin"/> names the rule.
/// </summary>
public sealed record SoundCue(long Tick, string EventName, SoundCueKind Kind, Vector3? Position, EntityId Entity, PlayerId Player, float Volume,
    int Priority, string Origin);

/// <summary>
/// Derives audio requests from simulation events, following the reference call sites (see <c>docs/PRESENTATION-FX.md</c> for the rule
/// table). FX-list <c>Sound</c> nuggets are emitted by <see cref="FxExecutor"/>, not here.
/// </summary>
public sealed class SoundCueDeriver(FxCatalog catalog)
{
    private const float UnderAttackDistance = 250f;
    private const long UnderAttackFrames = 30 * 10;
    private readonly List<(long Tick, string Template)> _pendingProduction = [];
    private readonly List<(PlayerId Player, long Tick, Vector3 Position)> _underAttack = [];

    public void Derive(SimEvent simEvent, IFxWorldView view, List<SoundCue> output)
    {
        var tick = simEvent.Tick;
        switch (simEvent.Kind)
        {
            case SimEventKind.WeaponFired:
                if (catalog.FindWeapon(simEvent.Text) is { } weapon && FxCatalog.IsAudible(weapon.FireSound) && view.GetPose(simEvent.Entity) is { } shooter)
                    Add(output, tick, weapon.FireSound!, SoundCueKind.Effect, shooter.Position, simEvent.Entity, shooter.Owner, "Weapon.FireSound");
                break;
            case SimEventKind.VoiceFearRequested:
                ObjectSound(output, simEvent, simEvent.Entity, view, "VoiceFear", SoundCueKind.Voice);
                break;
            case SimEventKind.ProductionCompleted:
                if (view.GetTemplateName((int)simEvent.Value) is { } produced) _pendingProduction.Add((tick, produced));
                break;
            case SimEventKind.EntitySpawned:
            {
                _pendingProduction.RemoveAll(entry => entry.Tick < tick);
                var index = _pendingProduction.FindIndex(entry => entry.Template.Equals(simEvent.Text, StringComparison.OrdinalIgnoreCase));
                if (index < 0) break;
                _pendingProduction.RemoveAt(index);
                ObjectSound(output, simEvent, simEvent.Entity, view, "VoiceCreated", SoundCueKind.Voice);
                if (view.GetPose(simEvent.Entity) is { } pose && catalog.FindObject(pose.TemplateName) is { } info
                    && info.UnitSpecificSounds.TryGetValue("VoiceCreate", out var perUnit) && FxCatalog.IsAudible(perUnit))
                    Add(output, tick, perUnit, SoundCueKind.Voice, pose.Position, simEvent.Entity, pose.Owner, "ProductionUpdate UnitSpecificSounds.VoiceCreate");
                break;
            }
            case SimEventKind.ConstructionCompleted:
                ObjectSound(output, simEvent, simEvent.Other, view, "VoiceTaskComplete", SoundCueKind.Voice);
                break;
            case SimEventKind.ConstructionStarted:
                Misc(output, tick, "MoneyWithdrawSound", OwnerOf(simEvent, view), "Money::withdraw");
                break;
            case SimEventKind.SupplyDeposited or SimEventKind.StructureSold or SimEventKind.ConstructionCancelled:
                if (simEvent.Value > 0) Misc(output, tick, "MoneyDepositSound", OwnerOf(simEvent, view), "Money::deposit");
                break;
            case SimEventKind.UpgradeCompleted:
                Upgrade(output, simEvent, view);
                break;
            case SimEventKind.SpecialPowerUsed:
                if (catalog.SpecialPowers.TryGetValue(simEvent.Text, out var power))
                {
                    var source = view.GetPose(simEvent.Entity);
                    if (power.Primary is { } initiate)
                        Add(output, tick, initiate, SoundCueKind.Effect, source?.Position, simEvent.Entity, source?.Owner ?? simEvent.Player, "SpecialPower.InitiateSound");
                    if (power.Secondary is { } atLocation && view.GetPose(simEvent.Other) is { } target)
                        Add(output, tick, atLocation, SoundCueKind.Effect, target.Position, simEvent.Other, source?.Owner ?? simEvent.Player, "SpecialPower.InitiateAtLocationSound");
                }
                break;
            case SimEventKind.EntityContained:
                ObjectSound(output, simEvent, simEvent.Other, view, "SoundEnter", SoundCueKind.Effect);
                break;
            case SimEventKind.EntityUncontained:
                ObjectSound(output, simEvent, simEvent.Other, view, "SoundExit", SoundCueKind.Effect);
                break;
            case SimEventKind.EntityDied:
                Died(output, simEvent, view);
                break;
            case SimEventKind.EntityDamaged:
                Damaged(output, simEvent, view);
                break;
            case SimEventKind.CommandRejected:
                Misc(output, tick, "NoCanDoSound", simEvent.Player, "CommandRejected (own rule: NoCanDoSound)");
                break;
        }
    }

    private void Upgrade(List<SoundCue> output, SimEvent simEvent, IFxWorldView view)
    {
        var pose = view.GetPose(simEvent.Entity);
        var player = pose?.Owner ?? simEvent.Player;
        catalog.Upgrades.TryGetValue(simEvent.Text, out var upgrade);
        if (upgrade?.Primary is { } research)
            Add(output, simEvent.Tick, research, SoundCueKind.Effect, pose?.Position, simEvent.Entity, player, "Upgrade.ResearchSound");
        else
            Add(output, simEvent.Tick, "UpgradeComplete", SoundCueKind.Eva, null, simEvent.Entity, player, "ProductionUpdate EVA_UpgradeComplete");
        if (upgrade?.Secondary is { } unitSpecific)
            Add(output, simEvent.Tick, unitSpecific, SoundCueKind.Effect, pose?.Position, simEvent.Entity, player, "Upgrade.UnitSpecificSound");
    }

    private void Died(List<SoundCue> output, SimEvent simEvent, IFxWorldView view)
    {
        if (view.GetPose(simEvent.Entity) is not { } pose || pose.Owner.IsSystem || simEvent.Other == simEvent.Entity) return;
        if (catalog.FindObject(pose.TemplateName) is not { } info) return;
        if (info.IsKindOf("STRUCTURE") && info.IsKindOf("MP_COUNT_FOR_VICTORY"))
            Add(output, simEvent.Tick, "BuildingLost", SoundCueKind.Eva, pose.Position, simEvent.Entity, pose.Owner, "Object::onDie EVA_BuldingLost");
        else if (info.IsKindOf("INFANTRY") || info.IsKindOf("VEHICLE"))
            Add(output, simEvent.Tick, "UnitLost", SoundCueKind.Eva, pose.Position, simEvent.Entity, pose.Owner, "Object::onDie EVA_UnitLost");
    }

    /// <summary><c>Radar::tryUnderAttackEvent</c>: one notification per player per 250 units and 10 seconds.</summary>
    private void Damaged(List<SoundCue> output, SimEvent simEvent, IFxWorldView view)
    {
        if (simEvent.Value <= 0 || view.GetPose(simEvent.Entity) is not { } victim || victim.Owner.IsSystem) return;
        if (view.GetPose(simEvent.Other) is { } attacker && attacker.Owner == victim.Owner) return;
        if (catalog.FindObject(victim.TemplateName) is not { } info) return;
        var tick = simEvent.Tick;
        _underAttack.RemoveAll(entry => tick - entry.Tick >= UnderAttackFrames);
        if (_underAttack.Any(entry => entry.Player == victim.Owner
                && Vector2.DistanceSquared(new Vector2(entry.Position.X, entry.Position.Y), new Vector2(victim.Position.X, victim.Position.Y)) <= UnderAttackDistance * UnderAttackDistance))
            return;
        _underAttack.Add((victim.Owner, tick, victim.Position));
        if (info.IsKindOf("INFANTRY") || info.IsKindOf("VEHICLE"))
            // The reference plays the structure sound for non-harvester units (Radar.cpp).
            Misc(output, tick, info.IsKindOf("HARVESTER") ? "RadarNotifyHarvesterUnderAttackSound" : "RadarNotifyStructureUnderAttackSound", victim.Owner, "Radar::tryUnderAttackEvent");
        else if (info.IsKindOf("STRUCTURE") && info.IsKindOf("MP_COUNT_FOR_VICTORY"))
        {
            Add(output, tick, "BaseUnderAttack", SoundCueKind.Eva, victim.Position, simEvent.Entity, victim.Owner, "Radar::tryUnderAttackEvent EVA_BaseUnderAttack");
            Misc(output, tick, "RadarNotifyStructureUnderAttackSound", victim.Owner, "Radar::tryUnderAttackEvent");
        }
        else Misc(output, tick, "RadarNotifyUnderAttackSound", victim.Owner, "Radar::tryUnderAttackEvent");
    }

    private void ObjectSound(List<SoundCue> output, SimEvent simEvent, EntityId entity, IFxWorldView view, string key, SoundCueKind kind)
    {
        if (view.GetPose(entity) is not { } pose || catalog.FindObject(pose.TemplateName)?.Sound(key) is not { } name) return;
        Add(output, simEvent.Tick, name, kind, pose.Position, entity, pose.Owner, $"Object.{key}");
    }

    private static PlayerId OwnerOf(SimEvent simEvent, IFxWorldView view)
        => !simEvent.Player.IsSystem ? simEvent.Player : view.GetPose(simEvent.Entity)?.Owner ?? PlayerId.System;

    private void Misc(List<SoundCue> output, long tick, string key, PlayerId player, string origin)
    {
        if (catalog.MiscAudio.TryGetValue(key, out var name) && FxCatalog.IsAudible(name))
            Add(output, tick, name, SoundCueKind.UiSound, null, EntityId.Invalid, player, $"MiscAudio.{key} ({origin})");
    }

    internal void Add(List<SoundCue> output, long tick, string name, SoundCueKind kind, Vector3? position, EntityId entity, PlayerId player, string origin)
    {
        var info = kind == SoundCueKind.Eva ? null : catalog.FindAudioEvent(name);
        output.Add(new SoundCue(tick, name, kind, position, entity, player, info?.Volume ?? 1f, info?.Priority ?? 2, origin));
    }
}

/// <summary>Limits of <see cref="SoundCueRateLimiter"/>; a <see cref="CutoffDistance"/> of 0 disables the distance test.</summary>
public sealed record SoundCueLimits(int MaxPerEventName = 3, long WindowTicks = 10, float CutoffDistance = 0f, int MaxPerTick = 48);

/// <summary>
/// Keeps big fights from spawning thousands of sounds: per event name at most <see cref="SoundCueLimits.MaxPerEventName"/> cues per
/// <see cref="SoundCueLimits.WindowTicks"/> ticks, at most <see cref="SoundCueLimits.MaxPerTick"/> cues per tick, and positional
/// effect/voice cues farther than <see cref="SoundCueLimits.CutoffDistance"/> (2D) from the listener are dropped. Deterministic:
/// decisions depend only on the cue order and ticks. EVA, UI and music cues ignore the distance test.
/// </summary>
public sealed class SoundCueRateLimiter(SoundCueLimits limits)
{
    private readonly Dictionary<string, Queue<long>> _recent = new(StringComparer.OrdinalIgnoreCase);
    private long _currentTick = long.MinValue;
    private int _acceptedThisTick;

    public SoundCueLimits Limits { get; } = limits;
    public long Accepted { get; private set; }
    public long DroppedByRate { get; private set; }
    public long DroppedByDistance { get; private set; }
    public long DroppedByTickCap { get; private set; }

    public ImmutableArray<SoundCue> Filter(IEnumerable<SoundCue> cues, Vector3? listener = null)
    {
        var accepted = ImmutableArray.CreateBuilder<SoundCue>();
        foreach (var cue in cues)
        {
            if (cue.Tick != _currentTick) { _currentTick = cue.Tick; _acceptedThisTick = 0; }
            if (Limits.CutoffDistance > 0f && listener is { } ear && cue.Position is { } at && cue.Kind is SoundCueKind.Effect or SoundCueKind.Voice
                && Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(ear.X, ear.Y)) > Limits.CutoffDistance)
            {
                DroppedByDistance++;
                continue;
            }
            if (!_recent.TryGetValue(cue.EventName, out var times)) _recent[cue.EventName] = times = new Queue<long>();
            while (times.Count > 0 && times.Peek() <= cue.Tick - Limits.WindowTicks) times.Dequeue();
            if (times.Count >= Limits.MaxPerEventName) { DroppedByRate++; continue; }
            if (_acceptedThisTick >= Limits.MaxPerTick) { DroppedByTickCap++; continue; }
            times.Enqueue(cue.Tick);
            _acceptedThisTick++;
            Accepted++;
            accepted.Add(cue);
        }
        return accepted.ToImmutable();
    }
}
