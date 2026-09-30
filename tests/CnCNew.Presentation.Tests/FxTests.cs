using System.Diagnostics;
using System.Numerics;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Presentation.Fx;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;

namespace CnCNew.Presentation.Tests;

/// <summary>Tests of <c>CnCNew.Presentation.Fx</c>: FXList/particle parsing, the particle simulation, the executor and sound cues.</summary>
public static class FxTests
{
    public static int Run(string[] args)
    {
        var run = new FxRunner();
        run.Test("Fx: FxRandom and GameClientRandomVariable semantics", RandomSemantics);
        run.Test("Fx: synthetic FXList and ParticleSystem parse with typed nuggets", SyntheticParse);
        run.Test("Fx: unknown nuggets, keys and enums are unexplained diagnostics", UnknownsAreReported);
        run.Test("Fx: alpha, color, size and motion keyframes (hand-computed)", KeyframesHandComputed);
        run.Test("Fx: emission volumes, bursts, system lifetime and initial delay", EmissionAndLifetime);
        run.Test("Fx: slave and per-particle attached systems", SlaveAndAttachedSystems);
        run.Test("Fx: budgets (total, priority eviction, min priority, per system, systems)", Budgets);
        run.Test("Fx: particle world is deterministic (byte-identical runs)", ParticleDeterminism);
        run.Test("Fx: executor turns nuggets into particles, tracers, lights, shakes, scorches and cues", ExecutorNuggets);
        run.Test("Fx: executor handles FxRequested, WeaponFired and DamageFX events", ExecutorEvents);
        run.Test("Fx: camera shake falloff and decay (W3DView::shake)", CameraShakeSemantics);
        run.Test("Fx: decal ring buffer and scorch atlas", DecalBufferSemantics);
        run.Test("Fx: sound cue derivation rules", SoundCueRules);
        run.Test("Fx: sound cue rate limiter", RateLimiter);
        var contentDirectory = args.SkipWhile(value => value != "--content-dir").Skip(1).FirstOrDefault();
        if (contentDirectory is not null) run.Test("Fx: real data parses cleanly and every FXList/particle system executes", () => RealData(contentDirectory));
        return run.Finish("fx");
    }

    // ------------------------------------------------------------------------------------------ fixture

    private const string GameDataIni = """
        GameData
          MaxParticleCount = 40
          MaxFieldParticleCount = 30
          ParticleScale = 1.0
          ShakeSubtleIntensity = 0.5
          ShakeNormalIntensity = 1.0
          ShakeStrongIntensity = 2.5
          ShakeSevereIntensity = 5.0
          ShakeCineExtremeIntensity = 7.5
          ShakeCineInsaneIntensity = 10.0
          MaxShakeIntensity = 10.0
          MaxShakeRange = 150.0
        End
        """;

    private const string ParticleIni = """
        ParticleSystem TestFade
          Priority = WEAPON_EXPLOSION
          Shader = ALPHA
          Type = PARTICLE
          ParticleName = TestPuff.tga
          Lifetime = 100 100
          SystemLifetime = 1
          Size = 2 2
          SizeRate = 1 1
          SizeRateDamping = 0.5 0.5
          VelocityDamping = 0.5 0.5
          Gravity = -1
          Alpha1 = 0 0 0
          Alpha2 = 1 1 10
          Alpha3 = 0 0 20
          Color1 = R:255 G:0 B:0 0
          Color2 = R:0 G:0 B:255 10
          BurstCount = 1 1
          BurstDelay = 1000 1000
          InitialDelay = 0 0
          DriftVelocity = X:1 Y:0 Z:0
          VelocityType = ORTHO
          VelOrthoX = 1 1
          VelOrthoY = 0 0
          VelOrthoZ = 0 0
          VolumeType = POINT
        End

        ParticleSystem TestSpray
          Priority = WEAPON_EXPLOSION
          Shader = ADDITIVE
          Type = PARTICLE
          ParticleName = TestSpark.tga
          Lifetime = 30 60
          SystemLifetime = 20
          Size = 1 3
          Alpha1 = 1 1 0
          Color1 = R:200 G:150 B:100 0
          Color2 = R:20 G:10 B:5 30
          BurstCount = 3 5
          BurstDelay = 0 2
          InitialDelay = 2 2
          VelocityType = SPHERICAL
          VelSpherical = 1 2
          VolumeType = SPHERE
          VolSphereRadius = 4
          IsHollow = Yes
          WindMotion = PingPong
        End

        ParticleSystem TestCritical
          Priority = CRITICAL
          Shader = ALPHA_TEST
          Type = STREAK
          ParticleName = TestStreak.tga
          Lifetime = 50 50
          Size = 1 1
          BurstCount = 5 5
          BurstDelay = 0 0
          VelocityType = CYLINDRICAL
          VelCylindricalRadial = 1 1
          VelCylindricalNormal = 0 1
          VolumeType = CYLINDER
          VolCylinderRadius = 3
          VolCylinderLength = 6
        End

        ParticleSystem TestMaster
          Priority = DEATH_EXPLOSION
          Shader = ALPHA_TEST
          Type = PARTICLE
          ParticleName = TestPuff.tga
          Lifetime = 10 10
          SystemLifetime = 3
          Size = 2 2
          BurstCount = 2 2
          BurstDelay = 0 0
          SlaveSystem = TestSlave
          SlavePosOffset = X:0 Y:0 Z:5
          PerParticleAttachedSystem = TestAttached
          VelocityType = OUTWARD
          VelOutward = 1 1
          VelOutwardOther = 0 0
          VolumeType = LINE
          VolLineStart = X:-5 Y:0 Z:0
          VolLineEnd = X:5 Y:0 Z:0
        End

        ParticleSystem TestSlave
          Priority = DEATH_EXPLOSION
          Shader = ALPHA_TEST
          Type = PARTICLE
          ParticleName = TestSmoke.tga
          Lifetime = 10 10
          Size = 0.5 0.5
          BurstCount = 9 9
          BurstDelay = 0 0
          VelocityType = ORTHO
          VolumeType = POINT
        End

        ParticleSystem TestAttached
          Priority = DEATH_EXPLOSION
          Shader = ALPHA_TEST
          Type = PARTICLE
          ParticleName = TestTrail.tga
          Lifetime = 2 2
          Size = 1 1
          BurstCount = 1 1
          BurstDelay = 0 0
          VelocityType = ORTHO
          VolumeType = BOX
          VolBoxHalfSize = X:1 Y:1 Z:1
        End

        ParticleSystem TestAlways
          Priority = ALWAYS_RENDER
          Shader = ALPHA_TEST
          Type = PARTICLE
          ParticleName = TestPuff.tga
          Lifetime = 100 100
          Size = 1 1
          BurstCount = 10 10
          BurstDelay = 0 0
          VelocityType = ORTHO
          VolumeType = POINT
        End
        """;

