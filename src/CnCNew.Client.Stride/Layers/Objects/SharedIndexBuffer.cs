using Stride.Graphics;

namespace CnCNew.Client.Stride.Layers.Objects;

/// <summary>
/// One identity index buffer (0, 1, 2, ...) shared by every non-indexed triangle-list batch of the object layer: a batch's
/// <see cref="Stride.Rendering.MeshDraw.IndexBuffer"/> reads only its own vertex count from the start of this buffer, so the same GPU
/// resource serves any batch no bigger than the current capacity. Grown (never shrunk) on demand; a superseded buffer stays alive until
/// <see cref="Dispose"/> because every mesh built earlier still binds it (disposing it on growth silently dropped the triangles of whatever
/// had been built before a bigger batch arrived: building walls vanished depending on the load order, docs/DEMO-FINDINGS.md #2).
/// </summary>
internal sealed class SharedIndexBuffer : IDisposable
{
    private readonly GraphicsDevice _device;
    private global::Stride.Graphics.Buffer? _buffer;
    private readonly List<global::Stride.Graphics.Buffer> _superseded = [];
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
            if (_buffer is not null) _superseded.Add(_buffer);
            _buffer = buffer;
            _capacity = next;
        }

        return new IndexBufferBinding(_buffer!, is32Bit: true, count);
    }

    public void Dispose()
    {
        foreach (var old in _superseded) old.Dispose();
        _superseded.Clear();
        _buffer?.Dispose();
        _buffer = null;
        _capacity = 0;
    }
}
