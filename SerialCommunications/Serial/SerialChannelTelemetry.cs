namespace SerialCommunications.Serial;

/// <summary>
/// Accumulated measurements for one <see cref="SerialChannel"/> lifetime.
/// </summary>
/// <remarks>
/// Counters are monotonic until <see cref="Reset"/> is called. Connection and protocol state do
/// not belong here; this type contains measurements derived from operation only.
/// </remarks>
public sealed class SerialChannelTelemetry
{
    private long bytesReceived;
    private long messagesReceived;
    private long messagesTransmitted;
    private long integrityFailures;
    private long receiveNotifications;
    private long recoveryAttempts;
    private long successfulRecoveries;

    public long BytesReceived => Interlocked.Read(ref bytesReceived);
    public long MessagesReceived => Interlocked.Read(ref messagesReceived);
    public long MessagesTransmitted => Interlocked.Read(ref messagesTransmitted);
    public long IntegrityFailures => Interlocked.Read(ref integrityFailures);
    public long ReceiveNotifications => Interlocked.Read(ref receiveNotifications);
    public long RecoveryAttempts => Interlocked.Read(ref recoveryAttempts);
    public long SuccessfulRecoveries => Interlocked.Read(ref successfulRecoveries);

    /// <summary>Clears accumulated measurements without affecting channel operation.</summary>
    public void Reset()
    {
        Interlocked.Exchange(ref bytesReceived, 0);
        Interlocked.Exchange(ref messagesReceived, 0);
        Interlocked.Exchange(ref messagesTransmitted, 0);
        Interlocked.Exchange(ref integrityFailures, 0);
        Interlocked.Exchange(ref receiveNotifications, 0);
        Interlocked.Exchange(ref recoveryAttempts, 0);
        Interlocked.Exchange(ref successfulRecoveries, 0);
    }

    internal void RecordBytesReceived(int count) => Interlocked.Add(ref bytesReceived, count);
    internal void RecordMessageReceived() => Interlocked.Increment(ref messagesReceived);
    internal void RecordMessageTransmitted() => Interlocked.Increment(ref messagesTransmitted);
    internal void RecordIntegrityFailure() => Interlocked.Increment(ref integrityFailures);
    internal void RecordReceiveNotification() => Interlocked.Increment(ref receiveNotifications);
    internal void RecordRecoveryAttempt() => Interlocked.Increment(ref recoveryAttempts);
    internal void RecordSuccessfulRecovery() => Interlocked.Increment(ref successfulRecoveries);
}
