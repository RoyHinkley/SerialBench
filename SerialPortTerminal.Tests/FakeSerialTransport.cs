using System.IO.Ports;
using SerialPortTerminal.Serial;

namespace SerialPortTerminal.Tests;

internal sealed class FakeSerialTransportFactory : ISerialTransportFactory
{
    private readonly Queue<FakeSerialTransport> transports = new();

    public List<FakeSerialTransport> Created { get; } = [];

    public FakeSerialTransport Enqueue()
    {
        var transport = new FakeSerialTransport();
        transports.Enqueue(transport);
        return transport;
    }

    public ISerialTransport Create(SerialPortSettings settings, SerialDevice.RtsModes rtsMode)
    {
        var transport = transports.Count > 0 ? transports.Dequeue() : new FakeSerialTransport();
        transport.Settings = settings;
        transport.RtsMode = rtsMode;
        Created.Add(transport);
        return transport;
    }
}

internal sealed class FakeSerialTransport : ISerialTransport
{
    private SerialDataReceivedEventHandler? dataReceived;
    private SerialPinChangedEventHandler? pinChanged;

    public SerialPortSettings? Settings { get; set; }
    public SerialDevice.RtsModes RtsMode { get; set; }
    public Exception? OpenException { get; set; }
    public Exception? DiscardOutException { get; set; }
    public Exception? DiscardInException { get; set; }
    public Exception? ReadException { get; set; }
    public Exception? WriteException { get; set; }
    public Exception? CloseException { get; set; }
    public Exception? DisposeException { get; set; }

    public int OpenCalls { get; private set; }
    public int ReadCalls { get; private set; }
    public int WriteCalls { get; private set; }
    public int CloseCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public int DataReceivedSubscriptions { get; private set; }
    public int PinChangedSubscriptions { get; private set; }
    public bool IsOpen { get; private set; }
    public bool RtsEnable { get; set; }
    public bool DtrEnable { get; set; }
    public bool CtsHolding { get; set; }
    public bool DsrHolding { get; set; }
    public bool CDHolding { get; set; }

    public event SerialDataReceivedEventHandler? DataReceived
    {
        add { dataReceived += value; DataReceivedSubscriptions++; }
        remove { dataReceived -= value; DataReceivedSubscriptions--; }
    }

    public event SerialPinChangedEventHandler? PinChanged
    {
        add { pinChanged += value; PinChangedSubscriptions++; }
        remove { pinChanged -= value; PinChangedSubscriptions--; }
    }

    public void Open()
    {
        OpenCalls++;
        if (OpenException is not null) throw OpenException;
        IsOpen = true;
    }

    public void Close()
    {
        CloseCalls++;
        IsOpen = false;
        if (CloseException is not null) throw CloseException;
    }

    public void DiscardOutBuffer()
    {
        if (DiscardOutException is not null) throw DiscardOutException;
    }

    public void DiscardInBuffer()
    {
        if (DiscardInException is not null) throw DiscardInException;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        ReadCalls++;
        if (ReadException is not null) throw ReadException;
        return 0;
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        WriteCalls++;
        if (WriteException is not null) throw WriteException;
    }

    public void Dispose()
    {
        DisposeCalls++;
        IsOpen = false;
        if (DisposeException is not null) throw DisposeException;
    }
}