using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Simulation.Ids;

namespace CnCNew.Presentation.Fx;

/// <summary><c>Vector3Randomizer</c>'s kinds (<c>v3_rnd.cpp</c>): where a W3D emitter places a particle around itself and how it varies its velocity.</summary>
public enum EmitterVolumeKind { SolidBox, SolidSphere, HollowSphere, SolidCylinder }

/// <summary>A randomizer of a W3D emitter: a box of three extents, a sphere of a radius (<see cref="Value1"/>) or a cylinder of an extent and a radius.</summary>
public readonly record struct EmitterVolume(EmitterVolumeKind Kind, float Value1, float Value2, float Value3)
{
    /// <summary><c>Get_Vector</c>: components drawn in [-1, 1]; sphere and circle by rejection, as the original.</summary>
    public Vector3 Sample(FxRandom random)
    {
        float Unit() => random.NextReal(-1f, 1f);
        switch (Kind)
        {
            case EmitterVolumeKind.SolidBox:
                return new Vector3(Unit() * Value1, Unit() * Value2, Unit() * Value3);
            case EmitterVolumeKind.SolidSphere:
                for (var attempt = 0; ; attempt++)
                {
                    var vector = new Vector3(Unit(), Unit(), Unit()) * Value1;
                    if (vector.LengthSquared() <= Value1 * Value1 || attempt > 64) return vector;
                }
            case EmitterVolumeKind.HollowSphere:
                for (var attempt = 0; ; attempt++)
                {
                    var vector = new Vector3(Unit(), Unit(), Unit());
                    var length = vector.LengthSquared();
                    if (length is <= 1f and > 0f) return vector * (Value1 / MathF.Sqrt(length));
                    if (attempt > 64) return new Vector3(Value1, 0f, 0f);
                }
            default:
            {
                var x = Unit() * Value1;
                for (var attempt = 0; ; attempt++)
                {
                    var circle = new Vector2(Unit(), Unit()) * Value2;
                    if (circle.LengthSquared() <= Value2 * Value2 || attempt > 64) return new Vector3(x, circle.X, circle.Y);
                }
            }
        }
    }
}

/// <summary>A keyframed scalar of a W3D emitter (<c>ParticlePropertyStruct&lt;float&gt;</c>): the value at age 0, the random range per particle, keys in seconds of age.</summary>
public sealed record EmitterScalarKeys(float Start, float Random, ImmutableArray<(float Seconds, float Value)> Keys)
{
    public static EmitterScalarKeys Constant(float value) => new(value, 0f, []);
}

/// <summary>A keyframed colour of a W3D emitter: red, green and blue in 0..1.</summary>
public sealed record EmitterColorKeys(Vector3 Start, Vector3 Random, ImmutableArray<(float Seconds, Vector3 Value)> Keys);

/// <summary>
/// What a model that is a W3D particle emitter (<c>ParticleEmitterClass</c>, made by <c>WW3DAssetManager::Create_Render_Obj</c> from a file of one emitter) emits, in the file's own
/// units: seconds, units per second, units per second squared. <see cref="MaxEmissions"/> 0 is "no limit". <see cref="NotReproduced"/> names what of the definition this runtime does not
/// draw (the line render modes, a texture cut into frames, blur times) - counted and shown, never dropped silently.
/// </summary>
public sealed record ModelEmitterSpec(
    string Name,
    string Texture,
    ParticleShader Shader,
    float EmissionRate,
    uint BurstSize,
    int MaxEmissions,
    float Lifetime,
    EmitterVolume CreationVolume,
    EmitterVolume VelocityRandom,
    Vector3 Velocity,
    float OutwardVelocity,
    float VelocityInherit,
    Vector3 Acceleration,
    EmitterColorKeys Colors,
    EmitterScalarKeys Opacities,
    EmitterScalarKeys Sizes,
    EmitterScalarKeys Rotations,
    float OrientationRandom)
{
    public ImmutableArray<string> NotReproduced { get; init; } = [];
}

public readonly record struct ModelEmitterStats(int Emitters, int Emitting, int Particles, long Started, long ParticlesCreated, long NotReproducedFeatures);

