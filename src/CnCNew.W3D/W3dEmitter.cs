using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.W3D;

/// <summary><c>W3D_EMITTER_RENDER_MODE_*</c> of <c>W3dEmitterInfoStructV2</c>: particles drawn as triangles or quads, or the line kinds.</summary>
public enum W3dEmitterRenderMode : uint
{
    TriangleParticles = 0,
    QuadParticles = 1,
    Line = 2,
    LineGroupTetrahedron = 3,
    LineGroupPrism = 4,
}

/// <summary><c>W3D_EMITTER_FRAME_MODE_*</c>: the texture cut into a grid of 1, 4, 16, 64 or 256 frames.</summary>
public enum W3dEmitterFrameMode : uint
{
    Grid1x1 = 0,
    Grid2x2 = 1,
    Grid4x4 = 2,
    Grid8x8 = 3,
    Grid16x16 = 4,
}

/// <summary><c>Vector3Randomizer</c>'s class ids (<c>v3_rnd.h</c>) as <c>W3dVolumeRandomizerStruct</c> stores them.</summary>
public enum W3dVolumeRandomizerKind : uint
{
    /// <summary><c>Vector3SolidBoxRandomizer</c>: every component in [-extent, extent]; the values are the three extents.</summary>
    SolidBox = 0,

    /// <summary><c>Vector3SolidSphereRandomizer</c>: inside a sphere; the first value is its radius.</summary>
    SolidSphere = 1,

    /// <summary><c>Vector3HollowSphereRandomizer</c>: on a sphere; the first value is its radius.</summary>
    HollowSphere = 2,

    /// <summary><c>Vector3SolidCylinderRandomizer</c>: X in [-extent, extent], Y and Z inside a circle; the values are extent and radius.</summary>
    SolidCylinder = 3,
}

/// <summary><c>W3dVolumeRandomizerStruct</c>: a randomizer's kind and its three values (the four reserved words are not kept).</summary>
public readonly record struct W3dVolumeRandomizer(W3dVolumeRandomizerKind Kind, float Value1, float Value2, float Value3);

/// <summary>One keyframe of an emitter property: its time in seconds of a particle's age and its value.</summary>
public readonly record struct W3dEmitterKey<T>(float Time, T Value);

/// <summary>
/// <c>ParticlePropertyStruct</c> as <c>ParticleEmitterDefClass</c> keeps it: the value at age 0, the random range added per particle and the keyframes after the start.
/// Colours are red, green and blue in 0..1 (<c>RGBA_TO_VECTOR3</c>).
/// </summary>
public sealed record W3dEmitterProperty<T>(T Start, T Random, ImmutableArray<W3dEmitterKey<T>> Keys) where T : struct;

/// <summary><c>W3dEmitterLinePropertiesStruct</c> (<c>W3D_CHUNK_EMITTER_LINE_PROPERTIES</c>), the settings of the line render modes.</summary>
public sealed record W3dEmitterLineProperties(uint Flags, uint SubdivisionLevel, float NoiseAmplitude, float MergeAbortFactor, float TextureTileFactor, float UPerSecond, float VPerSecond);

/// <summary>A sub-chunk of an emitter the original's loader does not handle (<c>ParticleEmitterDefClass::Load_W3D</c> says "Unhandled Chunk!" and skips it): kept and counted.</summary>
public sealed record W3dEmitterIgnoredChunk(uint Type, long HeaderOffset);

/// <summary>
/// A particle emitter definition (<c>W3D_CHUNK_EMITTER</c>), read as <c>ParticleEmitterDefClass::Load_W3D</c> reads it: header, user data and the version 1 info in this order,
/// for version 2 the second info and the keyframed properties after them, then the optional chunks (line properties, rotation, frame and blur-time keyframes, extra info) in any
/// order. A version 1 emitter is converted as <c>Convert_To_Ver2</c> does, except its shader: the original picks the alpha sprite shader when the texture has an alpha channel and the
/// additive one otherwise, which only the texture can say; <see cref="Shader"/> is then null and <see cref="ShaderFromTextureAlpha"/> true. Times are seconds, velocities units per
/// second, accelerations units per second squared, as the file states them.
/// </summary>
public sealed record W3dEmitter(
    uint Version,
    string Name,
    uint UserType,
    string UserString,
    string TextureName,
    float StartSize,
    float EndSize,
    float Lifetime,
    float EmissionRate,
    float MaxEmissions,
    float FadeTime,
    float Gravity,
    float Elasticity,
    Vector3 Velocity,
    Vector3 Acceleration,
    W3dRgba StartColor,
    W3dRgba EndColor,
    uint BurstSize,
    W3dVolumeRandomizer CreationVolume,
    W3dVolumeRandomizer VelocityRandom,
    float OutwardVelocity,
    float VelocityInherit,
    W3dShader? Shader,
    W3dEmitterRenderMode RenderMode,
    W3dEmitterFrameMode FrameMode,
    W3dEmitterProperty<Vector3> Colors,
    W3dEmitterProperty<float> Opacities,
    W3dEmitterProperty<float> Sizes,
    W3dEmitterProperty<float> Rotations,
    float InitialOrientationRandom,
    W3dEmitterProperty<float> Frames,
    W3dEmitterProperty<float> BlurTimes,
    W3dEmitterLineProperties? LineProperties,
    float FutureStartTime,
    ImmutableArray<W3dEmitterIgnoredChunk> IgnoredChunks)
{
    /// <summary>True for a version 1 emitter: the original chooses its shader by the texture's alpha channel (<c>Convert_To_Ver2</c>).</summary>
    public bool ShaderFromTextureAlpha => Shader is null;
}

