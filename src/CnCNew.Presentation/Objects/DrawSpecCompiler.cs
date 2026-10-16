using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Data.Ini;
using CnCNew.Data.Registry;

namespace CnCNew.Presentation.Objects;

/// <summary>A finding while compiling a draw module; <see cref="Key"/> is the offending key, condition name or rule, depending on <see cref="Code"/>.</summary>
public sealed record DrawDiagnostic(string Severity, string Code, string ModuleType, string Key, string Detail, IniSourceLocation Location);

/// <summary>
/// Compiles one <c>Draw</c> module of the definition graph into a <see cref="DrawSpec"/>. Draw modules are catalogued but not typed by the
/// data compiler, so their keys and nested blocks arrive as generic fields/sections; this compiler re-reads their original text with the
/// reference grammar (<c>W3DModelDraw.cpp W3DModelDrawModuleData::parseConditionState</c>, <c>parseAnimation</c>, <c>parseShowHideSubObject</c>,
/// <c>parseWeaponBoneName</c>, <c>parseParticleSysBone</c>, <c>parseRealRange</c>, <c>BitFlags::parse</c>, <c>INI::parseBitString32</c>).
/// Module keys and blocks are replayed in source order because <c>AliasConditionState</c> refers to the state declared before it.
/// Every deviation from the reference tables is reported as a <see cref="DrawDiagnostic"/>; nothing is dropped silently.
/// </summary>
public static class DrawSpecCompiler
{
    private static readonly string[] WeaponSlots = ["PRIMARY", "SECONDARY", "TERTIARY"];
    private static readonly string[] LodNames = ["Low", "Medium", "High", "Custom"];
    private static readonly string[] AnimModeNames = ["MANUAL", "LOOP", "ONCE", "LOOP_PINGPONG", "LOOP_BACKWARDS", "ONCE_BACKWARDS"];
    private static readonly string[] AnimFlagNames =
    [
        "RANDOMSTART", "START_FRAME_FIRST", "START_FRAME_LAST", "ADJUST_HEIGHT_BY_CONSTRUCTION_PERCENT", "PRISTINE_BONE_POS_IN_FINAL_FRAME",
        "MAINTAIN_FRAME_ACROSS_STATES", "RESTART_ANIM_WHEN_COMPLETE", "MAINTAIN_FRAME_ACROSS_STATES2", "MAINTAIN_FRAME_ACROSS_STATES3",
        "MAINTAIN_FRAME_ACROSS_STATES4",
    ];

    public static DrawSpec Compile(ModuleConfiguration module, List<DrawDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(module);
        var context = new Context(module.Name, diagnostics);
        if (!DrawKeyCatalog.Modules.TryGetValue(module.Name, out var info))
        {
            context.Error("unknown-draw-module", module.Name, "The draw module type has no key table.", module.Location);
            info = new DrawModuleInfo(module.Name, DrawModuleKind.Default, false, [], "unknown");
        }

        var builder = new ModuleBuilder(info);
        var items = new List<(IniSourceLocation Location, DefinitionField? Field, DefinitionSection? Section)>();
        foreach (var field in module.Fields) items.Add((field.Location, field, null));
        if (!module.Sections.IsDefault)
            foreach (var section in module.Sections) items.Add((section.Location, null, section));
        items.Sort((a, b) => a.Location.Line != b.Location.Line ? a.Location.Line.CompareTo(b.Location.Line) : a.Location.Column.CompareTo(b.Location.Column));

        foreach (var (location, field, section) in items)
        {
            if (field is not null) ModuleKey(context, builder, field.Name, field.Value.OriginalText, location);
            else Section(context, builder, section!);
        }
        return builder.Build(module, context);
    }

    // ------------------------------------------------------------------------------------------------ module keys

