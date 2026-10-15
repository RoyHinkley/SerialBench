namespace SerialPortTerminal.Serial.Protocol;

/// <summary>Composes serial byte acquisition, message decoding, and isolated consumer dispatch.</summary>
/// <remarks>
/// Transport notifications call <see cref="SignalDataAvailable"/> only. Acquisition and decoding run on the
/// receive-pump worker, while completed messages are delivered on the dispatch worker so application work cannot
/// delay continued serial reception.
/// </remarks>
internal sealed class SerialMessagePipeline : IDisposable
{
    private readonly MessageDispatchQueue dispatch;
    private readonly SerialReceiveAcquirer acquirer;
    private readonly ReceivePump receivePump;
    private bool disposed;

    public SerialMessagePipeline(
        ISerialTransport transport,
        IMessageDecoder decoder,
        Action<DecodedMessage> messageReceived,
        TimeSpan silenceInterval,
        Action<Exception>? receiveError = null,
        Action<Exception>? consumerError = null,
        Action<int>? bytesAcquired = null,
        int acquisitionBufferSize = 4096)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(messageReceived);

        dispatch = new MessageDispatchQueue(decoder, messageReceived, consumerError);
        acquirer = new SerialReceiveAcquirer(
            transport,
            bytes =>
            {
                bytesAcquired?.Invoke(bytes.Length);
                dispatch.Consume(bytes.Span);
            },
            acquisitionBufferSize);
        receivePump = new ReceivePump(
            () => acquirer.DrainAvailable(),
            dispatch.SilenceElapsed,
            silenceInterval,
            receiveError);
    }

    /// <summary>Signals transport receive activity without performing receive work on the notification path.</summary>
    public void SignalDataAvailable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        receivePump.SignalDataAvailable();
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        dispatch.Reset();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        // Stop acquisition before closing dispatch so no receive worker can enqueue after dispatch disposal.
        receivePump.Dispose();
        dispatch.Dispose();
    }
}