public static class W3dEmitterDecoder
{
    public const uint CurrentVersion = 0x00020000;
    public const uint Header = 0x0501;
    public const uint UserData = 0x0502;
    public const uint Info = 0x0503;
    public const uint InfoV2 = 0x0504;
    public const uint Properties = 0x0505;
    public const uint LineProperties = 0x0509;
    public const uint RotationKeyframes = 0x050a;
    public const uint FrameKeyframes = 0x050b;
    public const uint BlurTimeKeyframes = 0x050c;
    public const uint ExtraInfo = 0x050d;

    private const int HeaderSize = 4 + W3dBinary.NameLength;
    private const int UserDataSize = 12;
    private const int TextureNameLength = 260;
    private const int InfoSize = TextureNameLength + 16 * 4 + 8;
    private const int RandomizerSize = 32;
    private const int InfoV2Size = 4 + 2 * RandomizerSize + 8 + 16 + 8 + 24;
    private const int PropertiesHeaderSize = 40;
    private const int KeySize = 8;
    private const int LinePropertiesSize = 64;
    private const int ExtraInfoSize = 40;

    public static W3dEmitter Decode(ReadOnlySpan<byte> data, W3dChunk emitter)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        W3dBinary.RequireContainer(emitter, "Emitter");
        var children = emitter.Children;
        W3dChunk Expect(int index, uint type, string description)
        {
            if (index >= children.Count || children[index].Type != type)
            {
                throw new InvalidDataException(
                    $"Emitter at {emitter.HeaderOffset} lacks its {description} chunk 0x{type:X8} at position {index} (ParticleEmitterDefClass::Load_W3D reads it there).");
            }

            return W3dBinary.RequireLeaf(children[index], $"Emitter {description}");
        }

        var header = W3dBinary.Slice(data, W3dBinary.RequireSize(Expect(0, Header, "header"), HeaderSize, "Emitter header"));
        var version = W3dBinary.ReadUInt32(header, 0);
        var name = W3dBinary.ReadFixedString(header.Slice(4, W3dBinary.NameLength));
        string Where() => $"Emitter '{name}'";

        var userChunk = Expect(1, UserData, "user data");
        if (userChunk.Size < UserDataSize)
        {
            throw new InvalidDataException($"{Where()} user data has {userChunk.Size} bytes; expected at least {UserDataSize}.");
        }

        var user = W3dBinary.Slice(data, userChunk);
        var userType = W3dBinary.ReadUInt32(user, 0);
        var userStringLength = W3dBinary.ReadUInt32(user, 4);
        // Read_User_Data reads the whole struct (twelve bytes with its one character and padding) and the string after it.
        if (userChunk.Size != UserDataSize + (long)userStringLength)
        {
            throw new InvalidDataException($"{Where()} user data has {userChunk.Size} bytes; its string of {userStringLength} needs {UserDataSize + (long)userStringLength}.");
        }

        var userString = W3dBinary.ReadFixedString(user.Slice(UserDataSize, (int)userStringLength));

