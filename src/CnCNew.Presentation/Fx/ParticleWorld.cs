using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Simulation.Ids;

namespace CnCNew.Presentation.Fx;

/// <summary>How a particle system is placed (<c>ParticleSystemFXNugget::reallyDoFX</c> after <c>createParticleSystem</c>).</summary>
public sealed record ParticleSpawn(Vector3 Position)
{
    /// <summary>The local rotation (<c>setLocalTransform</c> + <c>rotateLocalTransformX/Y/Z</c>); its translation is ignored.</summary>
    public Matrix4x4? Orientation { get; init; }

    /// <summary>When valid, the system follows this entity (<c>attachToObject</c>) instead of using <see cref="Position"/>.</summary>
    public EntityId AttachTo { get; init; }

    /// <summary><c>setInitialDelay</c> in frames; null keeps the template's <c>InitialDelay</c> sample.</summary>
    public uint? InitialDelayFrames { get; init; }

    /// <summary><c>setEmissionVolumeSphereRadius</c>/<c>setEmissionVolumeCylinderRadius</c> (<c>UseCallersRadius</c>); 0 or null keeps the template.</summary>
    public float? EmissionRadius { get; init; }
}

/// <summary>One renderable particle. <see cref="Color"/> is RGBA in 0..1; <see cref="Batch"/> indexes <see cref="ParticleFrame.Batches"/>.</summary>
public readonly record struct ParticleInstance(Vector3 Position, float Size, Vector4 Color, float Rotation, int Batch, int SystemId, uint Personality);

/// <summary>What a renderer needs to group particles: texture (or drawable name), blend mode, kind and billboard/ground alignment.</summary>
public sealed record ParticleBatchKey(string Texture, ParticleShader Shader, ParticleKind Kind, bool GroundAligned);

/// <summary>
/// One frame of particles in a stable order: systems by ascending id (creation order), particles by ascending personality
/// (creation order inside the system); at most <see cref="ParticleBudget.MaxParticlesPerSystem"/> per system like the W3D point group.
/// </summary>
public sealed record ParticleFrame(long Frame, ImmutableArray<ParticleInstance> Particles, ImmutableArray<ParticleBatchKey> Batches, int Culled);

public readonly record struct ParticleWorldStats(
    long Frame, int Systems, int Particles, long SystemsCreated, long ParticlesCreated, long RejectedBelowMinPriority,
    long RejectedFieldCap, long RejectedBudget, long RejectedPerSystemCap, long EvictedForBudget, long RejectedSystemCap, long MissingTemplates);

/// <summary>
/// A CPU particle simulation re-implementing <c>ParticleSys.cpp</c> (<c>ParticleSystem::update</c>, <c>Particle::update</c>,
/// <c>generateParticleInfo</c>, <c>computeParticlePosition/Velocity</c>, <c>mergeRelatedParticleSystems</c>, <c>updateWindMotion</c>,
/// <c>ParticleSystemManager::update/createParticle/removeOldestParticles</c>). One <see cref="Step"/> is one logic frame (1/30 s), exactly
/// like the reference, which advances particles once per logic frame. Float math, but every random number comes from the injected
/// <see cref="FxRandom"/>, iteration is over lists in creation order and nothing depends on hashing, so equal inputs give equal bytes.
/// </summary>
public sealed class ParticleWorld
{
    private const int MaxSlaveDepth = 8;
    private readonly List<SystemState> _systems = [];
    private readonly LinkedList<Particle>[] _byPriority;
    private readonly FxRandom _random;
    private readonly Func<string, ParticleSystemTemplate?> _templates;
    private int _nextSystemId;
    private int _particleCount;
    private int _fieldParticlesLastFrame;
    private long _frame;
    private long _systemsCreated, _particlesCreated, _rejectedPriority, _rejectedField, _rejectedBudget, _rejectedPerSystem, _evicted,
        _rejectedSystems, _missingTemplates;

    public ParticleWorld(ParticleBudget budget, FxRandom random, Func<string, ParticleSystemTemplate?> templates)
    {
        Budget = budget;
        _random = random;
        _templates = templates;
        _byPriority = new LinkedList<Particle>[Enum.GetValues<ParticlePriority>().Length];
        for (var index = 0; index < _byPriority.Length; index++) _byPriority[index] = new LinkedList<Particle>();
    }

    public ParticleBudget Budget { get; }
    public long Frame => _frame;
    public int ParticleCount => _particleCount;
    public int SystemCount => _systems.Count;

