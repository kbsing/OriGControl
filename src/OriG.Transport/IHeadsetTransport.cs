namespace OriG.Transport;

/// <summary>
/// 耳机传输层抽象。
/// 契约：<see cref="ReadAsync"/> 返回 0 表示对端正常关闭；抛异常表示链路故障。
/// 写操作不保证线程安全，串行化由上层负责（RFCOMM 是独占通道）。
/// </summary>
public interface IHeadsetTransport : IAsyncDisposable
{
    Task ConnectAsync(HeadsetServiceInfo service, CancellationToken ct = default);
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);
    Task DisconnectAsync();
}
