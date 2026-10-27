using SerialBench.Application;

namespace SerialBench.Tests;

public sealed class SessionWorkspaceTests
{
    [Fact]
    public void OpenReplacesStoreAndRebuildsProjections()
    {
        var first = TempPath();
        var second = TempPath();
        try
        {
            using var workspace = new SessionWorkspace(first);
            workspace.Recorder.RecordDiagnostic(DateTime.UtcNow, "first");
            Assert.Equal(1, workspace.Trace.Count);

            using (var seed = new SessionStore(second))
                seed.AppendDiagnostic(DateTime.UtcNow, "second");

            workspace.Open(second);
            Assert.Equal(System.IO.Path.GetFullPath(second), workspace.Path);
            Assert.Equal(1, workspace.Trace.Count);
            workspace.Trace.ShowTail(10);
            Assert.Equal("second", Assert.IsType<DiagnosticSessionRecord>(workspace.Trace.Records[0]).Text);
        }
        finally { File.Delete(first); File.Delete(second); }
    }

    [Fact]
    public void FailedOpenLeavesCurrentWorkspaceUsable()
    {
        var good = TempPath();
        var bad = TempPath();
        try
        {
            using var workspace = new SessionWorkspace(good);
            workspace.Recorder.RecordDiagnostic(DateTime.UtcNow, "before");
            File.WriteAllBytes(bad, [0x4E, 0x4F, 0x50, 0x45, 1, 0]);

            Assert.Throws<InvalidDataException>(() => workspace.Open(bad));
            Assert.Equal(System.IO.Path.GetFullPath(good), workspace.Path);
            workspace.Recorder.RecordDiagnostic(DateTime.UtcNow, "after");
            Assert.Equal(2, workspace.Trace.Count);
        }
        finally { File.Delete(good); File.Delete(bad); }
    }

    [Fact]
    public void AutomaticPathAvoidsExistingSession()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SerialBench-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var now = new DateTime(2026, 10, 26, 14, 3, 4, DateTimeKind.Local);
            var first = SessionWorkspace.CreateAutomaticPath(directory, now);
            File.WriteAllBytes(first, []);
            var second = SessionWorkspace.CreateAutomaticPath(directory, now);
            Assert.EndsWith("serialbench-2026-10-26_140304-2.sbss", second, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string TempPath() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SerialBench-{Guid.NewGuid():N}.sbss");
}
