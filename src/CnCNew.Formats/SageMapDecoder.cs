using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using CnCNew.Data.World;

namespace CnCNew.Formats;

/// <summary>
/// Decodes the static content of a SAGE <c>.map</c> file into the immutable <see cref="MapData"/> model. Chunk layouts follow
/// <c>WorldHeightMap::ParseHeightMapData/ParseBlendTileData/ParseObjectData</c> (GeneralsMD, WorldHeightMap.cpp),
/// <c>SidesList::ParseSidesDataChunk</c>, <c>PolygonTrigger::ParsePolygonTriggersDataChunk</c> and
/// <c>TerrainLogic::ParseWaypointDataChunk</c>. Every field is bounds-checked; chunks that are not decoded (scripts,
/// lighting, previews) are listed in <see cref="MapData.SkippedChunks"/> instead of being ignored silently.
/// </summary>
public static class SageMapDecoder
{
    private const int FlagVal = 0x7ADA0000;
    private const decimal MinObjectZ = -1000m;
    private const decimal MaxObjectZ = 2550m * MapData.HeightScale;

    public static MapData Decode(Stream stream)
    {
        var (container, bytes) = LegacyAssetReaders.ExpandSageMap(stream);
        return Decode(container, bytes);
    }

    public static MapData Decode(string container, byte[] expanded)
    {
        var (symbols, chunks) = LegacyAssetReaders.ParseSageChunks(expanded);
        string Symbol(int id) => symbols.TryGetValue(unchecked((uint)id), out var name)
            ? name
            : throw new InvalidDataException($"Map dict uses unknown symbol id {id}.");

        MapHeightField? height = null;
        MapBlendTiles? blend = null;
        var worldInfo = MapProperties.Empty;
        var sides = ImmutableArray<MapSide>.Empty;
        var teams = ImmutableArray<MapProperties>.Empty;
        var objects = ImmutableArray<MapObject>.Empty;
        var triggers = ImmutableArray<MapPolygonTrigger>.Empty;
        var links = ImmutableArray<MapWaypointLink>.Empty;
        var skipped = ImmutableArray.CreateBuilder<MapSkippedChunk>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chunk in chunks)
        {
            if (!seen.Add(chunk.Name)) throw new InvalidDataException($"Map contains chunk '{chunk.Name}' twice.");
            var reader = new ChunkReader(expanded, checked((int)chunk.HeaderOffset + 10), chunk.Bytes, chunk.Name, Symbol);
            switch (chunk.Name)
            {
                case "HeightMapData":
                    height = ReadHeightField(reader, chunk.Version);
                    break;
                case "BlendTileData":
                    blend = ReadBlendTiles(reader, chunk.Version, height ?? throw new InvalidDataException("BlendTileData precedes HeightMapData."));
                    break;
                case "WorldInfo":
                    worldInfo = reader.ReadDict();
                    reader.ExpectEnd();
                    break;
                case "SidesList":
                    (sides, teams) = ReadSides(reader, chunk.Version, skipped);
                    break;
                case "ObjectsList":
                    objects = ReadObjects(reader, skipped);
                    break;
                case "PolygonTriggers":
                    triggers = ReadTriggers(reader, chunk.Version);
                    reader.ExpectEnd();
                    break;
                case "WaypointsList":
                    links = ReadLinks(reader);
                    reader.ExpectEnd();
                    break;
                default:
                    skipped.Add(new MapSkippedChunk(chunk.Name, chunk.Version, chunk.Bytes));
                    break;
            }
        }

