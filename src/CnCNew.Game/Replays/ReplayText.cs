using System.Globalization;

namespace CnCNew.Game.Replays;

/// <summary>
/// The words of the replay list and the playback, in this project's English (the original has no text for an outcome, a desync or a compatibility state of a replay).
/// Every ending has words of its own; a value without them throws.
/// </summary>
public static class ReplayText
{
    /// <summary>The title of the message that says why a replay does not play.</summary>
    public const string CannotPlayTitle = "Cannot Play Replay";

    /// <summary>The title of the message that says why a name was refused.</summary>
    public const string CannotRenameTitle = "Cannot Rename Replay";

    /// <summary>The title of the message over the score screen of a playback.</summary>
    public const string PlaybackTitle = "Replay";

    /// <summary>The title of the popup that renames a replay (the original's <c>PopupReplay.wnd</c>, which named the last replay after a match).</summary>
    public const string RenameTitle = "Rename Replay";

    /// <summary>The label of the list's third button: the original copied a replay to the desktop, here it renames it.</summary>
    public const string RenameButton = "Rename";
    public const string AnalysisButton = "Analysis";
    public const string CannotAnalyseTitle = "Cannot Analyse Replay";

    /// <summary>A length of game time as <c>m:ss</c> (or <c>h:mm:ss</c> from an hour on).</summary>
    public static string Duration(long seconds)
        => seconds >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");

    /// <summary>The date of the recording as the list shows it (<c>2026-10-16 14:23</c>, UTC as written).</summary>
    public static string Date(string recorded)
        => DateTimeOffset.TryParse(recorded, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : recorded;

    /// <summary>How the recording ended, in a few words: who won, the time limit, or why it ended otherwise (a desync, the connection, the server, a player who left).</summary>
    public static string EndingLine(MatchRecordingInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var at = Duration(info.TickRate > 0 ? info.Ending.Tick / info.TickRate : 0);
        return info.Ending.Kind switch
        {
            ReplayEndingKind.Completed => info.Winner == 0 ? "No winner (time limit)" : $"{PlayerName(info, info.Winner)} won",
            ReplayEndingKind.Desync => $"Out of sync at {at}",
            ReplayEndingKind.ServerEnded => $"Ended by the server at {at}",
            ReplayEndingKind.ConnectionLost => $"Connection lost at {at}",
            ReplayEndingKind.Left => $"Left the match at {at}",
            ReplayEndingKind.Abandoned => $"Not finished (stopped at {at})",
            _ => throw new ArgumentOutOfRangeException(nameof(info), info.Ending.Kind, "No words for this ending."),
        };
    }

    /// <summary>A player of the recording as the list names it: the person's name or the faction, and who it was.</summary>
    public static string PlayerName(MatchRecordingInfo info, int player)
    {
        var entry = info.Players.FirstOrDefault(candidate => candidate.Player == player);
        if (entry is null) return string.Create(CultureInfo.InvariantCulture, $"Player {player}");
        var faction = entry.Faction.StartsWith("Faction", StringComparison.Ordinal) && entry.Faction.Length > "Faction".Length ? entry.Faction["Faction".Length..] : entry.Faction;
        return entry.Name.Length > 0 ? $"{entry.Name} ({faction})" : faction;
    }

    /// <summary>The players of the recording in one line: each with its faction, and who was a computer and who played at this game.</summary>
    public static string PlayersLine(MatchRecordingInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return string.Join(", ", info.Players.Select(player =>
            PlayerName(info, player.Player) + (player.IsComputer ? " (AI)" : player.Player == info.LocalPlayer ? " (you)" : string.Empty)));
    }

    /// <summary>The lines beside the list for the chosen replay: when, where, who, how long, how it ended - and why it does not play, if it does not.</summary>
    public static string Details(ReplayEntry entry, string mapName)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Info is not { } info) return $"{entry.Name}\n{entry.Compatibility.Reason}";
        var lines = new List<string>
        {
            entry.Name,
            $"{Date(info.Recorded)}, {(info.Source == "network" ? "on a server" : "local")}",
            mapName,
            $"Length {Duration(info.DurationSeconds)}",
            EndingLine(info),
            PlayersLine(info),
        };
        if (info.Ending.Detail.Length > 0) lines.Add(info.Ending.Detail);
        if (!entry.Compatibility.IsPlayable) lines.Add(entry.Compatibility.Reason);
        return string.Join("\n", lines);
    }
}
