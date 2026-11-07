using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>
/// The debug options of a match (<c>MatchDebugOptions</c> of the match description, <c>docs/SIMULATION-CORE.md</c>): rules the original does not have, for testing. They are fixed at
/// tick 0 (<see cref="ConfigureMatchOptions"/>) and written into the world state only when they differ from the original game, so a match without them keeps its hashes.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>What everything built takes of its normal time, in percent (<see cref="ScaleBuildTicks"/>); 100 is the original.</summary>
    public int BuildTimePercent { get; private set; } = NormalBuildTimePercent;

    private const int NormalBuildTimePercent = 100;

    /// <summary>Seconds of game time after which a destroyed neutral building of the map comes back; 0 when it does not.</summary>
    public int NeutralRespawnSeconds { get; private set; }

    /// <summary>A neutral building the map placed, kept for its return (<see cref="StepNeutralRespawn"/>).</summary>
    private sealed class NeutralBuilding(EntityId id, int template, Fixed x, Fixed y, Fixed z, Fixed angle, MapObjectFlags flags, int attitude)
    {
        /// <summary>The building as it stands, or the one that stood until it was destroyed.</summary>
        public EntityId Id { get; set; } = id;
        public int Template { get; } = template;
        public Fixed X { get; } = x;
        public Fixed Y { get; } = y;
        public Fixed Z { get; } = z;
        public Fixed Angle { get; } = angle;
        public MapObjectFlags Flags { get; } = flags;
        public int Attitude { get; } = attitude;

        /// <summary>The tick the building comes back at, or -1 while it stands.</summary>
        public long Due { get; set; } = -1;
    }

    private readonly List<NeutralBuilding> _neutralBuildings = [];

    /// <summary>
    /// Sets the debug options before the first tick. Values of the original game leave the world as it is. The build time is a percentage of the normal time, the respawn a number of
    /// seconds of game time (0: off).
    /// </summary>
    public void ConfigureMatchOptions(int buildTimePercent, int neutralRespawnSeconds)
    {
        if (_tick != 0) throw new InvalidOperationException("The debug options of a match are set before its first tick.");
        if (buildTimePercent <= 0) throw new ArgumentOutOfRangeException(nameof(buildTimePercent));
        if (neutralRespawnSeconds < 0) throw new ArgumentOutOfRangeException(nameof(neutralRespawnSeconds));
        BuildTimePercent = buildTimePercent;
        NeutralRespawnSeconds = neutralRespawnSeconds;
    }

    /// <summary>
    /// A build time of <paramref name="ticks"/> (whole frames of the original) under <see cref="BuildTimePercent"/>: the frames times the percentage, rounded up, at least one frame.
    /// The normal time stays as it is. <c>calcTimeToBuild</c> and the build time of an upgrade take it before the power-supply ratio is applied.
    /// </summary>
    public long ScaleBuildTicks(long ticks)
    {
        if (BuildTimePercent == NormalBuildTimePercent || ticks <= 0) return ticks;
        var frames = Time.FramesIn(ticks);
        return Time.OriginalFrames(Math.Max(1, (frames * BuildTimePercent + NormalBuildTimePercent - 1) / NormalBuildTimePercent));
    }

    /// <summary>Called when a map object for the neutral player is queued: a building (not rubble from the start) is kept for its return.</summary>
    private void RecordNeutralBuilding(EntityId id, ObjectTemplate template, PlaceMapObjectCommand placed)
    {
        if (NeutralRespawnSeconds == 0 || !placed.Owner.IsSystem || placed.HealthPercent <= 0 || !template.IsKindOf("STRUCTURE")) return;
        _neutralBuildings.Add(new NeutralBuilding(id, template.Index, placed.X, placed.Y, placed.Z, placed.Angle, placed.Flags, placed.Attitude));
    }

    /// <summary>
    /// A neutral building that is destroyed - removed, rubble or without health - starts a countdown of <see cref="NeutralRespawnSeconds"/> of game time; at its end the old rubble is
    /// taken away and the building is made again like the map made it (its place, angle and properties, the neutral player, full health), unless something stands on the spot - then it
    /// is looked at again after a second. Runs before the spawn phase, so the building appears in the tick that decides it.
    /// </summary>
    private void StepNeutralRespawn(long tick)
    {
        var respawn = Time.WholeSecondsToTicks(NeutralRespawnSeconds);
        foreach (var building in _neutralBuildings)
        {
            if (_spawnQueue.Exists(pending => pending.Id == building.Id)) continue;
            var entity = FindEntity(building.Id);
            if (!IsDestroyedNeutral(entity))
            {
                building.Due = -1;
                continue;
            }
            if (building.Due < 0) building.Due = tick + respawn;
            if (tick < building.Due) continue;
            if (entity is not null)
            {
                // The rubble goes first; the spot is judged and the building made when it is gone.
                RequestDestroy(building.Id);
                continue;
            }
            var template = _templates[building.Template];
            if (!SiteIsFreeForRespawn(building, template))
            {
                building.Due = tick + Time.WholeSecondsToTicks(1);
                continue;
            }
            building.Id = Ids.AllocateEntity();
            building.Due = -1;
            _spawnQueue.Add(new PendingSpawn(building.Id, building.Template, PlayerId.System, building.X, building.Y, building.Z, building.Angle,
                new SpawnSetup(false, 100, false, 0, 0, 0, EntityId.Invalid, EntityId.Invalid, Fixed.Zero, EntityId.Invalid, MapFlags: building.Flags, Attitude: building.Attitude)));
        }
    }

    private static bool IsDestroyedNeutral(Entity? entity)
        => entity is null || entity.DestroyRequested || entity.IsRubble || (entity.Body is { } body && body.MaxHealth > Fixed.Zero && body.Health <= Fixed.Zero);

    /// <summary>Whether nothing that blocks a building stands on the spot: the dead, rubble, mines and inert objects do not (the old building is already gone).</summary>
    private bool SiteIsFreeForRespawn(NeutralBuilding building, ObjectTemplate template)
    {
        var ground = Navigation is { } navigation ? navigation.Terrain.HeightAt(building.X, building.Y) : Fixed.Zero;
        var site = new SiteShape(template, building.X, building.Y, building.Angle);
        var reach = template.BoundingCircleRadius;
        foreach (var them in _entities.Values)
        {
            if (FurtherThan(them.X, building.X, reach + them.Template.BoundingCircleRadius) || FurtherThan(them.Y, building.Y, reach + them.Template.BoundingCircleRadius)) continue;
            if (!Overlaps(site, ground, template, them)) continue;
            if (IsRemovableForConstruction(them) || them.IsRubble || them.HasStatus(NoCollisionsBit) || them.Template.IsKindOf("MINE") || them.Template.IsKindOf("INERT")) continue;
            return false;
        }
        return true;
    }

    private void DescribeMatchOptions(IStateWriter writer)
    {
        if (BuildTimePercent != NormalBuildTimePercent) writer.Int("buildTimePercent", BuildTimePercent);
        if (NeutralRespawnSeconds == 0) return;
        writer.Int("neutralRespawnSeconds", NeutralRespawnSeconds);
        writer.BeginArray("neutralBuildings");
        foreach (var building in _neutralBuildings)
        {
            writer.BeginObject(null);
            writer.UInt("id", building.Id.Value);
            writer.Text("template", _templates[building.Template].Name);
            writer.Fixed("x", building.X);
            writer.Fixed("y", building.Y);
            writer.Fixed("z", building.Z);
            writer.Fixed("angle", building.Angle);
            writer.Int("flags", (long)building.Flags);
            writer.Int("attitude", building.Attitude);
            writer.Int("due", building.Due);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadMatchOptions(IStateReader state)
    {
        BuildTimePercent = state.Has("buildTimePercent") ? checked((int)state.Int("buildTimePercent")) : NormalBuildTimePercent;
        NeutralRespawnSeconds = state.Has("neutralRespawnSeconds") ? checked((int)state.Int("neutralRespawnSeconds")) : 0;
        _neutralBuildings.Clear();
        if (NeutralRespawnSeconds == 0) return;
        foreach (var item in state.Array("neutralBuildings"))
        {
            if (!_templates.TryGet(item.Text("template"), out var template)) throw new InvalidDataException($"A neutral building of the snapshot names the unknown template '{item.Text("template")}'.");
            _neutralBuildings.Add(new NeutralBuilding(new EntityId(checked((uint)item.UInt("id"))), template.Index, item.Fixed("x"), item.Fixed("y"), item.Fixed("z"), item.Fixed("angle"),
                (MapObjectFlags)item.Int("flags"), checked((int)item.Int("attitude")))
            { Due = item.Int("due") });
        }
    }
}
