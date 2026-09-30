using System.Collections.Immutable;
using CnCNew.Data.Definitions;

namespace CnCNew.Presentation.Fx;

/// <summary>
/// The FX-relevant slice of a weapon (<c>Weapon.cpp TheWeaponTemplateFieldParseTable</c>). Per-veterancy lists are indexed by
/// <c>VeterancyLevel</c> (REGULAR, VETERAN, ELITE, HEROIC); <c>FireFX</c> fills all four (<c>parseAllVetLevelsFXList</c>), a later
/// <c>VeterancyFireFX = LEVEL name</c> overrides one (<c>parsePerVetLevelFXList</c>). <see cref="WeaponSpeed"/> is distance per frame.
/// </summary>
public sealed record WeaponFxInfo(
    string Name,
    ImmutableArray<string?> FireFx,
    ImmutableArray<string?> ProjectileDetonationFx,
    string? FireSound,
    string? ProjectileObject,
    float WeaponSpeed,
    float PrimaryDamageRadius,
    float AttackRange,
    string DamageType)
{
    /// <summary><c>WeaponTemplate::isContactWeapon</c> with <c>RATIONALIZE_ATTACK_RANGE</c>: range minus a quarter cell below one cell (10).</summary>
    public bool IsContact => AttackRange - 2.5f < 10f;
}

/// <summary>An object's audio table (<c>Voice*</c>/<c>Sound*</c> keys and <c>UnitSpecificSounds</c>), its KindOf flags and default DamageFX.</summary>
public sealed record ObjectFxInfo(
    string Name,
    ImmutableSortedDictionary<string, string> Sounds,
    ImmutableSortedDictionary<string, string> UnitSpecificSounds,
    ImmutableArray<string> KindOf,
    string? DamageFx)
{
    public bool IsKindOf(string kind) => KindOf.Contains(kind, StringComparer.OrdinalIgnoreCase);

    /// <summary>The event name of a sound key, or null when absent or <c>NoSound</c>.</summary>
    public string? Sound(string key) => Sounds.TryGetValue(key, out var value) && FxCatalog.IsAudible(value) ? value : null;
}

/// <summary>One <c>DamageFX</c> line in INI order; a null <see cref="DamageType"/> is <c>DEFAULT</c>, a negative veterancy is "all levels".</summary>
public sealed record DamageFxLine(string Key, int Veterancy, string? DamageType, string? FxList, float Amount, uint ThrottleFrames);

/// <summary>A <c>DamageFX</c> definition; <see cref="Resolve"/> replays the lines like <c>DamageFX::parse*</c> fills <c>m_dfx[type][level]</c>.</summary>
public sealed record DamageFxTable(string Name, ImmutableArray<DamageFxLine> Lines)
{
    public (string? Major, string? Minor, float AmountForMajor, uint ThrottleFrames) Resolve(string damageType, int veterancy)
    {
        string? major = null, minor = null;
        var amount = 0f;
        uint throttle = 0;
        foreach (var line in Lines)
        {
            if (line.Veterancy >= 0 && line.Veterancy != veterancy) continue;
            if (line.DamageType is not null && !line.DamageType.Equals(damageType, StringComparison.OrdinalIgnoreCase)) continue;
            switch (line.Key)
            {
                case "MajorFX": major = line.FxList; break;
                case "MinorFX": minor = line.FxList; break;
                case "AmountForMajorFX": amount = line.Amount; break;
                case "ThrottleTime": throttle = line.ThrottleFrames; break;
            }
        }
        return (major, minor, amount, throttle);
    }
}

/// <summary>Sounds of an upgrade (<c>ResearchSound</c>, <c>UnitSpecificSound</c>) or a special power (<c>InitiateSound</c>, <c>InitiateAtLocationSound</c>).</summary>
public sealed record NamedSounds(string Name, string? Primary, string? Secondary);

/// <summary>The <c>AudioEvent</c> fields a sound cue carries: volume as a ratio and the priority index (LOWEST=0 .. CRITICAL=4).</summary>
public sealed record AudioEventInfo(string Name, float Volume, int Priority);

