using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.World;

namespace CnCNew.Simulation.Skirmish.Original;

/// <summary>One occupied slot of a skirmish as the script attachment needs it.</summary>
/// <param name="Faction">The player template (<c>FactionAmerica</c>).</param>
/// <param name="Side">The side of that template (<c>America</c>): the skirmish template player of the map with the same side supplies the scripts.</param>
/// <param name="StartIndex">Zero-based start position (<c>multiplayerStartIndex</c>): the qualifier of every script, team, counter and flag name of this player.</param>
/// <param name="Team">Alliance number; 0 means no team (everybody else is an enemy).</param>
/// <param name="Scripted">A computer slot that is not run by the script library (another AI plays it): it gets no script list and no teams but its default team.</param>
public sealed record SkirmishScriptSlot(string Faction, string Side, int StartIndex, bool IsHuman, int Team, bool Scripted = true);

/// <summary>What the attachment did, for the caller's notes: nothing is dropped silently.</summary>
public sealed record SkirmishAttachmentReport(ImmutableArray<string> Notes, bool LibraryAttached);

/// <summary>
/// Turns a skirmish map into the map the script engine runs for a concrete match, as the reference does when a skirmish starts:
/// <list type="bullet">
/// <item><description><c>SidesList::prepareForMP_or_Skirmish</c> (SidesList.cpp): the map's own sides become the skirmish template sides; only <c>FactionCivilian</c> sides stay. If no template side
/// brought scripts of its own, the script lists of <c>SkirmishScripts.scb</c> are attached to the template sides by player name and its teams replace the map's.</description></item>
/// <item><description><c>GameLogic::startNewGame</c> (GameLogic.cpp): one side <c>player&lt;slot&gt;</c> per occupied slot with its allies and enemies by team number, and its default team
/// <c>teamplayer&lt;slot&gt;</c>.</description></item>
/// <item><description><c>Player::initFromDict</c> (Player.cpp): a computer slot gets a copy of the script list and of the teams of the template side whose faction has the player's side,
/// every name qualified with the start position (<c>ScriptList::duplicateAndQualify</c>, <c>Parameter::qualify</c>, Scripts.cpp); a human slot gets the civilian template side's list the same way.</description></item>
/// </list>
/// The result is ordinary <see cref="MapData"/>: side 0 neutral, sides 1..N the slots in order (so the player of slot <c>i</c> is player <c>i + 1</c>), then the civilian sides.
/// Not done here: <c>MultiplayerScripts.scb</c> (victory and defeat announcements for a local human; the session decides the winner itself) and the observer side.
/// </summary>
public static class SkirmishScriptAttachment
{
    private const string ThisTeam = "<This Team>";
    /// <summary>The template players of the shipped library are named after their side: <c>SkirmishAmerica</c>, <c>SkirmishChinaTankGeneral</c> ...</summary>
    private const string LibraryPlayerPrefix = "Skirmish";

    private static (string Name, string Faction, MapScriptList Scripts)? LibraryTemplate(MapScriptLibrary library, string side)
    {
        for (var index = 0; index < library.Players.Length && index < library.ScriptLists.Length; index++)
            if (string.Equals(library.Players[index].Name, LibraryPlayerPrefix + side, StringComparison.Ordinal)) return (library.Players[index].Name, string.Empty, library.ScriptLists[index]);
        return null;
    }
    private const string CivilianFaction = "FactionCivilian";

    /// <param name="sideOfFaction">The side of a player template name (<c>PlayerTemplate::getSide</c>), or <see langword="null"/> for an unknown template.</param>
    /// <param name="libraryForMissingSides">
    /// Not the reference: a computer slot whose side has no template side on the map takes the scripts and teams of the library's template player of that side
    /// (<c>Skirmish&lt;Side&gt;</c>) instead of running without scripts. The reference leaves such a player without AI ("quietly ... nonskirmish"); two shipped maps
    /// (<c>Armored Fury</c>, <c>Scorched Earth</c>) only have the three template sides of the base game, so a general has no scripts there.
    /// </param>
    public static MapData Attach(MapData map, MapScriptLibrary? library, ImmutableArray<SkirmishScriptSlot> slots, Func<string, string?> sideOfFaction, out SkirmishAttachmentReport report,
        bool libraryForMissingSides = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sideOfFaction);
        var notes = ImmutableArray.CreateBuilder<string>();

