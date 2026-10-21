using OriG.Transport;

namespace OriG.Core.Tests;

public class ServiceLocatorTests
{
    private static HeadsetServiceInfo Service(string id, string name) =>
        new(id, name, "00:11:22:33:44:55", "rfcomm", default);

    [Fact]
    public void PickTarget_PrefersKnownName()
    {
        HeadsetServiceInfo[] services =
        [
            Service("renamed-id", "客厅耳机"),
            Service("orig-id", "YUANDAO OriG in"),
        ];

        var picked = HeadsetServiceLocator.PickTarget(services, "renamed-id");

        Assert.NotNull(picked);
        Assert.Equal("orig-id", picked.DeviceId);
    }

    [Fact]
    public void PickTarget_PrefersLastDeviceAmongKnownNames()
    {
        HeadsetServiceInfo[] services =
        [
            Service("id-1", "YUANDAO OriG in"),
            Service("id-2", "NiceHCK EB2S"),
        ];

        var picked = HeadsetServiceLocator.PickTarget(services, "id-2");

        Assert.NotNull(picked);
        Assert.Equal("id-2", picked.DeviceId);
    }

    [Fact]
    public void PickTarget_FallsBackToLastSuccessfulDevice()
    {
        HeadsetServiceInfo[] services =
        [
            Service("id-1", "耳机 A"),
            Service("id-2", "耳机 B"),
        ];

        var picked = HeadsetServiceLocator.PickTarget(services, "id-2");

        Assert.NotNull(picked);
        Assert.Equal("id-2", picked.DeviceId);
    }

    [Fact]
    public void PickTarget_FallsBackToSingleService()
    {
        var picked = HeadsetServiceLocator.PickTarget([Service("id-1", "随便")], null);

        Assert.NotNull(picked);
        Assert.Equal("id-1", picked.DeviceId);
    }

    [Fact]
    public void PickTarget_ReturnsNullForAmbiguousUnknownDevices()
    {
        HeadsetServiceInfo[] services =
        [
            Service("id-1", "耳机 A"),
            Service("id-2", "耳机 B"),
        ];

        Assert.Null(HeadsetServiceLocator.PickTarget(services, null));
    }
}
