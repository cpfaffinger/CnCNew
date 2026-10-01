using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Game.Campaign;
using CnCNew.Game.Data;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Live;

/// <summary>
/// A running campaign mission behind a <see cref="LiveGame"/>: the same projected frames, fog, camera, minimap and effects a skirmish has, so the HUD
/// (<c>MatchController</c>: command bar, production, selection, orders) plays it. The mission's own script presentation (scripted camera moves, captions, radar
/// events) runs through <see cref="Director"/>, selections reach <c>NAMED_SELECTED</c> through <see cref="Bridge"/>; the human's orders go through the session's
/// per-player sequence counter like the scripts' own.
/// <para>
/// A mission is not a replay: scripts change the world directly, so the truth is <see cref="MissionSnapshot"/> (<c>docs/CAMPAIGN-AND-SAVEGAMES.md</c>).
/// </para>
/// </summary>
public sealed class LiveMission : LiveGame
{
    private ICommandSink? _commands;
    private readonly string _humanFaction;
    private readonly ImmutableArray<PlayerConfiguration> _players;

    private LiveMission(GameData data, MissionRun run, PlayerId human, string humanFaction, ImmutableArray<PlayerConfiguration> players, Func<string, string>? resolveText, Vector2 viewport)
        : base(data, run.Session.World, run.Map, run.MapPath, run.Terrain, players, human, run.Session.Options.WorldSeed, viewport, CameraStart(run, human))
    {
        Run = run;
        _humanFaction = humanFaction;
        _players = players;
        Director = new MissionPresentationDirector(run.Session, resolveText);
        Bridge = new MissionUiBridge(run.Session);
    }

    public MissionRun Run { get; }
    public MissionSession Session => Run.Session;

    /// <summary>The scripted presentation of the mission (captions, camera moves, radar blips); the host draws <see cref="MissionPresentationDirector.BuildOverlayCommands"/>.</summary>
    public MissionPresentationDirector Director { get; }

    /// <summary>Reports the human's selection and UI interactions back to the scripts (<c>NAMED_SELECTED</c>, <c>FLAG</c> after a UI action).</summary>
    public MissionUiBridge Bridge { get; }

    public MissionOutcome Outcome => Session.Outcome;

    public override string HumanFaction => _humanFaction;
    public override bool IsOver => Session.IsOver;

    /// <summary>The viewer wins a mission it won; a defeat or a running mission has no winner (the enemy sides are scripted, not a competing human).</summary>
    public override PlayerId? Winner => Session.Outcome == MissionOutcome.Victory ? Human : null;

    public override ImmutableArray<PlayerConfiguration> Players => _players;

    public override ICommandSink Commands => _commands ??= new MissionCommandSink(Session, Human);

    /// <summary>One tick: the director completes due presentations, the mission steps (scripts, combat, world), the director applies what the scripts asked for.</summary>
    protected override TickResult StepSimulation()
    {
        Director.BeforeStep();
        var result = Session.Step();
        Director.AfterStep();
        return result;
    }

    protected override void OnAdvanced() => Director.ApplyCameraTo(Camera);

    public static LiveMission Create(GameData data, MissionRun run, Vector2 viewport, Func<string, string>? resolveText = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(run);
        var human = MissionSession.LocalPlayerOf(run.Session.Program, run.Session.Options.HumanSideName);
        var players = ImmutableArray.CreateBuilder<PlayerConfiguration>();
        var faction = string.Empty;
        foreach (var player in run.Session.Program.Players)
        {
            if (player.Id.IsSystem) continue;
            players.Add(new PlayerConfiguration(player.Id, player.Name, player.Faction.Length > 0 ? player.Faction : null, 0, player.Id != human));
            if (player.Id == human) faction = player.Faction;
        }
        return new LiveMission(data, run, human, faction, players.ToImmutable(), resolveText, viewport);
    }

    /// <summary>Where the camera starts: over the viewer's objects (their centroid), else the middle of the map.</summary>
    private static Vector2 CameraStart(MissionRun run, PlayerId human)
    {
        long count = 0;
        double x = 0, y = 0;
        foreach (var entity in run.Session.World.Entities)
        {
            if (entity.Owner != human || entity.EffectivelyDead) continue;
            x += entity.X.Raw / 65536.0;
            y += entity.Y.Raw / 65536.0;
            count++;
        }
        if (count > 0) return new Vector2((float)(x / count), (float)(y / count));
        return new Vector2(run.Terrain.CellsX * TerrainMap.CellSize / 2f, run.Terrain.CellsY * TerrainMap.CellSize / 2f);
    }
}
