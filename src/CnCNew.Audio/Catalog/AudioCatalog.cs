using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Data.Ini;
using DataAudioEvent = CnCNew.Data.Definitions.AudioEventDefinition;
using DataDialogEvent = CnCNew.Data.Definitions.DialogEventDefinition;
using DataMusicTrack = CnCNew.Data.Definitions.MusicTrackDefinition;

namespace CnCNew.Audio.Catalog;

public sealed record AudioCatalogOptions(string Language = "English")
{
    public static AudioCatalogOptions Default { get; } = new();
}

/// <summary>
/// The typed audio definitions of a compiled graph. The compiler keeps audio blocks as generically typed fields; this
/// catalog re-reads every field's original text with the reference parse tables so that nothing depends on heuristic
/// typing (e.g. the two-value <c>PitchShift</c>). Semantics:
/// <list type="bullet">
/// <item>Every event starts as a copy of its template — <c>DefaultSoundEffect</c>, <c>DefaultDialog</c> or
/// <c>DefaultMusicTrack</c> — exactly like <c>INI::parseAudioEventDefinition</c>/<c>parseDialogDefinition</c>/
/// <c>parseMusicTrackDefinition</c> (<c>INIAudioEventInfo.cpp</c>).</item>
/// <item>A redefinition resets the event to its template before parsing (<c>newAudioEventInfo</c> returns the existing
/// info, which is then overwritten with the template), so only the last block's fields count; the compiler's merged
/// fields from earlier blocks are dropped and counted as <c>redefinition-reset</c>.</item>
/// <item>File names are built like <c>AudioEventRTS::generateFilename</c>/<c>generateFilenamePrefix</c>:
/// <c>AudioRoot\Folder\name</c> (+ <c>.SoundsExtension</c> for sound effects), preferring
/// <c>AudioRoot\Folder\&lt;Language&gt;\name</c> when that file exists (<c>adjustForLocalization</c>).</item>
/// </list>
/// </summary>
public sealed class AudioCatalog
{
    public const string SoundEffectTemplate = "DefaultSoundEffect";
    public const string DialogTemplate = "DefaultDialog";
    public const string MusicTemplate = "DefaultMusicTrack";

    private readonly ImmutableDictionary<string, AudioEventInfo> _byName;

    private AudioCatalog(AudioSettingsInfo settings, MiscAudioInfo misc, ImmutableArray<AudioEventInfo> events, AudioCatalogReport report)
    {
        Settings = settings;
        MiscAudio = misc;
        Events = events;
        Report = report;
        _byName = events.ToImmutableDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
    }

    public AudioSettingsInfo Settings { get; }
    public MiscAudioInfo MiscAudio { get; }

    /// <summary>All events including the three templates, ordered by name (ordinal, case-insensitive).</summary>
    public ImmutableArray<AudioEventInfo> Events { get; }

    public AudioCatalogReport Report { get; }

    public IEnumerable<AudioEventInfo> SoundEffects => Events.Where(value => value.Type == AudioType.SoundEffect && !value.IsTemplate);
    public IEnumerable<AudioEventInfo> MusicTracks => Events.Where(value => value.Type == AudioType.Music && !value.IsTemplate);
    public IEnumerable<AudioEventInfo> DialogEvents => Events.Where(value => value.Type == AudioType.Streaming && !value.IsTemplate);

    /// <summary>Looks an event up by name; <c>NoSound</c> and the empty name are the reference's explicit silence and return null.</summary>
    public AudioEventInfo? Find(string name)
        => string.IsNullOrEmpty(name) || name.Equals("NoSound", StringComparison.OrdinalIgnoreCase) ? null : _byName.GetValueOrDefault(name);

    public static AudioCatalog FromGraph(CompiledDefinitionGraph graph, Func<string, bool> fileExists, AudioCatalogOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(fileExists);
        options ??= AudioCatalogOptions.Default;
        var builder = new Builder(graph, fileExists, options);
        return builder.Build();
    }

