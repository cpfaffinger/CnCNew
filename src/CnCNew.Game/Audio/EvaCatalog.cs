using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Audio.Selection;
using CnCNew.Data;

namespace CnCNew.Game.Audio;

/// <summary>
/// One <c>EvaEvent</c> block of <c>Eva.ini</c> (<c>EvaCheckInfo</c> in <c>Eva.cpp</c>). Durations are logic frames
/// (<c>parseDurationUnsignedInt</c>: ceil(ms × 30 / 1000)); <see cref="SideSounds"/> maps a player template side to the
/// dialog events the announcer picks from.
/// </summary>
public sealed record EvaEventDefinition(
    string Name,
    int Priority,
    int FramesBetweenChecks,
    int FramesToExpire,
    ImmutableSortedDictionary<string, ImmutableArray<string>> SideSounds);

/// <summary>
/// The typed contents of <c>Eva.ini</c>. The data compiler keeps this file as opaque text (it has no typed definition),
/// so the announcer parses it itself. Unknown keys and malformed blocks throw; nothing falls back silently.
/// </summary>
public sealed class EvaCatalog
{
    public const string DefaultVirtualPath = "Data/INI/Eva.ini";

    private readonly ImmutableDictionary<string, EvaEventDefinition> _byName;

    private EvaCatalog(ImmutableArray<EvaEventDefinition> events)
    {
        Events = events;
        _byName = events.ToImmutableDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Events in file order (the order is the tie-break of the announcer, like the enum order of the reference).</summary>
    public ImmutableArray<EvaEventDefinition> Events { get; }

    public static EvaCatalog Empty { get; } = new([]);

    public EvaEventDefinition? Find(string name) => _byName.GetValueOrDefault(name);

    public static EvaCatalog Parse(Stream stream, string sourceName = DefaultVirtualPath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, leaveOpen: true);
        return Parse(reader.ReadToEnd(), sourceName);
    }

