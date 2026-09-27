using CnCNew.Content;
using CnCNew.Images;
using CnCNew.W3D;
using System.Runtime.InteropServices;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

var smokeTest = args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
var contentDirectory = GetArgument(args, "--content-dir");
var modelPath = GetArgument(args, "--model-path");
if (modelPath is not null && contentDirectory is null)
{
    throw new ArgumentException("--model-path requires --content-dir.");
}

if (contentDirectory is not null)
{
    var content = ContentFileSystem.Open(contentDirectory);
    if (content.FileCount == 0 || content.EnumerateFiles().Any(path => path.EndsWith(".big", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidDataException("The runtime content directory is empty or still contains BIG archives.");
    }

    using var firstFile = content.OpenRead(content.EnumerateFiles().First());
}

using var game = new CnCNewGame(smokeTest, contentDirectory, modelPath);
game.Run();

static string? GetArgument(string[] arguments, string name)
{
    var index = Array.FindIndex(arguments, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (index < 0)
    {
        return null;
    }

    return index + 1 < arguments.Length
        ? arguments[index + 1]
        : throw new ArgumentException($"{name} requires a value.");
}

internal sealed class CnCNewGame(bool smokeTest, string? contentDirectory, string? modelPath) : Game
{
    private int _renderedFrames;
    private Entity? _modelEntity;
    private readonly List<Texture> _runtimeTextures = [];
    private readonly Dictionary<MaterialPass, bool> _depthWrites = [];

    protected override void Initialize()
    {
        AutoLoadDefaultSettings = false;
        GraphicsDeviceManager.PreferredBackBufferWidth = 1280;
        GraphicsDeviceManager.PreferredBackBufferHeight = 720;
        base.Initialize();
        Window.Title = "CnCNew — Stride Direct3D 11 W3D Viewer";
    }

    protected override async Task LoadContent()
    {
        await base.LoadContent();
        if (contentDirectory is null || modelPath is null)
        {
            return;
        }

        var content = ContentFileSystem.Open(contentDirectory);
        using var stream = content.OpenRead(modelPath);
        var source = W3dFileReader.Read(stream);
        var sourceMeshes = W3dMeshDecoder.Decode(source).Where(mesh => mesh.Triangles.Count > 0).ToArray();
        if (sourceMeshes.Length == 0)
        {
            throw new InvalidDataException($"W3D model '{modelPath}' contains no renderable meshes.");
        }

        CreateModelScene(sourceMeshes, content);
        Window.Title = $"CnCNew — {modelPath} — {sourceMeshes.Length} meshes — {_runtimeTextures.Count} textures";
        if (smokeTest)
        {
            Console.WriteLine(
                $"Loaded {sourceMeshes.Length} W3D meshes and {_runtimeTextures.Count} runtime textures from direct content.");
        }
    }

    protected override void UnloadContent()
    {
        foreach (var texture in _runtimeTextures)
        {
            texture.Dispose();
        }

        _runtimeTextures.Clear();
        base.UnloadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (_modelEntity is not null && !smokeTest)
        {
            _modelEntity.Transform.Rotation = Quaternion.RotationY((float)gameTime.Total.TotalSeconds * 0.2f);
        }

        if (smokeTest && ++_renderedFrames >= 10)
        {
            Exit();
        }
    }

    private void CreateModelScene(IReadOnlyList<W3dMesh> sourceMeshes, ContentFileSystem content)
    {
        var scene = new Scene();
        var model = new Model();
        var fallbackMaterial = Material.New(GraphicsDevice, new MaterialDescriptor
        {
            Attributes =
            {
                Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(new Color4(0.68f, 0.76f, 0.86f, 1))),
                DiffuseModel = new MaterialDiffuseLambertModelFeature(),
            },
        });
        model.Materials.Add(fallbackMaterial);
        var materialIndices = new Dictionary<RenderMaterialKey, int>();

        var allPositions = sourceMeshes.SelectMany(mesh => mesh.Vertices).ToArray();
        var minimum = new System.Numerics.Vector3(
            allPositions.Min(position => position.X),
            allPositions.Min(position => position.Y),
            allPositions.Min(position => position.Z));
        var maximum = new System.Numerics.Vector3(
            allPositions.Max(position => position.X),
            allPositions.Max(position => position.Y),
            allPositions.Max(position => position.Z));
        var center = (minimum + maximum) * 0.5f;
        var extent = maximum - minimum;
        var scale = 2.5f / Math.Max(extent.X, Math.Max(extent.Y, extent.Z));

        foreach (var sourceMesh in sourceMeshes)
        {
            foreach (var batch in W3dRenderPlan.Build(sourceMesh))
            {
                if (batch.MaterialPass?.TextureStages.Count > 2)
                {
                    throw new NotSupportedException(
                        $"Mesh '{sourceMesh.Name}' pass {batch.MaterialPassIndex} has more than two texture stages.");
                }

                var stage0 = batch.MaterialPass?.TextureStages.ElementAtOrDefault(0);
                var stage1 = batch.MaterialPass?.TextureStages.ElementAtOrDefault(1);
                var vertices = new List<W3dRenderVertex>();
                foreach (var triangleIndex in batch.TriangleIndices)
                {
                    var triangle = sourceMesh.Triangles[triangleIndex];
                    AddVertex(vertices, sourceMesh, batch.MaterialPass, stage0, stage1, triangleIndex, 0, triangle.Vertex0, center, scale);
                    AddVertex(vertices, sourceMesh, batch.MaterialPass, stage0, stage1, triangleIndex, 1, triangle.Vertex1, center, scale);
                    AddVertex(vertices, sourceMesh, batch.MaterialPass, stage0, stage1, triangleIndex, 2, triangle.Vertex2, center, scale);
                }

                var indices = Enumerable.Range(0, vertices.Count).ToArray();
                var vertexBuffer = Stride.Graphics.Buffer.Vertex.New(GraphicsDevice, vertices.ToArray());
                var indexBuffer = Stride.Graphics.Buffer.Index.New(GraphicsDevice, indices);
                var draw = new MeshDraw
                {
                    StartLocation = 0,
                    PrimitiveType = PrimitiveType.TriangleList,
                    DrawCount = indices.Length,
                    VertexBuffers =
                    [
                        new VertexBufferBinding(vertexBuffer, W3dRenderVertex.Layout, vertices.Count),
                    ],
                    IndexBuffer = new IndexBufferBinding(indexBuffer, true, indices.Length),
                };
                model.Meshes.Add(new Mesh
                {
                    Draw = draw,
                    MaterialIndex = GetMaterialIndex(
                        model,
                        content,
                        sourceMesh,
                        batch,
                        materialIndices),
                    BoundingBox = new BoundingBox(new Vector3(-2), new Vector3(2)),
                    BoundingSphere = new BoundingSphere(Vector3.Zero, 2),
                });
            }
        }

        model.BoundingBox = new BoundingBox(new Vector3(-2), new Vector3(2));
        model.BoundingSphere = new BoundingSphere(Vector3.Zero, 2);
        _modelEntity = new Entity("W3D model") { new ModelComponent(model) };
        scene.Entities.Add(_modelEntity);

        var compositor = GraphicsCompositorHelper.CreateDefault(false);
        var meshFeature = compositor.RenderFeatures.OfType<MeshRenderFeature>().Single();
        var stageSelector = meshFeature.RenderStageSelectors.OfType<MeshTransparentRenderStageSelector>().Single();
        meshFeature.PipelineProcessors.Add(new W3dDepthWritePipelineProcessor(
            _depthWrites,
            stageSelector.OpaqueRenderStage,
            stageSelector.TransparentRenderStage));
        SceneSystem.GraphicsCompositor = compositor;
        var camera = new CameraComponent
        {
            Slot = Services.GetSafeServiceAs<SceneSystem>().GraphicsCompositor.Cameras[0].ToSlotId(),
            NearClipPlane = 0.05f,
            FarClipPlane = 100,
        };
        var cameraEntity = new Entity("Camera") { camera };
        cameraEntity.Transform.Position = new Vector3(0, 0.35f, 4);
        scene.Entities.Add(cameraEntity);

        var lightEntity = new Entity("Key light") { new LightComponent() };
        lightEntity.Transform.Rotation = Quaternion.RotationYawPitchRoll(-0.8f, -0.7f, 0);
        scene.Entities.Add(lightEntity);
        SceneSystem.SceneInstance = new SceneInstance(Services, scene);
    }

    private static void AddVertex(
        List<W3dRenderVertex> output,
        W3dMesh mesh,
        W3dMaterialPass? pass,
        W3dTextureStage? stage0,
        W3dTextureStage? stage1,
        int triangleIndex,
        int corner,
        uint vertexIndex,
        System.Numerics.Vector3 center,
        float scale)
    {
        var index = checked((int)vertexIndex);
        var sourcePosition = (mesh.Vertices[index] - center) * scale;
        var sourceNormal = mesh.Normals.Count == mesh.Vertices.Count
            ? mesh.Normals[index]
            : System.Numerics.Vector3.UnitZ;
        var position = new Vector3(sourcePosition.X, sourcePosition.Z, -sourcePosition.Y);
        var normal = Vector3.Normalize(new Vector3(sourceNormal.X, sourceNormal.Z, -sourceNormal.Y));
        var color = W3dRenderPlan.GetVertexColor(mesh, pass, index);
        var textureCoordinate0 = W3dRenderPlan.GetTextureCoordinate(stage0, triangleIndex, corner, index);
        var textureCoordinate1 = W3dRenderPlan.GetTextureCoordinate(stage1, triangleIndex, corner, index);
        output.Add(new W3dRenderVertex(
            position,
            normal,
            new Color(color.Red, color.Green, color.Blue, color.Alpha),
            textureCoordinate0,
            textureCoordinate1));
    }

    private int GetMaterialIndex(
        Model model,
        ContentFileSystem content,
        W3dMesh mesh,
        W3dRenderBatch batch,
        Dictionary<RenderMaterialKey, int> materialIndices)
    {
        var path0 = ResolveTexturePath(content, mesh, batch.TextureIds.ElementAtOrDefault(0));
        var path1 = ResolveTexturePath(content, mesh, batch.TextureIds.ElementAtOrDefault(1));
        var shader = batch.Shader;
        if (shader?.TexturingEnabled == false)
        {
            path0 = null;
            path1 = null;
        }

        var key = new RenderMaterialKey(path0, path1, shader, mesh.IsTwoSided);
        if (materialIndices.TryGetValue(key, out var existingIndex))
        {
            return existingIndex;
        }

        var texture0 = CreateTextureColor(content, path0, TextureCoordinate.Texcoord0);
        var vertexColor = new ComputeVertexStreamColor { Stream = new ColorVertexStreamDefinition() };
        IComputeColor surfaceColor = ApplyPrimaryGradient(texture0, vertexColor, shader, path0 is not null);
        if (shader?.TexturingEnabled != false && batch.TextureIds.Count > 1)
        {
            var texture1 = CreateTextureColor(content, path1, TextureCoordinate.Texcoord1);
            surfaceColor = ApplyDetail(surfaceColor, texture1, batch.DetailColorFunction, batch.DetailAlphaFunction);
        }

        var attributes = new MaterialAttributes
        {
            Diffuse = new MaterialDiffuseMapFeature(surfaceColor),
            DiffuseModel = new MaterialDiffuseLambertModelFeature(),
            CullMode = mesh.IsTwoSided ? CullMode.None : CullMode.Back,
        };
        if (shader?.AlphaTestEnabled == true)
        {
            attributes.Transparency = new MaterialTransparencyCutoffFeature
            {
                Alpha = new ComputeFloat(96f / 255f),
            };
        }

        var material = Material.New(GraphicsDevice, new MaterialDescriptor { Attributes = attributes });
        var materialPass = material.Passes[0];
        if (shader is not null)
        {
            materialPass.DepthFunction = MapDepthCompare(shader.DepthCompare);
            materialPass.CullMode = mesh.IsTwoSided ? CullMode.None : CullMode.Back;
            var opaque = shader.SourceBlend == W3dSourceBlend.One && shader.DestinationBlend == W3dDestinationBlend.Zero;
            materialPass.HasTransparency = !opaque;
            if (!opaque)
            {
                materialPass.BlendState = new BlendStateDescription(
                    MapSourceBlend(shader.SourceBlend),
                    MapDestinationBlend(shader.DestinationBlend));
                materialPass.AlphaToCoverage = false;
            }

            _depthWrites[materialPass] = shader.DepthWriteEnabled;
        }

        var index = model.Materials.Count;
        model.Materials.Add(material);
        materialIndices.Add(key, index);
        return index;
    }

    private static string? ResolveTexturePath(ContentFileSystem content, W3dMesh mesh, uint? textureId)
    {
        if (textureId is not uint id || id >= mesh.Materials.Textures.Count)
        {
            return null;
        }

        var reference = mesh.Materials.Textures[checked((int)id)].Name;
        var baseName = Path.GetFileNameWithoutExtension(reference);
        return new[]
        {
            $"Art/Textures/{baseName}.dds",
            $"Art/Textures/{Path.GetFileName(reference)}",
        }.FirstOrDefault(content.Exists);
    }

    private IComputeColor CreateTextureColor(
        ContentFileSystem content,
        string? path,
        TextureCoordinate textureCoordinate)
    {
        if (path is null)
        {
            return new ComputeColor(Color4.White);
        }

        using var stream = content.OpenRead(path);
        Texture texture;
        if (path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
        {
            var image = TgaReader.Read(stream);
            var format = GraphicsDevice.ColorSpace == ColorSpace.Linear
                ? PixelFormat.R8G8B8A8_UNorm_SRgb
                : PixelFormat.R8G8B8A8_UNorm;
            texture = Texture.New2D(
                GraphicsDevice,
                image.Width,
                image.Height,
                format,
                image.RgbaPixels,
                TextureFlags.ShaderResource,
                GraphicsResourceUsage.Immutable);
        }
        else
        {
            texture = Texture.Load(
                GraphicsDevice,
                stream,
                loadAsSRGB: GraphicsDevice.ColorSpace == ColorSpace.Linear);
        }

        _runtimeTextures.Add(texture);
        return new ComputeTextureColor(texture, textureCoordinate, Vector2.One, Vector2.Zero)
        {
            Filtering = TextureFilter.Linear,
            AddressModeU = TextureAddressMode.Wrap,
            AddressModeV = TextureAddressMode.Wrap,
        };
    }

    private static IComputeColor ApplyPrimaryGradient(
        IComputeColor texture,
        IComputeColor vertexColor,
        W3dShader? shader,
        bool hasTexture)
    {
        if (!hasTexture)
        {
            return shader?.PrimaryGradient == W3dPrimaryGradient.Disabled
                ? new ComputeColor(Color4.White)
                : vertexColor;
        }

        return shader?.PrimaryGradient switch
        {
            W3dPrimaryGradient.Disabled => texture,
            W3dPrimaryGradient.Add => SubstituteAlpha(
                ShaderNode("ComputeColorAdd", texture, vertexColor),
                new ComputeBinaryColor(texture, vertexColor, BinaryOperator.Multiply)),
            W3dPrimaryGradient.Modulate2X => SubstituteAlpha(
                new ComputeBinaryColor(
                    new ComputeBinaryColor(texture, vertexColor, BinaryOperator.Multiply),
                    new ComputeColor(new Color4(2, 2, 2, 1)),
                    BinaryOperator.Multiply),
                new ComputeBinaryColor(texture, vertexColor, BinaryOperator.Multiply)),
            _ => new ComputeBinaryColor(texture, vertexColor, BinaryOperator.Multiply),
        };
    }

    private static IComputeColor ApplyDetail(
        IComputeColor current,
        IComputeColor detail,
        byte colorFunction,
        byte alphaFunction)
    {
        var color = ApplyDetailFunction(current, detail, colorFunction, alphaOnly: false);
        var alpha = ApplyDetailFunction(current, detail, alphaFunction, alphaOnly: true);
        return SubstituteAlpha(color, alpha);
    }

    private static IComputeColor ApplyDetailFunction(
        IComputeColor current,
        IComputeColor detail,
        byte function,
        bool alphaOnly)
    {
        var one = new ComputeColor(Color4.White);
        return function switch
        {
            0 => current,
            1 => detail,
            2 => new ComputeBinaryColor(detail, current, BinaryOperator.Multiply),
            3 => new ComputeBinaryColor(
                one,
                new ComputeBinaryColor(
                    new ComputeBinaryColor(one, detail, BinaryOperator.Subtract),
                    new ComputeBinaryColor(one, current, BinaryOperator.Subtract),
                    BinaryOperator.Multiply),
                BinaryOperator.Subtract),
            4 when !alphaOnly => ShaderNode("ComputeColorAdd", detail, current),
            5 when !alphaOnly => new ComputeBinaryColor(detail, current, BinaryOperator.Subtract),
            6 when !alphaOnly => new ComputeBinaryColor(current, detail, BinaryOperator.Subtract),
            7 when !alphaOnly => ShaderNode("ComputeColorLerpAlpha", current, detail),
            8 when !alphaOnly => ShaderNode("ComputeColorLerpAlpha", current, SubstituteAlpha(detail, current)),
            9 when !alphaOnly => new ComputeBinaryColor(
                ShaderNode("ComputeColorAdd", detail, current),
                new ComputeColor(new Color4(0.5f)),
                BinaryOperator.Subtract),
            10 when !alphaOnly => new ComputeBinaryColor(
                new ComputeBinaryColor(
                    ShaderNode("ComputeColorAdd", detail, current),
                    new ComputeColor(new Color4(0.5f)),
                    BinaryOperator.Subtract),
                new ComputeColor(new Color4(2)),
                BinaryOperator.Multiply),
            11 when !alphaOnly => new ComputeBinaryColor(
                new ComputeBinaryColor(detail, current, BinaryOperator.Multiply),
                new ComputeColor(new Color4(2)),
                BinaryOperator.Multiply),
            _ => current,
        };
    }

    private static IComputeColor SubstituteAlpha(IComputeColor color, IComputeColor alpha)
        => new ComputeBinaryColor(color, alpha, BinaryOperator.SubstituteAlpha);

    private static IComputeColor ShaderNode(string mixin, IComputeColor color1, IComputeColor color2)
        => new ComputeShaderClassColor
        {
            MixinReference = mixin,
            CompositionNodes =
            {
                ["color1"] = color1,
                ["color2"] = color2,
            },
        };

    private static CompareFunction MapDepthCompare(W3dDepthCompare value) => value switch
    {
        W3dDepthCompare.Never => CompareFunction.Never,
        W3dDepthCompare.Less => CompareFunction.Less,
        W3dDepthCompare.Equal => CompareFunction.Equal,
        W3dDepthCompare.LessOrEqual => CompareFunction.LessEqual,
        W3dDepthCompare.Greater => CompareFunction.Greater,
        W3dDepthCompare.NotEqual => CompareFunction.NotEqual,
        W3dDepthCompare.GreaterOrEqual => CompareFunction.GreaterEqual,
        W3dDepthCompare.Always => CompareFunction.Always,
        _ => CompareFunction.LessEqual,
    };

    private static Blend MapSourceBlend(W3dSourceBlend value) => value switch
    {
        W3dSourceBlend.Zero => Blend.Zero,
        W3dSourceBlend.One => Blend.One,
        W3dSourceBlend.SourceAlpha => Blend.SourceAlpha,
        // The Generals runtime intentionally maps this legacy value to D3DBLEND_DESTCOLOR.
        W3dSourceBlend.OneMinusSourceAlpha => Blend.DestinationColor,
        _ => Blend.One,
    };

    private static Blend MapDestinationBlend(W3dDestinationBlend value) => value switch
    {
        W3dDestinationBlend.Zero => Blend.Zero,
        W3dDestinationBlend.One => Blend.One,
        W3dDestinationBlend.SourceColor or W3dDestinationBlend.SourceColorPreFog => Blend.SourceColor,
        W3dDestinationBlend.OneMinusSourceColor => Blend.InverseSourceColor,
        W3dDestinationBlend.SourceAlpha => Blend.SourceAlpha,
        W3dDestinationBlend.OneMinusSourceAlpha => Blend.InverseSourceAlpha,
        _ => Blend.Zero,
    };

    private readonly record struct RenderMaterialKey(
        string? Texture0Path,
        string? Texture1Path,
        W3dShader? Shader,
        bool TwoSided);
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct W3dRenderVertex(
    Vector3 position,
    Vector3 normal,
    Color color,
    Vector2 textureCoordinate0,
    Vector2 textureCoordinate1) : IVertex
{
    public Vector3 Position = position;
    public Vector3 Normal = normal;
    public Color Color = color;
    public Vector2 TextureCoordinate0 = textureCoordinate0;
    public Vector2 TextureCoordinate1 = textureCoordinate1;

    public static readonly VertexDeclaration Layout = new(
        VertexElement.Position<Vector3>(),
        VertexElement.Normal<Vector3>(),
        VertexElement.Color<Color>(),
        VertexElement.TextureCoordinate<Vector2>(0),
        VertexElement.TextureCoordinate<Vector2>(1));

    public readonly VertexDeclaration GetLayout() => Layout;

    public void FlipWinding()
    {
        TextureCoordinate0.X = 1f - TextureCoordinate0.X;
        TextureCoordinate1.X = 1f - TextureCoordinate1.X;
    }
}

internal sealed class W3dDepthWritePipelineProcessor : PipelineProcessor
{
    private IReadOnlyDictionary<MaterialPass, bool> _depthWrites = new Dictionary<MaterialPass, bool>();
    private RenderStage? _opaqueStage;
    private RenderStage? _transparentStage;

    public W3dDepthWritePipelineProcessor()
    {
    }

    public W3dDepthWritePipelineProcessor(
        IReadOnlyDictionary<MaterialPass, bool> depthWrites,
        RenderStage opaqueStage,
        RenderStage transparentStage)
    {
        _depthWrites = depthWrites;
        _opaqueStage = opaqueStage;
        _transparentStage = transparentStage;
    }

    public override void Process(
        RenderNodeReference renderNodeReference,
        ref RenderNode renderNode,
        RenderObject renderObject,
        PipelineStateDescription pipelineState)
    {
        if ((renderNode.RenderStage == _opaqueStage || renderNode.RenderStage == _transparentStage) &&
            renderObject is RenderMesh mesh &&
            _depthWrites.TryGetValue(mesh.MaterialPass, out var enabled))
        {
            pipelineState.DepthStencilState.DepthBufferWriteEnable = enabled;
        }
    }
}