    public ParticleWorldStats Stats => new(_frame, _systems.Count, _particleCount, _systemsCreated, _particlesCreated, _rejectedPriority,
        _rejectedField, _rejectedBudget, _rejectedPerSystem, _evicted, _rejectedSystems, _missingTemplates);

    /// <summary>Creates a system (with its slave chain) and places it. Returns the system id, or 0 when the system cap rejected it.</summary>
    public int Spawn(ParticleSystemTemplate template, ParticleSpawn spawn)
    {
        var system = CreateSystem(template, 0);
        if (system is null) return 0;
        if (spawn.Orientation is { } orientation)
        {
            system.Local = orientation with { M41 = 0f, M42 = 0f, M43 = 0f };
            system.LocalIdentity = false;
        }
        if (spawn.AttachTo.IsValid) system.Attached = spawn.AttachTo;
        else
        {
            system.Local.Translation = spawn.Position;
            system.LocalIdentity = false;
        }
        if (spawn.InitialDelayFrames is { } delay) system.DelayLeft = delay;
        if (spawn.EmissionRadius is { } radius && radius != 0f
            && system.Template.VolumeType is EmissionVolumeType.Sphere or EmissionVolumeType.Cylinder)
            system.Volume[0] = radius;
        return system.Id;
    }

    /// <summary><c>ParticleSystem::destroy</c>: stop emitting, disappear once the particles are gone.</summary>
    public void Destroy(int systemId)
    {
        foreach (var system in _systems)
            if (system.Id == systemId && !system.Removed) system.Destroy();
    }

    /// <summary>
    /// Advances every system by one logic frame in creation order (<c>ParticleSystemManager::update</c>). Systems created during the
    /// step (per-particle attached systems) are appended and updated in the same step, as with the reference's list iteration.
    /// </summary>
    public void Step(Func<EntityId, FxEntityPose?>? poses = null, Func<float, float, float>? groundHeight = null)
    {
        for (var index = 0; index < _systems.Count; index++)
        {
            var system = _systems[index];
            if (system.Removed) continue;
            if (!UpdateSystem(system, poses, groundHeight)) Remove(system);
        }
        _systems.RemoveAll(system => system.Removed);
        _frame++;
    }

    /// <summary>Builds the renderable frame; <paramref name="isVisible"/> (position, size) is the frustum/distance culling hook.</summary>
    public ParticleFrame Capture(Func<Vector3, float, bool>? isVisible = null)
    {
        var instances = ImmutableArray.CreateBuilder<ParticleInstance>(_particleCount);
        var batches = new List<ParticleBatchKey>();
        var culled = 0;
        var field = 0;
        // Slaves join the update list before their master (reference constructor order); the frame is ordered by id instead.
        foreach (var system in _systems.OrderBy(value => value.Id))
        {
            if (system.Removed) continue;
            var template = system.Template;
            var batch = -1;
            var count = 0;
            foreach (var particle in system.Particles)
            {
                if (particle.Dead) continue;
                if (isVisible is not null && !isVisible(particle.Position, particle.Size)) { culled++; continue; }
                if (template.Priority == ParticlePriority.AreaEffect && template.IsGroundAligned) field++;
                if (batch < 0)
                {
                    var key = new ParticleBatchKey(template.ParticleName, template.Shader, template.Kind, template.IsGroundAligned);
                    batch = batches.IndexOf(key);
                    if (batch < 0) { batch = batches.Count; batches.Add(key); }
                }
                var color = particle.Color;
                instances.Add(new ParticleInstance(particle.Position, particle.Size, new Vector4(color, particle.Alpha), particle.AngleZ, batch,
                    system.Id, particle.Personality));
                if (++count == Budget.MaxParticlesPerSystem) break;
            }
        }
        _fieldParticlesLastFrame = field;
        return new ParticleFrame(_frame, instances.ToImmutable(), [..batches], culled);
    }

    // ------------------------------------------------------------------------------------------------ systems

