using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Presentation.Fx;
using CnCNew.Simulation.Ids;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The W3D particle emitter a model can be (issue 369: the hydrant's water spray <c>EXHydrant</c> was not drawn), run as <c>ParticleEmitterClass</c> and <c>ParticleBufferClass</c> run it:
/// emission per whole interval of the W3D clock (33 ms a frame), particles placed at their age under constant acceleration, keys linear in milliseconds, the emitter attached to its
/// object, stopping and finishing. Every number below is computed by hand from those rules.
/// </summary>
public static class ModelEmitterTests
{
    public static int Run(string[] args)
    {
        var failures = 0;
        void Test(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine($"  ok   {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"  FAIL {name}: {exception.Message}");
            }
        }

        Test("Model emitter: a burst for each whole emission interval, stamped inside the frame, placed at its age", Emission);
        Test("Model emitter: colour, size and opacity follow their keys in milliseconds of age; a key at the lifetime bends the last segment", Keys);
        Test("Model emitter: particles die at the lifetime; a stopped emitter makes none and leaves when its last particle died", Lifetime);
        Test("Model emitter: it stands where its object stands and turns with it; an object that is gone stops it", Attached);
        Test("Model emitter: MaxEmissions counts particles and completes the emitter", MaxEmissions);
        Test("Model emitter: the outward velocity points along the creation place, along X from the centre", Outward);
        Test("Model emitter: two runs of the same seed give the same particles", Deterministic);
        Console.WriteLine($"model emitters: {(failures == 0 ? "all passed" : $"{failures} failed")}.");
        return failures;
    }

    private static readonly EntityId Spray = new(7);

    private static ModelEmitterSpec Spec(float rate = 10f, float lifetime = 1f, int maxEmissions = 0, float outward = 0f) => new(
        "TEST", "test.tga", ParticleShader.AlphaTest, rate, 1, maxEmissions, lifetime,
        new EmitterVolume(EmitterVolumeKind.SolidBox, 0f, 0f, 0f), new EmitterVolume(EmitterVolumeKind.SolidBox, 0f, 0f, 0f),
        new Vector3(0f, 0f, 100f), outward, 0f, new Vector3(0f, 0f, -100f),
        new EmitterColorKeys(new Vector3(1f, 0f, 0f), Vector3.Zero, [(1f, new Vector3(0f, 0f, 1f))]),
        new EmitterScalarKeys(1f, 0f, []),
        new EmitterScalarKeys(2f, 0f, [(0.5f, 4f)]),
        EmitterScalarKeys.Constant(0f), 0f);

    private static Func<EntityId, FxEntityPose?> At(Vector3 position, float heading = 0f) =>
        entity => entity == Spray ? new FxEntityPose(position, heading, "WaterSpray", default, 0, 2f) : null;

    private static ImmutableArray<ParticleInstance> Frame(ModelEmitterWorld world)
    {
        var particles = ImmutableArray.CreateBuilder<ParticleInstance>();
        var batches = new List<ParticleBatchKey>();
        var culled = 0;
        world.AppendTo(particles, batches, null, ref culled);
        Check(particles.Count == 0 || batches.Single() == new ParticleBatchKey("test.tga", ParticleShader.AlphaTest, ParticleKind.Particle, false), "one batch of the emitter's texture and blend");
        return particles.ToImmutable();
    }

    private static void Emission()
    {
        var world = new ModelEmitterWorld(new FxRandom(1));
        world.Start(Spec(), Spray, Vector3.Zero);
        var poses = At(Vector3.Zero);
        for (var step = 0; step < 3; step++) world.Step(poses);
        Check(Frame(world).IsEmpty, "99 ms are less than one interval of 100 ms");
        world.Step(poses);
        // 132 ms: one interval, the particle stamped at 100 ms (132 - the remainder 32), 32 ms old: z = 0.1 * 32 - 0.5 * 0.0001 * 32^2.
        var first = Frame(world).Single();
        Near(first.Position.Z, 0.1f * 32 - 0.5f * 0.0001f * 32 * 32, 1e-4f, "the first particle stands where 32 ms of flight took it");
        for (var step = 0; step < 30; step++) world.Step(poses);
        // 34 frames = 1,122 ms: born at 100, 200 ... 1,100; alive the ten younger than 1,000 ms (200 ... 1,100).
        var frame = Frame(world);
        Check(frame.Length == 10, $"ten particles live at 1,122 ms ({frame.Length})");
        var oldest = frame[0];
        Near(oldest.Position.Z, 0.1f * 922 - 0.5f * 0.0001f * 922 * 922, 1e-2f, "the oldest (922 ms) followed its parabola step by step");
    }

    private static void Keys()
    {
        var world = new ModelEmitterWorld(new FxRandom(2));
        world.Start(Spec(), Spray, Vector3.Zero);
        for (var step = 0; step < 34; step++) world.Step(At(Vector3.Zero));
        var frame = Frame(world);
        Check(frame.Length == 10, "ten particles at 1,122 ms");
        for (var index = 0; index < frame.Length; index++)
        {
            // Oldest first: born at 200, 300 ... 1,100 ms, so 922, 822 ... 22 ms old.
            var particle = frame[index];
            var age = 922 - 100 * index;
            var expectedSize = age >= 500 ? 4f : 2f + 2f * age / 500f;
            Near(particle.Size, expectedSize, 0.02f, $"size at age {age}");
            Near(particle.Color.X, 1f - age / 1000f, 0.002f, $"red falls toward the key at the lifetime (age {age})");
            Near(particle.Color.Z, age / 1000f, 0.002f, $"blue rises toward it (age {age})");
            Near(particle.Color.W, 1f, 1e-6f, "opacity without keys stays");
        }
    }

    private static void Lifetime()
    {
        var world = new ModelEmitterWorld(new FxRandom(3));
        var id = world.Start(Spec(), Spray, Vector3.Zero);
        for (var step = 0; step < 34; step++) world.Step(At(Vector3.Zero));
        world.Stop(id);
        Check(!world.IsEmitting(id), "a stopped emitter emits no more");
        world.Step(At(Vector3.Zero));
        Check(Frame(world).Length <= 10 && world.Stats.Emitters == 1, "its particles live on");
        for (var step = 0; step < 31; step++) world.Step(At(Vector3.Zero));
        Check(Frame(world).IsEmpty && world.Stats.Emitters == 0, "after its last particle died the emitter is gone");
    }

    private static void Attached()
    {
        var world = new ModelEmitterWorld(new FxRandom(4));
        var id = world.Start(Spec() with { Velocity = new Vector3(100f, 0f, 0f), Acceleration = Vector3.Zero }, Spray, new Vector3(0f, 0f, 3f));
        for (var step = 0; step < 4; step++) world.Step(At(new Vector3(50f, 20f, 10f), MathF.PI / 2));
        var particle = Frame(world).Single();
        // Velocity +X turned a quarter: +Y; 32 ms at 0.1 per ms; the offset 3 units up.
        Near(particle.Position.X, 50f, 1e-3f, "x at the object");
        Near(particle.Position.Y, 20f + 3.2f, 1e-3f, "the velocity turned with the object");
        Near(particle.Position.Z, 13f, 1e-3f, "the offset in its model space");
        world.Step(_ => null);
        Check(!world.IsEmitting(id), "an object that is gone stops its emitter");
    }

    private static void MaxEmissions()
    {
        var world = new ModelEmitterWorld(new FxRandom(5));
        var id = world.Start(Spec(rate: 30.30303f, maxEmissions: 3), Spray, Vector3.Zero);
        for (var step = 0; step < 10; step++) world.Step(At(Vector3.Zero));
        Check(Frame(world).Length == 3 && !world.IsEmitting(id) && world.Stats.ParticlesCreated == 3, $"three particles and done ({Frame(world).Length}, {world.Stats.ParticlesCreated})");
    }

    private static void Outward()
    {
        var world = new ModelEmitterWorld(new FxRandom(6));
        world.Start(Spec(outward: 50f) with { Velocity = Vector3.Zero, Acceleration = Vector3.Zero }, Spray, Vector3.Zero);
        for (var step = 0; step < 4; step++) world.Step(At(Vector3.Zero));
        Near(Frame(world).Single().Position.X, 0.05f * 32, 1e-4f, "from the centre the outward velocity points along X");
    }

    private static void Deterministic()
    {
        ImmutableArray<ParticleInstance> RunOnce()
        {
            var spec = Spec() with
            {
                CreationVolume = new EmitterVolume(EmitterVolumeKind.SolidSphere, 2f, 0f, 0f),
                VelocityRandom = new EmitterVolume(EmitterVolumeKind.SolidCylinder, 1f, 3f, 0f),
                Sizes = new EmitterScalarKeys(2f, 1f, [(0.5f, 4f)]),
                Colors = new EmitterColorKeys(Vector3.One, new Vector3(0.2f), []),
            };
            var world = new ModelEmitterWorld(new FxRandom(99));
            world.Start(spec, Spray, Vector3.Zero);
            for (var step = 0; step < 40; step++) world.Step(At(Vector3.Zero));
            return Frame(world);
        }
        var (a, b) = (RunOnce(), RunOnce());
        Check(a.Length > 0 && a.SequenceEqual(b), "the same particles");
        Check(a.Select(particle => particle.Size).Distinct().Count() > 1, "the size random varies the particles");
    }

    private static void Near(float actual, float expected, float tolerance, string what)
    {
        if (MathF.Abs(actual - expected) > tolerance) throw new InvalidOperationException($"{what}: expected {expected}, got {actual}");
    }

    private static void Check(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException(what);
    }
}