    private static void ModuleKey(Context c, ModuleBuilder b, string key, string text, IniSourceLocation location)
    {
        var tokens = Tokens(text);
        if (b.Info.HasConditionStates && key.Equals("AliasConditionState", StringComparison.OrdinalIgnoreCase))
        {
            Alias(c, b, tokens, location);
            return;
        }
        if (!b.Info.Knows(key))
        {
            c.Error("unknown-draw-key", key, $"'{key}' is not part of the {b.Info.ModuleType} field table ({b.Info.Source}).", location);
            return;
        }
        var canonical = b.Info.Keys.First(value => value.Equals(key, StringComparison.OrdinalIgnoreCase));
        b.Settings.Add(new DrawSetting(canonical, text));
        switch (canonical)
        {
            case "OkToChangeModelColor": b.OkToChangeModelColor = Bool(c, canonical, tokens, location, b.OkToChangeModelColor); break;
            case "AnimationsRequirePower": b.AnimationsRequirePower = Bool(c, canonical, tokens, location, b.AnimationsRequirePower); break;
            case "ParticlesAttachedToAnimatedBones": b.ParticlesAttached = Bool(c, canonical, tokens, location, b.ParticlesAttached); break;
            case "ReceivesDynamicLights": Bool(c, canonical, tokens, location, true); break;
            case "InitialRecoilSpeed" or "MaxRecoilDistance" or "RecoilDamping" or "RecoilSettleSpeed": Real(c, canonical, tokens, 0, location); Extra(c, canonical, tokens, 1, location); break;
            case "MinLODRequired": Index(c, canonical, tokens, LodNames, location); break;
            case "ProjectileBoneFeedbackEnabledSlots": BitString(c, canonical, tokens, WeaponSlots, 0, location); break;
            case "TrackMarks": b.TrackMarks = FirstLower(tokens); Extra(c, canonical, tokens, 1, location); break;
            case "ExtraPublicBone": b.ExtraPublicBones.AddRange(tokens); break;
            case "AttachToBoneInAnotherModule": b.AttachToBone = FirstLower(tokens); Extra(c, canonical, tokens, 1, location); break;
            case "IgnoreConditionStates": b.Ignore = Conditions(c, tokens, b.Ignore, location); break;
            case "ModelName": b.ModelName = tokens.Length > 0 ? tokens[0] : null; Extra(c, canonical, tokens, 1, location); break;
        }
    }

    private static void Section(Context c, ModuleBuilder b, DefinitionSection section)
    {
        var tokens = ValueGrammar.Tokenize(section.Declaration);
        var type = tokens.Length > 0 ? tokens[0] : string.Empty;
        var kind = type.ToUpperInvariant() switch
        {
            "DEFAULTCONDITIONSTATE" => StateKind.Default,
            "CONDITIONSTATE" => StateKind.Normal,
            "TRANSITIONSTATE" => StateKind.Transition,
            _ => (StateKind?)null,
        };
        if (kind is null || !b.Info.HasConditionStates)
        {
            c.Error("unknown-draw-key", type, $"Block '{type}' is not allowed in {b.Info.ModuleType}.", section.Location);
            return;
        }
        var arguments = tokens[1..];
        StateBuilder state;
        switch (kind.Value)
        {
            case StateKind.Default:
                if (b.DefaultIndex >= 0) c.Error("state-rule", "duplicate-default", "Only one DefaultConditionState is allowed.", section.Location);
                else if (arguments.Length > 0) c.Error("state-rule", "default-arguments", "DefaultConditionState takes no arguments.", section.Location);
                if (b.States.Count > 0) c.Error("state-rule", "default-not-first", "DefaultConditionState must be the first state.", section.Location);
                state = new StateBuilder();
                state.Conditions.Add(ModelConditionSet.Empty);
                state.IsDefault = true;
                if (b.DefaultIndex < 0) b.DefaultIndex = b.States.Count;
                break;
            case StateKind.Transition:
                if (arguments.Length < 2) { c.Error("state-rule", "transition-arguments", "TransitionState needs two state keys.", section.Location); return; }
                Extra(c, "TransitionState", arguments, 2, section.Location);
                state = b.DefaultIndex >= 0 ? b.States[b.DefaultIndex].Clone(copiedFromDefault: true) : new StateBuilder();
                state.IsDefault = false;
                state.TransitionKey = null;
                state.AllowToFinishKey = null;
                state.Conditions.Clear();
                state.TransitionFrom = arguments[0].ToLowerInvariant();
                state.TransitionTo = arguments[1].ToLowerInvariant();
                if (state.TransitionFrom == state.TransitionTo) c.Error("state-rule", "transition-same-states", "A transition between two identical states is not allowed.", section.Location);
                break;
            default:
                state = b.DefaultIndex >= 0 ? b.States[b.DefaultIndex].Clone(copiedFromDefault: true) : new StateBuilder();
                state.IsDefault = false;
                state.Conditions.Clear();
                var conditions = Conditions(c, arguments, ModelConditionSet.Empty, section.Location);
                CheckNewConditions(c, b, conditions, section.Location);
                if (b.DefaultIndex < 0 && b.States.Count == 0 && conditions.Any)
                    c.Error("state-rule", "first-state-not-none", "Without DefaultConditionState the first ConditionState must be for NONE.", section.Location);
                state.Conditions.Add(conditions);
                break;
        }

        foreach (var field in section.Fields) StateKey(c, state, field.Name, field.Value.OriginalText, field.Location);
        foreach (var child in section.Children)
            c.Error("unknown-draw-key", $"{type}/{ValueGrammar.Tokenize(child.Declaration).FirstOrDefault()}", "Nested blocks are not allowed in a condition state.", child.Location);

        if (!state.ModelGiven) c.Error("state-rule", "missing-model", "Every condition state must name a model.", section.Location);
        if (state.GotIdle && state.GotNormal) c.Error("state-rule", "idle-and-normal-animations", "A state may not mix Animation and IdleAnimation.", section.Location);
        if (state.GotIdle && state.Mode is not (AnimationMode.Once or AnimationMode.OnceBackwards))
            c.Error("state-rule", "idle-animation-mode", "Idle animations need ONCE or ONCE_BACKWARDS.", section.Location);
        if (kind == StateKind.Transition)
        {
            if (state.GotIdle) c.Error("state-rule", "transition-idle-animation", "Transition states may not use idle animations.", section.Location);
            if (state.Mode is not (AnimationMode.Once or AnimationMode.OnceBackwards))
                c.Error("state-rule", "transition-mode", "Transition states need ONCE or ONCE_BACKWARDS.", section.Location);
            if (state.TransitionKey is not null || state.AllowToFinishKey is not null)
                c.Error("state-rule", "transition-keys", "Transition states may not carry TransitionKey or WaitForStateToFinishIfPossible.", section.Location);
            b.Transitions.RemoveAll(existing => existing.TransitionFrom == state.TransitionFrom && existing.TransitionTo == state.TransitionTo);
            b.Transitions.Add(state);
        }
        else b.States.Add(state);
    }