/// <summary>
/// The W3D particle emitters that models are (<c>part_emt.cpp</c> <c>ParticleEmitterClass</c>, <c>part_buf.cpp</c> <c>ParticleBufferClass</c>), rewritten: an emitter is attached to an
/// object like its model and emits while its state shows it; its particles live in the world, not in the object, and finish their lives after the emitter stopped. The original runs these
/// on the W3D clock, which <c>W3DDisplay::draw</c> advances by <c>TheW3DFrameLengthInMsec</c> (<c>1000 / LOGICFRAMES_PER_SECOND</c> = 33 whole milliseconds) per drawn frame; here a
/// <see cref="Step"/> is that frame, and time is counted in whole milliseconds as there.
/// <list type="bullet">
/// <item><c>Create_New_Particles</c>: the frame's milliseconds are added to the remainder; for each whole emission interval (<c>1000 / EmissionRate</c>, truncated) a burst is made at the
/// emitter's transform interpolated between the last frame's and this one's, stamped with the moment inside the frame; <c>MaxEmissions</c> counts particles and ends the emitter.</item>
/// <item><c>Initialize_Particle</c>: a place from the creation volume, a velocity from the randomizer, the outward velocity along that place (or along X when it is the centre) and the
/// base velocity, turned by the emitter, plus the inherited velocity.</item>
/// <item><c>Get_New_Particles</c>, <c>Kill_Old_Particles</c>, <c>Update_Non_New_Particles</c>: a new particle is placed at its age under constant acceleration, an older one advanced by the
/// time since the last update; one at the lifetime dies; the buffer holds <c>BurstSize * EmissionRate * (Lifetime + 1)</c> (capped by <c>MaxEmissions</c>, at least 2) and a newer
/// particle overwrites the oldest.</item>
/// <item><c>Reset_Colors</c> .. <c>Reset_Rotations</c> and <c>Update_Visual_Particle_State</c>: colour, opacity, size and orientation are linear between keys in whole milliseconds, keys at or
/// beyond the lifetime cut the last segment toward them; the random offsets are tables of at most 32 entries indexed by the particle's place in the buffer; size is clamped at zero,
/// colour and opacity to 0..1.</item>
/// </list>
/// The original's level of detail (<c>DecimationThreshold</c>) is not reproduced. Random numbers come from a stream of this class's own.
/// </summary>
public sealed class ModelEmitterWorld(FxRandom random)
{
    /// <summary><c>TheW3DFrameLengthInMsec</c> (<c>W3DView.cpp</c>): the W3D clock's step per drawn frame, in whole milliseconds.</summary>
    public const uint FrameMilliseconds = 1000 / CnCNew.Data.OriginalFrame.PerSecond;

    private const int MaxRandomEntries = 32;

    /// <summary><c>Reset_Rotations</c>'s epsilons: a tenth of a degree of orientation, one rotation an hour.</summary>
    private const float RotationEpsilon = 2.77777778e-4f;
    private readonly List<Emitter> _emitters = [];
    private uint _syncTime;
    private int _nextId;
    private long _started, _particlesCreated, _notReproduced;

    public ModelEmitterStats Stats => new(_emitters.Count, _emitters.Count(emitter => emitter.Emitting), _emitters.Sum(emitter => emitter.Particles.Count), _started, _particlesCreated,
        _notReproduced);

    /// <summary>Whether any emitter has a particle to draw.</summary>
    public bool HasParticles => _emitters.Any(emitter => emitter.Particles.Count > 0);

    /// <summary>The names of the emitters that run now (emitting or finishing their particles), in creation order.</summary>
    public ImmutableArray<string> Running => [.. _emitters.Select(emitter => emitter.Spec.Name)];

