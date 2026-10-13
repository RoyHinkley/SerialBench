using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;
using SerialPortTerminal.Diagnostics;

namespace SerialPortTerminal.Serial;

public readonly record struct SerialSignalState(bool Rts, bool Cts, bool Dtr, bool Dsr, bool Dcd, bool Ring)
{
    public override string ToString() => $"RTS={(Rts ? 1 : 0)} CTS={(Cts ? 1 : 0)} DTR={(Dtr ? 1 : 0)} DSR={(Dsr ? 1 : 0)} DCD={(Dcd ? 1 : 0)} RI={(Ring ? 1 : 0)}";
}

/// <summary>Manages serial transport, framing, CRC validation, pacing, recovery, and diagnostics.</summary>
/// <remarks>The receive path is intentionally conservative and must not be simplified without characterization tests, particularly where a termination character and CRC interact.</remarks>
public sealed class SerialDevice : IDisposable
{
    private const int RxBufferSize = 4096;
    private static long instanceCount;
    public enum RtsModes { Enabled, Disabled, Toggle }

    public SerialPortSettings PortSettings { get; set; }
    public CrcOptions? CrcConfig { get; set; }
    public RtsModes RtsMode { get; set; } = RtsModes.Enabled;
    public int MillisecondsBetweenMessages { get; set; } = -1;
    public int MillisecondsBetweenBytes { get; set; } = -1;
    public int MaximumMillisecondsSilenceInMessage { get; set; } = 5;
    public bool BinaryComms { get; set; }
    public bool EscapeLoggedData { get; set; }
    public bool IgnoreCRCErrors { get; set; }
    public bool LogEverything { get; set; }
    public bool LogSignals { get; set; }
    public DiagnosticLog? Log { get; set; }

    public event EventHandler? Connected;
    public event EventHandler? Disconnecting;
    public event Action<ReceivedData>? DataReceived;
    public event Action<SerialSignalState>? SignalsChanged;

    public uint ReceiveEvents { get; private set; }
    public uint ETXCount { get; private set; }
    public uint ResponseCount { get; private set; }
    public uint TotalBytesRead { get; private set; }
    public bool ErrorBufferOverflow { get; private set; }
    public uint BufferOverflows { get; private set; }
    public ushort RxCrcCode { get; private set; }
    public bool ErrorCrc { get; private set; }
    public uint CRCErrors { get; private set; }
    public uint Resets { get; private set; }
    public double MillisecondsSinceLastTx => txSw.Elapsed.TotalMilliseconds;
    public double MillisecondsSinceLastRx => rxSw.Elapsed.TotalMilliseconds;
    public bool Ready => port?.IsOpen == true && connected;
    public bool HaveWork => transmitting || !commandQ.IsEmpty;
    public bool Busy => Ready && HaveWork;
    public bool Idle => !Busy;
    public bool Free => Ready && !HaveWork;
    public SerialProtocolSettings ProtocolSettings => protocolSettings;
    public SerialProtocolSettings? PendingProtocolSettings => pendingProtocolSettings;

    private readonly long instanceNumber = Interlocked.Increment(ref instanceCount);
    private readonly ISerialTransportFactory transportFactory;
    private ISerialTransport? port;
    private volatile bool connected;
    private volatile bool active;
    private byte[] rx = new byte[RxBufferSize];
    private readonly byte[] xferBuffer = new byte[RxBufferSize];
    private Crc? rxCrc;
    private Crc? txCrc;
    private ConcurrentQueue<byte[]> commandQ = new();
    private volatile bool transmitting;
    private Thread? txThread;
    private Thread? rxThread;
    private Thread? processThread;
    private readonly AutoResetEvent txSignal = new(false);
    private readonly AutoResetEvent rxSignal = new(false);
    private readonly AutoResetEvent processSignal = new(false);
    private readonly Stopwatch txSw = new();
    private readonly Stopwatch rxSw = new();
    private volatile int rxbWrite;
    private int rxbHead;
    private SerialSignalState? lastSignalState;
    private readonly object protocolLock = new();
    private SerialProtocolSettings protocolSettings = SerialProtocolSettings.From(null, false);
    private SerialProtocolSettings? pendingProtocolSettings;

