using System.Threading.Channels;
using OriG.Transport;

namespace OriG.Core.Tests;

internal sealed class FakeTransport : IHeadsetTransport
{
    private Channel<byte[]> _incoming = NewChannel();
    private readonly object _writeLock = new();

    public bool Connected { get; private set; }
    public List<byte[]> Writes { get; } = [];
    public Func<byte[], IReadOnlyList<byte[]>>? Responder { get; set; }

    public Task ConnectAsync(HeadsetServiceInfo service, CancellationToken ct = default)
    {
        // 会话在连接前会先 StopSession，旧通道已被完成；每次连接换一条新通道。
        _incoming = NewChannel();
        Connected = true;
        return Task.CompletedTask;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        byte[] bytes = data.ToArray();
        lock (_writeLock)
            Writes.Add(bytes);

        if (Responder is not null)
        {
            foreach (byte[] response in Responder(bytes))
                _incoming.Writer.TryWrite(response);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (!Connected)
            return 0;

        byte[] bytes;
        try
        {
            bytes = await _incoming.Reader.ReadAsync(ct);
        }
        catch (ChannelClosedException)
        {
            return 0;
        }

        int count = Math.Min(bytes.Length, buffer.Length);
        bytes.AsSpan(0, count).CopyTo(buffer.Span);
        return count;
    }

    public Task DisconnectAsync()
    {
        Connected = false;
        _incoming.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();

    public void Push(byte[] frame) => _incoming.Writer.TryWrite(frame);

    private static Channel<byte[]> NewChannel() => Channel.CreateUnbounded<byte[]>();
}
