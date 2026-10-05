using System.Globalization;
using System.Runtime.CompilerServices;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// Builds interpolated text with the invariant culture, whatever the culture of the machine. Text that reaches hashed or saved state (script diagnoses, counter
/// keys, forwarded arguments) must not carry a minus sign or a decimal separator that belongs to the machine: two computers would hash different states.
/// The compiler picks this handler for <c>$"…"</c> arguments of <see cref="Format"/> and of the methods that take it.
/// </summary>
[InterpolatedStringHandler]
internal ref struct InvariantText
{
    private DefaultInterpolatedStringHandler _inner;

    public InvariantText(int literalLength, int formattedCount) => _inner = new DefaultInterpolatedStringHandler(literalLength, formattedCount, CultureInfo.InvariantCulture);

    public void AppendLiteral(string value) => _inner.AppendLiteral(value);
    public void AppendFormatted(string? value) => _inner.AppendFormatted(value);
    public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);
    public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);

    public string ToStringAndClear() => _inner.ToStringAndClear();

    public static string Format(InvariantText text) => text.ToStringAndClear();
}