        var info = W3dBinary.Slice(data, W3dBinary.RequireSize(Expect(2, Info, "info"), InfoSize, "Emitter info"));
        var textureName = W3dBinary.ReadFixedString(info[..TextureNameLength]);
        var startSize = W3dBinary.ReadSingle(info, TextureNameLength);
        var endSize = W3dBinary.ReadSingle(info, TextureNameLength + 4);
        var lifetime = W3dBinary.ReadSingle(info, TextureNameLength + 8);
        var emissionRate = W3dBinary.ReadSingle(info, TextureNameLength + 12);
        var maxEmissions = W3dBinary.ReadSingle(info, TextureNameLength + 16);
        var velocityRandom1 = W3dBinary.ReadSingle(info, TextureNameLength + 20);
        var positionRandom1 = W3dBinary.ReadSingle(info, TextureNameLength + 24);
        var fadeTime = W3dBinary.ReadSingle(info, TextureNameLength + 28);
        var gravity = W3dBinary.ReadSingle(info, TextureNameLength + 32);
        var elasticity = W3dBinary.ReadSingle(info, TextureNameLength + 36);
        var velocity = W3dBinary.ReadVector3(info, TextureNameLength + 40);
        var acceleration = W3dBinary.ReadVector3(info, TextureNameLength + 52);
        var startColor = ReadRgba(info, TextureNameLength + 64);
        var endColor = ReadRgba(info, TextureNameLength + 68);

        var next = 3;
        uint burstSize;
        W3dVolumeRandomizer creationVolume, velocityRandomizer;
        float outward, inherit;
        W3dShader? shader;
        W3dEmitterRenderMode renderMode;
        W3dEmitterFrameMode frameMode;
        W3dEmitterProperty<Vector3> colors;
        W3dEmitterProperty<float> opacities, sizes;
        if (version > 0x00010000)
        {
            var v2 = W3dBinary.Slice(data, W3dBinary.RequireSize(Expect(next++, InfoV2, "second info"), InfoV2Size, "Emitter second info"));
            burstSize = W3dBinary.ReadUInt32(v2, 0);
            creationVolume = ReadRandomizer(v2, 4, Where(), "creation volume");
            velocityRandomizer = ReadRandomizer(v2, 4 + RandomizerSize, Where(), "velocity randomizer");
            outward = W3dBinary.ReadSingle(v2, 4 + 2 * RandomizerSize);
            inherit = W3dBinary.ReadSingle(v2, 8 + 2 * RandomizerSize);
            shader = W3dMeshDecoder.DecodeShader(v2.Slice(12 + 2 * RandomizerSize, 16), $"{Where()} shader");
            var mode = W3dBinary.ReadUInt32(v2, 28 + 2 * RandomizerSize);
            var frames = W3dBinary.ReadUInt32(v2, 32 + 2 * RandomizerSize);
            if (!Enum.IsDefined((W3dEmitterRenderMode)mode) || !Enum.IsDefined((W3dEmitterFrameMode)frames))
            {
                throw new InvalidDataException($"{Where()} has the unknown render mode {mode} or frame mode {frames}.");
            }

            renderMode = (W3dEmitterRenderMode)mode;
            frameMode = (W3dEmitterFrameMode)frames;
            (colors, opacities, sizes) = ReadProperties(data, Expect(next++, Properties, "properties"), Where());
        }
        else
        {
            // Convert_To_Ver2: one burst, the old randomizers as boxes (the position random in thousandths), and colour, opacity and size from the start and end values.
            burstSize = 1;
            outward = 0f;
            inherit = 0f;
            shader = null;
            renderMode = W3dEmitterRenderMode.TriangleParticles;
            frameMode = W3dEmitterFrameMode.Grid1x1;
            var position = positionRandom1 / 1000f;
            creationVolume = new W3dVolumeRandomizer(W3dVolumeRandomizerKind.SolidBox, position, position, position);
            velocityRandomizer = new W3dVolumeRandomizer(W3dVolumeRandomizerKind.SolidBox, velocityRandom1, velocityRandom1, velocityRandom1);
            colors = new W3dEmitterProperty<Vector3>(ToVector(startColor), Vector3.Zero, [new W3dEmitterKey<Vector3>(fadeTime, ToVector(endColor))]);
            opacities = new W3dEmitterProperty<float>(startColor.Alpha / 255f, 0f, [new W3dEmitterKey<float>(fadeTime, endColor.Alpha / 255f)]);
            sizes = new W3dEmitterProperty<float>(startSize, 0f, []);
        }

