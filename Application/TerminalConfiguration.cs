using System.ComponentModel;
using System.Globalization;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SerialCommunications.Serial;

namespace SerialBench.Application;

public enum ReceivedDataFormat
{
    Text,
    Bytes,
    [Obsolete("Text now always escapes non-printable bytes.")]
    EscapedText
}

public sealed class ConfigurationChangedEventArgs(string propertyName, object? oldValue, object? newValue) : EventArgs
{
    public string PropertyName { get; } = propertyName;
    public object? OldValue { get; } = oldValue;
    public object? NewValue { get; } = newValue;
}

public sealed class TerminalConfiguration : INotifyPropertyChanged
{
    private const string LastConfigurationFileName = "SerialBench.configuration.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private string? portName;
    private int baudRate = 115200;
    private Parity parity = Parity.None;
    private int dataBits = 8;
    private StopBits stopBits = StopBits.One;
    private Handshake handshake = Handshake.None;
    private SerialDevice.RtsModes rtsMode = SerialDevice.RtsModes.Enabled;
    private int millisecondsBetweenMessages = -1;
    private int millisecondsBetweenBytes = -1;
    private int maximumMillisecondsSilenceInMessage = 5;
    private bool useCrc = true;
    private string crcPolynomial = "DAAE";
    private string crcInitialValue = "FFFF";
    private string crcExpectedResidue = "82C0";
    private string termChar = "03";
    private bool crcPostInvert = true;
    private bool crcMsBitFirst;
    private bool crcMsByteFirst;
    private bool omitTermChar;
    private bool suppressCrcErrors;
    private bool includeDetailedTransportEvents;
    private bool logSignals;
    private ReceivedDataFormat receivedDataFormat;
    private bool showCrcBytes;
    private bool diagnosticWordWrap;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<ConfigurationChangedEventArgs>? Changed;

    public string? PortName { get => portName; set => Set(ref portName, value); }
    public int BaudRate { get => baudRate; set => Set(ref baudRate, value); }
    public Parity Parity { get => parity; set => Set(ref parity, value); }
    public int DataBits { get => dataBits; set => Set(ref dataBits, value); }
    public StopBits StopBits { get => stopBits; set => Set(ref stopBits, value); }
    public Handshake Handshake { get => handshake; set => Set(ref handshake, value); }
    public SerialDevice.RtsModes RtsMode { get => rtsMode; set => Set(ref rtsMode, value); }
    public int MillisecondsBetweenMessages { get => millisecondsBetweenMessages; set => Set(ref millisecondsBetweenMessages, Math.Max(-1, value)); }
    public int MillisecondsBetweenBytes { get => millisecondsBetweenBytes; set => Set(ref millisecondsBetweenBytes, Math.Max(-1, value)); }
    public int MaximumMillisecondsSilenceInMessage { get => maximumMillisecondsSilenceInMessage; set => Set(ref maximumMillisecondsSilenceInMessage, Math.Max(0, value)); }
    public bool UseCrc { get => useCrc; set => Set(ref useCrc, value); }
    public string CrcPolynomial { get => crcPolynomial; set => Set(ref crcPolynomial, value); }
    public string CrcInitialValue { get => crcInitialValue; set => Set(ref crcInitialValue, value); }
    public string CrcExpectedResidue { get => crcExpectedResidue; set => Set(ref crcExpectedResidue, value); }
    public string TermChar { get => termChar; set => Set(ref termChar, value); }
    public bool CrcPostInvert { get => crcPostInvert; set => Set(ref crcPostInvert, value); }
    public bool CrcMsBitFirst { get => crcMsBitFirst; set => Set(ref crcMsBitFirst, value); }
    public bool CrcMsByteFirst { get => crcMsByteFirst; set => Set(ref crcMsByteFirst, value); }
    public bool OmitTermChar { get => omitTermChar; set => Set(ref omitTermChar, value); }
    public bool SuppressCrcErrors { get => suppressCrcErrors; set => Set(ref suppressCrcErrors, value); }
    public bool IncludeDetailedTransportEvents { get => includeDetailedTransportEvents; set => Set(ref includeDetailedTransportEvents, value); }
    public bool LogSignals { get => logSignals; set => Set(ref logSignals, value); }
    public ReceivedDataFormat ReceivedDataFormat { get => receivedDataFormat; set => Set(ref receivedDataFormat, value); }
    public bool ShowCrcBytes { get => showCrcBytes; set => Set(ref showCrcBytes, value); }
    public bool DiagnosticWordWrap { get => diagnosticWordWrap; set => Set(ref diagnosticWordWrap, value); }

