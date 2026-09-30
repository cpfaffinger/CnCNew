using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;

namespace CnCNew.Presentation.Fx;

/// <summary>The position form of an FX invocation (<c>FXList::doFXPos(primary, primaryMtx, primarySpeed, secondary, overrideRadius)</c>).</summary>
public sealed record FxInvocation(Vector3 Primary)
{
    public Matrix4x4? PrimaryTransform { get; init; }
    /// <summary>Distance per frame (the weapon speed for fire effects; 0 otherwise).</summary>
    public float PrimarySpeed { get; init; }
    public Vector3? Secondary { get; init; }
    /// <summary>The caller's radius for <c>UseCallersRadius</c> particle nuggets (the weapon's primary damage radius).</summary>
    public float OverrideRadius { get; init; }
}

/// <summary>
/// Executes FXLists and the FX-bearing simulation events into renderer-neutral data: particle systems in a <see cref="ParticleWorld"/>,
/// tracers, ray requests, light pulses, view-shake requests, scorch decals and <see cref="SoundCue"/>s. Nothing calls back into a
/// renderer: callers run <see cref="ExecuteEvents"/> after each simulation tick, <see cref="Update"/> once per logic frame, and read
/// <see cref="Capture"/>/<see cref="DrainSoundCues"/>. Every skipped or approximated case is counted in <see cref="Counters"/>.
/// </summary>
public sealed class FxExecutor
{
    private const int MaxNesting = 8;
    private const int MaxBonesPerCall = 40;
    private const int MaxLights = 64;
    private readonly FxCatalog _catalog;
    private readonly FxRandom _random;
    private readonly SoundCueDeriver _deriver;
    private readonly List<TracerState> _tracers = [];
    private readonly List<LightState> _lights = [];
    private readonly List<RayEffectRequest> _rays = [];
    private readonly List<ViewShakeRequest> _shakes = [];
    private readonly List<SoundCue> _cues = [];
    private readonly SortedDictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, (string DamageType, long NextTick)> _damageThrottle = [];
    private readonly Dictionary<(uint Source, uint Target), string> _tickWeaponsByPair = [];
    private readonly Dictionary<uint, string> _tickWeaponsBySource = [];
    private long _currentTick;

    public FxExecutor(FxCatalog catalog, ulong seed, ParticleBudget? budget = null)
    {
        _catalog = catalog;
        _random = new FxRandom(seed);
        Particles = new ParticleWorld(budget ?? catalog.Settings.Budget, _random, catalog.FindParticleSystem);
        Decals = new DecalBuffer();
        _deriver = new SoundCueDeriver(catalog);
    }

    public FxCatalog Catalog => _catalog;
    public ParticleWorld Particles { get; }
    public DecalBuffer Decals { get; }
    public FxRandom Random => _random;
    public long Frame => Particles.Frame;

    /// <summary>Counters of executed, skipped and approximated cases (sorted by name).</summary>
    public ImmutableSortedDictionary<string, long> Counters => _counters.ToImmutableSortedDictionary(StringComparer.Ordinal);

    // -------------------------------------------------------------------------------------------- events

    /// <summary>
    /// Presents one tick's events in order. Weapon shots of the tick are indexed first so that <c>EntityDamaged</c> can pick the
    /// damage type for <c>DamageFX</c> (the event itself does not carry it).
    /// </summary>
    public void ExecuteEvents(IReadOnlyList<SimEvent> events, IFxWorldView view)
    {
        _tickWeaponsByPair.Clear();
        _tickWeaponsBySource.Clear();
        foreach (var simEvent in events)
            if (simEvent.Kind == SimEventKind.WeaponFired)
            {
                _tickWeaponsBySource[simEvent.Entity.Value] = simEvent.Text;
                if (simEvent.Other.IsValid) _tickWeaponsByPair[(simEvent.Entity.Value, simEvent.Other.Value)] = simEvent.Text;
            }
        foreach (var simEvent in events) Execute(simEvent, view);
    }