    private static void Alias(Context c, ModuleBuilder b, string[] tokens, IniSourceLocation location)
    {
        if (b.States.Count == 0)
        {
            c.Error("state-rule", "alias-without-state", "AliasConditionState must follow a condition state.", location);
            return;
        }
        var conditions = Conditions(c, tokens, ModelConditionSet.Empty, location);
        CheckNewConditions(c, b, conditions, location);
        b.States[^1].Conditions.Add(conditions);
    }

    private static void CheckNewConditions(Context c, ModuleBuilder b, ModelConditionSet conditions, IniSourceLocation location)
    {
        if (conditions.Overlaps(b.Ignore))
            c.Error("state-rule", "ignored-condition-used", $"'{conditions}' uses a bit of IgnoreConditionStates ({b.Ignore}).", location);
        if (b.States.Any(state => state.Conditions.Contains(conditions)))
            c.Error("state-rule", "duplicate-condition-state", $"Condition state '{conditions}' is declared twice.", location);
        if (!conditions.Any && b.DefaultIndex >= 0)
            c.Error("state-rule", "default-and-none-state", "A DefaultConditionState and a NONE state may not coexist.", location);
    }

    // ------------------------------------------------------------------------------------------------ state keys

    private static void StateKey(Context c, StateBuilder s, string key, string text, IniSourceLocation location)
    {
        var canonical = DrawKeyCatalog.ConditionStateKeys.FirstOrDefault(value => value.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (canonical is null)
        {
            c.Error("unknown-draw-key", "ConditionState/" + key, $"'{key}' is not part of parseConditionState's field table.", location);
            return;
        }
        var tokens = Tokens(text);
        switch (canonical)
        {
            case "Model":
                if (tokens.Length == 0) { c.Error("invalid-value", canonical, "Model needs a name.", location); break; }
                s.ModelGiven = true;
                // NONE and NULL draw nothing: NULL is W3D's null render object (the asset manager's NULL prototype), which the effect projectiles use - not a model to load.
                s.Model = tokens[0].Equals("NONE", StringComparison.OrdinalIgnoreCase) || tokens[0].Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : tokens[0].ToLowerInvariant();
                Extra(c, canonical, tokens, 1, location);
                break;
            case "Turret": s.Turrets[0].AngleBone = Bone(tokens); Extra(c, canonical, tokens, 1, location); break;
            case "TurretArtAngle": s.Turrets[0].ArtAngle = Real(c, canonical, tokens, 0, location); Extra(c, canonical, tokens, 1, location); break;
            case "TurretPitch": s.Turrets[0].PitchBone = Bone(tokens); Extra(c, canonical, tokens, 1, location); break;
            case "TurretArtPitch": s.Turrets[0].ArtPitch = Real(c, canonical, tokens, 0, location); Extra(c, canonical, tokens, 1, location); break;
            case "AltTurret": s.Turrets[1].AngleBone = Bone(tokens); Extra(c, canonical, tokens, 1, location); break;
            case "AltTurretArtAngle": s.Turrets[1].ArtAngle = Real(c, canonical, tokens, 0, location); Extra(c, canonical, tokens, 1, location); break;
            case "AltTurretPitch": s.Turrets[1].PitchBone = Bone(tokens); Extra(c, canonical, tokens, 1, location); break;
            case "AltTurretArtPitch": s.Turrets[1].ArtPitch = Real(c, canonical, tokens, 0, location); Extra(c, canonical, tokens, 1, location); break;
            case "ShowSubObject" or "HideSubObject": ShowHide(s, tokens, canonical == "HideSubObject"); break;
            case "WeaponFireFXBone": WeaponBone(c, s, canonical, tokens, 0, location); break;
            case "WeaponRecoilBone": WeaponBone(c, s, canonical, tokens, 1, location); break;
            case "WeaponMuzzleFlash": WeaponBone(c, s, canonical, tokens, 2, location); break;
            case "WeaponLaunchBone": WeaponBone(c, s, canonical, tokens, 3, location); break;
            case "WeaponHideShowBone": WeaponBone(c, s, canonical, tokens, 4, location); break;
            case "Animation" or "IdleAnimation": Animation(c, s, canonical, tokens, canonical == "IdleAnimation", location); break;
            case "AnimationMode":
                if (Index(c, canonical, tokens, AnimModeNames, location) is var mode and >= 0) s.Mode = (AnimationMode)mode;
                break;
            case "TransitionKey": s.TransitionKey = FirstLower(tokens); Extra(c, canonical, tokens, 1, location); break;
            case "WaitForStateToFinishIfPossible": s.AllowToFinishKey = FirstLower(tokens); Extra(c, canonical, tokens, 1, location); break;
            case "Flags": s.Flags = (AnimationFlags)BitString(c, canonical, tokens, AnimFlagNames, (int)s.Flags, location); break;
            case "ParticleSysBone":
                if (tokens.Length < 2) { c.Error("invalid-value", canonical, "ParticleSysBone needs a bone and a particle system.", location); break; }
                s.ParticleSysBones.Add(new ParticleSysBoneSpec(tokens[0].ToLowerInvariant(), tokens[1]));
                Extra(c, canonical, tokens, 2, location);
                break;
            case "AnimationSpeedFactorRange":
                if (tokens.Length < 2) { c.Error("invalid-value", canonical, "AnimationSpeedFactorRange needs a minimum and a maximum.", location); break; }
                s.SpeedMin = Real(c, canonical, tokens, 0, location);
                s.SpeedMax = Real(c, canonical, tokens, 1, location);
                Extra(c, canonical, tokens, 2, location);
                break;
        }
    }

    private static void Animation(Context c, StateBuilder s, string key, string[] tokens, bool idle, IniSourceLocation location)
    {
        if (tokens.Length == 0) { c.Error("invalid-value", key, "An animation needs a name.", location); return; }
        var name = tokens[0].ToLowerInvariant();
        var distance = tokens.Length > 1 ? Real(c, key, tokens, 1, location) : 0m;
        var repeat = 1L;
        if (tokens.Length > 2 && !long.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out repeat))
        {
            c.Error("invalid-value", key, $"Repeat count '{tokens[2]}' is not an integer.", location);
            repeat = 1;
        }
        Extra(c, key, tokens, 3, location);
        if (repeat < 1) repeat = 1;
        if (s.CopiedFromDefault)
        {
            s.CopiedFromDefault = false;
            s.GotIdle = false;
            s.GotNormal = false;
            s.Animations.Clear();
        }
        if (idle) s.GotIdle = true; else s.GotNormal = true;
        if (name.Equals("none", StringComparison.Ordinal)) return;
        var dot = name.IndexOf('.');
        var spec = dot < 0
            ? new AnimationSpec(name, null, null, distance, idle)
            : new AnimationSpec(name, name[..dot], name[(dot + 1)..] + ".w3d", distance, idle);
        if (dot < 0) c.Warning("animation-without-dot", key, $"Animation '{name}' has no 'hierarchy.animation' form; the reference cannot load it.", location);
        for (var i = 0; i < repeat; i++) s.Animations.Add(spec);
    }

