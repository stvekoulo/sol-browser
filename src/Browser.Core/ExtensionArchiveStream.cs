namespace Browser.Core;

/// <summary>The ZIP offsets start after the CRX envelope. The caller owns the package stream.</summary>
internal sealed class ExtensionArchiveStream(Stream package, long offset) : Stream
{
    private long _position;
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => package.Length - offset;
    public override long Position
    {
        get => _position;
        set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); _position = value; }
    }
    public override int Read(byte[] buffer, int start, int count) => Read(buffer.AsSpan(start, count));
    public override int Read(Span<byte> buffer)
    {
        var remaining = Length - Position;
        if (remaining <= 0) return 0;
        package.Position = checked(offset + Position);
        var count = package.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
        _position += count;
        return count;
    }
    public override long Seek(long value, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => value,
            SeekOrigin.Current => checked(Position + value),
            SeekOrigin.End => checked(Length + value),
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        return Position;
    }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int start, int count) => throw new NotSupportedException();
}
