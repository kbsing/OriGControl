namespace OriG.Transport;

using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Storage.Streams;

/// <summary>
/// 从 SDP 记录里读 RFCOMM 通道号。
/// ProtocolDescriptorList（属性 0x0004）形如 [[L2CAP uuid 0x0100], [RFCOMM uuid 0x0003, channel]]，
/// 编码上 RFCOMM 通道就是字节序列 `19 00 03 08 &lt;channel&gt;`。
/// </summary>
public static class SdpChannelReader
{
    public static async Task<byte?> GetRfcommChannelAsync(string deviceId, CancellationToken ct = default)
    {
        try
        {
            using var service = await RfcommDeviceService.FromIdAsync(deviceId);
            if (service is null)
                return null;

            var attributes = await service.GetSdpRawAttributesAsync().AsTask(ct);

            IBuffer? protocolList = null;
            foreach (var kv in attributes)
            {
                if (kv.Key == 0x0004)
                {
                    protocolList = kv.Value;
                    break;
                }
            }
            if (protocolList is null || protocolList.Length < 5)
                return null;

            var bytes = new byte[protocolList.Length];
            using (var reader = DataReader.FromBuffer(protocolList))
                reader.ReadBytes(bytes);

            // 扫 `19 00 03 08 xx`（UUID 16-bit 0x0003 = RFCOMM，后跟 8-bit 通道号）
            for (int i = 0; i + 4 < bytes.Length; i++)
            {
                if (bytes[i] == 0x19 && bytes[i + 1] == 0x00 && bytes[i + 2] == 0x03 && bytes[i + 3] == 0x08)
                    return bytes[i + 4];
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
