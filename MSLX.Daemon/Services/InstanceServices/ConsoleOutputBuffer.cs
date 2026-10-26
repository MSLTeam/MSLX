namespace MSLX.Daemon.Services.InstanceServices;

public sealed record ConsoleCursor(string StreamId, long Sequence);
public sealed record ConsoleFrame(long Sequence, string Text);
public sealed record ConsoleReplay(string StreamId, long Sequence, bool Gap, ConsoleFrame[] Frames);

/// <summary>控制输出指定的历史记录</summary>
public sealed class ConsoleOutputBuffer(int capacity)
{
    private readonly object _sync = new();
    private readonly Queue<ConsoleFrame> _frames = new();
    private readonly string _streamId = Guid.NewGuid().ToString("N");
    private long _sequence;

    public ConsoleCursor Append(string text)
    {
        lock (_sync)
        {
            _frames.Enqueue(new ConsoleFrame(++_sequence, text));
            while (_frames.Count > capacity) _frames.Dequeue();
            return new ConsoleCursor(_streamId, _sequence);
        }
    }

    public ConsoleReplay Replay(ConsoleCursor? cursor)
    {
        lock (_sync)
        {
            var sameStream = cursor?.StreamId == _streamId && cursor.Sequence <= _sequence && cursor.Sequence >= 0;
            var after = sameStream ? cursor!.Sequence : 0;
            var gap = cursor != null && (!sameStream || (_frames.TryPeek(out var first) && after < first.Sequence - 1));
            return new ConsoleReplay(_streamId, _sequence, gap, _frames.Where(f => f.Sequence > after).ToArray());
        }
    }

    public List<string> Texts() { lock (_sync) return _frames.Select(f => f.Text).ToList(); }
}
