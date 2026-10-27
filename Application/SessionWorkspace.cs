namespace SerialBench.Application;

/// <summary>Owns the current SerialBench session file and its bounded presentation projections.</summary>
public sealed class SessionWorkspace : IDisposable
{
    private SessionStore store;
    private bool disposed;

    public SessionWorkspace(string path)
    {
        Path = System.IO.Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
        store = new SessionStore(Path);
        Received = new ReceivedSessionProjection(store);
        Trace = new TraceSessionProjection(store);
        Recorder = new SessionRecorder(store);
    }

    public string Path { get; private set; }
    public SessionStore Store => store;
    public ReceivedSessionProjection Received { get; private set; }
    public TraceSessionProjection Trace { get; private set; }
    public SessionRecorder Recorder { get; private set; }

    /// <summary>Closes the current session and creates or opens the supplied session path.</summary>
    public void Open(string path)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var fullPath = System.IO.Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
        if (string.Equals(fullPath, Path, StringComparison.OrdinalIgnoreCase)) return;

        // Open first so a bad/corrupt replacement cannot destroy the usable current workspace.
        var replacement = new SessionStore(fullPath);
        store.Dispose();
        store = replacement;
        Path = fullPath;
        Received = new ReceivedSessionProjection(store);
        Trace = new TraceSessionProjection(store);
        Recorder = new SessionRecorder(store);
    }

    public static string CreateAutomaticPath(string directory, DateTime localNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        var baseName = $"serialbench-{localNow:yyyy-MM-dd_HHmmss}";
        var candidate = System.IO.Path.Combine(directory, baseName + ".sbss");
        for (var suffix = 2; File.Exists(candidate); suffix++)
            candidate = System.IO.Path.Combine(directory, $"{baseName}-{suffix}.sbss");
        return candidate;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        store.Dispose();
    }
}