    private const string FxListIni = """
        FXList FX_TestAll
          ParticleSystem
            Name = TestSpray
            Count = 3
            Radius = 5 5 CONSTANT
            Height = 2 2 CONSTANT
            InitialDelay = 100 100 UNIFORM
          End
          Sound
            Name = TestBoom
          End
          LightPulse
            Color = R:255 G:128 B:0
            Radius = 30
            IncreaseTime = 0
            DecreaseTime = 100
          End
          LightPulse
            Color = R:255 G:255 B:255
            Radius = 15
            DecreaseTime = 500
          End
          ViewShake
            Type = STRONG
          End
          TerrainScorch
            Type = SHADOW_SCORCH
            Radius = 12
          End
          Tracer
            TracerName = TestTracer
            Speed = 300
            Length = 10
            Width = 0.5
            DecayAt = 1
            Color = R:255 G:255 B:0
          End
          FXListAtBonePos
            FX = FX_TestBone
            BoneName = MUZZLE
          End
        End

        FXList FX_TestBone
          Sound
            Name = TestBoneSound
          End
        End

        FXList FX_TestAttach
          ParticleSystem
            Name = TestSpray
            AttachToObject = Yes
            OrientToObject = No
          End
        End

        FXList FX_TestFire
          Sound
            Name = TestMuzzleSound
          End
        End

        FXList FX_TestMajor
          ViewShake
            Type = SEVERE
          End
        End

        FXList FX_TestMinor
          ViewShake
            Type = SUBTLE
          End
        End

        FXList FX_TestRedefined
          ViewShake
            Type = SUBTLE
          End
        End

        FXList FX_TestRedefined
          TerrainScorch
            Type = SCORCH_2
            Radius = 8
          End
        End
        """;

    private const string OtherIni = """
        Weapon TestGun
          PrimaryDamage = 10
          AttackRange = 100
          DamageType = ARMOR_PIERCING
          WeaponSpeed = 300
          FireFX = FX_TestFire
          FireSound = TestGunShot
        End

        DamageFX TestDamageFX
          ThrottleTime = DEFAULT 1000
          AmountForMajorFX = DEFAULT 5.0
          MajorFX = DEFAULT FX_TestMajor
          MinorFX = DEFAULT FX_TestMinor
          MajorFX = SMALL_ARMS None
        End
        """;

    private const string ObjectIni = """
        Object TestTracer
          KindOf = DRAWABLE_ONLY
        End

        Object TestTank
          KindOf = SELECTABLE VEHICLE SCORE
          VoiceFear = TestTankFear
          VoiceCreated = TestTankCreated
          VoiceTaskComplete = TestTankDone
          SoundEnter = TestTankEnter
          UnitSpecificSounds
            VoiceCreate = TestTankPerUnitCreate
          End
          ArmorSet
            Conditions = None
            Armor = TestArmor
            DamageFX = TestDamageFX
          End
        End

        Object TestBase
          KindOf = STRUCTURE MP_COUNT_FOR_VICTORY SELECTABLE
        End
        """;

    private const string AudioIni = """
        MiscAudio
          MoneyDepositSound = TestMoneyIn
          MoneyWithdrawSound = TestMoneyOut
          NoCanDoSound = TestNoCanDo
          RadarNotifyStructureUnderAttackSound = TestRadarStructure
          RadarNotifyHarvesterUnderAttackSound = TestRadarHarvester
          RadarNotifyUnderAttackSound = TestRadarAny
        End
        """;

    private static readonly Lazy<CompiledDefinitionGraph> SyntheticGraph = new(() => Compile(new Dictionary<string, string>
    {
        ["Data/INI/GameData.ini"] = GameDataIni,
        ["Data/INI/ParticleSystem.ini"] = ParticleIni,
        ["Data/INI/FXList.ini"] = FxListIni,
        ["Data/INI/Weapon.ini"] = OtherIni.Split("DamageFX TestDamageFX")[0],
        ["Data/INI/DamageFX.ini"] = "DamageFX TestDamageFX" + OtherIni.Split("DamageFX TestDamageFX")[1],
        ["Data/INI/Object/TestObjects.ini"] = ObjectIni,
        ["Data/INI/MiscAudio.ini"] = AudioIni,
    }));

    private static CompiledDefinitionGraph Compile(Dictionary<string, string> files)
        => DefinitionCompiler.Compile(new InMemoryDefinitionSource(files), ModuleRegistry.Default, DefinitionCompilerOptions.Audit);

    private static FxCatalog Catalog() => FxCatalog.Build(SyntheticGraph.Value);

    // ------------------------------------------------------------------------------------------ tests

    private static void RandomSemantics()
    {
        var a = new FxRandom(42);
        var b = new FxRandom(42);
        for (var index = 0; index < 100; index++) Assert(a.NextUInt64() == b.NextUInt64(), "same seed, same sequence");
        var random = new FxRandom(7);
        var before = random.State;
        Assert(FxRandomVariable.Constant(3f).Sample(random) == 3f && random.State == before, "CONSTANT low == high returns low without drawing");
        Assert(new FxRandomVariable(2f, 2f, FxDistribution.Uniform).Sample(random) == 2f && random.State != before, "UNIFORM low == high still draws");
        var constantRange = new FxRandomVariable(1f, 2f, FxDistribution.Constant).Sample(random);
        Assert(constantRange is >= 1f and <= 2f, "CONSTANT low != high falls through to UNIFORM");
        Assert(new FxRandomVariable(1f, 2f, FxDistribution.Gaussian).Sample(random) == 0f, "GAUSSIAN is unimplemented in the reference: 0");
        var seen = new bool[4];
        for (var index = 0; index < 400; index++)
        {
            var value = random.NextInt(0, 3);
            Assert(value is >= 0 and <= 3, "NextInt is inclusive");
            seen[value] = true;
        }
        Assert(seen.All(value => value), "NextInt covers both ends");
        for (var index = 0; index < 1000; index++)
        {
            var unit = random.NextUnit();
            Assert(unit is >= 0f and <= 1f, "NextUnit in [0, 1]");
        }
    }

