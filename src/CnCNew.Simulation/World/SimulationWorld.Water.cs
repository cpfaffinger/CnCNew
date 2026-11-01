using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// The level of the water of a map, which scripts change (<c>TerrainLogic::setWaterHeight</c>, <c>changeWaterHeightOverTime</c>, <c>TerrainLogic::update</c>; F-25). The areas are
/// those of the terrain, in its order; their levels live here because the terrain is shared and never changes. A level is a whole number, as a water polygon stores it in its
/// integer points. A change writes nothing while the map's own levels stand, so a world that never raises its water keeps its state and hash. A change over time moves once per
/// logic frame of the original, at the first tick of the frame, at every rate.
/// </summary>
public sealed partial class SimulationWorld
{
    private const int MaxDynamicWater = 64;
    private static readonly Fixed FramesPerSecond = Fixed.FromInt(CnCNew.Data.OriginalFrame.PerSecond);

    /// <summary>The levels by the index of <see cref="TerrainMap.WaterAreas"/>; null while they are the map's.</summary>
    private Fixed[]? _waterLevels;

    private readonly List<WaterChange> _waterChanges = [];

    private sealed class WaterChange
    {
        public int Area;
        public Fixed ChangePerFrame;
        public Fixed Target;
        public Fixed Damage;
        public Fixed Current;
    }

    /// <summary><c>TerrainLogic::getWaterHeight</c> of the water at a point, with what scripts did to it.</summary>
    public bool TryWaterLevel(Fixed x, Fixed y, out Fixed level)
    {
        level = Fixed.Zero;
        return Terrain is not null && Terrain.TryWaterLevel(x, y, _waterLevels, out level);
    }

    /// <summary><c>TerrainLogic::isUnderwater</c>: water at the point and the terrain below its level.</summary>
    public bool IsUnderwater(Fixed x, Fixed y) => Terrain is not null && TryWaterLevel(x, y, out var level) && Terrain.HeightAt(x, y) < level;

    /// <inheritdoc cref="Modules.IWorldServices.SurfaceHeightAt"/>
    public Fixed SurfaceHeightAt(Fixed x, Fixed y) => TryWaterLevel(x, y, out var level) && Terrain!.HeightAt(x, y) < level ? level : GroundHeightAt(x, y);

    /// <summary>The level of an area of <see cref="TerrainMap.WaterAreas"/> now.</summary>
    public Fixed WaterLevelOf(int area) => _waterLevels?[area] ?? Terrain!.WaterAreas[area].Level;

    private int WaterAreaNamed(string name)
    {
        if (Terrain is null) return -1;
        for (var index = 0; index < Terrain.WaterAreas.Length; index++)
            if (string.Equals(Terrain.WaterAreas[index].Name, name, StringComparison.Ordinal)) return index;
        return -1;
    }

    /// <summary><c>doWaterChangeHeight</c>: at once, with the original's damage for what the water closes over.</summary>
    public void SetWaterHeight(string name, Fixed height)
    {
        if (WaterAreaNamed(name) is var area and >= 0) SetWaterHeight(area, height, Fixed.FromInt(999999), forceRecalculation: true);
    }

    /// <summary><c>changeWaterHeightOverTime</c>: a new change of an area replaces the one it had.</summary>
    public void ChangeWaterHeightOverTime(string name, Fixed height, Fixed seconds, Fixed damage)
    {
        if (WaterAreaNamed(name) is not (var area and >= 0)) return;
        _waterChanges.RemoveAll(change => change.Area == area);
        if (_waterChanges.Count >= MaxDynamicWater) return;
        var current = WaterLevelOf(area);
        // A transition of no time ends with the next frame, as the original's infinite step does.
        var change = seconds > Fixed.Zero ? (height - current) / (FramesPerSecond * seconds) : height - current;
        _waterChanges.Add(new WaterChange { Area = area, ChangePerFrame = change, Target = height, Damage = damage, Current = current });
    }

    /// <summary><c>TerrainLogic::update</c>: the changes are worked from the last to the first; damage while the water rises only once per second, and with the last step in any case.</summary>
    private void StepWater(long tick)
    {
        if (_waterChanges.Count == 0 || !Time.IsOriginalFrameBoundary(tick)) return;
        var damageThisFrame = tick % Time.TicksPerSecond == 0;
        for (var index = _waterChanges.Count - 1; index >= 0; index--)
        {
            var change = _waterChanges[index];
            var last = change.ChangePerFrame > Fixed.Zero ? change.Current + change.ChangePerFrame >= change.Target : change.Current + change.ChangePerFrame <= change.Target;
            if (last)
            {
                SetWaterHeight(change.Area, change.Target, change.Damage, forceRecalculation: true);
                _waterChanges.RemoveAt(index);
                continue;
            }
            change.Current += change.ChangePerFrame;
            SetWaterHeight(change.Area, change.Current, damageThisFrame ? change.Damage : Fixed.Zero, forceRecalculation: false);
        }
    }

