using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

// ========================================================================================== PhysicsBehavior / SquishCollide

/// <summary>The <c>PhysicsBehavior</c> fields the module carries; friction, shock, fall-damage and crash-weapon keys are typed by the catalog schema but have no consumer yet.</summary>
public sealed record PhysicsData(Fixed Mass, bool AllowBouncing, bool AllowCollideForce, bool KillWhenRestingOnGround) : ModuleData
{
    public static PhysicsData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Real(configuration, "Mass", Fixed.One),
        ModuleConfigReader.Bool(configuration, "AllowBouncing", false),
        ModuleConfigReader.Bool(configuration, "AllowCollideForce", true),
        ModuleConfigReader.Bool(configuration, "KillWhenRestingOnGround", false));
}

/// <summary>
/// <c>PhysicsUpdate.cpp</c>: the reference module acts on forces (<c>applyForce</c>, <c>applyShock</c>, <c>applyMotiveForce</c>) and on terrain contact, and sleeps until one arrives. This
/// simulation has no force source (no knockback, no bouncing debris, no locomotor thrust), so the module is the reference's resting state — except for one force, gravity on an object that is
/// let go in the air (a bomb leaving its plane, <see cref="StartFall"/>): it then falls with <c>GravityPerTickSquared</c> and the horizontal speed it was given, lands on the terrain and rests
/// there (<c>KillWhenRestingOnGround</c> kills it; the bomb's <c>HeightDieUpdate</c> normally ends it a tick earlier, one unit above the ground). Ground movement comes from the move-order system.
/// </summary>
public sealed class PhysicsBehavior(PhysicsData data) : SimModule, IUpdateModule
{
    /// <summary><c>getDisabledTypesToProcess</c>.</summary>
    public DisabledMask DisabledTypesToProcess => DisabledMask.All;

    private long _nextWake = SimulationTiming.SleepForever;
    private bool _falling;
    private Fixed _velocityX;
    private Fixed _velocityY;
    private Fixed _velocityZ;
    // The ticks of the current frame of the original already fallen (0: this tick takes the frame's step). Always 0 at 30 Hz (and then not written).
    private int _subStep;

    public override string ModuleName => "PhysicsBehavior";
    public PhysicsData Data => data;
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary>The object is in free fall.</summary>
    public bool Falling => _falling;

    /// <summary>
    /// Lets the object go: it keeps (<paramref name="velocityX"/>, <paramref name="velocityY"/>) per logic frame of the original horizontally and accelerates downwards. The fall is
    /// stepped once per frame, on the object's own phase (the first step one frame after the release), exactly as at 30 Hz: gravity per frame² is added to the velocity per frame,
    /// the landing is judged on the whole step. The step of a frame is fallen in equal shares over its ticks (docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md).
    /// </summary>
    public void StartFall(IWorldServices world, Fixed velocityX, Fixed velocityY)
    {
        _falling = true;
        _velocityX = velocityX;
        _velocityY = velocityY;
        _velocityZ = Fixed.Zero;
        _subStep = 0;
        world.WakeAt(Entity, this, world.Tick + world.Time.OriginalFrames(SimulationTiming.SleepNone));
    }

