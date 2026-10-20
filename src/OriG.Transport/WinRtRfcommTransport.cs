namespace OriG.Transport;

using Windows.Networking.Sockets;
using Windows.Storage.Streams;

/// <summary>
/// 基于 WinRT <see cref="StreamSocket"/> 的 RFCOMM 传输（方案 T1）。
/// 实测在未打包桌面应用下可用，无需 MSIX 能力声明。
///
/// 注意：读侧不用 DataReader.LoadAsync —— 实测它会把同一批字节重复返回
/// （9 字节的帧被读成 4 次），改用 IInputStream.ReadAsync 直接取 IBuffer。
/// </summary>
public sealed class WinRtRfcommTransport : IHeadsetTransport
{
    private StreamSocket? _socket;
    private DataWriter? _writer;

    public async Task ConnectAsync(HeadsetServiceInfo service, CancellationToken ct = default)
    {
        await DisconnectAsync();

        var socket = new StreamSocket();
        try
        {
            // 保护级别用服务协商出的 MaxProtectionLevel（实测为 EncryptionWithAuthentication）
            await socket
                .ConnectAsync(HeadsetServiceLocator.ToHostName(service.HostName),
                              service.ServiceName,
                              service.ProtectionLevel)
                .AsTask(ct);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        _socket = socket;
        _writer = new DataWriter(socket.OutputStream);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        var writer = _writer ?? throw new InvalidOperationException("未连接");
        writer.WriteBytes(data.ToArray());
        await writer.StoreAsync().AsTask(ct);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var socket = _socket ?? throw new InvalidOperationException("未连接");
        uint capacity = (uint)buffer.Length;

        var request = new Windows.Storage.Streams.Buffer(capacity);
        IBuffer result = await socket.InputStream
            .ReadAsync(request, capacity, InputStreamOptions.Partial)
            .AsTask(ct);

        int length = (int)result.Length;
        if (length == 0)
            return 0;

        using var reader = DataReader.FromBuffer(result);
        var temp = new byte[length];
        reader.ReadBytes(temp);
        temp.AsSpan().CopyTo(buffer.Span);
        return length;
    }

    public Task DisconnectAsync()
    {
        _writer?.Dispose();
        _writer = null;
        _socket?.Dispose();
        _socket = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
