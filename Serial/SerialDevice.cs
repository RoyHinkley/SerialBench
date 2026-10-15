using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;
using SerialPortTerminal.Diagnostics;
using SerialPortTerminal.Serial.Protocol;

namespace SerialPortTerminal.Serial;

public readonly record struct SerialSignalState(bool Rts, bool Cts, bool Dtr, bool Dsr, bool Dcd, bool Ring)
{
    public override string ToString() => $"RTS={(Rts ? 1 : 0)} CTS={(Cts ? 1 : 0)} DTR={(Dtr ? 1 : 0)} DSR={(Dsr ? 1 : 0)} DCD={(Dcd ? 1 : 0)} RI={(Ring ? 1 : 0)}";
}

/// <summary>Coordinates serial transport ownership, message processing, pacing, recovery, and diagnostics.</summary>
public sealed class SerialDevice : IDisposable
{
    private static readonly TimeSpan AvailabilityPollInterval = TimeSpan.FromMilliseconds(250);
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
    public SerialRecoveryPolicy RecoveryPolicy { get; set; } = SerialRecoveryPolicy.Default;
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
    public bool ConnectionRequested { get; private set; }
    public SerialConnectionState ConnectionState { get; private set; } = SerialConnectionState.Disconnected;
    public bool RecoveryActive => recoveryActive && ConnectionRequested && ConnectionState == SerialConnectionState.Unavailable;
    public int RecoveryAttempts { get; private set; }
    public DateTimeOffset? RecoveryStartedAt { get; private set; }
    public bool Ready => ConnectionState == SerialConnectionState.Connected && port?.IsOpen == true;
    public bool HaveWork => transmitting || !commandQ.IsEmpty;
    public bool Busy => Ready && HaveWork;
    public bool Idle => !Busy;
    public bool Free => Ready && !HaveWork;
    public SerialProtocolSettings ProtocolSettings => protocolSettings;
    public SerialProtocolSettings? PendingProtocolSettings => pendingProtocolSettings;

    private readonly long instanceNumber = Interlocked.Increment(ref instanceCount);
    private readonly ISerialTransportFactory transportFactory;
    private readonly object lifecycleLock = new();
    private readonly object protocolLock = new();
    private readonly ConcurrentQueue<SerialProtocolSettings> receivedSettings = new();
    private ISerialTransport? port;
    private SerialMessagePipeline? receivePipeline;
    private volatile bool active;
    private bool disposed;
    private Crc? txCrc;
    private ConcurrentQueue<byte[]> commandQ = new();
    private volatile bool transmitting;
    private Thread? txThread;
    private Thread? recoveryThread;
    private volatile bool recoveryActive;
    private readonly AutoResetEvent txSignal = new(false);
    private readonly AutoResetEvent recoverySignal = new(false);
    private readonly Stopwatch txSw = new(), rxSw = new(), recoverySw = new();
    private SerialSignalState? lastSignalState;
    private SerialProtocolSettings protocolSettings = SerialProtocolSettings.From(null, false);
    private SerialProtocolSettings? pendingProtocolSettings;
    private string? recoveryFailureSignature;
    private int repeatedRecoveryFailures;

