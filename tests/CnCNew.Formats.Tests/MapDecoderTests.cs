using System.Buffers.Binary;
using System.Text;
using CnCNew.Data.World;
using CnCNew.Formats;

internal static class MapDecoderTests
{
    public static void Run()
    {
        DecodesFullSyntheticMap();
        DerivesCliffFlagsBeforeVersion7();
        ReadsVersion7ShortCliffStride();
        ReportsUnparsedChunks();
        RejectsBrokenMaps();
        Console.WriteLine("All map decoder tests passed.");
    }

    private static void DecodesFullSyntheticMap()
    {
        var b = new MapBuilder();
        b.Chunk("HeightMapData", 4, Height(5, 5, 1, Enumerable.Range(0, 25).Select(i => (byte)(i * 2)).ToArray()));
        b.Chunk("BlendTileData", 8, Blend(5, 5, 8, cliffBytes: [0, 4, 0, 0, 0], blended: 2, cliffInfos: 2));
        b.Chunk("WorldInfo", 1, b.Dict(("mapName", "Test Map"), ("weather", 1), ("gravity", 2.5f), ("flag", true), ("title", new Uni("Hé"))));
        b.Chunk("ObjectsList", 3, [
            ..b.Nested("Object", 3, Obj(b, 10, 20, 5, 1.5f, 0x2, "Prop", b.Dict(("waypointID", 7), ("waypointName", "Player_1_Start")))),
            ..b.Nested("Object", 3, Obj(b, 30, 40, 0, 0, 0, "Tree", b.Dict())),
        ]);
        b.Chunk("PolygonTriggers", 4, Triggers());
        b.Chunk("WaypointsList", 1, [..I(2), ..I(7), ..I(8), ..I(8), ..I(9)]);
        b.Chunk("SidesList", 3, [
            ..I(1), ..b.Dict(("playerName", "Player_1")), ..I(1),
            ..A("Barracks"), ..A("AmericaBarracks"), ..F(100), ..F(200), ..F(9), ..F(0.5f), 1, ..I(3),
            ..A("Script"), ..I(80), 1, 0, 1,
            ..I(1), ..b.Dict(("teamName", "teamPlayer_1")),
            ..b.Nested("PlayerScriptsList", 1, PlayerScripts(b)),
        ]);
        b.Chunk("GlobalLighting", 3, [9, 9]);

        var map = SageMapDecoder.Decode("CkMp", b.Build());
        Assert(map.HeightField is { Width: 5, Height: 5, Border: 1 }, "height field size");
        Assert(map.HeightField.Boundaries.Length == 1 && map.HeightField.Boundaries[0] == new MapBoundary(3, 3), "boundary");
        Assert(map.HeightField.HeightAt(2, 1) == 14, "height sample");
        Assert(map.BlendTiles.Version == 8 && map.BlendTiles.CliffStride == 1, "blend version/stride");
        Assert(map.BlendTiles.IsCliff(2, 1) && !map.BlendTiles.IsCliff(0, 0), "cliff bit");
        Assert(map.BlendTiles.TextureClasses.Single().Name == "Grass" && map.BlendTiles.BlendedTiles.Length == 1, "texture classes and blends");
        Assert(map.BlendTiles.CliffInfos.Single().Uv.Length == 8 && map.BlendTiles.CliffInfos[0].Mutant, "cliff info");
        Assert(map.WorldInfo.GetText("mapName") == "Test Map" && map.WorldInfo.GetText("title") == "Hé", "world info strings");
        Assert(map.WorldInfo.Find("gravity") is { Type: MapPropertyType.Real, Real: 2.5m }, "world info real");
        Assert(map.WorldInfo.GetBool("flag") && map.WorldInfo.TryGetInt("weather", out var weather) && weather == 1, "world info bool/int");
        Assert(map.Objects.Length == 2 && map.Objects[0].IsWaypoint && map.Objects[0].IsRoadPoint && !map.Objects[1].IsWaypoint, "objects");
        Assert(map.Objects[0] is { X: 10, Y: 20, Z: 5, Angle: 1.5m } && map.Objects[0].Properties.GetText("waypointName") == "Player_1_Start", "object fields");
        Assert(map.Triggers.Length == 2 && map.Triggers[0] is { IsWater: true, Id: 4 } && map.Triggers[0].Points[1] == new MapPoint(100, 0, 7), "triggers");
        Assert(map.Triggers[1] is { IsRiver: true, RiverStart: 1, Layer: "L" }, "river trigger");
        Assert(map.WaypointLinks.SequenceEqual([new MapWaypointLink(7, 8), new MapWaypointLink(8, 9)]), "links");
        Assert(map.Sides.Length == 1 && map.Sides[0].BuildList.Single() is { BuildingName: "Barracks", Template: "AmericaBarracks", X: 100, Y: 200, Rebuilds: 3, Health: 80, Repairable: true, Script: "Script" }, "build list");
        Assert(map.Teams.Single().GetText("teamName") == "teamPlayer_1", "teams");
        Assert(map.SkippedChunks.Select(s => s.Path).SequenceEqual(["GlobalLighting"]), "scripts are no longer reported as skipped");
        var list = map.PlayerScripts.Single();
        Assert(list.Scripts.Length == 1 && list.Groups.Length == 1, "one top-level script and one group");
        var script = list.Scripts[0];
        Assert(script is { Name: "DoIt", Comment: "note", IsActive: true, IsOneShot: true, Easy: true, Normal: false, Hard: true, DelayEvaluationSeconds: 5 }, "script fields");
        var condition = script.OrConditions.Single().AndConditions.Single();
        Assert(condition is { TypeName: "CONDITION_TRUE" } && condition.Parameters.IsEmpty, "condition decoded by name, zero parameters");
        var action = script.Actions.Single();
        Assert(action.TypeName == "DEBUG_STRING" && action.Parameters.Single() is { Type: MapScriptParameterType.TextString, Text: "hi" }, "action parameter");
        Assert(script.FalseActions.IsEmpty, "no false actions on this script");
        var group = list.Groups[0];
        Assert(group is { Name: "Group1", IsActive: true, IsSubroutine: true } && group.Scripts.Single().Name == "InGroup", "script group");
    }

