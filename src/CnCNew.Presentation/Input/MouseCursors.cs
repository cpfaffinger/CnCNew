using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Images;

namespace CnCNew.Presentation.Input;

/// <summary>
/// One <c>MouseCursor</c> block of <c>Mouse.ini</c> (<c>Mouse.cpp</c> <c>TheMouseCursorFieldParseTable</c>, defaults of <c>CursorInfo::CursorInfo</c>).
/// <see cref="Texture"/> names the cursor file; <see cref="Directions"/> &gt; 1 means one file per direction (<c>Texture0..n-1</c>).
/// <see cref="HotSpot"/>, <see cref="Frames"/> and <see cref="FramesPerSecond"/> belong to the original's textured cursor modes; the Windows cursor
/// mode this client reproduces takes hot spot and timing from the cursor file itself.
/// </summary>
public sealed record MouseCursorDefinition(string Name, CursorKind Kind, string Image, string Texture, int Directions, int Frames, float FramesPerSecond, (int X, int Y)? HotSpot)
{
    /// <summary>
    /// The runtime files of this cursor in direction order (<c>Win32Mouse::initCursorResources</c>: <c>data\cursors\%s.ANI</c>, or
    /// <c>%s%d.ANI</c> for a directional cursor).
    /// </summary>
    public ImmutableArray<string> Files => Directions > 1
        ? [..Enumerable.Range(0, Directions).Select(direction => $"Data/Cursors/{Texture}{direction.ToString(CultureInfo.InvariantCulture)}.ani")]
        : [$"Data/Cursors/{Texture}.ani"];
}

public enum MouseCursorIssueKind
{
    /// <summary>A <c>MouseCursor</c> block whose name is not one of the engine's cursor names (<c>CursorININames</c>): the original ignores it.</summary>
    UnknownCursorName,
    UnknownField,
    BadValue,
    /// <summary>A block without <c>Texture</c>: the original would load nothing for it.</summary>
    MissingTexture,
}

public sealed record MouseCursorIssue(MouseCursorIssueKind Kind, string Cursor, string Detail);

/// <summary>
/// The cursor table of <c>Mouse.ini</c>. The compiler keeps <c>MouseCursor</c> blocks as compatibility definitions (id <c>mousecursor/NAME</c>)
/// with their field text; this reads them like <c>Mouse::parseIni</c>. Nothing is guessed: a block that cannot be used is an issue and has no
/// definition, and a <see cref="CursorKind"/> without a block is listed in <see cref="WithoutDefinition"/>.
/// </summary>
public sealed class MouseCursorCatalog
{
    private static readonly string[] IgnoredFields = ["CursorText", "CursorTextColor", "CursorTextDropColor", "W3DModel", "W3DAnim", "W3DScale", "Loop"];
    private readonly ImmutableDictionary<CursorKind, MouseCursorDefinition> _byKind;

    private MouseCursorCatalog(ImmutableArray<MouseCursorDefinition> definitions, ImmutableArray<MouseCursorIssue> issues)
    {
        Definitions = definitions;
        Issues = issues;
        _byKind = definitions.ToImmutableDictionary(definition => definition.Kind);
        WithoutDefinition = [..Enum.GetValues<CursorKind>().Where(kind => !_byKind.ContainsKey(kind))];
    }

    /// <summary>In <see cref="CursorKind"/> order.</summary>
    public ImmutableArray<MouseCursorDefinition> Definitions { get; }
    public ImmutableArray<MouseCursorIssue> Issues { get; }

    /// <summary>Engine cursors the data does not define. The retail data leaves out <c>Build</c> and <c>InvalidBuild</c>: the original then shows no cursor while placing a building.</summary>
    public ImmutableArray<CursorKind> WithoutDefinition { get; }

    public bool TryGet(CursorKind kind, out MouseCursorDefinition definition) => _byKind.TryGetValue(kind, out definition!);