/// <summary>
/// Everything the FX layer reads from a <see cref="CompiledDefinitionGraph"/>: FXLists, particle system templates, weapon/object/DamageFX
/// FX references, sound tables and the GameData settings. Names are looked up case-insensitively like every <c>CnCNew.Data</c> name.
/// All parse and reference findings are in <see cref="Diagnostics"/> (sorted, deterministic).
/// </summary>
public sealed class FxCatalog
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    internal static readonly string[] VeterancyNames = ["REGULAR", "VETERAN", "ELITE", "HEROIC"];
    private static readonly string[] AudioPriorityNames = ["LOWEST", "LOW", "NORMAL", "HIGH", "CRITICAL"];

    private FxCatalog() { }

    public ImmutableSortedDictionary<string, FxList> FxLists { get; private init; } = ImmutableSortedDictionary<string, FxList>.Empty;
    public ImmutableSortedDictionary<string, ParticleSystemTemplate> ParticleSystems { get; private init; } = ImmutableSortedDictionary<string, ParticleSystemTemplate>.Empty;
    public ImmutableSortedDictionary<string, WeaponFxInfo> Weapons { get; private init; } = ImmutableSortedDictionary<string, WeaponFxInfo>.Empty;
    public ImmutableSortedDictionary<string, ObjectFxInfo> Objects { get; private init; } = ImmutableSortedDictionary<string, ObjectFxInfo>.Empty;
    public ImmutableSortedDictionary<string, DamageFxTable> DamageFx { get; private init; } = ImmutableSortedDictionary<string, DamageFxTable>.Empty;
    public ImmutableSortedDictionary<string, NamedSounds> Upgrades { get; private init; } = ImmutableSortedDictionary<string, NamedSounds>.Empty;
    public ImmutableSortedDictionary<string, NamedSounds> SpecialPowers { get; private init; } = ImmutableSortedDictionary<string, NamedSounds>.Empty;
    public ImmutableSortedDictionary<string, string> MiscAudio { get; private init; } = ImmutableSortedDictionary<string, string>.Empty;
    public ImmutableSortedDictionary<string, AudioEventInfo> AudioEvents { get; private init; } = ImmutableSortedDictionary<string, AudioEventInfo>.Empty;
    public FxSettings Settings { get; private init; } = FxSettings.Default;
    public ImmutableArray<FxDiagnostic> Diagnostics { get; private init; } = [];

    public ImmutableArray<FxDiagnostic> UnexplainedDiagnostics => [..Diagnostics.Where(value => !value.IsExplained)];

    public FxList? FindFxList(string? name) => IsNamed(name) && FxLists.TryGetValue(name!, out var list) ? list : null;
    public ParticleSystemTemplate? FindParticleSystem(string? name) => IsNamed(name) && ParticleSystems.TryGetValue(name!, out var t) ? t : null;
    public WeaponFxInfo? FindWeapon(string? name) => IsNamed(name) && Weapons.TryGetValue(name!, out var w) ? w : null;
    public ObjectFxInfo? FindObject(string? name) => IsNamed(name) && Objects.TryGetValue(name!, out var o) ? o : null;

    public AudioEventInfo? FindAudioEvent(string name) => AudioEvents.TryGetValue(name, out var info) ? info : null;

    /// <summary><c>FXListStore::findFXList</c> treats <c>None</c> as "no list"; empty names never resolve either.</summary>
    internal static bool IsNamed(string? name) => !string.IsNullOrEmpty(name) && !name.Equals("None", StringComparison.OrdinalIgnoreCase);

    /// <summary>The reference skips empty event names and <c>NoSound</c> (<c>Weapon.cpp</c> FireSound check, <c>AudioEventRTS</c>).</summary>
    public static bool IsAudible(string? name) => !string.IsNullOrEmpty(name) && !name.Equals("NoSound", StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds the catalog from a compiled graph. Parsing never throws for data problems; they end up in <see cref="Diagnostics"/>.</summary>
    public static FxCatalog Build(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var diagnostics = new FxDiagnosticBag();
        var fxLists = ImmutableSortedDictionary.CreateBuilder<string, FxList>(Names);
        var particles = ImmutableSortedDictionary.CreateBuilder<string, ParticleSystemTemplate>(Names);
        var weapons = ImmutableSortedDictionary.CreateBuilder<string, WeaponFxInfo>(Names);
        var objects = ImmutableSortedDictionary.CreateBuilder<string, ObjectFxInfo>(Names);
        var damage = ImmutableSortedDictionary.CreateBuilder<string, DamageFxTable>(Names);
        var upgrades = ImmutableSortedDictionary.CreateBuilder<string, NamedSounds>(Names);
        var powers = ImmutableSortedDictionary.CreateBuilder<string, NamedSounds>(Names);
        var misc = ImmutableSortedDictionary.CreateBuilder<string, string>(Names);
        var audio = ImmutableSortedDictionary.CreateBuilder<string, AudioEventInfo>(Names);
        GameDefinition? gameData = null;

        foreach (var definition in graph.Definitions)
        {
            switch (definition)
            {
                case FxListDefinition list:
                    fxLists[list.OriginalName] = ParseFxList(list, diagnostics);
                    break;
                case FxDefinition system:
                    particles[system.OriginalName] = ParticleSystemTemplate.Parse(system.OriginalName, system.Fields, diagnostics);
                    break;
                case WeaponDefinition weapon:
                    weapons[weapon.OriginalName] = ParseWeapon(weapon, diagnostics);
                    break;
                case ObjectDefinition obj:
                    objects[obj.OriginalName] = ParseObject(obj);
                    break;
                case DamageDefinition dfx:
                    damage[dfx.OriginalName] = ParseDamageFx(dfx, diagnostics);
                    break;
                case UpgradeDefinition upgrade:
                    upgrades[upgrade.OriginalName] = new NamedSounds(upgrade.OriginalName, Token(upgrade, "ResearchSound"), Token(upgrade, "UnitSpecificSound"));
                    break;
                case SpecialPowerDefinition power:
                    powers[power.OriginalName] = new NamedSounds(power.OriginalName, Token(power, "InitiateSound"), Token(power, "InitiateAtLocationSound"));
                    break;
                case AudioEventDefinition sound:
                    audio[sound.OriginalName] = ParseAudioEvent(sound);
                    break;
                case GlobalDataDefinition global when global.Id.Kind == DefinitionKind.GameData:
                    gameData = global;
                    break;
                case CompatibilityDefinition compat when compat.Id.Name.StartsWith("miscaudio/", StringComparison.Ordinal):
                    foreach (var field in compat.Fields)
                        if (FxValueReader.Tokens(field.Value.OriginalText).FirstOrDefault() is { } value) misc[field.Name] = value;
                    break;
            }
        }

        var catalog = new FxCatalog
        {
            FxLists = fxLists.ToImmutable(),
            ParticleSystems = particles.ToImmutable(),
            Weapons = weapons.ToImmutable(),
            Objects = objects.ToImmutable(),
            DamageFx = damage.ToImmutable(),
            Upgrades = upgrades.ToImmutable(),
            SpecialPowers = powers.ToImmutable(),
            MiscAudio = misc.ToImmutable(),
            AudioEvents = audio.ToImmutable(),
            Settings = FxSettings.FromGameData(gameData, diagnostics),
        };
        catalog.CheckReferences(diagnostics);
        return new FxCatalog
        {
            FxLists = catalog.FxLists, ParticleSystems = catalog.ParticleSystems, Weapons = catalog.Weapons, Objects = catalog.Objects,
            DamageFx = catalog.DamageFx, Upgrades = catalog.Upgrades, SpecialPowers = catalog.SpecialPowers, MiscAudio = catalog.MiscAudio,
            AudioEvents = catalog.AudioEvents, Settings = catalog.Settings, Diagnostics = diagnostics.ToSortedArray(),
        };
    }

    private static FxList ParseFxList(FxListDefinition definition, FxDiagnosticBag diagnostics)
    {
        // The compiler appends the nuggets of a repeated FXList block; FXListStore::parseFXListDefinition clears them, so only the
        // sections of the last block (same file, after its declaration line) are kept.
        var location = definition.Location;
        var kept = definition.Sections.Where(section => section.Location.Source == location.Source && section.Location.Line > location.Line).ToArray();
        if (kept.Length != definition.Sections.Length)
            diagnostics.Add(FxDiagnosticCodes.FxListRedefined, $"FXList {definition.OriginalName}",
                $"{definition.Sections.Length - kept.Length} nugget(s) of earlier definitions dropped; {kept.Length} kept.");
        if (definition.Fields.Length > 0)
            diagnostics.Add(FxDiagnosticCodes.UnknownKey, $"FXList {definition.OriginalName}",
                $"{definition.Fields.Length} key(s) outside a nugget: {string.Join(", ", definition.Fields.Select(field => field.Name))}.");
        if (definition.Modules.Length > 0)
            diagnostics.Add(FxDiagnosticCodes.UnknownNugget, $"FXList {definition.OriginalName}", $"{definition.Modules.Length} module block(s).");
        var nuggets = ImmutableArray.CreateBuilder<FxNugget>();
        foreach (var section in kept)
            if (FxList.ParseNugget(definition.OriginalName, section, diagnostics) is { } nugget) nuggets.Add(nugget);
        return new FxList(definition.OriginalName, nuggets.ToImmutable());
    }

    private static WeaponFxInfo ParseWeapon(WeaponDefinition weapon, FxDiagnosticBag diagnostics)
    {
        var read = new FxValueReader(diagnostics, $"Weapon {weapon.OriginalName}");
        var fire = new string?[4];
        var detonation = new string?[4];
        string? sound = null, projectile = null, damageType = null;
        float speed = 0f, radius = 0f, range = 0f;
        foreach (var field in weapon.Fields)
        {
            var tokens = FxValueReader.Tokens(field.Value.OriginalText);
            switch (field.Name.ToLowerInvariant())
            {
                case "firefx": Fill(fire, -1, tokens.FirstOrDefault()); break;
                case "projectiledetonationfx": Fill(detonation, -1, tokens.FirstOrDefault()); break;
                case "veterancyfirefx": FillVeterancy(fire, tokens, read, field.Name, field.Value.OriginalText); break;
                case "veterancyprojectiledetonationfx": FillVeterancy(detonation, tokens, read, field.Name, field.Value.OriginalText); break;
                case "firesound": sound = tokens.FirstOrDefault(); break;
                case "projectileobject": projectile = tokens.FirstOrDefault(); break;
                case "weaponspeed": speed = read.VelocityPerFrame(field.Name, field.Value.OriginalText, 0f); break;
                case "primarydamageradius": radius = read.Real(field.Name, field.Value.OriginalText, 0f); break;
                case "damagetype": damageType = tokens.FirstOrDefault(); break;
                case "attackrange": range = read.Real(field.Name, field.Value.OriginalText, 0f); break;
            }
        }
        return new WeaponFxInfo(weapon.OriginalName, [..fire], [..detonation], sound, IsNamed(projectile) ? projectile : null, speed, radius,
            range, damageType ?? "EXPLOSION");
    }

    private static void Fill(string?[] levels, int level, string? name)
    {
        var value = IsNamed(name) ? name : null;
        if (level < 0) Array.Fill(levels, value); else levels[level] = value;
    }

    private static void FillVeterancy(string?[] levels, string[] tokens, FxValueReader read, string key, string text)
    {
        var level = tokens.Length >= 2 ? Array.FindIndex(VeterancyNames, name => name.Equals(tokens[0], StringComparison.OrdinalIgnoreCase)) : -1;
        if (level < 0) { read.Invalid(key, text, "'<VeterancyLevel> <FXList>'"); return; }
        Fill(levels, level, tokens[1]);
    }

    private static ObjectFxInfo ParseObject(ObjectDefinition obj)
    {
        var sounds = ImmutableSortedDictionary.CreateBuilder<string, string>(Names);
        foreach (var field in obj.Fields)
            if ((field.Name.StartsWith("Voice", StringComparison.OrdinalIgnoreCase) || field.Name.StartsWith("Sound", StringComparison.OrdinalIgnoreCase))
                && FxValueReader.Tokens(field.Value.OriginalText).FirstOrDefault() is { } name)
                sounds[field.Name] = name;
        var unitSpecific = ImmutableSortedDictionary.CreateBuilder<string, string>(Names);
        string? damageFx = null;
        foreach (var section in obj.Sections)
        {
            var type = FxValueReader.Tokens(section.Declaration).FirstOrDefault() ?? string.Empty;
            if (type.Equals("UnitSpecificSounds", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var field in section.Fields)
                    if (FxValueReader.Tokens(field.Value.OriginalText).FirstOrDefault() is { } name) unitSpecific[field.Name] = name;
            }
            else if (type.Equals("ArmorSet", StringComparison.OrdinalIgnoreCase))
            {
                // Only the unconditional armor set (Conditions = None) is known without the object's live upgrade/state bits.
                var conditions = section.Fields.LastOrDefault(field => field.Name.Equals("Conditions", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim();
                if (conditions is not null && !conditions.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
                var fx = section.Fields.LastOrDefault(field => field.Name.Equals("DamageFX", StringComparison.OrdinalIgnoreCase));
                if (fx is not null) damageFx = FxValueReader.Tokens(fx.Value.OriginalText).FirstOrDefault();
            }
        }
        var kindOf = obj.Fields.LastOrDefault(field => field.Name.Equals("KindOf", StringComparison.OrdinalIgnoreCase))?.Value.Value is FlagsValue flags
            ? flags.Names : ImmutableArray<string>.Empty;
        return new ObjectFxInfo(obj.OriginalName, sounds.ToImmutable(), unitSpecific.ToImmutable(), kindOf, IsNamed(damageFx) ? damageFx : null);
    }

    private static DamageFxTable ParseDamageFx(DamageDefinition dfx, FxDiagnosticBag diagnostics)
    {
        var read = new FxValueReader(diagnostics, $"DamageFX {dfx.OriginalName}");
        var lines = ImmutableArray.CreateBuilder<DamageFxLine>();
        foreach (var field in dfx.Fields)
        {
            var key = field.Name;
            var text = field.Value.OriginalText;
            var tokens = FxValueReader.Tokens(text);
            var veterancyForm = key.StartsWith("Veterancy", StringComparison.OrdinalIgnoreCase);
            var baseKey = veterancyForm ? key["Veterancy".Length..] : key;
            var canonical = new[] { "MajorFX", "MinorFX", "AmountForMajorFX", "ThrottleTime" }.FirstOrDefault(name => name.Equals(baseKey, StringComparison.OrdinalIgnoreCase));
            if (canonical is null)
            {
                diagnostics.Add(FxDiagnosticCodes.UnknownKey, read.Subject, $"'{key}' is not part of DamageFX::getFieldParse.");
                continue;
            }
            var offset = veterancyForm ? 1 : 0;
            if (tokens.Length < offset + 2) { read.Invalid(key, text, "'[<Veterancy>] <DamageType|Default> <value>'"); continue; }
            var veterancy = -1;
            if (veterancyForm)
            {
                veterancy = Array.FindIndex(VeterancyNames, name => name.Equals(tokens[0], StringComparison.OrdinalIgnoreCase));
                if (veterancy < 0) { diagnostics.Add(FxDiagnosticCodes.UnknownEnum, $"{read.Subject}/{key}", $"'{tokens[0]}' is not a veterancy level."); continue; }
            }
            var damageType = tokens[offset].Equals("Default", StringComparison.OrdinalIgnoreCase) ? null : tokens[offset];
            var value = tokens[offset + 1];
            switch (canonical)
            {
                case "MajorFX" or "MinorFX":
                    lines.Add(new DamageFxLine(canonical, veterancy, damageType, IsNamed(value) ? value : null, 0f, 0));
                    break;
                case "AmountForMajorFX":
                    lines.Add(new DamageFxLine(canonical, veterancy, damageType, null, read.Real(key, value, 0f), 0));
                    break;
                default:
                    lines.Add(new DamageFxLine(canonical, veterancy, damageType, null, 0f, read.DurationFrames(key, value, 0)));
                    break;
            }
        }
        return new DamageFxTable(dfx.OriginalName, lines.ToImmutable());
    }

    private static AudioEventInfo ParseAudioEvent(AudioEventDefinition sound)
    {
        var volume = 1f;
        var priority = 2;
        foreach (var field in sound.Fields)
        {
            var token = FxValueReader.Tokens(field.Value.OriginalText).FirstOrDefault();
            if (token is null) continue;
            if (field.Name.Equals("Volume", StringComparison.OrdinalIgnoreCase) && FxValueReader.TryReal(token.TrimEnd('%'), out var percent))
                volume = percent / 100f;
            else if (field.Name.Equals("Priority", StringComparison.OrdinalIgnoreCase))
            {
                var index = Array.FindIndex(AudioPriorityNames, name => name.Equals(token, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) priority = index;
            }
        }
        return new AudioEventInfo(sound.OriginalName, volume, priority);
    }

    private static string? Token(GameDefinition definition, string key)
    {
        var field = definition.Fields.LastOrDefault(value => value.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        var token = field is null ? null : FxValueReader.Tokens(field.Value.OriginalText).FirstOrDefault();
        return IsAudible(token) ? token : null;
    }

    private void CheckReferences(FxDiagnosticBag diagnostics)
    {
        void Check<T>(ImmutableSortedDictionary<string, T> table, string? name, string missingCode, string subject)
        {
            if (!IsNamed(name)) return;
            if (!table.ContainsKey(name!)) diagnostics.Add(missingCode, subject, $"'{name}' does not exist.");
            else if (!table.Keys.First(key => Names.Equals(key, name)).Equals(name, StringComparison.Ordinal))
                diagnostics.Add(FxDiagnosticCodes.CaseMismatch, subject, $"'{name}' resolves to '{table.Keys.First(key => Names.Equals(key, name))}'.");
        }

        foreach (var list in FxLists.Values)
            foreach (var nugget in list.Nuggets)
                switch (nugget)
                {
                    case ParticleSystemFxNugget particle:
                        Check(ParticleSystems, particle.Name, FxDiagnosticCodes.MissingParticleSystem, $"FXList {list.Name}/ParticleSystem");
                        break;
                    case FxListAtBonePosFxNugget bone:
                        Check(FxLists, bone.FxListName, FxDiagnosticCodes.MissingFxList, $"FXList {list.Name}/FXListAtBonePos");
                        break;
                    case TracerFxNugget tracer:
                        Check(Objects, tracer.TracerName, FxDiagnosticCodes.MissingTracerTemplate, $"FXList {list.Name}/Tracer");
                        break;
                }
        foreach (var system in ParticleSystems.Values)
        {
            Check(ParticleSystems, system.SlaveSystem, FxDiagnosticCodes.MissingSlaveSystem, $"ParticleSystem {system.Name}/SlaveSystem");
            Check(ParticleSystems, system.PerParticleAttachedSystem, FxDiagnosticCodes.MissingAttachedSystem, $"ParticleSystem {system.Name}/PerParticleAttachedSystem");
        }
        foreach (var weapon in Weapons.Values)
            foreach (var name in weapon.FireFx.Concat(weapon.ProjectileDetonationFx).Distinct(Names))
                Check(FxLists, name, FxDiagnosticCodes.MissingFxList, $"Weapon {weapon.Name}");
        foreach (var table in DamageFx.Values)
            foreach (var name in table.Lines.Select(line => line.FxList).Distinct(Names))
                Check(FxLists, name, FxDiagnosticCodes.MissingFxList, $"DamageFX {table.Name}");
        foreach (var obj in Objects.Values)
            Check(DamageFx, obj.DamageFx, FxDiagnosticCodes.MissingDamageFx, $"Object {obj.Name}/ArmorSet");
    }
}