    private sealed class Builder(CompiledDefinitionGraph graph, Func<string, bool> fileExists, AudioCatalogOptions options)
    {
        private readonly List<AudioCatalogDiagnostic> _diagnostics = [];
        private readonly List<UnresolvedAudioFile> _unresolved = [];
        private readonly Dictionary<string, bool> _existsCache = new(StringComparer.OrdinalIgnoreCase);

        public AudioCatalog Build()
        {
            var settings = BuildSettings();
            var loadOrder = graph.LoadedFiles.Select((file, index) => (file.Path, index))
                .GroupBy(value => value.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Max(value => value.index), StringComparer.OrdinalIgnoreCase);

            var sources = graph.Definitions
                .Select(definition => definition switch
                {
                    DataAudioEvent => (definition, Type: (AudioType?)AudioType.SoundEffect),
                    DataDialogEvent => (definition, Type: AudioType.Streaming),
                    DataMusicTrack => (definition, Type: AudioType.Music),
                    _ => (definition, Type: null),
                })
                .Where(value => value.Type is not null)
                .Select(value => (Definition: value.definition, Type: value.Type!.Value))
                .ToList();

            // The reference keeps all three kinds in one name map; the block loaded last wins.
            var winners = new Dictionary<string, (GameDefinition Definition, AudioType Type)>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources.OrderBy(value => loadOrder.GetValueOrDefault(value.Definition.Location.Source, int.MaxValue))
                         .ThenBy(value => value.Definition.Location.Line))
            {
                if (winners.TryGetValue(source.Definition.OriginalName, out var previous) && previous.Type != source.Type)
                    Report("cross-kind-redefinition", source.Definition.OriginalName, "", $"{previous.Type} replaced by {source.Type}");
                winners[source.Definition.OriginalName] = source;
            }

            var multiFile = graph.MultiFileDefinitions.Select(value => value.Id).ToHashSet();
            var templates = new Dictionary<string, EventState>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in new[] { MusicTemplate, SoundEffectTemplate, DialogTemplate })
            {
                if (!winners.TryGetValue(name, out var template))
                {
                    Report("missing-template", name, "", "events of this kind start from zero values");
                    continue;
                }
                if (multiFile.Contains(template.Definition.Id))
                    Report("template-redefined", name, "", "the final template state is used for every event");
                var state = new EventState(template.Type);
                Apply(state, template.Definition, template.Definition.OriginalName);
                templates[name] = state;
            }