    public static MouseCursorCatalog FromGraph(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var issues = ImmutableArray.CreateBuilder<MouseCursorIssue>();
        var definitions = new SortedDictionary<CursorKind, MouseCursorDefinition>();
        foreach (var block in graph.Definitions.OfType<CompatibilityDefinition>()
                     .Where(definition => definition.Id.Name.StartsWith("mousecursor/", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(definition => definition.Id.Name, StringComparer.Ordinal))
        {
            var name = block.OriginalName;
            if (!CursorNames.TryParse(name, out var kind))
            {
                issues.Add(new MouseCursorIssue(MouseCursorIssueKind.UnknownCursorName, name, "not one of the engine's cursor names"));
                continue;
            }
            string image = string.Empty, texture = string.Empty;
            var (directions, frames, fps) = (1, 1, 20f);
            (int X, int Y)? hotSpot = null;
            var valid = true;
            foreach (var field in block.Fields)
            {
                var value = field.Value.OriginalText.Trim();
                switch (field.Name.ToUpperInvariant())
                {
                    case "IMAGE": image = value; break;
                    case "TEXTURE": texture = value; break;
                    case "DIRECTIONS":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out directions) && directions is >= 1 and <= 64) break;
                        Bad(field.Name, value);
                        break;
                    case "FRAMES":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out frames) && frames >= 1) break;
                        Bad(field.Name, value);
                        break;
                    case "FPS":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out fps) && fps > 0f) break;
                        Bad(field.Name, value);
                        break;
                    case "HOTSPOT":
                        if (TryParseCoord(value, out var spot)) hotSpot = spot;
                        else Bad(field.Name, value);
                        break;
                    default:
                        if (!IgnoredFields.Contains(field.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            issues.Add(new MouseCursorIssue(MouseCursorIssueKind.UnknownField, name, field.Name));
                            valid = false;
                        }
                        break;
                }
            }
            if (valid && texture.Length == 0)
            {
                issues.Add(new MouseCursorIssue(MouseCursorIssueKind.MissingTexture, name, "no Texture field"));
                valid = false;
            }
            // The last block of a name wins, as Mouse::parseIni overwrites the cursor's record.
            if (valid) definitions[kind] = new MouseCursorDefinition(name, kind, image, texture, directions, frames, fps, hotSpot);

            void Bad(string field, string text)
            {
                issues.Add(new MouseCursorIssue(MouseCursorIssueKind.BadValue, name, $"{field} = {text}"));
                valid = false;
            }
        }
        return new MouseCursorCatalog([..definitions.Values], issues.ToImmutable());
    }

    /// <summary><c>INI::parseICoord2D</c>: <c>X:16 Y:16</c>.</summary>
    private static bool TryParseCoord(string text, out (int X, int Y) value)
    {
        value = default;
        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0].StartsWith("X:", StringComparison.OrdinalIgnoreCase) && parts[1].StartsWith("Y:", StringComparison.OrdinalIgnoreCase)
               && int.TryParse(parts[0].AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out value.X)
               && int.TryParse(parts[1].AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out value.Y);
    }
}

/// <summary>
/// The cursor the client should show: the kind, and for a directional cursor the screen offset it points along (x right, y down; zero when
/// the cursor is not directional or nothing moves).
/// </summary>
public readonly record struct CursorState(CursorKind Kind, Vector2 Direction)
{
    public static readonly CursorState Arrow = new(CursorKind.Arrow, Vector2.Zero);
}

/// <summary>One decoded cursor file.</summary>
public sealed record LoadedCursor(string File, AniCursor Animation);

/// <summary>What is on screen for a <see cref="CursorState"/> at a point in time.</summary>
public readonly record struct CursorFrame(CursorKind Kind, LoadedCursor Cursor, int FrameIndex)
{
    public CursorImage Image => Cursor.Animation.Frames[FrameIndex];
}

/// <summary>
/// The decoded cursors of a <see cref="MouseCursorCatalog"/>, loaded through the caller's file access (the runtime content of the game layer, a
/// dictionary in tests). A file that is missing or cannot be decoded is listed, the cursors that need it have no picture, and
/// <see cref="Resolve"/> says so; no other cursor stands in.
/// </summary>
public sealed class CursorSet
{
    private readonly ImmutableDictionary<CursorKind, ImmutableArray<LoadedCursor?>> _byKind;

    private CursorSet(MouseCursorCatalog catalog, ImmutableDictionary<CursorKind, ImmutableArray<LoadedCursor?>> byKind, ImmutableArray<LoadedCursor> files,
        ImmutableArray<string> missing, ImmutableArray<string> undecodable)
    {
        Catalog = catalog;
        _byKind = byKind;
        Files = files;
        MissingFiles = missing;
        UndecodableFiles = undecodable;
    }

    public MouseCursorCatalog Catalog { get; }

    /// <summary>Every distinct decoded file, in path order.</summary>
    public ImmutableArray<LoadedCursor> Files { get; }
    public ImmutableArray<string> MissingFiles { get; }

    /// <summary>"path (reason)" per file that exists but could not be decoded.</summary>
    public ImmutableArray<string> UndecodableFiles { get; }

    public int FrameCount => Files.Sum(file => file.Animation.Frames.Length);

    /// <summary>Pixels of the decoded frames that ask to invert the screen, which a blended cursor shows as plain colour instead.</summary>
    public int InvertedPixels => Files.Sum(file => file.Animation.Frames.Sum(frame => frame.InvertedPixels));

