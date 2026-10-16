using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Views;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Fx;

/// <summary>One slot of <c>BoneFXUpdate</c>: <c>{State}ParticleSystemN</c> or <c>{State}FXListN</c> = <c>Bone:NAME OnlyOnce:Yes|No MIN MAX PSys:|FXList:NAME</c>.</summary>
/// <param name="State">0 pristine, 1 damaged, 2 really damaged, 3 rubble.</param>
/// <param name="MinFrames">The delay range in logic frames of the original (the file writes milliseconds, <c>parseDurationReal</c>).</param>
public sealed record BoneFxSlot(int State, bool IsParticleSystem, string Bone, bool OnlyOnce, float MinFrames, float MaxFrames, string Name);

/// <summary>One slot of <c>TransitionDamageFX</c>: <c>{State}ParticleSystemN</c> or <c>{State}FXListN</c> = <c>Bone:NAME RandomBone:Yes|No PSys:NAME</c> or <c>Loc: X: Y: Z: PSys:NAME</c>.</summary>
public sealed record TransitionFxSlot(int State, bool IsParticleSystem, string? Bone, bool RandomBone, Vector3 Location, string Name);

/// <summary>What an object template carries of the attached effects that are not FX lists of events.</summary>
/// <param name="HasActiveBody">The body is an <c>ActiveBody</c> or one of its kinds: it shows <c>ActiveBody::updateBodyParticleSystems</c>'s fire and smoke.</param>
/// <param name="MissileIgnitionFrames">For a projectile with <c>MissileAIUpdate</c> its <c>IgnitionDelay</c> in logic frames of the original (the exhaust starts then); null for every other object.</param>
public sealed record AttachedEffectTemplate(ImmutableArray<BoneFxSlot> BoneFx, ImmutableArray<TransitionFxSlot> Transition, bool HasActiveBody, long? MissileIgnitionFrames);

/// <summary>One category of <c>ActiveBody</c>'s automatic particles (<c>GameData.ini</c> <c>AutoFireParticleSmallPrefix</c> etc.): bones named PREFIX01, PREFIX02 ... of the current model.</summary>
public sealed record BodyParticleRule(string Prefix, string System, int Max);

/// <summary>
/// The definitions of every continuous effect an object carries outside the FX lists of events (docs/PRESENTATION-FX.md, "Attached effects"): compiled once from the
/// definition graph. <see cref="BodyRules"/> are the seven categories of <c>ActiveBody</c> in the order small, medium, large fire, small, medium, large smoke, aflame.
/// </summary>
public sealed class AttachedEffectCatalog
{
    public const int FireSmall = 0, FireMedium = 1, FireLarge = 2, SmokeSmall = 3, SmokeMedium = 4, SmokeLarge = 5, Aflame = 6;

    private static readonly string[] BodyKeys = ["AutoFireParticleSmall", "AutoFireParticleMedium", "AutoFireParticleLarge", "AutoSmokeParticleSmall", "AutoSmokeParticleMedium",
        "AutoSmokeParticleLarge", "AutoAflameParticle"];

    /// <summary>The bodies that are an <c>ActiveBody</c> (<c>ActiveBody.h</c> and its subclasses); <c>InactiveBody</c> shows nothing.</summary>
    private static readonly string[] ActiveBodies = ["ActiveBody", "StructureBody", "HighlanderBody", "ImmortalBody", "HiveStructureBody", "UndeadBody"];

    private static readonly string[] StateNames = ["Pristine", "Damaged", "ReallyDamaged", "Rubble"];

    public ImmutableDictionary<string, AttachedEffectTemplate> Objects { get; private init; } = ImmutableDictionary<string, AttachedEffectTemplate>.Empty;
    public ImmutableArray<BodyParticleRule> BodyRules { get; private init; } = [];

    /// <summary>Per weapon the <c>ProjectileExhaust</c> of the four veterancy levels (<c>VeterancyProjectileExhaust</c> overrides one), null where none.</summary>
    public ImmutableDictionary<string, ImmutableArray<string?>> WeaponExhausts { get; private init; } = ImmutableDictionary<string, ImmutableArray<string?>>.Empty;

    /// <summary>Per weapon the stream object its projectiles draw (<c>ProjectileStreamName</c>) with that object's <c>W3DProjectileStreamDraw</c>.</summary>
    public ImmutableDictionary<string, StreamSpec> WeaponStreams { get; private init; } = ImmutableDictionary<string, StreamSpec>.Empty;

    /// <summary>Values the parser could not read, each with its place: counted, never guessed at.</summary>
    public ImmutableArray<string> Problems { get; private init; } = [];

    public static AttachedEffectCatalog Empty { get; } = new();

