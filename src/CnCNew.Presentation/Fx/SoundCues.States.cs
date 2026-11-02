using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Fx;

/// <summary>
/// The sounds the original plays where a state changes: the move (<c>AIInternalMoveToState::startMoveSound</c>), a worse condition (<c>ActiveBody::attemptDamage</c>), stealth
/// (<c>StealthUpdate::update</c>), a chute that opens and an ability that packs or unpacks. The simulation raises no event for them, so they are read from the world once per logic frame of the
/// original. Loops are not played here: <c>SoundMoveLoop</c> and <c>PrepSoundLoop</c> need a voice that is ended with the state.
/// </summary>
public sealed partial class SoundCueDeriver
{
    private static readonly int StealthedBit = ObjectStatusBits.Index("STEALTHED");
    private static readonly int DetectedBit = ObjectStatusBits.Index("DETECTED");

    private readonly record struct Seen(bool Moving, int Damage, bool Stealthed, bool Detected, bool Opened, byte Pack, int Triggers);

    private readonly Dictionary<uint, Seen> _seen = [];

    /// <summary>The state of every object against the last look; what is new is seen and says nothing.</summary>
    public void Track(SimulationWorld world, IFxWorldView view, List<SoundCue> output)
    {
        foreach (var entity in world.Entities)
        {
            if (entity.ContainedBy.IsValid || catalog.FindObject(entity.Template.Name) is not { } info) continue;
            var body = entity.Body as ActiveBody;
            var now = new Seen(entity.HasMoveTarget, body?.DamageState ?? BodyDamageStates.Pristine, entity.HasStatus(StealthedBit), entity.HasStatus(DetectedBit), false, 0, 0);
            foreach (var module in entity.Modules)
            {
                if (module is ParachuteContain chute) now = now with { Opened = chute.Opened };
                else if (module is SpecialAbilityUpdate ability) now = now with { Pack = ability.PackPhase, Triggers = ability.Triggers };
            }
            var known = _seen.TryGetValue(entity.Id.Value, out var was);
            _seen[entity.Id.Value] = now;
            if (!known || now == was) continue;
            void Play(string key)
            {
                if (info.Sound(key) is { } name && view.GetPose(entity.Id) is { } pose) Add(output, world.Tick, name, SoundCueKind.Effect, pose.Position, entity.Id, pose.Owner, $"Object.{key}");
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
        }
        if (_seen.Count > 2 * world.Entities.Count + 64)
            foreach (var id in _seen.Keys.Where(id => world.FindEntity(new Simulation.Ids.EntityId(id)) is null).ToArray()) _seen.Remove(id);
    }
}