    private static void ShowHide(StateBuilder s, string[] tokens, bool hide)
    {
        if (tokens.Length == 0) return;
        if (tokens[0].Equals("NONE", StringComparison.OrdinalIgnoreCase))
        {
            s.SubObjects.Clear();
            return;
        }
        foreach (var token in tokens)
        {
            var name = token.ToLowerInvariant();
            var index = s.SubObjects.FindIndex(entry => entry.SubObject.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) s.SubObjects[index] = s.SubObjects[index] with { Hide = hide };
            else s.SubObjects.Add(new SubObjectVisibility(name, hide));
        }
    }

    private static void WeaponBone(Context c, StateBuilder s, string key, string[] tokens, int kind, IniSourceLocation location)
    {
        if (tokens.Length < 2) { c.Error("invalid-value", key, $"{key} needs a weapon slot and a bone.", location); return; }
        var slot = Array.FindIndex(WeaponSlots, name => name.Equals(tokens[0], StringComparison.OrdinalIgnoreCase));
        if (slot < 0) { c.Error("unknown-enum", key, $"'{tokens[0]}' is not a weapon slot (TheWeaponSlotTypeNames).", location); return; }
        var bone = tokens[1].Equals("NONE", StringComparison.OrdinalIgnoreCase) ? null : tokens[1].ToLowerInvariant();
        s.WeaponBones[slot, kind] = bone;
        Extra(c, key, tokens, 2, location);
    }