    public void Execute(SimEvent simEvent, IFxWorldView view)
    {
        _currentTick = simEvent.Tick;
        switch (simEvent.Kind)
        {
            case SimEventKind.FxRequested:
                // FXListDie and friends: OrientToObject (Value 1) uses the object form, otherwise the position form at the object.
                if (simEvent.Value == 1) ExecuteFxListAtEntity(simEvent.Text, simEvent.Entity, simEvent.Other, view);
                else if (view.GetPose(simEvent.Entity) is { } pose) ExecuteFxListAtPosition(simEvent.Text, new FxInvocation(pose.Position), view);
                else Count("fx-request-without-pose");
                break;
            case SimEventKind.WeaponFired:
                WeaponFired(simEvent, view);
                break;
            case SimEventKind.EntityDamaged:
                Damaged(simEvent, view);
                break;
        }
        _deriver.Derive(simEvent, view, _cues);
    }

    private void WeaponFired(SimEvent simEvent, IFxWorldView view)
    {
        if (_catalog.FindWeapon(simEvent.Text) is not { } weapon) { Count("weapon-unknown"); return; }
        if (view.GetPose(simEvent.Entity) is not { } source) { Count("weapon-source-without-pose"); return; }
        var level = Math.Clamp(source.Veterancy, 0, 3);
        Vector3? target = view.GetPose(simEvent.Other)?.Position;
        if (weapon.FireFx[level] is { } fire)
        {
            // Weapon::fireWeaponTemplate: without a drawable muzzle the effect plays at the drawable (or the target for contact weapons).
            var muzzle = view.GetWeaponMuzzle(simEvent.Entity, weapon.Name);
            if (muzzle is null) Count("weapon-fx-without-muzzle");
            var primary = weapon.IsContact && target is { } contact ? contact : muzzle ?? source.Position;
            ExecuteFxListAtPosition(fire, new FxInvocation(primary)
            {
                PrimaryTransform = source.Transform,
                PrimarySpeed = weapon.WeaponSpeed,
                Secondary = target,
                OverrideRadius = weapon.PrimaryDamageRadius,
            }, view);
        }
        if (weapon.ProjectileObject is not null && weapon.ProjectileDetonationFx[level] is { } detonation && target is { } impact)
        {
            // The simulation resolves shots instantly (no projectile objects yet): the detonation effect plays at the target now.
            Count("projectile-detonation-immediate");
            ExecuteFxListAtPosition(detonation, new FxInvocation(impact) { OverrideRadius = weapon.PrimaryDamageRadius }, view);
        }
    }

    /// <summary><c>ActiveBody::doDamageFX</c> with the victim's unconditional armor set's DamageFX.</summary>
    private void Damaged(SimEvent simEvent, IFxWorldView view)
    {
        if (simEvent.Value == 0) return;
        if (view.GetPose(simEvent.Entity) is not { } victim || _catalog.FindObject(victim.TemplateName) is not { DamageFx: { } dfxName }) return;
        if (!_catalog.DamageFx.TryGetValue(dfxName, out var table)) { Count("damagefx-missing"); return; }
        string? weaponName = null;
        if (simEvent.Other.IsValid && !_tickWeaponsByPair.TryGetValue((simEvent.Other.Value, simEvent.Entity.Value), out weaponName))
            _tickWeaponsBySource.TryGetValue(simEvent.Other.Value, out weaponName);
        if (_catalog.FindWeapon(weaponName) is not { } weapon) { Count("damagefx-without-weapon"); return; }
        var damageType = weapon.DamageType;
        if (_damageThrottle.TryGetValue(simEvent.Entity.Value, out var last) && last.DamageType.Equals(damageType, StringComparison.OrdinalIgnoreCase)
            && last.NextTick > simEvent.Tick)
        {
            Count("damagefx-throttled");
            return;
        }
        var sourceLevel = Math.Clamp(view.GetPose(simEvent.Other)?.Veterancy ?? 0, 0, 3);
        var (major, minor, amount, throttle) = table.Resolve(damageType, sourceLevel);
        _damageThrottle[simEvent.Entity.Value] = (damageType, simEvent.Tick + throttle);
        var damage = simEvent.Value / 65536f;
        var fx = damage >= amount ? major : minor;
        if (fx is not null) ExecuteFxListAtEntity(fx, simEvent.Entity, simEvent.Other, view);
    }

    // ------------------------------------------------------------------------------------------- FXLists

    /// <summary><c>FXList::doFXPos</c>. Unknown names are counted, never thrown.</summary>
    public void ExecuteFxListAtPosition(string name, FxInvocation invocation, IFxWorldView view)
        => RunPosition(name, invocation, view, 0);

