namespace SerialPortTerminal.Serial;

/// <summary>Describes the availability of a requested serial transport.</summary>
/// <remarks>
/// Connection intent, transport availability, and communications health are deliberately separate concepts.
/// In particular, <see cref="Unavailable"/> does not mean that the user has disconnected: the requested
/// connection may remain active while the device owns no OS serial-port handle and periodically attempts
/// to reacquire the configured endpoint.
/// </remarks>
public enum SerialConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Unavailable,
    Disposed
}
