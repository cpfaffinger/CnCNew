using System.Text.RegularExpressions;

/// <summary>
/// The fourth lexical rule set (docs/FIXED-POINT-Q39-24.md §5): in the simulation a value is not multiplied with itself, unless it is a coordinate
/// component. A reach from the data (a range, a radius, a limit) is compared through <c>SquaredReach</c>, which cannot overflow; the square of a
/// difference of two positions (<c>dx * dx</c>) stays a plain product, because positions are bounded by the map. The rule reads names, not types:
/// an operand whose last identifier ends in x, y or z is taken as such a component, everything else is a violation or a named exception with the
/// reason why its value is small. Like every rule here it is a tripwire, not a proof – a square written as <c>a * b</c> passes.
/// </summary>
static partial class Tests
{
    private const string RuleSelfProduct = "a value multiplied with itself (compare through SquaredReach, or name the exception)";

    // "x * x" for an identifier chain x, and "(e) * (e)" for one parenthesised expression e without inner parentheses.
    private static readonly Regex SelfProductPattern = new(@"(?<![\w.)\]])(?<operand>(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*)\s*\*\s*\k<operand>(?![\w.(\[])|\((?<operand>[^()]+)\)\s*\*\s*\(\k<operand>\)");

    private static bool SquaresSomethingThatIsNoComponent(string line)
    {
        foreach (Match match in SelfProductPattern.Matches(line))
        {
            var operand = match.Groups["operand"].Value.TrimEnd();
            if (operand[^1] is not ('x' or 'y' or 'z' or 'X' or 'Y' or 'Z')) return true;
        }
        return false;
    }

    private static readonly SourceRule[] SelfProductRules = [new(RuleSelfProduct, SelfProductPattern, SquaresSomethingThatIsNoComponent)];