        var none = new W3dEmitterProperty<float>(0f, 0f, []);
        var rotations = none;
        var frameKeys = none;
        var blurTimes = none;
        var orientationRandom = 0f;
        W3dEmitterLineProperties? lineProperties = null;
        var futureStart = 0f;
        var ignored = ImmutableArray.CreateBuilder<W3dEmitterIgnoredChunk>();
        for (; next < children.Count; next++)
        {
            var child = children[next];
            switch (child.Type)
            {
                case LineProperties:
                {
                    var line = W3dBinary.Slice(data, W3dBinary.RequireSize(W3dBinary.RequireLeaf(child, "Emitter line properties"), LinePropertiesSize, "Emitter line properties"));
                    lineProperties = new W3dEmitterLineProperties(W3dBinary.ReadUInt32(line, 0), W3dBinary.ReadUInt32(line, 4), W3dBinary.ReadSingle(line, 8),
                        W3dBinary.ReadSingle(line, 12), W3dBinary.ReadSingle(line, 16), W3dBinary.ReadSingle(line, 20), W3dBinary.ReadSingle(line, 24));
                    break;
                }
                case RotationKeyframes:
                    // W3dEmitterRotationHeaderStruct: count, random rotation (rotations per second), orientation random (rotations), one reserved word.
                    (rotations, orientationRandom) = ReadKeyframes(data, child, 16, Where(), "rotation", hasOrientation: true);
                    break;
                case FrameKeyframes:
                    // W3dEmitterFrameHeaderStruct: count, random, two reserved words.
                    frameKeys = ReadKeyframes(data, child, 16, Where(), "frame", hasOrientation: false).Keys;
                    break;
                case BlurTimeKeyframes:
                    // W3dEmitterBlurTimeHeaderStruct: count, random, one reserved word.
                    blurTimes = ReadKeyframes(data, child, 12, Where(), "blur time", hasOrientation: false).Keys;
                    break;
                case ExtraInfo:
                    futureStart = W3dBinary.ReadSingle(W3dBinary.Slice(data, W3dBinary.RequireSize(W3dBinary.RequireLeaf(child, "Emitter extra info"), ExtraInfoSize, "Emitter extra info")), 0);
                    break;
                default:
                    ignored.Add(new W3dEmitterIgnoredChunk(child.Type, child.HeaderOffset));
                    break;
            }
        }

        if (!float.IsFinite(lifetime) || !float.IsFinite(emissionRate) || !W3dBinary.IsFinite(velocity) || !W3dBinary.IsFinite(acceleration))
        {
            throw new InvalidDataException($"{Where()} has a lifetime, emission rate, velocity or acceleration that is not a number.");
        }

