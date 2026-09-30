using Stride.Graphics;

namespace CnCNew.Client.Stride.Layers.Fx;

/// <summary>
/// The shared CPU vertex array and its GPU buffers. Every effect primitive is a quad (four vertices, six indices of a static pattern),
/// so a draw run is just a range of quads. Only the used prefix of the dynamic vertex buffer is uploaded each frame.
/// </summary>
internal sealed class FxGeometry : IDisposable
{
    public const int DefaultMaxQuads = 32768;

    private readonly FxVertex[] _vertices;

    public FxGeometry(GraphicsDevice device, int maxQuads = DefaultMaxQuads)
    {
        MaxQuads = maxQuads;
        _vertices = new FxVertex[maxQuads * 4];

        var indices = new int[maxQuads * 6];
        for (int quad = 0, vertex = 0, index = 0; quad < maxQuads; quad++, vertex += 4, index += 6)
        {
            indices[index] = vertex;
            indices[index + 1] = vertex + 1;
            indices[index + 2] = vertex + 2;
            indices[index + 3] = vertex + 2;
            indices[index + 4] = vertex + 3;
            indices[index + 5] = vertex;
        }

        VertexBuffer = global::Stride.Graphics.Buffer.Vertex.New(device, _vertices.Length * FxVertex.SizeInBytes, GraphicsResourceUsage.Dynamic);
        IndexBuffer = global::Stride.Graphics.Buffer.Index.New(device, indices);
    }

    public int MaxQuads { get; }

    /// <summary>Quads written this frame.</summary>
    public int QuadCount { get; private set; }

    /// <summary>Quads that did not fit this frame (counted, never silently lost).</summary>
    public int DroppedQuads { get; private set; }

    public global::Stride.Graphics.Buffer VertexBuffer { get; }

    public global::Stride.Graphics.Buffer IndexBuffer { get; }

    public void Reset()
    {
        QuadCount = 0;
        DroppedQuads = 0;
    }

    /// <summary>The four vertices of the next quad (empty when the buffer is full).</summary>
    public Span<FxVertex> NextQuad()
    {
        if (QuadCount >= MaxQuads)
        {
            DroppedQuads++;
            return default;
        }

        return _vertices.AsSpan(QuadCount++ * 4, 4);
    }

    /// <summary>Uploads the used prefix and returns the byte count.</summary>
    public int Upload(CommandList commandList)
    {
        if (QuadCount == 0)
        {
            return 0;
        }

        var used = _vertices.AsSpan(0, QuadCount * 4);
        VertexBuffer.SetData(commandList, (ReadOnlySpan<FxVertex>)used);
        return used.Length * FxVertex.SizeInBytes;
    }

    public void Dispose()
    {
        VertexBuffer.Dispose();
        IndexBuffer.Dispose();
    }
}
