using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>The computer players of a mission: every side that is not human has an <c>AIPlayer</c> (F-91, docs/SKIRMISH-AI-ORIGINAL.md §10).</summary>
static partial class Tests
{
    private static readonly PlayerId HumanOfAiMission = new(1);
    private static readonly PlayerId ComputerOfAiMission = new(2);

    private static MapSide AiMissionSide(string name, bool human, string faction, string enemy, ImmutableArray<MapBuildListEntry> buildList, params MapProperty[] extra)
        => new(new MapProperties([Scr.Text("playerName", name), Scr.Flag("playerIsHuman", human), Scr.Text("playerFaction", faction), Scr.Text("playerAllies", string.Empty), Scr.Text("playerEnemies", enemy), ..extra]), buildList);

    /// <summary>
    /// The map of <see cref="MissionMap"/> with factions on its two sides: <see cref="PlayerA"/> is human (<c>FactionAlpha</c>), <see cref="PlayerB"/> the computer (<c>FactionBeta</c>) with the given
    /// build list and teams and with <paramref name="scripts"/> in its own script list.
    /// </summary>
    private static (MapData Map, SyntheticFixture Fixture, TerrainMap Terrain) AiMission(MapScript[] scripts, MapObject[]? objects = null, MapBuildListEntry[]? buildList = null, MapProperties[]? teams = null,
        IReadOnlyDictionary<string, string>? files = null, MapProperty[]? computerSide = null, MapScript[]? human = null)
    {
        var (map, fixture, _) = MissionMap(human ?? [], objects: objects, extraFiles: files);
        map = map with
        {
            Sides = [map.Sides[0], AiMissionSide(PlayerA, true, "FactionAlpha", PlayerB, []), AiMissionSide(PlayerB, false, "FactionBeta", PlayerA, [..buildList ?? []], computerSide ?? [])],
            Teams = [..map.Teams, ..teams ?? []],
            PlayerScripts = [map.PlayerScripts[0], map.PlayerScripts[1], new MapScriptList([..scripts], [])],
        };
        return (map, fixture, TerrainMap.Build(map, "mission"));
    }

    private static MissionSession StartAiMission(MapScript[] scripts, MapObject[]? objects = null, MapBuildListEntry[]? buildList = null, MapProperties[]? teams = null,
        IReadOnlyDictionary<string, string>? files = null, MapProperty[]? computerSide = null, MissionOptions? options = null)
    {
        var (map, fixture, terrain) = AiMission(scripts, objects, buildList, teams, files, computerSide);
        return MissionSession.Start(options ?? MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
    }

    /// <summary><c>Player::init</c>: a template without money gives <c>DefaultStartingCash</c>; <c>Player::initFromDict</c>: <c>playerStartMoney</c> comes on top.</summary>
    public static void MissionPlayersStartWithTheDefaultCash()
    {
        var files = new Dictionary<string, string>
        {
            ["Data/INI/GameData.ini"] = "GameData\n  SellPercentage = 50%\n  DefaultStartingCash = 3000\nEnd\n",
            ["Data/INI/Object/PoorFixture.ini"] = "PlayerTemplate FactionPoor\n  Side = Beta\n  PlayableSide = Yes\nEnd\n",
        };
        var (map, fixture, terrain) = AiMission([], files: files, computerSide: [IntProperty("playerStartMoney", 250)]);
        var money = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(fixture.Templates.DefaultStartingCash == 3000, "The key of GameData.ini is read.");
        Check.That(money.World.MoneyOf(HumanOfAiMission) == Fixed.FromInt(500), $"A template with money keeps it ({money.World.MoneyOf(HumanOfAiMission)}).");
        Check.That(money.World.MoneyOf(ComputerOfAiMission) == Fixed.FromInt(750), $"The map's playerStartMoney comes on top ({money.World.MoneyOf(ComputerOfAiMission)}).");

        var poor = map with { Sides = [map.Sides[0], map.Sides[1], AiMissionSide(PlayerB, false, "FactionPoor", PlayerA, [], IntProperty("playerStartMoney", 250))] };
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, TerrainMap.Build(poor, "mission"), poor);
        Check.That(session.World.MoneyOf(ComputerOfAiMission) == Fixed.FromInt(3250), $"A template without money gives the default cash, the map's money on top ({session.World.MoneyOf(ComputerOfAiMission)}).");

        var unknown = map with { Sides = [map.Sides[0], map.Sides[1], AiMissionSide(PlayerB, false, "FactionNobody", PlayerA, [])] };
        var nobody = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, TerrainMap.Build(unknown, "mission"), unknown);
        Check.That(nobody.World.MoneyOf(ComputerOfAiMission) == Fixed.Zero, "A faction that is no template is initialised like the neutral player: without money.");

        Check.Throws<TemplateBuildException>(() => _ = new SyntheticFixture(TestRate.Default, files: new Dictionary<string, string>(ScriptFiles()) { ["Data/INI/GameData.ini"] = "GameData\n  DefaultStartingCash = plenty\nEnd\n" }), "DefaultStartingCash");
    }
}