    /// <summary><c>FXList::doFXObj(primary, secondary)</c>: the primary receives the effect, the secondary may be the one dealing it.</summary>
    public void ExecuteFxListAtEntity(string name, EntityId primary, EntityId secondary, IFxWorldView view)
    {
        if (!FxCatalog.IsNamed(name)) return;
        if (_catalog.FindFxList(name) is not { } list) { Count("fxlist-missing"); return; }
        if (view.GetPose(primary) is not { } pose) { Count("fxlist-primary-without-pose"); return; }
        if (view.IsShrouded(pose.Position)) { Count("fxlist-shrouded"); return; }
        Count("fxlist-executed");
        var secondaryPose = view.GetPose(secondary);
        foreach (var nugget in list.Nuggets)
        {
            switch (nugget)
            {
                case SoundFxNugget sound:
                    Sound(sound, pose.Position, primary, pose.Owner);
                    break;
                case LightPulseFxNugget light:
                    Light(light, pose.Position, light.RadiusAsPercentOfObjectSize > 0f ? pose.BoundingRadius * light.RadiusAsPercentOfObjectSize : light.Radius);
                    break;
                case ParticleSystemFxNugget particles when particles.Ricochet && secondaryPose is { } dealer:
                {
                    var aim = MathF.Atan2(pose.Position.Y - dealer.Position.Y, pose.Position.X - dealer.Position.X);
                    SpawnParticles(particles, pose.Position, Matrix4x4.CreateRotationZ(aim), primary, 0f, view);
                    break;
                }
                case ParticleSystemFxNugget particles:
                    SpawnParticles(particles, pose.Position, pose.Transform, primary, 0f, view);
                    break;
                case FxListAtBonePosFxNugget bones:
                    AtBones(bones, primary, pose, view, 0);
                    break;
                default:
                    // FXNugget::doFXObj: position form with the object's transform, speed 0 and the secondary's position.
                    PositionNugget(nugget, new FxInvocation(pose.Position) { PrimaryTransform = pose.Transform, Secondary = secondaryPose?.Position },
                        EntityId.Invalid, pose.Owner, view);
                    break;
            }
        }
    }

    private void RunPosition(string name, FxInvocation invocation, IFxWorldView view, int depth)
    {
        if (!FxCatalog.IsNamed(name)) return;
        if (_catalog.FindFxList(name) is not { } list) { Count("fxlist-missing"); return; }
        if (view.IsShrouded(invocation.Primary)) { Count("fxlist-shrouded"); return; }
        Count("fxlist-executed");
        foreach (var nugget in list.Nuggets) PositionNugget(nugget, invocation, EntityId.Invalid, PlayerId.System, view);
    }

    private void PositionNugget(FxNugget nugget, FxInvocation invocation, EntityId attachTo, PlayerId player, IFxWorldView view)
    {
        var primary = invocation.Primary;
        switch (nugget)
        {
            case SoundFxNugget sound:
                Sound(sound, primary, EntityId.Invalid, player);
                break;
            case TracerFxNugget tracer:
                Tracer(tracer, invocation);
                break;
            case RayEffectFxNugget ray:
                if (invocation.Secondary is not { } end) { Count("rayeffect-without-secondary"); break; }
                if (_catalog.FindObject(ray.TemplateName) is null) { Count("rayeffect-template-missing"); break; }
                _rays.Add(new RayEffectRequest(ray.TemplateName, primary + ray.PrimaryOffset, end + ray.SecondaryOffset));
                break;
            case LightPulseFxNugget light:
                Light(light, primary, light.Radius);
                break;
            case ViewShakeFxNugget shake:
                _shakes.Add(new ViewShakeRequest(shake.Type, primary));
                break;
            case TerrainScorchFxNugget scorch:
            {
                var type = scorch.Type == ScorchType.Random ? _random.NextInt((int)ScorchType.Scorch1, (int)ScorchType.Scorch4) : (int)scorch.Type;
                if (!Decals.AddScorch(primary, scorch.Radius, type, Frame)) Count("scorch-duplicate");
                break;
            }
            case ParticleSystemFxNugget particles:
                SpawnParticles(particles, primary, invocation.PrimaryTransform, attachTo, invocation.OverrideRadius, view);
                break;
            case FxListAtBonePosFxNugget:
                // FXListAtBonePosFXNugget::doFXPos is a DEBUG_CRASH no-op: bones need the object form.
                Count("bonepos-without-object");
                break;
        }
    }

    private void Sound(SoundFxNugget sound, Vector3 position, EntityId entity, PlayerId player)
    {
        if (!FxCatalog.IsAudible(sound.EventName)) return;
        _deriver.Add(_cues, _currentTick, sound.EventName, SoundCueKind.Effect, position, entity, player, "FXList.Sound");
    }