    private static readonly SourceException[] SelfProductExceptions =
    [
        // -- a length that is at most a distance between two positions (the squared distance it was taken from did not overflow)
        new("Simulation/World/SimulationWorld.Combat.cs", RuleSelfProduct, "shrunkSquared = shrunk * shrunk", "the centre distance minus the victim's bounding sphere: below a distance whose square was just formed"),
        new("Simulation/Modules/BuiltIn/AirWeaponModules.cs", RuleSelfProduct, "shrunk * shrunk : Fixed.Zero", "the centre distance minus the candidate's bounding sphere: below a distance whose square was just formed"),
        new("Simulation/Modules/BuiltIn/SlaveModules.cs", RuleSelfProduct, "private static Fixed ShrunkSquared(Fixed distance)", "a distance between two objects minus their bounding circles"),
        // -- geometry of a template or of a growing shape: the largest retail radius is 1,117 (rate-audit --ranges)
        new("Simulation/Templates/ObjectTemplate.cs", RuleSelfProduct, "public Fixed BoundingCircleRadius =>", "the diagonal of a box of the template's geometry"),
        new("Simulation/Templates/ObjectTemplate.cs", RuleSelfProduct, "if (IsBox) return Fixed.Sqrt(", "the diagonal of a box of the template's geometry"),
        new("Simulation/Modules/BuiltIn/WorldEffectModules.cs", RuleSelfProduct, "_current.Major * _current.Major", "the diagonal of the firestorm's current box (FinalMajor of the data: at most 90)"),
        // -- speeds per tick and fractions between 0 and 1
        new("Simulation/Modules/BuiltIn/Phase5Modules.cs", RuleSelfProduct, "var brakingDistance = locomotor.Braking > Fixed.Zero", "v² / 2a with the unit's current speed per tick"),
        new("Simulation/Modules/BuiltIn/Phase5Modules.cs", RuleSelfProduct, "brakingDistance = (Entity.Speed * Entity.Speed - locomotor.MinSpeed * locomotor.MinSpeed)", "v² / 2a with speeds per tick"),
        new("Simulation/Modules/BuiltIn/Phase5Modules.cs", RuleSelfProduct, "var brakingDist = locomotor.Braking > Fixed.Zero", "v² / 2a with the unit's current speed per tick"),
        new("Simulation/Modules/BuiltIn/ProjectileModules.cs", RuleSelfProduct, "var b0 = u * u * u;", "Bernstein polynomial of a fraction in [0, 1]"),
        new("Simulation/Modules/BuiltIn/ProjectileModules.cs", RuleSelfProduct, "var b1 = Fixed.FromInt(3) * u * u * t;", "Bernstein polynomial of a fraction in [0, 1]"),
        new("Simulation/Modules/BuiltIn/ProjectileModules.cs", RuleSelfProduct, "var b2 = Fixed.FromInt(3) * u * t * t;", "Bernstein polynomial of a fraction in [0, 1]"),
        new("Simulation/Modules/BuiltIn/ProjectileModules.cs", RuleSelfProduct, "var b3 = t * t * t;", "Bernstein polynomial of a fraction in [0, 1]"),
        new("Simulation/Modules/BuiltIn/AirWeaponModules.cs", RuleSelfProduct, "scaled * scaled / factor * data.SpecialSpeedHeight", "the acceleration factor of the data times a fraction in [0, 1]"),
        new("Simulation/Modules/BuiltIn/FlightModules.cs", RuleSelfProduct, "Fixed.Sqrt(Fixed.One - dot * dot)", "the cosine between two unit vectors, clamped to ±0.99999 the line before"),
        new("Simulation/Modules/BuiltIn/JetAIUpdate.cs", RuleSelfProduct, "parking.Data.ApproachHeight * ratio * ratio", "a fraction in [0, 1]"),
        // -- whole numbers, not fixed-point values
        new("Simulation/Terrain/VisionMap.cs", RuleSelfProduct, "private static long SquaredRadius(long reach)", "the saturating square of the vision circle test itself (machine words at 16 fraction bits)"),
        new("Simulation/Time/SimulationTime.cs", RuleSelfProduct, "public Fixed PerSecondSquaredToPerTickSquared(decimal unitsPerSecondSquared)", "ticks per second, an integer"),
        new("Simulation/Time/SimulationTime.cs", RuleSelfProduct, "public Fixed PerOriginalFrameSquaredToPerTickSquared(decimal valuePerFrameSquared)", "ticks per original frame, an integer"),
        new("Simulation/Time/SimulationTime.cs", RuleSelfProduct, "var perTick = valuePerFrameSquared / Fixed.FromInt(_ticksPerOriginalFrame * _ticksPerOriginalFrame);", "ticks per original frame, an integer"),
        new("Simulation/World/SimulationWorld.cs", RuleSelfProduct, "SimulationRate.OriginalFramesPerSecond * SimulationRate.OriginalFramesPerSecond", "30 · 30, an integer constant in a decimal"),
    ];

    public static void SquaredReachSourceRules()
    {
        var root = RepositoryRoot();
        var violations = ScanSources(root, SourceFilesUnder(root, Path.Combine("src", "CnCNew.Simulation")), SelfProductRules, SelfProductExceptions);
        Check.That(violations.Count == 0, $"Squares outside SquaredReach in the simulation ({violations.Count}):\n  " + string.Join("\n  ", violations));

        string[] caught =
        [
            "if (dx * dx + dy * dy <= reach * reach) return true;", "var limit = data.ScanRange * data.ScanRange;", "if (distanceSquared > range*range) continue;",
            "return distanceSquared >= minimum * minimum - Fixed.FromDecimal(0.5m);", "var best = (radius + margin) * (radius + margin);", "var bestDistance = BoardingReach * BoardingReach;",
            "if (rx * rx + ry * ry > _radius * _radius) continue;",
        ];
        foreach (var line in caught)
            Check.That(SelfProductRules[0].Hits(line), $"The self-product rule does not catch: {line}");
        string[] allowed =
        [
            "var distance = dx * dx + dy * dy + dz * dz;", "if (SquaredReach.Of(reach).Contains(dx * dx + dy * dy)) return true;", "var length = Fixed.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);",
            "var squared = (value.X - carrier.X) * (value.X - carrier.X);", "var product = reach * reachOfTheOther;", "var area = width * height;", "var sum = a.Range * b.Range;",
            "private const int CloseEnoughSquared = 15 * 15;", "var scaled = range * range2;", "x = toX * toX + beforeDy * beforeDy;",
        ];
        foreach (var line in allowed)
            Check.That(!SelfProductRules[0].Hits(line), $"The self-product rule wrongly catches: {line}");
    }
}
