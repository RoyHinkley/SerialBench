using SerialPortTerminal.Serial.Protocol;

namespace SerialPortTerminal.Tests;

public sealed class MessageDispatchQueueTests
{
    [Fact]
    public void SlowConsumerDoesNotBlockDecoding()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var secondDelivered = new ManualResetEventSlim();
        var delivered = 0;
        using var dispatch = new MessageDispatchQueue(
            new MessageDecoder(new TerminatorMessageFramer(0x03), NoMessageIntegrity.Instance),
            _ =>
            {
                var n = Interlocked.Increment(ref delivered);
                if (n == 1)
                {
                    entered.Set();
                    release.Wait();
                }
                else secondDelivered.Set();
            });

        dispatch.Consume(new byte[] { 0x11, 0x03 });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));

        var completed = Task.Run(() => dispatch.Consume(new byte[] { 0x22, 0x03 }))
            .Wait(TimeSpan.FromSeconds(1));

        Assert.True(completed);
        Assert.Equal(1, Volatile.Read(ref delivered));
        release.Set();
        Assert.True(secondDelivered.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(2, Volatile.Read(ref delivered));
    }

    [Fact]
    public void ConsumerExceptionsDoNotStopFollowingMessages()
    {
        using var delivered = new ManualResetEventSlim();
        using var errorSeen = new ManualResetEventSlim();
        var calls = 0;
        using var dispatch = new MessageDispatchQueue(
            new MessageDecoder(new TerminatorMessageFramer(0x03), NoMessageIntegrity.Instance),
            _ =>
            {
                if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("test");
                delivered.Set();
            },
            e => { Assert.IsType<InvalidOperationException>(e); errorSeen.Set(); });

        dispatch.Consume(new byte[] { 0x11, 0x03, 0x22, 0x03 });

        Assert.True(errorSeen.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(delivered.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Fact]
    public void DisposeDrainsAlreadyDecodedMessages()
    {
        var messages = new List<byte>();
        using (var dispatch = new MessageDispatchQueue(
            new MessageDecoder(new TerminatorMessageFramer(0x03), NoMessageIntegrity.Instance),
            message => messages.Add(message.Payload.Span[0])))
        {
            dispatch.Consume(new byte[] { 0x11, 0x03, 0x22, 0x03, 0x33, 0x03 });
        }

        Assert.Equal(new byte[] { 0x11, 0x22, 0x33 }, messages);
    }

    [Fact]
    public void CallsAfterDisposeAreRejected()
    {
        var dispatch = new MessageDispatchQueue(
            new MessageDecoder(new TerminatorMessageFramer(0x03), NoMessageIntegrity.Instance),
            _ => { });
        dispatch.Dispose();

        Assert.Throws<ObjectDisposedException>(() => dispatch.Consume(new byte[] { 0x11 }));
        Assert.Throws<ObjectDisposedException>(() => dispatch.SilenceElapsed());
        Assert.Throws<ObjectDisposedException>(() => dispatch.Reset());
    }
}