    public static AttachedEffectCatalog Build(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var objects = ImmutableDictionary.CreateBuilder<string, AttachedEffectTemplate>(StringComparer.OrdinalIgnoreCase);
        var exhausts = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string?>>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        var streamNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var streamDraws = new Dictionary<string, StreamSpec>(StringComparer.OrdinalIgnoreCase);
        var rules = new BodyParticleRule[BodyKeys.Length];
        for (var index = 0; index < rules.Length; index++) rules[index] = new BodyParticleRule(string.Empty, string.Empty, 0);
        foreach (var definition in graph.Definitions)
        {
            switch (definition)
            {
                case ObjectDefinition obj:
                    if (Compile(obj, problems) is { } template) objects[obj.OriginalName] = template;
                    if (obj.Modules.FirstOrDefault(module => module.Name.Equals("W3DProjectileStreamDraw", StringComparison.Ordinal)) is { } stream)
                    {
                        string Value(string key, string fallback) => stream.Fields.LastOrDefault(field => field.Name.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim() ?? fallback;
                        float Number(string key, float fallback) => float.TryParse(Value(key, string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : fallback;
                        streamDraws[obj.OriginalName] = new StreamSpec(Value("Texture", string.Empty), Number("Width", 0f), Number("TileFactor", 0f), Number("ScrollRate", 0f), (int)Number("MaxSegments", 0f));
                    }
                    break;
                case WeaponDefinition weapon:
                    var levels = new string?[4];
                    foreach (var field in weapon.Fields)
                    {
                        var tokens = ValueGrammar.Tokenize(field.Value.OriginalText);
                        if (field.Name.Equals("ProjectileExhaust", StringComparison.OrdinalIgnoreCase) && tokens.Length > 0)
                            for (var level = 0; level < levels.Length; level++) levels[level] = Named(tokens[0]);
                        else if (field.Name.Equals("VeterancyProjectileExhaust", StringComparison.OrdinalIgnoreCase) && tokens.Length >= 2 && Level(tokens[0]) is { } level)
                            levels[level] = Named(tokens[1]);
                    }
                    if (levels.Any(value => value is not null)) exhausts[weapon.OriginalName] = [.. levels];
                    if (weapon.Fields.LastOrDefault(field => field.Name.Equals("ProjectileStreamName", StringComparison.OrdinalIgnoreCase)) is { } streamField
                        && ValueGrammar.Tokenize(streamField.Value.OriginalText).FirstOrDefault() is { } streamName && Named(streamName) is not null)
                        streamNames[weapon.OriginalName] = streamName;
                    break;
                case GlobalDataDefinition global when global.Id.Kind == DefinitionKind.GameData:
                    for (var index = 0; index < BodyKeys.Length; index++)
                    {
                        string? Text(string key) => global.Fields.LastOrDefault(field => field.Name.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim();
                        var max = 0;
                        if (Text(BodyKeys[index] + "Max") is { } maxText && !int.TryParse(maxText, NumberStyles.Integer, CultureInfo.InvariantCulture, out max))
                            problems.Add($"GameData {BodyKeys[index]}Max: '{maxText}' is not a number");
                        rules[index] = new BodyParticleRule((Text(BodyKeys[index] + "Prefix") ?? string.Empty).ToLowerInvariant(), Text(BodyKeys[index] + "System") ?? string.Empty, Math.Max(0, max));
                    }
                    break;
            }
        }
        var streams = ImmutableDictionary.CreateBuilder<string, StreamSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var (weapon, name) in streamNames)
        {
            if (streamDraws.TryGetValue(name, out var spec)) streams[weapon] = spec;
            else problems.Add($"Weapon {weapon}: the stream object '{name}' has no W3DProjectileStreamDraw");
        }
        return new AttachedEffectCatalog { Objects = objects.ToImmutable(), BodyRules = [.. rules], WeaponExhausts = exhausts.ToImmutable(), WeaponStreams = streams.ToImmutable(), Problems = [.. problems] };
    }

    private static string? Named(string token) => token.Length == 0 || token.Equals("None", StringComparison.OrdinalIgnoreCase) ? null : token;

    private static int? Level(string token) => token.ToUpperInvariant() switch
    {
        "REGULAR" => 0,
        "VETERAN" => 1,
        "ELITE" => 2,
        "HEROIC" => 3,
        _ => null,
    };

    private static AttachedEffectTemplate? Compile(ObjectDefinition obj, List<string> problems)
    {
        var bone = ImmutableArray.CreateBuilder<BoneFxSlot>();
        var transition = ImmutableArray.CreateBuilder<TransitionFxSlot>();
        var activeBody = false;
        long? ignition = null;
        foreach (var module in obj.Modules)
        {
            if (module.BlockKind == ModuleBlockKind.Body && ActiveBodies.Contains(module.Name, StringComparer.Ordinal)) activeBody = true;
            if (module.Name.Equals("MissileAIUpdate", StringComparison.Ordinal))
            {
                var delay = module.Fields.LastOrDefault(field => field.Name.Equals("IgnitionDelay", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim();
                ignition = delay is not null && decimal.TryParse(delay, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds) ? (long)Math.Ceiling(milliseconds * CnCNew.Data.OriginalFrame.PerSecond / 1000) : 0;
            }
            var isBone = module.Name.Equals("BoneFXUpdate", StringComparison.Ordinal);
            var isTransition = module.Name.Equals("TransitionDamageFX", StringComparison.Ordinal);
            if (!isBone && !isTransition) continue;
            foreach (var field in module.Fields)
            {
                for (var state = 0; state < StateNames.Length; state++)
                {
                    var particle = field.Name.StartsWith(StateNames[state] + "ParticleSystem", StringComparison.OrdinalIgnoreCase);
                    var fxList = field.Name.StartsWith(StateNames[state] + "FXList", StringComparison.OrdinalIgnoreCase);
                    if (!particle && !fxList) continue;
                    // "ReallyDamaged..." also starts with "Damaged"? No - but "Pristine"/"Damaged" must not take "ReallyDamaged": the prefix test is exact from the start.
                    var slot = ParseSlot(field.Value.OriginalText);
                    if (slot.Name is null)
                    {
                        if (!field.Value.OriginalText.Trim().Equals("None", StringComparison.OrdinalIgnoreCase) && field.Value.OriginalText.Trim().Length > 0)
                            problems.Add($"{obj.OriginalName} {module.Name} {field.Name}: '{field.Value.OriginalText.Trim()}' names no effect");
                        break;
                    }
                    if (isBone) bone.Add(new BoneFxSlot(state, particle, (slot.Bone ?? string.Empty).ToLowerInvariant(), slot.OnlyOnce, slot.Min, slot.Max, slot.Name));
                    else transition.Add(new TransitionFxSlot(state, particle, slot.Bone?.ToLowerInvariant(), slot.RandomBone, slot.Location, slot.Name));
                    break;
                }
            }
        }
        if (bone.Count == 0 && transition.Count == 0 && !activeBody && ignition is null) return null;
        return new AttachedEffectTemplate(bone.ToImmutable(), transition.ToImmutable(), activeBody, ignition);
    }

    private readonly record struct Slot(string? Bone, bool OnlyOnce, float Min, float Max, string? Name, bool RandomBone, Vector3 Location);

    /// <summary>
    /// The value of a slot as <c>BoneFXUpdate.cpp</c> (<c>parseBoneFXParticleSystem</c>) and <c>TransitionDamageFX.cpp</c> (<c>parseFXLocInfo</c>) read it: <c>Key:Value</c> tokens
    /// (the value may also follow as the next token), and for the bone effect two bare numbers, the delay range in milliseconds.
    /// </summary>
    private static Slot ParseSlot(string text)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string? bone = null, name = null;
        var onlyOnce = false;
        var randomBone = false;
        var numbers = new List<float>();
        var location = Vector3.Zero;
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            var colon = token.IndexOf(':');
            if (colon < 0)
            {
                if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) numbers.Add(number);
                continue;
            }
            var key = token[..colon].ToUpperInvariant();
            var value = token[(colon + 1)..];
            if (value.Length == 0 && key is not "LOC" && index + 1 < tokens.Length && !tokens[index + 1].Contains(':', StringComparison.Ordinal)) value = tokens[++index];
            switch (key)
            {
                case "BONE": bone = value; break;
                case "ONLYONCE": onlyOnce = value.Equals("Yes", StringComparison.OrdinalIgnoreCase); break;
                case "RANDOMBONE": randomBone = value.Equals("Yes", StringComparison.OrdinalIgnoreCase); break;
                case "PSYS" or "FXLIST": name = Named(value); break;
                case "X" when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x): location.X = x; break;
                case "Y" when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y): location.Y = y; break;
                case "Z" when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var z): location.Z = z; break;
            }
        }
        var min = numbers.Count > 0 ? numbers[0] * CnCNew.Data.OriginalFrame.PerSecond / 1000f : 0f;
        var max = numbers.Count > 1 ? numbers[1] * CnCNew.Data.OriginalFrame.PerSecond / 1000f : min;
        return new Slot(bone, onlyOnce, min, max, name, randomBone, location);
    }
}