    private static void SyntheticParse()
    {
        var catalog = Catalog();
        Assert(catalog.UnexplainedDiagnostics.Length == 0, "fixture parses without unexplained diagnostics: "
            + string.Join("; ", catalog.UnexplainedDiagnostics.Select(value => $"{value.Code} {value.Subject} {value.Detail}")));
        var all = catalog.FindFxList("fx_testall")!;
        Assert(all.Nuggets.Length == 8, "eight nuggets");
        var particles = (ParticleSystemFxNugget)all.Nuggets[0];
        Assert(particles.Name == "TestSpray" && particles.Count == 3 && particles.Radius == FxRandomVariable.Constant(5f), "particle nugget fields");
        Assert(particles.InitialDelay == new FxRandomVariable(100f, 100f, FxDistribution.Uniform), "InitialDelay random variable");
        var light = (LightPulseFxNugget)all.Nuggets[2];
        Assert(light.DecreaseFrames == 3 && Near(light.Color.Y, 128f / 255f), "LightPulse: 100 ms is 3 frames, color divided by 255");
        Assert(((ViewShakeFxNugget)all.Nuggets[4]).Type == ViewShakeType.Strong, "ViewShake type");
        Assert(((TerrainScorchFxNugget)all.Nuggets[5]).Type == ScorchType.ShadowScorch, "scorch type");
        var tracer = (TracerFxNugget)all.Nuggets[6];
        Assert(Near(tracer.Speed, 10f) && tracer.Probability == 1f && tracer.TracerName == "TestTracer", "tracer speed per frame and defaults");
        Assert(((FxListAtBonePosFxNugget)all.Nuggets[7]).OrientToBone, "OrientToBone defaults to true");
        var redefined = catalog.FindFxList("FX_TestRedefined")!;
        Assert(redefined.Nuggets.Length == 1 && redefined.Nuggets[0] is TerrainScorchFxNugget, "a redefined FXList keeps only the last block");
        Assert(catalog.Diagnostics.Any(value => value.Code == FxDiagnosticCodes.FxListRedefined), "redefinition is reported (explained)");

        var fade = catalog.FindParticleSystem("TestFade")!;
        Assert(fade.Shader == ParticleShader.Alpha && fade.Kind == ParticleKind.Particle && fade.Priority == ParticlePriority.WeaponExplosion, "enums");
        Assert(fade.AlphaKeys[1] == new AlphaKeyframe(new FxRandomVariable(1f, 1f, FxDistribution.Uniform), 10), "alpha keyframe");
        Assert(fade.ColorKeys[1].Frame == 10 && fade.ColorKeys[1].Color == new Vector3(0f, 0f, 1f), "color keyframe");
        Assert(fade.DriftVelocity == new Vector3(1f, 0f, 0f) && fade.Gravity == -1f && fade.SystemLifetime == 1, "drift, gravity, lifetime");
        var master = catalog.FindParticleSystem("TestMaster")!;
        Assert(master.LineStart == new Vector3(-5f, 0f, 0f) && master.LineEnd == new Vector3(5f, 0f, 0f), "line volume slots");
        Assert(master.SlaveSystem == "TestSlave" && master.SlavePositionOffset == new Vector3(0f, 0f, 5f), "slave fields");
        var spray = catalog.FindParticleSystem("TestSpray")!;
        Assert(spray.WindMotion == ParticleWindMotion.PingPong && spray.IsHollow && spray.SphereRadius == 4f, "wind and sphere");
        Assert(Near(spray.WindPingPongEndAngleMax, 2f * MathF.PI), "wind default angles");

        var weapon = catalog.FindWeapon("TestGun")!;
        Assert(weapon.FireFx.All(name => name == "FX_TestFire") && weapon.FireSound == "TestGunShot" && Near(weapon.WeaponSpeed, 10f), "weapon FX");
        Assert(weapon.DamageType == "ARMOR_PIERCING" && !weapon.IsContact, "weapon damage type and contact test");
        var table = catalog.DamageFx["TestDamageFX"];
        var armorPiercing = table.Resolve("ARMOR_PIERCING", 0);
        Assert(armorPiercing is { Major: "FX_TestMajor", Minor: "FX_TestMinor", AmountForMajor: 5f, ThrottleFrames: 30 }, "DamageFX DEFAULT lines");
        Assert(table.Resolve("SMALL_ARMS", 0).Major is null, "a later specific line overrides DEFAULT");
        var tank = catalog.FindObject("TestTank")!;
        Assert(tank.DamageFx == "TestDamageFX" && tank.Sound("VoiceFear") == "TestTankFear" && tank.UnitSpecificSounds["VoiceCreate"] == "TestTankPerUnitCreate", "object sounds");
        Assert(catalog.MiscAudio["MoneyDepositSound"] == "TestMoneyIn", "MiscAudio");
        Assert(catalog.Settings.Budget.MaxParticles == 40 && catalog.Settings.ShakeIntensity(ViewShakeType.CineExtreme) == 7.5f, "GameData settings");
    }

    private static void UnknownsAreReported()
    {
        var graph = Compile(new Dictionary<string, string>
        {
            ["Data/INI/FXList.ini"] = """
                FXList FX_Broken
                  Explosion
                    Name = Whatever
                  End
                  ViewShake
                    Type = WOBBLY
                    Strength = 3
                  End
                End
                """,
            ["Data/INI/ParticleSystem.ini"] = """
                ParticleSystem BrokenSystem
                  Shader = GLOWY
                  Lifetime = 1 2 GAUSSIAN
                  Sparkle = Yes
                  Alpha1 = 1
                End
                """,
        });
        var codes = FxCatalog.Build(graph).UnexplainedDiagnostics.Select(value => value.Code).ToArray();
        Assert(codes.Count(code => code == FxDiagnosticCodes.UnknownNugget) == 1, "unknown nugget");
        Assert(codes.Count(code => code == FxDiagnosticCodes.UnknownKey) == 2, "unknown nugget key and template key");
        Assert(codes.Count(code => code == FxDiagnosticCodes.UnknownEnum) == 2, "unknown shake type and shader");
        Assert(codes.Count(code => code == FxDiagnosticCodes.UnsupportedDistribution) == 1, "GAUSSIAN reported");
        Assert(codes.Count(code => code == FxDiagnosticCodes.InvalidValue) == 1, "malformed keyframe");
    }