    public long Update(IWorldServices world)
    {
        if (!_falling) return SimulationTiming.SleepForever;
        var time = world.Time;
        if (_subStep != 0)
        {
            // Between two frames: one more share of the frame's step.
            Entity.X += time.FrameShare(_velocityX, _subStep);
            Entity.Y += time.FrameShare(_velocityY, _subStep);
            Entity.Z += time.FrameShare(_velocityZ, _subStep);
            _subStep = time.NextSubStep(_subStep);
            world.MarkDirty(Entity);
            return SimulationTiming.SleepNone;
        }
        _velocityZ += ProjectileModules.GravityPerFrameSquared(world);
        var beforeX = Entity.X;
        var beforeY = Entity.Y;
        var beforeZ = Entity.Z;
        Entity.X += _velocityX;
        Entity.Y += _velocityY;
        Entity.Z += _velocityZ;
        var ground = world.GroundHeightAt(Entity.X, Entity.Y);
        if (Entity.Z <= ground)
        {
            Entity.Z = ground;
            _falling = false;
            world.MarkDirty(Entity);
            if (data.KillWhenRestingOnGround) world.Kill(Entity.Id);
            return SimulationTiming.SleepForever;
        }
        if (time.TicksPerOriginalFrame != 1)
        {
            // The landing was judged on the whole step of this frame, as at 30 Hz; the object itself falls the first share of it now and the rest on the next ticks.
            Entity.X = beforeX + time.FrameShare(_velocityX, 0);
            Entity.Y = beforeY + time.FrameShare(_velocityY, 0);
            Entity.Z = beforeZ + time.FrameShare(_velocityZ, 0);
            _subStep = time.NextSubStep(0);
        }
        world.MarkDirty(Entity);
        return SimulationTiming.SleepNone;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        if (!_falling) return;
        writer.Bool("falling", true);
        writer.Fixed("velocityX", _velocityX);
        writer.Fixed("velocityY", _velocityY);
        writer.Fixed("velocityZ", _velocityZ);
        if (_subStep != 0) writer.Int("subStep", _subStep);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _falling = reader.Has("falling");
        _velocityX = _falling ? reader.Fixed("velocityX") : Fixed.Zero;
        _velocityY = _falling ? reader.Fixed("velocityY") : Fixed.Zero;
        _velocityZ = _falling ? reader.Fixed("velocityZ") : Fixed.Zero;
        _subStep = _falling && reader.Has("subStep") ? checked((int)reader.Int("subStep")) : 0;
    }
}

/// <summary>
/// <c>SquishCollide.cpp</c>: <c>onCollide</c> crushes an infantry unit a moving vehicle runs over. The simulation has no collision
/// detection (objects do not push, block or overlap-test each other yet), so <c>onCollide</c> is never called and the module carries no
/// state; it exists so the vehicles that have it can be instantiated. Collisions and crushing are phase 6.
/// </summary>
public sealed class SquishCollide : SimModule
{
    public override string ModuleName => "SquishCollide";
    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

// ========================================================================================== GenerateMinefieldBehavior

public sealed record GenerateMinefieldData(
    UpgradeMuxData UpgradeMux, string MineName, string UpgradedMineName, string UpgradedTrigger, string GenerationFx, Fixed? DistanceAroundObject,
    bool GenerateOnlyOnDeath, bool BorderOnly, bool SmartBorder, bool SmartBorderSkipInterior, bool AlwaysCircular, bool Upgradable,
    Fixed RandomJitter, Fixed SkipIfUnderStructure) : ModuleData
{
    public static GenerateMinefieldData Parse(RatedModuleConfiguration configuration, UpgradeCatalog upgrades) => new(
        UpgradeMuxData.Parse(configuration, upgrades),
        ModuleConfigReader.Text(configuration, "MineName"),
        ModuleConfigReader.Text(configuration, "UpgradedMineName"),
        ModuleConfigReader.Text(configuration, "UpgradedTriggeredBy"),
        ModuleConfigReader.Text(configuration, "GenerationFX"),
        ModuleConfigReader.Find(configuration, "DistanceAroundObject") is null ? null : ModuleConfigReader.Real(configuration, "DistanceAroundObject", Fixed.Zero),
        ModuleConfigReader.Bool(configuration, "GenerateOnlyOnDeath", false),
        ModuleConfigReader.Bool(configuration, "BorderOnly", true),
        ModuleConfigReader.Bool(configuration, "SmartBorder", false),
        ModuleConfigReader.Bool(configuration, "SmartBorderSkipInterior", true),
        ModuleConfigReader.Bool(configuration, "AlwaysCircular", false),
        ModuleConfigReader.Bool(configuration, "Upgradable", false),
        ModuleConfigReader.Real(configuration, "RandomJitter", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "SkipIfThisMuchUnderStructure", Fixed.FromDecimal(0.33m)));
}

/// <summary>
/// <c>GenerateMinefieldBehavior.cpp</c>: lays a ring of mine objects around the owner, either when the module's <c>TriggeredBy</c> upgrade
/// completes or, with <c>GenerateOnlyOnDeath</c>, when the owner dies; an <c>Upgradable</c> minefield swaps its mines for the
/// <c>UpgradedMineName</c> ones once the owner has completed <c>UpgradedTriggeredBy</c>. Ported: the <c>m_generated</c>/<c>m_upgraded</c>
/// state machine, <c>update()</c> (polls every tick while an upgrade is still possible, as the reference does), the ring geometry of
/// <c>placeMines</c> with <c>SmartBorder</c> + circular footprint (all 63 uses in the shipped data), the draw order of the logic
/// stream (jitter x, jitter y, orientation per mine) and the "mostly under a structure" veto (the mine's footprint shrunk to
/// <c>SkipIfThisMuchUnderStructure</c> of its radius against every STRUCTURE, approximated by bounding circles).
/// Not modeled, and never silently: terrain vetoes (water, cliffs; no terrain yet), <c>setScootParms</c> (the mine's slide from the producer, part of
/// <c>MinefieldBehavior</c>), and the rectangular-border / footprint-fill layouts (no shipped use); those report
/// <see cref="CreationSkipReasons.SettingNotSimulated"/>. Mines that are not instantiable themselves (<c>MinefieldBehavior</c> is a module of its own)
/// report <see cref="CreationSkipReasons.TemplateNotInstantiable"/> once per placement.
/// </summary>
public sealed class GenerateMinefieldBehavior(GenerateMinefieldData data) : SimModule, IUpdateModule, IUpgradeModule, IDieModule
{
    private readonly UpgradeMuxState _mux = new(data.UpgradeMux);
    private readonly List<EntityId> _mines = [];
    private long _nextWake = -1;
    private bool _generated;
    private bool _upgraded;
    private UpgradeMask? _upgradedMask;

