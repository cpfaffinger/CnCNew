using System.Globalization;
using System.Text;

namespace CnCNew.Ui.Text;

/// <summary>
/// The printf subset that localized texts use with <c>GameTextManager::fetchFormat</c> (<c>UnicodeString::format_va</c>
/// on a wide format string): <c>%[flags][width][.precision][h|l|ll|I64]conversion</c> with the flags <c>- + 0 space #</c>
/// and the conversions <c>d i u x X c s S f F %</c>. <c>%s</c>, <c>%ls</c>, <c>%hs</c> and <c>%S</c> all take a
/// string here because .NET strings have no narrow/wide distinction. Arguments are type-checked: a mismatch, a missing or
/// a surplus argument, an unknown conversion or a dangling '%' is a <see cref="FormatException"/>, never a guess.
/// Formatting uses the invariant culture so the output is deterministic.
/// </summary>
public static class GameTextFormatter
{
    public static string Format(string format, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(args);
        var output = new StringBuilder(format.Length + 16);
        var next = 0;
        var index = 0;
        while (index < format.Length)
        {
            var ch = format[index++];
            if (ch != '%')
            {
                output.Append(ch);
                continue;
            }
            var start = index - 1;
            if (index >= format.Length) throw new FormatException($"Dangling '%' at the end of \"{format}\".");
            if (format[index] == '%')
            {
                output.Append('%');
                index++;
                continue;
            }

            var left = false; var plus = false; var zero = false; var space = false; var alternate = false;
            for (; index < format.Length; index++)
            {
                switch (format[index])
                {
                    case '-': left = true; continue;
                    case '+': plus = true; continue;
                    case '0': zero = true; continue;
                    case ' ': space = true; continue;
                    case '#': alternate = true; continue;
                }
                break;
            }
            var width = ReadNumber(format, ref index);
            int? precision = null;
            if (index < format.Length && format[index] == '.')
            {
                index++;
                precision = ReadNumber(format, ref index) ?? 0;
            }
            SkipLengthModifier(format, ref index);
            if (index >= format.Length) throw new FormatException($"Incomplete conversion '{format[start..]}' in \"{format}\".");
            var conversion = format[index++];
            var spec = format[start..index];
            if (next >= args.Length)
                throw new FormatException($"Conversion '{spec}' in \"{format}\" has no argument ({args.Length} supplied).");
            var argument = args[next++];

            string body;
            var numeric = true;
            switch (conversion)
            {
                case 'd' or 'i':
                    body = FormatSigned(Integer(argument, spec), precision, plus, space);
                    break;
                case 'u':
                    body = PadDigits(UnsignedInteger(argument, spec).ToString(CultureInfo.InvariantCulture), precision);
                    break;
                case 'x' or 'X':
                    var hex = UnsignedInteger(argument, spec).ToString(conversion == 'x' ? "x" : "X", CultureInfo.InvariantCulture);
                    body = PadDigits(hex, precision);
                    if (alternate && hex != "0") body = (conversion == 'x' ? "0x" : "0X") + body;
                    break;
                case 'c':
                    body = argument switch
                    {
                        char value => value.ToString(),
                        _ => throw Mismatch(spec, "a char", argument),
                    };
                    numeric = false;
                    break;
                case 's' or 'S':
                    var text = argument as string ?? throw Mismatch(spec, "a string", argument);
                    body = precision is { } maximum && maximum < text.Length ? text[..maximum] : text;
                    numeric = false;
                    break;
                case 'f' or 'F':
                    body = FormatReal(Real(argument, spec), precision ?? 6, plus, space);
                    break;
                default:
                    throw new FormatException($"Unsupported conversion '{spec}' in \"{format}\".");
            }

            if (width is { } minimum && body.Length < minimum)
            {
                if (left) body = body.PadRight(minimum);
                else if (zero && numeric && !(precision.HasValue && conversion is 'd' or 'i' or 'u' or 'x' or 'X'))
                {
                    var sign = body.Length > 0 && body[0] is '-' or '+' or ' ' ? 1 : 0;
                    body = body[..sign] + new string('0', minimum - body.Length) + body[sign..];
                }
                else body = body.PadLeft(minimum);
            }
            output.Append(body);
        }
        if (next != args.Length)
            throw new FormatException($"\"{format}\" consumes {next} argument(s) but {args.Length} were supplied.");
        return output.ToString();
    }

    private static int? ReadNumber(string format, ref int index)
    {
        if (index < format.Length && format[index] == '*')
            throw new FormatException($"'*' widths are not supported in \"{format}\".");
        var start = index;
        while (index < format.Length && char.IsAsciiDigit(format[index])) index++;
        return index == start ? null : int.Parse(format.AsSpan(start, index - start), CultureInfo.InvariantCulture);
    }

    private static void SkipLengthModifier(string format, ref int index)
    {
        if (string.CompareOrdinal(format, index, "I64", 0, 3) == 0) { index += 3; return; }
        if (string.CompareOrdinal(format, index, "ll", 0, 2) == 0) { index += 2; return; }
        if (index < format.Length && format[index] is 'h' or 'l' or 'L') index++;
    }

    private static long Integer(object? value, string spec) => value switch
    {
        int v => v, long v => v, short v => v, sbyte v => v, byte v => v, ushort v => v, uint v => v,
        _ => throw Mismatch(spec, "an integer", value),
    };

    private static ulong UnsignedInteger(object? value, string spec) => value switch
    {
        uint v => v, ulong v => v, ushort v => v, byte v => v,
        int v => unchecked((uint)v),
        long v => unchecked((ulong)v),
        _ => throw Mismatch(spec, "an integer", value),
    };

    private static double Real(object? value, string spec) => value switch
    {
        float v => v, double v => v, decimal v => (double)v,
        _ => throw Mismatch(spec, "a real number", value),
    };

    private static string FormatSigned(long value, int? precision, bool plus, bool space)
    {
        var digits = PadDigits(Math.Abs((decimal)value).ToString(CultureInfo.InvariantCulture), precision);
        return (value < 0 ? "-" : plus ? "+" : space ? " " : string.Empty) + digits;
    }

    private static string PadDigits(string digits, int? precision)
        => precision is { } minimum && digits.Length < minimum ? new string('0', minimum - digits.Length) + digits : digits;

    private static string FormatReal(double value, int precision, bool plus, bool space)
    {
        if (!double.IsFinite(value)) throw new FormatException($"Non-finite value {value} cannot be formatted.");
        var text = value.ToString("F" + precision, CultureInfo.InvariantCulture);
        if (!text.StartsWith('-') && (plus || space)) text = (plus ? "+" : " ") + text;
        return text;
    }
    private static FormatException Mismatch(string spec, string expected, object? value)
        => new($"Conversion '{spec}' expects {expected}, got {(value is null ? "null" : value.GetType().Name)}.");
}