    /// <summary><c>TracerFXNugget::doFXPos</c>: the lifetime is the estimated flight time scaled by <c>DecayAt</c>.</summary>
    private void Tracer(TracerFxNugget tracer, FxInvocation invocation)
    {
        if (tracer.Probability <= _random.NextReal(0f, 1f)) { Count("tracer-probability-skip"); return; }
        if (invocation.Secondary is not { } target) { Count("tracer-without-secondary"); return; }
        if (_catalog.FindObject(tracer.TracerName) is null) { Count("tracer-template-missing"); return; }
        var delta = target - invocation.Primary;
        if (delta == Vector3.Zero) { Count("tracer-zero-length"); return; }
        var speed = tracer.Speed == 0f ? invocation.PrimarySpeed : tracer.Speed;
        var distance = delta.Length() - tracer.Length;
        float frames;
        if (speed <= 0f)
        {
            // The reference divides by zero here; one frame is the documented stand-in.
            Count("tracer-zero-speed");
            frames = 1f;
        }
        else frames = distance >= 0f ? distance / speed : 1f;
        var lifetime = (long)MathF.Ceiling(frames * tracer.DecayAt);
        _tracers.Add(new TracerState(invocation.Primary, Vector3.Normalize(delta), tracer.Length, tracer.Width, tracer.Color, speed, Frame + lifetime));
        Count("tracer-created");
    }

    /// <summary><c>W3DDisplay::createLightPulse(pos, color, 1, radius, inc, dec)</c>: skipped when <c>1 + radius &lt; 2 * 10 + 1</c>.</summary>
    private void Light(LightPulseFxNugget light, Vector3 position, float radius)
    {
        const float innerRadius = 1f;
        if (innerRadius + radius < 2f * 10f + 1f) { Count("lightpulse-below-visible-radius"); return; }
        if (_lights.Count >= MaxLights) { _lights.RemoveAt(0); Count("lightpulse-evicted"); }
        _lights.Add(new LightState(position, light.Color, innerRadius, innerRadius + radius, light.IncreaseFrames, light.DecreaseFrames));
        Count("lightpulse-created");
    }

    /// <summary><c>ParticleSystemFXNugget::reallyDoFX</c>.</summary>
    private void SpawnParticles(ParticleSystemFxNugget nugget, Vector3 primary, Matrix4x4? transform, EntityId attachTo, float overrideRadius, IFxWorldView view)
    {
        if (_catalog.FindParticleSystem(nugget.Name) is not { } template) { Count("particle-system-missing"); return; }
        var offset = transform is { } rotate ? Vector3.TransformNormal(nugget.Offset, rotate) : nugget.Offset;
        for (var index = 0; index < nugget.Count; index++)
        {
            var radius = nugget.Radius.Sample(_random);
            var angle = _random.NextReal(0f, 2f * MathF.PI);
            var position = new Vector3(primary.X + offset.X + radius * MathF.Cos(angle), primary.Y + offset.Y + radius * MathF.Sin(angle), primary.Z + offset.Z);
            position.Z = nugget.CreateAtGroundHeight ? view.GroundHeight(position.X, position.Y) : position.Z + nugget.Height.Sample(_random);
            Matrix4x4? orientation = nugget.OrientToObject && transform is { } objectTransform ? objectTransform : null;
            if (nugget.RotateX != 0f) orientation = Matrix4x4.CreateRotationX(nugget.RotateX) * (orientation ?? Matrix4x4.Identity);
            if (nugget.RotateY != 0f) orientation = Matrix4x4.CreateRotationY(nugget.RotateY) * (orientation ?? Matrix4x4.Identity);
            if (nugget.RotateZ != 0f) orientation = Matrix4x4.CreateRotationZ(nugget.RotateZ) * (orientation ?? Matrix4x4.Identity);
            var delay = nugget.InitialDelay.Sample(_random);
            var spawn = new ParticleSpawn(position)
            {
                Orientation = orientation,
                AttachTo = nugget.AttachToObject ? attachTo : EntityId.Invalid,
                InitialDelayFrames = delay >= 0f ? FxValueReader.MillisecondsToFrames(delay) : null,
                EmissionRadius = nugget.UseCallersRadius && overrideRadius != 0f ? overrideRadius : null,
            };
            Count(Particles.Spawn(template, spawn) == 0 ? "particle-system-rejected" : "particle-system-created");
        }
    }