    private static byte[] PlayerScripts(MapBuilder b)
    {
        var condition = Condition(b, "CONDITION_TRUE");
        var trueAction = ScriptAction(b, "DEBUG_STRING", Param(MapScriptParameterType.TextString, text: "hi"));
        var script = Script(b, "DoIt", comment: "note", isActive: true, isOneShot: true, easy: true, normal: false, hard: true,
            isSubroutine: false, delaySeconds: 5, orConditions: [b.Nested("Condition", 4, condition)], actions: [trueAction]);
        var groupedScript = Script(b, "InGroup", comment: "", isActive: true, isOneShot: false, easy: true, normal: true, hard: true,
            isSubroutine: false, delaySeconds: 0, orConditions: [], actions: []);
        var group = ScriptGroup(b, "Group1", isActive: true, isSubroutine: true, scripts: [groupedScript]);
        var scriptList = new byte[][]
        {
            b.Nested("Script", 2, script),
            b.Nested("ScriptGroup", 2, group),
        }.SelectMany(x => x).ToArray();
        return [..b.Nested("ScriptList", 1, scriptList)];
    }

    private static byte[] Script(MapBuilder b, string name, string comment, bool isActive, bool isOneShot, bool easy, bool normal, bool hard,
        bool isSubroutine, int delaySeconds, byte[][] orConditions, byte[][] actions) =>
    [
        ..A(name), ..A(comment), ..A(""), ..A(""),
        isActive ? (byte)1 : (byte)0, isOneShot ? (byte)1 : (byte)0, easy ? (byte)1 : (byte)0, normal ? (byte)1 : (byte)0, hard ? (byte)1 : (byte)0,
        isSubroutine ? (byte)1 : (byte)0,
        ..I(delaySeconds),
        ..orConditions.SelectMany(c => b.Nested("OrCondition", 1, c)),
        ..actions.SelectMany(a => b.Nested("ScriptAction", 2, a)),
    ];