    public SerialDevice(SerialPortSettings portSettings) : this(portSettings, new SerialPortTransportFactory()) { }
    internal SerialDevice(SerialPortSettings portSettings, ISerialTransportFactory transportFactory)
    {
        PortSettings = portSettings ?? throw new ArgumentNullException(nameof(portSettings));
        this.transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
    }
    public SerialDevice(string portName, int baudRate = 115200) : this(new SerialPortSettings(portName, baudRate)) { }

    /// <summary>Queues exact payload bytes for transmission. The caller's buffer is copied before returning.</summary>
    public bool Command(ReadOnlySpan<byte> command)
    {
        if (command.IsEmpty) return false;
        var bytes = command.ToArray();
        Trace($"received Command \"{SerialDataFormatter.ToEscapedText(bytes)}\"");
        commandQ.Enqueue(bytes); txSignal.Set(); return Ready;
    }

    /// <summary>Stages a complete protocol snapshot for atomic adoption at the next clean receive-message boundary.</summary>
    /// <returns>False when the requested settings require a different receive framing worker; reconnect is then required.</returns>
    public bool StageProtocolSettings(SerialProtocolSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (protocolLock)
        {
            if (!protocolSettings.UsesSameFramingStrategyAs(settings)) return false;
            pendingProtocolSettings = settings;
        }
        processSignal.Set();
        return true;
    }

    public bool Connect()
    {
        if (connected) return true; Trace("connecting...");
        try
        {
            port = transportFactory.Create(PortSettings, RtsMode);
            port.DataReceived += RxDetected; port.PinChanged += PinChanged; port.Open(); port.DiscardOutBuffer(); port.DiscardInBuffer();
            protocolSettings = SerialProtocolSettings.From(CrcConfig, !IgnoreCRCErrors); pendingProtocolSettings = null;
            CreateCommsSession(); active = true; rxThread = StartThread(Receive, "receive"); processThread = StartThread(protocolSettings.UseCrc && !protocolSettings.OmitTermChar ? ProcessRx : ProcessRxBySilence, "process_rx"); txThread = StartThread(Transmit, "transmit"); connected = true;
            Trace("connected."); PublishSignals(force: true); Dispatch(Connected, nameof(Connected)); return true;
        }
        catch (Exception e) { Error($"Connect failed: {e}"); Disconnect(); return false; }
    }

    public bool Disconnect()
    {
        var errors = 0; Trace("disconnecting..."); if (connected) Dispatch(Disconnecting, nameof(Disconnecting)); connected = false; active = false; txSignal.Set(); rxSignal.Set(); processSignal.Set();
        if (port is not null) { port.DataReceived -= RxDetected; port.PinChanged -= PinChanged; try { port.Close(); } catch (Exception e) { errors++; Error($"port close exception: {e}"); } try { port.Dispose(); } catch (Exception e) { errors++; Error($"port dispose exception: {e}"); } port = null; }
        if (!WaitForWorkers(250)) { errors++; Error("worker thread shutdown timeout."); } lastSignalState = null; Trace("disconnected."); return errors == 0;
    }
    public bool Reset() { Trace("resetting..."); Disconnect(); Resets++; return Connect(); }
    public void WaitForIdle() { while (!Idle) Thread.Sleep(5); }

    private void CreateCommsSession()
    {
        commandQ = new(); transmitting = false; rx = new byte[RxBufferSize]; rxbWrite = rxbHead = 0; var crc = protocolSettings.CreateCrcOptions(); rxCrc = crc is null ? null : new Crc(crc); txCrc = crc is null ? null : new Crc(crc); ErrorBufferOverflow = ErrorCrc = false; RxCrcCode = 0; ResponseCount = 0; lastSignalState = null; txSw.Reset(); rxSw.Reset(); Drain(txSignal); Drain(rxSignal); Drain(processSignal);
    }
    private Thread StartThread(ThreadStart action, string role) { var thread = new Thread(action) { Name = $"SerialDevice {instanceNumber} {role}", IsBackground = true }; thread.Start(); return thread; }

