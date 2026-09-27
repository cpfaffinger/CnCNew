using System.Numerics;

namespace CnCNew.W3D;

public sealed record W3dMaterialData(
    int DeclaredPassCount,
    int DeclaredVertexMaterialCount,
    int DeclaredShaderCount,
    int DeclaredTextureCount,
    IReadOnlyList<W3dShader> Shaders,
    IReadOnlyList<W3dVertexMaterial> VertexMaterials,
    IReadOnlyList<W3dTextureReference> Textures,
    IReadOnlyList<W3dMaterialPass> Passes)
{
    public static W3dMaterialData Empty { get; } = new(0, 0, 0, 0, [], [], [], []);
}

public sealed record W3dTextureReference(string Name);

public sealed record W3dMaterialPass(
    IReadOnlyList<uint> VertexMaterialIds,
    IReadOnlyList<uint> ShaderIds,
    IReadOnlyList<W3dRgba> DiffuseColors,
    IReadOnlyList<W3dTextureStage> TextureStages);

public sealed record W3dTextureStage(
    IReadOnlyList<uint> TextureIds,
    IReadOnlyList<Vector2> TextureCoordinates,
    IReadOnlyList<W3dTriangleIndices> PerFaceTextureCoordinateIndices);

public readonly record struct W3dTriangleIndices(uint Index0, uint Index1, uint Index2);

public readonly record struct W3dRgb(byte Red, byte Green, byte Blue);

public readonly record struct W3dRgba(byte Red, byte Green, byte Blue, byte Alpha);

public sealed record W3dVertexMaterial(
    string Name,
    uint Attributes,
    W3dRgb Ambient,
    W3dRgb Diffuse,
    W3dRgb Specular,
    W3dRgb Emissive,
    float Shininess,
    float Opacity,
    float Translucency,
    string? MapperArguments0,
    string? MapperArguments1);

public sealed record W3dShader(
    W3dDepthCompare DepthCompare,
    bool DepthWriteEnabled,
    byte ColorMask,
    W3dDestinationBlend DestinationBlend,
    byte FogFunction,
    W3dPrimaryGradient PrimaryGradient,
    byte SecondaryGradient,
    W3dSourceBlend SourceBlend,
    bool TexturingEnabled,
    byte DetailColorFunction,
    byte DetailAlphaFunction,
    byte ShaderPreset,
    bool AlphaTestEnabled,
    byte PostDetailColorFunction,
    byte PostDetailAlphaFunction,
    byte Padding);

public enum W3dDepthCompare : byte
{
    Never = 0,
    Less = 1,
    Equal = 2,
    LessOrEqual = 3,
    Greater = 4,
    NotEqual = 5,
    GreaterOrEqual = 6,
    Always = 7,
}

public enum W3dDestinationBlend : byte
{
    Zero = 0,
    One = 1,
    SourceColor = 2,
    OneMinusSourceColor = 3,
    SourceAlpha = 4,
    OneMinusSourceAlpha = 5,
    SourceColorPreFog = 6,
}

public enum W3dSourceBlend : byte
{
    Zero = 0,
    One = 1,
    SourceAlpha = 2,
    OneMinusSourceAlpha = 3,
}

public enum W3dPrimaryGradient : byte
{
    Disabled = 0,
    Modulate = 1,
    Add = 2,
    BumpEnvironmentMap = 3,
    BumpEnvironmentMapLuminance = 4,
    Modulate2X = 5,
}

public enum W3dMaterialVariantKind
{
    Default,
    PrelitUnlit,
    PrelitVertex,
    PrelitLightmapMultiPass,
    PrelitLightmapMultiTexture,
}

public sealed record W3dMaterialVariant(W3dMaterialVariantKind Kind, W3dMaterialData Materials);