    /// <summary><c>FXListAtBonePosFXNugget::doFxAtBones</c>: the bone itself, then <c>Bone01</c>, <c>Bone02</c>, ... until one is missing.</summary>
    private void AtBones(FxListAtBonePosFxNugget nugget, EntityId entity, FxEntityPose pose, IFxWorldView view, int depth)
    {
        if (depth >= MaxNesting) { Count("fxlist-nesting-limit"); return; }
        var found = 0;
        for (var start = 0; start <= 1; start++)
        {
            var last = start == 0 ? 0 : 99;
            for (var index = start; index <= last && found < MaxBonesPerCall; index++)
            {
                var bone = index == 0 ? nugget.BoneName : $"{nugget.BoneName}{index:00}";
                if (view.GetBonePosition(entity, bone) is not { } position) break;
                found++;
                // The bone matrix is not available; the object's orientation stands in (OrientToBone is unused by the reference).
                RunPosition(nugget.FxListName, new FxInvocation(position) { PrimaryTransform = pose.Transform with { Translation = position } }, view, depth + 1);
            }
        }
        if (found == 0) Count("bonepos-no-bones");
    }

    // ------------------------------------------------------------------------------------------- frames

    /// <summary>Advances particles, tracers, lights and decal lifetimes by one logic frame.</summary>
    public void Update(IFxWorldView view)
    {
        Particles.Step(view.GetPose, view.GroundHeight);
        var frame = Frame;
        foreach (var tracer in _tracers) tracer.Advance(frame);
        _tracers.RemoveAll(tracer => frame >= tracer.ExpireFrame);
        foreach (var light in _lights) light.Advance();
        _lights.RemoveAll(light => !light.Enabled);
        Decals.Expire(frame);
    }

    /// <summary>The current renderable state; ray and shake requests are handed out once.</summary>
    public FxFrame Capture(Func<Vector3, float, bool>? isVisible = null)
    {
        var frame = new FxFrame(Frame, Particles.Capture(isVisible),
            [.._tracers.Select(tracer => tracer.ToInstance())],
            [.._lights.Select(light => light.ToInstance())],
            Decals.Snapshot(), [.._rays], [.._shakes]);
        _rays.Clear();
        _shakes.Clear();
        return frame;
    }

    /// <summary>All sound cues since the last drain, in event order.</summary>
    public ImmutableArray<SoundCue> DrainSoundCues()
    {
        var cues = _cues.ToImmutableArray();
        _cues.Clear();
        return cues;
    }

    private void Count(string key) => _counters[key] = _counters.GetValueOrDefault(key) + 1;

    private sealed class TracerState(Vector3 head, Vector3 direction, float length, float width, Vector3 color, float speed, long expireFrame)
    {
        private Vector3 _head = head;
        private float _opacity = 1f;
        public long ExpireFrame { get; } = expireFrame;

        /// <summary><c>W3DTracerDraw::doDrawModule</c>: fade by opacity / frames left, then move forward.</summary>
        public void Advance(long frame)
        {
            if (ExpireFrame > frame) _opacity -= _opacity / (ExpireFrame - frame);
            _head += direction * speed;
        }

        public TracerInstance ToInstance() => new(_head, direction, length, width, color, _opacity, speed, ExpireFrame);
    }

    private sealed class LightState(Vector3 position, Vector3 color, float inner, float outer, uint increaseFrames, uint decayFrames)
    {
        private readonly uint _increase = increaseFrames, _decay = decayFrames;
        private uint _increaseLeft = increaseFrames, _decayLeft = decayFrames;
        private float _factor = 1f;
        public bool Enabled { get; private set; } = true;

        /// <summary><c>W3DDynamicLight::On_Frame_Update</c> (a zero decay never fades).</summary>
        public void Advance()
        {
            if (_increaseLeft > 0 && _increase > 0)
            {
                _increaseLeft--;
                _factor = (_increase - _increaseLeft) / (float)_increase;
            }
            else if (_decay == 0) _factor = 1f;
            else
            {
                _decayLeft--;
                if (_decayLeft == 0) { Enabled = false; return; }
                _factor = _decayLeft / (float)_decay;
            }
        }

        public LightPulseInstance ToInstance() => new(position, color * _factor, inner, MathF.Max(inner, _factor * outer), _factor);
    }
}