        return new MapData(
            container,
            height ?? throw new InvalidDataException("Map has no HeightMapData chunk."),
            blend ?? throw new InvalidDataException("Map has no BlendTileData chunk."),
            worldInfo, sides, teams, objects, triggers, links, skipped.ToImmutable());
    }

    private static MapHeightField ReadHeightField(ChunkReader r, int version)
    {
        if (version < 2) throw new InvalidDataException($"HeightMapData version {version} (5-unit cells) is not supported.");
        var width = r.ReadInt();
        var height = r.ReadInt();
        var border = version >= 3 ? r.ReadInt() : 0;
        if (width <= 0 || height <= 0 || border < 0 || (long)width * height > 4_000_000)
            throw new InvalidDataException($"HeightMapData has invalid size {width}x{height} border {border}.");
        ImmutableArray<MapBoundary> boundaries;
        if (version >= 4)
        {
            var count = r.ReadInt();
            if (count < 0 || count > 1024) throw new InvalidDataException($"HeightMapData has invalid boundary count {count}.");
            var builder = ImmutableArray.CreateBuilder<MapBoundary>(count);
            for (var i = 0; i < count; i++) builder.Add(new MapBoundary(r.ReadInt(), r.ReadInt()));
            boundaries = builder.MoveToImmutable();
        }
        else
        {
            boundaries = [new MapBoundary(width - 2 * border, height - 2 * border)];
        }
        var size = r.ReadInt();
        if (size != width * height) throw new InvalidDataException($"HeightMapData size {size} does not match {width}x{height}.");
        var data = r.ReadBytes(size);
        r.ExpectEnd();
        return new MapHeightField(width, height, border, boundaries, Wrap(data));
    }

    private static MapBlendTiles ReadBlendTiles(ChunkReader r, int version, MapHeightField field)
    {
        if (version < 2) throw new InvalidDataException($"BlendTileData version {version} is not supported.");
        var length = r.ReadInt();
        var cells = field.Width * field.Height;
        if (length != cells) throw new InvalidDataException($"BlendTileData length {length} does not match {cells} cells.");
        var stride = (field.Width + 7) / 8;
        var tiles = r.ReadShorts(cells);
        var blends = r.ReadShorts(cells);
        var extra = version >= 6 ? r.ReadShorts(cells) : new short[cells];
        var cliffInfoIndices = version >= 5 ? r.ReadShorts(cells) : new short[cells];
        var cliffFlags = new byte[stride * field.Height];
        if (version >= 7)
        {
            // Version 7 files were saved with the too-short stride (width+1)/8; the reference copies row by row.
            var storedStride = version == 7 ? (field.Width + 1) / 8 : stride;
            var stored = r.ReadBytes(storedStride * field.Height);
            for (var y = 0; y < field.Height; y++)
                for (var x = 0; x < storedStride; x++)
                    cliffFlags[y * stride + x] = stored[y * storedStride + x];
        }
        else
        {
            for (var y = 0; y < field.Height - 1; y++)
                for (var x = 0; x < field.Width - 1; x++)
                {
                    var a = field.HeightAt(x, y);
                    var b = field.HeightAt(x + 1, y);
                    var c = field.HeightAt(x, y + 1);
                    var d = field.HeightAt(x + 1, y + 1);
                    // (max - min) * 0.625 > 9.8 (PATHFIND_CLIFF_SLOPE_LIMIT_F) <=> max - min >= 16
                    if (Math.Max(Math.Max(a, b), Math.Max(c, d)) - Math.Min(Math.Min(a, b), Math.Min(c, d)) >= 16)
                        cliffFlags[y * stride + (x >> 3)] |= (byte)(1 << (x & 7));
                }
        }

        var bitmapTiles = r.ReadInt();
        var blendedCount = r.ReadInt();
        var cliffInfoCount = version >= 5 ? r.ReadInt() : 1;
        if (bitmapTiles < 0 || blendedCount < 0 || blendedCount > 1_000_000 || cliffInfoCount < 0 || cliffInfoCount > 1_000_000)
            throw new InvalidDataException($"BlendTileData has invalid counts {bitmapTiles}/{blendedCount}/{cliffInfoCount}.");

        var classCount = r.ReadInt();
        if (classCount < 0 || classCount > 4096) throw new InvalidDataException($"BlendTileData has invalid texture class count {classCount}.");
        var classes = ImmutableArray.CreateBuilder<MapTextureClass>(classCount);
        for (var i = 0; i < classCount; i++)
        {
            var first = r.ReadInt();
            var count = r.ReadInt();
            var width = r.ReadInt();
            r.ReadInt(); // legacy GDF flag
            classes.Add(new MapTextureClass(first, count, width, r.ReadAscii()));
        }

        var edgeTiles = 0;
        var edgeClasses = ImmutableArray.CreateBuilder<MapTextureClass>();
        if (version >= 4)
        {
            edgeTiles = r.ReadInt();
            var edgeClassCount = r.ReadInt();
            if (edgeClassCount < 0 || edgeClassCount > 4096) throw new InvalidDataException($"BlendTileData has invalid edge class count {edgeClassCount}.");
            for (var i = 0; i < edgeClassCount; i++)
            {
                var first = r.ReadInt();
                var count = r.ReadInt();
                var width = r.ReadInt();
                edgeClasses.Add(new MapTextureClass(first, count, width, r.ReadAscii()));
            }
        }

        var blended = ImmutableArray.CreateBuilder<MapBlendedTile>(Math.Max(0, blendedCount - 1));
        for (var i = 1; i < blendedCount; i++)
        {
            var blendIndex = r.ReadInt();
            var horizontal = r.ReadByte() != 0;
            var vertical = r.ReadByte() != 0;
            var right = r.ReadByte() != 0;
            var left = r.ReadByte() != 0;
            var inverted = r.ReadByte();
            var longDiagonal = version >= 3 && r.ReadByte() != 0;
            var customEdge = version >= 4 ? r.ReadInt() : -1;
            if (r.ReadInt() != FlagVal) throw new InvalidDataException($"BlendTileData blended tile {i} has a bad marker.");
            blended.Add(new MapBlendedTile(blendIndex, horizontal, vertical, right, left, inverted, longDiagonal, customEdge));
        }

        var cliffInfos = ImmutableArray.CreateBuilder<MapCliffInfo>();
        if (version >= 5)
        {
            for (var i = 1; i < cliffInfoCount; i++)
            {
                var tile = r.ReadInt();
                var uv = ImmutableArray.CreateBuilder<decimal>(8);
                for (var k = 0; k < 8; k++) uv.Add(r.ReadReal());
                var flip = r.ReadByte() != 0;
                var mutant = r.ReadByte() != 0;
                cliffInfos.Add(new MapCliffInfo(tile, uv.MoveToImmutable(), flip, mutant));
            }
        }
        r.ExpectEnd();

        return new MapBlendTiles(
            version, Wrap(tiles), Wrap(blends), Wrap(extra),
            Wrap(cliffInfoIndices), Wrap(cliffFlags), stride, bitmapTiles, edgeTiles,
            classes.MoveToImmutable(), edgeClasses.ToImmutable(), blended.MoveToImmutable(), cliffInfos.ToImmutable());
    }

    private static (ImmutableArray<MapSide>, ImmutableArray<MapProperties>) ReadSides(
        ChunkReader r, int version, ImmutableArray<MapSkippedChunk>.Builder skipped)
    {
        var sideCount = r.ReadInt();
        if (sideCount < 0 || sideCount > 64) throw new InvalidDataException($"SidesList has invalid side count {sideCount}.");
        var sides = ImmutableArray.CreateBuilder<MapSide>(sideCount);
        for (var i = 0; i < sideCount; i++)
        {
            var properties = r.ReadDict();
            var buildCount = r.ReadInt();
            if (buildCount < 0 || buildCount > 4096) throw new InvalidDataException($"SidesList side {i} has invalid build list count {buildCount}.");
            var build = ImmutableArray.CreateBuilder<MapBuildListEntry>(buildCount);
            for (var b = 0; b < buildCount; b++)
            {
                var name = r.ReadAscii();
                var template = r.ReadAscii();
                var x = r.ReadReal();
                var y = r.ReadReal();
                r.ReadReal(); // z is forced to 0 by BuildListInfo::parseBuildList
                var angle = r.ReadReal();
                var initiallyBuilt = r.ReadByte() != 0;
                var rebuilds = r.ReadInt();
                var script = "";
                var health = 100;
                var whiner = true;
                var unsellable = false;
                var repairable = false;
                if (version >= 3)
                {
                    script = r.ReadAscii();
                    health = r.ReadInt();
                    whiner = r.ReadByte() != 0;
                    unsellable = r.ReadByte() != 0;
                    repairable = r.ReadByte() != 0;
                }
                build.Add(new MapBuildListEntry(name, template, x, y, angle, initiallyBuilt, rebuilds, script, health, whiner, unsellable, repairable));
            }
            sides.Add(new MapSide(properties, build.MoveToImmutable()));
        }

        var teams = ImmutableArray.CreateBuilder<MapProperties>();
        if (version >= 2)
        {
            var teamCount = r.ReadInt();
            if (teamCount < 0 || teamCount > 4096) throw new InvalidDataException($"SidesList has invalid team count {teamCount}.");
            for (var i = 0; i < teamCount; i++) teams.Add(r.ReadDict());
        }

        while (!r.AtEnd)
        {
            var (name, childVersion, size) = r.ReadChunkHeader();
            r.Skip(size);
            skipped.Add(new MapSkippedChunk("SidesList/" + name, childVersion, size));
        }
        return (sides.MoveToImmutable(), teams.ToImmutable());
    }

    private static ImmutableArray<MapObject> ReadObjects(ChunkReader r, ImmutableArray<MapSkippedChunk>.Builder skipped)
    {
        var objects = ImmutableArray.CreateBuilder<MapObject>();
        while (!r.AtEnd)
        {
            var (name, version, size) = r.ReadChunkHeader();
            if (name != "Object")
            {
                r.Skip(size);
                skipped.Add(new MapSkippedChunk("ObjectsList/" + name, version, size));
                continue;
            }
            var child = r.Sub(size);
            var x = child.ReadReal();
            var y = child.ReadReal();
            var storedZ = child.ReadReal();
            var z = version <= 2 ? 0m : storedZ;
            var angle = child.ReadReal();
            var flags = child.ReadInt();
            var objectName = child.ReadAscii();
            var properties = version >= 2 ? child.ReadDict() : MapProperties.Empty;
            child.ExpectEnd();
            if (z < MinObjectZ || z > MaxObjectZ)
            {
                skipped.Add(new MapSkippedChunk("ObjectsList/Object[z out of range]", version, size));
                continue;
            }
            objects.Add(new MapObject(objectName, x, y, z, angle, flags, properties));
        }
        return objects.ToImmutable();
    }

    private static ImmutableArray<MapPolygonTrigger> ReadTriggers(ChunkReader r, int version)
    {
        if (version < 2) throw new InvalidDataException($"PolygonTriggers version {version} is not supported.");
        var count = r.ReadInt();
        if (count < 0 || count > 65535) throw new InvalidDataException($"PolygonTriggers has invalid count {count}.");
        var triggers = ImmutableArray.CreateBuilder<MapPolygonTrigger>(count);
        for (var i = 0; i < count; i++)
        {
            var name = r.ReadAscii();
            var layer = version >= 4 ? r.ReadAscii() : "";
            var id = r.ReadInt();
            var isWater = r.ReadByte() != 0;
            var isRiver = false;
            var riverStart = 0;
            if (version >= 3)
            {
                isRiver = r.ReadByte() != 0;
                riverStart = r.ReadInt();
            }
            var pointCount = r.ReadInt();
            if (pointCount < 0 || pointCount > 100_000) throw new InvalidDataException($"Trigger '{name}' has invalid point count {pointCount}.");
            var points = ImmutableArray.CreateBuilder<MapPoint>(pointCount);
            for (var p = 0; p < pointCount; p++) points.Add(new MapPoint(r.ReadInt(), r.ReadInt(), r.ReadInt()));
            triggers.Add(new MapPolygonTrigger(name, layer, id, isWater, isRiver, riverStart, points.MoveToImmutable()));
        }
        return triggers.MoveToImmutable();
    }

    private static ImmutableArray<MapWaypointLink> ReadLinks(ChunkReader r)
    {
        var count = r.ReadInt();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException($"WaypointsList has invalid link count {count}.");
        var links = ImmutableArray.CreateBuilder<MapWaypointLink>(count);
        for (var i = 0; i < count; i++) links.Add(new MapWaypointLink(r.ReadInt(), r.ReadInt()));
        return links.MoveToImmutable();
    }

    private static ImmutableArray<T> Wrap<T>(T[] array) => System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(array);

    private sealed class ChunkReader
    {
        private readonly byte[] _data;
        private readonly int _end;
        private readonly string _label;
        private readonly Func<int, string> _symbol;
        private int _position;

        public ChunkReader(byte[] data, int start, int size, string label, Func<int, string> symbol)
        {
            if (start < 0 || size < 0 || start > data.Length - size) throw new InvalidDataException($"Chunk '{label}' lies outside the map.");
            _data = data;
            _position = start;
            _end = start + size;
            _label = label;
            _symbol = symbol;
        }

        public bool AtEnd => _position == _end;

        public ChunkReader Sub(int size)
        {
            var child = new ChunkReader(_data, _position, size <= _end - _position ? size : throw Truncated(), _label, _symbol);
            _position += size;
            return child;
        }

        public void ExpectEnd()
        {
            if (_position != _end) throw new InvalidDataException($"Chunk '{_label}' has {_end - _position} unread bytes.");
        }

        public void Skip(int count)
        {
            Need(count);
            _position += count;
        }

        public (string Name, int Version, int Size) ReadChunkHeader()
        {
            var id = ReadInt();
            Need(6);
            var version = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_position));
            var size = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_position + 2));
            _position += 6;
            if (size < 0 || size > _end - _position) throw new InvalidDataException($"Nested chunk in '{_label}' has invalid size {size}.");
            return (_symbol(id), version, size);
        }

        public byte ReadByte()
        {
            Need(1);
            return _data[_position++];
        }

        public int ReadInt()
        {
            Need(4);
            var value = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_position));
            _position += 4;
            return value;
        }

        public decimal ReadReal()
        {
            var value = BitConverter.Int32BitsToSingle(ReadInt());
            if (!float.IsFinite(value)) throw new InvalidDataException($"Chunk '{_label}' contains a non-finite real.");
            return (decimal)value;
        }

        public byte[] ReadBytes(int count)
        {
            if (count < 0) throw Truncated();
            Need(count);
            var result = _data.AsSpan(_position, count).ToArray();
            _position += count;
            return result;
        }

        public short[] ReadShorts(int count)
        {
            var raw = ReadBytes(checked(count * 2));
            var result = new short[count];
            for (var i = 0; i < count; i++) result[i] = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(i * 2));
            return result;
        }

        public string ReadAscii()
        {
            Need(2);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_position));
            _position += 2;
            Need(length);
            var text = Encoding.Latin1.GetString(_data, _position, length);
            _position += length;
            return text;
        }

        public string ReadUnicode()
        {
            Need(2);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_position));
            _position += 2;
            Need(length * 2);
            var text = Encoding.Unicode.GetString(_data, _position, length * 2);
            _position += length * 2;
            return text;
        }

        public MapProperties ReadDict()
        {
            Need(2);
            var count = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_position));
            _position += 2;
            var items = ImmutableArray.CreateBuilder<MapProperty>(count);
            for (var i = 0; i < count; i++)
            {
                var keyAndType = ReadInt();
                var key = _symbol(keyAndType >> 8);
                var type = (keyAndType & 0xff) switch
                {
                    0 => MapPropertyType.Bool,
                    1 => MapPropertyType.Int,
                    2 => MapPropertyType.Real,
                    3 => MapPropertyType.Ascii,
                    4 => MapPropertyType.Unicode,
                    var other => throw new InvalidDataException($"Dict key '{key}' in '{_label}' has unknown type {other}."),
                };
                items.Add(type switch
                {
                    MapPropertyType.Bool => new MapProperty(key, type, ReadByte() != 0 ? 1 : 0, 0, ""),
                    MapPropertyType.Int => new MapProperty(key, type, ReadInt(), 0, ""),
                    MapPropertyType.Real => new MapProperty(key, type, 0, ReadReal(), ""),
                    MapPropertyType.Ascii => new MapProperty(key, type, 0, 0, ReadAscii()),
                    _ => new MapProperty(key, type, 0, 0, ReadUnicode()),
                });
            }
            return new MapProperties(items.MoveToImmutable());
        }

        private void Need(int count)
        {
            if (count < 0 || count > _end - _position) throw Truncated();
        }

        private InvalidDataException Truncated() => new($"Chunk '{_label}' is truncated at offset {_position}.");
    }
}