    private static void KeyframesHandComputed()
    {
        var catalog = Catalog();
        var world = new ParticleWorld(new ParticleBudget(), new FxRandom(1), catalog.FindParticleSystem);
        world.Spawn(catalog.FindParticleSystem("TestFade")!, new ParticleSpawn(new Vector3(10f, 20f, 30f)));
        // Update k (1-based) runs with elapsed = k - 1: alpha grows 0.1 per frame to the key at 10, then falls 0.1 per frame.
        var expectedX = 10f;
        var velocityX = 1f;
        var velocityZ = 0f;
        var z = 30f;
        var size = 2f;
        var sizeRate = 1f;
        for (var update = 1; update <= 21; update++)
        {
            world.Step();
            var frame = world.Capture();
            var elapsed = update - 1;
            if (elapsed == 20)
            {
                Assert(frame.Particles.Length == 0 && world.ParticleCount == 0, "the particle dies when alpha reaches the final 0 key");
                break;
            }
            Assert(frame.Particles.Length == 1, $"one particle after update {update}");
            var p = frame.Particles[0];
            var alpha = elapsed <= 10 ? 0.1f * Math.Min(elapsed + 1, 10) : 1f - 0.1f * (elapsed - 10);
            Assert(Near(p.Color.W, alpha, 1e-4f), $"alpha after update {update}: {p.Color.W} vs {alpha}");
            velocityX *= 0.5f;
            velocityZ = (velocityZ - 1f) * 0.5f;
            expectedX += velocityX + 1f;
            z += velocityZ;
            size += sizeRate;
            sizeRate *= 0.5f;
            Assert(Near(p.Position.X, expectedX, 1e-4f) && Near(p.Position.Z, z, 1e-4f) && p.Position.Y == 20f, $"motion after update {update}");
            Assert(Near(p.Size, size, 1e-5f), $"size after update {update}");
            if (elapsed == 4) Assert(Near(p.Color.X, 0.5f, 1e-5f) && p.Color.Y == 0f && Near(p.Color.Z, 0.5f, 1e-5f), "color halfway between keys");
            if (elapsed == 12) Assert(p.Color == new Vector4(0f, 0f, 1f, p.Color.W), "color settles on the last key (red clamped)");
        }
        Assert(world.SystemCount == 0, "the one-frame system is gone with its last particle");
    }

    private static void EmissionAndLifetime()
    {
        var catalog = Catalog();
        var spray = catalog.FindParticleSystem("TestSpray")!;
        var world = new ParticleWorld(new ParticleBudget(), new FxRandom(3), catalog.FindParticleSystem);
        world.Spawn(spray, new ParticleSpawn(new Vector3(0f, 0f, 50f)));
        world.Step();
        world.Step();
        Assert(world.ParticleCount == 0, "InitialDelay 2 frames: nothing emitted while delayed");
        world.Step();
        var first = world.Capture();
        Assert(first.Particles.Length is >= 3 and <= 5, $"BurstCount 3..5 on the first emitting frame, got {first.Particles.Length}");
        foreach (var particle in first.Particles)
        {
            var distance = Vector3.Distance(particle.Position, new Vector3(0f, 0f, 50f));
            Assert(distance is > 1f and < 7f, $"hollow sphere radius 4, no velocity (damping 0), at most 2.6 of wind: {distance}");
        }
        for (var index = 0; index < 200; index++) world.Step();
        Assert(world.SystemCount == 0 && world.ParticleCount == 0, "system lifetime 20 ends the system once its particles are gone");

        var critical = new ParticleWorld(new ParticleBudget(), new FxRandom(3), catalog.FindParticleSystem);
        var id = critical.Spawn(catalog.FindParticleSystem("TestCritical")!, new ParticleSpawn(Vector3.Zero) { InitialDelayFrames = 0 });
        critical.Step();
        var frame = critical.Capture();
        Assert(frame.Particles.Length == 5 && frame.Batches.Single() == new ParticleBatchKey("TestStreak.tga", ParticleShader.AlphaTest, ParticleKind.Streak, false),
            "streak batch key");
        foreach (var particle in frame.Particles)
            Assert(new Vector2(particle.Position.X, particle.Position.Y).Length() <= 3.01f && MathF.Abs(particle.Position.Z) <= 3.01f, "cylinder volume");
        var culled = critical.Capture(static (_, _) => false);
        Assert(culled.Particles.Length == 0 && culled.Culled == 5, "culling hook");
        critical.Destroy(id);
        for (var index = 0; index < 60; index++) critical.Step();
        Assert(critical.SystemCount == 0, "a destroyed forever-system disappears after its particles");
    }

    private static void SlaveAndAttachedSystems()
    {
        var catalog = Catalog();
        var world = new ParticleWorld(new ParticleBudget(), new FxRandom(5), catalog.FindParticleSystem);
        world.Spawn(catalog.FindParticleSystem("TestMaster")!, new ParticleSpawn(new Vector3(100f, 0f, 0f)));
        Assert(world.SystemCount == 2, "the slave is created with its master");
        world.Step();
        var frame = world.Capture();
        var bySystem = frame.Particles.GroupBy(particle => particle.SystemId).ToDictionary(group => group.Key, group => group.ToArray());
        // Systems: slave (id 2) joins the list before the master (id 1); two per-particle attached systems follow (ids 3, 4).
        Assert(world.SystemCount == 4, $"two attached systems, one per master particle; got {world.SystemCount}");
        Assert(bySystem[1].Length == 2 && bySystem[2].Length == 2, "the slave emits exactly one particle per master particle, never on its own");
        foreach (var slave in bySystem[2])
            // mergeRelatedParticleSystems adds the *slave's* m_slavePosOffset (copied from the slave template, zero here), not the master's.
            Assert(slave.Position.Z == 0f && slave.Position.X is >= 94.9f and <= 105.1f && Near(slave.Size, 2f * 0.5f),
                $"slave particle: master placement, slave offset, size scaled by the slave size: {slave}");
        Assert(frame.Particles.Select(particle => (particle.SystemId, particle.Personality)).SequenceEqual(
            frame.Particles.OrderBy(particle => particle.SystemId).ThenBy(particle => particle.Personality).Select(particle => (particle.SystemId, particle.Personality))),
            "instances are ordered by system id and personality");
        for (var index = 0; index < 40; index++) world.Step();
        Assert(world.SystemCount == 0 && world.ParticleCount == 0, "master lifetime ends; slave and attached systems follow");
    }

