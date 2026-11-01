using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CnCNew.Ui.Images;

namespace CnCNew.Game.Data;

/// <summary>
/// A <c>map.ini</c> from a stranger, made fit for the strict compiler. The compiler refuses what the shipped data must never do, and that stays; a map is written against the original's
/// parser, which takes much of it without a word. This turns those lines into what the original ends up with, and names each one: a weapon it cannot find leaves the slot empty
/// (<c>WeaponStore::findWeaponTemplate</c>), a model it cannot find draws nothing, a number too long for its type overflows, <c>RemoveModule = Tag</c> is the tag (the parser's separators
/// hold <c>=</c>), and an image without its sizes is no image. Nothing else is changed.
/// </summary>
internal static partial class MapRulesText
{
    /// <param name="isWeapon">Whether the base data defines a weapon of this name.</param>
    /// <param name="hasModel">Whether the content has a model of this name.</param>
    public static string Clean(string text, Func<string, bool> isWeapon, Func<string, bool> hasModel, ICollection<string> notes)
    {
        var ownWeapons = WeaponBlock().Matches(text).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = text.Split('\n');
        var result = new StringBuilder(text.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var code = line.Split(';', 2)[0];
            if (ImageBlock().Match(code) is { Success: true } image)
            {
                var end = index + 1;
                while (end < lines.Length && !EndLine().IsMatch(lines[end].Split(';', 2)[0])) end++;
                var body = string.Join('\n', lines[(index + 1)..Math.Min(end, lines.Length)].Select(part => part.Split(';', 2)[0]));
                var lacking = MappedImageTable.RequiredKeys.FirstOrDefault(key => !Regex.IsMatch(body, $@"^\s*{key}\s*=", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
                if (lacking is not null)
                {
                    notes.Add($"image {image.Groups[1].Value} has no {lacking}: left out");
                    index = end;
                    continue;
                }
                if (ImageCoords().Match(body) is { Success: true } coords
                    && (int.Parse(coords.Groups[3].Value, CultureInfo.InvariantCulture) <= int.Parse(coords.Groups[1].Value, CultureInfo.InvariantCulture)
                        || int.Parse(coords.Groups[4].Value, CultureInfo.InvariantCulture) <= int.Parse(coords.Groups[2].Value, CultureInfo.InvariantCulture)))
                {
                    notes.Add($"image {image.Groups[1].Value} is an empty rectangle: left out");
                    index = end;
                    continue;
                }
            }
            if (RemoveKey().Match(code) is { Success: true } remove) line = $"{remove.Groups[1].Value}RemoveModule {remove.Groups[2].Value}";
            else if (WeaponLine().Match(code) is { Success: true } weapon)
            {
                var name = weapon.Groups[3].Value;
                var rest = weapon.Groups[4].Value.Trim();
                if (!name.Equals("None", StringComparison.OrdinalIgnoreCase) && !ownWeapons.Contains(name) && !isWeapon(name))
                {
                    notes.Add($"weapon {name} does not exist: the slot stays empty");
                    line = $"{weapon.Groups[1].Value}Weapon = {weapon.Groups[2].Value} None";
                }
                else if (rest.Length > 0)
                {
                    notes.Add($"weapon line with more than a name ({name} {rest}): the rest is ignored");
                    line = $"{weapon.Groups[1].Value}Weapon = {weapon.Groups[2].Value} {name}";
                }
            }
            else if (ModelLine().Match(code) is { Success: true } model)
            {
                var name = model.Groups[2].Value;
                if (!name.Equals("NONE", StringComparison.OrdinalIgnoreCase) && !name.Equals("NULL", StringComparison.OrdinalIgnoreCase) && !hasModel(name.Split('.')[0]))
                {
                    notes.Add($"model {name} does not exist: it draws nothing");
                    line = $"{model.Groups[1].Value}Model = NONE";
                }
            }
            if (WholeNumberKey().Match(code) is { Success: true } whole)
            {
                notes.Add($"{whole.Groups[2].Value} takes a whole number, not {whole.Groups[3].Value}{whole.Groups[4].Value}: read as {whole.Groups[3].Value}");
                line = $"{whole.Groups[1].Value}{whole.Groups[2].Value} = {whole.Groups[3].Value}";
            }
            line = DoubledPoint().Replace(line, match =>
            {
                notes.Add($"number {match.Value} has two points: read as {match.Groups[1].Value}");
                return match.Groups[1].Value;
            });
            line = LongNumber().Replace(line, match =>
            {
                notes.Add($"number {match.Value[..Math.Min(match.Length, 16)]}... is too long for its type: taken as the largest integer");
                return int.MaxValue.ToString(CultureInfo.InvariantCulture);
            });
            result.Append(line);
            if (index < lines.Length - 1) result.Append('\n');
        }

        return result.ToString();
    }

    [GeneratedRegex(@"^\s*Weapon\s+(\S+)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WeaponBlock();

    [GeneratedRegex(@"^\s*MappedImage\s+(\S+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageBlock();

    [GeneratedRegex(@"Left:\s*(-?\d+)\s+Top:\s*(-?\d+)\s+Right:\s*(-?\d+)\s+Bottom:\s*(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageCoords();

    [GeneratedRegex(@"^\s*End\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EndLine();

    [GeneratedRegex(@"^(\s*)RemoveModule\s*=\s*(\S+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RemoveKey();

    [GeneratedRegex(@"^(\s*)Weapon\s*=\s*(\S+)\s+(\S+)(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WeaponLine();

    [GeneratedRegex(@"^(\s*)Model\s*=\s*(\S+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModelLine();

    [GeneratedRegex(@"(?<![\w.:\-])\d{11,}(?![\w.])", RegexOptions.CultureInvariant)]
    private static partial Regex LongNumber();

    /// <summary><c>INI::scanInt</c> is <c>sscanf %d</c>: it stops at the point. The one key where real maps write a fraction; the others stay refused until one does.</summary>
    [GeneratedRegex(@"^(\s*)(MilitaryCaptionSpeed)\s*=\s*(-?\d+)(\.\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WholeNumberKey();

    /// <summary><c>atof</c> stops at the second point.</summary>
    [GeneratedRegex(@"(?<![\w.])(-?\d+\.\d+)(?:\.\d+)+(?![\w.])", RegexOptions.CultureInvariant)]
    private static partial Regex DoubledPoint();
}
