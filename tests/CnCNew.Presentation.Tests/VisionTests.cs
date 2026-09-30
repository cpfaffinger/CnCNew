using System.Numerics;
using CnCNew.Presentation.Vision;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

internal static class VisionTests
{
    public static FogSettings Settings { get; } = new(0, 127, 255, 510f);

    public static int Run(string[] args)
    {
        var t = new TestHarness("Vision");
        t.Run("states from the vision map", States);
        t.Run("fading between updates", Fading);
        t.Run("world mapping and helpers", Mapping);
        t.Run("smoothed level texture", Smoothing);
        t.Run("argument checks", Checks);
        return t.Failures;
    }

    private static void States()
    {
        var vision = new VisionMap(8, 6, 2);
        vision.Reveal(0, Fixed.FromInt(25), Fixed.FromInt(25), Fixed.FromInt(10));
        var fog = FogOfWarGrid.ForVision(vision, Settings);
        fog.Update(vision, 0);
        Assert(fog.StateAt(2, 2) == FogCellState.Visible && fog.StateAt(1, 2) == FogCellState.Visible && fog.StateAt(0, 0) == FogCellState.Unexplored, "visible disc");
        vision.BeginRefresh();
        vision.Reveal(0, Fixed.FromInt(65), Fixed.FromInt(45), Fixed.FromInt(5));
        fog.Update(vision, 0);
        Assert(fog.StateAt(2, 2) == FogCellState.Fogged && fog.StateAt(6, 4) == FogCellState.Visible, "explored cells turn to fog");
        fog.Update(vision, 1);
        Assert(fog.StateAt(6, 4) == FogCellState.Unexplored, "other players see nothing");
        Assert(fog.StateAt(-1, 0) == FogCellState.Unexplored && fog.LevelAt(99, 0) == 0, "outside the grid is shroud");
    }

    private static void Fading()
    {
        var vision = new VisionMap(2, 1, 1);
        var fog = FogOfWarGrid.ForVision(vision, Settings);
        Assert(fog.LevelAt(0, 0) == 0, "starts shrouded");
        vision.Reveal(0, Fixed.FromInt(5), Fixed.FromInt(5), Fixed.FromInt(3));
        fog.Update(vision, 0);
        fog.Advance(0.25f);
        Assert(fog.LevelAt(0, 0) == 128 && fog.LevelAt(1, 0) == 0, "510 levels per second: halfway after 0.25 s");
        fog.Advance(0.5f);
        Assert(fog.LevelAt(0, 0) == 255, "clamped at the clear level");
        vision.BeginRefresh();
        fog.Update(vision, 0);
        fog.Advance(0.1f);
        Assert(fog.LevelAt(0, 0) == 204 && fog.StateAt(0, 0) == FogCellState.Fogged, "fades down towards fog");
        fog.Advance(10f);
        Assert(fog.LevelAt(0, 0) == 127, "stops at the fog level");
        fog.SetStates([FogCellState.Visible, FogCellState.Fogged]);
        fog.Snap();
        Span<byte> levels = stackalloc byte[2];
        fog.CopyLevels(levels);
        Assert(levels[0] == 255 && levels[1] == 127, "snap jumps to the targets");
    }

    private static void Mapping()
    {
        var fog = new FogOfWarGrid(10, 10, Settings);
        fog.SetStates(Enumerable.Range(0, 100).Select(index => index == 3 * 10 + 4 ? FogCellState.Visible : FogCellState.Fogged).ToArray());
        Assert(fog.IsVisibleAt(new Vector2(45, 35)) && !fog.IsVisibleAt(new Vector2(45, 45)) && fog.IsExploredAt(new Vector2(99, 99)), "world lookups");
        Assert(FogOfWarGrid.CellOf(39.99f) == 3 && FogOfWarGrid.CellOf(40f) == 4 && FogOfWarGrid.CellOf(-0.1f) == -1, "cell of a coordinate");
        Assert(FogOfWarGrid.CellCenter(4, 3) == new Vector2(45, 35), "cell centre");
        Assert(!fog.IsExploredAt(new Vector2(-5, 5)), "outside is unexplored");
    }

    private static void Smoothing()
    {
        var fog = new FogOfWarGrid(2, 1, Settings);
        fog.SetStates([FogCellState.Unexplored, FogCellState.Visible]);
        fog.Snap();
        var texture = fog.BuildSmoothedLevels(4);
        // Texel centres at cell coordinates -0.375, -0.125, 0.125, 0.375 (clamped) then 0.625 ... relative to cell 0's centre.
        Assert(texture.Length == 32 && texture.Take(8).SequenceEqual(new byte[] { 0, 0, 32, 96, 159, 223, 255, 255 }) && texture.Skip(24).SequenceEqual(texture.Take(8)), string.Join(",", texture));
        Assert(fog.BuildSmoothedLevels(1).SequenceEqual(new byte[] { 0, 255 }), "factor 1 is the cell texture");
    }

    private static void Checks()
    {
        Throws<ArgumentOutOfRangeException>(() => new FogOfWarGrid(0, 3, Settings), "empty grid");
        Throws<ArgumentOutOfRangeException>(() => new FogOfWarGrid(2, 2, Settings with { FadeLevelsPerSecond = 0 }), "fade speed");
        var fog = new FogOfWarGrid(2, 2, Settings);
        Throws<ArgumentException>(() => fog.Update(new VisionMap(3, 2, 1), 0), "size mismatch");
        Throws<ArgumentOutOfRangeException>(() => fog.Update(new VisionMap(2, 2, 1), 1), "slot range");
    }
}
