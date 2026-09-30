namespace CnCNew.W3D;

public static class W3dChunkType
{
    public const uint Mesh = 0x00000000;
    public const uint Vertices = 0x00000002;
    public const uint VertexNormals = 0x00000003;
    public const uint MeshUserText = 0x0000000c;
    public const uint VertexInfluences = 0x0000000e;
    public const uint MeshHeader3 = 0x0000001f;
    public const uint Triangles = 0x00000020;
    public const uint VertexShadeIndices = 0x00000022;
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
    public const uint Ps2Shaders = 0x00000080;
    public const uint AabTree = 0x00000090;

    public const uint Hierarchy = 0x00000100;
    public const uint HierarchyHeader = 0x00000101;
    public const uint Pivots = 0x00000102;
    public const uint PivotFixups = 0x00000103;

    public const uint Animation = 0x00000200;
    public const uint AnimationHeader = 0x00000201;
    public const uint AnimationChannel = 0x00000202;
    public const uint BitChannel = 0x00000203;

    public const uint CompressedAnimation = 0x00000280;
    public const uint CompressedAnimationHeader = 0x00000281;
    public const uint CompressedAnimationChannel = 0x00000282;
    public const uint CompressedBitChannel = 0x00000283;

    public const uint MorphAnimation = 0x000002c0;
    public const uint HModel = 0x00000300;
    public const uint LodModel = 0x00000400;
    public const uint Collection = 0x00000420;
    public const uint Points = 0x00000440;
    public const uint Light = 0x00000460;
    public const uint Emitter = 0x00000500;
    public const uint Aggregate = 0x00000600;

    public const uint Hlod = 0x00000700;
    public const uint HlodHeader = 0x00000701;
    public const uint HlodLodArray = 0x00000702;
    public const uint HlodSubObjectArrayHeader = 0x00000703;
    public const uint HlodSubObject = 0x00000704;
    public const uint HlodAggregateArray = 0x00000705;
    public const uint HlodProxyArray = 0x00000706;

    public const uint Box = 0x00000740;
    public const uint Sphere = 0x00000741;
    public const uint Ring = 0x00000742;
    public const uint NullObject = 0x00000750;
    public const uint Lightscape = 0x00000800;
    public const uint Dazzle = 0x00000900;
    public const uint SoundRenderObject = 0x00000a00;
    public const uint ShaderMesh = 0x00000b00;
}
