using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace CnCNew.Ui.Text;

/// <summary><c>LanguageID</c> of <c>Common/Language.h</c>, stored in the CSF header from version 2 on.</summary>
public enum CsfLanguage
{
    Us = 0, Uk, German, French, Spanish, Italian, Japanese, Jabber, Korean, Unknown,
}

/// <summary>One value of a CSF label. <see cref="Text"/> is the decoded text exactly as stored (no whitespace
/// normalization); <see cref="Speech"/> is the ASCII extra value of an <c>STRW</c> record, <c>null</c> for <c>STR </c>.</summary>
public sealed record CsfValue(string Text, string? Speech);

public sealed record CsfLabel(string Label, ImmutableArray<CsfValue> Values)
{
    /// <summary>
    /// The entry the game uses: <c>GameTextManager::parseCSF</c> keeps only the first value, normalizes it with
    /// <c>stripSpaces</c> and takes the speech name of the first value when it is non-empty. A label without values
    /// yields an empty text.
    /// </summary>
    public StringEntry ToEntry()
    {
        if (Values.IsDefaultOrEmpty) return new StringEntry(Label, string.Empty, null);
        var first = Values[0];
        return new StringEntry(Label, GameTextWhitespace.StripSpaces(first.Text), string.IsNullOrEmpty(first.Speech) ? null : first.Speech);
    }
}

/// <param name="DeclaredStringCount">Header field <c>num_strings</c>; the original reads but never uses it.</param>
/// <param name="Reserved">Header field <c>skip</c>; the original reads but never uses it.</param>
public sealed record CsfFile(int Version, CsfLanguage Language, int DeclaredStringCount, int Reserved, ImmutableArray<CsfLabel> Labels)
{
    public const int HeaderSize = 24;
    public const uint FileId = ('C' << 24) | ('S' << 16) | ('F' << 8) | ' ';
    public const uint LabelId = ('L' << 24) | ('B' << 16) | ('L' << 8) | ' ';
    public const uint StringId = ('S' << 24) | ('T' << 16) | ('R' << 8) | ' ';
    public const uint StringWithSpeechId = ('S' << 24) | ('T' << 16) | ('R' << 8) | 'W';
    public const int MaximumKnownVersion = 3;

    public int ValueCount => Labels.Sum(label => label.Values.Length);
}

public sealed class CsfFormatException(string message) : FormatException(message);