    private void Transmit()
    {
        Trace("starting Transmit thread.");
        try
        {
            byte[] tx = []; var offset = 0; txSw.Restart();
            while (active)
            {
                if (offset < tx.Length)
                {
                    try
                    {
                        if (MillisecondsBetweenBytes < 0)
                        {
                            if (MillisecondsSinceLastTx >= MillisecondsBetweenMessages) { Trace($"Transmit ({MillisecondsSinceLastTx:0} ms since last): {SerialDataFormatter.ToByteString(tx)}"); port!.Write(tx, 0, tx.Length); txSw.Restart(); offset = tx.Length; }
                            else Thread.Sleep(1);
                        }
                        else if (MillisecondsSinceLastTx >= MillisecondsBetweenBytes) { Trace($"Transmit ({MillisecondsSinceLastTx:0} ms since last): {tx[offset]:X2}"); port!.Write(tx, offset++, 1); txSw.Restart(); }
                        else Thread.Sleep(1);
                    }
                    catch (Exception e) { Error($"transmit exception: {e}"); Thread.Sleep(Math.Max(20, MillisecondsBetweenMessages)); }
                }
                else { transmitting = false; if (commandQ.TryDequeue(out var command)) { offset = 0; tx = txCrc is null ? command : txCrc.Append(command); transmitting = true; } else txSignal.WaitOne(1000); }
            }
        }
        catch (Exception e) { Error($"fatal Transmit exception: {e}"); }
        finally { Trace("ending Transmit thread."); }
    }

    private void Receive()
    {
        Trace("starting Receive thread."); var signalReceived = false;
        while (active)
        {
            try { if (rxSignal.WaitOne(signalReceived ? MaximumMillisecondsSilenceInMessage : 500)) { if (!active) break; signalReceived = true; } else if (signalReceived) { if (!active) break; GetRxData(); signalReceived = false; } }
            catch (Exception e) { Error($"Receive exception: {e}"); }
        }
        Trace("ending Receive thread.");
    }

    private void GetRxData()
    {
        Trace("GetRxData triggered"); int n; try { n = port?.Read(xferBuffer, 0, xferBuffer.Length) ?? 0; } catch (Exception e) { Error($"GetRxData read exception: {e.Message}"); n = 0; }
        if (n == 0) return; rxSw.Restart(); var available = rxbHead > rxbWrite ? rxbHead - rxbWrite : RxBufferSize - rxbWrite + rxbHead;
        if (ErrorBufferOverflow = n > available) { BufferOverflows++; Error($"receive buffer overflow; {n} bytes lost."); return; }
        var end = rxbWrite + n - 1;
        if (end >= RxBufferSize) { end -= RxBufferSize; var first = n - end - 1; Array.Copy(xferBuffer, 0, rx, rxbWrite, first); Array.Copy(xferBuffer, first, rx, 0, end + 1); }
        else Array.Copy(xferBuffer, 0, rx, rxbWrite, n);
        rxbWrite = Advance(end); TotalBytesRead += (uint)n; Trace($"Receive {n} bytes: {SerialDataFormatter.ToByteString(xferBuffer.AsSpan(0, n))} (TotalBytesRead = {TotalBytesRead})"); processSignal.Set();
    }

    private void ProcessRx()
    {
        Trace("starting ProcessRx thread.");
        try
        {
            var read = ClearRxb(); rxCrc!.Init(); ErrorCrc = false; var bytesWithUnexpectedTermChar = 0;
            while (active)
            {
                while (read != rxbWrite)
                {
                    // Aeon framing reserves TermChar for the final terminator, but either of the two CRC
                    // bytes may equal it. Seeing TermChar therefore cannot be classified until two more
                    // bytes have been examined. This delay is protocol behavior, not accidental complexity.
                    if (bytesWithUnexpectedTermChar > 0) bytesWithUnexpectedTermChar++;
                    var c = rx[read];
                    var settings = protocolSettings;
                    if (c == settings.TermChar)
                    {
                        ETXCount++;
                        if (rxCrc.Good()) { var data = CreateReceivedData(RxbBytes(read), true, settings); Trace($"ProcessRx message: {SerialDataFormatter.ToEscapedText(data.PayloadBytes.Span)}"); DispatchData(data); ResponseCount++; }
                        else bytesWithUnexpectedTermChar = 1;
                        if (rxCrc.Good() || ErrorCrc)
                        {
                            if (ErrorCrc) { var data = CreateReceivedData(RxbBytes(read), false, settings); Error($"ProcessRx: {SerialDataFormatter.ToEscapedText(data.WireBytes.Span)} [CRC Error]"); if (!settings.SuppressCrcErrors) { DispatchData(data); ResponseCount++; } }
                            read = rxbHead = Advance(read); bytesWithUnexpectedTermChar = 0; rxCrc.Init(); ErrorCrc = false; ApplyPendingProtocolSettingsIfBoundary(); continue;
                        }
                    }
                    RxCrcCode = rxCrc.Update(c); read = Advance(read);
                    if (!ErrorCrc && bytesWithUnexpectedTermChar > 2) { HandleCrcError(); read = Retreat2(read); Error($"ProcessRx: {SerialDataFormatter.ToEscapedText(RxbBytes(read))} [CRC Error]"); rxbHead = read; bytesWithUnexpectedTermChar = 0; rxCrc.Init(); ErrorCrc = false; ApplyPendingProtocolSettingsIfBoundary(); }
                }
                if (ErrorBufferOverflow) { HandleCrcError(); read = ClearRxb(); ApplyPendingProtocolSettingsIfBoundary(); }
                if (read == rxbHead) ApplyPendingProtocolSettingsIfBoundary();
                processSignal.WaitOne();
            }
        }
        catch (Exception e) { Error($"fatal ProcessRx exception: {e}"); }
        finally { Trace("ending ProcessRx thread."); }
    }

