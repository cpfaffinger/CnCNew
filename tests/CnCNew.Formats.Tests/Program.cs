using System.Buffers.Binary;
using System.Text;
using CnCNew.Formats;

using (var dds = new MemoryStream(CreateDds()))
{
    var metadata = LegacyAssetReaders.ReadDds(dds);
    Assert(metadata is { Width: 4, Height: 4, PixelFormat: "BC1" }, "DDS metadata mismatch.");
}
ExpectFailure(() => LegacyAssetReaders.ReadDds(new MemoryStream(new byte[128])), "DDS signature");

using (var wave = new MemoryStream(CreateWave()))
    Assert(LegacyAssetReaders.ReadRiff(wave, "WAVE").Chunks.SequenceEqual(["fmt ", "data"]), "WAVE chunks mismatch.");
var truncatedWave = CreateWave()[..^1];
ExpectFailure(() => LegacyAssetReaders.ReadRiff(new MemoryStream(truncatedWave), "WAVE"), "RIFF length");

using (var str = new MemoryStream(Encoding.Latin1.GetBytes("GUI:Title\r\n\"Hello\"\r\nEND\r\n")))
    Assert(LegacyAssetReaders.ReadStr(str).Single().Value == "Hello", "STR value mismatch.");
ExpectFailure(() => LegacyAssetReaders.ReadStr(new MemoryStream(Encoding.Latin1.GetBytes("Key\nunterminated"))), "STR quote");

using (var wnd = new MemoryStream(Encoding.Latin1.GetBytes("FILE_VERSION = 2;\nWINDOW { IMAGE = Art/Textures/Test.dds; }")))
    Assert(LegacyAssetReaders.ReadWnd(wnd).References.Single() == "Art/Textures/Test.dds", "WND reference mismatch.");
ExpectFailure(() => LegacyAssetReaders.ReadWnd(new MemoryStream(Encoding.ASCII.GetBytes("FILE_VERSION = 3;"))), "WND version");

using (var map = new MemoryStream(CreateSageMap()))
    Assert(LegacyAssetReaders.ReadSageMap(map).Symbols.Single().Value == "Test", "SAGE symbol mismatch.");
var badMap = CreateSageMap().Concat(new byte[] { 1, 0, 0, 0, 1, 0, 100, 0, 0, 0 }).ToArray();
ExpectFailure(() => LegacyAssetReaders.ValidateSageMap(new MemoryStream(badMap)), "MAP chunk bounds");
ExpectFailure(() => LegacyAssetReaders.ValidateWak(new MemoryStream(new byte[23])), "WAK length");

MapDecoderTests.Run();

Assert(AssetFormat.Classify("movie.bik").ProductScope, "BIK must be in product scope.");
Assert(AssetFormat.Classify("driver.m3d").Strategy == "out-of-scope", "Miles provider must be out of scope.");
Console.WriteLine("All legacy asset format tests passed.");

static byte[] CreateDds()
{
    var data = new byte[136];
    "DDS "u8.CopyTo(data);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 124);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 4);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 4);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(76), 32);
    "DXT1"u8.CopyTo(data.AsSpan(84));
    return data;
}
static byte[] CreateWave()
{
    var data = new byte[48];
    "RIFF"u8.CopyTo(data); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 40); "WAVE"u8.CopyTo(data.AsSpan(8));
    "fmt "u8.CopyTo(data.AsSpan(12)); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 16);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(20), 1); BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(22), 1);
    "data"u8.CopyTo(data.AsSpan(36)); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(40), 4);
    return data;
}
static byte[] CreateSageMap()
{
    using var stream = new MemoryStream();
    stream.Write("CkMp"u8);
    Span<byte> value = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(value, 1); stream.Write(value);
    stream.WriteByte(4); stream.Write("Test"u8);
    BinaryPrimitives.WriteUInt32LittleEndian(value, 1); stream.Write(value);
    return stream.ToArray();
}
static void ExpectFailure(Action action, string name)
{
    try { action(); throw new InvalidOperationException($"{name} did not fail."); }
    catch (InvalidDataException) { }
}
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
