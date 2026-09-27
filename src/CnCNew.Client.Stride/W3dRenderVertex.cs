using System.Runtime.InteropServices;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace CnCNew.Client.Stride;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct W3dRenderVertex(
    Vector3 position,
    Vector3 normal,
    Color color0,
    Color color1,
    Color color2,
    Color color3,
    Vector2 textureCoordinate0,
    Vector2 textureCoordinate1,
    Vector2 textureCoordinate2,
    Vector2 textureCoordinate3) : IVertex
{
    public Vector3 Position = position;
    public Vector3 Normal = normal;
    public Color Color0 = color0;
    public Color Color1 = color1;
    public Color Color2 = color2;
    public Color Color3 = color3;
    public Vector2 TextureCoordinate0 = textureCoordinate0;
    public Vector2 TextureCoordinate1 = textureCoordinate1;
    public Vector2 TextureCoordinate2 = textureCoordinate2;
    public Vector2 TextureCoordinate3 = textureCoordinate3;

    public static readonly VertexDeclaration Layout = new(
        VertexElement.Position<Vector3>(),
        VertexElement.Normal<Vector3>(),
        VertexElement.Color<Color>(0),
        VertexElement.Color<Color>(1),
        VertexElement.Color<Color>(2),
        VertexElement.Color<Color>(3),
        VertexElement.TextureCoordinate<Vector2>(0),
        VertexElement.TextureCoordinate<Vector2>(1),
        VertexElement.TextureCoordinate<Vector2>(2),
        VertexElement.TextureCoordinate<Vector2>(3));

    public readonly VertexDeclaration GetLayout() => Layout;

    public void FlipWinding()
    {
        TextureCoordinate0.X = 1f - TextureCoordinate0.X;
        TextureCoordinate1.X = 1f - TextureCoordinate1.X;
        TextureCoordinate2.X = 1f - TextureCoordinate2.X;
        TextureCoordinate3.X = 1f - TextureCoordinate3.X;
    }
}