        // The template sides: every non-civilian named side of the map, with the script list the map gives it.
        var templates = new List<(string Name, string Faction, MapScriptList Scripts)>();
        var civilians = new List<(MapSide Side, MapScriptList Scripts)>();
        for (var index = 1; index < map.Sides.Length; index++)
        {
            var properties = map.Sides[index].Properties;
            var scripts = index < map.PlayerScripts.Length ? map.PlayerScripts[index] : Empty;
            if (properties.GetText("playerFaction") == CivilianFaction) civilians.Add((map.Sides[index], scripts));
            else templates.Add((properties.GetText("playerName") ?? string.Empty, properties.GetText("playerFaction") ?? string.Empty, scripts));
        }
        var templateTeams = map.Teams;
        var mapHasScripts = templates.Any(template => template.Scripts.Scripts.Length + template.Scripts.Groups.Length > 0);
        var attached = false;
        if (!mapHasScripts && library is not null)
        {
            attached = true;
            templateTeams = library.Teams;
            for (var index = 0; index < templates.Count; index++)
            {
                var position = library.Players.IndexOf(library.Players.FirstOrDefault(player => player.Name == templates[index].Name)!);
                if (position >= 0 && position < library.ScriptLists.Length) templates[index] = templates[index] with { Scripts = library.ScriptLists[position] };
            }
        }
        else if (mapHasScripts) notes.Add("the map brings its own skirmish scripts; the script library is not attached");
        else notes.Add("no script library: computer players run without scripts");

        var sides = ImmutableArray.CreateBuilder<MapSide>();
        var lists = ImmutableArray.CreateBuilder<MapScriptList>();
        var teams = ImmutableArray.CreateBuilder<MapProperties>();
        sides.Add(map.Sides.Length > 0 ? map.Sides[0] : new MapSide(Dict(("playerName", string.Empty)), []));
        lists.Add(Empty);
        // Teams that survive: the neutral side's and the civilians' (their owners still exist).
        var civilianNames = civilians.Select(entry => entry.Side.Properties.GetText("playerName") ?? string.Empty).ToHashSet(StringComparer.Ordinal);
        foreach (var team in map.Teams)
        {
            var owner = team.GetText("teamOwner") ?? string.Empty;
            if (owner.Length == 0 || civilianNames.Contains(owner)) teams.Add(team);
        }