    [JsonIgnore] public static string LastConfigurationPath => Path.Combine(AppContext.BaseDirectory, LastConfigurationFileName);

    public static TerminalConfiguration LoadLast()
    {
        try
        {
            if (!File.Exists(LastConfigurationPath)) return new TerminalConfiguration();
            var loaded = JsonSerializer.Deserialize<TerminalConfiguration>(File.ReadAllText(LastConfigurationPath), JsonOptions) ?? new TerminalConfiguration();
#pragma warning disable CS0618
            if (loaded.ReceivedDataFormat == ReceivedDataFormat.EscapedText) loaded.receivedDataFormat = ReceivedDataFormat.Text;
#pragma warning restore CS0618
            return loaded;
        }
        catch { return new TerminalConfiguration(); }
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    public void SaveLast() => Save(LastConfigurationPath);

    public string Describe()
    {
        var connection = $"{PortName ?? "(no port)"} {BaudRate} {DataBits}{ParityAbbreviation(Parity)}{StopBitsAbbreviation(StopBits)}, handshake={Handshake}, RTS={RtsMode}" +
            $", messagePacing={MillisecondsBetweenMessages}ms bytePacing={MillisecondsBetweenBytes}ms receiveSilence={MaximumMillisecondsSilenceInMessage}ms";
        if (!UseCrc) return connection + ", CRC=off";
        return connection + $", CRC=on poly={CrcPolynomial} initial={CrcInitialValue} residue={CrcExpectedResidue}" +
            $" term={TermChar} postInvert={CrcPostInvert} msBitFirst={CrcMsBitFirst} msByteFirst={CrcMsByteFirst}" +
            $" omitTermChar={OmitTermChar} suppressCrcErrors={SuppressCrcErrors}";
    }

    public SerialPortSettings CreatePortSettings()
    {
        if (string.IsNullOrWhiteSpace(PortName)) throw new InvalidOperationException("No serial port is selected.");
        return new SerialPortSettings(PortName, BaudRate, Parity, DataBits, StopBits, Handshake);
    }

    public bool TryCreateCrcOptions(out CrcOptions? options, out string? error)
    {
        options = null; error = null;
        if (!UseCrc) return true;
        if (!TryHex16(CrcPolynomial, "CRC polynomial", out var polynomial, out error) || !TryHex16(CrcInitialValue, "CRC initial value", out var initial, out error) || !TryHex16(CrcExpectedResidue, "CRC expected residue", out var residue, out error) || !TryHex8(TermChar, "termination character", out var term, out error)) return false;
        options = new CrcOptions(initial, polynomial, residue, CrcPostInvert, CrcMsBitFirst, CrcMsByteFirst, term, OmitTermChar); return true;
    }

    /// <summary>Builds one immutable protocol snapshot so transport code never observes a partially edited configuration.</summary>
    public bool TryCreateProtocolSettings(out SerialProtocolSettings? settings, out string? error)
    {
        settings = null;
        if (!TryCreateCrcOptions(out var crc, out error)) return false;
        settings = SerialProtocolSettings.From(crc, SuppressCrcErrors);
        return true;
    }

    private static string ParityAbbreviation(Parity value) => value switch { Parity.None => "N", Parity.Odd => "O", Parity.Even => "E", Parity.Mark => "M", Parity.Space => "S", _ => value.ToString() };
    private static string StopBitsAbbreviation(StopBits value) => value switch { StopBits.One => "1", StopBits.OnePointFive => "1.5", StopBits.Two => "2", _ => value.ToString() };
    private static bool TryHex16(string text, string name, out ushort value, out string? error) { if (ushort.TryParse(NormalizeHex(text), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)) { error = null; return true; } error = $"Invalid {name}: '{text}'. Enter 1-4 hexadecimal digits."; return false; }
    private static bool TryHex8(string text, string name, out byte value, out string? error) { if (byte.TryParse(NormalizeHex(text), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)) { error = null; return true; } error = $"Invalid {name}: '{text}'. Enter 1-2 hexadecimal digits."; return false; }
    private static string NormalizeHex(string? text) { var value = text?.Trim() ?? string.Empty; return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value; }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; var oldValue = field; field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); Changed?.Invoke(this, new ConfigurationChangedEventArgs(propertyName!, oldValue, value)); }
}