    // ------------------------------------------------------------------------------------------------ value grammar

    /// <summary>
    /// The token stream the reference sees when it reads a value with <c>INI::getNextAsciiString</c> (<c>INI.cpp</c>): tokens split at
    /// blanks, tabs and '='; a token that starts with a quote is joined, strtok-style, with the text up to the next quote (<c>m_sepsQuote</c>),
    /// so <c>"UIMOB03 R HAND"</c> is one token <c>UIMOB03 R HAND</c>.
    /// </summary>
    public static string[] Tokens(string text)
    {
        const string separators = " \t=";
        const string quoteSeparators = "\"=";
        var tokens = new List<string>();
        var i = 0;
        while (true)
        {
            while (i < text.Length && separators.Contains(text[i])) i++;
            if (i >= text.Length) break;
            var end = text.IndexOfAny(separators.ToCharArray(), i);
            var token = end < 0 ? text[i..] : text[i..end];
            i = end < 0 ? text.Length : end + 1;
            if (token[0] != '"')
            {
                tokens.Add(token);
                continue;
            }
            var buffer = token[1..];
            while (i < text.Length && quoteSeparators.Contains(text[i])) i++;
            if (i >= text.Length)
            {
                tokens.Add(buffer.EndsWith('"') ? buffer[..^1] : buffer);
                break;
            }
            var stop = text.IndexOfAny(quoteSeparators.ToCharArray(), i);
            var rest = stop < 0 ? text[i..] : text[i..stop];
            i = stop < 0 ? text.Length : stop + 1;
            tokens.Add(buffer + (rest.Length > 1 && rest[1] != '\t' ? " " : string.Empty) + rest);
        }
        return [..tokens];
    }