    private static void Budgets()
    {
        var catalog = Catalog();
        var always = catalog.FindParticleSystem("TestAlways")!;
        var low = catalog.FindParticleSystem("TestAlways")! with { Name = "Low", Priority = ParticlePriority.WeaponExplosion };
        var high = catalog.FindParticleSystem("TestAlways")! with { Name = "High", Priority = ParticlePriority.Critical };

        var world = new ParticleWorld(new ParticleBudget(MaxParticles: 25), new FxRandom(1), _ => null);
        world.Spawn(low, new ParticleSpawn(Vector3.Zero));
        world.Step();
        world.Step();
        world.Step();
        Assert(world.ParticleCount == 25, $"the total stays at the cap: {world.ParticleCount}");
        world.Spawn(high, new ParticleSpawn(Vector3.One));
        world.Step();
        var stats = world.Stats;
        Assert(world.ParticleCount == 25 && stats.EvictedForBudget >= 10, "higher priority evicts the oldest lower-priority particles");
        for (var index = 0; index < 5; index++) world.Step();
        var frame = world.Capture();
        Assert(frame.Particles.Count(particle => particle.SystemId == 2) == 25, "eventually only the high-priority system remains");
        Assert(world.Stats.RejectedBudget > 0, "low priority cannot evict high priority");
        world.Spawn(always, new ParticleSpawn(Vector3.Zero));
        world.Step();
        Assert(world.ParticleCount == 35, "ALWAYS_RENDER is exempt from the cap");

        var lod = new ParticleWorld(new ParticleBudget(MinPriority: ParticlePriority.Critical), new FxRandom(1), _ => null);
        lod.Spawn(low, new ParticleSpawn(Vector3.Zero));
        lod.Step();
        Assert(lod.ParticleCount == 0 && lod.Stats.RejectedBelowMinPriority == 10, "below the LOD minimum priority nothing is created");

        var perSystem = new ParticleWorld(new ParticleBudget(MaxParticlesPerSystem: 7), new FxRandom(1), _ => null);
        perSystem.Spawn(low, new ParticleSpawn(Vector3.Zero));
        perSystem.Step();
        perSystem.Step();
        Assert(perSystem.ParticleCount == 7 && perSystem.Stats.RejectedPerSystemCap == 13, "per-system cap");

        var systems = new ParticleWorld(new ParticleBudget(MaxSystems: 2), new FxRandom(1), _ => null);
        var ids = Enumerable.Range(0, 4).Select(_ => systems.Spawn(low, new ParticleSpawn(Vector3.Zero))).ToArray();
        Assert(ids.Count(value => value != 0) == 2 && systems.Stats.RejectedSystemCap == 2, "system cap");

        var zero = new ParticleWorld(new ParticleBudget(MaxParticles: 0), new FxRandom(1), _ => null);
        zero.Spawn(low, new ParticleSpawn(Vector3.Zero));
        zero.Step();
        Assert(zero.ParticleCount == 0, "MaxParticleCount 0 disables particles (GlobalData default)");
    }

    private static void ParticleDeterminism()
    {
        static byte[] Simulate(ulong seed)
        {
            var catalog = Catalog();
            var executor = new FxExecutor(catalog, seed);
            var view = new TestView();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            for (var frame = 0; frame < 150; frame++)
            {
                if (frame % 10 == 0)
                {
                    executor.ExecuteFxListAtPosition("FX_TestAll", new FxInvocation(new Vector3(frame, 0f, 0f)) { Secondary = new Vector3(frame + 100f, 0f, 0f) }, view);
                    executor.ExecuteFxListAtEntity("FX_TestAttach", TestView.Tank, EntityId.Invalid, view);
                    executor.Particles.Spawn(catalog.FindParticleSystem("TestMaster")!, new ParticleSpawn(new Vector3(0f, frame, 0f)));
                }
                view.TankPosition += new Vector3(1f, 0.5f, 0f);
                executor.Update(view);
                var captured = executor.Capture();
                foreach (var particle in captured.Particles.Particles)
                {
                    writer.Write(particle.SystemId);
                    writer.Write(particle.Personality);
                    writer.Write(particle.Position.X); writer.Write(particle.Position.Y); writer.Write(particle.Position.Z);
                    writer.Write(particle.Size);
                    writer.Write(particle.Color.X); writer.Write(particle.Color.Y); writer.Write(particle.Color.Z); writer.Write(particle.Color.W);
                    writer.Write(particle.Rotation);
                }
                foreach (var tracer in captured.Tracers) { writer.Write(tracer.Head.X); writer.Write(tracer.Opacity); }
                foreach (var light in captured.Lights) writer.Write(light.Factor);
                foreach (var cue in executor.DrainSoundCues()) writer.Write(cue.EventName);
            }
            writer.Flush();
            return stream.ToArray();
        }

        var first = Simulate(99);
        var second = Simulate(99);
        Assert(first.Length > 10_000, "the scenario produces particles");
        Assert(first.AsSpan().SequenceEqual(second), "two runs with the same seed are byte-identical");
        Assert(!first.AsSpan().SequenceEqual(Simulate(100)), "another seed gives another result");
    }

