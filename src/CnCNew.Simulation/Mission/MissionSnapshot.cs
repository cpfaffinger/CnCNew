using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CnCNew.Data.World;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Mission;

/// <summary>
/// Canonical, versioned state of a running <see cref="MissionSession"/>: the <see cref="WorldSnapshot"/> (embedded verbatim), the script engine's state, the
/// session's command sequences and the options the mission was started with — UTF-8 JSON, fixed property order, integers only, no paths or timestamps. A
/// restore rebuilds the world from the snapshot, re-derives the script program from the map, loads the engine state and verifies both hashes; the restored
/// session then continues hash-identically to the one that was saved (<c>Tests.Mission.cs</c>). Anything foreign (other version, other definitions, other
/// terrain, other map script program, a flipped byte) is refused with a <see cref="SnapshotFormatException"/>.
/// </summary>
public static class MissionSnapshot
{
    public const string Format = "cncnew-mission-state";
    public const int Version = 1;

    public static byte[] Write(MissionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var world = WorldSnapshot.Write(session.World);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            var writer = new JsonStateWriter(json);
            writer.BeginObject(null);
            writer.Text("format", Format);
            writer.Int("version", Version);
            writer.Int("tickRate", SimulationTiming.LogicFramesPerSecond);
            writer.Text("programFingerprint", session.Program.Fingerprint);
            writer.Text("definitionFingerprint", session.World.Configuration.DefinitionFingerprint);
            writer.Text("terrainFingerprint", session.World.Configuration.TerrainFingerprint);
            writer.BeginObject("options");
            writer.UInt("worldSeed", session.Options.WorldSeed);
            writer.Int("difficulty", (int)session.Options.Difficulty);
            writer.Int("maxTicks", session.Options.MaxTicks);
            writer.Int("presentationLatencyTicks", session.Options.PresentationLatencyTicks);
            writer.Int("combatIntervalTicks", session.Options.CombatIntervalTicks);
            writer.Int("missionAttempts", session.Options.MissionAttempts);
            writer.EndObject();
            writer.Text("stateHash", session.StateHash().ToString("X16"));
            writer.BeginObject("session");
            session.DescribeSession(writer);
            writer.EndObject();
            writer.BeginObject("script");
            session.Engine.Describe(writer);
            writer.EndObject();
            json.WritePropertyName("world");
            json.WriteRawValue(world, skipInputValidation: true);
            writer.EndObject();
        }
        return stream.ToArray();
    }

    public static string Sha256(ReadOnlySpan<byte> snapshot) => Convert.ToHexString(SHA256.HashData(snapshot));

    /// <summary>Rebuilds a session from a snapshot taken by <see cref="Write"/>; <paramref name="map"/> must be the map the mission was started on.</summary>
    public static MissionSession Restore(ReadOnlySpan<byte> snapshot, TemplateCatalog templates, string expectedDefinitionFingerprint, TerrainMap terrain, MapData map)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(map);
        try
        {
            using var document = JsonDocument.Parse(snapshot.ToArray());
            var root = new JsonStateReader(document.RootElement);
            if (root.Text("format") != Format) throw new SnapshotFormatException($"Not a {Format} document.");
            if (root.Int("version") != Version) throw new SnapshotFormatException($"Unsupported mission state version {root.Int("version")} (expected {Version}).");
            if (root.Int("tickRate") != SimulationTiming.LogicFramesPerSecond) throw new SnapshotFormatException("Tick rate mismatch.");
            if (root.Text("definitionFingerprint") != expectedDefinitionFingerprint)
                throw new SnapshotFormatException("The mission state was written for different definitions (fingerprint mismatch).");
            var program = ScriptProgram.Build(map);
            if (root.Text("programFingerprint") != program.Fingerprint)
                throw new SnapshotFormatException("The mission state belongs to a different map script program (fingerprint mismatch).");

            var options = root.Object("options");
            var difficulty = checked((int)options.Int("difficulty"));
            if (!Enum.IsDefined((ScriptDifficulty)difficulty)) throw new SnapshotFormatException("Unknown difficulty in mission state.");
            var restoredOptions = new MissionOptions(checked((uint)options.UInt("worldSeed")), (ScriptDifficulty)difficulty, options.Int("maxTicks"),
                checked((int)options.Int("presentationLatencyTicks")), checked((int)options.Int("combatIntervalTicks")), checked((int)options.Int("missionAttempts")));

            var worldBytes = Encoding.UTF8.GetBytes(document.RootElement.GetProperty("world").GetRawText());
            var world = WorldSnapshot.Restore(worldBytes, templates, expectedDefinitionFingerprint, terrain);
            var session = new MissionSession(restoredOptions, world, program, map, ImmutableArray<string>.Empty);
            session.LoadSession(root.Object("session"));
            session.Engine.Load(root.Object("script"));
            var recorded = root.Text("stateHash");
            var actual = session.StateHash().ToString("X16");
            if (!string.Equals(recorded, actual, StringComparison.Ordinal))
                throw new SnapshotFormatException($"Mission state integrity check failed: recorded hash {recorded}, restored state hashes to {actual}.");
            return session;
        }
        catch (SnapshotFormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or OverflowException or KeyNotFoundException or FormatException or InvalidOperationException or ArgumentException)
        {
            throw new SnapshotFormatException($"Corrupt or truncated mission state: {exception.Message}", exception);
        }
    }
}