    private static ModelConditionSet Conditions(Context c, IEnumerable<string> tokens, ModelConditionSet start, IniSourceLocation location)
    {
        var errors = new List<string>();
        var set = ModelConditions.ParseList(tokens, start, errors);
        foreach (var error in errors)
        {
            if (error.StartsWith("unknown:", StringComparison.Ordinal))
                c.Error("unknown-condition", error["unknown:".Length..], "Not a model condition (ModelConditionFlags::s_bitNameList).", location);
            else c.Error("invalid-value", error, "Plain and +/- model conditions may not be mixed.", location);
        }
        return set;
    }

    private static string? Bone(string[] tokens)
        => tokens.Length == 0 || tokens[0].Equals("NONE", StringComparison.OrdinalIgnoreCase) ? null : tokens[0].ToLowerInvariant();

    private static string? FirstLower(string[] tokens) => tokens.Length == 0 ? null : tokens[0].ToLowerInvariant();

    private static bool Bool(Context c, string key, string[] tokens, IniSourceLocation location, bool fallback)
    {
        if (tokens.Length > 0 && ValueGrammar.TryBool(tokens[0], out var value))
        {
            Extra(c, key, tokens, 1, location);
            return value;
        }
        c.Error("invalid-value", key, $"'{string.Join(' ', tokens)}' is not Yes or No.", location);
        return fallback;
    }

    private static decimal Real(Context c, string key, string[] tokens, int index, IniSourceLocation location)
    {
        if (index < tokens.Length && ValueGrammar.TryRealPrefix(tokens[index], out var value, out _)) return value;
        c.Error("invalid-value", key, $"Token {index + 1} of '{string.Join(' ', tokens)}' is not a number.", location);
        return 0m;
    }

    private static int Index(Context c, string key, string[] tokens, string[] names, IniSourceLocation location)
    {
        if (tokens.Length == 0) { c.Error("invalid-value", key, "A value is required.", location); return -1; }
        var index = Array.FindIndex(names, name => name.Equals(tokens[0], StringComparison.OrdinalIgnoreCase));
        if (index < 0) c.Error("unknown-enum", key, $"'{tokens[0]}' is not one of {string.Join(", ", names)}.", location);
        Extra(c, key, tokens, 1, location);
        return index;
    }

    /// <summary><c>INI::parseBitString32</c>: NONE clears, +/- modify <paramref name="start"/>, a plain list replaces it.</summary>
    private static int BitString(Context c, string key, string[] tokens, string[] names, int start, IniSourceLocation location)
    {
        var bits = start;
        var normal = false;
        var delta = false;
        foreach (var token in tokens)
        {
            if (token.Equals("NONE", StringComparison.OrdinalIgnoreCase)) return 0;
            var isDelta = token[0] is '+' or '-';
            if (isDelta ? normal : delta) c.Error("invalid-value", key, "Plain and +/- names may not be mixed.", location);
            var name = isDelta ? token[1..] : token;
            var index = Array.FindIndex(names, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index < 0) { c.Error("unknown-enum", key, $"'{name}' is not one of {string.Join(", ", names)}.", location); continue; }
            if (isDelta) { delta = true; bits = token[0] == '+' ? bits | (1 << index) : bits & ~(1 << index); }
            else
            {
                if (!normal) bits = 0;
                normal = true;
                bits |= 1 << index;
            }
        }
        return bits;
    }

    /// <summary>Tokens the reference reads past without looking at them: reported as a warning, never an error.</summary>
    private static void Extra(Context c, string key, string[] tokens, int used, IniSourceLocation location)
    {
        if (tokens.Length > used)
            c.Warning("ignored-tokens", key, $"The reference ignores '{string.Join(' ', tokens[used..])}'.", location);
    }

    // ------------------------------------------------------------------------------------------------ builders

    private enum StateKind { Default, Normal, Transition }

    private sealed class Context(string moduleType, List<DrawDiagnostic> diagnostics)
    {
        public void Error(string code, string key, string detail, IniSourceLocation location)
            => diagnostics.Add(new DrawDiagnostic("error", code, moduleType, key, detail, location));

        public void Warning(string code, string key, string detail, IniSourceLocation location)
            => diagnostics.Add(new DrawDiagnostic("warning", code, moduleType, key, detail, location));
    }

