using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace CnCNew.Archives;

public sealed class BigArchive : IDisposable
{
    private const int HeaderSize = 16;
    private const int MaxPathBytes = 32 * 1024;
    private readonly FileStream _stream;

    private BigArchive(string path, FileStream stream, IReadOnlyList<BigArchiveEntry> entries)
    {
        Path = path;
        _stream = stream;
        Entries = entries;
    }

    public string Path { get; }
    public IReadOnlyList<BigArchiveEntry> Entries { get; }

    public static BigArchive Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = System.IO.Path.GetFullPath(path);
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var entries = ReadDirectory(stream, fullPath);
            return new BigArchive(fullPath, stream, new ReadOnlyCollection<BigArchiveEntry>(entries));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public Stream OpenEntry(BigArchiveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!Entries.Contains(entry))
        {
            throw new ArgumentException("The entry does not belong to this archive.", nameof(entry));
        }

        return new ArchiveEntryStream(Path, entry.Offset, entry.Length);
    }

    public async Task ExtractToAsync(string destinationDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        var destinationRoot = System.IO.Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destinationRoot);

        foreach (var entry in Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = GetSafeDestinationPath(destinationRoot, entry.Path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationPath)!);

            await using var input = OpenEntry(entry);
            await using var output = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 128,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    public void Dispose() => _stream.Dispose();

    private static List<BigArchiveEntry> ReadDirectory(Stream stream, string archivePath)
    {
        if (stream.Length < HeaderSize)
        {
            throw new InvalidDataException($"'{archivePath}' is too small to be a BIG archive.");
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("BIGF"u8) && !header[..4].SequenceEqual("BIG4"u8))
        {
            throw new InvalidDataException($"'{archivePath}' has an unsupported BIG signature.");
        }

        var declaredSizeLittleEndian = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
        var declaredSizeBigEndian = BinaryPrimitives.ReadUInt32BigEndian(header[4..8]);
        if (declaredSizeLittleEndian != stream.Length && declaredSizeBigEndian != stream.Length)
        {
            throw new InvalidDataException(
                $"'{archivePath}' declares size {declaredSizeLittleEndian}/{declaredSizeBigEndian}, actual size is {stream.Length}.");
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(header[8..12]);
        var directoryEnd = BinaryPrimitives.ReadUInt32BigEndian(header[12..16]);
        if (directoryEnd < HeaderSize || directoryEnd > stream.Length)
        {
            throw new InvalidDataException($"'{archivePath}' has an invalid directory boundary.");
        }

        if (count > int.MaxValue)
        {
            throw new InvalidDataException($"'{archivePath}' contains too many entries.");
        }

        var entries = new List<BigArchiveEntry>((int)count);
        Span<byte> metadata = stackalloc byte[8];
        for (var index = 0U; index < count; index++)
        {
            stream.ReadExactly(metadata);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(metadata[..4]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(metadata[4..]);
            var path = ReadNullTerminatedPath(stream, directoryEnd, archivePath);

            if ((ulong)offset + length > (ulong)stream.Length)
            {
                throw new InvalidDataException($"Entry '{path}' points outside '{archivePath}'.");
            }

            entries.Add(new BigArchiveEntry(NormalizeArchivePath(path), offset, length));
        }

        return entries;
    }

    private static string ReadNullTerminatedPath(Stream stream, uint directoryEnd, string archivePath)
    {
        using var bytes = new MemoryStream();
        while (stream.Position < directoryEnd && bytes.Length < MaxPathBytes)
        {
            var value = stream.ReadByte();
            if (value < 0)
            {
                break;
            }

            if (value == 0)
            {
                return Encoding.Latin1.GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length));
            }

            bytes.WriteByte((byte)value);
        }

        throw new InvalidDataException($"'{archivePath}' contains an unterminated or oversized entry path.");
    }

    private static string NormalizeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException("A BIG archive entry has an empty path.");
        }

        return path.Replace('\\', '/');
    }

    private static string GetSafeDestinationPath(string destinationRoot, string archivePath)
    {
        var relativePath = string.Join(
            System.IO.Path.DirectorySeparatorChar,
            archivePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(EscapeWindowsSegment));
        if (System.IO.Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Archive entry '{archivePath}' is rooted.");
        }

        var destinationPath = System.IO.Path.GetFullPath(relativePath, destinationRoot);
        var rootWithSeparator = destinationRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (!destinationPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Archive entry '{archivePath}' escapes the destination directory.");
        }

        return destinationPath;
    }

    private static string EscapeWindowsSegment(string segment)
    {
        var escaped = new StringBuilder(segment.Length);
        foreach (var character in segment)
        {
            if (character == '%' || character < 32 || character is '<' or '>' or ':' or '"' or '|' or '?' or '*')
            {
                foreach (var value in Encoding.UTF8.GetBytes([character]))
                {
                    escaped.Append('%').Append(value.ToString("X2"));
                }
            }
            else
            {
                escaped.Append(character);
            }
        }

        while (escaped.Length > 0 && escaped[^1] is '.' or ' ')
        {
            var trailing = escaped[^1];
            escaped.Length--;
            escaped.Append(trailing == '.' ? "%2E" : "%20");
        }

        var candidate = escaped.ToString();
        var baseName = candidate.Split('.')[0];
        if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (baseName.Length == 4
                && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && baseName[3] is >= '1' and <= '9'))
        {
            candidate = "%" + ((byte)candidate[0]).ToString("X2") + candidate[1..];
        }

        return candidate;
    }
}

