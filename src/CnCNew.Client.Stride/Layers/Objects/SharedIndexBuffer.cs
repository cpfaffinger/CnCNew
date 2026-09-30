using Stride.Graphics;

namespace CnCNew.Client.Stride.Layers.Objects;

/// <summary>
/// One identity index buffer (0, 1, 2, ...) shared by every non-indexed triangle-list batch of the object layer: a batch's
/// <see cref="Stride.Rendering.MeshDraw.IndexBuffer"/> reads only its own vertex count from the start of this buffer, so the same GPU
/// resource serves any batch no bigger than the current capacity. Grown (never shrunk) on demand.
/// </summary>
internal sealed class SharedIndexBuffer : IDisposable
{
    private readonly GraphicsDevice _device;
    private global::Stride.Graphics.Buffer? _buffer;
    private int _capacity;

    public SharedIndexBuffer(GraphicsDevice device) => _device = device;

    public IndexBufferBinding GetBinding(int count)
    {
        if (count > _capacity)
        {
            var next = Math.Max(count, Math.Max(_capacity * 2, 1024));
            var indices = new int[next];
            for (var i = 0; i < next; i++) indices[i] = i;
            var buffer = global::Stride.Graphics.Buffer.Index.New(_device, indices);
            _buffer?.Dispose();
            _buffer = buffer;
            _capacity = next;
        }

        return new IndexBufferBinding(_buffer!, is32Bit: true, count);
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        _capacity = 0;
    }
}