    private void ProcessRxBySilence()
    {
        Trace("starting ProcessRxBySilence thread.");
        try
        {
            while (processSignal.WaitOne() && active)
            {
                var tail = rxbWrite; if (ErrorBufferOverflow) { ClearRxb(); ApplyPendingProtocolSettingsIfBoundary(); continue; } var wire = RxbBytes(tail); rxbHead = tail; if (wire.Length == 0) { ApplyPendingProtocolSettingsIfBoundary(); continue; }
                var settings = protocolSettings;
                var data = DecodeSilenceFramedMessage(wire, settings);
                if (data.CrcValid == true) { Trace($"ProcessRxBySilence message: {SerialDataFormatter.ToEscapedText(data.PayloadBytes.Span)}"); DispatchData(data); ResponseCount++; }
                else if (data.CrcValid == false) { HandleCrcError(); Error($"ProcessRxBySilence: {SerialDataFormatter.ToEscapedText(wire)} [CRC Error]"); if (!settings.SuppressCrcErrors) { DispatchData(data); ResponseCount++; } }
                else { Trace($"ProcessRxBySilence message: {SerialDataFormatter.ToEscapedText(data.PayloadBytes.Span)}"); DispatchData(data); ResponseCount++; }
                ApplyPendingProtocolSettingsIfBoundary();
            }
        }
        catch (Exception e) { Error($"fatal ProcessRxBySilence exception: {e}"); }
        finally { Trace("ending ProcessRxBySilence thread."); }
    }

    /// <summary>Decodes one message whose boundary has already been established by receive silence.</summary>
    /// <remarks>This is also the characterization-test seam. It deliberately contains the same CRC path used by the live receive worker and performs no serial-port I/O.</remarks>
    internal ReceivedData DecodeSilenceFramedMessage(ReadOnlySpan<byte> wireBytes) => DecodeSilenceFramedMessage(wireBytes, protocolSettings.UseCrc || CrcConfig is null ? protocolSettings : SerialProtocolSettings.From(CrcConfig, !IgnoreCRCErrors));

    private ReceivedData DecodeSilenceFramedMessage(ReadOnlySpan<byte> wireBytes, SerialProtocolSettings settings)
    {
        var wire = wireBytes.ToArray();
        if (!settings.UseCrc) return new ReceivedData(wire, wire, null);

        rxCrc ??= new Crc(settings.CreateCrcOptions()!);
        rxCrc.Init(); ErrorCrc = false; RxCrcCode = rxCrc.Update(wire);
        return CreateReceivedData(wire, rxCrc.Good(), settings);
    }

    /// <summary>Captures the CRC evidence while it is still in hand; presentation must not have to reconstruct it later.</summary>
    private ReceivedData CreateReceivedData(byte[] wire, bool crcValid, SerialProtocolSettings settings)
    {
        var payloadLength = Math.Max(0, wire.Length - 2);
        var payload = wire[..payloadLength];
        ushort? receivedCrc = null;
        if (wire.Length >= 2)
        {
            var first = wire[^2]; var second = wire[^1];
            receivedCrc = settings.MsByteFirst ? (ushort)((first << 8) | second) : (ushort)(first | (second << 8));
        }
        return new ReceivedData(wire, payload, crcValid, receivedCrc, rxCrc!.Code, settings.ExpectedResidue);
    }