    private static byte[] ScriptGroup(MapBuilder b, string name, bool isActive, bool isSubroutine, byte[][] scripts) =>
    [
        ..A(name), isActive ? (byte)1 : (byte)0, isSubroutine ? (byte)1 : (byte)0,
        ..scripts.SelectMany(s => b.Nested("Script", 2, s)),
    ];

    // The raw enum index is the decoded identity (see MapScriptCatalog); the NameKeyType field is unresolvable on real maps and is
    // written here only because version 4 conditions/version 2 actions carry one on disk (the decoder reads and discards it).
    private static byte[] Condition(MapBuilder b, string typeName, params byte[][] parameters) =>
    [
        ..I(MapScriptCatalog.ConditionTypeNames.IndexOf(typeName)), ..I(b.NameKey(typeName)), ..I(parameters.Length), ..parameters.SelectMany(p => p),
    ];

    private static byte[] ScriptAction(MapBuilder b, string typeName, params byte[][] parameters) =>
    [
        ..I(MapScriptCatalog.ScriptActionTypeNames.IndexOf(typeName)), ..I(b.NameKey(typeName)), ..I(parameters.Length), ..parameters.SelectMany(p => p),
    ];

    private static byte[] Param(MapScriptParameterType type, int number = 0, float real = 0, string text = "") =>
        [..I((int)type), ..I(number), ..F(real), ..A(text)];

    private static void DerivesCliffFlagsBeforeVersion7()
    {
        // A 16-unit jump (10.0 world units) between neighbouring vertices makes a cell a cliff; 15 (9.375) does not.
        var heights = new byte[9];
        heights[1] = 16;
        heights[3 + 2] = 15;
        var b = new MapBuilder();
        b.Chunk("HeightMapData", 2, Height(3, 3, 0, heights, version: 2));
        b.Chunk("BlendTileData", 6, Blend(3, 3, 6, cliffBytes: null, blended: 1, cliffInfos: 1));
        var map = SageMapDecoder.Decode("CkMp", b.Build());
        Assert(map.BlendTiles.IsCliff(0, 0) && map.BlendTiles.IsCliff(1, 0), "cliff from height difference 16");
        Assert(!map.BlendTiles.IsCliff(0, 1) && !map.BlendTiles.IsCliff(1, 1), "difference 15 is no cliff");
    }

    private static void ReadsVersion7ShortCliffStride()
    {
        // width 17: the correct stride is 3 bytes, version 7 files stored (17+1)/8 = 2 bytes per row.
        var b = new MapBuilder();
        b.Chunk("HeightMapData", 4, Height(17, 2, 0, new byte[34]));
        b.Chunk("BlendTileData", 7, Blend(17, 2, 7, cliffBytes: [0b0000_0100, 0b0000_0001, 0b0000_1000, 0b0000_0010], blended: 1, cliffInfos: 1));
        var map = SageMapDecoder.Decode("CkMp", b.Build());
        Assert(map.BlendTiles.CliffStride == 3, "stride");
        Assert(map.BlendTiles.IsCliff(2, 0) && map.BlendTiles.IsCliff(8, 0) && map.BlendTiles.IsCliff(3, 1) && map.BlendTiles.IsCliff(9, 1), "v7 rows copied");
        Assert(!map.BlendTiles.IsCliff(16, 0), "third byte stays clear");
    }

    private static void ReportsUnparsedChunks()
    {
        var b = new MapBuilder();
        b.Chunk("HeightMapData", 4, Height(2, 2, 0, new byte[4]));
        b.Chunk("BlendTileData", 8, Blend(2, 2, 8, cliffBytes: new byte[2], blended: 1, cliffInfos: 1));
        b.Chunk("ObjectsList", 3, b.Nested("Object", 3, Obj(b, 0, 0, 5000, 0, 0, "Sky", b.Dict())));
        b.Chunk("MapPreview", 1, [1]);
        var map = SageMapDecoder.Decode("CkMp", b.Build());
        Assert(map.Objects.IsEmpty, "out-of-range object dropped");
        Assert(map.SkippedChunks.Select(s => s.Path).SequenceEqual(["ObjectsList/Object[z out of range]", "MapPreview"]), "drops are reported");
    }

