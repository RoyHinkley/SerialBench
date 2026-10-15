using SerialPortTerminal.Serial;
using SerialPortTerminal.Serial.Protocol;

namespace SerialPortTerminal.Tests;

public sealed class SerialMessagePipelineTests
{
    [Fact]
    public void Transport_notification_flows_through_acquisition_decoding_and_dispatch()
    {
        var transport = new FakeSerialTransport();
        using var delivered = new ManualResetEventSlim();
        DecodedMessage? received = null;
        using var pipeline = new SerialMessagePipeline(
            transport,
            new MessageDecoder(new TerminatorMessageFramer(0x03), NoMessageIntegrity.Instance),
            message => { received = message; delivered.Set(); },
            TimeSpan.FromMilliseconds(100));

        transport.EnqueueReceived(0x10, 0x20, 0x03);
        pipeline.SignalDataAvailable();

        Assert.True(delivered.Wait(TimeSpan.FromSeconds(2)));
        Assert.NotNull(received);
        Assert.Equal(new byte[] { 0x10, 0x20 }, received!.Payload.ToArray());
        Assert.Equal(0, transport.BytesToRead);
    }

    [Fact]
    public void Silence_is_reported_to_silence_framer_after_acquisition()
    {
        var transport = new FakeSerialTransport();
        using var delivered = new ManualResetEventSlim();
        DecodedMessage? received = null;
        using var pipeline = new SerialMessagePipeline(
            transport,
            new MessageDecoder(new SilenceMessageFramer(), NoMessageIntegrity.Instance),
            message => { received = message; delivered.Set(); },
            TimeSpan.FromMilliseconds(25));

        transport.EnqueueReceived(0x41, 0x42, 0x43);
        pipeline.SignalDataAvailable();

        Assert.True(delivered.Wait(TimeSpan.FromSeconds(2)));
        Assert.NotNull(received);
        Assert.Equal(new byte[] { 0x41, 0x42, 0x43 }, received!.Payload.ToArray());
    }
}