    private SystemState? CreateSystem(ParticleSystemTemplate template, int depth)
    {
        if (_systems.Count >= Budget.MaxSystems) { _rejectedSystems++; return null; }
        var system = new SystemState(++_nextSystemId, template)
        {
            DelayLeft = ToUnsigned(template.InitialDelay.Sample(_random)),
            LifetimeLeft = template.SystemLifetime,
            Forever = template.SystemLifetime == 0,
            // ParticleSystem::ParticleSystem rescales ColorScale by 1/255 through setRange, whose default distribution is UNIFORM.
            ColorScale = new FxRandomVariable(template.ColorScale.Low / 255f, template.ColorScale.High / 255f, FxDistribution.Uniform),
            WindAngleChange = 0.15f,
            WindToEnd = true,
        };
        system.WindStart = _random.NextReal(template.WindPingPongStartAngleMin, template.WindPingPongStartAngleMax);
        system.WindEnd = _random.NextReal(template.WindPingPongEndAngleMin, template.WindPingPongEndAngleMax);
        system.WindAngle = _random.NextReal(system.WindStart, system.WindEnd);
        _systemsCreated++;
        if (FxCatalog.IsNamed(template.SlaveSystem) && depth < MaxSlaveDepth)
        {
            if (_templates(template.SlaveSystem) is { } slaveTemplate)
            {
                // The reference constructs the slave inside the master's constructor, so the slave joins the system list first.
                var slave = CreateSystem(slaveTemplate, depth + 1);
                if (slave is not null)
                {
                    system.Slave = slave;
                    slave.Master = system;
                }
            }
            else _missingTemplates++;
        }
        _systems.Add(system);
        return system;
    }

    private void Remove(SystemState system)
    {
        system.Removed = true;
        if (system.Slave is { } slave)
        {
            // Deviation: ~ParticleSystem only unlinks the slave, which would then emit on its own at the world origin; destroy it instead.
            slave.Master = null;
            slave.Destroy();
            system.Slave = null;
        }
        if (system.Master is { } master) master.Slave = null;
        foreach (var particle in system.Particles)
            if (!particle.Dead) Kill(particle);
        system.Particles.Clear();
        if (system.ControlParticle is { } control) control.Controlled = null;
        system.ControlParticle = null;
    }

    private bool UpdateSystem(SystemState system, Func<EntityId, FxEntityPose?>? poses, Func<float, float, float>? groundHeight)
    {
        var template = system.Template;
        if (system.DelayLeft > 0)
        {
            system.DelayLeft--;
            return true;
        }
        if (template.WindMotion != ParticleWindMotion.NotUsed) UpdateWind(system);

        Matrix4x4? parent = null;
        if (system.Attached.IsValid)
        {
            if (poses?.Invoke(system.Attached) is { } pose)
            {
                parent = pose.Transform;
                system.LastPos = system.Pos;
                system.Pos = pose.Position;
                system.AttachedPosition = pose.Position;
            }
            else
            {
                system.Attached = EntityId.Invalid;
                system.AttachedPosition = Vector3.Zero;
                system.Destroy();
            }
        }
        if (parent is { } parentTransform)
        {
            system.Transform = system.LocalIdentity ? parentTransform : system.Local * parentTransform;
            system.Identity = false;
        }
        else if (!system.LocalIdentity)
        {
            system.Transform = system.Local;
            system.Identity = false;
        }
        else system.Identity = true;

        if (system.ControlParticle is { } control)
        {
            system.Transform.Translation = control.Position;
            system.Identity = false;
            system.LastPos = system.Pos;
            system.Pos = control.Position;
        }

        if (!system.Destroyed && (system.Forever || system.LifetimeLeft > 0) && system.Master is null)
        {
            if (system.BurstDelayLeft == 0)
            {
                var priority = template.Priority;
                var count = (int)template.BurstCount.Sample(_random);
                for (var index = 0; index < count; index++)
                {
                    var info = GenerateInfo(system, index, count);
                    if (template.IsEmitAboveGroundOnly && info.Position.Z < (groundHeight?.Invoke(info.Position.X, info.Position.Y) ?? 0f)) continue;
                    var particle = CreateParticle(system, info, priority);
                    if (particle is null) continue;
                    if (FxCatalog.IsNamed(template.PerParticleAttachedSystem))
                    {
                        if (_templates(template.PerParticleAttachedSystem) is { } attachedTemplate)
                        {
                            if (CreateSystem(attachedTemplate, 0) is { } attached)
                            {
                                attached.ControlParticle = particle;
                                particle.Controlled = attached;
                            }
                        }
                        else _missingTemplates++;
                    }
                    if (system.Slave is { } slave)
                        CreateParticle(slave, Merge(system, slave), priority);
                }
                system.BurstDelayLeft = ToUnsigned(template.BurstDelay.Sample(_random));
            }
            else system.BurstDelayLeft--;
        }

        var gravity = new Vector3(0f, 0f, template.Gravity);
        foreach (var particle in system.Particles)
        {
            if (particle.Dead) continue;
            if (template.Gravity != 0f) particle.Acceleration += gravity;
            if (!UpdateParticle(system, particle, _frame)) Kill(particle);
        }
        system.Particles.RemoveAll(particle => particle.Dead);

        if (system.Destroyed && system.LiveCount == 0) return false;
        if (!system.Forever)
        {
            if (system.LifetimeLeft > 0) system.LifetimeLeft--;
            if (system.LiveCount > 0) return true;
            if (system.LifetimeLeft == 0) return false;
        }
        return true;
    }

