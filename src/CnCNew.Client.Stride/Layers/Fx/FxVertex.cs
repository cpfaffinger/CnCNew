using System.Numerics;
using System.Runtime.InteropServices;
using Stride.Graphics;

namespace CnCNew.Client.Stride.Layers.Fx;

/// <summary>Position, packed RGBA (byte order R, G, B, A) and one texture coordinate: 24 bytes per corner of an effect quad.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct FxVertex
{
    public Vector3 Position;
    public uint Color;
    public Vector2 Uv;

    public static readonly VertexDeclaration Layout = new(
        VertexElement.Position<global::Stride.Core.Mathematics.Vector3>(),
        VertexElement.Color<global::Stride.Core.Mathematics.Color>(0),
        VertexElement.TextureCoordinate<global::Stride.Core.Mathematics.Vector2>(0));

    public const int SizeInBytes = 24;
}