    public SerialDevice(SerialPortSettings portSettings) : this(portSettings, new SerialPortTransportFactory()) { }
    internal SerialDevice(SerialPortSettings portSettings, ISerialTransportFactory transportFactory)
    { PortSettings = portSettings ?? throw new ArgumentNullException(nameof(portSettings)); this.transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory)); }
    public SerialDevice(string portName, int baudRate = 115200) : this(new SerialPortSettings(portName, baudRate)) { }

    public bool Command(ReadOnlySpan<byte> command)
    {
        ThrowIfDisposed(); if (command.IsEmpty) return false; var bytes = command.ToArray();
        Trace($"received Command \"{SerialDataFormatter.ToEscapedText(bytes)}\""); commandQ.Enqueue(bytes); txSignal.Set(); return Ready;
    }

    public bool StageProtocolSettings(SerialProtocolSettings settings)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(settings);
        lock (protocolLock)
        {
            if (!protocolSettings.UsesSameFramingStrategyAs(settings)) return false;
            pendingProtocolSettings = settings;
            if (receivePipeline is null) { ApplyProtocolSettings(settings); pendingProtocolSettings = null; return true; }
            receivePipeline.StageDecoder(settings.CreateMessageDecoder(), () => ApplyStagedProtocolSettings(settings));
        }
        return true;
    }

    public bool Connect()
    {
        lock (lifecycleLock)
        {
            ThrowIfDisposed(); ConnectionRequested = true; if (ConnectionState == SerialConnectionState.Connected) return true;
            var connected = TryAcquireTransport(false); StartConnectionMaintenance(); return connected;
        }
    }

    private bool TryAcquireTransport(bool isRecoveryAttempt)
    {
        ConnectionState = SerialConnectionState.Connecting;
        if (!isRecoveryAttempt || repeatedRecoveryFailures < 3) Trace(isRecoveryAttempt ? "recovery connection attempt..." : "connecting...");
        try
        {
            port = transportFactory.Create(PortSettings, RtsMode); port.DataReceived += RxDetected; port.PinChanged += PinChanged; port.Open(); port.DiscardOutBuffer(); port.DiscardInBuffer();
            protocolSettings = SerialProtocolSettings.From(CrcConfig, !IgnoreCRCErrors); pendingProtocolSettings = null; CreateCommsSession(); active = true; txThread = StartThread(Transmit, "transmit"); ConnectionState = SerialConnectionState.Connected;
            if (isRecoveryAttempt) { Log?.Record($"SerialDevice @{PortSettings.PortName} connection recovered after {RecoveryAttempts} attempt{(RecoveryAttempts == 1 ? "" : "s")} and {recoverySw.Elapsed.TotalSeconds:0.0} s."); recoveryFailureSignature = null; repeatedRecoveryFailures = 0; }
            else Trace("connected.");
            PublishSignals(true); Dispatch(Connected, nameof(Connected)); return true;
        }
        catch (Exception e)
        {
            if (isRecoveryAttempt) RecordRecoveryFailure(e); else Error($"Connect failed: {e}"); StopWorkers(); ReleaseTransport(); ConnectionState = SerialConnectionState.Unavailable; if (!isRecoveryAttempt) BeginRecoveryPeriod(); return false;
        }
    }

    public bool Disconnect()
    {
        lock (lifecycleLock)
        {
            if (disposed) return true; ConnectionRequested = false; StopConnectionMaintenance();
            if (ConnectionState == SerialConnectionState.Disconnected && port is null && !WorkersAlive) return true;
            var wasConnected = ConnectionState == SerialConnectionState.Connected; ConnectionState = SerialConnectionState.Disconnecting; Trace("disconnecting..."); if (wasConnected) Dispatch(Disconnecting, nameof(Disconnecting));
            StopWorkers(); var success = ReleaseTransport(); lastSignalState = null; ConnectionState = SerialConnectionState.Disconnected; Trace("disconnected."); return success;
        }
    }

    public bool Reset() { ThrowIfDisposed(); Trace("resetting..."); Disconnect(); Resets++; return Connect(); }
    public void WaitForIdle() { ThrowIfDisposed(); while (!Idle) Thread.Sleep(5); }

    private void StartConnectionMaintenance()
    { if (recoveryThread?.IsAlive == true) { recoverySignal.Set(); return; } recoveryActive = true; recoveryThread = StartThread(MaintainConnection, "connection_maintenance"); }
    private void StopConnectionMaintenance()
    { recoveryActive = false; recoverySignal.Set(); JoinWorker(recoveryThread); recoveryThread = null; }
    private void BeginRecoveryPeriod()
    { RecoveryAttempts = 0; RecoveryStartedAt = DateTimeOffset.Now; recoverySw.Restart(); recoveryFailureSignature = null; repeatedRecoveryFailures = 0; }

    private void MaintainConnection()
    {
        Trace("starting connection maintenance thread.");
        try
        {
            while (recoveryActive)
            {
                var delay = ConnectionState == SerialConnectionState.Unavailable ? RecoveryPolicy.RetryInterval : AvailabilityPollInterval;
                if (delay <= TimeSpan.Zero) { Error("recovery stopped: RetryInterval must be greater than zero."); recoveryActive = false; break; }
                recoverySignal.WaitOne(delay); if (!recoveryActive) break; if (!Monitor.TryEnter(lifecycleLock)) continue;
                try
                {
                    if (disposed || !ConnectionRequested) break;
                    if (ConnectionState == SerialConnectionState.Connected)
                    {
                        bool available; try { available = port?.IsAvailable == true; } catch (Exception e) { Error($"transport availability check exception: {e.Message}"); continue; }
                        if (!available) HandleTransportUnavailable(); continue;
                    }
                    if (ConnectionState != SerialConnectionState.Unavailable) continue;
                    SerialRecoveryPolicy policy;
                    try { policy = RecoveryPolicy; if (!policy.AllowsAttempt(RecoveryAttempts, recoverySw.Elapsed)) { Log?.Record($"SerialDevice @{PortSettings.PortName} automatic recovery stopped after {RecoveryAttempts} attempt{(RecoveryAttempts == 1 ? "" : "s")} and {recoverySw.Elapsed.TotalSeconds:0.0} s; connection remains requested and unavailable."); recoveryActive = false; break; } }
                    catch (Exception e) { Error($"recovery policy invalid: {e.Message}"); recoveryActive = false; break; }
                    RecoveryAttempts++; TryAcquireTransport(true);
                }
                finally { Monitor.Exit(lifecycleLock); }
            }
        }
        catch (ObjectDisposedException) when (disposed) { }
        catch (Exception e) { Error($"connection maintenance exception: {e}"); }
        finally { Trace("ending connection maintenance thread."); }
    }

    private void HandleTransportUnavailable()
    { Log?.Record($"SerialDevice @{PortSettings.PortName} transport unavailable; preserving connection request and releasing transport."); StopWorkers(); ReleaseTransport(); lastSignalState = null; ConnectionState = SerialConnectionState.Unavailable; BeginRecoveryPeriod(); Dispatch(Disconnecting, nameof(Disconnecting)); }

    private void RecordRecoveryFailure(Exception e)
    {
        var signature = $"{e.GetType().FullName}: {e.Message}";
        if (!StringComparer.Ordinal.Equals(signature, recoveryFailureSignature)) { recoveryFailureSignature = signature; repeatedRecoveryFailures = 1; Error($"recovery attempt {RecoveryAttempts} failed: {signature}"); return; }
        repeatedRecoveryFailures++; if (repeatedRecoveryFailures <= 3) Error($"recovery attempt {RecoveryAttempts} failed: {signature}"); else if (repeatedRecoveryFailures == 4) Error("recovery continues after 4 identical failures; further identical failures will be condensed."); else if (repeatedRecoveryFailures % 10 == 0) Error($"recovery still failing identically after {repeatedRecoveryFailures} attempts: {signature}");
    }

    private void CreateCommsSession()
    {
        commandQ = new(); transmitting = false; var crc = protocolSettings.CreateCrcOptions(); txCrc = crc is null ? null : new Crc(crc); ErrorBufferOverflow = ErrorCrc = false; RxCrcCode = 0; ResponseCount = 0; lastSignalState = null; txSw.Reset(); rxSw.Reset(); Drain(txSignal); while (receivedSettings.TryDequeue(out _)) { }
        receivePipeline = new SerialMessagePipeline(port!, protocolSettings.CreateMessageDecoder(), HandleDecodedMessage, TimeSpan.FromMilliseconds(Math.Max(1, MaximumMillisecondsSilenceInMessage)), e => Error($"receive exception: {e}"), e => Error($"receive consumer exception: {e}"), OnBytesAcquired, _ => receivedSettings.Enqueue(protocolSettings));
    }

    private Thread StartThread(ThreadStart action, string role) { var thread = new Thread(action) { Name = $"SerialDevice {instanceNumber} {role}", IsBackground = true }; thread.Start(); return thread; }
    private void StopWorkers()
    { active = false; txSignal.Set(); var pipeline = receivePipeline; receivePipeline = null; pipeline?.Dispose(); JoinWorker(txThread); txThread = null; transmitting = false; while (receivedSettings.TryDequeue(out _)) { } }
    private static void JoinWorker(Thread? thread) { if (thread is not null && thread != Thread.CurrentThread && thread.IsAlive) thread.Join(); }

    private bool ReleaseTransport()
    {
        var p = port; port = null; if (p is null) return true; var success = true;
        try { p.DataReceived -= RxDetected; } catch (Exception e) { success = false; Error($"DataReceived unsubscribe exception: {e}"); }
        try { p.PinChanged -= PinChanged; } catch (Exception e) { success = false; Error($"PinChanged unsubscribe exception: {e}"); }
        try { p.Close(); } catch (Exception e) { success = false; Error($"port close exception: {e}"); }
        try { p.Dispose(); } catch (Exception e) { success = false; Error($"port dispose exception: {e}"); }
        return success;
    }
    private bool WorkersAlive => (txThread?.IsAlive ?? false) || receivePipeline is not null;

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
                        if (MillisecondsBetweenBytes < 0) { if (MillisecondsSinceLastTx >= MillisecondsBetweenMessages) { Trace($"Transmit ({MillisecondsSinceLastTx:0} ms since last): {SerialDataFormatter.ToByteString(tx)}"); port!.Write(tx, 0, tx.Length); txSw.Restart(); offset = tx.Length; } else Thread.Sleep(1); }
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

    private void OnBytesAcquired(int count) { if (count <= 0) return; TotalBytesRead += (uint)count; rxSw.Restart(); Trace($"Receive {count} bytes (TotalBytesRead = {TotalBytesRead})"); }

    private void HandleDecodedMessage(DecodedMessage message)
    {
        if (!receivedSettings.TryDequeue(out var settings)) settings = protocolSettings;
        var data = ReceivedDataAdapter.Create(message, settings);
        if (settings.UseCrc)
        {
            RxCrcCode = data.CalculatedResidue ?? 0;
            if (!message.IntegrityValid) { HandleCrcError(); Error($"Receive: {SerialDataFormatter.ToEscapedText(data.WireBytes.Span)} [CRC Error]"); if (settings.SuppressCrcErrors) return; }
            if (!settings.OmitTermChar) ETXCount++;
        }
        Trace($"Receive message: {SerialDataFormatter.ToEscapedText(data.PayloadBytes.Span)}"); DispatchData(data); ResponseCount++;
    }

    private void ApplyStagedProtocolSettings(SerialProtocolSettings settings)
    {
        lock (protocolLock) { if (pendingProtocolSettings != settings) return; ApplyProtocolSettings(settings); pendingProtocolSettings = null; }
        Trace("applied staged protocol settings at message boundary.");
    }
    private void ApplyProtocolSettings(SerialProtocolSettings settings)
    { protocolSettings = settings; var crc = settings.CreateCrcOptions(); txCrc = crc is null ? null : new Crc(crc); CrcConfig = crc; IgnoreCRCErrors = !settings.SuppressCrcErrors; }

    internal ReceivedData DecodeSilenceFramedMessage(ReadOnlySpan<byte> wireBytes)
    {
        var settings = protocolSettings.UseCrc || CrcConfig is null ? protocolSettings : SerialProtocolSettings.From(CrcConfig, !IgnoreCRCErrors);
        IMessageIntegrity integrity = settings.UseCrc ? new Crc16MessageIntegrity(settings.CreateCrcOptions()!) : NoMessageIntegrity.Instance;
        var decoder = new MessageDecoder(new SilenceMessageFramer(), integrity); DecodedMessage? decoded = null; decoder.Consume(wireBytes, m => decoded = m); decoder.SilenceElapsed(m => decoded = m);
        return ReceivedDataAdapter.Create(decoded ?? throw new InvalidOperationException("Silence decoder produced no message."), settings);
    }

    private void RxDetected(object sender, SerialDataReceivedEventArgs e) { ReceiveEvents++; try { receivePipeline?.SignalDataAvailable(); } catch (ObjectDisposedException) when (!active) { } }
    private void PinChanged(object sender, SerialPinChangedEventArgs e) { if (LogSignals) Log?.Record($"SerialDevice @{PortSettings.PortName} pin changed: {e.EventType}"); PublishSignals(ring: e.EventType == SerialPinChange.Ring); }
    private void PublishSignals(bool force = false, bool ring = false)
    {
        var p = port; if (p?.IsOpen != true) return;
        try { var state = new SerialSignalState(p.RtsEnable, p.CtsHolding, p.DtrEnable, p.DsrHolding, p.CDHolding, ring); if (!force && lastSignalState == state) return; lastSignalState = state; if (LogSignals) Log?.Record($"SerialDevice @{PortSettings.PortName} signals: {state}"); var handlers = SignalsChanged; if (handlers is null) return; foreach (Action<SerialSignalState> handler in handlers.GetInvocationList()) try { handler(state); } catch (Exception e) { Error($"signal handler exception: {e}"); } }
        catch (Exception e) { Error($"signal state exception: {e.Message}"); }
    }

    private void HandleCrcError() { ErrorCrc = true; CRCErrors++; }
    private void DispatchData(ReceivedData data) { var handlers = DataReceived; if (handlers is null) return; foreach (Action<ReceivedData> handler in handlers.GetInvocationList()) try { handler(data); } catch (Exception e) { Error($"data handler exception: {e}"); } }
    private void Dispatch(EventHandler? handlers, string name) { if (handlers is null) return; foreach (EventHandler handler in handlers.GetInvocationList()) try { handler(this, EventArgs.Empty); } catch (Exception e) { Error($"{name} handler exception: {e}"); } }
    private void Trace(string message) { if (LogEverything) Log?.Record($"SerialDevice @{PortSettings.PortName} {message}"); }
    private void Error(string message) => Log?.Record($"SerialDevice @{PortSettings.PortName} ERROR: {message}");
    private static void Drain(AutoResetEvent signal) { while (signal.WaitOne(0)) { } }
    private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(SerialDevice)); }
    public void Close() => Disconnect();

    public void Dispose()
    {
        lock (lifecycleLock)
        {
            if (disposed) return; ConnectionRequested = false; StopConnectionMaintenance(); if (ConnectionState == SerialConnectionState.Connected) Dispatch(Disconnecting, nameof(Disconnecting)); ConnectionState = SerialConnectionState.Disconnecting; StopWorkers(); ReleaseTransport(); lastSignalState = null; disposed = true; ConnectionState = SerialConnectionState.Disposed; txSignal.Dispose(); recoverySignal.Dispose();
        }
    }
}
