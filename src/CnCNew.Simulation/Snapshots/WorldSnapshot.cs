using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Snapshots;

public sealed class SnapshotFormatException(string message, Exception? inner = null) : FormatException(message, inner);

/// <summary>
/// Canonical, versioned world snapshot: UTF-8 JSON with a fixed property order, integers only (fixed-point values as raw
/// integers), no paths, no timestamps, every collection sorted by id. <see cref="Sha256"/> identifies a snapshot.
/// The header carries the definition fingerprint and the world hash; <see cref="Restore"/> rejects truncated, corrupted,
/// version-foreign or definition-foreign snapshots. Restore is internal-contract minimal: it serves the replay harness.
/// Version 10 (phase 22): the raw integers carry 24 fraction bits instead of 16; a version 9 snapshot is rejected, never rescaled.
/// </summary>
public static class WorldSnapshot
{
    public const string Format = "cncnew-world-snapshot";
    public const int Version = 11;

    public static byte[] Write(SimulationWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var hash = world.ComputeFullHash();
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            var writer = new JsonStateWriter(json);
            writer.BeginObject(null);
            writer.Text("format", Format);
            writer.Int("version", Version);
            writer.Int("tickRate", world.Rate.TicksPerSecond);
            writer.Text("definitionFingerprint", world.Configuration.DefinitionFingerprint);
            writer.Text("fixture", world.Configuration.FixtureId);
            writer.Text("terrainFingerprint", world.Configuration.TerrainFingerprint);
            writer.UInt("worldId", world.Configuration.Id.Value);
            writer.UInt("seed", world.Configuration.Seed);
            writer.Text("stateHash", hash.ToString("X16", System.Globalization.CultureInfo.InvariantCulture));
            writer.BeginObject("state");
            world.DescribeFull(writer);
            writer.EndObject();
            writer.BeginObject("presentation");
            world.Streams.DescribePresentation(writer);
            writer.EndObject();
            writer.EndObject();
        }
        return stream.ToArray();
    }

    public static string Sha256(ReadOnlySpan<byte> snapshot) => Convert.ToHexString(SHA256.HashData(snapshot));

    /// <summary>
    /// The tick rate a snapshot was written at. An unsupported rate is refused; a supported one must be the rate <paramref name="templates"/> was built
    /// for, because the state holds absolute ticks and per-tick values of that rate.
    /// </summary>
    internal static SimulationRate ReadRate(JsonStateReader root, TemplateCatalog templates)
    {
        var stored = root.Int("tickRate");
        if (!SimulationRate.IsSupported(stored)) throw new SnapshotFormatException($"Unsupported tick rate {stored} Hz (supported: 30, 60, 120).");
        var rate = SimulationRate.FromTicksPerSecond(stored);
        if (rate != templates.Rate) throw new SnapshotFormatException($"The snapshot was written at {rate}; the catalog was built for {templates.Rate}.");
        return rate;
    }

    /// <summary>
    /// Rebuilds a world from a snapshot, at the tick rate the snapshot was written at (<paramref name="templates"/> must be a catalog of that rate).
    /// Throws <see cref="SnapshotFormatException"/> for any defect.
    /// </summary>
    public static SimulationWorld Restore(ReadOnlySpan<byte> snapshot, TemplateCatalog templates, string expectedDefinitionFingerprint, TerrainMap? terrain = null)
    {
        ArgumentNullException.ThrowIfNull(templates);
        try
        {
            using var document = JsonDocument.Parse(snapshot.ToArray());
            var root = new JsonStateReader(document.RootElement);
            if (root.Text("format") != Format) throw new SnapshotFormatException($"Not a {Format} document.");
            if (root.Int("version") != Version) throw new SnapshotFormatException($"Unsupported snapshot version {root.Int("version")} (expected {Version}).");
            var rate = ReadRate(root, templates);
            if (root.Text("definitionFingerprint") != expectedDefinitionFingerprint)
                throw new SnapshotFormatException("The snapshot was written for different definitions (fingerprint mismatch).");

            var state = root.Object("state");
            var players = ImmutableArray.CreateBuilder<PlayerConfiguration>();
            foreach (var player in state.Array("players"))
            {
                var id = checked((int)player.Int("id"));
                if (id != 0) players.Add(new PlayerConfiguration(new PlayerId(id), player.Text("name"), Team: checked((int)player.Int("team")), IsComputer: player.Bool("computer")));
            }
            var configuration = new WorldConfiguration(new WorldId(checked((uint)root.UInt("worldId"))), rate, checked((uint)root.UInt("seed")),
                players.ToImmutable(), root.Text("fixture"), root.Text("definitionFingerprint"), root.Text("terrainFingerprint"));
            if (!string.Equals(configuration.TerrainFingerprint, terrain?.Fingerprint ?? string.Empty, StringComparison.Ordinal))
                throw new SnapshotFormatException("The snapshot was written for a different terrain (fingerprint mismatch).");
            var world = new SimulationWorld(configuration, templates, terrain);
            world.LoadFull(state);
            world.Streams.LoadPresentation(root.Object("presentation"));
            var recorded = root.Text("stateHash");
            var actual = world.ComputeFullHash().ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
            if (!string.Equals(recorded, actual, StringComparison.Ordinal))
                throw new SnapshotFormatException($"Snapshot integrity check failed: recorded hash {recorded}, restored state hashes to {actual}.");
            return world;
        }
        catch (SnapshotFormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or OverflowException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            throw new SnapshotFormatException($"Corrupt or truncated snapshot: {exception.Message}", exception);
        }
    }
}
