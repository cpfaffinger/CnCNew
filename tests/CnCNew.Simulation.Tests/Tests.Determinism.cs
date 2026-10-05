using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Terrain;

/// <summary>Phase 18: determinism across machines (docs/DETERMINISM-FINDINGS.md). Every finding of the read-only search is confirmed or refuted here before it is fixed.</summary>
static partial class Tests
{
    /// <summary>
    /// The cultures the simulation must not notice: German (decimal comma), Turkish (dotless i), Arabic and Swedish (other minus signs, Swedish also sorts 'Ö' after 'Z'),
    /// and a made-up culture whose number format shares nothing with the invariant one.
    /// </summary>
    internal static ImmutableArray<CultureInfo> HostileCultures()
    {
        var hostile = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        hostile.NumberFormat.NegativeSign = "~";
        hostile.NumberFormat.NumberDecimalSeparator = "#";
        hostile.NumberFormat.NumberGroupSeparator = "'";
        return [CultureInfo.GetCultureInfo("de-DE"), CultureInfo.GetCultureInfo("tr-TR"), CultureInfo.GetCultureInfo("ar-SA"), CultureInfo.GetCultureInfo("sv-SE"), hostile];
    }

    internal static T UnderCulture<T>(CultureInfo culture, Func<T> body)
    {
        var (current, ui, threadDefault, threadDefaultUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture, CultureInfo.DefaultThreadCurrentCulture, CultureInfo.DefaultThreadCurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = current;
            CultureInfo.CurrentUICulture = ui;
            CultureInfo.DefaultThreadCurrentCulture = threadDefault;
            CultureInfo.DefaultThreadCurrentUICulture = threadDefaultUi;
        }
    }

    internal static string CultureLabel(CultureInfo culture) => culture.Name.Length > 0 ? culture.Name : "made-up";

    /// <summary>
    /// D4: the hashed script state held a culture-sorted table (production priorities by team name), a diagnosis with a culture-formatted number, and the forwarded
    /// presentation arguments formatted coordinates with the culture of the machine. The same mission must hash and report the same under every culture.
    /// </summary>
    public static void ScriptStateIsCultureIndependent()
    {
        (ulong Hash, string Outputs, string Diagnostics) Play()
        {
            var (map, fixture, terrain) = MissionMap([
                Scr.Script("Set", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                    // Two prototypes whose names sort differently by culture ('Ö' before 'Z' in German, after it in Swedish) and by code point.
                    Scr.Do("TEAM_INCREASE_PRIORITY", Scr.Team("Öland")),
                    Scr.Do("TEAM_INCREASE_PRIORITY", Scr.Team("Zulu")),
                    Scr.Do("TEAM_INCREASE_PRIORITY", Scr.Team("alpha")),
                    // A negative number inside a diagnosis: the minus sign is a property of the culture.
                    Scr.Do("TECHTREE_MODIFY_BUILDABILITY_OBJECT", Scr.Type("AlphaSoldier"), Scr.Int(-1)),
                    // A forwarded action with coordinates: the decimal separator is a property of the culture.
                    Scr.Do("SPEECH_PLAY", Scr.Coord(1.5m, -2.25m, 0m), Scr.Real(-0.5m), Scr.Int(-7))], oneShot: true),
            ], objects: [Placed("AlphaSoldier", 100m, 200m, "\u00D6land"), Placed("AlphaSoldier", 110m, 200m, "Zulu"), Placed("AlphaSoldier", 120m, 200m, "alpha")]);
            MapProperties Team(string name) => new([Scr.Text("teamName", name), Scr.Text("teamOwner", PlayerA), Scr.Flag("teamIsSingleton", true),
                new MapProperty("teamProductionPrioritySuccessIncrease", MapPropertyType.Int, 3, 0m, string.Empty)]);
            map = map with { Teams = [..map.Teams, Team("Öland"), Team("Zulu"), Team("alpha")] };
            var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
            var outputs = new List<string>();
            for (var tick = 0; tick < 5; tick++)
            {
                session.Step();
                foreach (var output in session.Engine.DrainOutputs()) outputs.Add(output.Action + "(" + string.Join("|", output.Arguments) + ")");
            }
            Check.That(session.Program.FindTeam("Zulu") is { } zulu && session.Engine.ProductionPriorityOf(zulu) == zulu.ProductionPriority + 3, "The fixture changes the production priorities.");
            return (session.StateHash(), string.Join(";", outputs), string.Join(";", session.Engine.Diagnostics.Keys));
        }

        var reference = UnderCulture(CultureInfo.InvariantCulture, Play);
        Check.That(reference.Diagnostics.Contains("buildable status -1 out of range", StringComparison.Ordinal), $"The fixture produces the diagnosis with a negative number ({reference.Diagnostics}).");
        Check.That(reference.Outputs.Contains("SPEECH_PLAY(1.5,-2.25,0|-0.5|-7)", StringComparison.Ordinal), $"The fixture forwards coordinates and numbers in invariant notation ({reference.Outputs}).");
        var differing = new List<string>();
        foreach (var culture in HostileCultures())
        {
            var result = UnderCulture(culture, Play);
            if (result != reference)
                differing.Add($"{CultureLabel(culture)}: hash {(result.Hash == reference.Hash ? "equal" : "differs")}, outputs {(result.Outputs == reference.Outputs ? "equal" : result.Outputs)}, diagnostics {(result.Diagnostics == reference.Diagnostics ? "equal" : result.Diagnostics)}");
        }
        Check.That(differing.Count == 0, "The script state depends on the culture of the machine:\n         " + string.Join("\n         ", differing));
    }
}
