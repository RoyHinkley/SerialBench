using System.Collections.Concurrent;

namespace SerialPortTerminal.Serial.Protocol;

/// <summary>
/// Decouples deterministic message decoding from arbitrary consumer work.
/// </summary>
/// <remarks>
/// <see cref="Consume"/> and <see cref="SilenceElapsed"/> perform framing and integrity decoding synchronously,
/// then enqueue completed messages. Consumer callbacks run only on the dispatch worker and therefore cannot
/// stall transport notification, byte acquisition, or decoding.
/// </remarks>
public sealed class MessageDispatchQueue : IDisposable
{
    private readonly IMessageDecoder decoder;
    private readonly Action<DecodedMessage> consumer;
    private readonly Action<Exception>? consumerError;
    private readonly ConcurrentQueue<DecodedMessage> queue = new();
    private readonly AutoResetEvent signal = new(false);
    private readonly Thread worker;
    private volatile bool active = true;
    private bool disposed;

    public MessageDispatchQueue(IMessageDecoder decoder, Action<DecodedMessage> consumer, Action<Exception>? consumerError = null)
    {
        this.decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        this.consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        this.consumerError = consumerError;
        worker = new Thread(Dispatch) { Name = "serial message dispatch", IsBackground = true };
        worker.Start();
    }

    public void Consume(ReadOnlySpan<byte> bytes)
    {
        ThrowIfDisposed();
        decoder.Consume(bytes, Enqueue);
    }

    public void SilenceElapsed()
    {
        ThrowIfDisposed();
        decoder.SilenceElapsed(Enqueue);
    }

    public void Reset()
    {
        ThrowIfDisposed();
        decoder.Reset();
        while (queue.TryDequeue(out _)) { }
    }

    private void Enqueue(DecodedMessage message)
    {
        queue.Enqueue(message);
        signal.Set();
    }

    private void Dispatch()
    {
        while (active || !queue.IsEmpty)
        {
            if (!queue.TryDequeue(out var message))
            {
                signal.WaitOne();
                continue;
            }

            try { consumer(message); }
            catch (Exception e) { consumerError?.Invoke(e); }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        active = false;
        signal.Set();
        if (worker != Thread.CurrentThread && worker.IsAlive) worker.Join();
        signal.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
