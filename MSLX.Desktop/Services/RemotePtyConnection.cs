using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Porta.Pty;

namespace MSLX.Desktop.Services;

/// <summary>
/// 将远程 SignalR PTY 会话桥接为本地 IPtyConnection，供 TerminalControl 挂载
/// </summary>
public class RemotePtyConnection : IPtyConnection
{
    private readonly InstanceSignalRService _signalR;
    private readonly int _instanceId;
    private readonly Pipe _readPipe = new();
    private readonly Stream _readerStream;
    private readonly Stream _writerStream;
    private readonly object _pipeLock = new();
    private int _lastCols;
    private int _lastRows;
    private bool _isDisposed;

    public RemotePtyConnection(InstanceSignalRService signalR, int instanceId)
    {
        _signalR = signalR;
        _instanceId = instanceId;
        _readerStream = _readPipe.Reader.AsStream();
        _writerStream = new RemotePtyWriterStream(signalR, instanceId);

        _signalR.PtyDataReceived += OnPtyDataReceived;
        _signalR.LogReceived += OnLogReceived;
    }

    private void OnPtyDataReceived(string chunk)
    {
        if (_isDisposed || string.IsNullOrEmpty(chunk)) return;
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(chunk);
            lock (_pipeLock)
            {
                var span = _readPipe.Writer.GetSpan(bytes.Length);
                bytes.AsSpan().CopyTo(span);
                _readPipe.Writer.Advance(bytes.Length);
                _ = _readPipe.Writer.FlushAsync();
            }
        }
        catch { }
    }

    private void OnLogReceived(string log)
    {
        if (_isDisposed || string.IsNullOrEmpty(log)) return;
        if (log.StartsWith("[MSLX") || log.StartsWith(">>>") || log.StartsWith("[System]") || log.StartsWith("[RCON]"))
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes($"\r\n{log}\r\n");
                lock (_pipeLock)
                {
                    var span = _readPipe.Writer.GetSpan(bytes.Length);
                    bytes.AsSpan().CopyTo(span);
                    _readPipe.Writer.Advance(bytes.Length);
                    _ = _readPipe.Writer.FlushAsync();
                }
            }
            catch { }
        }
    }

    public Stream ReaderStream => _readerStream;
    public Stream WriterStream => _writerStream;
    public int Pid => 1;
    public int ExitCode => 0;

    public event EventHandler<PtyExitedEventArgs>? ProcessExited;

    public void Resize(int cols, int rows)
    {
        if (_isDisposed || cols <= 0 || rows <= 0) return;
        if (cols == _lastCols && rows == _lastRows) return;
        _lastCols = cols;
        _lastRows = rows;
        _ = _signalR.ResizePtyAsync(_instanceId, cols, rows);
    }

    public void Kill()
    {
        // 远程 PTY 发送 Ctrl+C
        _ = _signalR.SendPtyInputAsync(_instanceId, "\x03");
    }

    public bool WaitForExit(int milliseconds) => true;

    public void TriggerExited(int exitCode)
    {
        try
        {
            var args = (PtyExitedEventArgs)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PtyExitedEventArgs));
            ProcessExited?.Invoke(this, args);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _signalR.PtyDataReceived -= OnPtyDataReceived;
        _signalR.LogReceived -= OnLogReceived;
        try { _readPipe.Writer.Complete(); } catch { }
        try { _readerStream.Dispose(); } catch { }
        try { _writerStream.Dispose(); } catch { }
    }

    private class RemotePtyWriterStream : Stream
    {
        private readonly InstanceSignalRService _signalR;
        private readonly int _instanceId;

        public RemotePtyWriterStream(InstanceSignalRService signalR, int instanceId)
        {
            _signalR = signalR;
            _instanceId = instanceId;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count <= 0) return;
            string text = Encoding.UTF8.GetString(buffer, offset, count);
            _ = _signalR.SendPtyInputAsync(_instanceId, text);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (count <= 0) return;
            string text = Encoding.UTF8.GetString(buffer, offset, count);
            await _signalR.SendPtyInputAsync(_instanceId, text);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length <= 0) return;
            string text = Encoding.UTF8.GetString(buffer.Span);
            await _signalR.SendPtyInputAsync(_instanceId, text);
        }
    }
}