    private void ApplyPendingProtocolSettingsIfBoundary()
    {
        SerialProtocolSettings? pending;
        lock (protocolLock)
        {
            pending = pendingProtocolSettings;
            if (pending is null) return;
            protocolSettings = pending;
            pendingProtocolSettings = null;
        }
        var crc = pending.CreateCrcOptions();
        rxCrc = crc is null ? null : new Crc(crc);
        txCrc = crc is null ? null : new Crc(crc);
        CrcConfig = crc;
        IgnoreCRCErrors = !pending.SuppressCrcErrors;
        Trace("applied staged protocol settings at message boundary.");
    }

    private void RxDetected(object sender, SerialDataReceivedEventArgs e) { ReceiveEvents++; rxSignal.Set(); }
    private void PinChanged(object sender, SerialPinChangedEventArgs e) { if (LogSignals) Log?.Record($"SerialDevice @{PortSettings.PortName} pin changed: {e.EventType}"); PublishSignals(ring: e.EventType == SerialPinChange.Ring); }
    private void PublishSignals(bool force = false, bool ring = false)
    {
        var p = port; if (p?.IsOpen != true) return;
        try
        {
            var state = new SerialSignalState(p.RtsEnable, p.CtsHolding, p.DtrEnable, p.DsrHolding, p.CDHolding, ring);
            if (!force && lastSignalState == state) return; lastSignalState = state;
            if (LogSignals) Log?.Record($"SerialDevice @{PortSettings.PortName} signals: {state}");
            var handlers = SignalsChanged; if (handlers is null) return;
            foreach (Action<SerialSignalState> handler in handlers.GetInvocationList()) try { handler(state); } catch (Exception e) { Error($"signal handler exception: {e}"); }
        }
        catch (Exception e) { Error($"signal state exception: {e.Message}"); }
    }

    private void HandleCrcError() { ErrorCrc = true; CRCErrors++; }
    private void DispatchData(ReceivedData data) { var handlers = DataReceived; if (handlers is null) return; foreach (Action<ReceivedData> handler in handlers.GetInvocationList()) try { handler(data); } catch (Exception e) { Error($"data handler exception: {e}"); } }
    private void Dispatch(EventHandler? handlers, string name) { if (handlers is null) return; foreach (EventHandler handler in handlers.GetInvocationList()) try { handler(this, EventArgs.Empty); } catch (Exception e) { Error($"{name} handler exception: {e}"); } }
    private int Advance(int p) => (p + 1) % RxBufferSize;
    private int Retreat2(int p) => (p + RxBufferSize - 2) % RxBufferSize;
    private int ClearRxb() { rxbHead = rxbWrite; return rxbHead; }
    private byte[] RxbBytes(int tail) { if (tail >= rxbHead) return rx[rxbHead..tail]; var result = new byte[RxBufferSize - rxbHead + tail]; Array.Copy(rx, rxbHead, result, 0, RxBufferSize - rxbHead); Array.Copy(rx, 0, result, RxBufferSize - rxbHead, tail); return result; }
    private void Trace(string message) { if (LogEverything) Log?.Record($"SerialDevice @{PortSettings.PortName} {message}"); }
    private void Error(string message) => Log?.Record($"SerialDevice @{PortSettings.PortName} ERROR: {message}");
    private bool WaitForWorkers(int milliseconds) { var deadline = Environment.TickCount64 + milliseconds; while (Environment.TickCount64 < deadline) { if (!(txThread?.IsAlive ?? false) && !(rxThread?.IsAlive ?? false) && !(processThread?.IsAlive ?? false)) return true; Thread.Sleep(5); } return !(txThread?.IsAlive ?? false) && !(rxThread?.IsAlive ?? false) && !(processThread?.IsAlive ?? false); }
    private static void Drain(AutoResetEvent signal) { while (signal.WaitOne(0)) { } }
    public void Close() => Disconnect();
    public void Dispose() { Disconnect(); txSignal.Dispose(); rxSignal.Dispose(); processSignal.Dispose(); }
}