    private static void RejectsBrokenMaps()
    {
        var b = new MapBuilder();
        b.Chunk("BlendTileData", 8, Blend(2, 2, 8, cliffBytes: new byte[2], blended: 1, cliffInfos: 1));
        Throws(() => SageMapDecoder.Decode("CkMp", b.Build()), "blend before height");

        b = new MapBuilder();
        b.Chunk("HeightMapData", 4, Height(2, 2, 0, new byte[4]));
        Throws(() => SageMapDecoder.Decode("CkMp", b.Build()), "no blend tile data");

        b = new MapBuilder();
        b.Chunk("HeightMapData", 4, Height(2, 2, 0, new byte[4])[..^1]);
        b.Chunk("BlendTileData", 8, Blend(2, 2, 8, cliffBytes: new byte[2], blended: 1, cliffInfos: 1));
        Throws(() => SageMapDecoder.Decode("CkMp", b.Build()), "truncated heights");

        b = new MapBuilder();
        b.Chunk("HeightMapData", 4, Height(2, 2, 0, new byte[4]));
        b.Chunk("BlendTileData", 8, Blend(2, 2, 8, cliffBytes: new byte[2], blended: 3, cliffInfos: 1, breakMarker: true));
        Throws(() => SageMapDecoder.Decode("CkMp", b.Build()), "bad blended tile marker");

        b = new MapBuilder();
        b.Chunk("HeightMapData", 4, Height(2, 2, 0, new byte[4]));
        b.Chunk("BlendTileData", 8, Blend(2, 2, 8, cliffBytes: new byte[2], blended: 1, cliffInfos: 1));
        b.Chunk("WorldInfo", 1, [1, 0, ..I(99 << 8 | 1), ..I(5)]);
        Throws(() => SageMapDecoder.Decode("CkMp", b.Build()), "unknown dict symbol");
    }

    private static byte[] Height(int width, int height, int border, byte[] data, int version = 4)
    {
        var bytes = new List<byte>();
        bytes.AddRange(I(width));
        bytes.AddRange(I(height));
        if (version >= 3) bytes.AddRange(I(border));
        if (version >= 4)
        {
            bytes.AddRange(I(1));
            bytes.AddRange(I(width - 2 * border));
            bytes.AddRange(I(height - 2 * border));
        }
        bytes.AddRange(I(width * height));
        bytes.AddRange(data);
        return [.. bytes];
    }

    private static byte[] Blend(int width, int height, int version, byte[]? cliffBytes, int blended, int cliffInfos, bool breakMarker = false)
    {
        var cells = width * height;
        var bytes = new List<byte>();
        bytes.AddRange(I(cells));
        for (var layer = 0; layer < (version >= 6 ? 3 : 2); layer++) bytes.AddRange(new byte[cells * 2]);
        if (version >= 5) bytes.AddRange(new byte[cells * 2]);
        if (version >= 7) bytes.AddRange(cliffBytes!);
        bytes.AddRange(I(4));
        bytes.AddRange(I(blended));
        if (version >= 5) bytes.AddRange(I(cliffInfos));
        bytes.AddRange(I(1));
        bytes.AddRange(I(0)); bytes.AddRange(I(4)); bytes.AddRange(I(2)); bytes.AddRange(I(0)); bytes.AddRange(A("Grass"));
        if (version >= 4) { bytes.AddRange(I(0)); bytes.AddRange(I(0)); }
        for (var i = 1; i < blended; i++)
        {
            bytes.AddRange(I(i));
            bytes.AddRange(new byte[] { 1, 0, 0, 0, 0 });
            if (version >= 3) bytes.Add(0);
            if (version >= 4) bytes.AddRange(I(-1));
            bytes.AddRange(I(breakMarker ? 1 : 0x7ADA0000));
        }
        if (version >= 5)
            for (var i = 1; i < cliffInfos; i++)
            {
                bytes.AddRange(I(3));
                for (var k = 0; k < 8; k++) bytes.AddRange(F(k / 8f));
                bytes.AddRange(new byte[] { 0, 1 });
            }
        return [.. bytes];
    }

