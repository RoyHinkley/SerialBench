using System.Collections.Concurrent;

namespace SerialPortTerminal.Serial.Protocol;

/// <summary>Decouples deterministic message decoding from arbitrary consumer work.</summary>
public sealed class MessageDispatchQueue : IDisposable
{
    private readonly object decoderLock = new();
    private IMessageDecoder decoder;
    private IMessageDecoder? pendingDecoder;
    private Action? pendingDecoderApplied;
    private readonly Action<DecodedMessage> consumer;
    private readonly Action<DecodedMessage>? decoded;
    private readonly Action<Exception>? consumerError;
    private readonly ConcurrentQueue<DecodedMessage> queue = new();
    private readonly AutoResetEvent signal = new(false);
    private readonly Thread worker;
    private volatile bool active = true;
    private bool disposed;

    public MessageDispatchQueue(
        IMessageDecoder decoder,
        Action<DecodedMessage> consumer,
        Action<Exception>? consumerError = null,
        Action<DecodedMessage>? decoded = null)
    {
        this.decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        this.consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        this.consumerError = consumerError;
        this.decoded = decoded;
        worker = new Thread(Dispatch) { Name = "serial message dispatch", IsBackground = true };
        worker.Start();
    }

    public void Consume(ReadOnlySpan<byte> bytes)
    {
        ThrowIfDisposed();
        lock (decoderLock) decoder.Consume(bytes, Enqueue);
    }

    public void SilenceElapsed()
    {
        ThrowIfDisposed();
        lock (decoderLock) decoder.SilenceElapsed(Enqueue);
    }

    /// <summary>Stages a decoder for adoption immediately after the next decoded message boundary.</summary>
    public void StageDecoder(IMessageDecoder replacement, Action? applied = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(replacement);
        lock (decoderLock)
        {
            pendingDecoder = replacement;
            pendingDecoderApplied = applied;
        }
    }

    public void Reset()
    {
        ThrowIfDisposed();
        lock (decoderLock)
        {
            decoder.Reset();
            ApplyPendingDecoder();
        }
        while (queue.TryDequeue(out _)) { }
    }

    private void Enqueue(DecodedMessage message)
    {
        if (!active) return;
        try { decoded?.Invoke(message); }
        catch (Exception e) { ReportError(e); }
        queue.Enqueue(message);
        signal.Set();
        ApplyPendingDecoder();
    }

    private void ApplyPendingDecoder()
    {
        if (pendingDecoder is null) return;
        decoder = pendingDecoder;
        pendingDecoder = null;
        var applied = pendingDecoderApplied;
        pendingDecoderApplied = null;
        try { applied?.Invoke(); }
        catch (Exception e) { ReportError(e); }
    }

    private void Dispatch()
    {
        try
        {
            while (active)
            {
                if (!queue.TryDequeue(out var message))
                {
                    signal.WaitOne();
                    continue;
                }

                try { consumer(message); }
                catch (Exception e) { ReportError(e); }
            }
        }
        finally
        {
            while (queue.TryDequeue(out _)) { }
            signal.Dispose();
        }
    }

    private void ReportError(Exception exception)
    {
        try { consumerError?.Invoke(exception); }
        catch { /* Error reporting must not terminate receive decoding or dispatch. */ }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        active = false;
        while (queue.TryDequeue(out _)) { }
        signal.Set();
        // Deliberately do not join. A consumer is application code and may block indefinitely;
        // serial disconnect/disposal must never inherit that lifetime.
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