/// <summary>The keys of a <c>W3DProjectileStreamDraw</c>: <c>Texture</c>, <c>Width</c>, <c>TileFactor</c>, <c>ScrollRate</c>, <c>MaxSegments</c> (0: every point).</summary>
public sealed record StreamSpec(string Texture, float Width, float TileFactor, float ScrollRate, int MaxSegments);

/// <summary>One object as the projector captured it this tick: what <see cref="AttachedEffects"/> needs of it.</summary>
public readonly record struct ProjectedEntity(EntityId Entity, ObjectVisual Visual, ModelConditionSet Conditions, ImmutableArray<DrawModuleSelection> Selections);

/// <summary>Counters of <see cref="AttachedEffects"/>, for tests and the client's report.</summary>
public sealed record AttachedEffectStats(long ModelStateSystems, long BodySystems, long BoneFxSystems, long BoneFxLists, long TransitionSystems, long TransitionFxLists, long ExhaustSystems,
    long MissingTemplates, long MissingBones, int Tracked)
{
    /// <summary>Projectiles that joined a stream, and the bands of the last update.</summary>
    public long StreamProjectiles { get; init; }
    public int StreamsNow { get; init; }

    /// <summary>Systems of the tank and truck draws started because the vehicle moved.</summary>
    public long MotionSystems { get; init; }
}

