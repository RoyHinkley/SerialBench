namespace SerialCommunications.Serial;

/// <summary>
/// Reusable configuration for the communications behavior of a <see cref="SerialChannel"/>.
/// </summary>
/// <remarks>
/// This is deliberately independent of persistence, UI representation, named profiles, and
/// application defaults. Consumers such as SerialBench and AeonHacs may construct it in their
/// own native configuration systems.
/// </remarks>
public sealed record SerialChannelConfiguration(SerialPortSettings PortSettings)
{
    public SerialChannel.RtsModes RtsMode { get; init; } = SerialChannel.RtsModes.Enabled;
    public int MillisecondsBetweenMessages { get; init; } = -1;
    public int MillisecondsBetweenBytes { get; init; } = -1;
    public int MaximumMillisecondsSilenceInMessage { get; init; } = 5;
    public SerialProtocolSettings ProtocolSettings { get; init; } = SerialProtocolSettings.From(null, false);
    public SerialRecoveryPolicy RecoveryPolicy { get; init; } = SerialRecoveryPolicy.Default;
}
