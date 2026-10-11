using System.Text;
using System.Text.Json;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Skirmish.Original;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;

namespace CnCNew.Game.Save;

/// <summary>What a savegame holds: a mission (<see cref="SaveGame"/>) or a running skirmish (<see cref="SkirmishSaveGame"/>).</summary>
public enum SaveGameKind
{
    Mission,
    Skirmish,
}

/// <summary>Who the local viewer of a saved skirmish was; a matter of this machine, not of the match.</summary>
public enum SkirmishViewer
{
    /// <summary>The first slot is played by the human at this machine.</summary>
    Player,
    /// <summary>The computer plays the first slot, the viewer looks on as that player.</summary>
    AutoPlay,
    /// <summary>Nobody plays; the viewer only watches.</summary>
    Spectator,
}

/// <param name="State">The session state: <see cref="SkirmishMatchSession.Save"/>, or <see cref="ScriptedSkirmishSession.Save"/> when <paramref name="OriginalAi"/> is set.</param>
/// <param name="OriginalAi">The match runs the original AI in a <see cref="ScriptedSkirmishSession"/>.</param>
public sealed record LoadedSkirmishSave(SaveGameHeader Header, byte[] State, bool OriginalAi, SkirmishViewer Viewer);

/// <summary>
/// The savegame of a running skirmish: one UTF-8 JSON document with a fixed property order, like <see cref="SaveGame"/> a wrapper around the simulation's own state - here the
/// session state of the skirmish (match description, world, every computer player, the recording so far). It has a format of its own (<see cref="Format"/>,
/// <see cref="Version"/>): a mission save and a skirmish save cannot be taken for each other. A file of another version, tick rate or definitions, or with a flipped byte, is
/// refused with a message fit to show.
/// </summary>
public static class SkirmishSaveGame
{
    public const string Format = "cncnew-skirmish-savegame";
    public const int Version = 1;

    public static byte[] Write(string title, string mapPath, long tick, int tickRate, string definitionFingerprint, string stateHash, byte[] state, bool originalAi, SkirmishViewer viewer)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            var writer = new JsonStateWriter(json);
            writer.BeginObject(null);
            writer.Text("format", Format);
            writer.Int("version", Version);
            writer.Text("title", title);
            writer.Text("mapPath", mapPath);
            writer.Int("tick", tick);
            writer.Int("tickRate", tickRate);
            writer.Text("definitionFingerprint", definitionFingerprint);
            writer.Text("stateHash", stateHash);
            writer.Text("session", originalAi ? "original" : "builtin");
            writer.Int("viewer", (int)viewer);
            writer.Text("stateSha256", MissionSnapshotSha(state));
            json.WritePropertyName("state");
            json.WriteRawValue(state, skipInputValidation: true);
            writer.EndObject();
        }
        return stream.ToArray();
    }

    /// <summary>Whether the bytes claim to be a skirmish savegame (of any version); cheap, for telling the two kinds apart.</summary>
    public static bool IsSkirmishSave(ReadOnlySpan<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray());
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String
                && format.GetString() == Format;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static SaveGameHeader ReadHeader(ReadOnlySpan<byte> bytes)
    {
        using var document = Parse(bytes);
        return Header(new JsonStateReader(document.RootElement));
    }

    /// <summary>Checks the file and returns its session state; restoring the session needs the map, which the caller loads (<c>LiveMatch.Restore</c>).</summary>
    public static LoadedSkirmishSave Load(ReadOnlySpan<byte> bytes, int tickRate, string definitionFingerprint)
    {
        try
        {
            using var document = Parse(bytes);
            var root = new JsonStateReader(document.RootElement);
            var header = Header(root);
            if (header.TickRate != tickRate) throw new SaveGameException($"This savegame was written at {header.TickRate} Hz; this session runs at {tickRate} Hz. It cannot be loaded.");
            if (!string.Equals(header.DefinitionFingerprint, definitionFingerprint, StringComparison.OrdinalIgnoreCase))
                throw new SaveGameException("This savegame was written with different game data (definition fingerprint mismatch) and cannot be loaded.");
            var session = root.Text("session");
            if (session is not ("builtin" or "original")) throw new SaveGameException($"Unknown session kind '{session}' in the savegame.");
            var viewer = checked((int)root.Int("viewer"));
            if (!Enum.IsDefined((SkirmishViewer)viewer)) throw new SaveGameException("Unknown viewer in the savegame.");
            var state = Encoding.UTF8.GetBytes(document.RootElement.GetProperty("state").GetRawText());
            if (!string.Equals(root.Text("stateSha256"), MissionSnapshotSha(state), StringComparison.Ordinal)) throw new SaveGameException("The savegame is damaged (its checksum does not match).");
            return new LoadedSkirmishSave(header, state, session == "original", (SkirmishViewer)viewer);
        }
        catch (SaveGameException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or KeyNotFoundException or FormatException or OverflowException or InvalidOperationException)
        {
            throw new SaveGameException($"The savegame is damaged or incomplete: {exception.Message}", exception);
        }
    }

    private static string MissionSnapshotSha(byte[] state) => CnCNew.Simulation.Mission.MissionSnapshot.Sha256(state);

    private static JsonDocument Parse(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var document = JsonDocument.Parse(bytes.ToArray());
            var root = new JsonStateReader(document.RootElement);
            if (!root.Has("format") || root.Text("format") != Format) { document.Dispose(); throw new SaveGameException("Not a CnCNew skirmish savegame."); }
            if (root.Int("version") != Version) { var found = root.Int("version"); document.Dispose(); throw new SaveGameException($"Unsupported skirmish savegame version {found} (this game reads version {Version})."); }
            return document;
        }
        catch (JsonException exception)
        {
            throw new SaveGameException($"The savegame is damaged or truncated: {exception.Message}", exception);
        }
    }

    private static SaveGameHeader Header(JsonStateReader root)
    {
        var tickRate = root.Int("tickRate");
        if (!SimulationRate.IsSupported(tickRate)) throw new SaveGameException($"Unsupported tick rate {tickRate} Hz in the savegame.");
        return new SaveGameHeader(root.Text("title"), root.Text("mapPath"), string.Empty, string.Empty, ScriptDifficulty.Normal, root.Int("tick"), (int)tickRate,
            root.Text("definitionFingerprint"), root.Text("stateHash")) { Kind = SaveGameKind.Skirmish };
    }
}