    public override string ModuleName => "GenerateMinefieldBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
        => _nextWake = data.Upgradable ? world.Tick + SimulationTiming.SleepNone : SimulationTiming.SleepForever;

    public void TryActivate(IWorldServices world, UpgradeMask keyMask) => _mux.AttemptUpgrade(world, Entity, keyMask, PlaceMines);
    public void TryReset(IWorldServices world, UpgradeMask keyMask) => _mux.ResetUpgrade(keyMask);
    public bool WouldUpgrade(UpgradeMask keyMask) => _mux.WouldUpgrade(keyMask);

    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (data.GenerateOnlyOnDeath) PlaceMines(world);
    }

    public long Update(IWorldServices world)
    {
        if (_upgraded || !data.Upgradable) return SimulationTiming.SleepForever;
        if (_generated && Entity.CompletedUpgrades.TestForAny(UpgradedMask(world)))
        {
            _upgraded = true;
            foreach (var id in _mines)
                if (world.FindEntity(id) is not null) world.RequestDestroy(id);
            _mines.Clear();
            _generated = false;
            PlaceMines(world);
        }
        return SimulationTiming.SleepNone;
    }

    private UpgradeMask UpgradedMask(IWorldServices world)
        => _upgradedMask ??= data.UpgradedTrigger.Length == 0 ? UpgradeMask.Empty : world.Upgrades.MaskOf([data.UpgradedTrigger]);

    private static Fixed BoundingRadius(ObjectTemplate template) => template.BoundingCircleRadius;