    private static void ExecutorNuggets()
    {
        var catalog = Catalog();
        var executor = new FxExecutor(catalog, 11, new ParticleBudget());
        var view = new TestView();
        executor.ExecuteFxListAtPosition("FX_TestAll", new FxInvocation(Vector3.Zero) { Secondary = new Vector3(100f, 0f, 0f) }, view);
        Assert(executor.Particles.SystemCount == 3, "Count = 3 creates three systems");
        var cues = executor.DrainSoundCues();
        Assert(cues.Length == 1 && cues[0] is { EventName: "TestBoom", Kind: SoundCueKind.Effect, Position: not null }, "Sound nugget becomes an effect cue");
        var frame = executor.Capture();
        Assert(frame.ViewShakes.Single().Type == ViewShakeType.Strong, "view shake request");
        Assert(frame.Lights.Length == 1 && executor.Counters["lightpulse-below-visible-radius"] == 1, "radius 15 is below the visible limit (1 + r < 21)");
        Assert(frame.Decals.Single() is { Kind: DecalKind.Scorch, Radius: 12f } decal && decal.UvRect == DecalBuffer.ScorchUv(4), "shadow scorch decal");
        var tracer = frame.Tracers.Single();
        Assert(tracer.ExpireFrame == 9 && tracer.Direction == Vector3.UnitX && tracer.Length == 10f, "tracer lifetime ceil((100 - 10) / 10 * 1) = 9 frames");
        Assert(executor.Counters["bonepos-without-object"] == 1, "FXListAtBonePos needs the object form");
        Assert(executor.Capture().ViewShakes.IsEmpty, "one-shot requests are drained");

        var systemsBefore = executor.Particles.SystemCount;
        executor.ExecuteFxListAtEntity("FX_TestAll", TestView.Tank, TestView.Base, view);
        Assert(executor.Particles.SystemCount == systemsBefore + 3, "object form spawns the particle nugget too");
        var boneCues = executor.DrainSoundCues().Select(cue => cue.EventName).ToArray();
        Assert(boneCues.Count(name => name == "TestBoneSound") == 3, "MUZZLE, MUZZLE01 and MUZZLE02 each run the bone FXList");

        executor.Update(view);
        var light = executor.Capture().Lights.First();
        Assert(Near(light.Factor, 2f / 3f, 1e-6f) && Near(light.OuterRadius, 31f * 2f / 3f, 1e-4f), "DecreaseTime 3 frames: factor 2/3 after one frame");
        for (var index = 0; index < 3; index++) executor.Update(view);
        var moved = executor.Capture().Tracers.First();
        Assert(Near(moved.Head.X, 40f, 1e-4f) && moved.Opacity is < 1f and > 0f, "tracer moves 10 per frame and fades");
        for (var index = 0; index < 20; index++) executor.Update(view);
        var done = executor.Capture();
        Assert(done.Tracers.IsEmpty && done.Lights.IsEmpty, "tracers and lights expire");
        Assert(done.Decals.Length == 1, "scorches are permanent (duplicate from the second call suppressed)");
    }

    private static void ExecutorEvents()
    {
        var catalog = Catalog();
        var executor = new FxExecutor(catalog, 5, new ParticleBudget());
        var view = new TestView();
        var tick = 100L;
        SimEvent Event(SimEventKind kind, EntityId entity, EntityId other, string text, long value, PlayerId? player = null)
            => new(new EventId(1), tick, kind, entity, other, player ?? PlayerId.System, text, value);

        executor.ExecuteEvents([Event(SimEventKind.FxRequested, TestView.Tank, EntityId.Invalid, "FX_TestAttach", 1)], view);
        Assert(executor.Particles.SystemCount == 1, "FxRequested with OrientToObject uses the object form (attached system)");
        for (var index = 0; index < 5; index++) { view.TankPosition += new Vector3(10f, 0f, 0f); executor.Update(view); }
        var attached = executor.Capture().Particles.Particles;
        Assert(attached.Length > 0 && attached.Max(particle => particle.Position.X) > 20f, "the attached system follows the entity");
        view.TankGone = true;
        for (var index = 0; index < 80; index++) executor.Update(view);
        Assert(executor.Particles.SystemCount == 0, "a system whose entity disappeared is destroyed");
        view.TankGone = false;

        executor.ExecuteEvents(
        [
            Event(SimEventKind.WeaponFired, TestView.Base, TestView.Tank, "TestGun", 20 * 65536L),
            Event(SimEventKind.EntityDamaged, TestView.Tank, TestView.Base, string.Empty, 20 * 65536L),
            Event(SimEventKind.EntityDamaged, TestView.Tank, TestView.Base, string.Empty, 2 * 65536L),
        ], view);
        var cues = executor.DrainSoundCues().Select(cue => cue.EventName).ToArray();
        Assert(cues.Contains("TestMuzzleSound") && cues.Contains("TestGunShot"), $"FireFX sound nugget and weapon FireSound: {string.Join(",", cues)}");
        var shakes = executor.Capture().ViewShakes;
        Assert(shakes.Length == 1 && shakes[0].Type == ViewShakeType.Severe, "20 damage >= 5 picks MajorFX; the second hit is throttled");
        Assert(executor.Counters["damagefx-throttled"] == 1, "throttle counted");
        Assert(executor.Counters["weapon-fx-without-muzzle"] == 1, "muzzle fallback counted");

        tick += 31;
        executor.ExecuteEvents(
        [
            Event(SimEventKind.WeaponFired, TestView.Base, TestView.Tank, "TestGun", 2 * 65536L),
            Event(SimEventKind.EntityDamaged, TestView.Tank, TestView.Base, string.Empty, 2 * 65536L),
            Event(SimEventKind.EntityDamaged, TestView.Tank, TestView.Base, string.Empty, 2 * 65536L),
        ], view);
        Assert(executor.Capture().ViewShakes.Single().Type == ViewShakeType.Subtle, "after the throttle window a small hit picks MinorFX");
        Assert(executor.Counters["damagefx-throttled"] == 2, "same damage type inside the window is throttled");
        executor.ExecuteEvents([Event(SimEventKind.EntityDamaged, TestView.Tank, TestView.Base, string.Empty, 2 * 65536L)], view);
        Assert(executor.Counters["damagefx-without-weapon"] == 1, "damage without a shot in the same tick has no damage type and is counted");
    }

    private static void CameraShakeSemantics()
    {
        var settings = Catalog().Settings;
        var request = new ViewShakeRequest(ViewShakeType.Strong, new Vector3(75f, 0f, 0f));
        Assert(Near(CameraShake.FalloffIntensity(settings, request, Vector2.Zero), 1.25f), "STRONG 2.5 at half the range is 1.25");
        Assert(CameraShake.FalloffIntensity(settings, request with { Epicenter = new Vector3(151f, 0f, 0f) }, Vector2.Zero) == 0f, "beyond MaxShakeRange nothing");
        var shake = new CameraShake(settings, new FxRandom(1));
        shake.Add(request, Vector2.Zero);
        var first = shake.Update();
        var second = shake.Update();
        Assert(Near(first.Length(), 1.25f, 1e-5f) && Near(second.Length(), 0.9375f, 1e-5f), "intensity decays by 0.75 per frame");
        Assert(Vector2.Dot(first, second) < 0f, "the direction flips every frame");
        for (var index = 0; index < 40; index++) shake.Update();
        Assert(shake.Offset == Vector2.Zero && shake.Intensity == 0f, "below 0.01 the shake stops");
        var clamp = new CameraShake(settings, new FxRandom(1));
        for (var index = 0; index < 3; index++) clamp.Add(new ViewShakeRequest(ViewShakeType.CineInsane, Vector3.Zero), Vector2.Zero);
        Assert(clamp.Intensity == 3f, "above MaxShakeIntensity the reference resets to 3.0");
    }

