using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Fx;

/// <summary>
/// The sounds the original plays where a state changes: the move (<c>AIInternalMoveToState::startMoveSound</c>), a worse condition (<c>ActiveBody::attemptDamage</c>), stealth
/// (<c>StealthUpdate::update</c>), a chute that opens, an ability that packs or unpacks, and a locomotive's clatter and whistle. The simulation raises no event for them, so they are read from the world once per logic frame of the
/// original. Loops are not played here: <c>SoundMoveLoop</c> and <c>PrepSoundLoop</c> need a voice that is ended with the state.
/// </summary>
public sealed partial class SoundCueDeriver
{
    private static readonly int StealthedBit = ObjectStatusBits.Index("STEALTHED");
    private static readonly int DetectedBit = ObjectStatusBits.Index("DETECTED");

    private readonly record struct Seen(bool Moving, int Damage, bool Stealthed, bool Detected, bool Opened, byte Pack, int Triggers, int TrackPoint = -1, long Whistle = -1, float Clatter = 0f, int Plan = 0);

    private readonly Dictionary<uint, Seen> _seen = [];

    /// <summary>The state of every object against the last look; what is new is seen and says nothing.</summary>
    public void Track(SimulationWorld world, IFxWorldView view, List<SoundCue> output)
    {
        foreach (var entity in world.Entities)
        {
            // What a container takes out of the world is not heard; a fire base's soldiers and a portable structure on its host are in it (Enclosure).
            if (Enclosure.IsEnclosedAnywhere(world, entity) || catalog.FindObject(entity.Template.Name) is not { } info) continue;
            var body = entity.Body as ActiveBody;
            var now = new Seen(entity.HasMoveTarget, body?.DamageState ?? BodyDamageStates.Pristine, entity.HasStatus(StealthedBit), entity.HasStatus(DetectedBit), false, 0, 0);
            foreach (var module in entity.Modules)
            {
                if (module is ParachuteContain chute) now = now with { Opened = chute.Opened };
                else if (module is SpecialAbilityUpdate ability) now = now with { Pack = ability.PackPhase, Triggers = ability.Triggers };
                else if (module is BattlePlanUpdate center) now = now with { Plan = ((int)center.Status << 3) | (int)center.CurrentPlan };
                else if (module is RailroadBehavior { IsLocomotive: true } train)
                    now = now with
                    {
                        TrackPoint = train.InTunnel ? -1 : train.CurrentPoint,
                        // m_clicketyClackSound.setVolume(conductorPullInfo.speed / 10): the conductor's distance per frame of the original, whatever the tick rate. A volume below zero is no sound.
                        Clatter = Math.Clamp(FixedConvert.ToSingle(train.ConductorSpeedPerFrame) / 10f, 0f, 1f),
                        Whistle = train.Conductor == RailroadBehavior.ConductorStates.WaitAtStation && train.WaitAtStationTimer == world.Time.ToOriginalFrames(train.Data.WaitAtStationTicks) / 4 ? train.WaitAtStationTimer : -1,
                    };
            }
            var known = _seen.TryGetValue(entity.Id.Value, out var was);
            _seen[entity.Id.Value] = now;
            if (!known || now == was) continue;
            void Play(string key, float? volume = null)
            {
                if (info.Sound(key) is { } name && view.GetPose(entity.Id) is { } pose)
                {
                    Add(output, world.Tick, name, SoundCueKind.Effect, pose.Position, entity.Id, pose.Owner, $"Object.{key}");
                    if (volume is { } set) output[^1] = output[^1] with { VolumeOverride = set };
                }
            }
            if (now.Moving && !was.Moving) Play(now.Damage > BodyDamageStates.Damaged ? "SoundMoveStartDamaged" : "SoundMoveStart");
            if (now.Damage > was.Damage)
            {
                if (now.Damage == BodyDamageStates.Damaged) Play("SoundOnDamaged");
                else if (now.Damage == BodyDamageStates.ReallyDamaged) Play("SoundOnReallyDamaged");
            }
            // StealthUpdate::update plays the same sound for going in and coming out; being found has its own.
            if (now.Stealthed != was.Stealthed) Play("SoundStealthOn");
            if (now.Detected && !was.Detected) Play("SoundStealthOff");
            if (now.Opened && !was.Opened) Play("ParachuteOpenSound");
            if (now.Pack != was.Pack)
            {
                if (now.Pack == SpecialAbilityUpdate.PhasePacking) Play("PackSound");
                else if (now.Pack == SpecialAbilityUpdate.PhaseUnpacking) Play("UnpackSound");
            }
            if (now.Triggers > was.Triggers) Play("TriggerSound");
            // BattlePlanUpdate::setStatus: a plan that unpacks sounds its unpack and its announcement, one that packs its pack sound (the search-and-destroy idle loop is a loop and is not played here).
            if (now.Plan != was.Plan && (now.Plan & 7) is >= 1 and <= 3)
            {
                var plan = (BattlePlanKind)(now.Plan & 7) switch { BattlePlanKind.Bombardment => "Bombardment", BattlePlanKind.HoldTheLine => "HoldTheLine", _ => "SearchAndDestroy" };
                var planStatus = (BattlePlanUpdate.TransitionStatus)(now.Plan >> 3);
                if (planStatus == BattlePlanUpdate.TransitionStatus.Unpacking)
                {
                    Play(plan + "PlanUnpackSoundName");
                    Play(plan + "AnnouncementName");
                }
                else if (planStatus == BattlePlanUpdate.TransitionStatus.Packing) Play(plan + "PlanPackSoundName");
            }
            // RailroadBehavior::updatePositionTrackDistance: a locomotive "just rode over a join IN the tracks"; a quarter of the station's wait before it leaves it whistles.
            if (now.TrackPoint >= 0 && now.TrackPoint != was.TrackPoint) Play("ClicketyClackSound", now.Clatter);
            if (now.Whistle >= 0 && was.Whistle < 0) Play("WhistleSound");
        }
        if (_seen.Count > 2 * world.Entities.Count + 64)
            foreach (var id in _seen.Keys.Where(id => world.FindEntity(new Simulation.Ids.EntityId(id)) is null).ToArray()) _seen.Remove(id);
    }
}
