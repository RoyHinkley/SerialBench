using System.Collections.Concurrent;
using System.Text;

namespace SerialPortTerminal.Diagnostics;

/// <summary>
/// Ordered diagnostic log used to observe serial communications and transport behavior.
/// </summary>
/// <remarks>
/// Producers only timestamp and enqueue entries. File I/O and observer notification occur on a
/// dedicated worker so diagnostics do not materially perturb the serial timing being measured.
/// </remarks>
public sealed class DiagnosticLog : IDisposable
{
    private readonly BlockingCollection<string> entries = new(new ConcurrentQueue<string>());
    private readonly object fileSync = new();
    private readonly Thread worker;
    private StreamWriter? writer;
    private string? fileName;
    private bool disposed;

    public DiagnosticLog()
    {
        worker = new Thread(ProcessEntries)
        {
            IsBackground = true,
            Name = "SerialPortTerminal diagnostic log"
        };
        worker.Start();
    }

    /// <summary>
    /// Raised in log order by the diagnostic worker after an entry has been processed.
    /// Subscriber exceptions are isolated from both the logger and serial communications.
    /// </summary>
    public event Action<string>? EntryRecorded;

    /// <summary>
    /// Gets or sets the file receiving persistent diagnostic output. Null disables file output.
    /// Changing the file waits only for the file lock, never for serial producers.
    /// </summary>
    public string? FileName
    {
        get
        {
            lock (fileSync)
                return fileName;
        }
        set
        {
            lock (fileSync)
            {
                ThrowIfDisposed();
                if (string.Equals(fileName, value, StringComparison.Ordinal))
                    return;

                CloseWriter();
                fileName = string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
            }
        }
    }

    public string TimeStampFormat { get; set; } = "MM/dd/yyyy HH:mm:ss.fff\t";

    public string TimeStamp() => DateTime.Now.ToString(TimeStampFormat);

    public void Record(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Enqueue(TimeStamp() + entry + Environment.NewLine);
    }

    public void Write(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Enqueue(entry);
    }

    public void WriteLine(string entry) => Write(entry + Environment.NewLine);

    /// <summary>
    /// Stops accepting entries, drains the queue, flushes the backing file, and stops the worker.
    /// </summary>
    public void Close()
    {
        if (disposed)
            return;

        disposed = true;
        entries.CompleteAdding();
        if (Thread.CurrentThread != worker)
            worker.Join();
    }

    public void Dispose()
    {
        Close();
        entries.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Enqueue(string entry)
    {
        ThrowIfDisposed();
        entries.Add(entry);
    }

    private void ProcessEntries()
    {
        foreach (var entry in entries.GetConsumingEnumerable())
        {
            lock (fileSync)
            {
                if (fileName is not null)
                {
                    try
                    {
                        EnsureWriter();
                        writer!.Write(entry);
                        writer.Flush();
                    }
                    catch
                    {
                        // File failure must not stop diagnostics from reaching the UI. A later
                        // settings/status surface can report persistence failure without recursively logging it.
                        CloseWriter();
                    }
                }
            }

            DispatchEntry(entry);
        }

        lock (fileSync)
            CloseWriter();
    }

    private void EnsureWriter()
    {
        if (writer is not null || fileName is null)
            return;

        var directory = Path.GetDirectoryName(fileName);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        writer = new StreamWriter(fileName, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private void CloseWriter()
    {
        writer?.Dispose();
        writer = null;
    }

    private void DispatchEntry(string entry)
    {
        var handlers = EntryRecorded;
        if (handlers is null)
            return;

        foreach (Action<string> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(entry);
            }
            catch
            {
                // Diagnostic observers must never be able to interrupt serial communications.
            }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