    /// <summary>
    /// Starts an emitter attached to <paramref name="entity"/> at <paramref name="localOffset"/> in its model space. Returns its id (negative, apart from the particle systems' ids).
    /// </summary>
    public int Start(ModelEmitterSpec spec, EntityId entity, Vector3 localOffset)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var emitter = new Emitter(--_nextId, spec, entity, localOffset, random, _syncTime);
        _emitters.Add(emitter);
        _started++;
        _notReproduced += spec.NotReproduced.Length;
        return emitter.Id;
    }

    /// <summary>The emitter stops emitting (its model left the scene or the state that showed it); its particles live on to their ends.</summary>
    public void Stop(int id)
    {
        foreach (var emitter in _emitters)
            if (emitter.Id == id) emitter.Emitting = false;
    }

    /// <summary>Whether the emitter of this id still emits.</summary>
    public bool IsEmitting(int id) => _emitters.Any(emitter => emitter.Id == id && emitter.Emitting);

    /// <summary>One drawn frame of the W3D clock: every emitter emits for it and its particles move.</summary>
    public void Step(Func<EntityId, FxEntityPose?> poses)
    {
        ArgumentNullException.ThrowIfNull(poses);
        _syncTime += FrameMilliseconds;
        foreach (var emitter in _emitters)
        {
            var pose = emitter.Entity.IsValid ? poses(emitter.Entity) : null;
            if (pose is null) emitter.Emitting = false;
            _particlesCreated += emitter.Update(pose, _syncTime);
        }
        _emitters.RemoveAll(emitter => !emitter.Emitting && emitter.Particles.Count == 0 && emitter.Pending.Count == 0);
    }

    /// <summary>Adds the particles of every emitter to a frame's lists, oldest first per emitter, emitters in creation order.</summary>
    public void AppendTo(ImmutableArray<ParticleInstance>.Builder particles, List<ParticleBatchKey> batches, Func<Vector3, float, bool>? isVisible, ref int culled)
    {
        foreach (var emitter in _emitters)
        {
            if (emitter.Particles.Count == 0) continue;
            var key = new ParticleBatchKey(emitter.Spec.Texture, emitter.Spec.Shader, ParticleKind.Particle, false);
            var batch = batches.IndexOf(key);
            if (batch < 0) { batch = batches.Count; batches.Add(key); }
            foreach (var particle in emitter.Particles)
            {
                var (color, alpha, size, rotation) = emitter.Visual(particle, _syncTime);
                if (isVisible is not null && !isVisible(particle.Position, size)) { culled++; continue; }
                particles.Add(new ParticleInstance(particle.Position, size, new Vector4(color, alpha), rotation, batch, emitter.Id, particle.Serial));
            }
        }
    }

    private sealed class Particle
    {
        public uint Stamp;
        public Vector3 Position;
        public Vector3 Velocity;
        public int Slot;
        public uint Serial;
    }

    private readonly record struct NewParticle(uint Stamp, Vector3 Position, Vector3 Velocity);

    /// <summary>A keyframed value in whole milliseconds: <c>Reset_Size</c> and its siblings.</summary>
    private sealed class Track<T>(uint[] times, T[] values, T[] deltas, T[] randoms) where T : struct
    {
        public readonly uint[] Times = times;
        public readonly T[] Values = values;
        public readonly T[] Deltas = deltas;
        public readonly T[] Randoms = randoms;

        public int KeyAt(uint age)
        {
            var key = Times.Length - 1;
            while (age < Times[key]) key--;
            return key;
        }
    }

    private sealed class Emitter
    {
        public readonly int Id;
        public readonly ModelEmitterSpec Spec;
        public readonly EntityId Entity;
        private readonly Vector3 _offset;
        private readonly FxRandom _random;
        public bool Emitting = true;
        public readonly List<Particle> Particles = [];
        public readonly List<NewParticle> Pending = [];
        private bool _firstTime = true;
        private Quaternion _previousRotation = Quaternion.Identity;
        private Vector3 _previousOrigin;
        private uint _emitRemain;
        private int _particlesLeft;
        private bool _complete;
        private readonly uint _emitRate;
        private readonly uint _burst;
        private readonly uint _maxAge;
        private readonly int _maxNum;
        private readonly Vector3 _baseVelocity;
        private readonly float _outward;
        private readonly Vector3 _acceleration;
        private readonly bool _hasAcceleration;
        private uint _lastUpdate;
        private int _nextSlot;
        private uint _serial;
        private readonly Track<Vector3> _colors;
        private readonly Track<float> _opacities;
        private readonly Track<float> _sizes;
        private readonly uint[] _rotationTimes;
        private readonly float[] _rotationValues, _halfRotationDeltas, _orientationValues, _rotationRandoms, _orientationRandoms;

        public Emitter(int id, ModelEmitterSpec spec, EntityId entity, Vector3 offset, FxRandom random, uint now)
        {
            Id = id;
            Spec = spec;
            Entity = entity;
            _offset = offset;
            _random = random;
            _lastUpdate = now;
            _emitRate = spec.EmissionRate > 0f ? (uint)(1000f / spec.EmissionRate) : 1000u;
            _burst = spec.BurstSize != 0 ? spec.BurstSize : 1u;
            var lifetime = spec.Lifetime > 0f ? spec.Lifetime : 1f;
            _maxAge = (uint)(1000f * lifetime);
            _particlesLeft = spec.MaxEmissions;
            var maxNum = (int)(_burst * spec.EmissionRate * (lifetime + 1f));
            if (spec.MaxEmissions > 0) maxNum = Math.Min(maxNum, spec.MaxEmissions);
            _maxNum = Math.Max(maxNum, 2);
            _baseVelocity = spec.Velocity * 0.001f;
            _outward = spec.OutwardVelocity * 0.001f;
            _acceleration = spec.Acceleration / 1000000f;
            _hasAcceleration = _acceleration != Vector3.Zero;
            var entries = Math.Min((int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)_maxNum), MaxRandomEntries);
            _colors = BuildColors(spec.Colors, entries);
            _opacities = BuildScalars(spec.Opacities, entries, 0.0038f);
            _sizes = BuildScalars(spec.Sizes, entries, 1.0e-12f);
            (_rotationTimes, _rotationValues, _halfRotationDeltas, _orientationValues) = BuildRotations(spec.Rotations);
            _rotationRandoms = Table(MathF.Abs(spec.Rotations.Random) < RotationEpsilon ? 1 : entries, spec.Rotations.Random * 0.001f);
            _orientationRandoms = Table(MathF.Abs(spec.OrientationRandom) < RotationEpsilon ? 1 : entries, spec.OrientationRandom);
        }

        /// <summary>One frame: emit (<c>ParticleEmitterClass::Emit</c>), then <c>Update_Kinematic_Particle_State</c>. Returns how many particles were made.</summary>
        public int Update(FxEntityPose? pose, uint now)
        {
            var made = 0;
            if (pose is { } at)
            {
                var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, at.Heading);
                var origin = Vector3.Transform(_offset, at.Transform);
                if (Emitting && !_complete)
                {
                    if (_firstTime)
                    {
                        _previousRotation = rotation;
                        _previousOrigin = origin;
                        _firstTime = false;
                    }
                    made = CreateNewParticles(rotation, origin, now);
                }
                _previousRotation = rotation;
                _previousOrigin = origin;
            }

            var elapsed = now - _lastUpdate;
            if (elapsed == 0) return made;
            // Get_New_Particles: placed at their ages; ones already past the lifetime never enter the buffer.
            var fresh = new List<Particle>(Pending.Count);
            foreach (var created in Pending)
            {
                var age = now - created.Stamp;
                if (age >= _maxAge) continue;
                var time = (float)age;
                var particle = new Particle { Stamp = created.Stamp, Slot = _nextSlot, Serial = _serial++ };
                _nextSlot = (_nextSlot + 1) % _maxNum;
                if (_hasAcceleration)
                {
                    particle.Position = created.Position + (created.Velocity + 0.5f * _acceleration * time) * time;
                    particle.Velocity = created.Velocity + _acceleration * time;
                }
                else
                {
                    particle.Position = created.Position + created.Velocity * time;
                    particle.Velocity = created.Velocity;
                }
                fresh.Add(particle);
            }
            Pending.Clear();
            // Kill_Old_Particles, then Update_Non_New_Particles over the time since the last update.
            var dead = 0;
            while (dead < Particles.Count && now - Particles[dead].Stamp >= _maxAge) dead++;
            Particles.RemoveRange(0, dead);
            var dt = (float)elapsed;
            var deltaV = _acceleration * dt;
            var deltaP = _acceleration * (0.5f * dt * dt);
            foreach (var particle in Particles)
            {
                if (_hasAcceleration)
                {
                    particle.Position += particle.Velocity * dt + deltaP;
                    particle.Velocity += deltaV;
                }
                else particle.Position += particle.Velocity * dt;
            }
            Particles.AddRange(fresh);
            // The circular buffer: a newer particle overwrites the oldest.
            if (Particles.Count > _maxNum) Particles.RemoveRange(0, Particles.Count - _maxNum);
            _lastUpdate = now;
            return made;
        }

        private int CreateNewParticles(Quaternion rotation, Vector3 origin, uint now)
        {
            var frameTime = FrameMilliseconds;
            if (frameTime > 100 * _emitRate)
            {
                var bursts = (uint)_maxNum / Gcd((uint)_maxNum, _burst);
                var cycle = _emitRate * bursts;
                frameTime = cycle > 1 ? frameTime % cycle : 1;
            }
            _emitRemain += frameTime;
            var frame = (float)frameTime;
            var alpha = 1f - _emitRemain / frame;
            var deltaAlpha = _emitRate / frame;
            var inherited = Spec.VelocityInherit != 0f ? (origin - _previousOrigin) * (Spec.VelocityInherit / frame) : Vector3.Zero;
            var made = 0;
            while (_emitRemain > _emitRate)
            {
                _emitRemain -= _emitRate;
                alpha += deltaAlpha;
                var turn = Quaternion.Slerp(_previousRotation, rotation, alpha);
                var at = Vector3.Lerp(_previousOrigin, origin, alpha);
                var stamp = now - _emitRemain;
                var burst = _burst;
                var complete = false;
                if (_particlesLeft > 0)
                {
                    if (burst > (uint)_particlesLeft)
                    {
                        burst = (uint)_particlesLeft;
                        _particlesLeft = 0;
                    }
                    else _particlesLeft -= (int)burst;
                    complete = _particlesLeft <= 0;
                }
                for (var index = 0; index < burst; index++)
                {
                    Pending.Add(Initialize(stamp, turn, at, inherited));
                    if (Pending.Count > _maxNum) Pending.RemoveAt(0);
                    made++;
                }
                if (complete)
                {
                    // IsComplete: the emitter is done and leaves the scene once its particles are gone.
                    _complete = true;
                    Emitting = false;
                    break;
                }
            }
            return made;
        }

        private NewParticle Initialize(uint stamp, Quaternion turn, Vector3 at, Vector3 inherited)
        {
            var place = Spec.CreationVolume.Sample(_random);
            var velocity = Spec.VelocityRandom.Sample(_random) * 0.001f;
            if (_outward != 0f)
            {
                var length = place.LengthSquared();
                velocity += length != 0f ? place * (_outward / MathF.Sqrt(length)) : new Vector3(_outward, 0f, 0f);
            }
            velocity += _baseVelocity;
            return new NewParticle(stamp, Vector3.Transform(place, turn) + at, inherited + Vector3.Transform(velocity, turn));
        }

        /// <summary><c>Update_Visual_Particle_State</c> and <c>Combine_Color_And_Alpha</c> for one particle: colour, alpha, size, and the orientation in radians (256 steps a turn).</summary>
        public (Vector3 Color, float Alpha, float Size, float Rotation) Visual(Particle particle, uint now)
        {
            var age = now - particle.Stamp;
            var c = _colors.KeyAt(age);
            var color = _colors.Values[c] + _colors.Deltas[c] * (float)(age - _colors.Times[c]) + _colors.Randoms[particle.Slot & (_colors.Randoms.Length - 1)];
            var a = _opacities.KeyAt(age);
            var alpha = _opacities.Values[a] + _opacities.Deltas[a] * (float)(age - _opacities.Times[a]) + _opacities.Randoms[particle.Slot & (_opacities.Randoms.Length - 1)];
            var s = _sizes.KeyAt(age);
            var size = _sizes.Values[s] + _sizes.Deltas[s] * (float)(age - _sizes.Times[s]) + _sizes.Randoms[particle.Slot & (_sizes.Randoms.Length - 1)];
            var rotation = 0f;
            if (_rotationTimes.Length > 0)
            {
                var r = _rotationTimes.Length - 1;
                while (age < _rotationTimes[r]) r--;
                var dt = (float)(age - _rotationTimes[r]);
                var turns = _orientationValues[r] + (_rotationValues[r] + _halfRotationDeltas[r] * dt) * dt
                    + _rotationRandoms[particle.Slot & (_rotationRandoms.Length - 1)] * age + _orientationRandoms[particle.Slot & (_orientationRandoms.Length - 1)];
                rotation = (((int)(turns * 256f)) & 0xFF) * (MathF.PI * 2f / 256f);
            }
            return (Vector3.Clamp(color, Vector3.Zero, Vector3.One), Math.Clamp(alpha, 0f, 1f), Math.Max(size, 0f), rotation);
        }

        private float[] Table(int count, float range)
        {
            var table = new float[count];
            if (count > 1)
                for (var index = 0; index < count; index++) table[index] = _random.NextReal(-1f, 1f) * range;
            return table;
        }

        /// <summary>The keys that count: the start at 0 and each later key below the lifetime; whether the last segment is constant (no key reached the lifetime).</summary>
        private (uint[] Times, int Used, bool ConstantAtEnd) Times<T>(ImmutableArray<(float Seconds, T Value)> keys)
        {
            var used = 0;
            while (used < keys.Length && (uint)(keys[used].Seconds * 1000f) < _maxAge) used++;
            var times = new uint[used + 1];
            for (var index = 1; index <= used; index++) times[index] = (uint)(keys[index - 1].Seconds * 1000f);
            return (times, used, used == keys.Length);
        }

        private Track<Vector3> BuildColors(EmitterColorKeys keys, int entries)
        {
            var (times, used, constant) = Times(keys.Keys);
            var values = new Vector3[used + 1];
            values[0] = keys.Start;
            for (var index = 1; index <= used; index++) values[index] = keys.Keys[index - 1].Value;
            var deltas = new Vector3[used + 1];
            for (var index = 0; index < used; index++) deltas[index] = (values[index + 1] - values[index]) / (times[index + 1] - times[index]);
            deltas[used] = constant ? Vector3.Zero : (keys.Keys[used].Value - values[used]) / (keys.Keys[used].Seconds * 1000f - times[used]);
            var zero = MathF.Abs(keys.Random.X) < 0.0038f && MathF.Abs(keys.Random.Y) < 0.0038f && MathF.Abs(keys.Random.Z) < 0.0038f;
            var randoms = new Vector3[zero ? 1 : entries];
            if (!zero)
                for (var index = 0; index < randoms.Length; index++)
                    randoms[index] = new Vector3(_random.NextReal(-1f, 1f) * keys.Random.X, _random.NextReal(-1f, 1f) * keys.Random.Y, _random.NextReal(-1f, 1f) * keys.Random.Z);
            return new Track<Vector3>(times, values, deltas, randoms);
        }

        private Track<float> BuildScalars(EmitterScalarKeys keys, int entries, float epsilon)
        {
            var (times, used, constant) = Times(keys.Keys);
            var values = new float[used + 1];
            values[0] = keys.Start;
            for (var index = 1; index <= used; index++) values[index] = keys.Keys[index - 1].Value;
            var deltas = new float[used + 1];
            for (var index = 0; index < used; index++) deltas[index] = (values[index + 1] - values[index]) / (times[index + 1] - times[index]);
            deltas[used] = constant ? 0f : (keys.Keys[used].Value - values[used]) / (keys.Keys[used].Seconds * 1000f - times[used]);
            return new Track<float>(times, values, deltas, Table(MathF.Abs(keys.Random) < epsilon ? 1 : entries, keys.Random));
        }

        /// <summary><c>Reset_Rotations</c>: rotations per second become per millisecond, the orientation at each key is the integral of the rotation before it. No rotation at all: none.</summary>
        private (uint[], float[], float[], float[]) BuildRotations(EmitterScalarKeys keys)
        {
            if (MathF.Abs(Spec.OrientationRandom) < RotationEpsilon && MathF.Abs(keys.Random) < RotationEpsilon && keys.Keys.IsEmpty && MathF.Abs(keys.Start) < RotationEpsilon)
                return ([], [], [], []);
            var (times, used, constant) = Times(keys.Keys);
            var values = new float[used + 1];
            values[0] = keys.Start * 0.001f;
            for (var index = 1; index <= used; index++) values[index] = keys.Keys[index - 1].Value * 0.001f;
            var half = new float[used + 1];
            for (var index = 0; index < used; index++) half[index] = 0.5f * ((values[index + 1] - values[index]) / (times[index + 1] - times[index]));
            half[used] = constant ? 0f : 0.5f * (keys.Keys[used].Value * 0.001f - values[used]) / (keys.Keys[used].Seconds * 1000f - times[used]);
            var orientation = new float[used + 1];
            for (var index = 1; index <= used; index++)
            {
                var dt = (float)(times[index] - times[index - 1]);
                orientation[index] = orientation[index - 1] + dt * (values[index - 1] + half[index - 1] * dt);
            }
            return (times, values, half, orientation);
        }

        private static uint Gcd(uint a, uint b)
        {
            while (b != 0) (a, b) = (b, a % b);
            return a;
        }
    }
}