/// <summary>
/// The continuous effects an object carries (docs/PRESENTATION-FX.md, "Attached effects") - the presentation part of mechanisms the simulation either does not have or keeps as
/// an empty shell, rewritten after the original:
/// <list type="bullet">
/// <item><c>W3DModelDraw</c>'s <c>ParticleSysBone</c> of the current condition state of every draw module (<c>W3DModelDraw::setModelState</c>,
/// <c>recalcBonesForClientParticleSystems</c>, <c>stopClientParticleSystems</c>): created when the state is entered, destroyed when another state is entered or the object is
/// no longer shown; a bone the model lacks puts the system at the origin.</item>
/// <item><c>ActiveBody</c>'s fire and smoke (<c>ActiveBody::updateBodyParticleSystems</c>): rebuilt when the damage state or the aflame status changes, at the bones
/// PREFIX01, PREFIX02 ... of the current model, picked at random up to the category's maximum; aflame shifts fire up a size, turns smoke into small fire, doubles every maximum
/// and adds the aflame category. No bones, no systems.</item>
/// <item><c>BoneFXUpdate</c> (<c>changeBodyDamageState</c>, <c>update</c>, <c>doParticleSystemAtBone</c>): the slots of the current damage state, each after a random delay, once
/// or again and again; systems are destroyed when the state changes.</item>
/// <item><c>TransitionDamageFX</c> (<c>onBodyDamageStateChange</c>): on a worse damage state its particle systems at their bone (or a random numbered one, or a location) and its
/// FX lists; the systems of a state end when the object leaves it.</item>
/// <item>A missile's exhaust (<c>MissileAIUpdate::doIgnitionState</c>): the launching weapon's <c>ProjectileExhaust</c> of the launcher's veterancy, attached to the projectile
/// after its ignition delay.</item>
/// </list>
/// Every system is attached to its object (<see cref="ParticleSpawn.AttachTo"/>, with the bone as <see cref="ParticleSpawn.LocalOffset"/>) and dies with it. Bones are the
/// rest pose of the state's model (<paramref name="bones"/>: model name, lower-case bone name, model-space position, or null), scaled by the object's scale; bone rotations and
/// animated bones are not followed. The random choices come from a stream of this class's own; nothing here touches the simulation.
/// </summary>
public sealed class AttachedEffects(AttachedEffectCatalog catalog, FxExecutor fx, Func<string, string, Vector3?> bones, ulong seed)
{
    private const int MaxNumberedBones = 16;

    private sealed class Tracked
    {
        public int[] StateKey = [];
        public readonly List<int> StateSystems = [];
        public int DamageState = -1;
        public bool Aflame;
        public readonly List<int> BodySystems = [];
        public int BoneFxState = -1;
        public double[] BoneFxDue = [];
        public readonly List<int> BoneFxSystems = [];
        public readonly List<int>[] TransitionSystems = [[], [], [], []];
        public bool Shown;
        public readonly List<int> MotionSystems = [];
        public bool Moving;
        public Vector3? LastPosition;
    }

    private readonly FxRandom _random = new(seed ^ 0xA77AC4EDUL);

    /// <summary>
    /// <c>ProjectileStreamUpdate</c>: per launcher and weapon the projectiles in flight, oldest first (at most 20); a projectile aimed elsewhere than the one before breaks the
    /// line (an invalid id is the hole). The stream ends when it is empty and its launcher is gone.
    /// </summary>
    private sealed class Stream(StreamSpec spec)
    {
        public readonly StreamSpec Spec = spec;
        public readonly List<uint> Projectiles = [];
        public Vector3 LastTarget = new(float.NaN);
    }

    private const int MaxStreamProjectiles = 20;
    private readonly SortedDictionary<(uint Launcher, string Weapon), Stream> _streams = [];
    private ImmutableArray<StreamInstance> _streamFrame = [];
    private long _streamProjectiles;
    private long _motionSystems;

    /// <summary>The projectile streams of the last update, for the frame (<see cref="FxExecutor.ProjectileStreams"/>).</summary>
    public ImmutableArray<StreamInstance> ProjectileStreams => _streamFrame;
    private readonly SortedDictionary<uint, Tracked> _tracked = [];
    private readonly Dictionary<uint, ProjectedEntity> _current = [];
    private readonly List<ProjectedEntity> _ordered = [];
    private long _modelStateSystems, _bodySystems, _boneFxSystems, _boneFxLists, _transitionSystems, _transitionFxLists, _exhaustSystems, _missingTemplates, _missingBones;