            var events = new List<AudioEventInfo>();
            foreach (var (name, source) in winners.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                var isTemplate = name.Equals(SoundEffectTemplate, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(DialogTemplate, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(MusicTemplate, StringComparison.OrdinalIgnoreCase);
                EventState state;
                if (isTemplate && templates.TryGetValue(name, out var own))
                    state = own.Clone(source.Type);
                else
                {
                    var templateName = source.Type switch
                    {
                        AudioType.Music => MusicTemplate,
                        AudioType.Streaming => DialogTemplate,
                        _ => SoundEffectTemplate,
                    };
                    state = templates.TryGetValue(templateName, out var template) ? template.Clone(source.Type) : new EventState(source.Type);
                    Apply(state, source.Definition, source.Definition.OriginalName);
                }
                events.Add(Resolve(settings, state, source.Definition, isTemplate));
            }

            var catalogEvents = events.ToImmutableArray();
            var misc = BuildMiscAudio(catalogEvents, out var unresolvedHooks);
            var nonTemplates = catalogEvents.Where(value => !value.IsTemplate).ToArray();
            var references = nonTemplates.SelectMany(value => value.AllFiles).ToArray();
            var silent = nonTemplates.Count(value => value.Type == AudioType.SoundEffect ? value.Sounds.IsEmpty : value.File is null);
            var report = new AudioCatalogReport(
                nonTemplates.Count(value => value.Type == AudioType.SoundEffect),
                nonTemplates.Count(value => value.Type == AudioType.Music),
                nonTemplates.Count(value => value.Type == AudioType.Streaming),
                catalogEvents.Length - nonTemplates.Length,
                references.Length,
                references.Count(value => value.IsResolved),
                references.Count(value => value.Localized),
                references.Where(value => value.IsResolved).Select(value => value.VirtualPath!).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                silent,
                [.._unresolved.OrderBy(value => value.Event, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.Key, StringComparer.Ordinal).ThenBy(value => value.Name, StringComparer.Ordinal)],
                _diagnostics.GroupBy(value => value.Code == "unknown-key" ? $"unknown-key:{value.Key}" : value.Code, StringComparer.Ordinal)
                    .ToImmutableSortedDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                [.._diagnostics.OrderBy(value => value.Code, StringComparer.Ordinal).ThenBy(value => value.Event, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(value => value.Key, StringComparer.Ordinal).ThenBy(value => value.Detail, StringComparer.Ordinal)],
                misc.Hooks.Count,
                unresolvedHooks);
            return new AudioCatalog(settings, misc, catalogEvents, report);
        }

        // ------------------------------------------------------------------------------------------ events

        private void Apply(EventState state, GameDefinition definition, string name)
        {
            foreach (var field in LastBlockFields(definition, name))
            {
                var text = field.Value.OriginalText;
                switch (field.Name.ToLowerInvariant())
                {
                    case "filename": state.Filename = ParseString(text); break;
                    case "volume": Percent(name, field, value => state.Volume = value); break;
                    case "volumeshift": Percent(name, field, value => state.VolumeShift = value); break;
                    case "minvolume": Percent(name, field, value => state.MinVolume = value); break;
                    case "lowpasscutoff": Percent(name, field, value => state.LowPassCutoff = value); break;
                    case "minrange": Real(name, field, value => state.MinRange = value); break;
                    case "maxrange": Real(name, field, value => state.MaxRange = value); break;
                    case "limit": Integer(name, field, value => state.Limit = value); break;
                    case "loopcount": Integer(name, field, value => state.LoopCount = value); break;
                    case "pitchshift": ParsePitchShift(state, name, field); break;
                    case "delay": ParseDelay(state, name, field); break;
                    case "priority": ParsePriority(state, name, field); break;
                    case "type": state.Types = (SoundTypeFlags)ParseBits((int)state.Types, SoundTypeNames, name, field); break;
                    case "control": state.Control = (AudioControlFlags)ParseBits((int)state.Control, ControlNames, name, field); break;
                    case "sounds": state.Sounds = SoundsList(text); break;
                    case "soundsmorning": state.SoundsMorning = SoundsList(text); break;
                    case "soundsevening": state.SoundsEvening = SoundsList(text); break;
                    case "soundsnight": state.SoundsNight = SoundsList(text); break;
                    case "attack": state.Attack = SoundsList(text); break;
                    case "decay": state.Decay = SoundsList(text); break;
                    default: Report("unknown-key", name, field.Name, text); break;
                }
            }
        }

        /// <summary>Fields of the last block only (see the class remarks), in source order.</summary>
        private IEnumerable<DefinitionField> LastBlockFields(GameDefinition definition, string name)
        {
            var header = definition.Location;
            var dropped = 0;
            var kept = new List<DefinitionField>();
            foreach (var field in definition.Fields)
            {
                if (field.Location.Source.Equals(header.Source, StringComparison.OrdinalIgnoreCase) && field.Location.Line > header.Line)
                    kept.Add(field);
                else
                    dropped++;
            }
            if (dropped > 0) Report("redefinition-reset", name, "", $"{dropped} field(s) of an earlier block dropped");
            return kept.OrderBy(field => field.Location.Line).ThenBy(field => field.Location.Column);
        }

        private AudioEventInfo Resolve(AudioSettingsInfo settings, EventState state, GameDefinition definition, bool isTemplate)
        {
            var name = definition.OriginalName;
            var folder = state.Type switch
            {
                AudioType.Music => settings.MusicFolder,
                AudioType.Streaming => settings.StreamingFolder,
                _ => settings.SoundsFolder,
            };
            var extension = state.Type == AudioType.Music ? string.Empty : "." + settings.SoundsExtension;

            AudioSoundFile File(string key, string soundName, bool addExtension)
            {
                var fileName = addExtension ? soundName + extension : soundName;
                var generic = JoinPath(settings.AudioRoot, folder, fileName);
                var localized = JoinPath(settings.AudioRoot, folder, options.Language, fileName);
                if (Exists(localized)) return new AudioSoundFile(soundName, localized, true);
                if (Exists(generic)) return new AudioSoundFile(soundName, generic, false);
                if (!isTemplate) _unresolved.Add(new UnresolvedAudioFile(name, key, soundName, generic));
                return new AudioSoundFile(soundName, null, false);
            }

            ImmutableArray<AudioSoundFile> Files(string key, ImmutableArray<string> names)
                => [..names.Select(soundName => File(key, soundName, addExtension: true))];

            AudioSoundFile? file = null;
            if (state.Type is AudioType.Music or AudioType.Streaming)
            {
                // Music and streaming play m_filename as written (no extension appended); their Sounds lists are unused.
                if (state.Filename is { Length: > 0 } filename) file = File("Filename", filename, addExtension: false);
                if (!state.Sounds.IsEmpty && !isTemplate) Report("sounds-ignored", name, "Sounds", "music and dialog events play Filename only");
            }
            else if (state.Filename is not null && !isTemplate)
                Report("filename-ignored", name, "Filename", "sound effects play their Sounds list only");

            return new AudioEventInfo(name, state.Type, isTemplate, state.Filename, file,
                state.Volume, state.VolumeShift, state.MinVolume, state.PitchShiftMin, state.PitchShiftMax,
                state.DelayMin, state.DelayMax, state.Limit, state.LoopCount, state.Priority, state.Types, state.Control,
                Files("Sounds", state.Sounds), Files("SoundsMorning", state.SoundsMorning), Files("SoundsEvening", state.SoundsEvening),
                Files("SoundsNight", state.SoundsNight), Files("Attack", state.Attack), Files("Decay", state.Decay),
                state.LowPassCutoff, state.MinRange, state.MaxRange, definition.Location);
        }

        private bool Exists(string path)
        {
            if (!_existsCache.TryGetValue(path, out var exists))
                _existsCache[path] = exists = fileExists(path);
            return exists;
        }

        private static string JoinPath(params string[] parts)
            => string.Join('/', parts.SelectMany(part => part.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)));

        private void ParsePitchShift(EventState state, string name, DefinitionField field)
        {
            var tokens = ValueGrammar.Tokenize(field.Value.OriginalText);
            if (tokens.Length < 2 || !TryReal(tokens[0], out var min, name, field) || !TryReal(tokens[1], out var max, name, field))
            {
                Report("invalid-value", name, field.Name, field.Value.OriginalText);
                return;
            }
            if (tokens.Length > 2) Report("extra-tokens", name, field.Name, field.Value.OriginalText);
            if (!(min > -100 && max >= min)) Report("invalid-range", name, field.Name, field.Value.OriginalText);
            state.PitchShiftMin = 1f + min / 100f;
            state.PitchShiftMax = 1f + max / 100f;
        }

        private void ParseDelay(EventState state, string name, DefinitionField field)
        {
            var tokens = ValueGrammar.Tokenize(field.Value.OriginalText);
            if (tokens.Length < 2 || !TryInt(tokens[0], out var min, name, field) || !TryInt(tokens[1], out var max, name, field))
            {
                Report("invalid-value", name, field.Name, field.Value.OriginalText);
                return;
            }
            if (tokens.Length > 2) Report("extra-tokens", name, field.Name, field.Value.OriginalText);
            if (!(min >= 0 && max >= min)) Report("invalid-range", name, field.Name, field.Value.OriginalText);
            state.DelayMin = min;
            state.DelayMax = max;
        }

        private void ParsePriority(EventState state, string name, DefinitionField field)
        {
            var tokens = ValueGrammar.Tokenize(field.Value.OriginalText);
            var index = tokens.Length == 0 ? -1 : Array.FindIndex(PriorityNames, value => value.Equals(tokens[0], StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                Report("invalid-value", name, field.Name, field.Value.OriginalText);
                return;
            }
            if (tokens.Length > 1) Report("extra-tokens", name, field.Name, field.Value.OriginalText);
            state.Priority = (AudioPriority)index;
        }

        /// <summary><c>INI::parseBitString32</c>: <c>NONE</c> clears, plain names replace, <c>+X</c>/<c>-X</c> edit the inherited bits.</summary>
        private int ParseBits(int inherited, string[] names, string name, DefinitionField field)
        {
            var bits = inherited;
            var normal = false;
            var relative = false;
            foreach (var token in ValueGrammar.Tokenize(field.Value.OriginalText))
            {
                if (token.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                {
                    if (normal || relative) Report("mixed-bit-syntax", name, field.Name, field.Value.OriginalText);
                    return 0;
                }
                var isRelative = token[0] is '+' or '-';
                var flagName = isRelative ? token[1..] : token;
                var index = Array.FindIndex(names, value => value.Equals(flagName, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    Report("invalid-value", name, field.Name, token);
                    continue;
                }
                if (isRelative)
                {
                    if (normal) Report("mixed-bit-syntax", name, field.Name, field.Value.OriginalText);
                    relative = true;
                    bits = token[0] == '+' ? bits | (1 << index) : bits & ~(1 << index);
                }
                else
                {
                    if (relative) Report("mixed-bit-syntax", name, field.Name, field.Value.OriginalText);
                    if (!normal) bits = 0;
                    normal = true;
                    bits |= 1 << index;
                }
            }
            return bits;
        }

        /// <summary><c>INI::parseSoundsList</c>: all tokens separated by blanks, commas or '='.</summary>
        private static ImmutableArray<string> SoundsList(string text)
            => [..text.Split([' ', '\t', ',', '='], StringSplitOptions.RemoveEmptyEntries)];

        private static string ParseString(string text)
        {
            var trimmed = text.Trim();
            if (trimmed.StartsWith('"'))
            {
                var end = trimmed.IndexOf('"', 1);
                return end < 0 ? trimmed[1..] : trimmed[1..end];
            }
            var tokens = ValueGrammar.Tokenize(trimmed);
            return tokens.Length == 0 ? string.Empty : tokens[0];
        }

        private void Percent(string name, DefinitionField field, Action<float> assign)
        {
            var tokens = field.Value.OriginalText.Split([' ', '\t', '=', '%'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || !TryReal(tokens[0], out var value, name, field))
            {
                Report("invalid-value", name, field.Name, field.Value.OriginalText);
                return;
            }
            if (tokens.Length > 1) Report("extra-tokens", name, field.Name, field.Value.OriginalText);
            assign(value / 100f);
        }

        private void Real(string name, DefinitionField field, Action<float> assign)
        {
            var tokens = ValueGrammar.Tokenize(field.Value.OriginalText);
            if (tokens.Length == 0 || !TryReal(tokens[0], out var value, name, field))
            {
                Report("invalid-value", name, field.Name, field.Value.OriginalText);
                return;
            }
            if (tokens.Length > 1) Report("extra-tokens", name, field.Name, field.Value.OriginalText);
            assign(value);
        }

        private void Integer(string name, DefinitionField field, Action<int> assign)
        {
            var tokens = ValueGrammar.Tokenize(field.Value.OriginalText);
            if (tokens.Length == 0 || !TryInt(tokens[0], out var value, name, field))
            {
                Report("invalid-value", name, field.Name, field.Value.OriginalText);
                return;
            }
            if (tokens.Length > 1) Report("extra-tokens", name, field.Name, field.Value.OriginalText);
            assign(value);
        }

        /// <summary><c>INI::scanReal</c> (<c>sscanf %f</c>) accepts a numeric prefix; trailing text is reported.</summary>
        private bool TryReal(string token, out float value, string name, DefinitionField field)
        {
            value = 0;
            if (!ValueGrammar.TryRealPrefix(token, out var parsed, out var trailing)) return false;
            if (trailing) Report("trailing-text", name, field.Name, token);
            value = (float)parsed;
            return true;
        }

        /// <summary><c>INI::scanInt</c> (<c>sscanf %d</c>): an integer prefix; a fractional or other tail is reported.</summary>
        private bool TryInt(string token, out int value, string name, DefinitionField field)
        {
            value = 0;
            var end = token.Length > 0 && token[0] is '+' or '-' ? 1 : 0;
            var start = end;
            while (end < token.Length && char.IsAsciiDigit(token[end])) end++;
            if (end == start || !int.TryParse(token.AsSpan(0, end), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)) return false;
            if (end != token.Length) Report("trailing-text", name, field.Name, token);
            return true;
        }

        private void Report(string code, string eventName, string key, string detail)
            => _diagnostics.Add(new AudioCatalogDiagnostic(code, eventName, key, detail));

        // ------------------------------------------------------------------------------------------ settings

        private AudioSettingsInfo BuildSettings()
        {
            var definition = graph.Definitions.OfType<CompatibilityDefinition>()
                .SingleOrDefault(value => value.Id.Name == "audiosettings/audiosettings")
                ?? throw new InvalidDataException("The graph has no AudioSettings block (Data/INI/AudioSettings.ini).");
            var values = new Dictionary<string, DefinitionField>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in definition.Fields.OrderBy(value => value.Location.Line))
                values[field.Name] = field;

            const string owner = "AudioSettings";
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string Text(string key, bool optional = false)
            {
                known.Add(key);
                if (values.TryGetValue(key, out var field)) return ParseString(field.Value.OriginalText);
                if (!optional) Report("missing-key", owner, key, "no value; the reference would read uninitialized memory");
                return string.Empty;
            }
            bool Bool(string key)
            {
                var text = Text(key);
                if (text.Length == 0) return false;
                if (ValueGrammar.TryBool(text, out var value)) return value;
                Report("invalid-value", owner, key, text);
                return false;
            }
            int Int(string key)
            {
                known.Add(key);
                var result = 0;
                if (values.TryGetValue(key, out var field)) Integer(owner, field, value => result = value);
                else Report("missing-key", owner, key, "no value; the reference would read uninitialized memory");
                return result;
            }
            float Ratio(string key)
            {
                known.Add(key);
                var result = 0f;
                if (values.TryGetValue(key, out var field)) Percent(owner, field, value => result = value);
                else Report("missing-key", owner, key, "no value; the reference would read uninitialized memory");
                return result;
            }
            float Number(string key)
            {
                known.Add(key);
                var result = 0f;
                if (values.TryGetValue(key, out var field)) Real(owner, field, value => result = value);
                else Report("missing-key", owner, key, "no value; the reference would read uninitialized memory");
                return result;
            }
            string Speaker(string key)
            {
                var text = Text(key);
                if (text.Length > 0 && !AudioSettingsInfo.SpeakerTypes.Contains(text, StringComparer.OrdinalIgnoreCase))
                    Report("invalid-value", owner, key, text);
                return text;
            }

            var settings = new AudioSettingsInfo(
                Text("AudioRoot"), Text("SoundsFolder"), Text("MusicFolder"), Text("StreamingFolder"), Text("SoundsExtension"),
                Bool("UseDigital"), Bool("UseMidi"), Int("OutputRate"), Int("OutputBits"), Int("OutputChannels"),
                Int("SampleCount2D"), Int("SampleCount3D"), Int("StreamCount"),
                [Text("Preferred3DHW1", true), Text("Preferred3DHW2", true), Text("Preferred3DHW3", true), Text("Preferred3DHW4", true), Text("Preferred3DSW", true)],
                Speaker("Default2DSpeakerType"), Speaker("Default3DSpeakerType"),
                Ratio("MinSampleVolume"), Int("GlobalMinRange"), Int("GlobalMaxRange"),
                Int("TimeBetweenDrawableSounds"), Int("TimeToFadeAudio"), Int("AudioFootprintInBytes"),
                Ratio("Relative2DVolume"), Ratio("DefaultSoundVolume"), Ratio("Default3DSoundVolume"), Ratio("DefaultSpeechVolume"),
                Ratio("DefaultMusicVolume"), Number("MicrophoneDesiredHeightAboveTerrain"), Ratio("MicrophoneMaxPercentageBetweenGroundAndCamera"),
                Number("ZoomMinDistance"), Number("ZoomMaxDistance"), Ratio("ZoomSoundVolumePercentageAmount"));
            foreach (var field in definition.Fields.Where(value => !known.Contains(value.Name)))
                Report("unknown-key", owner, field.Name, field.Value.OriginalText);
            if (settings.AudioRoot.Length == 0 || settings.SoundsFolder.Length == 0 || settings.SoundsExtension.Length == 0)
                throw new InvalidDataException("AudioSettings must define AudioRoot, SoundsFolder and SoundsExtension to resolve sound files.");
            return settings;
        }

        private MiscAudioInfo BuildMiscAudio(ImmutableArray<AudioEventInfo> events, out ImmutableArray<string> unresolved)
        {
            var names = events.Select(value => value.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hooks = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            var missing = new List<string>();
            var definition = graph.Definitions.OfType<CompatibilityDefinition>().SingleOrDefault(value => value.Id.Name == "miscaudio/miscaudio");
            if (definition is null)
                Report("missing-misc-audio", "MiscAudio", "", "no MiscAudio block");
            else
            {
                foreach (var field in definition.Fields.OrderBy(value => value.Location.Line))
                {
                    var known = MiscAudioInfo.KnownHooks.FirstOrDefault(value => value.Equals(field.Name, StringComparison.OrdinalIgnoreCase));
                    if (known is null)
                    {
                        Report("unknown-key", "MiscAudio", field.Name, field.Value.OriginalText);
                        continue;
                    }
                    var eventName = ParseString(field.Value.OriginalText);
                    hooks[known] = eventName;
                    if (!eventName.Equals("NoSound", StringComparison.OrdinalIgnoreCase) && !names.Contains(eventName))
                        missing.Add($"{known} = {eventName}");
                }
            }
            unresolved = [..missing.Order(StringComparer.Ordinal)];
            return new MiscAudioInfo(hooks.ToImmutable());
        }
    }

    private static readonly string[] PriorityNames = ["LOWEST", "LOW", "NORMAL", "HIGH", "CRITICAL"];
    private static readonly string[] SoundTypeNames = ["UI", "WORLD", "SHROUDED", "GLOBAL", "VOICE", "PLAYER", "ALLIES", "ENEMIES", "EVERYONE"];
    private static readonly string[] ControlNames = ["LOOP", "RANDOM", "ALL", "POSTDELAY", "INTERRUPT"];

    /// <summary>Mutable parse state of one event; copied from the template like <c>(*track) = (*defaultInfo)</c>.</summary>
    private sealed class EventState(AudioType type)
    {
        public AudioType Type { get; private set; } = type;
        public string? Filename { get; set; }
        public float Volume { get; set; }
        public float VolumeShift { get; set; }
        public float MinVolume { get; set; }
        public float PitchShiftMin { get; set; } = 1f;
        public float PitchShiftMax { get; set; } = 1f;
        public int DelayMin { get; set; }
        public int DelayMax { get; set; }
        public int Limit { get; set; }
        public int LoopCount { get; set; }
        public AudioPriority Priority { get; set; }
        public SoundTypeFlags Types { get; set; }
        public AudioControlFlags Control { get; set; }
        public ImmutableArray<string> Sounds { get; set; } = [];
        public ImmutableArray<string> SoundsMorning { get; set; } = [];
        public ImmutableArray<string> SoundsEvening { get; set; } = [];
        public ImmutableArray<string> SoundsNight { get; set; } = [];
        public ImmutableArray<string> Attack { get; set; } = [];
        public ImmutableArray<string> Decay { get; set; } = [];
        public float LowPassCutoff { get; set; }
        public float MinRange { get; set; }
        public float MaxRange { get; set; }

        public EventState Clone(AudioType type)
        {
            var copy = (EventState)MemberwiseClone();
            copy.Type = type;
            return copy;
        }
    }
}
