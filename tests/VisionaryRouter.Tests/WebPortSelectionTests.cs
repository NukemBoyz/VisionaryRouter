using VisionaryRouter.Server.Services;

namespace VisionaryRouter.Tests;

public sealed class WebPortSelectionTests
{
    [Theory]
    [InlineData(1024, true)]
    [InlineData(8088, true)]
    [InlineData(65535, true)]
    [InlineData(0, false)]
    [InlineData(1023, false)]
    [InlineData(65536, false)]
    public void IsValid_enforces_non_system_tcp_port_range(int port, bool expected)
    {
        Assert.Equal(expected, WebPortSelection.IsValid(port));
    }

    [Fact]
    public void FindAvailable_uses_preferred_port_when_free()
    {
        Assert.Equal(8088, WebPortSelection.FindAvailable(8088, new HashSet<int>()));
    }

    [Fact]
    public void FindAvailable_advances_past_occupied_ports()
    {
        var occupied = new HashSet<int> { 8088, 8089, 8090 };

        Assert.Equal(8091, WebPortSelection.FindAvailable(8088, occupied));
    }

    [Fact]
    public void FindAvailable_wraps_after_highest_port()
    {
        Assert.Equal(1024, WebPortSelection.FindAvailable(65535, new HashSet<int> { 65535 }));
    }

    [Theory]
    [InlineData("9090", 8088, 9090)]
    [InlineData("not-a-port", 8088, 8088)]
    [InlineData("80", 8088, 8088)]
    [InlineData(null, 8088, 8088)]
    public void ParseOrDefault_rejects_invalid_saved_values(string? value, int fallback, int expected)
    {
        Assert.Equal(expected, WebPortSelection.ParseOrDefault(value, fallback));
    }
}
