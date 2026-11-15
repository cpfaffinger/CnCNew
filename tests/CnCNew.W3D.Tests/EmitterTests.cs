using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using CnCNew.W3D;

/// <summary>
/// The particle emitter chunk (<c>W3D_CHUNK_EMITTER</c>, issue 369): read as <c>ParticleEmitterDefClass::Load_W3D</c> reads it, from small files built here, and a file of one
/// emitter alone is a model (<c>WW3DAssetManager::Create_Render_Obj</c> makes a <c>ParticleEmitterClass</c> of it). With real data: the hydrant's water spray.
/// </summary>
internal static class EmitterTests
{
    public static void RunSynthetic()
    {
        VersionTwoEmitter();
        EmitterModel();
        VersionOneConversion();
        BlackLastColourKey();
        Strictness();
    }

    public static void RunRealData(string contentDirectory)
    {
        var library = new W3dAssetLibrary(path =>
        {
            var physical = Path.Combine(contentDirectory, path.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(physical) ? File.OpenRead(physical) : null;
        });
        var model = library.GetModel("EXHydrant");
        Check(model.Emitter is not null && model.SubObjects.IsEmpty && model.Skeleton.BoneCount == 1, "EXHydrant is an emitter model without sub-objects.");
        var emitter = model.Emitter!;
        Check(emitter.Name == "EXHydrant" && emitter.TextureName.Equals("exhydrant.tga", StringComparison.OrdinalIgnoreCase), $"EXHydrant's name and texture ({emitter.Name}, {emitter.TextureName}).");
        Check(emitter.RenderMode == W3dEmitterRenderMode.QuadParticles && emitter.FrameMode == W3dEmitterFrameMode.Grid1x1 && emitter.BurstSize == 5, "EXHydrant emits bursts of five quads.");
        Check(Near(emitter.Lifetime, 1.5f) && Near(emitter.EmissionRate, 30.30303f) && Near(emitter.Velocity.Z, 80f) && Near(emitter.Acceleration.Z, -100f) && Near(emitter.OutwardVelocity, 7f),
            $"EXHydrant's lifetime {emitter.Lifetime}, rate {emitter.EmissionRate}, velocity {emitter.Velocity}, acceleration {emitter.Acceleration}, outward {emitter.OutwardVelocity}.");
        Check(emitter.Shader is { SourceBlend: W3dSourceBlend.One, DestinationBlend: W3dDestinationBlend.Zero, AlphaTestEnabled: true }, "EXHydrant draws alpha-tested.");
        Check(emitter.Sizes.Keys.Length == 1 && Near(emitter.Sizes.Start, 1.5f) && Near(emitter.Sizes.Random, 1f), "EXHydrant's size keys.");
        Check(emitter.IgnoredChunks.IsEmpty, "EXHydrant has no chunk the loader skips.");
        foreach (var name in new[] { "EXDrtExp01", "EXLeafFall" })
        {
            Check(library.GetModel(name).Emitter is { RenderMode: W3dEmitterRenderMode.QuadParticles }, $"{name} reads as an emitter model.");
        }
    }

    private static void VersionTwoEmitter()
    {
        var file = Decode(Emitter("SPRAY", "spray.tga", rotationKeys: true, extraChunk: 0x05ff));
        var emitter = file.Emitters.Single();
        Check(file.IgnoredChunks.IsEmpty && file.Meshes.IsEmpty, "An emitter is decoded, not ignored.");
        Check(emitter.Name == "SPRAY" && emitter.Version == W3dEmitterDecoder.CurrentVersion && emitter.UserType == 7 && emitter.UserString == "abc", "Header and user data.");
        Check(emitter.TextureName == "spray.tga" && Near(emitter.Lifetime, 2f) && Near(emitter.EmissionRate, 10f) && Near(emitter.MaxEmissions, 40f), "Info.");
        Check(emitter.Velocity == new Vector3(1, 2, 3) && emitter.Acceleration == new Vector3(0, 0, -9), "Velocity and acceleration.");
        Check(emitter.BurstSize == 3 && emitter.CreationVolume == new W3dVolumeRandomizer(W3dVolumeRandomizerKind.SolidSphere, 4f, 0f, 0f)
              && emitter.VelocityRandom == new W3dVolumeRandomizer(W3dVolumeRandomizerKind.SolidCylinder, 1f, 2f, 0f), "Randomizers.");
        Check(Near(emitter.OutwardVelocity, 5f) && Near(emitter.VelocityInherit, 0.5f), "Outward velocity and inheritance.");
        Check(emitter.Shader is { SourceBlend: W3dSourceBlend.SourceAlpha, DestinationBlend: W3dDestinationBlend.OneMinusSourceAlpha, AlphaTestEnabled: false }, "The shader struct.");
        Check(emitter.RenderMode == W3dEmitterRenderMode.QuadParticles && emitter.FrameMode == W3dEmitterFrameMode.Grid2x2, "Render and frame mode.");
        Check(emitter.Colors.Start == new Vector3(1f, 0f, 0f) && emitter.Colors.Keys.Single() is { Time: 1f } key && key.Value == new Vector3(0f, 0f, 1f), "Colour keys.");
        Check(Near(emitter.Opacities.Start, 1f) && emitter.Opacities.Keys.Single().Value == 0f && Near(emitter.Opacities.Random, 0.25f), "Opacity keys.");
        Check(Near(emitter.Sizes.Start, 2f) && emitter.Sizes.Keys.Length == 2 && Near(emitter.Sizes.Keys[1].Value, 6f) && Near(emitter.Sizes.Random, 0.5f), "Size keys.");
        Check(Near(emitter.Rotations.Start, 0.5f) && Near(emitter.Rotations.Random, 0.1f) && Near(emitter.InitialOrientationRandom, 0.25f)
              && emitter.Rotations.Keys.Single() is { Time: 1f, Value: 2f }, "Rotation keys and the orientation random.");
        Check(emitter.IgnoredChunks.Single().Type == 0x05ff, "A sub-chunk the original's loader skips is kept and counted.");
    }

    private static void EmitterModel()
    {
        var model = W3dModel.Create("SPRAY", Decode(Emitter("SPRAY", "spray.tga")), _ => null);
        Check(model.Emitter?.Name == "SPRAY" && model.SubObjects.IsEmpty && model.LodCount == 1 && model.HierarchySource == W3dHierarchySource.Default, "A file of one emitter is an emitter model.");
        Throws<InvalidDataException>(() => W3dModel.Create("OTHER", Decode(Emitter("SPRAY", "spray.tga")), _ => null), "An emitter of another name is not the model's render object.");
    }

    private static void VersionOneConversion()
    {
        var emitter = Decode(Emitter("OLD", "old.tga", version: 0x00010000)).Emitters.Single();
        Check(emitter.ShaderFromTextureAlpha && emitter.BurstSize == 1 && emitter.RenderMode == W3dEmitterRenderMode.TriangleParticles, "Convert_To_Ver2: one burst, the shader by the texture.");
        Check(emitter.CreationVolume == new W3dVolumeRandomizer(W3dVolumeRandomizerKind.SolidBox, 0.006f, 0.006f, 0.006f), "The position random in thousandths as a box.");
        Check(emitter.VelocityRandom == new W3dVolumeRandomizer(W3dVolumeRandomizerKind.SolidBox, 0.5f, 0.5f, 0.5f), "The velocity random as a box.");
        Check(emitter.Colors.Start == new Vector3(1f, 1f, 1f) && emitter.Colors.Keys.Single() is { Time: 0.75f } end && end.Value == Vector3.Zero, "Colour from start to end over the fade time.");
        Check(Near(emitter.Opacities.Start, 1f) && emitter.Opacities.Keys.Single().Value == 0f && Near(emitter.Sizes.Start, 3f) && emitter.Sizes.Keys.IsEmpty, "Opacity and size.");
    }

    private static void BlackLastColourKey()
    {
        var emitter = Decode(Emitter("BLACK", "b.tga", blackEnd: true)).Emitters.Single();
        Check(emitter.Colors.Keys[^1].Value == -emitter.Colors.Random && emitter.Colors.Random != Vector3.Zero, "Read_Props: a black last key of a randomised colour becomes minus the random.");
    }

    private static void Strictness()
    {
        Fails(Container(W3dChunkType.Emitter, Chunk(W3dEmitterDecoder.Header, [0])), "A truncated emitter header fails.");
        Fails(Container(W3dChunkType.Emitter, Header("X", W3dEmitterDecoder.CurrentVersion)), "An emitter without its user data and info fails.");
        Fails(Emitter("X", "x.tga", randomizerClass: 9), "An unknown randomizer class fails.");
        Fails(Emitter("X", "x.tga", renderMode: 7), "An unknown render mode fails.");
        Fails(Emitter("X", "x.tga", truncateProperties: true), "Properties shorter than their keys fail.");
    }

    // ---------------------------------------------------------------- building emitter files

    private static byte[] Emitter(string name, string texture, uint version = W3dEmitterDecoder.CurrentVersion, bool rotationKeys = false, uint extraChunk = 0,
        uint randomizerClass = 1, uint renderMode = 1, bool truncateProperties = false, bool blackEnd = false)
    {
        var children = new List<byte[]> { Header(name, version) };
        var user = new byte[12 + 3];
        BinaryPrimitives.WriteUInt32LittleEndian(user, 7);
        BinaryPrimitives.WriteUInt32LittleEndian(user.AsSpan(4), 3);
        Encoding.ASCII.GetBytes("abc").CopyTo(user, 12);
        children.Add(Chunk(W3dEmitterDecoder.UserData, user));

        var info = new byte[332];
        Encoding.ASCII.GetBytes(texture).CopyTo(info, 0);
        float[] reals = [3f, 1f, 2f, 10f, 40f, 0.5f, 6f, 0.75f, 0f, 0f, 1f, 2f, 3f, 0f, 0f, -9f];
        for (var index = 0; index < reals.Length; index++) WriteSingle(info, 260 + index * 4, reals[index]);
        info[260 + 64] = 255; info[261 + 64] = 255; info[262 + 64] = 255; info[263 + 64] = 255;
        children.Add(Chunk(W3dEmitterDecoder.Info, info));

        if (version > 0x00010000)
        {
            var v2 = new byte[124];
            BinaryPrimitives.WriteUInt32LittleEndian(v2, 3);
            BinaryPrimitives.WriteUInt32LittleEndian(v2.AsSpan(4), randomizerClass);
            WriteSingle(v2, 8, 4f);
            BinaryPrimitives.WriteUInt32LittleEndian(v2.AsSpan(36), 3);
            WriteSingle(v2, 40, 1f);
            WriteSingle(v2, 44, 2f);
            WriteSingle(v2, 68, 5f);
            WriteSingle(v2, 72, 0.5f);
            var shader = v2.AsSpan(76, 16);
            shader[0] = 3; shader[1] = 0; shader[3] = 5; shader[7] = 2; shader[8] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(v2.AsSpan(92), renderMode);
            BinaryPrimitives.WriteUInt32LittleEndian(v2.AsSpan(96), 1);
            children.Add(Chunk(W3dEmitterDecoder.InfoV2, v2));

            var colors = blackEnd ? 3u : 2u;
            var props = new List<byte>();
            var head = new byte[40];
            BinaryPrimitives.WriteUInt32LittleEndian(head, colors);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(4), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(8), 3);
            if (blackEnd) { head[12] = 51; head[13] = 51; head[14] = 51; }
            WriteSingle(head, 16, 0.25f);
            WriteSingle(head, 20, 0.5f);
            props.AddRange(head);
            props.AddRange(ColorKey(0f, 255, 0, 0));
            props.AddRange(blackEnd ? ColorKey(0.5f, 0, 0, 255) : ColorKey(1f, 0, 0, 255));
            if (blackEnd) props.AddRange(ColorKey(1f, 0, 0, 0));
            props.AddRange(Key(0f, 1f));
            props.AddRange(Key(1f, 0f));
            props.AddRange(Key(0f, 2f));
            props.AddRange(Key(0.5f, 4f));
            props.AddRange(Key(1f, 6f));
            if (truncateProperties) props.RemoveRange(props.Count - 8, 8);
            children.Add(Chunk(W3dEmitterDecoder.Properties, [.. props]));
        }

        if (rotationKeys)
        {
            var rotation = new List<byte>();
            var head = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(head, 1);
            WriteSingle(head, 4, 0.1f);
            WriteSingle(head, 8, 0.25f);
            rotation.AddRange(head);
            rotation.AddRange(Key(0f, 0.5f));
            rotation.AddRange(Key(1f, 2f));
            children.Add(Chunk(W3dEmitterDecoder.RotationKeyframes, [.. rotation]));
        }

        if (extraChunk != 0)
        {
            children.Add(Chunk(extraChunk, [1, 2, 3, 4]));
        }

        return Container(W3dChunkType.Emitter, [.. children]);
    }