    public static CursorSet Load(MouseCursorCatalog catalog, Func<string, bool> exists, Func<string, Stream> open)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(open);
        var loaded = new SortedDictionary<string, LoadedCursor?>(StringComparer.OrdinalIgnoreCase);
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var undecodable = new SortedSet<string>(StringComparer.Ordinal);
        var byKind = ImmutableDictionary.CreateBuilder<CursorKind, ImmutableArray<LoadedCursor?>>();
        foreach (var definition in catalog.Definitions)
        {
            var directions = ImmutableArray.CreateBuilder<LoadedCursor?>();
            foreach (var file in definition.Files)
            {
                if (!loaded.TryGetValue(file, out var cursor))
                {
                    if (!exists(file)) missing.Add(file);
                    else
                    {
                        try
                        {
                            using var stream = open(file);
                            using var memory = new MemoryStream();
                            stream.CopyTo(memory);
                            cursor = new LoadedCursor(file, CursorReader.ReadAni(memory.GetBuffer().AsSpan(0, (int)memory.Length)));
                        }
                        catch (InvalidDataException exception)
                        {
                            undecodable.Add($"{file} ({exception.Message})");
                        }
                    }
                    loaded.Add(file, cursor);
                }
                directions.Add(cursor);
            }
            byKind[definition.Kind] = directions.ToImmutable();
        }
        return new CursorSet(catalog, byKind.ToImmutable(), [..loaded.Values.OfType<LoadedCursor>()], [..missing], [..undecodable]);
    }

    /// <summary>
    /// The picture for <paramref name="state"/>, <paramref name="elapsedMilliseconds"/> after that cursor became current. False when the kind
    /// has no <c>MouseCursor</c> block (the original shows no cursor then) or its file is missing or undecodable.
    /// </summary>
    public bool Resolve(CursorState state, long elapsedMilliseconds, out CursorFrame frame)
    {
        frame = default;
        if (!_byKind.TryGetValue(state.Kind, out var directions)) return false;
        if (directions[DirectionIndex(state.Direction, directions.Length)] is not { } cursor) return false;
        frame = new CursorFrame(state.Kind, cursor, cursor.Animation.FrameIndexAt(elapsedMilliseconds));
        return true;
    }

    /// <summary>Kinds that have a block but no usable picture in at least one direction.</summary>
    public ImmutableArray<CursorKind> Unusable => [.._byKind.Where(pair => pair.Value.Any(cursor => cursor is null)).Select(pair => pair.Key).Order()];

    /// <summary>
    /// <c>W3DMouse::setCursorDirection</c>: the direction frame of a directional cursor from the scroll offset in screen space (y down). Frame 0
    /// points right, the frames continue clockwise on screen; no offset is frame 0.
    /// </summary>
    public static int DirectionIndex(Vector2 screenOffset, int directions)
    {
        if (directions <= 1 || screenOffset == Vector2.Zero) return 0;
        var theta = MathF.Atan2(screenOffset.Y, screenOffset.X);
        theta = (theta + MathF.Tau) % MathF.Tau;
        var frame = (int)(theta / (MathF.Tau / directions) + 0.5f);
        return frame >= directions ? 0 : frame;
    }

    /// <summary>One line for the console report of a run.</summary>
    public string Report()
        => string.Create(CultureInfo.InvariantCulture,
            $"{Catalog.Definitions.Length} definitions, issues [{string.Join(", ", Catalog.Issues.Select(issue => $"{issue.Cursor}: {issue.Kind} {issue.Detail}"))}], {Files.Length} files loaded, {FrameCount} frames, {InvertedPixels} inverting pixels, missing files [{string.Join(", ", MissingFiles)}], undecodable [{string.Join(", ", UndecodableFiles)}], engine cursors without a definition [{string.Join(", ", Catalog.WithoutDefinition)}]");
}

/// <summary>
/// The cursor clock: remembers which cursor is current and since when, so an animated cursor restarts when it changes (as a fresh
/// <c>SetCursor</c> does) and keeps running while it stays.
/// </summary>
public sealed class CursorAnimator(CursorSet cursors)
{
    private CursorState _state = CursorState.Arrow;
    private int _direction;
    private long _since;
    private bool _started;

    public CursorSet Cursors { get; } = cursors;
    public CursorState State => _state;

    /// <summary>How often a cursor was asked for that has no picture, by kind; a caller reports it instead of drawing something else.</summary>
    public SortedDictionary<CursorKind, int> Unresolved { get; } = [];

    /// <summary>The frame to draw at <paramref name="nowMilliseconds"/> for <paramref name="state"/>, or false when that cursor has no picture.</summary>
    public bool Update(CursorState state, long nowMilliseconds, out CursorFrame frame)
    {
        var direction = Cursors.Catalog.TryGet(state.Kind, out var definition) ? CursorSet.DirectionIndex(state.Direction, definition.Directions) : 0;
        if (!_started || state.Kind != _state.Kind || direction != _direction)
        {
            _since = nowMilliseconds;
            _started = true;
        }
        _state = state;
        _direction = direction;
        if (Cursors.Resolve(state, nowMilliseconds - _since, out frame)) return true;
        // Build and InvalidBuild have no block in the retail data: no cursor is the original behaviour, not a fault.
        if (definition is not null) Unresolved[state.Kind] = Unresolved.GetValueOrDefault(state.Kind) + 1;
        return false;
    }
}
