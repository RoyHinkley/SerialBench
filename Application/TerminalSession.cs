using SerialCommunications.Diagnostics;
using SerialCommunications.Serial;

namespace SerialBench.Application;

/// <summary>Coordinates interactive command/response traffic over a <see cref="SerialDevice"/>.</summary>
/// <remarks>The terminal/application boundary is byte-oriented. Conversion between human notation and bytes belongs outside this class.</remarks>
public sealed class TerminalSession : IDisposable
{
    private SerialDevice? serialDevice;
    private DiagnosticLog? log;
    private bool logEverything;
    public event EventHandler? Connected;
    public event EventHandler? Disconnecting;
    public event Action<ReceivedData>? DataReceived;
    public event Action<byte[]>? CommandSent;
    public SerialDevice? SerialDevice { get => serialDevice; set { if (ReferenceEquals(serialDevice, value)) return; DetachSerialDevice(); serialDevice = value; AttachSerialDevice(); } }
    public DiagnosticLog? Log { get => log; set { if (ReferenceEquals(log, value)) return; log = value; UpdateSerialDeviceLogging(); } }
    public bool LogEverything { get => logEverything; set { if (logEverything == value) return; logEverything = value; UpdateSerialDeviceLogging(); } }
    public bool LogCommands { get; set; }
    public bool LogResponses { get; set; }
    public bool Ready => SerialDevice?.Ready == true;
    public bool Busy => SerialDevice?.Busy == true;
    public bool Idle => !Busy;
    public bool Free => SerialDevice?.Free == true;
    public uint CommandCount { get; private set; }
    public uint ResponseCount { get; private set; }
    public byte[] LastCommand { get; private set; } = [];
    public ReceivedData? LastReceivedData { get; private set; }
    public string LastResponse => LastReceivedData?.PayloadText ?? string.Empty;
    public TerminalSession() { }
    public TerminalSession(SerialDevice serialDevice, DiagnosticLog? log = null) { this.log = log; SerialDevice = serialDevice; }
    public bool Connect() => SerialDevice?.Connect() == true;
    public bool Disconnect() => SerialDevice?.Disconnect() ?? true;
    public bool Reset() => SerialDevice?.Reset() == true;
    public void WaitForIdle() => SerialDevice?.WaitForIdle();

    /// <summary>Sends exact payload bytes; text/escape interpretation is deliberately outside this layer.</summary>
    public bool Send(ReadOnlySpan<byte> command)
    {
        if (SerialDevice is null || command.IsEmpty) return false;
        var bytes = command.ToArray(); LastCommand = bytes; CommandCount++;
        if (LogCommands) Log?.Record($"TerminalSession command: \"{SendDataParser.Escape(bytes)}\"");
        if (LogEverything) Log?.Record($"TerminalSession sending command #{CommandCount}.");
        var acceptedWhileReady = SerialDevice.Command(bytes);
        Dispatch(CommandSent, bytes, "CommandSent"); return acceptedWhileReady;
    }

    private void Receive(ReceivedData data)
    {
        LastReceivedData = data; ResponseCount++;
        if (LogResponses)
        {
            var crcError = data.CrcValid == false ? " CRC ERROR" : string.Empty;
            Log?.Record($"TerminalSession response: \"{SerialDataFormatter.ToEscapedText(data.PayloadBytes.Span)}\"{crcError}");
        }
        if (LogEverything) Log?.Record($"TerminalSession received response #{ResponseCount}.");
        Dispatch(DataReceived, data, "DataReceived");
    }
    private void AttachSerialDevice() { if (serialDevice is null) return; serialDevice.Connected += SerialDeviceConnected; serialDevice.Disconnecting += SerialDeviceDisconnecting; serialDevice.DataReceived += Receive; UpdateSerialDeviceLogging(); }
    private void DetachSerialDevice() { if (serialDevice is null) return; serialDevice.Connected -= SerialDeviceConnected; serialDevice.Disconnecting -= SerialDeviceDisconnecting; serialDevice.DataReceived -= Receive; serialDevice.Log = null; }
    private void UpdateSerialDeviceLogging() { if (serialDevice is not null) { serialDevice.Log = Log; serialDevice.LogEverything = LogEverything; } }
    private void SerialDeviceConnected(object? sender, EventArgs e) => Dispatch(Connected, nameof(Connected));
    private void SerialDeviceDisconnecting(object? sender, EventArgs e) => Dispatch(Disconnecting, nameof(Disconnecting));
    private void Dispatch(Action<byte[]>? handlers, byte[] value, string name) { if (handlers is null) return; foreach (Action<byte[]> handler in handlers.GetInvocationList()) try { handler(value); } catch (Exception e) { Log?.Record($"TerminalSession ERROR: {name} handler exception: {e}"); } }
    private void Dispatch(Action<ReceivedData>? handlers, ReceivedData value, string name) { if (handlers is null) return; foreach (Action<ReceivedData> handler in handlers.GetInvocationList()) try { handler(value); } catch (Exception e) { Log?.Record($"TerminalSession ERROR: {name} handler exception: {e}"); } }
    private void Dispatch(EventHandler? handlers, string name) { if (handlers is null) return; foreach (EventHandler handler in handlers.GetInvocationList()) try { handler(this, EventArgs.Empty); } catch (Exception e) { Log?.Record($"TerminalSession ERROR: {name} handler exception: {e}"); } }
    public void Dispose() { if (serialDevice is not null) { DetachSerialDevice(); serialDevice.Dispose(); serialDevice = null; } }
}