    private void UpdateWind(SystemState system)
    {
        var template = system.Template;
        switch (template.WindMotion)
        {
            case ParticleWindMotion.PingPong:
            {
                var start = system.WindStart;
                var end = system.WindEnd;
                var halfSpan = (end - start) / 2f;
                var fromCenter = MathF.Abs(halfSpan - system.WindAngle + start);
                var change = (1f - fromCenter / halfSpan) * system.WindAngleChange;
                if (change < 0.005f) change = 0.005f;
                var turn = system.WindToEnd ? system.WindAngle + change >= end : system.WindAngle - change <= start;
                system.WindAngle += system.WindToEnd ? change : -change;
                if (turn)
                {
                    system.WindToEnd = !system.WindToEnd;
                    system.WindAngleChange = _random.NextReal(template.WindAngleChangeMin, template.WindAngleChangeMax);
                    system.WindStart = _random.NextReal(template.WindPingPongStartAngleMin, template.WindPingPongStartAngleMax);
                    system.WindEnd = _random.NextReal(template.WindPingPongEndAngleMin, template.WindPingPongEndAngleMax);
                }
                break;
            }
            case ParticleWindMotion.Circular:
                if (system.WindAngleChange == 0f) system.WindAngleChange = _random.NextReal(template.WindAngleChangeMin, template.WindAngleChangeMax);
                system.WindAngle += system.WindAngleChange;
                if (system.WindAngle > 2f * MathF.PI) system.WindAngle -= 2f * MathF.PI;
                else if (system.WindAngle < 0f) system.WindAngle += 2f * MathF.PI;
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------- particles

    private Particle? CreateParticle(SystemState system, in ParticleInfo info, ParticlePriority priority)
    {
        if (priority < Budget.MinPriority) { _rejectedPriority++; return null; }
        var template = system.Template;
        if (system.LiveCount > 0 && priority == ParticlePriority.AreaEffect && template.IsGroundAligned && _fieldParticlesLastFrame > Budget.MaxFieldParticles)
        {
            _rejectedField++;
            return null;
        }
        if (priority != ParticlePriority.AlwaysRender)
        {
            if (Budget.MaxParticles <= 0) { _rejectedBudget++; return null; }
            // Documented deviation from removeOldestParticles' off-by-one: keep the total at or below the cap by evicting the oldest
            // particle of a strictly lower priority (lowest priority list first); without such a victim the new particle is refused.
            if (_particleCount >= Budget.MaxParticles && !EvictOldest(priority)) { _rejectedBudget++; return null; }
        }
        if (system.LiveCount >= Budget.MaxParticlesPerSystem) { _rejectedPerSystem++; return null; }

        var particle = new Particle(system, info, _frame, system.PersonalityStore++);
        system.Particles.Add(particle);
        system.LiveCount++;
        particle.PriorityNode = _byPriority[(int)template.Priority].AddLast(particle);
        _particleCount++;
        _particlesCreated++;
        return particle;
    }

    private bool EvictOldest(ParticlePriority cap)
    {
        for (var priority = (int)ParticlePriority.WeaponExplosion; priority < (int)cap; priority++)
            if (_byPriority[priority].First is { } oldest)
            {
                Kill(oldest.Value);
                _evicted++;
                return true;
            }
        return false;
    }

    private void Kill(Particle particle)
    {
        if (particle.Dead) return;
        particle.Dead = true;
        if (particle.PriorityNode is { } node) node.List!.Remove(node);
        particle.PriorityNode = null;
        particle.System.LiveCount--;
        _particleCount--;
        if (particle.Controlled is { } controlled)
        {
            controlled.ControlParticle = null;
            controlled.Destroy();
            particle.Controlled = null;
        }
    }

    private static bool UpdateParticle(SystemState system, Particle p, long frame)
    {
        var template = system.Template;
        p.Velocity += p.Acceleration;
        p.Velocity *= p.VelocityDamping;
        p.Position += p.Velocity + template.DriftVelocity;
        if (template.WindMotion != ParticleWindMotion.NotUsed) ApplyWind(system, p);

        p.AngleZ += p.AngularRate;
        p.AngularRate *= p.AngularDamping;
        if (p.UpTowardsEmitter)
            p.AngleZ = AngleBetween(new Vector2(0f, 1f), new Vector2(p.Position.X - p.EmitterPosition.X, p.Position.Y - p.EmitterPosition.Y)) + MathF.PI;

        p.Size += p.SizeRate;
        p.SizeRate *= p.SizeRateDamping;

        var elapsed = frame - p.CreatedFrame;
        var alphaKeys = template.AlphaKeys;
        if (template.Shader != ParticleShader.Additive)
        {
            p.Alpha += p.AlphaRate;
            if (p.AlphaTarget < ParticleSystemTemplate.MaxKeyframes && alphaKeys[p.AlphaTarget].Frame != 0)
            {
                if (elapsed >= alphaKeys[p.AlphaTarget].Frame)
                {
                    p.Alpha = p.AlphaValues[p.AlphaTarget];
                    p.AlphaTarget++;
                    p.ComputeAlphaRate();
                }
            }
            else p.AlphaRate = 0f;
            p.Alpha = Math.Clamp(p.Alpha, 0f, 1f);
        }

        var colorKeys = template.ColorKeys;
        p.Color += p.ColorRate;
        if (p.ColorTarget < ParticleSystemTemplate.MaxKeyframes && colorKeys[p.ColorTarget].Frame != 0)
        {
            // The reference does not snap to the key color here (ColorScale would be lost), it only retargets.
            if (elapsed >= colorKeys[p.ColorTarget].Frame)
            {
                p.ColorTarget++;
                p.ComputeColorRate();
            }
        }
        else p.ColorRate = Vector3.Zero;
        p.Color += new Vector3(p.ColorScale);
        p.Color = ClampLikeReference(p.Color);

        p.Acceleration = Vector3.Zero;
        if (p.LifetimeLeft != 0 && --p.LifetimeLeft == 0) return false;
        return !p.IsInvisible();
    }

    /// <summary>
    /// <c>Particle::update</c>'s clamp, including its typo: the green lower bound tests red (<c>if (m_color.red &lt; 0) m_color.green = 0</c>),
    /// so a negative green survives unless red is negative too. Kept so particles tint exactly like the original.
    /// </summary>
    private static Vector3 ClampLikeReference(Vector3 color)
    {
        var redNegative = color.X < 0f;
        var red = redNegative ? 0f : color.X > 1f ? 1f : color.X;
        var green = redNegative ? 0f : color.Y > 1f ? 1f : color.Y;
        var blue = color.Z < 0f ? 0f : color.Z > 1f ? 1f : color.Z;
        return new Vector3(red, green, blue);
    }

    private static void ApplyWind(SystemState system, Particle p)
    {
        const float fullForceDistance = 75f;
        const float noForceDistance = 200f;
        var systemPos = system.Local.Translation + system.AttachedPosition;
        var distance = (p.Position - systemPos).Length();
        if (distance >= noForceDistance) return;
        var strength = 2f * p.WindRandomness;
        if (distance > fullForceDistance) strength *= 1f - (distance - fullForceDistance) / (noForceDistance - fullForceDistance);
        p.Position += new Vector3(MathF.Cos(system.WindAngle) * strength, MathF.Sin(system.WindAngle) * strength, 0f);
    }

    /// <summary>The file-local <c>angleBetween</c> of <c>ParticleSys.cpp</c>.</summary>
    private static float AngleBetween(Vector2 a, Vector2 b)
    {
        var lengthA = a.Length();
        var lengthB = b.Length();
        if (lengthA == 0f || lengthB == 0f) return 0f;
        var dot = a.X * b.X + a.Y * b.Y;
        if (dot == 0f) return b.X > 0f ? MathF.PI : 0f;
        var theta = MathF.Acos(Math.Clamp(dot / (lengthA * lengthB), -1f, 1f));
        return b.X > 0f ? theta : -theta;
    }

    // ------------------------------------------------------------------------------------------- emission

    private ParticleInfo GenerateInfo(SystemState system, int particleNum, int particleCount)
    {
        var template = system.Template;
        var info = new ParticleInfo { Position = ComputePosition(system) };
        info.Velocity = ComputeVelocity(system, info.Position);
        if (!system.Identity)
        {
            if (system.FirstPos)
            {
                system.LastPos = system.Pos;
                system.FirstPos = false;
            }
            var adjustment = (1f - (float)particleNum / particleCount) * (system.Pos - system.LastPos);
            info.Position = Vector3.Transform(info.Position, system.Transform) - adjustment;
            info.Velocity = Vector3.TransformNormal(info.Velocity, system.Transform);
        }
        info.VelocityDamping = template.VelocityDamping.Sample(_random);
        info.AngularDamping = template.AngularDamping.Sample(_random);
        info.AngleZ = template.AngleZ.Sample(_random);
        info.AngularRate = template.AngularRateZ.Sample(_random);
        info.Lifetime = ToUnsigned(template.Lifetime.Sample(_random));
        var scale = Budget.ParticleScale;
        info.Size = template.StartSize.Sample(_random) * scale;
        info.SizeRate = template.SizeRate.Sample(_random) * scale;
        info.SizeRateDamping = template.SizeRateDamping.Sample(_random);
        info.Size += system.AccumulatedSizeBonus;
        system.AccumulatedSizeBonus += template.StartSizeRate.Sample(_random);
        if (system.AccumulatedSizeBonus != 0f) system.AccumulatedSizeBonus = MathF.Min(system.AccumulatedSizeBonus, 50f);
        info.AlphaValues = new float[ParticleSystemTemplate.MaxKeyframes];
        for (var index = 0; index < ParticleSystemTemplate.MaxKeyframes; index++)
            info.AlphaValues[index] = template.AlphaKeys[index].Value.Sample(_random);
        info.ColorScale = system.ColorScale.Sample(_random);
        info.EmitterPosition = system.Transform.Translation;
        info.UpTowardsEmitter = template.IsParticleUpTowardsEmitter;
        info.WindRandomness = _random.NextReal(0.7f, 1.3f);
        return info;
    }

    /// <summary><c>mergeRelatedParticleSystems(master, slave, false)</c>: the master's placement with the slave's look.</summary>
    private ParticleInfo Merge(SystemState master, SystemState slave)
    {
        var merged = GenerateInfo(master, 1, 1);
        var own = GenerateInfo(slave, 1, 1);
        merged.Lifetime = own.Lifetime;
        merged.Size *= own.Size;
        merged.SizeRate *= own.SizeRate;
        merged.SizeRateDamping *= own.SizeRateDamping;
        merged.AngleZ = own.AngleZ;
        merged.AngularRate = own.AngularRate;
        merged.AngularDamping = own.AngularDamping;
        merged.AlphaValues = own.AlphaValues;
        merged.ColorScale = own.ColorScale;
        merged.Position += slave.Template.SlavePositionOffset;
        return merged;
    }

    private Vector3 ComputePointOnUnitSphere()
    {
        Vector3 point;
        do
        {
            point = new Vector3(_random.NextReal(-1f, 1f), _random.NextReal(-1f, 1f), _random.NextReal(-1f, 1f));
        }
        while (point == Vector3.Zero);
        return Vector3.Normalize(point);
    }

    private Vector3 ComputePosition(SystemState system)
    {
        var template = system.Template;
        var volume = system.Volume;
        Vector3 position;
        switch (template.VolumeType)
        {
            case EmissionVolumeType.Cylinder:
            {
                var angle = _random.NextReal(0f, 2f * MathF.PI);
                var radius = template.IsHollow ? volume[0] : _random.NextReal(0f, volume[0]);
                var halfLength = volume[1] / 2f;
                position = new Vector3(radius * MathF.Cos(angle), radius * MathF.Sin(angle), _random.NextReal(-halfLength, halfLength));
                break;
            }
            case EmissionVolumeType.Sphere:
            {
                var radius = template.IsHollow ? volume[0] : _random.NextReal(0f, volume[0]);
                position = ComputePointOnUnitSphere() * radius;
                break;
            }
            case EmissionVolumeType.Box when !template.IsHollow:
                position = new Vector3(_random.NextReal(-volume[0], volume[0]), _random.NextReal(-volume[1], volume[1]), _random.NextReal(-volume[2], volume[2]));
                break;
            case EmissionVolumeType.Box:
                // The hollow BOX branch of computeParticlePosition has no break and falls into the LINE case, which overwrites the
                // result with a point on the union's line (box half size to the unused line end). The draws are replayed for parity.
                _ = _random.NextInt(0, 6);
                _ = _random.NextReal(0f, 1f);
                _ = _random.NextReal(0f, 1f);
                position = PointOnLine(volume);
                break;
            case EmissionVolumeType.Line:
                position = PointOnLine(volume);
                break;
            default:
                position = Vector3.Zero;
                break;
        }
        return position * (0.5f + Budget.ParticleScale / 2f);
    }

    private Vector3 PointOnLine(float[] volume)
    {
        var start = new Vector3(volume[0], volume[1], volume[2]);
        var end = new Vector3(volume[3], volume[4], volume[5]);
        return start + _random.NextReal(0f, 1f) * (end - start);
    }

    private Vector3 ComputeVelocity(SystemState system, Vector3 position)
    {
        var template = system.Template;
        Vector3 velocity;
        switch (template.VelocityType)
        {
            case EmissionVelocityType.Ortho:
                velocity = new Vector3(template.OrthoX.Sample(_random), template.OrthoY.Sample(_random), template.OrthoZ.Sample(_random));
                break;
            case EmissionVelocityType.Cylindrical:
            {
                var radial = template.CylindricalRadial.Sample(_random);
                var angle = _random.NextReal(0f, 2f * MathF.PI);
                velocity = new Vector3(radial * MathF.Cos(angle), radial * MathF.Sin(angle), template.CylindricalNormal.Sample(_random));
                break;
            }
            case EmissionVelocityType.Outward:
            {
                var speed = template.OutwardSpeed.Sample(_random);
                var other = template.OutwardOtherSpeed.Sample(_random);
                switch (template.VolumeType)
                {
                    case EmissionVolumeType.Cylinder:
                    {
                        var disk = SafeNormalize(new Vector2(position.X, position.Y));
                        velocity = new Vector3(speed * disk.X, speed * disk.Y, other);
                        break;
                    }
                    case EmissionVolumeType.Box or EmissionVolumeType.Sphere:
                        velocity = SafeNormalize(position) * speed;
                        break;
                    case EmissionVolumeType.Line:
                    {
                        var volume = system.Volume;
                        var along = SafeNormalize(new Vector3(volume[3] - volume[0], volume[4] - volume[1], volume[5] - volume[2]));
                        var perpendicular = Vector3.Cross(Vector3.UnitZ, along);
                        var up = Vector3.Cross(along, perpendicular);
                        velocity = speed * perpendicular + other * up;
                        break;
                    }
                    case EmissionVolumeType.Point:
                        velocity = ComputePointOnUnitSphere() * speed;
                        break;
                    default:
                        // The reference leaves its static result untouched here (stale value); zero is the documented choice.
                        velocity = Vector3.Zero;
                        break;
                }
                break;
            }
            case EmissionVelocityType.Spherical:
            {
                var speed = template.SphericalSpeed.Sample(_random);
                velocity = ComputePointOnUnitSphere() * speed;
                break;
            }
            case EmissionVelocityType.Hemispherical:
            {
                var speed = template.SphericalSpeed.Sample(_random);
                Vector3 direction;
                do
                {
                    direction = new Vector3(_random.NextReal(-1f, 1f), _random.NextReal(-1f, 1f), _random.NextReal(0f, 1f));
                }
                while (direction == Vector3.Zero);
                velocity = Vector3.Normalize(direction) * speed;
                break;
            }
            default:
                velocity = Vector3.Zero;
                break;
        }
        return velocity * (0.5f + Budget.ParticleScale / 2f);
    }

    private static Vector3 SafeNormalize(Vector3 value) => value == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(value);
    private static Vector2 SafeNormalize(Vector2 value) => value == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(value);

    /// <summary>The reference's <c>(UnsignedInt)</c> casts of sampled reals; negative samples (undefined in C++) become 0.</summary>
    private static uint ToUnsigned(float value) => value <= 0f ? 0u : value >= uint.MaxValue ? uint.MaxValue : (uint)value;

    // ------------------------------------------------------------------------------------------------ state

    private struct ParticleInfo
    {
        public Vector3 Position, Velocity, EmitterPosition;
        public float VelocityDamping, AngularDamping, AngleZ, AngularRate, Size, SizeRate, SizeRateDamping, ColorScale, WindRandomness;
        public uint Lifetime;
        public float[] AlphaValues;
        public bool UpTowardsEmitter;
    }

    private sealed class SystemState(int id, ParticleSystemTemplate template)
    {
        public readonly int Id = id;
        public readonly ParticleSystemTemplate Template = template;
        public readonly List<Particle> Particles = [];
        public readonly float[] Volume = [..template.VolumeSlots];
        public int LiveCount;
        public Matrix4x4 Local = Matrix4x4.Identity;
        public bool LocalIdentity = true;
        public Matrix4x4 Transform = Matrix4x4.Identity;
        public bool Identity = true;
        public Vector3 Pos, LastPos, AttachedPosition;
        public bool FirstPos = true;
        public EntityId Attached;
        public Particle? ControlParticle;
        public SystemState? Master, Slave;
        public bool Destroyed, Removed, Forever, WindToEnd;
        public uint DelayLeft, BurstDelayLeft, LifetimeLeft, PersonalityStore;
        public float AccumulatedSizeBonus, WindAngle, WindAngleChange, WindStart, WindEnd;
        public FxRandomVariable ColorScale;

        public void Destroy()
        {
            Destroyed = true;
            Slave?.Destroy();
        }
    }

    private sealed class Particle
    {
        public Particle(SystemState system, in ParticleInfo info, long frame, uint personality)
        {
            System = system;
            Personality = personality;
            CreatedFrame = frame;
            Position = info.Position;
            Velocity = info.Velocity;
            EmitterPosition = info.EmitterPosition;
            VelocityDamping = info.VelocityDamping;
            AngleZ = info.AngleZ;
            AngularRate = info.AngularRate;
            AngularDamping = info.AngularDamping;
            LifetimeLeft = info.Lifetime;
            Size = info.Size;
            SizeRate = info.SizeRate;
            SizeRateDamping = info.SizeRateDamping;
            AlphaValues = info.AlphaValues;
            ColorScale = info.ColorScale;
            WindRandomness = info.WindRandomness;
            UpTowardsEmitter = info.UpTowardsEmitter;
            Alpha = AlphaValues[0];
            AlphaTarget = 1;
            ComputeAlphaRate();
            Color = system.Template.ColorKeys[0].Color;
            ColorTarget = 1;
            ComputeColorRate();
        }

        public readonly SystemState System;
        public readonly uint Personality;
        public readonly long CreatedFrame;
        public readonly float[] AlphaValues;
        public Vector3 Position, Velocity, Acceleration, EmitterPosition, Color, ColorRate;
        public float VelocityDamping, AngleZ, AngularRate, AngularDamping, Size, SizeRate, SizeRateDamping, Alpha, AlphaRate, ColorScale, WindRandomness;
        public uint LifetimeLeft;
        public int AlphaTarget, ColorTarget;
        public bool UpTowardsEmitter, Dead;
        public SystemState? Controlled;
        public LinkedListNode<Particle>? PriorityNode;

        public void ComputeAlphaRate()
        {
            var keys = System.Template.AlphaKeys;
            if (AlphaTarget >= ParticleSystemTemplate.MaxKeyframes || keys[AlphaTarget].Frame == 0) { AlphaRate = 0f; return; }
            var time = unchecked(keys[AlphaTarget].Frame - keys[AlphaTarget - 1].Frame);
            AlphaRate = (AlphaValues[AlphaTarget] - AlphaValues[AlphaTarget - 1]) / time;
        }

        public void ComputeColorRate()
        {
            var keys = System.Template.ColorKeys;
            if (ColorTarget >= ParticleSystemTemplate.MaxKeyframes || keys[ColorTarget].Frame == 0) { ColorRate = Vector3.Zero; return; }
            var time = unchecked(keys[ColorTarget].Frame - keys[ColorTarget - 1].Frame);
            ColorRate = (keys[ColorTarget].Color - keys[ColorTarget - 1].Color) / time;
        }

        /// <summary><c>Particle::isInvisible</c>.</summary>
        public bool IsInvisible()
        {
            var colorKeys = System.Template.ColorKeys;
            var settled = ColorTarget >= ParticleSystemTemplate.MaxKeyframes || colorKeys[ColorTarget].Frame == 0;
            return System.Template.Shader switch
            {
                ParticleShader.Additive => settled && Color.X + Color.Y + Color.Z <= 0.06f,
                ParticleShader.Alpha => Alpha < 0.02f,
                ParticleShader.AlphaTest => false,
                ParticleShader.Multiply => settled && Color.X * Color.Y * Color.Z > 0.95f,
                _ => true,
            };
        }
    }
}
