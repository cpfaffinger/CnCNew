using System.Numerics;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;

namespace CnCNew.Presentation.Spectator;

/// <summary>What a computer player's order says about where it is "looking"; the order of the members is the order of their weight (later wins).</summary>
public enum AiInterestKind
{
    /// <summary>Nothing ordered yet: the start position.</summary>
    Home,
    /// <summary>A supply truck or worker was sent to a dock (<c>DockCommand</c>): the dock.</summary>
    Supply,
    /// <summary>A single unit or a small group was moved (<c>SetMoveTargetCommand</c>, <c>MoveAndEnterCommand</c>, <c>EvacuateCommand</c>, fewer attack-move orders than a wave).</summary>
    Move,
    /// <summary>An upgrade was bought at a building (<c>PurchaseUpgradeCommand</c> with a target): the building. A science has no place and moves nothing.</summary>
    Research,
    /// <summary>A unit was queued (<c>QueueProductionCommand</c>): the production building.</summary>
    Production,
    /// <summary>A builder was sent to a site or a damaged building, or a building is taken over or sold (<c>ResumeConstructCommand</c>, <c>RepairCommand</c>, <c>CaptureCommand</c>, <c>SellCommand</c>).</summary>
    Upkeep,
    /// <summary>A building was placed (<c>ConstructCommand</c>): the construction site.</summary>
    Construction,
    /// <summary>A special power was used (<c>UseSpecialPowerCommand</c>): its target, or its source without one.</summary>
    Power,
    /// <summary>A wave was sent (<see cref="AiCameraSettings.WaveUnits"/> or more attack-move orders in one tick): the centre of the ordered units, followed while it holds.</summary>
    Attack,
}

/// <param name="Position">World x/y the interest points at when it was derived.</param>
/// <param name="Tick">The tick of the order.</param>
/// <param name="Subject">The object the position came from (invalid for a ground position or a group).</param>
/// <param name="Units">How many units the order addressed (a wave's size; 1 otherwise).</param>
public readonly record struct AiInterest(AiInterestKind Kind, Vector2 Position, long Tick, EntityId Subject, int Units);

/// <summary>
/// The tuning of <see cref="AiCamera"/>. Every duration is in original frames (1/30 s of game time), so the camera behaves the same at every simulation rate.
/// The values are this client's own choice; the original has no camera for a computer player.
/// </summary>
public sealed record AiCameraSettings
{
    /// <summary>Attack-move orders of one tick that make a wave.</summary>
    public int WaveUnits { get; init; } = 3;

    /// <summary>Time constant of the glide towards the interest (half a second).</summary>
    public int SmoothingFrames { get; init; } = 15;

    /// <summary>Further than this the camera cuts instead of gliding (a player would press a hotkey or click the radar).</summary>
    public float CutDistance { get; init; } = 600f;

    /// <summary>How long an interest holds the camera against interests of the same or a lower weight.</summary>
    public int DwellFrames(AiInterestKind kind) => kind switch
    {
        AiInterestKind.Attack => 180,
        AiInterestKind.Power => 150,
        AiInterestKind.Construction => 120,
        AiInterestKind.Upkeep => 90,
        AiInterestKind.Production => 90,
        AiInterestKind.Research => 60,
        AiInterestKind.Move => 60,
        AiInterestKind.Supply => 60,
        _ => 0,
    };
}

/// <summary>
/// The simulated camera of one computer player: a point of interest derived from the orders the player gives, with a dwell time per kind of interest and a smoothed
/// look-at position. A computer player has no camera; this one answers "where would a player have to look to give these orders" so a spectator can follow it.
/// <para>
/// It is a pure function of the orders and of the object positions it is given: <see cref="Observe"/> takes the orders of one tick and picks the heaviest one
/// (<see cref="AiInterestKind"/>); it replaces the current interest when it is heavier, when the current one has held the camera for its dwell time, or when it continues
/// the current one (the next orders of a wave under way). <see cref="Advance"/> moves the look-at position once per tick. Nothing here writes to the simulation.
/// </para>
/// </summary>
public sealed class AiCamera
{
    private readonly AiCameraSettings _settings;
    private readonly int _ticksPerFrame;
    private readonly float _blend;
    private readonly List<EntityId> _followed = [];
    private readonly SortedDictionary<AiInterestKind, int> _counts = [];
    private Vector2 _target;

    /// <param name="ticksPerOriginalFrame">Ticks of the watched world in one original frame (<c>world.Time.TicksPerOriginalFrame</c>).</param>
    public AiCamera(PlayerId player, Vector2 home, int ticksPerOriginalFrame, AiCameraSettings? settings = null)
    {
        if (ticksPerOriginalFrame < 1) throw new ArgumentOutOfRangeException(nameof(ticksPerOriginalFrame));
        Player = player;
        Home = home;
        _settings = settings ?? new AiCameraSettings();
        _ticksPerFrame = ticksPerOriginalFrame;
        // The same exponential glide at every rate: the share of the remaining distance covered in one tick.
        _blend = 1f - MathF.Exp(-1f / (Math.Max(1, _settings.SmoothingFrames) * (float)ticksPerOriginalFrame));
        Interest = new AiInterest(AiInterestKind.Home, home, 0, EntityId.Invalid, 0);
        Position = _target = home;
    }

    public PlayerId Player { get; }
    public Vector2 Home { get; }

    /// <summary>The interest that holds the camera.</summary>
    public AiInterest Interest { get; private set; }

    /// <summary>Where the camera looks now (world x/y): gliding towards <see cref="Target"/>.</summary>
    public Vector2 Position { get; private set; }