        return new W3dEmitter(version, name, userType, userString, textureName, startSize, endSize, lifetime, emissionRate, maxEmissions, fadeTime, gravity, elasticity,
            velocity, acceleration, startColor, endColor, burstSize, creationVolume, velocityRandomizer, outward, inherit, shader, renderMode, frameMode, colors, opacities, sizes,
            rotations, orientationRandom, frameKeys, blurTimes, lineProperties, futureStart, ignored.ToImmutable());
    }

    private static W3dRgba ReadRgba(ReadOnlySpan<byte> data, int offset) => new(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]);

    private static Vector3 ToVector(W3dRgba color) => new(color.Red / 255f, color.Green / 255f, color.Blue / 255f);

    private static W3dVolumeRandomizer ReadRandomizer(ReadOnlySpan<byte> data, int offset, string where, string description)
    {
        var kind = W3dBinary.ReadUInt32(data, offset);
        // Create_Randomizer leaves an unknown class id without a randomizer (no offset at all); here it is a decode error, never a silent zero.
        if (!Enum.IsDefined((W3dVolumeRandomizerKind)kind))
        {
            throw new InvalidDataException($"{where} {description} has the unknown randomizer class {kind}.");
        }

        return new W3dVolumeRandomizer((W3dVolumeRandomizerKind)kind, W3dBinary.ReadSingle(data, offset + 4), W3dBinary.ReadSingle(data, offset + 8), W3dBinary.ReadSingle(data, offset + 12));
    }

    /// <summary>
    /// <c>Read_Props</c>: the counts of colour, opacity and size keys (each including the start key), the colour, opacity and size random, then the keys. As there, when the
    /// last of at least two colour keys after the start is black and the colour is randomised, that key becomes minus the random, so every randomised colour ends black.
    /// </summary>
    private static (W3dEmitterProperty<Vector3>, W3dEmitterProperty<float>, W3dEmitterProperty<float>) ReadProperties(ReadOnlySpan<byte> data, W3dChunk chunk, string where)
    {
        var all = W3dBinary.Slice(data, chunk);
        if (all.Length < PropertiesHeaderSize)
        {
            throw new InvalidDataException($"{where} properties have {all.Length} bytes; expected at least {PropertiesHeaderSize}.");
        }

        var colorCount = W3dBinary.ReadUInt32(all, 0);
        var opacityCount = W3dBinary.ReadUInt32(all, 4);
        var sizeCount = W3dBinary.ReadUInt32(all, 8);
        if (colorCount is 0 or > 256 || opacityCount is 0 or > 256 || sizeCount is 0 or > 256)
        {
            throw new InvalidDataException($"{where} properties count {colorCount} colour, {opacityCount} opacity and {sizeCount} size keys; each needs its start key and at most 256.");
        }

        var expected = PropertiesHeaderSize + (int)(colorCount + opacityCount + sizeCount) * KeySize;
        if (all.Length != expected)
        {
            throw new InvalidDataException($"{where} properties have {all.Length} bytes; their keys need {expected}.");
        }

        var colorRandom = ToVector(ReadRgba(all, 12));
        var opacityRandom = W3dBinary.ReadSingle(all, 16);
        var sizeRandom = W3dBinary.ReadSingle(all, 20);
        var offset = PropertiesHeaderSize;
        var colorKeys = new W3dEmitterKey<Vector3>[colorCount];
        for (var index = 0; index < colorKeys.Length; index++, offset += KeySize)
        {
            colorKeys[index] = new W3dEmitterKey<Vector3>(W3dBinary.ReadSingle(all, offset), ToVector(ReadRgba(all, offset + 4)));
        }

        var last = colorKeys.Length - 1;
        if (last >= 2 && colorKeys[last].Value == Vector3.Zero && (colorRandom.X > 0f || colorRandom.Y > 0f || colorRandom.Z > 0f))
        {
            colorKeys[last] = colorKeys[last] with { Value = -colorRandom };
        }

        var opacityKeys = ReadScalarKeys(all, ref offset, opacityCount);
        var sizeKeys = ReadScalarKeys(all, ref offset, sizeCount);
        return (
            new W3dEmitterProperty<Vector3>(colorKeys[0].Value, colorRandom, [.. colorKeys.Skip(1)]),
            new W3dEmitterProperty<float>(opacityKeys[0].Value, opacityRandom, [.. opacityKeys.Skip(1)]),
            new W3dEmitterProperty<float>(sizeKeys[0].Value, sizeRandom, [.. sizeKeys.Skip(1)]));
    }

    private static W3dEmitterKey<float>[] ReadScalarKeys(ReadOnlySpan<byte> all, ref int offset, uint count)
    {
        var keys = new W3dEmitterKey<float>[count];
        for (var index = 0; index < keys.Length; index++, offset += KeySize)
        {
            keys[index] = new W3dEmitterKey<float>(W3dBinary.ReadSingle(all, offset), W3dBinary.ReadSingle(all, offset + 4));
        }

        return keys;
    }

    /// <summary>
    /// <c>Read_Rotation_Keyframes</c>, <c>Read_Frame_Keyframes</c>, <c>Read_Blur_Time_Keyframes</c>: a header whose first word counts the keys after the start key and whose second is the
    /// random range, then the start key and the keys. The rotation header's third word is the random initial orientation.
    /// </summary>
    private static (W3dEmitterProperty<float> Keys, float Orientation) ReadKeyframes(ReadOnlySpan<byte> data, W3dChunk chunk, int headerSize, string where, string description, bool hasOrientation)
    {
        W3dBinary.RequireLeaf(chunk, $"Emitter {description} keyframes");
        var all = W3dBinary.Slice(data, chunk);
        if (all.Length < headerSize + KeySize)
        {
            throw new InvalidDataException($"{where} {description} keyframes have {all.Length} bytes; expected at least {headerSize + KeySize}.");
        }

        var count = W3dBinary.ReadUInt32(all, 0);
        if (count > 256 || all.Length != headerSize + (int)(count + 1) * KeySize)
        {
            throw new InvalidDataException($"{where} {description} keyframes have {all.Length} bytes for {count} keys after the start key.");
        }

        var random = W3dBinary.ReadSingle(all, 4);
        var orientation = hasOrientation ? W3dBinary.ReadSingle(all, 8) : 0f;
        var start = W3dBinary.ReadSingle(all, headerSize + 4);
        var keys = ImmutableArray.CreateBuilder<W3dEmitterKey<float>>((int)count);
        for (var index = 1; index <= count; index++)
        {
            var offset = headerSize + index * KeySize;
            keys.Add(new W3dEmitterKey<float>(W3dBinary.ReadSingle(all, offset), W3dBinary.ReadSingle(all, offset + 4)));
        }

        return (new W3dEmitterProperty<float>(start, random, keys.MoveToImmutable()), orientation);
    }
}