    private void PlaceMines(IWorldServices world)
    {
        if (_generated) return;
        _generated = true;

        var mineName = _upgraded ? data.UpgradedMineName : data.MineName;
        if (!world.Templates.TryGet(mineName, out var mine))
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, mineName, CreationSkipReasons.UnknownTemplate);
            return;
        }

        if (!data.SmartBorder || (!data.AlwaysCircular && Entity.Template.Geometry.Equals("BOX", StringComparison.OrdinalIgnoreCase)))
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid,
                data.SmartBorder ? "GenerateMinefieldBehavior rectangular border" : data.BorderOnly ? "GenerateMinefieldBehavior plain border" : "GenerateMinefieldBehavior footprint fill",
                CreationSkipReasons.SettingNotSimulated);
            return;
        }

        var mineRadius = BoundingRadius(mine);
        if (!mine.IsInstantiable || mineRadius <= Fixed.Zero)
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, mineName, mine.IsInstantiable ? CreationSkipReasons.NoObjectNamed : CreationSkipReasons.TemplateNotInstantiable);
        else
        {
            if (!data.SmartBorderSkipInterior) PlaceMineAt(world, mine, mineRadius, Entity.X, Entity.Y);
            var radius = (data.SmartBorderSkipInterior ? BoundingRadius(Entity.Template) : mineRadius) + mineRadius;
            var limit = data.DistanceAroundObject ?? world.Templates.StandardMinefieldDistance;
            do
            {
                PlaceRing(world, mine, mineRadius, radius);
                radius += mineRadius * 2L;
            }
            while (radius < limit);
        }

        if (data.GenerationFx.Length != 0) world.Emit(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, data.GenerationFx, 0);
    }

    private void PlaceRing(IWorldServices world, ObjectTemplate mine, Fixed mineRadius, Fixed radius)
    {
        var mineDiameter = mineRadius * 2L;
        var count = Math.Max(1L, (FixedTrig.TwoPi * radius / mineDiameter).Ceiling());
        var increment = FixedTrig.TwoPi / Fixed.FromInt(count);
        var limit = FixedTrig.TwoPi - increment / Fixed.FromInt(2);
        var jitter = mineRadius * data.RandomJitter;
        for (var angle = Fixed.Zero; angle < limit; angle += increment)
        {
            var x = Entity.X + radius * FixedTrig.Cos(angle);
            var y = Entity.Y + radius * FixedTrig.Sin(angle);
            x += world.LogicRandom.Range(-jitter, jitter);
            y += world.LogicRandom.Range(-jitter, jitter);
            PlaceMineAt(world, mine, mineRadius, x, y);
        }
    }

    private void PlaceMineAt(IWorldServices world, ObjectTemplate mine, Fixed mineRadius, Fixed x, Fixed y)
    {
        var orientation = world.LogicRandom.Range(-FixedTrig.Pi, FixedTrig.Pi);
        var reach = mineRadius * data.SkipIfUnderStructure;
        foreach (var other in world.Entities)
        {
            if (!other.Template.IsKindOf("STRUCTURE")) continue;
            var (dx, dy) = (other.X - x, other.Y - y);
            var limit = BoundingRadius(other.Template) + reach;
            if (SquaredReach.Of(limit).ContainsStrictly(dx * dx + dy * dy)) return;
        }
        // GenerateMinefieldBehavior.cpp: mine->setProducer(producer) — the mine stops regenerating when this object is gone.
        var id = world.RequestSpawn(mine.Index, Entity.Owner, x, y, Entity.Z, orientation, new SpawnExtras(Fixed.Zero, Producer: Entity.Id));
        if (data.Upgradable) _mines.Add(id);
    }

    public override void Describe(IStateWriter writer)
    {
        _mux.Describe(writer);
        writer.Int("nextWake", _nextWake);
        writer.Bool("generated", _generated);
        writer.Bool("upgraded", _upgraded);
        writer.BeginArray("mines");
        foreach (var id in _mines)
        {
            writer.BeginObject(null);
            writer.UInt("id", id.Value);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _mux.Load(reader);
        _nextWake = reader.Int("nextWake");
        _generated = reader.Bool("generated");
        _upgraded = reader.Bool("upgraded");
        _mines.Clear();
        foreach (var entry in reader.Array("mines")) _mines.Add(new EntityId(checked((uint)entry.UInt("id"))));
    }
}
