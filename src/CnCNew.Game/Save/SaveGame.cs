using System.Text;
using System.Text.Json;
using CnCNew.Game.Campaign;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.State;

namespace CnCNew.Game.Save;

/// <summary>A savegame that cannot be read or does not fit this build; the message says why (the UI shows it instead of loading something wrong).</summary>
public sealed class SaveGameException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>What a savegame says about itself, readable without restoring the simulation (the load screen lists these).</summary>
public sealed record SaveGameHeader(
    string Title, string MapPath, string Campaign, string Mission, ScriptDifficulty Difficulty, long Tick, string DefinitionFingerprint, string StateHash);

/// <summary>A restored savegame: the running mission and, for a campaign mission, the progress it was saved with.</summary>
public sealed record LoadedSave(SaveGameHeader Header, MissionRun Run, CampaignProgress? Progress);

/// <summary>
/// The savegame file: one UTF-8 JSON document with a fixed property order, integers only, no absolute paths and no timestamps — the same stream for the
/// same state, so two saves of one tick are byte-identical. It wraps a <see cref="MissionSnapshot"/> (world, script state, session counters; the
/// snapshot is the source of truth, never a replay) with what the world cannot know: the map to rebuild the terrain and script program from, the
/// campaign progress and a title. <see cref="Version"/> is this format's own; a file of another version, of other game definitions (fingerprint) or
/// with a flipped byte (checksum over the state, plus the simulation's own state hash) is refused with a <see cref="SaveGameException"/>.
/// <para>Scope: missions (campaign or single map with scripts). A skirmish keeps AI state outside the world that this format does not carry.</para>
/// </summary>
public static class SaveGame
{
    public const string Format = "cncnew-savegame";
    public const int Version = 1;

    public static byte[] Write(MissionRun run, CampaignProgress? progress, string title)
    {
        ArgumentNullException.ThrowIfNull(run);
        var state = MissionSnapshot.Write(run.Session);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            var writer = new JsonStateWriter(json);
            writer.BeginObject(null);
            writer.Text("format", Format);
            writer.Int("version", Version);
            writer.Text("title", title);
            writer.Text("mapPath", run.MapPath);
            writer.Text("campaign", progress?.Campaign ?? string.Empty);
            writer.Text("mission", run.Mission?.Name ?? string.Empty);
            writer.Int("difficulty", (int)run.Session.Options.Difficulty);
            writer.Int("tick", run.Session.World.Tick);
            writer.Text("definitionFingerprint", run.Session.World.Configuration.DefinitionFingerprint);
            writer.Text("stateHash", run.Session.StateHash().ToString("X16"));
            writer.Text("stateSha256", MissionSnapshot.Sha256(state));
            writer.BeginObject("progress");
            if (progress is not null) progress.Describe(writer);
            writer.EndObject();
            json.WritePropertyName("state");
            json.WriteRawValue(state, skipInputValidation: true);
            writer.EndObject();
        }
        return stream.ToArray();
    }

    /// <summary>Reads only the header; throws <see cref="SaveGameException"/> for anything that is not a savegame of this format version.</summary>
    public static SaveGameHeader ReadHeader(ReadOnlySpan<byte> bytes)
    {
        using var document = Parse(bytes);
        return Header(new JsonStateReader(document.RootElement));
    }

    /// <summary>Restores the mission; <paramref name="campaigns"/> resolves the campaign mission the save belongs to (may be <see langword="null"/> for a save of a single map).</summary>
    public static LoadedSave Load(ReadOnlySpan<byte> bytes, MissionContext context, CampaignCatalog? campaigns = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            using var document = Parse(bytes);
            var root = new JsonStateReader(document.RootElement);
            var header = Header(root);
            if (header.DefinitionFingerprint != context.Fingerprint)
                throw new SaveGameException("This savegame was written with different game data (definition fingerprint mismatch) and cannot be loaded.");
            CampaignProgress? progress = null;
            CampaignMission? mission = null;
            if (header.Campaign.Length > 0)
            {
                progress = CampaignProgress.Load(root.Object("progress"));
                if (campaigns?.Find(header.Campaign) is { } campaign) mission = campaign.FindMission(header.Mission);
                else if (campaigns is not null) throw new SaveGameException($"The savegame belongs to campaign '{header.Campaign}', which this game does not have.");
            }
            var stateBytes = Encoding.UTF8.GetBytes(document.RootElement.GetProperty("state").GetRawText());
            // The simulation hash does not cover presentation-only state (audio/fx random streams); the checksum over the embedded state covers every byte.
            if (!string.Equals(root.Text("stateSha256"), MissionSnapshot.Sha256(stateBytes), StringComparison.Ordinal))
                throw new SaveGameException("The savegame is damaged (its checksum does not match).");
            var run = MissionRun.Restore(context, header.MapPath, stateBytes, mission);
            if (run.Session.StateHash().ToString("X16") != header.StateHash)
                throw new SaveGameException("The savegame's state does not match its header (the file is damaged).");
            return new LoadedSave(header, run, progress);
        }
        catch (SaveGameException)
        {
            throw;
        }
        catch (SnapshotFormatException exception)
        {
            throw new SaveGameException($"The savegame cannot be restored: {exception.Message}", exception);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or KeyNotFoundException or FormatException or OverflowException or InvalidOperationException or FileNotFoundException)
        {
            throw new SaveGameException($"The savegame is damaged or incomplete: {exception.Message}", exception);
        }
    }

    private static JsonDocument Parse(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var document = JsonDocument.Parse(bytes.ToArray());
            var root = new JsonStateReader(document.RootElement);
            if (!root.Has("format") || root.Text("format") != Format) { document.Dispose(); throw new SaveGameException("Not a CnCNew savegame."); }
            if (root.Int("version") != Version) { var found = root.Int("version"); document.Dispose(); throw new SaveGameException($"Unsupported savegame version {found} (this game reads version {Version})."); }
            return document;
        }
        catch (JsonException exception)
        {
            throw new SaveGameException($"The savegame is damaged or truncated: {exception.Message}", exception);
        }
    }

    private static SaveGameHeader Header(JsonStateReader root)
    {
        var difficulty = checked((int)root.Int("difficulty"));
        if (!Enum.IsDefined((ScriptDifficulty)difficulty)) throw new SaveGameException("Unknown difficulty in the savegame.");
        return new SaveGameHeader(root.Text("title"), root.Text("mapPath"), root.Text("campaign"), root.Text("mission"), (ScriptDifficulty)difficulty, root.Int("tick"),
            root.Text("definitionFingerprint"), root.Text("stateHash"));
    }
}