    public static EvaCatalog Parse(string text, string sourceName = DefaultVirtualPath)
    {
        ArgumentNullException.ThrowIfNull(text);
        var events = new List<EvaBuilder>();
        EvaBuilder? current = null;
        SideBuilder? side = null;
        var lineNumber = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine;
            var comment = line.IndexOf(';');
            if (comment >= 0) line = line[..comment];
            line = line.Trim();
            if (line.Length == 0) continue;

            InvalidDataException Error(string message) => new($"{sourceName}({lineNumber}): {message}");

            if (current is null)
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || !parts[0].Equals("EvaEvent", StringComparison.OrdinalIgnoreCase))
                    throw Error($"expected 'EvaEvent <Name>', found '{line}'");
                // A repeated block keeps the earlier values like the reference, where newEvaCheckInfo returns the existing entry only once per name.
                current = events.FirstOrDefault(value => value.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase)) ?? new EvaBuilder(parts[1]);
                if (!events.Contains(current)) events.Add(current);
                continue;
            }

            if (line.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                if (side is not null)
                {
                    if (side.Side is null) throw Error("SideSounds without Side");
                    current.SideSounds[side.Side] = [..side.Sounds];
                    side = null;
                }
                else current = null;
                continue;
            }
            if (side is null && line.Equals("SideSounds", StringComparison.OrdinalIgnoreCase))
            {
                side = new SideBuilder();
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals < 0) throw Error($"expected 'Key = Value', found '{line}'");
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (side is not null)
            {
                if (key.Equals("Side", StringComparison.OrdinalIgnoreCase)) side.Side = value;
                else if (key.Equals("Sounds", StringComparison.OrdinalIgnoreCase))
                    side.Sounds.AddRange(value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                else throw Error($"unknown SideSounds key '{key}'");
            }
            else if (key.Equals("Priority", StringComparison.OrdinalIgnoreCase)) current.Priority = ParseInt(value, Error);
            else if (key.Equals("TimeBetweenChecksMS", StringComparison.OrdinalIgnoreCase)) current.FramesBetweenChecks = Frames(ParseInt(value, Error));
            else if (key.Equals("ExpirationTimeMS", StringComparison.OrdinalIgnoreCase))
            {
                // Retail's comment on BeaconDetected: "-1 means FOREVER".
                var milliseconds = ParseInt(value, Error, allowNegativeOne: true);
                current.FramesToExpire = milliseconds == -1 ? int.MaxValue : Frames(milliseconds);
            }
            else throw Error($"unknown EvaEvent key '{key}'");
        }
        if (current is not null) throw new InvalidDataException($"{sourceName}: EvaEvent '{current.Name}' is not closed by End");
        return new EvaCatalog([..events.Select(value => value.Build())]);
    }

    private static int ParseInt(string value, Func<string, InvalidDataException> error, bool allowNegativeOne = false)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && (number >= 0 || (allowNegativeOne && number == -1))
            ? number
            : throw error($"'{value}' is not a non-negative integer{(allowNegativeOne ? " (or -1)" : "")}");

    // INI::parseDurationUnsignedInt: milliseconds to original frames (1/30 s), rounded up. The announcer counts original frames at every tick rate.
    private static int Frames(int milliseconds) => checked((int)Math.Ceiling(milliseconds * (double)OriginalFrame.PerSecond / 1000.0));

    private sealed class SideBuilder
    {
        public string? Side { get; set; }
        public List<string> Sounds { get; } = [];
    }

    private sealed class EvaBuilder(string name)
    {
        public string Name { get; } = name;

        // Defaults of EvaCheckInfo::EvaCheckInfo, in original frames (30 s and 5 s).
        public int Priority { get; set; } = 1;
        public int FramesBetweenChecks { get; set; } = 900;
        public int FramesToExpire { get; set; } = 150;
        public SortedDictionary<string, ImmutableArray<string>> SideSounds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public EvaEventDefinition Build()
            => new(Name, Priority, FramesBetweenChecks, FramesToExpire, SideSounds.ToImmutableSortedDictionary(StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// The announcer of <c>Eva::update</c>/<c>processPlayingMessages</c> (<c>Eva.cpp</c>), driven by original frames (1/30 s of game time, never simulation ticks). Game code flags
/// events with <see cref="Request"/>; <see cref="Update"/> turns the flags into queued checks, expires stale ones, and — when no
/// announcement is speaking — picks the highest-priority waiting check and returns the dialog event to play for the local side.
/// Flags are consumed by every update like <c>m_shouldPlay</c>, so a request made while its event is still in cool-down is lost.
/// Differences from the reference: events are visited in file order rather than <c>EvaMessage</c> enum order, and the sound is chosen
/// with the director's seeded generator.
/// </summary>
public sealed class EvaAnnouncer(EvaCatalog catalog, SeededRng rng)
{
    private sealed class Check(EvaEventDefinition definition, long triggeredOn)
    {
        public EvaEventDefinition Definition { get; } = definition;
        public long TriggeredOn { get; } = triggeredOn;
        public long NextCheck { get; set; } = triggeredOn + definition.FramesBetweenChecks;
        public bool Played { get; set; }
    }

    private readonly List<Check> _checks = [];
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedDictionary<string, int> _unknown = new(StringComparer.OrdinalIgnoreCase);

    private bool _enabled = true;

    /// <summary>
    /// <c>Eva::setEvaEnabled</c> (<c>EVA_SET_ENABLED_DISABLED</c>): setting it, either way, first clears the flagged events; while it is <see langword="false"/> <see cref="Update"/> does
    /// nothing at all (queued checks are neither expired nor played), exactly as <c>Eva::update</c> returns at its first line.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _flags.Clear();
            _enabled = value;
        }
    }

    public long Requested { get; private set; }
    public long Played { get; private set; }
    public long Expired { get; private set; }

    /// <summary>A check won but the event has no sound for the local side (the reference plays nothing and still starts the cool-down).</summary>
    public long SideMissing { get; private set; }

    /// <summary>Requested event names that <c>Eva.ini</c> does not define, with counts.</summary>
    public ImmutableSortedDictionary<string, int> UnknownEvents => _unknown.ToImmutableSortedDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Flags an event for the next update (<c>Eva::setShouldPlay</c>); false and counted when the catalog does not define it.</summary>
    public bool Request(string evaEvent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evaEvent);
        if (catalog.Find(evaEvent) is null)
        {
            _unknown[evaEvent] = _unknown.GetValueOrDefault(evaEvent) + 1;
            return false;
        }
        _flags.Add(evaEvent);
        Requested++;
        return true;
    }

    /// <param name="frame">Original frame of game time (<c>GameClock.OriginalFrames</c>); the reference does nothing before frame 2 so that the initial power of 0 is ignored.</param>
    /// <param name="lowPower">The local player's energy is insufficient (<c>shouldPlayLowPower</c>), evaluated every frame.</param>
    /// <param name="speaking">An earlier announcement is still playing.</param>
    /// <param name="side">The local player's template side (<c>Player::getSide</c>), e.g. <c>America</c>.</param>
    /// <returns>The dialog event to play, or null.</returns>
    public string? Update(long frame, bool lowPower, bool speaking, string side)
    {
        if (!_enabled) return null;
        if (frame < 2)
        {
            _flags.Clear();
            return null;
        }
        foreach (var definition in catalog.Events)
        {
            if (_checks.Any(value => value.Definition == definition)) continue;
            var fires = definition.Name.Equals("LowPower", StringComparison.OrdinalIgnoreCase) ? lowPower : _flags.Contains(definition.Name);
            if (fires) _checks.Add(new Check(definition, frame));
        }
        _flags.Clear();

        _checks.RemoveAll(check =>
        {
            if (check.Played) return check.NextCheck <= frame + 1;
            if (check.TriggeredOn + check.Definition.FramesToExpire > frame) return false;
            Expired++;
            return true;
        });
        if (_checks.Count == 0 || speaking) return null;

        Check? winner = null;
        var highest = 0;
        foreach (var check in _checks)
            if (!check.Played && check.Definition.Priority > highest)
            {
                winner = check;
                highest = check.Definition.Priority;
            }
        if (winner is null) return null;

        winner.Played = true;
        winner.NextCheck = frame + winner.Definition.FramesBetweenChecks;
        if (!winner.Definition.SideSounds.TryGetValue(side, out var sounds) || sounds.IsEmpty)
        {
            SideMissing++;
            return null;
        }
        Played++;
        return sounds[rng.NextInt(0, sounds.Length - 1)];
    }
}