    /// <summary><c>TerrainLogic::setWaterHeight</c>: the pathfinder learns of a change of the level, and water that rises hurts what is under it now.</summary>
    private void SetWaterHeight(int area, Fixed height, Fixed damage, bool forceRecalculation)
    {
        var terrain = Terrain!;
        var previous = WaterLevelOf(area);
        var level = Fixed.FromInt(height >= Fixed.Zero ? height.Floor() : -(-height).Floor());
        _waterLevels ??= [.. terrain.WaterAreas.Select(water => water.Level)];
        _waterLevels[area] = level;
        if (forceRecalculation || previous != level) ReclassifyWater(area);
        if (damage <= Fixed.Zero || level <= previous) return;

        var (minX, minY, maxX, maxY) = terrain.BoundsOf(area);
        var (centerX, centerY) = (Fixed.FromInt(minX + maxX) / Fixed.FromInt(2), Fixed.FromInt(minY + maxY) / Fixed.FromInt(2));
        // The largest distance is the diagonal of the bounding region.
        var (spanX, spanY) = ((long)(maxX - minX), (long)(maxY - minY));
        var reach = SquaredReach.Of(Fixed.Sqrt(Fixed.FromInt(spanX * spanX + spanY * spanY)));
        foreach (var entity in _entities.Values.ToArray())
        {
            var (dx, dy) = (entity.X - centerX, entity.Y - centerY);
            if (!reach.Contains(dx * dx + dy * dy) || !IsUnderwater(entity.X, entity.Y)) continue;
            Damage(entity.Id, new DamageInfo(EntityId.Invalid, damage, DamageTypes.Water, DeathTypes.Normal, false));
        }
    }

    /// <summary>The cells the area can reach tell the grid whether they are water now; <c>Pathfinder::forceMapRecalculation</c> does this for the whole map.</summary>
    private void ReclassifyWater(int area)
    {
        if (Navigation is not { } grid || Terrain is not { } terrain) return;
        var (minX, minY, maxX, maxY) = terrain.BoundsOf(area);
        var (lastX, lastY) = (Math.Min(terrain.CellsX - 1, maxX / TerrainMap.CellSize + 1), Math.Min(terrain.CellsY - 1, maxY / TerrainMap.CellSize + 1));
        for (var cy = Math.Max(0, minY / TerrainMap.CellSize - 1); cy <= lastY; cy++)
            for (var cx = Math.Max(0, minX / TerrainMap.CellSize - 1); cx <= lastX; cx++)
                grid.SetWater(cx, cy, terrain.IsWaterCell(cx, cy, _waterLevels));
    }

    private void DescribeWater(IStateWriter writer)
    {
        if (_waterLevels is not null)
        {
            writer.BeginArray("waterLevels");
            foreach (var level in _waterLevels)
            {
                writer.BeginObject(null);
                writer.Fixed("level", level);
                writer.EndObject();
            }
            writer.EndArray();
        }
        if (_waterChanges.Count == 0) return;
        writer.BeginArray("waterChanges");
        foreach (var change in _waterChanges)
        {
            writer.BeginObject(null);
            writer.Int("area", change.Area);
            writer.Fixed("step", change.ChangePerFrame);
            writer.Fixed("target", change.Target);
            writer.Fixed("damage", change.Damage);
            writer.Fixed("current", change.Current);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadWater(IStateReader reader)
    {
        if (reader.Has("waterLevels"))
        {
            _waterLevels = [.. reader.Array("waterLevels").Select(entry => entry.Fixed("level"))];
            for (var area = 0; area < _waterLevels.Length; area++) ReclassifyWater(area);
        }
        if (!reader.Has("waterChanges")) return;
        foreach (var entry in reader.Array("waterChanges"))
            _waterChanges.Add(new WaterChange { Area = checked((int)entry.Int("area")), ChangePerFrame = entry.Fixed("step"), Target = entry.Fixed("target"), Damage = entry.Fixed("damage"), Current = entry.Fixed("current") });
    }
}
