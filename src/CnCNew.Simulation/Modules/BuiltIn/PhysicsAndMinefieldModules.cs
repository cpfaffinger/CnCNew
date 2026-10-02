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
    public static PhysicsData Parse(ModuleConfiguration configuration) => new(
        ModuleConfigReader.Real(configuration, "Mass", Fixed.One),
        ModuleConfigReader.Bool(configuration, "AllowBouncing", false),
        ModuleConfigReader.Bool(configuration, "AllowCollideForce", true),
        ModuleConfigReader.Bool(configuration, "KillWhenRestingOnGround", false));
}

/// <summary>
/// <c>PhysicsUpdate.cpp</c>: the reference module only acts on forces (<c>applyForce</c>, <c>applyShock</c>, <c>applyMotiveForce</c>) and on
/// terrain contact, and sleeps forever until one arrives. This simulation has neither terrain nor any force source (no knockback, no
/// crash, no bouncing debris, no locomotor thrust), so the module is exactly the reference's resting state: bound with the full strict
/// schema, parked forever, never moving the object. Ground movement comes from the move-order system in the meantime.
/// Forces and terrain contact are phase 6 (<c>docs/GAME-RULES.md</c> §7a).
/// </summary>
public sealed class PhysicsBehavior(PhysicsData data) : SimModule, IUpdateModule
{
    private long _nextWake = SimulationTiming.SleepForever;

    public override string ModuleName => "PhysicsBehavior";
    public PhysicsData Data => data;
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public long Update(IWorldServices world) => SimulationTiming.SleepForever;

    public override void Describe(IStateWriter writer) => writer.Int("nextWake", _nextWake);
    public override void Load(IStateReader reader) => _nextWake = reader.Int("nextWake");
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
    public static GenerateMinefieldData Parse(ModuleConfiguration configuration, UpgradeCatalog upgrades) => new(
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
            if (dx * dx + dy * dy < limit * limit) return;
        }
        var id = world.RequestSpawn(mine.Index, Entity.Owner, x, y, Entity.Z, orientation);
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
