namespace BotWatch.Probe.Telegram.Telegram;

// WTelegram читает сессию один раз при старте и потом целиком перезаписывает её через Write.
// держим её в памяти, а воркер периодически сохраняет в базу, если она менялась
internal sealed class SessionStore(byte[]? initial) : Stream
{
    private readonly Lock _lock = new();
    private byte[] _data = initial ?? [];
    private bool _dirty;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _data.Length;

    public override long Position
    {
        get => 0;
        set { }
    }

    public byte[] Current
    {
        get
        {
            lock (_lock)
            {
                return _data;
            }
        }
    }

    // null - с прошлого раза сессия не менялась
    public byte[]? TakeChanged()
    {
        lock (_lock)
        {
            if (!_dirty)
            {
                return null;
            }

            _dirty = false;
            return _data;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            count = Math.Min(count, _data.Length);
            Array.Copy(_data, 0, buffer, offset, count);
            return count;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            _data = buffer.AsSpan(offset, count).ToArray();
            _dirty = true;
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => 0;

    public override void SetLength(long value)
    {
    }
}
