using System.IO.Ports;

namespace SerialPortTerminal.Serial;

/// <summary>
/// Describes the settings required to open a serial port.
/// </summary>
/// <remarks>
/// This is intentionally only a connection-settings value. Port discovery and lists of
/// UI choices belong outside the serial connection description.
/// </remarks>
public sealed record SerialPortSettings(
    string PortName,
    int BaudRate = 115200,
    Parity Parity = Parity.None,
    int DataBits = 8,
    StopBits StopBits = StopBits.One,
    Handshake Handshake = Handshake.None);
