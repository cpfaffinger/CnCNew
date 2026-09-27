namespace CnCNew.W3D;

using System.Buffers.Binary;

public static class W3dFileReader
{
    public static W3dReadResult Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("W3D input must be readable.", nameof(stream));
        }

        using var source = new MemoryStream();
        stream.CopyTo(source);
        var bytes = source.ToArray();
        try
        {
            using var strict = new MemoryStream(bytes, writable: false);
            return new W3dReadResult(W3dChunkReader.Read(strict), bytes, false, 0, bytes.Length);
        }
        catch (InvalidDataException strictFailure)
        {
            var (recovered, removed, insertionOffsets) = ReverseLegacyTextMode(bytes);
            if (removed == 0)
            {
                throw;
            }

            if (TryParse(recovered, out var recoveredFile))
            {
                return new W3dReadResult(recoveredFile!, recovered, true, removed, bytes.Length);
            }

            var search = FindUniqueRecovery(recovered, insertionOffsets, maximumPreservedPairs: 3);
            if (search.Candidate is not null)
            {
                return new W3dReadResult(
                    search.Candidate.Value.File,
                    search.Candidate.Value.Data,
                    true,
                    removed - search.Candidate.Value.Preserved,
                    bytes.Length);
            }

            throw new InvalidDataException(
                $"W3D is structurally invalid and CRLF text-mode recovery has no unique valid result. " +
                $"Strict: {strictFailure.Message} Search: {search.Diagnostic}");
        }
    }

    private static (byte[] Bytes, int Removed, int[] InsertionOffsets) ReverseLegacyTextMode(ReadOnlySpan<byte> source)
    {
        var destination = new byte[source.Length];
        var insertionOffsets = new List<int>();
        var written = 0;
        var removed = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == 0x0d && index + 1 < source.Length && source[index + 1] == 0x0a)
            {
                insertionOffsets.Add(written);
                removed++;
                continue;
            }

            destination[written++] = source[index];
        }

        return (destination[..written], removed, [.. insertionOffsets]);
    }

    private static ((W3dFile File, byte[] Data, int Preserved)? Candidate, string Diagnostic) FindUniqueRecovery(
        byte[] stripped,
        int[] insertionOffsets,
        int maximumPreservedPairs)
    {
        for (var preserved = 1; preserved <= maximumPreservedPairs && preserved <= insertionOffsets.Length; preserved++)
        {
            var selected = new int[preserved];
            var matches = new List<(W3dFile File, byte[] Data, int InvalidValues, double Magnitude, string Offsets)>();
            Search(0, 0);
            if (matches.Count == 1)
            {
                return ((matches[0].File, matches[0].Data, preserved), $"unique result preserving {preserved} CRLF pair(s)");
            }

            if (matches.Count > 1)
            {
                var semanticallyValid = matches.Where(candidate => candidate.InvalidValues == 0).ToArray();
                if (semanticallyValid.Length == 1)
                {
                    return ((semanticallyValid[0].File, semanticallyValid[0].Data, preserved),
                        $"unique semantically valid animation result preserving {preserved} CRLF pair(s)");
                }

                return (null,
                    $"{matches.Count} results preserving {preserved} pair(s); decoded offsets " +
                    string.Join(", ", matches.Take(12).Select(candidate =>
                        $"{candidate.Offsets}[invalid={candidate.InvalidValues},magnitude={candidate.Magnitude:F3}]")));
            }

            void Search(int selectionIndex, int nextCandidate)
            {
                if (selectionIndex == selected.Length)
                {
                    var candidateBytes = RestoreCarriageReturns(stripped, insertionOffsets, selected);
                    if (TryParse(candidateBytes, out var file))
                    {
                        var score = ScoreClassicAnimationChannels(candidateBytes, file!);
                        matches.Add((
                            file!,
                            candidateBytes,
                            score.InvalidValues,
                            score.Magnitude,
                            string.Join("+", selected.Select(index => insertionOffsets[index]))));
                    }

                    return;
                }

                var candidatesNeeded = selected.Length - selectionIndex;
                for (var index = nextCandidate; index <= insertionOffsets.Length - candidatesNeeded; index++)
                {
                    selected[selectionIndex] = index;
                    Search(selectionIndex + 1, index + 1);
                }
            }
        }

        return (null, $"no result preserving up to {maximumPreservedPairs} CRLF pairs");
    }

    private static byte[] RestoreCarriageReturns(byte[] stripped, int[] insertionOffsets, int[] selected)
    {
        var restored = new byte[stripped.Length + selected.Length];
        var sourceOffset = 0;
        var destinationOffset = 0;
        foreach (var selectedIndex in selected)
        {
            var insertionOffset = insertionOffsets[selectedIndex];
            var length = insertionOffset - sourceOffset;
            Array.Copy(stripped, sourceOffset, restored, destinationOffset, length);
            sourceOffset += length;
            destinationOffset += length;
            restored[destinationOffset++] = 0x0d;
        }

        Array.Copy(stripped, sourceOffset, restored, destinationOffset, stripped.Length - sourceOffset);
        return restored;
    }

    private static bool TryParse(byte[] bytes, out W3dFile? file)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            file = W3dChunkReader.Read(stream);
            return true;
        }
        catch (InvalidDataException)
        {
            file = null;
            return false;
        }
    }

    private static (int InvalidValues, double Magnitude) ScoreClassicAnimationChannels(
        byte[] bytes,
        W3dFile file)
    {
        var invalidValues = 0;
        double magnitude = 0;
        foreach (var chunk in file.AllChunks.Where(chunk => chunk.Type == 0x00000202))
        {
            if (chunk.Size < 16 || chunk.DataOffset > int.MaxValue)
            {
                invalidValues++;
                continue;
            }

            var offset = checked((int)chunk.DataOffset);
            var firstFrame = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
            var lastFrame = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 2, 2));
            var vectorLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 4, 2));
            if (lastFrame < firstFrame || vectorLength is 0 or > 4)
            {
                invalidValues++;
                continue;
            }

            var valueCount = checked((lastFrame - firstFrame + 1) * vectorLength);
            var requiredBytes = checked(12 + valueCount * sizeof(float));
            if (requiredBytes > chunk.Size)
            {
                invalidValues++;
                continue;
            }

            var valueOffset = offset + 12;
            for (var index = 0; index < valueCount; index++)
            {
                var bits = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(valueOffset + index * 4, 4));
                var value = BitConverter.Int32BitsToSingle(bits);
                if (!float.IsFinite(value) || Math.Abs(value) > 1_000_000)
                {
                    invalidValues++;
                }
                else
                {
                    magnitude += Math.Log2(1 + Math.Abs(value));
                }
            }
        }

        return (invalidValues, magnitude);
    }
}