    private static void DecalBufferSemantics()
    {
        var buffer = new DecalBuffer(capacity: 3);
        Assert(buffer.AddScorch(Vector3.Zero, 8f, 0, 0), "first scorch");
        Assert(!buffer.AddScorch(new Vector3(1f, 1f, 0f), 8.5f, 0, 1), "within a quarter radius and same type: duplicate");
        Assert(buffer.AddScorch(new Vector3(1f, 1f, 0f), 8.5f, 1, 1), "another type is no duplicate");
        buffer.AddScorch(new Vector3(50f, 0f, 0f), 8f, 0, 2);
        buffer.AddScorch(new Vector3(100f, 0f, 0f), 8f, 0, 3);
        Assert(buffer.Count == 3 && buffer.Dropped == 1 && buffer.Snapshot()[0].Position == new Vector3(1f, 1f, 0f), "ring drops the oldest");
        buffer.Add(new DecalInstance(DecalKind.Fx, "TestDecal.tga", Vector3.Zero, 5f, 0f, Vector4.One, new Vector4(0f, 0f, 1f, 1f), 3, 10, 5));
        buffer.Expire(10);
        Assert(buffer.Snapshot().All(decal => decal.Kind == DecalKind.Scorch), "timed decals expire");
        Assert(DecalBuffer.ScorchUv(4) == new Vector4(1.5f / 4f, 1.5f / 4f, 2.5f / 4f, 2.5f / 4f), "SHADOW_SCORCH is the centre atlas cell");
        Assert(DecalBuffer.ScorchUv(2) == new Vector4(3f / 4f, 0f, 1f, 1f / 4f), "SCORCH_3 is the third cell of the first row");
    }

    private static void SoundCueRules()
    {
        var catalog = Catalog();
        var deriver = new SoundCueDeriver(catalog);
        var view = new TestView();
        var cues = new List<SoundCue>();
        SimEvent Event(long tick, SimEventKind kind, EntityId entity, EntityId other, string text, long value, PlayerId? player = null)
            => new(new EventId(1), tick, kind, entity, other, player ?? PlayerId.System, text, value);

        deriver.Derive(Event(1, SimEventKind.ProductionCompleted, TestView.Base, EntityId.Invalid, string.Empty, 7), view, cues);
        deriver.Derive(Event(1, SimEventKind.EntitySpawned, TestView.Tank, EntityId.Invalid, "TestTank", 0), view, cues);
        Assert(cues.Select(cue => cue.EventName).SequenceEqual(["TestTankCreated", "TestTankPerUnitCreate"]) && cues.All(cue => cue.Kind == SoundCueKind.Voice),
            "a produced unit plays VoiceCreated and the per-unit VoiceCreate");
        cues.Clear();
        deriver.Derive(Event(2, SimEventKind.EntitySpawned, TestView.Tank, EntityId.Invalid, "TestTank", 0), view, cues);
        Assert(cues.Count == 0, "a spawn without production is silent");
        deriver.Derive(Event(2, SimEventKind.VoiceFearRequested, TestView.Tank, TestView.Base, string.Empty, 0), view, cues);
        deriver.Derive(Event(2, SimEventKind.ConstructionCompleted, TestView.Base, TestView.Tank, string.Empty, 0), view, cues);
        deriver.Derive(Event(2, SimEventKind.SupplyDeposited, TestView.Base, TestView.Tank, string.Empty, 75), view, cues);
        deriver.Derive(Event(2, SimEventKind.ConstructionStarted, TestView.Base, TestView.Tank, "TestBase", 1000, new PlayerId(1)), view, cues);
        deriver.Derive(Event(2, SimEventKind.UpgradeCompleted, TestView.Tank, EntityId.Invalid, "Upgrade_Unknown", 0, new PlayerId(1)), view, cues);
        deriver.Derive(Event(2, SimEventKind.CommandRejected, EntityId.Invalid, EntityId.Invalid, "MoveCommand", 3, new PlayerId(1)), view, cues);
        deriver.Derive(Event(2, SimEventKind.EntityContained, TestView.Base, TestView.Tank, string.Empty, 0), view, cues);
        Assert(cues.Select(cue => (cue.EventName, cue.Kind)).SequenceEqual(
        [
            ("TestTankFear", SoundCueKind.Voice), ("TestTankDone", SoundCueKind.Voice), ("TestMoneyIn", SoundCueKind.UiSound),
            ("TestMoneyOut", SoundCueKind.UiSound), ("UpgradeComplete", SoundCueKind.Eva), ("TestNoCanDo", SoundCueKind.UiSound),
            ("TestTankEnter", SoundCueKind.Effect),
        ]), "rule table: " + string.Join(", ", cues.Select(cue => cue.EventName)));
        Assert(cues[2].Player == new PlayerId(2), "a module-emitted deposit is addressed to the owner of the supply center");
        cues.Clear();
        deriver.Derive(Event(3, SimEventKind.EntityDamaged, TestView.Base, TestView.Tank, string.Empty, 65536), view, cues);
        deriver.Derive(Event(4, SimEventKind.EntityDamaged, TestView.Base, TestView.Tank, string.Empty, 65536), view, cues);
        Assert(cues.Select(cue => cue.EventName).SequenceEqual(["BaseUnderAttack", "TestRadarStructure"]), "one under-attack notification per 10 s and 250 units");
        cues.Clear();
        deriver.Derive(Event(5, SimEventKind.EntityDied, TestView.Base, TestView.Tank, string.Empty, 0), view, cues);
        deriver.Derive(Event(5, SimEventKind.EntityDied, TestView.Tank, TestView.Tank, string.Empty, 0), view, cues);
        Assert(cues.Single() is { EventName: "BuildingLost", Kind: SoundCueKind.Eva }, "BuildingLost; self-inflicted deaths are silent");
    }