    /// <summary>Where the current interest is now (a wave's centre moves with its units).</summary>
    public Vector2 Target => _target;

    /// <summary>Orders of this player that carried an interest.</summary>
    public int OrdersSeen { get; private set; }

    /// <summary>How often the interest changed.</summary>
    public int Switches { get; private set; }

    /// <summary>How often the camera cut instead of gliding.</summary>
    public int Cuts { get; private set; }

    /// <summary>Interests taken, by kind.</summary>
    public IReadOnlyDictionary<AiInterestKind, int> SwitchesByKind => _counts;

    /// <summary>The orders of one tick (any players'; only this player's count). <paramref name="positionOf"/> gives the ground position of a living object, or null.</summary>
    public void Observe(long tick, IEnumerable<SimCommand> commands, Func<EntityId, Vector2?> positionOf)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(positionOf);
        AiInterest? best = null;
        List<EntityId>? marchers = null;
        var marchSum = Vector2.Zero;
        foreach (var command in commands)
        {
            if (command.Player != Player) continue;
            if (command is AttackMoveCommand march)
            {
                if (positionOf(march.Unit) is not { } at) continue;
                (marchers ??= []).Add(march.Unit);
                marchSum += at;
                OrdersSeen++;
                continue;
            }
            if (Derive(command, tick, positionOf) is not { } interest) continue;
            OrdersSeen++;
            if (best is null || interest.Kind >= best.Value.Kind) best = interest;
        }
        if (marchers is not null)
        {
            var kind = marchers.Count >= _settings.WaveUnits ? AiInterestKind.Attack : AiInterestKind.Move;
            var march = new AiInterest(kind, marchSum / marchers.Count, tick, marchers.Count == 1 ? marchers[0] : EntityId.Invalid, marchers.Count);
            if (best is null || march.Kind >= best.Value.Kind) best = march;
            else marchers = null;
        }
        if (best is not { } candidate) return;

        var held = tick - Interest.Tick;
        var continues = candidate.Kind == Interest.Kind && (candidate.Kind == AiInterestKind.Attack || (candidate.Subject.IsValid && candidate.Subject == Interest.Subject));
        if (!continues && candidate.Kind <= Interest.Kind && held < (long)_settings.DwellFrames(Interest.Kind) * _ticksPerFrame) return;
        if (!continues)
        {
            Switches++;
            _counts[candidate.Kind] = _counts.GetValueOrDefault(candidate.Kind) + 1;
        }
        // A wave under way keeps the tick it began to hold the camera, so a lasting attack does not block every other interest for ever.
        Interest = continues ? candidate with { Tick = Interest.Tick } : candidate;
        _target = candidate.Position;
        _followed.Clear();
        if (candidate.Kind == AiInterestKind.Attack && marchers is not null) _followed.AddRange(marchers);
    }

    /// <summary>One tick: a followed wave's centre is read again and the look-at position glides (or cuts) towards the interest.</summary>
    public void Advance(Func<EntityId, Vector2?> positionOf)
    {
        ArgumentNullException.ThrowIfNull(positionOf);
        if (_followed.Count > 0)
        {
            var sum = Vector2.Zero;
            var alive = 0;
            foreach (var unit in _followed)
                if (positionOf(unit) is { } at)
                {
                    sum += at;
                    alive++;
                }
            if (alive > 0) _target = sum / alive;
            else _followed.Clear();
        }
        var offset = _target - Position;
        if (offset.Length() > _settings.CutDistance)
        {
            Position = _target;
            Cuts++;
        }
        else Position += offset * _blend;
    }

    private static AiInterest? Derive(SimCommand command, long tick, Func<EntityId, Vector2?> positionOf)
    {
        AiInterest? At(AiInterestKind kind, EntityId subject) => positionOf(subject) is { } position ? new AiInterest(kind, position, tick, subject, 1) : null;
        static Vector2 Ground(CnCNew.Simulation.Numerics.Fixed x, CnCNew.Simulation.Numerics.Fixed y) => new(FixedConvert.ToSingle(x), FixedConvert.ToSingle(y));
        return command switch
        {
            ConstructCommand construct => new AiInterest(AiInterestKind.Construction, Ground(construct.X, construct.Y), tick, EntityId.Invalid, 1),
            UseSpecialPowerCommand { TargetKind: SpecialPowerTargetKind.Location } power => new AiInterest(AiInterestKind.Power, Ground(power.X, power.Y), tick, EntityId.Invalid, 1),
            UseSpecialPowerCommand { TargetKind: SpecialPowerTargetKind.Object } power => At(AiInterestKind.Power, power.Target) ?? At(AiInterestKind.Power, power.Source),
            UseSpecialPowerCommand power => At(AiInterestKind.Power, power.Source),
            ResumeConstructCommand resume => At(AiInterestKind.Upkeep, resume.Site),
            RepairCommand repair => At(AiInterestKind.Upkeep, repair.Target),
            CaptureCommand capture => At(AiInterestKind.Upkeep, capture.Target),
            SellCommand sell => At(AiInterestKind.Upkeep, sell.Target),
            QueueProductionCommand queue => At(AiInterestKind.Production, queue.Producer),
            PurchaseUpgradeCommand { Target: { } building } => At(AiInterestKind.Research, building),
            SetMoveTargetCommand move => new AiInterest(AiInterestKind.Move, Ground(move.X, move.Y), tick, move.Target, 1),
            MoveAndEnterCommand enter => At(AiInterestKind.Move, enter.Container),
            EvacuateCommand evacuate => At(AiInterestKind.Move, evacuate.Container),
            DockCommand dock => At(AiInterestKind.Supply, dock.Dock),
            _ => null,
        };
    }
}
