namespace SerialPortTerminal.Serial.Protocol;

/// <summary>Composes independent framing and integrity strategies into a message decoder.</summary>
public sealed class MessageDecoder(IMessageFramer framer, IMessageIntegrity integrity) : IMessageDecoder
{
    private readonly IMessageFramer framer = framer ?? throw new ArgumentNullException(nameof(framer));
    private readonly IMessageIntegrity integrity = integrity ?? throw new ArgumentNullException(nameof(integrity));

    public void Consume(ReadOnlySpan<byte> bytes, Action<DecodedMessage> messageReady)
    {
        ArgumentNullException.ThrowIfNull(messageReady);
        framer.Consume(bytes, frame => Decode(frame, messageReady));
    }

    public void SilenceElapsed(Action<DecodedMessage> messageReady)
    {
        ArgumentNullException.ThrowIfNull(messageReady);
        framer.SilenceElapsed(frame => Decode(frame, messageReady));
    }

    public void Reset() => framer.Reset();

    private void Decode(ReadOnlyMemory<byte> frame, Action<DecodedMessage> messageReady)
    {
        var wireFrame = frame.ToArray();
        var valid = integrity.TryValidate(wireFrame, out var payload);
        messageReady(new DecodedMessage(wireFrame, payload, valid));
    }
}