    private readonly SortedSet<string> _missingBoneNames = new(StringComparer.Ordinal);

    /// <summary>The first 64 model bones a <c>ParticleSysBone</c> named and the model did not have (<c>model:bone</c>): the original puts those systems at the origin.</summary>
    public IReadOnlyCollection<string> MissingBoneNames => _missingBoneNames;

    public AttachedEffectStats Stats => new(_modelStateSystems, _bodySystems, _boneFxSystems, _boneFxLists, _transitionSystems, _transitionFxLists, _exhaustSystems, _missingTemplates,
        _missingBones, _tracked.Count) { StreamProjectiles = _streamProjectiles, StreamsNow = _streamFrame.Length, MotionSystems = _motionSystems };

    /// <summary>The events of a tick: a projectile launched by a weapon with an exhaust gets it (<c>ProjectileLaunched</c>: the projectile, its launcher, the weapon).</summary>
    public void Observe(IReadOnlyList<SimEvent> events, SimulationWorld world)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var simEvent in events)
        {
            if (simEvent.Kind != SimEventKind.ProjectileLaunched || !simEvent.Entity.IsValid) continue;
            if (catalog.WeaponStreams.TryGetValue(simEvent.Text, out var streamSpec))
            {
                var key = (simEvent.Other.Value, simEvent.Text);
                if (!_streams.TryGetValue(key, out var stream)) _streams[key] = stream = new Stream(streamSpec);
                var target = new Vector3(FixedConvert.ToSingle(simEvent.X), FixedConvert.ToSingle(simEvent.Y), FixedConvert.ToSingle(simEvent.Z));
                if (!float.IsNaN(stream.LastTarget.X) && Vector3.DistanceSquared(target, stream.LastTarget) > 1f) stream.Projectiles.Add(0);
                stream.LastTarget = target;
                stream.Projectiles.Add(simEvent.Entity.Value);
                while (stream.Projectiles.Count > MaxStreamProjectiles) stream.Projectiles.RemoveAt(0);
                _streamProjectiles++;
            }
            if (!catalog.WeaponExhausts.TryGetValue(simEvent.Text, out var exhausts)) continue;
            if (world.FindEntity(simEvent.Entity) is not { } projectile || !catalog.Objects.TryGetValue(projectile.Template.Name, out var template) || template.MissileIgnitionFrames is not { } ignition) continue;
            var level = Math.Clamp(world.FindEntity(simEvent.Other)?.VeterancyLevel ?? 0, 0, 3);
            if (exhausts[level] is not { } name) continue;
            if (Spawn(name, simEvent.Entity, Vector3.Zero, ignition > 0 ? (uint)ignition : null) != 0) _exhaustSystems++;
        }
    }

    /// <summary>
    /// After the projector advanced: every object it shows is brought up to date, every object it no longer shows loses what its model state and its body showed.
    /// <paramref name="originalFrame"/> is the logic frame of the original the world stands in; the delays of the bone effects count those frames.
    /// </summary>
    public void Update(SimulationWorld world, IEnumerable<ProjectedEntity> projected, IFxWorldView view, long originalFrame)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(projected);
        _current.Clear();
        _ordered.Clear();
        foreach (var item in projected)
        {
            _current[item.Entity.Value] = item;
            _ordered.Add(item);
        }
        var gone = new List<uint>();
        foreach (var (id, tracked) in _tracked)
        {
            var entity = world.FindEntity(new EntityId(id));
            if (entity is null || entity.EffectivelyDead && !_current.ContainsKey(id)) { gone.Add(id); continue; }
            if (!_current.ContainsKey(id) && tracked.Shown) Hide(tracked);
        }
        foreach (var id in gone) _tracked.Remove(id);
        UpdateStreams(world, view);
        foreach (var item in _ordered)
        {
            var id = item.Entity.Value;
            if (world.FindEntity(item.Entity) is not { } entity) continue;
            if (!_tracked.TryGetValue(id, out var tracked)) _tracked[id] = tracked = new Tracked();
            tracked.Shown = true;
            UpdateModelState(item, tracked);
            catalog.Objects.TryGetValue(item.Visual.TemplateName, out var template);
            var damage = entity.HasStatus(ObjectStatusBits.UnderConstruction) ? tracked.DamageState : entity.DamageState();
            var aflame = entity.HasStatus(AflameStatus);
            if (template is { HasActiveBody: true } && !entity.HasStatus(ObjectStatusBits.UnderConstruction) && (damage != tracked.DamageState || aflame != tracked.Aflame))
                RebuildBody(item, tracked, aflame);
            if (template is not null && damage >= 0 && damage != tracked.DamageState)
            {
                if (!template.Transition.IsDefaultOrEmpty) ChangeTransition(item, tracked, template, tracked.DamageState, damage, view, entity);
                if (!template.BoneFx.IsDefaultOrEmpty) StartBoneFx(template, tracked, damage, originalFrame);
            }
            if (damage >= 0) tracked.DamageState = damage;
            tracked.Aflame = aflame;
            if (template is not null && !template.BoneFx.IsDefaultOrEmpty) RunBoneFx(item, tracked, template, originalFrame, view, entity);
            UpdateMotion(item, tracked, view);
        }
    }

    private static readonly int AflameStatus = ObjectStatusBits.Index("AFLAME");

    /// <summary>
    /// The stream points of this tick (<c>ProjectileStreamUpdate::cullFrontOfList</c>, <c>getAllPoints</c>; <c>W3DProjectileStreamDraw</c>): dead projectiles leave the front,
    /// a dead one in the middle is a hole, every run of two or more points is one band, and <c>MaxSegments</c> keeps the newest points.
    /// </summary>
    private void UpdateStreams(SimulationWorld world, IFxWorldView view)
    {
        var frame = ImmutableArray.CreateBuilder<StreamInstance>();
        var ended = new List<(uint, string)>();
        foreach (var (key, stream) in _streams)
        {
            while (stream.Projectiles.Count > 0 && (stream.Projectiles[0] == 0 || world.FindEntity(new EntityId(stream.Projectiles[0])) is null)) stream.Projectiles.RemoveAt(0);
            if (stream.Projectiles.Count == 0)
            {
                if (world.FindEntity(new EntityId(key.Item1)) is null) ended.Add(key);
                continue;
            }
            var points = new List<Vector3>();
            foreach (var id in stream.Projectiles)
            {
                var entity = id == 0 ? null : world.FindEntity(new EntityId(id));
                if (entity is null || view.GetPose(entity.Id) is not { } pose)
                {
                    Flush(frame, stream.Spec, points);
                    continue;
                }
                points.Add(pose.Position);
            }
            Flush(frame, stream.Spec, points);
        }
        foreach (var key in ended) _streams.Remove(key);
        _streamFrame = frame.ToImmutable();
    }

    private static void Flush(ImmutableArray<StreamInstance>.Builder frame, StreamSpec spec, List<Vector3> points)
    {
        if (points.Count >= 2)
        {
            var kept = spec.MaxSegments > 0 && points.Count > spec.MaxSegments ? points.GetRange(points.Count - spec.MaxSegments, spec.MaxSegments) : points;
            frame.Add(new StreamInstance([.. kept], spec.Texture, spec.Width, spec.TileFactor));
        }
        points.Clear();
    }

    /// <summary>The world position of a bone of the object's current model (what <see cref="IFxWorldView.GetBonePosition"/> answers), or null.</summary>
    public Vector3? BonePosition(EntityId entity, string bone, FxEntityPose pose)
    {
        if (!_current.TryGetValue(entity.Value, out var item) || ModelOf(item) is not { } model) return null;
        var lower = bone.ToLowerInvariant();
        var local = bones(model, lower) ?? bones(model, lower + "01");
        return local is { } found ? Vector3.Transform(found * item.Visual.Scale, pose.Transform) : null;
    }

    /// <summary>
    /// The muzzle of the slot the object fires from now (<c>WeaponFireFXBone</c> of the current condition state, its first numbered bone when the name itself is not one), in
    /// world space, or null. The weapon of a shot is the current slot's - the event names the weapon, not the slot.
    /// </summary>
    public Vector3? MuzzlePosition(EntityId entity, int slot, FxEntityPose pose)
    {
        if (!_current.TryGetValue(entity.Value, out var item)) return null;
        for (var module = 0; module < item.Visual.Draws.Length && module < item.Selections.Length; module++)
        {
            var selection = item.Selections[module];
            if (selection.StateIndex < 0 || selection.Model is not { } model) continue;
            var state = item.Visual.Draws[module].States[selection.StateIndex];
            if (slot >= state.WeaponBones.Length || state.WeaponBones[slot].FireFxBone is not { } name) continue;
            if ((bones(model, name) ?? bones(model, name + "01")) is { } local) return Vector3.Transform(local * item.Visual.Scale, pose.Transform);
        }
        return null;
    }

    private static string? ModelOf(ProjectedEntity item)
    {
        foreach (var selection in item.Selections)
            if (selection.StateIndex >= 0 && selection.Model is { } model) return model;
        return null;
    }

    private int Spawn(string name, EntityId entity, Vector3 offset, uint? delay = null)
    {
        if (fx.Catalog.FindParticleSystem(name) is not { } template)
        {
            _missingTemplates++;
            return 0;
        }
        return fx.Particles.Spawn(template, new ParticleSpawn(Vector3.Zero) { AttachTo = entity, LocalOffset = offset, InitialDelayFrames = delay });
    }

    private void Destroy(List<int> systems)
    {
        foreach (var system in systems) fx.Particles.Destroy(system);
        systems.Clear();
    }

    private void Hide(Tracked tracked)
    {
        tracked.Shown = false;
        Destroy(tracked.MotionSystems);
        tracked.Moving = false;
        tracked.LastPosition = null;
        tracked.StateKey = [];
        Destroy(tracked.StateSystems);
        Destroy(tracked.BodySystems);
        tracked.DamageState = -1;
        tracked.Aflame = false;
    }

    private Vector3? Bone(string? model, string bone, float scale)
    {
        if (model is null || bone.Length == 0) return null;
        return bones(model, bone) is { } local ? local * scale : null;
    }

    // ---------------------------------------------------------------------------------------------------- W3DModelDraw ParticleSysBone

    private void UpdateModelState(ProjectedEntity item, Tracked tracked)
    {
        var same = tracked.StateKey.Length == item.Selections.Length;
        for (var index = 0; same && index < item.Selections.Length; index++) same = tracked.StateKey[index] == item.Selections[index].StateIndex;
        if (same) return;
        var key = new int[item.Selections.Length];
        for (var index = 0; index < key.Length; index++) key[index] = item.Selections[index].StateIndex;
        tracked.StateKey = key;
        Destroy(tracked.StateSystems);
        for (var module = 0; module < item.Visual.Draws.Length && module < item.Selections.Length; module++)
        {
            var index = item.Selections[module].StateIndex;
            var draw = item.Visual.Draws[module];
            if (index < 0 || index >= draw.States.Length) continue;
            var state = draw.States[index];
            foreach (var entry in state.ParticleSysBones)
            {
                var offset = Bone(state.Model, entry.Bone, item.Visual.Scale);
                if (offset is null && entry.Bone is not ("null" or "none"))
                {
                    _missingBones++;
                    if (_missingBoneNames.Count < 64) _missingBoneNames.Add($"{state.Model ?? "-"}:{entry.Bone}");
                }
                var system = Spawn(entry.ParticleSystem, item.Entity, offset ?? Vector3.Zero);
                if (system == 0) continue;
                tracked.StateSystems.Add(system);
                _modelStateSystems++;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------- W3DTankDraw / W3DTruckDraw

    /// <summary>The keys of the tank and truck draws whose particle systems run while the vehicle moves.</summary>
    private static readonly string[] MotionKeys = ["TreadDebrisLeft", "TreadDebrisRight", "Dust"];

    /// <summary>
    /// <c>W3DTankDraw::doDrawModule</c> (tread debris) and <c>W3DTruckDraw::doDrawModule</c> (dust): attached to the vehicle without an offset, emitting while it moves and not
    /// while it stands. Here a system is created when the vehicle starts to move and destroyed when it stops (the original pauses and resumes the same one; its particles
    /// fly out either way). The truck's <c>DirtSpray</c> (accelerating) and <c>PowerslideSpray</c> (turning) and the speed scaling of the emission are not reproduced.
    /// </summary>
    private void UpdateMotion(ProjectedEntity item, Tracked tracked, IFxWorldView view)
    {
        if (view.GetPose(item.Entity) is not { } pose) return;
        var moved = tracked.LastPosition is { } last && Vector2.DistanceSquared(new Vector2(last.X, last.Y), new Vector2(pose.Position.X, pose.Position.Y)) > 1e-4f;
        tracked.LastPosition = pose.Position;
        if (moved == tracked.Moving) return;
        tracked.Moving = moved;
        if (!moved)
        {
            Destroy(tracked.MotionSystems);
            return;
        }
        foreach (var draw in item.Visual.Draws)
            foreach (var setting in draw.Settings)
            {
                if (!MotionKeys.Contains(setting.Key, StringComparer.OrdinalIgnoreCase)) continue;
                var name = setting.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (name is null || name.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
                var system = Spawn(name, item.Entity, Vector3.Zero);
                if (system == 0) continue;
                tracked.MotionSystems.Add(system);
                _motionSystems++;
            }
    }

    // ---------------------------------------------------------------------------------------------------- ActiveBody fire and smoke

    private void RebuildBody(ProjectedEntity item, Tracked tracked, bool aflame)
    {
        Destroy(tracked.BodySystems);
        var model = ModelOf(item);
        if (model is null) return;
        var rules = catalog.BodyRules;
        if (rules.Length < 7) return;
        var multiplier = aflame ? 2 : 1;
        for (var category = 0; category < rules.Length; category++)
        {
            if (category == AttachedEffectCatalog.Aflame && !aflame) continue;
            var rule = rules[category];
            if (rule.Max <= 0 || rule.Prefix.Length == 0) continue;
            // Aflame: fire one size up, smoke turned into small fire (the bones stay those of the category).
            var system = !aflame ? rule.System : category switch
            {
                AttachedEffectCatalog.FireSmall => rules[AttachedEffectCatalog.FireMedium].System,
                AttachedEffectCatalog.FireMedium or AttachedEffectCatalog.FireLarge => rules[AttachedEffectCatalog.FireLarge].System,
                AttachedEffectCatalog.SmokeSmall or AttachedEffectCatalog.SmokeMedium or AttachedEffectCatalog.SmokeLarge => rules[AttachedEffectCatalog.FireSmall].System,
                _ => rule.System,
            };
            var found = new List<Vector3>();
            for (var number = 1; number <= MaxNumberedBones; number++)
            {
                if (bones(model, rule.Prefix + number.ToString("00", CultureInfo.InvariantCulture)) is not { } position) break;
                found.Add(position * item.Visual.Scale);
            }
            var count = Math.Min(found.Count, rule.Max * multiplier);
            for (var made = 0; made < count; made++)
            {
                var pick = found.Count > count - made ? _random.NextInt(0, found.Count - 1) : 0;
                var created = Spawn(system, item.Entity, found[pick]);
                found.RemoveAt(pick);
                if (created == 0) continue;
                tracked.BodySystems.Add(created);
                _bodySystems++;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------- BoneFXUpdate

    private void StartBoneFx(AttachedEffectTemplate template, Tracked tracked, int state, long frame)
    {
        Destroy(tracked.BoneFxSystems);
        tracked.BoneFxState = state;
        tracked.BoneFxDue = new double[template.BoneFx.Length];
        for (var index = 0; index < template.BoneFx.Length; index++)
        {
            var slot = template.BoneFx[index];
            tracked.BoneFxDue[index] = slot.State == state && slot.Bone.Length > 0 ? frame + _random.NextReal(slot.MinFrames, slot.MaxFrames) : -1;
        }
    }

    private void RunBoneFx(ProjectedEntity item, Tracked tracked, AttachedEffectTemplate template, long frame, IFxWorldView view, Entity entity)
    {
        if (tracked.BoneFxDue.Length != template.BoneFx.Length) return;
        var model = ModelOf(item);
        for (var index = 0; index < template.BoneFx.Length; index++)
        {
            var due = tracked.BoneFxDue[index];
            if (due < 0 || due > frame) continue;
            var slot = template.BoneFx[index];
            // A bone the model does not have is the object's origin (getPristineBonePositions leaves the position at zero).
            var offset = Bone(model, slot.Bone, item.Visual.Scale) ?? Vector3.Zero;
            if (slot.IsParticleSystem)
            {
                var system = Spawn(slot.Name, item.Entity, offset);
                if (system != 0)
                {
                    tracked.BoneFxSystems.Add(system);
                    _boneFxSystems++;
                }
            }
            else if (view.GetPose(item.Entity) is { } pose)
            {
                fx.ExecuteFxListAtPosition(slot.Name, new FxInvocation(Vector3.Transform(offset, pose.Transform)), view);
                _boneFxLists++;
            }
            tracked.BoneFxDue[index] = slot.OnlyOnce ? -1 : frame + Math.Max(1f, _random.NextReal(slot.MinFrames, slot.MaxFrames));
        }
        if (tracked.BoneFxSystems.Count > 32) tracked.BoneFxSystems.RemoveAll(system => !fx.Particles.IsRunning(system));
    }

    // ---------------------------------------------------------------------------------------------------- TransitionDamageFX

    private void ChangeTransition(ProjectedEntity item, Tracked tracked, AttachedEffectTemplate template, int oldState, int newState, IFxWorldView view, Entity entity)
    {
        if (oldState >= 0) Destroy(tracked.TransitionSystems[oldState]);
        if (newState <= Math.Max(oldState, 0)) return;
        var model = ModelOf(item);
        foreach (var slot in template.Transition)
        {
            if (slot.State != newState) continue;
            var offset = slot.Location;
            if (slot.Bone is { Length: > 0 } bone)
            {
                if (slot.RandomBone)
                {
                    var candidates = new List<Vector3>();
                    for (var number = 1; number <= 32 && model is not null; number++)
                    {
                        if (bones(model, bone + number.ToString("00", CultureInfo.InvariantCulture)) is not { } position) break;
                        candidates.Add(position * item.Visual.Scale);
                    }
                    if (candidates.Count > 0) offset = candidates[_random.NextInt(0, candidates.Count - 1)];
                }
                else if (Bone(model, bone, item.Visual.Scale) is { } position) offset = position;
            }
            if (slot.IsParticleSystem)
            {
                var system = Spawn(slot.Name, item.Entity, offset);
                if (system == 0) continue;
                tracked.TransitionSystems[newState].Add(system);
                _transitionSystems++;
            }
            else if (view.GetPose(item.Entity) is { } pose)
            {
                fx.ExecuteFxListAtPosition(slot.Name, new FxInvocation(Vector3.Transform(offset, pose.Transform)), view);
                _transitionFxLists++;
            }
        }
    }
}