    private static void RateLimiter()
    {
        SoundCue Cue(long tick, string name, float x = 0f, SoundCueKind kind = SoundCueKind.Effect)
            => new(tick, name, kind, new Vector3(x, 0f, 0f), EntityId.Invalid, PlayerId.System, 1f, 2, "test");
        var limiter = new SoundCueRateLimiter(new SoundCueLimits(MaxPerEventName: 3, WindowTicks: 10, CutoffDistance: 500f, MaxPerTick: 5));
        var burst = limiter.Filter(Enumerable.Range(0, 10).Select(_ => Cue(1, "Gun")));
        Assert(burst.Length == 3 && limiter.DroppedByRate == 7, "at most three per name inside the window");
        Assert(limiter.Filter([Cue(10, "gun")]).Length == 0, "the window is ten ticks and names ignore case");
        Assert(limiter.Filter([Cue(11, "Gun")]).Length == 1, "after the window the name is free again");
        Assert(limiter.Filter([Cue(12, "Far", 900f)], Vector3.Zero).Length == 0 && limiter.DroppedByDistance == 1, "distance cutoff");
        Assert(limiter.Filter([Cue(12, "FarEva", 900f, SoundCueKind.Eva)], Vector3.Zero).Length == 1, "EVA ignores distance");
        var many = limiter.Filter(Enumerable.Range(0, 8).Select(index => Cue(13, $"Name{index}")));
        Assert(many.Length == 5 && limiter.DroppedByTickCap == 3, "per-tick cap");
        var again = new SoundCueRateLimiter(limiter.Limits);
        var replay = again.Filter(Enumerable.Range(0, 10).Select(_ => Cue(1, "Gun")));
        Assert(replay.SequenceEqual(burst), "deterministic");
    }

    private static void RealData(string contentDirectory)
    {
        var clock = Stopwatch.StartNew();
        var content = ContentFileSystem.Open(contentDirectory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new ContentSource(content), ModuleRegistry.Default, options);
        var compileMs = clock.ElapsedMilliseconds;
        clock.Restart();
        var catalog = FxCatalog.Build(graph);
        var catalogMs = clock.ElapsedMilliseconds;
        var unexplained = catalog.UnexplainedDiagnostics;
        Assert(unexplained.Length == 0, "every retail FXList and particle system parses: " + string.Join("; ", unexplained.Take(10).Select(value => $"{value.Code} {value.Subject} {value.Detail}")));
        Assert(catalog.FxLists.Count > 400 && catalog.ParticleSystems.Count > 1000, "inventory size");
        Assert(graph.Definitions.Count(value => value is FxListDefinition) == catalog.FxLists.Count, "every FXList definition is in the catalog");
        Assert(graph.Definitions.Count(value => value is FxDefinition) == catalog.ParticleSystems.Count, "every ParticleSystem definition is in the catalog");
        var report = FxAudit.Run(graph, catalog, content.Exists);
        Assert(report.IsClean, "the audit is clean");

        clock.Restart();
        var sweep = FxAudit.ExecuteAll(catalog);
        var sweepMs = clock.ElapsedMilliseconds;
        var again = FxAudit.ExecuteAll(catalog);
        Assert(sweep.Checksum == again.Checksum && sweep.Counters.SequenceEqual(again.Counters), "the sweep is deterministic");
        Assert(sweep.FxListsExecuted == catalog.FxLists.Count && sweep.ParticleSystemsSpawned == catalog.ParticleSystems.Count, "everything executed");
        Assert(sweep.PeakParticles > 0, "particles were produced");
        var nuggets = catalog.FxLists.Values.SelectMany(list => list.Nuggets).GroupBy(nugget => nugget.Kind).OrderBy(group => group.Key)
            .Select(group => $"{group.Key}={group.Count()}");
        Console.WriteLine($"    real data: {catalog.FxLists.Count} FXLists ({string.Join(", ", nuggets)}), {catalog.ParticleSystems.Count} particle systems, "
            + $"{catalog.Diagnostics.Length} explained diagnostics, {report.MissingTextures} missing textures");
        Console.WriteLine($"    sweep: {sweep.Frames} frames, peak {sweep.PeakParticles} particles, {sweep.Particles.ParticlesCreated} created, "
            + $"{sweep.SoundCues} cues, checksum {sweep.Checksum:X16}");
        Console.WriteLine($"    timing: compile {compileMs} ms, catalog {catalogMs} ms, sweep {sweepMs} ms ({((double)sweepMs / sweep.Frames).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} ms/frame)");
    }

    // ------------------------------------------------------------------------------------------ helpers

    private sealed class TestView : IFxWorldView
    {
        public static readonly EntityId Tank = new(1);
        public static readonly EntityId Base = new(2);
        public Vector3 TankPosition = new(0f, 0f, 0f);
        public bool TankGone;

        public FxEntityPose? GetPose(EntityId entity)
            => entity == Tank && !TankGone ? new FxEntityPose(TankPosition, 0f, "TestTank", new PlayerId(1), 0, 12f)
                : entity == Base ? new FxEntityPose(new Vector3(200f, 0f, 0f), 0f, "TestBase", new PlayerId(2), 0, 30f)
                : null;

        public Vector3? GetBonePosition(EntityId entity, string boneName)
            => entity == Tank && boneName is "MUZZLE" or "MUZZLE01" or "MUZZLE02" ? TankPosition + new Vector3(0f, 0f, 3f) : null;

        public Vector3? GetWeaponMuzzle(EntityId entity, string weaponName) => null;
        public string? GetTemplateName(int templateIndex) => templateIndex == 7 ? "TestTank" : null;
        public float GroundHeight(float x, float y) => 0f;
        public bool IsShrouded(Vector3 position) => false;
    }

    private static bool Near(float a, float b, float tolerance = 1e-6f) => MathF.Abs(a - b) <= tolerance;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

file sealed class ContentSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}

file sealed class FxRunner
{
    private int _failed;
    private int _passed;

    public void Test(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"  ok   {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.Error.WriteLine($"  FAIL {name}: {exception.Message}");
            Console.Error.WriteLine(exception.StackTrace);
        }
    }

    public int Finish(string suite)
    {
        Console.WriteLine($"{suite}: {_passed} passed, {_failed} failed.");
        return _failed;
    }
}