    private static byte[] Header(string name, uint version)
    {
        var header = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(header, version);
        Encoding.ASCII.GetBytes(name).CopyTo(header, 4);
        return Chunk(W3dEmitterDecoder.Header, header);
    }

    private static byte[] ColorKey(float time, byte red, byte green, byte blue)
    {
        var key = new byte[8];
        WriteSingle(key, 0, time);
        key[4] = red; key[5] = green; key[6] = blue;
        return key;
    }

    private static byte[] Key(float time, float value)
    {
        var key = new byte[8];
        WriteSingle(key, 0, time);
        WriteSingle(key, 4, value);
        return key;
    }

    private static void WriteSingle(byte[] bytes, int offset, float value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), BitConverter.SingleToInt32Bits(value));

    private static void WriteSingle(Span<byte> bytes, int offset, float value) => BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..], BitConverter.SingleToInt32Bits(value));

    private static byte[] Chunk(uint type, byte[] payload)
    {
        var bytes = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] Container(uint type, params byte[][] children)
    {
        var payload = children.SelectMany(child => child).ToArray();
        var bytes = Chunk(type, payload);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)payload.Length | 0x80000000u);
        return bytes;
    }

    private static W3dAssetFile Decode(byte[] bytes) => W3dAssetFile.Read(new MemoryStream(bytes, writable: false));

    private static void Fails(byte[] bytes, string message) => Throws<InvalidDataException>(() => Decode(bytes), message);

    private static bool Near(float actual, float expected) => MathF.Abs(actual - expected) < 1e-4f;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
