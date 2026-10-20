using System.ComponentModel;
using System.IO.Ports;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using SerialBench.Application;
using SerialBench.Diagnostics;
using SerialCommunications.Serial;

namespace SerialBench;

public sealed partial class MainWindow : Window
{
    private const string ApplicationTitle = "Serial Port Terminal";
    private readonly AppSettings settings = AppSettings.Load();
    private readonly TerminalConfiguration configuration = TerminalConfiguration.LoadLast();
    private readonly DiagnosticLog log = new();
    private readonly TerminalSession session = new();
    private readonly List<string> commandHistory = [];
    private readonly List<ReceivedData> receivedData = [];
    private readonly object receivedDataLock = new();
    private readonly DateTime sessionStarted = DateTime.Now;
    private int commandHistoryIndex;

    public MainWindow()
    {
        DataContext = configuration; InitializeComponent();
        BaudBox.ItemsSource = new[] { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };
        ParityBox.ItemsSource = Enum.GetValues<Parity>(); DataBitsBox.ItemsSource = new[] { 5, 6, 7, 8 }; StopBitsBox.ItemsSource = new[] { StopBits.One, StopBits.OnePointFive, StopBits.Two };
        HandshakeBox.ItemsSource = Enum.GetValues<Handshake>(); RtsBox.ItemsSource = Enum.GetValues<SerialChannel.RtsModes>(); ReceivedFormatBox.ItemsSource = new[] { ReceivedDataFormat.Text, ReceivedDataFormat.Bytes };
        configuration.PropertyChanged += Configuration_PropertyChanged; configuration.Changed += Configuration_Changed; log.EntryRecorded += AppendDiagnostic;
        session.Log = log; session.DataReceived += ReceivedDataReceived; session.Connected += (_, _) => PostConnectionState(); session.Disconnecting += (_, _) => PostConnectionState(false);
        RefreshPorts(); ApplyDiagnosticWordWrap(); UpdateConnectionState(false); UpdateSignalDisplay(null); UpdateLogFileDisplay(); Closed += MainWindow_Closed;
    }
    private void RefreshPorts_Click(object? sender, RoutedEventArgs eventArgs) => RefreshPorts();
    private void RefreshPorts() { var ports = SerialPort.GetPortNames().OrderBy(PortSortKey).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray(); PortBox.ItemsSource = ports; if (string.IsNullOrWhiteSpace(configuration.PortName)) configuration.PortName = ports.FirstOrDefault(); }
    private static int PortSortKey(string p) => p.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(p.AsSpan(3), out var n) ? n : int.MaxValue;
    private void Connect_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (session.Ready) { session.Disconnect(); UpdateConnectionState(false); UpdateSignalDisplay(null); return; }
        if (!configuration.TryCreateChannelConfiguration(out var channelConfiguration, out var configurationError)) { AppendDiagnostic(configurationError!); return; }
        EnsureSessionLog(); log.Record($"Connection configuration: {configuration.Describe()}"); session.SerialChannel?.Dispose();
        var channel = new SerialChannel(channelConfiguration!);
        channel.SignalsChanged += SignalsChanged; session.SerialChannel = channel;
        session.LogEverything = configuration.IncludeDetailedTransportEvents; session.LogNoisy = configuration.LogSignals;
        if (!session.Connect()) log.Record($"Unable to connect to {channelConfiguration!.PortSettings.PortName}."); UpdateConnectionState(session.Ready); if (!session.Ready) UpdateSignalDisplay(null);
    }
    private void EnsureSessionLog()
    {
        if (log.FileName is not null) return; var directory = settings.ResolvedLogFolder; Directory.CreateDirectory(directory); log.FileName = Path.Combine(directory, $"{settings.LogFilePrefix}{sessionStarted:yyyy-MM-dd_HHmmss}.log");
        ApplyLogRetention(directory); UpdateLogFileDisplay(); log.Record($"Diagnostic session started. Log: {log.FileName}"); log.Record($"Initial configuration: {configuration.Describe()}");
    }
    private void ApplyLogRetention(string directory)
    {
        var current = log.FileName; var logs = Directory.EnumerateFiles(directory, settings.LogFilePrefix + "*.log", SearchOption.TopDirectoryOnly).Where(p => !string.Equals(Path.GetFullPath(p), current, StringComparison.OrdinalIgnoreCase)).OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var oldLog in logs.Skip(Math.Max(0, settings.LogFilesToKeep - 1))) try { File.Delete(oldLog); } catch { }
    }
    private void UpdateLogFileDisplay() { if (LogFileBox is not null) LogFileBox.Text = log.FileName is null ? "(starts on first connection attempt)" : Path.GetFileName(log.FileName); }
    private void Send_Click(object? sender, RoutedEventArgs e) => SendCommand();
    private void CommandBox_KeyDown(object? sender, KeyEventArgs e) { switch (e.Key) { case Key.Enter: e.Handled = true; SendCommand(); break; case Key.Up: e.Handled = RecallCommand(-1); break; case Key.Down: e.Handled = RecallCommand(1); break; } }
    private bool RecallCommand(int direction) { if (commandHistory.Count == 0) return false; commandHistoryIndex = Math.Clamp(commandHistoryIndex + direction, 0, commandHistory.Count); CommandBox.Text = commandHistoryIndex == commandHistory.Count ? string.Empty : commandHistory[commandHistoryIndex]; CommandBox.CaretIndex = CommandBox.Text?.Length ?? 0; return true; }
    private void SendCommand()
    {
        var expression = CommandBox.Text ?? string.Empty; if (expression.Length == 0) return;
        if (!SendDataParser.TryParse(expression, out var bytes, out var parseError)) { EnsureSessionLog(); log.Record($"Send rejected: {parseError}"); return; }
        if (!session.Ready) { EnsureSessionLog(); log.Record($"Send rejected: no device connected. Expression: \"{expression}\""); return; }
        if (session.Send(bytes)) { commandHistory.Add(expression); commandHistoryIndex = commandHistory.Count; CommandBox.SelectAll(); }
    }
    private void ReceivedDataReceived(ReceivedData data) { lock (receivedDataLock) receivedData.Add(data); AppendReceived(FormatReceivedData(data)); }

    private string FormatReceivedData(ReceivedData data)
    {
        var bytesMode = configuration.ReceivedDataFormat == ReceivedDataFormat.Bytes;
        var payload = bytesMode ? BinaryDataFormatter.ToHexBytes(data.PayloadBytes.Span) : BinaryDataFormatter.ToEscapedText(data.PayloadBytes.Span);
        var result = payload;
        if (configuration.ShowCrcBytes && !data.CrcBytes.IsEmpty)
        {
            var crc = bytesMode ? BinaryDataFormatter.ToHexBytes(data.CrcBytes.Span) : string.Concat(data.CrcBytes.Span.ToArray().Select(b => $"\\x{b:X2}"));
            result += (result.Length == 0 ? string.Empty : " ") + crc;
        }
        if (data.CrcValid == false)
        {
            var received = data.ReceivedCrc is ushort r ? $"0x{r:X4}" : "unknown";
            var residue = data.CalculatedResidue is ushort c ? $"0x{c:X4}" : "unknown";
            var expected = data.ExpectedResidue is ushort e ? $"0x{e:X4}" : "unknown";
            result += $"  [CRC ERROR: received {received}, residue {residue}, expected {expected}]";
        }
        return result;
    }

    private void RerenderReceivedData() { ReceivedData[] snapshot; lock (receivedDataLock) snapshot = receivedData.ToArray(); var rendered = string.Join(Environment.NewLine, snapshot.Select(FormatReceivedData)); Dispatcher.UIThread.Post(() => { ReceivedBox.Text = rendered; ReceivedBox.CaretIndex = ReceivedBox.Text?.Length ?? 0; }); }
    private void SignalsChanged(SerialSignalState state) => Dispatcher.UIThread.Post(() => UpdateSignalDisplay(state));
    private void UpdateSignalDisplay(SerialSignalState? state)
    {
        var unknown = new SolidColorBrush(Color.Parse("#707070"));
        var inactive = new SolidColorBrush(Color.Parse("#303030"));
        var active = new SolidColorBrush(Color.Parse("#36B24A"));
        static void SetLed(Ellipse led, bool value, IBrush activeBrush, IBrush inactiveBrush) => led.Fill = value ? activeBrush : inactiveBrush;
        if (state is not { } s)
        {
            RtsSignal.Fill = CtsSignal.Fill = DtrSignal.Fill = DsrSignal.Fill = DcdSignal.Fill = RiSignal.Fill = unknown;
            return;
        }
        SetLed(RtsSignal, s.Rts, active, inactive); SetLed(CtsSignal, s.Cts, active, inactive); SetLed(DtrSignal, s.Dtr, active, inactive);
        SetLed(DsrSignal, s.Dsr, active, inactive); SetLed(DcdSignal, s.Dcd, active, inactive); SetLed(RiSignal, s.Ring, active, inactive);
    }
    private void Configuration_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalConfiguration.DiagnosticWordWrap)) Dispatcher.UIThread.Post(ApplyDiagnosticWordWrap);
        else if (e.PropertyName == nameof(TerminalConfiguration.IncludeDetailedTransportEvents)) session.LogEverything = configuration.IncludeDetailedTransportEvents;
        else if (e.PropertyName == nameof(TerminalConfiguration.LogSignals)) session.LogNoisy = configuration.LogSignals;
        else if (IsTimingProperty(e.PropertyName)) ApplyTimingConfiguration();
        else if (IsProtocolProperty(e.PropertyName)) ApplyProtocolConfiguration();
        else if (e.PropertyName is nameof(TerminalConfiguration.ReceivedDataFormat) or nameof(TerminalConfiguration.ShowCrcBytes)) RerenderReceivedData();
    }

    private static bool IsTimingProperty(string? propertyName) => propertyName is
        nameof(TerminalConfiguration.MillisecondsBetweenMessages) or nameof(TerminalConfiguration.MillisecondsBetweenBytes) or
        nameof(TerminalConfiguration.MaximumMillisecondsSilenceInMessage);

    private void ApplyTimingConfiguration()
    {
        if (session.SerialChannel is not { } channel) return;
        channel.MillisecondsBetweenMessages = configuration.MillisecondsBetweenMessages;
        channel.MillisecondsBetweenBytes = configuration.MillisecondsBetweenBytes;
        channel.MaximumMillisecondsSilenceInMessage = configuration.MaximumMillisecondsSilenceInMessage;
    }

    private static bool IsProtocolProperty(string? propertyName) => propertyName is
        nameof(TerminalConfiguration.UseCrc) or nameof(TerminalConfiguration.CrcPolynomial) or
        nameof(TerminalConfiguration.CrcInitialValue) or nameof(TerminalConfiguration.CrcExpectedResidue) or
        nameof(TerminalConfiguration.TermChar) or nameof(TerminalConfiguration.CrcPostInvert) or
        nameof(TerminalConfiguration.CrcMsBitFirst) or nameof(TerminalConfiguration.CrcMsByteFirst) or
        nameof(TerminalConfiguration.OmitTermChar) or nameof(TerminalConfiguration.SuppressCrcErrors);

    private void ApplyProtocolConfiguration()
    {
        if (session.SerialChannel is not { } channel || !session.Ready) return;
        if (!configuration.TryCreateProtocolSettings(out var protocol, out var error))
        {
            log.Record($"Protocol configuration not applied: {error}");
            return;
        }
        if (!channel.StageProtocolSettings(protocol!))
            log.Record("Protocol configuration requires reconnect because the receive framing strategy changed.");
    }

    private void Configuration_Changed(object? sender, ConfigurationChangedEventArgs e) { if (log.FileName is not null) log.Record($"Configuration changed: {e.PropertyName}: {FormatConfigurationValue(e.OldValue)} -> {FormatConfigurationValue(e.NewValue)}"); }
    private static string FormatConfigurationValue(object? value) => value switch { null => "(null)", string text => $"\"{text}\"", bool b => b ? "true" : "false", _ => value.ToString() ?? "(null)" };
    private void ApplyDiagnosticWordWrap() { DiagnosticsBox.TextWrapping = configuration.DiagnosticWordWrap ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap; DiagnosticsBox.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, configuration.DiagnosticWordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto); }
    private void ClearReceived_Click(object? sender, RoutedEventArgs e) { lock (receivedDataLock) receivedData.Clear(); ReceivedBox.Clear(); }
    private void ClearDiagnostics_Click(object? sender, RoutedEventArgs e) => DiagnosticsBox.Clear();
    private void AppendReceived(string entry) => AppendText(ReceivedBox, entry, true); private void AppendDiagnostic(string entry) => AppendText(DiagnosticsBox, entry, false);
    private static void AppendText(TextBox box, string entry, bool separateEntry) { if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => AppendText(box, entry, separateEntry)); return; } var prefix = separateEntry && !string.IsNullOrEmpty(box.Text) ? Environment.NewLine : string.Empty; box.Text += prefix + entry; box.CaretIndex = box.Text?.Length ?? 0; }
    private void PostConnectionState(bool? connected = null) => Dispatcher.UIThread.Post(() => UpdateConnectionState(connected ?? session.Ready));
    private void UpdateConnectionState(bool connected)
    {
        var portName = session.SerialChannel?.PortSettings.PortName;
        ConnectionStatus.Text = connected ? $"Connected: {portName}" : "Disconnected";
        Title = connected && !string.IsNullOrWhiteSpace(portName) ? $"{portName} - {ApplicationTitle}" : ApplicationTitle;
        ConnectButton.Content = connected ? "Disconnect" : "Connect";
        SendButton.IsEnabled = connected;
        PortBox.IsEnabled = !connected; BaudBox.IsEnabled = !connected; ParityBox.IsEnabled = !connected; DataBitsBox.IsEnabled = !connected; StopBitsBox.IsEnabled = !connected; HandshakeBox.IsEnabled = !connected; RtsBox.IsEnabled = !connected; RefreshPortsButton.IsEnabled = !connected;
    }
    private void MainWindow_Closed(object? sender, EventArgs e) { configuration.PropertyChanged -= Configuration_PropertyChanged; configuration.Changed -= Configuration_Changed; try { configuration.SaveLast(); } catch { } log.EntryRecorded -= AppendDiagnostic; session.Dispose(); log.Dispose(); }
}
