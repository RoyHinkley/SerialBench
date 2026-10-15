using SerialPortTerminal.Serial;

namespace SerialPortTerminal.Tests;

public sealed class ProtocolFramingTransitionTests
{
    [Fact]
    public void Omit_term_character_change_applies_after_current_message_without_reconnect()
    {
        var oldOptions = AeonOptions(omitTermChar: true);
        var newOptions = AeonOptions(omitTermChar: false);
        var factory = new FakeSerialTransportFactory();
        var transport = factory.Enqueue();
        using var device = new SerialDevice(new SerialPortSettings("TEST", 115200), factory)
        {
            CrcConfig = oldOptions,
            MaximumMillisecondsSilenceInMessage = 5
        };

        var received = new List<ReceivedData>();
        var sync = new object();
        device.DataReceived += data => { lock (sync) { received.Add(data); Monitor.PulseAll(sync); } };
        Assert.True(device.Connect());

        var oldPayload = new byte[] { 0x41, 0x42, 0x43 };
        transport.EnqueueReceived(new Crc(oldOptions).Append(oldPayload));
        transport.RaiseDataReceived();
        WaitForMessages(received, sync, 1);

        Assert.True(device.StageProtocolSettings(SerialProtocolSettings.From(newOptions, false)));

        // The staged decoder is adopted at the next message boundary. Complete one old-style
        // message, then verify that the following message uses explicit ETX framing.
        var boundaryPayload = new byte[] { 0x44, 0x45, 0x46 };
        transport.EnqueueReceived(new Crc(oldOptions).Append(boundaryPayload));
        transport.RaiseDataReceived();
        WaitForMessages(received, sync, 2);
        Assert.False(device.ProtocolSettings.OmitTermChar);

        var newPayload = new byte[] { 0x51, 0x52, 0x53 };
        var newWire = new Crc(newOptions).Append(newPayload);
        Assert.Equal(newOptions.TermChar, newWire[^1]);
        transport.EnqueueReceived(newWire);
        transport.RaiseDataReceived();
        var messages = WaitForMessages(received, sync, 3);

        Assert.Equal(newPayload, messages[2].PayloadBytes.ToArray());
        Assert.True(messages[2].CrcValid);
    }

    private static CrcOptions AeonOptions(bool omitTermChar) => new()
    {
        Polynomial = 0xDAAE,
        InitialValue = 0xFFFF,
        ExpectedResidue = 0x82C0,
        PostInvert = true,
        MsBitFirst = false,
        MsByteFirst = false,
        TermChar = 0x03,
        OmitTermChar = omitTermChar
    };

    private static IReadOnlyList<ReceivedData> WaitForMessages(List<ReceivedData> messages, object sync, int count)
    {
        var deadline = Environment.TickCount64 + 2000;
        lock (sync)
        {
            while (messages.Count < count)
            {
                var remaining = deadline - Environment.TickCount64;
                if (remaining <= 0) throw new TimeoutException($"Expected {count} messages; received {messages.Count}.");
                Monitor.Wait(sync, (int)Math.Min(remaining, int.MaxValue));
            }
            return messages.ToArray();
        }
    }
}
