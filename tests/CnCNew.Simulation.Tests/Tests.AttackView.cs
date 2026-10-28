using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;

// F-55: the attack's view test. Step (a): the terrain line of BaseHeightMapRenderObjClass::isClearLineOfSight.
static partial class Tests
{
    public static void TerrainLineOfSightFollowsTheOriginal()
    {
        // 10 x 10 cells, border 1: world x in [10k, 10k+10) is vertex square k+1. Vertex column 5 is a ridge of 16 raw (10 m), so squares 4 and 5 are 10 high.
        var ridge = TerrainMap.Build(SyntheticMaps.Build(10, 10, 1, (x, _) => x == 5 ? 16 : 0), "ridge");
        static Fixed F(decimal value) => Fixed.FromDecimal(value);
        bool Clear(TerrainMap map, decimal x0, decimal z0, decimal x1, decimal z1) => map.IsClearLineOfSight(F(x0), F(50), F(z0), F(x1), F(50), F(z1));

        Check.That(!Clear(ridge, 5, 9.4m, 95, 9.4m), "A ridge 10 high blocks a level line at 9.4 (more than 0.5 above it).");
        Check.That(Clear(ridge, 5, 9.5m, 95, 9.5m), "At 9.5 the ridge is exactly 0.5 above the line: clear (LOS_FUDGE).");
        Check.That(!Clear(ridge, 95, 9.4m, 5, 9.4m), "The same line walked the other way is blocked as well.");
        Check.That(Clear(ridge, 5, 0, 5, 0) && Clear(ridge, 41, 0, 49, 0), "A line inside one square is clear, whatever stands there.");
        Check.That(!Clear(ridge, 41, 0, 95, 0), "The start square is tested: a line starting on the ridge is blocked.");
        Check.That(Clear(ridge, -50, 0, 95, 0), "A walk that starts off the field stops at once: clear (the original leaves the loop).");

        // The end square is never tested: vertex column 8 raised makes squares 7 and 8 high; a line ending in square 7 passes, one ending in square 8 does not.
        var wall = TerrainMap.Build(SyntheticMaps.Build(10, 10, 1, (x, _) => x == 8 ? 16 : 0), "wall");
        Check.That(Clear(wall, 5, 0, 65, 0), "A line ending in a high square: its end square is not tested.");
        Check.That(!Clear(wall, 5, 0, 75, 0), "One square further the high square is passed and blocks.");

        // The sight line climbs by an equal share per square: from 0 up to 30 over nine squares it is 13.3 at square 4 - above the ridge.
        Check.That(Clear(ridge, 5, 0, 95, 30), "A line that climbs over the ridge is clear.");
        Check.That(!Clear(ridge, 5, 0, 95, 15), "A line that climbs too slowly is blocked.");
    }
}