        for (var slot = 0; slot < slots.Length; slot++)
        {
            var setup = slots[slot];
            var name = PlayerName(slot);
            var allies = new List<string>();
            var enemies = new List<string>();
            for (var other = 0; other < slots.Length; other++)
            {
                if (other == slot) continue;
                (setup.Team == 0 || slots[other].Team != setup.Team ? enemies : allies).Add(PlayerName(other));
            }
            sides.Add(new MapSide(new MapProperties(
            [
                Text("playerName", name), Flag("playerIsHuman", setup.IsHuman), Text("playerDisplayName", name), Text("playerFaction", setup.Faction),
                Text("playerAllies", string.Join(' ', allies)), Text("playerEnemies", string.Join(' ', enemies)), Number("multiplayerStartIndex", setup.StartIndex),
            ]), []));
            teams.Add(new MapProperties([Text("teamName", "team" + name), Text("teamOwner", name), Flag("teamIsSingleton", true)]));

            if (!setup.IsHuman && !setup.Scripted)
            {
                lists.Add(Empty);
                continue;
            }
            var qualifier = setup.StartIndex.ToString(CultureInfo.InvariantCulture);
            // A human gets the scripts of the civilian template side, a computer those of the template side of its own side (Player::initFromDict).
            var wantedSide = setup.IsHuman ? "Civilian" : setup.Side;
            var templateIndex = setup.IsHuman
                ? -1
                : templates.FindIndex(template => string.Equals(sideOfFaction(template.Faction), wantedSide, StringComparison.Ordinal));
            if (setup.IsHuman)
            {
                // The civilian template side comes from the library when it was attached (its script list replaces the map's civilian one for this purpose only).
                var civilianName = civilians.Count > 0 ? civilians[0].Side.Properties.GetText("playerName") ?? string.Empty : string.Empty;
                var source = attached && library!.Players.IndexOf(library.Players.FirstOrDefault(player => player.Name == civilianName)!) is var position and >= 0 && position < library.ScriptLists.Length
                    ? library.ScriptLists[position]
                    : Empty;
                lists.Add(Qualify(source, qualifier, civilianName + qualifier, name));
                continue;
            }
            (string Name, string Faction, MapScriptList Scripts) template;
            if (templateIndex >= 0) template = templates[templateIndex];
            else if (libraryForMissingSides && attached && LibraryTemplate(library!, setup.Side) is { } fromLibrary)
            {
                template = fromLibrary;
                notes.Add($"{name}: the map has no skirmish template side for side '{setup.Side}'; by option the player takes the library's '{fromLibrary.Name}' (not what the reference does)");
            }
            else
            {
                notes.Add($"{name}: the map has no skirmish template side for side '{setup.Side}'; the player runs without scripts");
                lists.Add(Empty);
                continue;
            }
            lists.Add(Qualify(template.Scripts, qualifier, template.Name + qualifier, name));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var team in templateTeams)
            {
                if (team.GetText("teamOwner") != template.Name) continue;
                var teamName = (team.GetText("teamName") ?? string.Empty) + qualifier;
                // "team" + template name is the template's default team; the player has its own.
                if (!seen.Add(teamName)) continue;
                teams.Add(QualifyTeam(team, teamName, name, qualifier));
            }
        }

        foreach (var (side, scripts) in civilians)
        {
            sides.Add(side);
            lists.Add(scripts);
        }
        report = new SkirmishAttachmentReport(notes.ToImmutable(), attached);
        return map with { Sides = sides.ToImmutable(), Teams = teams.ToImmutable(), PlayerScripts = lists.ToImmutable() };
    }

    public static string PlayerName(int slot) => "player" + slot.ToString(CultureInfo.InvariantCulture);

    private static MapScriptList Empty { get; } = new([], []);

    private static MapProperty Text(string key, string value) => new(key, MapPropertyType.Ascii, 0, 0m, value);
    private static MapProperty Flag(string key, bool value) => new(key, MapPropertyType.Bool, value ? 1 : 0, 0m, string.Empty);
    private static MapProperty Number(string key, int value) => new(key, MapPropertyType.Int, value, 0m, string.Empty);
    private static MapProperties Dict(params (string Key, string Value)[] items) => new([..items.Select(item => Text(item.Key, item.Value))]);

    /// <summary>The keys of a team dict that name scripts (<c>Player::initFromDict</c>): each gets the qualifier appended, like the scripts themselves.</summary>
    private static readonly string[] TeamScriptKeys =
        ["teamOnCreateScript", "teamOnIdleScript", "teamOnUnitDestroyedScript", "teamOnDestroyedScript", "teamEnemySightedScript", "teamAllClearScript", "teamProductionCondition"];

    private static MapProperties QualifyTeam(MapProperties team, string newName, string owner, string qualifier)
    {
        var items = ImmutableArray.CreateBuilder<MapProperty>(team.Items.Length);
        foreach (var item in team.Items)
        {
            if (item.Key == "teamName") items.Add(item with { Text = newName });
            else if (item.Key == "teamOwner") items.Add(item with { Text = owner });
            else if (item.Text.Length > 0 && (Array.IndexOf(TeamScriptKeys, item.Key) >= 0 || item.Key.StartsWith("teamGenericScriptHook", StringComparison.Ordinal))) items.Add(item with { Text = item.Text + qualifier });
            else items.Add(item);
        }
        return new MapProperties(items.MoveToImmutable());
    }

    /// <summary><c>ScriptList::duplicateAndQualify</c>: every group and script name gets the qualifier, and so do the names inside the parameters (<c>Parameter::qualify</c>).</summary>
    private static MapScriptList Qualify(MapScriptList list, string qualifier, string templatePlayerName, string newPlayerName)
    {
        MapScript Script(MapScript script) => script with
        {
            Name = script.Name + qualifier,
            OrConditions = [..script.OrConditions.Select(or => new MapOrCondition([..or.AndConditions.Select(condition => condition with { Parameters = Parameters(condition.Parameters) })]))],
            Actions = [..script.Actions.Select(action => action with { Parameters = Parameters(action.Parameters) })],
            FalseActions = [..script.FalseActions.Select(action => action with { Parameters = Parameters(action.Parameters) })],
        };
        ImmutableArray<MapScriptParameter> Parameters(ImmutableArray<MapScriptParameter> parameters)
        {
            var builder = ImmutableArray.CreateBuilder<MapScriptParameter>(parameters.Length);
            foreach (var parameter in parameters)
            {
                switch (parameter.Type)
                {
                    case MapScriptParameterType.Side:
                        builder.Add(parameter.Text + qualifier == templatePlayerName ? parameter with { Text = newPlayerName } : parameter);
                        break;
                    case MapScriptParameterType.Team when parameter.Text == ThisTeam:
                        builder.Add(parameter);
                        break;
                    case MapScriptParameterType.Team:
                    case MapScriptParameterType.Script:
                    case MapScriptParameterType.Counter:
                    case MapScriptParameterType.Flag:
                    case MapScriptParameterType.ScriptSubroutine:
                        builder.Add(parameter with { Text = parameter.Text + qualifier });
                        break;
                    default:
                        builder.Add(parameter);
                        break;
                }
            }
            return builder.MoveToImmutable();
        }
        return new MapScriptList([..list.Scripts.Select(Script)], [..list.Groups.Select(group => group with { Name = group.Name + qualifier, Scripts = [..group.Scripts.Select(Script)] })]);
    }
}
