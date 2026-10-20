namespace OriG.Transport;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking;
using Windows.Networking.Sockets;

/// <summary>一个可连接的耳机 SPP 服务端点。</summary>
public sealed record HeadsetServiceInfo(
    string DeviceId,
    string DisplayName,
    string HostName,
    string ServiceName,
    SocketProtectionLevel ProtectionLevel);

/// <summary>已配对的经典蓝牙设备（用于诊断，不保证有我们的服务）。</summary>
public sealed record PairedDeviceInfo(string Name, ulong Address, bool Connected)
{
    public string AddressText => string.Join(':',
        Enumerable.Range(0, 6)
            .Select(i => ((Address >> ((5 - i) * 8)) & 0xFF).ToString("X2")));
}

/// <summary>
/// 定位原道耳机的 RFCOMM 服务。
/// 服务 UUID 尾部 8 字节 = ASCII "NHCKCTRL"。
/// </summary>
public static class HeadsetServiceLocator
{
    public static readonly Guid ServiceUuid = new("0000a100-1000-8000-4e48-434b4354524c");

    /// <summary>名称匹配候选（原道 = NiceHCK 贴牌，同协议族）。</summary>
    public static readonly string[] NameHints =
        ["YUANDAO", "NiceHCK", "OriG", "EB2S", "NHCK", "原道"];

    public static async Task<IReadOnlyList<HeadsetServiceInfo>> FindAsync(
        Guid? serviceUuid = null, CancellationToken ct = default)
    {
        var serviceId = RfcommServiceId.FromUuid(serviceUuid ?? ServiceUuid);
        var selector = RfcommDeviceService.GetDeviceSelector(serviceId);
        var devices = await DeviceInformation.FindAllAsync(selector).AsTask(ct);

        var list = new List<HeadsetServiceInfo>();
        foreach (var device in devices)
        {
            var service = await RfcommDeviceService.FromIdAsync(device.Id);
            if (service is null)
                continue;

            list.Add(new HeadsetServiceInfo(
                device.Id,
                device.Name,
                service.ConnectionHostName.ToString(),
                service.ConnectionServiceName,
                service.MaxProtectionLevel));
        }
        return list;
    }

    /// <summary>按名称前缀挑目标；都没命中时，若只有一个服务就直接用它。</summary>
    public static HeadsetServiceInfo? PickTarget(IReadOnlyList<HeadsetServiceInfo> services)
    {
        foreach (var s in services)
        {
            foreach (var hint in NameHints)
            {
                if (s.DisplayName.StartsWith(hint, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
        }
        return services.Count == 1 ? services[0] : null;
    }

    public static async Task<IReadOnlyList<PairedDeviceInfo>> ListPairedAsync(CancellationToken ct = default)
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var devices = await DeviceInformation.FindAllAsync(selector).AsTask(ct);

        var list = new List<PairedDeviceInfo>();
        foreach (var info in devices)
        {
            try
            {
                var bt = await BluetoothDevice.FromIdAsync(info.Id).AsTask(ct);
                if (bt is null)
                    continue;
                list.Add(new PairedDeviceInfo(bt.Name, bt.BluetoothAddress, bt.ConnectionStatus == BluetoothConnectionStatus.Connected));
            }
            catch
            {
                // 个别设备取不到信息，跳过即可
            }
        }
        return list;
    }

    internal static HostName ToHostName(string hostName) => new(hostName);
}