    private static byte[] Obj(MapBuilder b, float x, float y, float z, float angle, int flags, string name, byte[] dict)
        => [..F(x), ..F(y), ..F(z), ..F(angle), ..I(flags), ..A(name), ..dict];

    private static byte[] Triggers() =>
    [
        ..I(2),
        ..A("Lake"), ..A(""), ..I(4), 1, 0, ..I(0), ..I(3), ..P(0, 0, 7), ..P(100, 0, 7), ..P(100, 100, 7),
        ..A("River"), ..A("L"), ..I(5), 1, 1, ..I(1), ..I(2), ..P(0, 0, 1), ..P(5, 5, 1),
    ];

    private static byte[] P(int x, int y, int z) => [..I(x), ..I(y), ..I(z)];

    internal static byte[] I(int value)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(data, value);
        return data;
    }

    internal static byte[] F(float value) => I(BitConverter.SingleToInt32Bits(value));

    internal static byte[] A(string text) => [..BitConverter.GetBytes((ushort)text.Length), ..Encoding.Latin1.GetBytes(text)];

    private static void Throws(Action action, string name)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException($"Expected InvalidDataException: {name}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Map decoder test failed: " + message);
    }

    private sealed record Uni(string Text);

    private sealed class MapBuilder
    {
        private readonly List<string> _symbols = [];
        private readonly List<(string Name, int Version, byte[] Data)> _chunks = [];

        private int Id(string name)
        {
            var index = _symbols.IndexOf(name);
            if (index < 0) { _symbols.Add(name); index = _symbols.Count - 1; }
            return index + 1;
        }

        public void Chunk(string name, int version, byte[] data)
        {
            Id(name);
            _chunks.Add((name, version, data));
        }

        /// <summary>A <c>NameKeyType</c> value: the same symbol table id a chunk name or dict key would get.</summary>
        public int NameKey(string name) => Id(name);

        public byte[] Nested(string name, int version, byte[] data)
            => [..I(Id(name)), ..BitConverter.GetBytes((ushort)version), ..I(data.Length), ..data];

        public byte[] Dict(params (string Key, object Value)[] entries)
        {
            var bytes = new List<byte>(BitConverter.GetBytes((ushort)entries.Length));
            foreach (var (key, value) in entries)
            {
                switch (value)
                {
                    case bool flag: bytes.AddRange(I(Id(key) << 8 | 0)); bytes.Add(flag ? (byte)1 : (byte)0); break;
                    case int number: bytes.AddRange(I(Id(key) << 8 | 1)); bytes.AddRange(I(number)); break;
                    case float real: bytes.AddRange(I(Id(key) << 8 | 2)); bytes.AddRange(F(real)); break;
                    case Uni uni:
                        bytes.AddRange(I(Id(key) << 8 | 4));
                        bytes.AddRange(BitConverter.GetBytes((ushort)uni.Text.Length));
                        bytes.AddRange(Encoding.Unicode.GetBytes(uni.Text));
                        break;
                    case string text: bytes.AddRange(I(Id(key) << 8 | 3)); bytes.AddRange(A(text)); break;
                    default: throw new InvalidOperationException("Unsupported dict value.");
                }
            }
            return [.. bytes];
        }

        public byte[] Build()
        {
            var bytes = new List<byte>("CkMp"u8.ToArray());
            bytes.AddRange(I(_symbols.Count));
            for (var i = 0; i < _symbols.Count; i++)
            {
                bytes.Add((byte)_symbols[i].Length);
                bytes.AddRange(Encoding.Latin1.GetBytes(_symbols[i]));
                bytes.AddRange(I(i + 1));
            }
            foreach (var (name, version, data) in _chunks)
            {
                bytes.AddRange(I(Id(name)));
                bytes.AddRange(BitConverter.GetBytes((ushort)version));
                bytes.AddRange(I(data.Length));
                bytes.AddRange(data);
            }
            return [.. bytes];
        }
    }
}