    private sealed class TurretBuilder
    {
        public string? AngleBone;
        public decimal ArtAngle;
        public string? PitchBone;
        public decimal ArtPitch;
        public TurretBuilder Clone() => (TurretBuilder)MemberwiseClone();
        public TurretSpec Build() => AngleBone is null && PitchBone is null && ArtAngle == 0m && ArtPitch == 0m
            ? TurretSpec.None
            : new TurretSpec(AngleBone, ArtAngle, PitchBone, ArtPitch);
    }

    private sealed class StateBuilder
    {
        public bool IsDefault;
        public List<ModelConditionSet> Conditions = [];
        public string? TransitionFrom;
        public string? TransitionTo;
        public bool ModelGiven;
        public string? Model;
        public List<AnimationSpec> Animations = [];
        public bool CopiedFromDefault;
        public bool GotIdle;
        public bool GotNormal;
        public AnimationMode Mode = AnimationMode.Once;
        public decimal SpeedMin = 1m;
        public decimal SpeedMax = 1m;
        public AnimationFlags Flags;
        public string? TransitionKey;
        public string? AllowToFinishKey;
        public TurretBuilder[] Turrets = [new(), new()];
        public string?[,] WeaponBones = new string?[3, 5];
        public List<SubObjectVisibility> SubObjects = [];
        public List<ParticleSysBoneSpec> ParticleSysBones = [];

        public StateBuilder Clone(bool copiedFromDefault)
        {
            var copy = (StateBuilder)MemberwiseClone();
            copy.Conditions = [..Conditions];
            copy.Animations = [..Animations];
            copy.Turrets = [Turrets[0].Clone(), Turrets[1].Clone()];
            copy.WeaponBones = (string?[,])WeaponBones.Clone();
            copy.SubObjects = [..SubObjects];
            copy.ParticleSysBones = [..ParticleSysBones];
            copy.CopiedFromDefault = copiedFromDefault;
            return copy;
        }

        public ModelStateSpec Build(int index) => new(
            index, IsDefault, [..Conditions], TransitionFrom, TransitionTo, Model, [..Animations], Mode, SpeedMin, SpeedMax, Flags,
            TransitionKey, AllowToFinishKey, Turrets[0].Build(), Turrets[1].Build(),
            [..Enumerable.Range(0, 3).Select(slot => WeaponBones[slot, 0] is null && WeaponBones[slot, 1] is null && WeaponBones[slot, 2] is null
                                                     && WeaponBones[slot, 3] is null && WeaponBones[slot, 4] is null
                ? WeaponSlotBones.None
                : new WeaponSlotBones(WeaponBones[slot, 0], WeaponBones[slot, 1], WeaponBones[slot, 2], WeaponBones[slot, 3], WeaponBones[slot, 4]))],
            [..SubObjects], [..ParticleSysBones]);
    }

    private sealed class ModuleBuilder(DrawModuleInfo info)
    {
        public DrawModuleInfo Info { get; } = info;
        public List<StateBuilder> States { get; } = [];
        public List<StateBuilder> Transitions { get; } = [];
        public int DefaultIndex = -1;
        public ModelConditionSet Ignore;
        public bool OkToChangeModelColor;
        public bool AnimationsRequirePower = true;
        public bool ParticlesAttached;
        public string? TrackMarks;
        public List<string> ExtraPublicBones { get; } = [];
        public string? AttachToBone;
        public string? ModelName;
        public List<DrawSetting> Settings { get; } = [];

        public DrawSpec Build(ModuleConfiguration module, Context context)
        {
            var states = States.Select((state, index) => state.Build(index)).ToImmutableArray();
            var defaultIndex = DefaultIndex;
            if (Info.Kind is DrawModuleKind.Tree or DrawModuleKind.Prop && ModelName is not null)
            {
                var model = ModelName.Equals("NONE", StringComparison.OrdinalIgnoreCase) ? null : ModelName;
                states = [new StateBuilder { IsDefault = true, Conditions = [ModelConditionSet.Empty], ModelGiven = true, Model = model }.Build(0)];
                defaultIndex = 0;
            }
            return new DrawSpec(Info.ModuleType, module.Tag, Info.Kind, states, defaultIndex,
                [..Transitions.Select((state, index) => state.Build(index))], Ignore, OkToChangeModelColor, AnimationsRequirePower,
                ParticlesAttached, TrackMarks, [..ExtraPublicBones], AttachToBone, [..Settings]);
        }
    }
}
