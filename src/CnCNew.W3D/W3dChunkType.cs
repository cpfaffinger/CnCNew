namespace CnCNew.W3D;

public static class W3dChunkType
{
    public const uint Mesh = 0x00000000;
    public const uint Vertices = 0x00000002;
    public const uint VertexNormals = 0x00000003;
    public const uint MeshHeader3 = 0x0000001f;
    public const uint Triangles = 0x00000020;
    public const uint PrelitUnlit = 0x00000023;
    public const uint PrelitVertex = 0x00000024;
    public const uint PrelitLightmapMultiPass = 0x00000025;
    public const uint PrelitLightmapMultiTexture = 0x00000026;
    public const uint MaterialInfo = 0x00000028;
    public const uint Shaders = 0x00000029;
    public const uint VertexMaterials = 0x0000002a;
    public const uint VertexMaterial = 0x0000002b;
    public const uint VertexMaterialName = 0x0000002c;
    public const uint VertexMaterialInfo = 0x0000002d;
    public const uint VertexMapperArgs0 = 0x0000002e;
    public const uint VertexMapperArgs1 = 0x0000002f;
    public const uint Textures = 0x00000030;
    public const uint Texture = 0x00000031;
    public const uint TextureName = 0x00000032;
    public const uint TextureInfo = 0x00000033;
    public const uint MaterialPass = 0x00000038;
    public const uint VertexMaterialIds = 0x00000039;
    public const uint ShaderIds = 0x0000003a;
    public const uint DiffuseColors = 0x0000003b;
    public const uint DiffuseIllumination = 0x0000003c;
    public const uint SpecularColors = 0x0000003e;
    public const uint TextureStage = 0x00000048;
    public const uint TextureIds = 0x00000049;
    public const uint StageTexCoords = 0x0000004a;
    public const uint PerFaceTexCoordIds = 0x0000004b;
}
