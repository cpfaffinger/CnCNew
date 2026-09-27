namespace CnCNew.Archives;

internal sealed class ArchiveEntryStream : Stream
{
    private readonly FileStream _stream;
    private readonly long _length;
    private long _position;

    public ArchiveEntryStream(string archivePath, uint offset, uint length)
    {
        _stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        _stream.Position = offset;
        _length = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var boundedCount = (int)Math.Min(count, _length - _position);
        if (boundedCount <= 0)
        {
            return 0;
        }

        var read = _stream.Read(buffer, offset, boundedCount);
        _position += read;
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var boundedLength = (int)Math.Min(buffer.Length, _length - _position);
        if (boundedLength <= 0)
        {
            return 0;
        }

        var read = _stream.Read(buffer[..boundedLength]);
        _position += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var boundedLength = (int)Math.Min(buffer.Length, _length - _position);
        if (boundedLength <= 0)
        {
            return 0;
        }

        var read = await _stream.ReadAsync(buffer[..boundedLength], cancellationToken);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        if (newPosition < 0 || newPosition > _length)
        {
            throw new IOException("Attempted to seek outside the archive entry.");
        }

        _stream.Position += newPosition - _position;
        _position = newPosition;
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stream.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

