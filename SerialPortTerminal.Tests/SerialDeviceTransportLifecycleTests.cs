using SerialPortTerminal.Serial;

namespace SerialPortTerminal.Tests;

public sealed class SerialDeviceTransportLifecycleTests
{
    private static SerialPortSettings Settings() => new("TEST", 115200);

    [Fact]
    public void Connect_uses_transport_factory_and_disposes_transport_on_disconnect()
    {
        var factory = new FakeSerialTransportFactory();
        var transport = factory.Enqueue();
        using var device = new SerialDevice(Settings(), factory);

        Assert.True(device.Connect());
        Assert.True(device.Ready);
        Assert.Equal("TEST", transport.Settings!.PortName);

        Assert.True(device.Disconnect());
        Assert.False(device.Ready);
        Assert.Equal(1, transport.CloseCalls);
        Assert.Equal(1, transport.DisposeCalls);
    }

    [Fact]
    public void Failed_open_releases_the_transport_instance()
    {
        var factory = new FakeSerialTransportFactory();
        var transport = factory.Enqueue();
        transport.OpenException = new IOException("open failed");
        using var device = new SerialDevice(Settings(), factory);

        Assert.False(device.Connect());

        Assert.False(device.Ready);
        Assert.Equal(1, transport.OpenCalls);
        Assert.Equal(1, transport.CloseCalls);
        Assert.Equal(1, transport.DisposeCalls);
    }

    [Fact]
    public void Failure_after_open_releases_the_partially_initialized_transport()
    {
        var factory = new FakeSerialTransportFactory();
        var transport = factory.Enqueue();
        transport.DiscardInException = new IOException("discard failed");
        using var device = new SerialDevice(Settings(), factory);

        Assert.False(device.Connect());

        Assert.False(device.Ready);
        Assert.Equal(1, transport.OpenCalls);
        Assert.Equal(1, transport.CloseCalls);
        Assert.Equal(1, transport.DisposeCalls);
    }

    [Fact]
    public void Cleanup_continues_when_close_throws()
    {
        var factory = new FakeSerialTransportFactory();
        var transport = factory.Enqueue();
        transport.DiscardInException = new IOException("initialization failed");
        transport.CloseException = new IOException("close failed");
        using var device = new SerialDevice(Settings(), factory);

        Assert.False(device.Connect());

        Assert.Equal(1, transport.CloseCalls);
        Assert.Equal(1, transport.DisposeCalls);
        Assert.False(device.Ready);
    }
}
