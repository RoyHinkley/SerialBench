using SerialCommunications.Diagnostics;
using SerialCommunications.Serial;

namespace SerialBench.Application;

/// <summary>Coordinates interactive command/response traffic over a <see cref="SerialChannel"/>.</summary>
/// <remarks>The terminal/application boundary is byte-oriented. Conversion between human notation and bytes belongs outside this class.</remarks>
public sealed class TerminalSession : IDisposable
{
    private SerialChannel? serialChannel;
    private DiagnosticLog? log;
    private bool logEverything;
    public event EventHandler? Connected;
    public event EventHandler? Disconnecting;
    public event Action<ReceivedData>? DataReceived;
    public event Action<byte[]>? CommandSent;
    public SerialChannel? SerialChannel { get => serialChannel; set { if (ReferenceEquals(serialChannel, value)) return; DetachSerialChannel(); serialChannel = value; AttachSerialChannel(); } }
    public DiagnosticLog? Log { get => log; set { if (ReferenceEquals(log, value)) return; log = value; UpdateSerialChannelLogging(); } }
    public bool LogEverything { get => logEverything; set { if (logEverything == value) return; logEverything = value; UpdateSerialChannelLogging(); } }
    public bool LogCommands { get; set; }
    public bool LogResponses { get; set; }
    public bool Ready => SerialChannel?.Ready == true;
    public bool Busy => SerialChannel?.Busy == true;
    public bool Idle => !Busy;
    public bool Free => SerialChannel?.Free == true;
    public uint CommandCount { get; private set; }
    public uint ResponseCount { get; private set; }
    public byte[] LastCommand { get; private set; } = [];
    public ReceivedData? LastReceivedData { get; private set; }
    public string LastResponse => LastReceivedData?.PayloadText ?? string.Empty;
    public TerminalSession() { }
    public TerminalSession(SerialChannel serialChannel, DiagnosticLog? log = null) { this.log = log; SerialChannel = serialChannel; }
    public bool Connect() => SerialChannel?.Connect() == true;
    public bool Disconnect() => SerialChannel?.Disconnect() ?? true;
    public bool Reset() => SerialChannel?.Reset() == true;
    public void WaitForIdle() => SerialChannel?.WaitForIdle();

    /// <summary>Sends exact payload bytes; text/escape interpretation is deliberately outside this layer.</summary>
    public bool Send(ReadOnlySpan<byte> command)
    {
        if (SerialChannel is null || command.IsEmpty) return false;
        var bytes = command.ToArray(); LastCommand = bytes; CommandCount++;
        if (LogCommands) Log?.Record($"TerminalSession command: \"{SendDataParser.Escape(bytes)}\"");
        if (LogEverything) Log?.Record($"TerminalSession sending command #{CommandCount}.");
        var acceptedWhileReady = SerialChannel.Command(bytes);
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
    private void AttachSerialChannel() { if (serialChannel is null) return; serialChannel.Connected += SerialChannelConnected; serialChannel.Disconnecting += SerialChannelDisconnecting; serialChannel.DataReceived += Receive; UpdateSerialChannelLogging(); }
    private void DetachSerialChannel() { if (serialChannel is null) return; serialChannel.Connected -= SerialChannelConnected; serialChannel.Disconnecting -= SerialChannelDisconnecting; serialChannel.DataReceived -= Receive; serialChannel.Log = null; }
    private void UpdateSerialChannelLogging() { if (serialChannel is not null) { serialChannel.Log = Log; serialChannel.LogEverything = LogEverything; } }
    private void SerialChannelConnected(object? sender, EventArgs e) => Dispatch(Connected, nameof(Connected));
    private void SerialChannelDisconnecting(object? sender, EventArgs e) => Dispatch(Disconnecting, nameof(Disconnecting));
    private void Dispatch(Action<byte[]>? handlers, byte[] value, string name) { if (handlers is null) return; foreach (Action<byte[]> handler in handlers.GetInvocationList()) try { handler(value); } catch (Exception e) { Log?.Record($"TerminalSession ERROR: {name} handler exception: {e}"); } }
    private void Dispatch(Action<ReceivedData>? handlers, ReceivedData value, string name) { if (handlers is null) return; foreach (Action<ReceivedData> handler in handlers.GetInvocationList()) try { handler(value); } catch (Exception e) { Log?.Record($"TerminalSession ERROR: {name} handler exception: {e}"); } }
    private void Dispatch(EventHandler? handlers, string name) { if (handlers is null) return; foreach (EventHandler handler in handlers.GetInvocationList()) try { handler(this, EventArgs.Empty); } catch (Exception e) { Log?.Record($"TerminalSession ERROR: {name} handler exception: {e}"); } }
    public void Dispose() { if (serialChannel is not null) { DetachSerialChannel(); serialChannel.Dispose(); serialChannel = null; } }
}
