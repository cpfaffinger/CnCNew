using System.Text;

namespace CnCNew.Ui.Text;

public sealed class StrFormatException(string message) : FormatException(message);

/// <summary>
/// Reader for the textual string format (<c>Data/Generals.str</c>, <c>map.str</c>), re-implemented from
/// <c>GameTextManager::parseStringFile</c>, <c>readToEndOfQuote</c>, <c>translateCopy</c> and <c>stripSpaces</c>
/// (GameText.cpp). Layout per entry: a label line, one quoted text (may span lines; line breaks become blanks),
/// optionally followed on the closing line by a speech name, then <c>END</c>. Lines that start with <c>//</c> and
/// blank lines are skipped. Bytes are Latin-1 (<c>*inbuf &amp; 0x00FF</c>).
/// <para>
/// Deviations from the original are explicit errors instead of undefined behaviour: a repeated label and a second
/// quoted text inside one entry (both <c>DEBUG_CRASH</c> in the original), an unterminated quote (the original then
/// reads NUL characters to the buffer limit), a missing <c>END</c> (the original fails the whole file) and any other
/// non-comment line inside an entry (the original skips it silently).
/// </para>
/// </summary>
public static class StrReader
{
    public static StringTable Read(ReadOnlySpan<byte> data, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var cursor = new Cursor(Encoding.Latin1.GetString(data), source);
        var entries = new List<StringEntry>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        while (cursor.TryReadLine(out var raw))
        {
            var label = Trim(raw);
            if (label.Length == 0 || label.StartsWith("//", StringComparison.Ordinal)) continue;
            var labelLine = cursor.Line;
            if (seen.TryGetValue(label, out var firstLine))
                throw cursor.Error($"String label '{label}' is defined again (first at line {firstLine}).");
            seen.Add(label, labelLine);

            string? text = null;
            string? speech = null;
            while (true)
            {
                if (!cursor.TryReadLine(out var line))
                    throw cursor.Error($"Unexpected end of string file inside label '{label}' (line {labelLine}); END is missing.");
                var content = Trim(line);
                if (content.StartsWith('"'))
                {
                    if (text is not null)
                        throw cursor.Error($"String label '{label}' has more than one string defined.");
                    var (quoted, speechName) = ReadToEndOfQuote(cursor, content[1..] + "\n", label);
                    text = GameTextWhitespace.StripSpaces(TranslateEscapes(quoted));
                    speech = speechName.Length == 0 ? null : speechName;
                }
                else if (content.Equals("END", StringComparison.OrdinalIgnoreCase))
                    break;
                else if (content.Length != 0 && !content.StartsWith("//", StringComparison.Ordinal))
                    throw cursor.Error($"Unexpected line '{content}' inside string label '{label}'; expected a quoted string or END.");
            }
            entries.Add(new StringEntry(label, text ?? string.Empty, speech));
        }
        return new StringTable(source, StringTableFormat.Str, entries);
    }

    public static StringTable Read(Stream stream, string source)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Read(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), source);
    }

    /// <summary>
    /// Collects the quoted text up to the first quote that is not escaped by a backslash. Line breaks and other
    /// whitespace become blanks. The rest of the closing line may name a speech file: leading blanks and '=' are
    /// skipped, then <c>[A-Za-z0-9_]</c> characters are taken; a name ending in a digit gets the suffix <c>e</c>
    /// (the English speech file naming the original appends).
    /// </summary>
    private static (string Text, string Speech) ReadToEndOfQuote(Cursor cursor, string pending, string label)
    {
        var text = new StringBuilder();
        var index = 0;
        var slash = false;
        var startLine = cursor.Line;
        while (true)
        {
            if (!Next(cursor, pending, ref index, out var ch))
                throw cursor.Error($"Unterminated quoted string of label '{label}' (opened at line {startLine}).");
            if (ch == '\n') { slash = false; ch = ' '; }
            else if (ch == '\\') slash = !slash;
            else if (ch == '"' && !slash) break;
            else slash = false;
            text.Append(IsSpace(ch) ? ' ' : ch);
        }

        var speech = new StringBuilder();
        var state = 0;
        while (Next(cursor, pending, ref index, out var ch) && ch != '\n')
        {
            if (state == 0)
            {
                if (IsSpace(ch) || ch == '=') continue;
                state = 1;
            }
            if (state == 1)
            {
                if (char.IsAsciiLetterOrDigit(ch) || ch == '_') speech.Append(ch);
                else state = 2;
            }
        }
        if (speech.Length > 0 && char.IsAsciiDigit(speech[^1])) speech.Append('e');
        return (text.ToString(), speech.ToString());
    }

    private static bool Next(Cursor cursor, string pending, ref int index, out char ch)
    {
        if (index < pending.Length)
        {
            ch = pending[index++];
            return true;
        }
        return cursor.TryReadChar(out ch);
    }

    /// <summary><c>translateCopy</c>: <c>\\ \' \" \? \t \n</c> are escapes, any other escaped character stands for itself.</summary>
    public static string TranslateEscapes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var output = new StringBuilder(text.Length);
        var slash = false;
        foreach (var ch in text)
        {
            if (slash)
            {
                slash = false;
                output.Append(ch switch { 't' => '\t', 'n' => '\n', _ => ch });
            }
            else if (ch == '\\') slash = true;
            else output.Append(ch);
        }
        return output.ToString();
    }

    private static readonly char[] Spaces = [' ', '\t', '\n', '\v', '\f', '\r'];

    // The C locale's iswspace: ASCII whitespace only, so Latin-1 U+00A0 stays part of the text.
    private static bool IsSpace(char ch) => Array.IndexOf(Spaces, ch) >= 0;

    private static string Trim(string line) => line.Trim(Spaces);

    private sealed class Cursor(string text, string source)
    {
        private int _position;
        public int Line { get; private set; }

        public bool TryReadLine(out string line)
        {
            if (_position >= text.Length)
            {
                line = string.Empty;
                return false;
            }
            var end = text.IndexOf('\n', _position);
            if (end < 0) end = text.Length;
            line = text[_position..end];
            _position = Math.Min(end + 1, text.Length);
            Line++;
            return true;
        }

        public bool TryReadChar(out char ch)
        {
            if (_position >= text.Length)
            {
                ch = '\0';
                return false;
            }
            ch = text[_position++];
            if (ch == '\n') Line++;
            return true;
        }

        public StrFormatException Error(string message) => new($"{source}({Line}): {message}");
    }
}
