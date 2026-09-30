using System.Collections.Immutable;

namespace CnCNew.Game.Save;

/// <summary>One savegame file in a store: its slot name and either its header or the reason it cannot be read.</summary>
public sealed record SaveSlot(string Name, long Bytes, SaveGameHeader? Header, string? Problem);

/// <summary>
/// Savegame files in one directory (<c>&lt;slot&gt;.cncsave</c>). Writing is atomic (temporary file, then move), so an interrupted save never replaces a good
/// one; listing never throws for a bad file, it reports the problem per slot. The store knows nothing about what is in a file beyond the header.
/// </summary>
public sealed class SaveGameStore
{
    public const string Extension = ".cncsave";
    private readonly string _directory;

    public SaveGameStore(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        _directory = directory;
    }

    public string PathOf(string slot)
    {
        if (slot.Length == 0 || slot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || slot.Contains("..", StringComparison.Ordinal)) throw new ArgumentException($"'{slot}' is not a valid save slot name.", nameof(slot));
        return Path.Combine(_directory, slot + Extension);
    }

    public void Save(string slot, ReadOnlySpan<byte> bytes)
    {
        var path = PathOf(slot);
        Directory.CreateDirectory(_directory);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes.ToArray());
        File.Move(temporary, path, overwrite: true);
    }

    public byte[] Read(string slot)
    {
        var path = PathOf(slot);
        if (!File.Exists(path)) throw new SaveGameException($"There is no savegame '{slot}'.");
        return File.ReadAllBytes(path);
    }

    public bool Delete(string slot)
    {
        var path = PathOf(slot);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>Every slot in name order (ordinal), damaged ones included with their problem.</summary>
    public ImmutableArray<SaveSlot> List()
    {
        if (!Directory.Exists(_directory)) return [];
        var slots = ImmutableArray.CreateBuilder<SaveSlot>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*" + Extension).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var bytes = File.ReadAllBytes(file);
            try { slots.Add(new SaveSlot(name, bytes.Length, SaveGame.ReadHeader(bytes), null)); }
            catch (SaveGameException exception) { slots.Add(new SaveSlot(name, bytes.Length, null, exception.Message)); }
        }
        return slots.ToImmutable();
    }
}
