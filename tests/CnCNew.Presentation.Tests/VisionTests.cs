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
        t.Run("fading visits only moving cells and equals the full pass", FadingEqualsTheFullPass);
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

    /// <summary>
    /// The grid only visits cells whose level is still moving. The reference here is the plain rule applied to every cell in every step; both must give the
    /// same levels and the same number of cells still on their way, through state changes in mid-fade, steps of length zero, snaps and the vision-map path.
    /// </summary>
    private static void FadingEqualsTheFullPass()
    {
        const int width = 23, height = 17;
        // Equal fog and shroud levels: a state change that does not move the level must leave the cell at rest.
        foreach (var settings in new[] { Settings, new FogSettings(0, 127, 255, 97.3f), new FogSettings(40, 40, 200, 333f) })
        {
            var fog = new FogOfWarGrid(width, height, settings);
            var states = new FogCellState[width * height];
            var expected = new float[width * height];
            Array.Fill(expected, settings.ShroudLevel);
            var actual = new byte[width * height];
            var vision = new VisionMap(width, height, 2);
            uint random = 12345;
            uint Next() => random = random * 1664525u + 1013904223u;
            for (var round = 0; round < 400; round++)
            {
                switch (Next() >> 28)
                {
                    case 0 or 1 or 2:
                        // A handful of cells change state.
                        for (var change = (int)(Next() >> 27); change > 0; change--) states[(int)(Next() % (uint)states.Length)] = (FogCellState)(Next() % 3);
                        fog.SetStates(states);
                        break;
                    case 3:
                        // The path the game takes: states from the simulation's vision map.
                        vision.BeginRefresh();
                        vision.Reveal(1, Fixed.FromInt((int)(Next() % (width * 10))), Fixed.FromInt((int)(Next() % (height * 10))), Fixed.FromInt(10 + (int)(Next() % 60)));
                        fog.Update(vision, 1);
                        for (var cy = 0; cy < height; cy++)
                            for (var cx = 0; cx < width; cx++)
                                states[cy * width + cx] = vision.IsVisible(1, cx, cy) ? FogCellState.Visible : vision.IsExplored(1, cx, cy) ? FogCellState.Fogged : FogCellState.Unexplored;
                        break;
                    case 4:
                        fog.Snap();
                        for (var index = 0; index < expected.Length; index++) expected[index] = settings.LevelOf(states[index]);
                        Assert(fog.FadingCells == 0, "nothing fades after a snap");
                        break;
                }

                var dt = (Next() >> 29) switch { 0 => 0f, 1 => CnCNew.Tests.TestTime.FrameSecondsF, 2 => 1f / 144, 3 => 0.25f, _ => (Next() % 1000) / 20000f };
                fog.Advance(dt);
                var step = settings.FadeLevelsPerSecond * dt;
                var moving = 0;
                for (var index = 0; index < expected.Length; index++)
                {
                    float target = settings.LevelOf(states[index]);
                    var current = expected[index];
                    expected[index] = current < target ? Math.Min(target, current + step) : Math.Max(target, current - step);
                    if (expected[index] != target) moving++;
                }

                fog.CopyLevels(actual);
                for (var index = 0; index < expected.Length; index++)
                {
                    Assert(actual[index] == (byte)MathF.Round(expected[index]), $"round {round}, cell {index}: level {actual[index]} instead of {(byte)MathF.Round(expected[index])}");
                    Assert(fog.StateAt(index % width, index / width) == states[index], $"round {round}, cell {index}: state");
                }
                // A cell counts as arrived exactly when the plain rule has reached the target float.
                Assert(fog.FadingCells == moving, $"round {round}: {fog.FadingCells} fading cells instead of {moving}");
            }

            // Long enough for every fade to end: nothing is left to visit, and the levels are exactly the levels of the states.
            for (var round = 0; round < 40; round++) fog.Advance(1f);
            Assert(fog.FadingCells == 0, "every fade ends");
            for (var index = 0; index < states.Length; index++)
                Assert(fog.LevelAt(index % width, index / width) == settings.LevelOf(states[index]), $"cell {index} at rest");
        }
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
