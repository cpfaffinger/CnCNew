using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Terrain;

/// <summary>
/// The <c>TERRAIN_RESTRICTIONS</c> of <c>BuildAssistant::isLocationLegalToBuild</c>: the footprint is sampled at three cells and then at one (<c>iterateFootprint</c>); a sample off the map, on water,
/// on a cliff or closer to the map's edge than <c>MinDistFromEdgeOfMapForBuild</c> restricts the site (<c>checkSampleBuildLocation</c>), and the highest and the lowest sample may differ by
/// <c>AllowedHeightVariationForBuilding</c> at most. The coarse pass answers first, so a slope that the coarse samples see outranks a cliff only the fine ones reach.
/// </summary>
internal static class BuildSiteTerrain
{
    private static readonly Fixed Cell = Fixed.FromInt(TerrainMap.CellSize);

    public static LegalBuildCode Check(TerrainMap terrain, ObjectTemplate template, Fixed x, Fixed y, Fixed angle, BuildSiteRules rules)
    {
        var box = template.Geometry.Equals("BOX", StringComparison.OrdinalIgnoreCase);
        var samples = new Samples(terrain, x, y, FixedTrig.Cos(angle), FixedTrig.Sin(angle), box ? template.GeometryMajorRadius : template.BoundingCircleRadius,
            box ? template.GeometryMinorRadius : template.BoundingCircleRadius, box, rules.EdgeDistance);
        foreach (var resolution in new[] { Cell * Fixed.FromInt(3), Cell })
        {
            samples.Walk(resolution);
            if (samples.Restricted) return LegalBuildCode.RestrictedTerrain;
            // A small circle has no sample at three cells: its corners lie outside it, and nothing has been measured yet.
            if (samples.High >= samples.Low && samples.High - samples.Low > rules.HeightVariation) return LegalBuildCode.NotFlatEnough;
        }
        return LegalBuildCode.Ok;
    }

    private sealed class Samples(TerrainMap terrain, Fixed x, Fixed y, Fixed cos, Fixed sin, Fixed halfWidth, Fixed halfHeight, bool box, Fixed edge)
    {
        private readonly Fixed _limitX = Fixed.FromInt(terrain.CellsX * TerrainMap.CellSize) - edge;
        private readonly Fixed _limitY = Fixed.FromInt(terrain.CellsY * TerrainMap.CellSize) - edge;
        private readonly SquaredReach _circle = SquaredReach.Of(halfWidth);

        public bool Restricted { get; private set; }
        public Fixed High { get; private set; } = Fixed.MinValue;
        public Fixed Low { get; private set; } = Fixed.MaxValue;

        /// <summary><c>iterateFootprint</c>: "the sample process goes an extra sample resolution outside of the real extent" and snaps it back to the exact edge.</summary>
        public void Walk(Fixed step)
        {
            for (var alongY = -halfHeight; alongY < halfHeight + step; alongY += step)
            {
                if (alongY > halfHeight) alongY = halfHeight;
                for (var alongX = -halfWidth; alongX < halfWidth + step; alongX += step)
                {
                    if (alongX > halfWidth) alongX = halfWidth;
                    if (!box && !_circle.Contains(alongX * alongX + alongY * alongY)) continue;
                    Sample(x + alongX * cos - alongY * sin, y + alongX * sin + alongY * cos);
                }
            }
        }

        private void Sample(Fixed sampleX, Fixed sampleY)
        {
            var (cellX, cellY) = (TerrainMap.CellOf(sampleX), TerrainMap.CellOf(sampleY));
            if (!terrain.IsInside(cellX, cellY) || (terrain.CellAt(cellX, cellY) & (CellFlags.Cliff | CellFlags.Water)) != 0) Restricted = true;
            var height = terrain.HeightAt(sampleX, sampleY);
            if (height < Low) Low = height;
            if (height > High) High = height;
            if (edge > Fixed.Zero && (sampleX < edge || sampleX > _limitX || sampleY < edge || sampleY > _limitY)) Restricted = true;
        }
    }
}