/// <summary>
/// Reader for the compiled string file, re-implemented from <c>GameTextManager::getCSFInfo</c>/<c>parseCSF</c>
/// (GameText.cpp). All integers are little-endian 32-bit; tags are stored as the integer of four ASCII characters
/// (so <c>'CSF '</c> appears as the bytes <c>" FSC"</c>). Unlike the original, which silently stops at short reads and
/// trusts the header's label count for its allocation, every structural deviation is an explicit error here.
/// </summary>
public static class CsfReader
{
    public static CsfFile Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < CsfFile.HeaderSize)
            throw new CsfFormatException($"CSF data has {data.Length} bytes; the header alone needs {CsfFile.HeaderSize}.");
        var id = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (id != CsfFile.FileId)
            throw new CsfFormatException($"CSF header id is 0x{id:X8}, expected 'CSF ' (0x{CsfFile.FileId:X8}).");
        var version = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        if (version < 1 || version > CsfFile.MaximumKnownVersion)
            throw new CsfFormatException($"CSF version {version} is not supported (known: 1 to {CsfFile.MaximumKnownVersion}).");
        var labelCount = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        var stringCount = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        var reserved = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
        var languageId = BinaryPrimitives.ReadInt32LittleEndian(data[20..]);
        if (labelCount < 0) throw new CsfFormatException($"CSF header declares a negative label count ({labelCount}).");

        // Versions before 2 carry no language; the original assumes LANGUAGE_ID_US.
        var language = CsfLanguage.Us;
        if (version >= 2)
        {
            if (languageId < 0 || languageId > (int)CsfLanguage.Unknown)
                throw new CsfFormatException($"CSF language id {languageId} is not a LanguageID (0 to {(int)CsfLanguage.Unknown}).");
            language = (CsfLanguage)languageId;
        }

        var position = CsfFile.HeaderSize;
        var labels = ImmutableArray.CreateBuilder<CsfLabel>(labelCount);
        while (position < data.Length)
        {
            var labelIndex = labels.Count;
            var tag = ReadUInt32(data, ref position, $"label {labelIndex} tag");
            if (tag != CsfFile.LabelId)
                throw new CsfFormatException($"Expected 'LBL ' at offset {position - 4} (label {labelIndex}), found 0x{tag:X8}.");
            var valueCount = ReadLength(data, ref position, $"label {labelIndex} value count");
            var labelLength = ReadLength(data, ref position, $"label {labelIndex} name length");
            var label = Encoding.Latin1.GetString(Take(data, ref position, labelLength, $"label {labelIndex} name"));
            if (label.Length == 0) throw new CsfFormatException($"Label {labelIndex} at offset {position} has an empty name.");

            var values = ImmutableArray.CreateBuilder<CsfValue>(valueCount);
            for (var valueIndex = 0; valueIndex < valueCount; valueIndex++)
            {
                var what = $"value {valueIndex} of label '{label}'";
                var valueTag = ReadUInt32(data, ref position, what + " tag");
                if (valueTag != CsfFile.StringId && valueTag != CsfFile.StringWithSpeechId)
                    throw new CsfFormatException($"Expected 'STR ' or 'STRW' at offset {position - 4} ({what}), found 0x{valueTag:X8}.");
                var charCount = ReadLength(data, ref position, what + " length");
                if (charCount > int.MaxValue / 2) throw new CsfFormatException($"The length of {what} ({charCount}) is out of range.");
                var text = DecodeInverted(Take(data, ref position, charCount * 2, what));
                string? speech = null;
                if (valueTag == CsfFile.StringWithSpeechId)
                {
                    var speechLength = ReadLength(data, ref position, what + " speech length");
                    speech = Encoding.Latin1.GetString(Take(data, ref position, speechLength, what + " speech"));
                }
                values.Add(new CsfValue(text, speech));
            }
            labels.Add(new CsfLabel(label, values.MoveToImmutable()));
        }

        if (labels.Count != labelCount)
            throw new CsfFormatException($"CSF header declares {labelCount} labels but the file contains {labels.Count}.");
        return new CsfFile(version, language, stringCount, reserved, labels.MoveToImmutable());
    }

    public static CsfFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Read(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    /// <summary>
    /// Text code units are stored bitwise inverted (<c>*ptr = ~*ptr</c>). The original copies the result into a
    /// null-terminated <c>UnicodeString</c>, so a decoded U+0000 ends the text; that truncation is kept.
    /// </summary>
    private static string DecodeInverted(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length / 2];
        for (var index = 0; index < chars.Length; index++)
            chars[index] = (char)~BinaryPrimitives.ReadUInt16LittleEndian(bytes[(index * 2)..]);
        var end = Array.IndexOf(chars, '\0');
        return new string(chars, 0, end < 0 ? chars.Length : end);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int position, string what)
        => BinaryPrimitives.ReadUInt32LittleEndian(Take(data, ref position, 4, what));

    private static int ReadLength(ReadOnlySpan<byte> data, ref int position, string what)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(Take(data, ref position, 4, what));
        if (value < 0) throw new CsfFormatException($"The {what} at offset {position - 4} is negative ({value}).");
        return value;
    }

    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> data, ref int position, int count, string what)
    {
        if (count > data.Length - position)
            throw new CsfFormatException($"CSF data is truncated: {what} needs {count} bytes at offset {position}, {data.Length - position} remain.");
        var slice = data.Slice(position, count);
        position += count;
        return slice;
    }
}
