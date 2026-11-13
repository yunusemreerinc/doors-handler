using System.Runtime.InteropServices;
using DoorsHandler.Core;

namespace DoorsHandler.Core.Tests;

public class DoorsErrorClassifierTests
{
    [Theory]
    [InlineData(0x80040154u, true, DoorsErrorKind.NotInstalled)]   // REGDB_E_CLASSNOTREG
    [InlineData(0x80080005u, true, DoorsErrorKind.StartFailed)]    // CO_E_SERVER_EXEC_FAILURE
    [InlineData(0x80070005u, true, DoorsErrorKind.AccessDenied)]   // E_ACCESSDENIED
    [InlineData(0x80070005u, false, DoorsErrorKind.AccessDenied)]
    [InlineData(0x80010001u, false, DoorsErrorKind.Busy)]          // RPC_E_CALL_REJECTED
    [InlineData(0x8001010Au, true, DoorsErrorKind.Busy)]           // RPC_E_SERVERCALL_RETRYLATER
    [InlineData(0x800706BAu, false, DoorsErrorKind.ConnectionLost)] // RPC_S_SERVER_UNAVAILABLE
    [InlineData(0x800706BAu, true, DoorsErrorKind.StartFailed)]    // ...while still starting up
    [InlineData(0x80010108u, false, DoorsErrorKind.ConnectionLost)] // RPC_E_DISCONNECTED
    [InlineData(0x80020009u, false, DoorsErrorKind.ScriptFailed)]  // DISP_E_EXCEPTION from runStr
    [InlineData(0x80004005u, true, DoorsErrorKind.StartFailed)]    // E_FAIL while starting
    public void FromHResult_MapsToExpectedKind(uint hresult, bool duringStartup, DoorsErrorKind expected)
    {
        Assert.Equal(expected, DoorsErrorClassifier.FromHResult(unchecked((int)hresult), duringStartup));
    }

    [Fact]
    public void Explain_GivesReasonAndSuggestionForEveryKind()
    {
        foreach (var kind in Enum.GetValues<DoorsErrorKind>())
        {
            var (reason, suggestion) = DoorsErrorReport.Explain(kind);
            Assert.False(string.IsNullOrWhiteSpace(reason), $"{kind} has no reason");
            Assert.False(string.IsNullOrWhiteSpace(suggestion), $"{kind} has no suggestion");
        }
    }
}

public class DoorsErrorReportTests
{
    private static readonly DateTime When = new(2026, 11, 12, 14, 30, 5);

    [Fact]
    public void Create_ForComFailure_ExplainsReasonAndKeepsTechnicalDetails()
    {
        var com = new COMException("Class not registered", unchecked((int)0x80040154));
        var ex = new DoorsException(DoorsErrorKind.NotInstalled, "'DOORS.Application' is not registered.", com);

        var report = DoorsErrorReport.Create("Could not connect to DOORS", ex,
            [new("Connection type", "IBM DOORS (local client)")], When);

        Assert.Equal("Could not connect to DOORS", report.Title);
        Assert.Equal(DoorsErrorKind.NotInstalled, report.Kind);
        Assert.Contains("DOORS client was not found", report.Reason);
        Assert.Contains("Demo data", report.Suggestion);
        Assert.Contains("2026-11-12 14:30:05", report.Details);
        Assert.Contains("Error kind: NotInstalled", report.Details);
        Assert.Contains("Connection type: IBM DOORS (local client)", report.Details);
        Assert.Contains("HRESULT 0x80040154", report.Details);
        Assert.Contains("Class not registered", report.Details);
    }

    [Fact]
    public void Create_ForRootNotFound_UsesSpecificMessageAsReason()
    {
        var ex = new DoorsException(DoorsErrorKind.RootNotFound, "Folder or project '/Missing' was not found in DOORS.");

        var report = DoorsErrorReport.Create("Could not list modules", ex, occurredAt: When);

        Assert.Contains("/Missing", report.Reason);
    }

    [Fact]
    public void Create_ForUnexpectedException_IncludesItsMessage()
    {
        var report = DoorsErrorReport.Create("Could not connect to DOORS", new InvalidCastException("boom"), occurredAt: When);

        Assert.Equal(DoorsErrorKind.Unknown, report.Kind);
        Assert.Contains("boom", report.Reason);
        Assert.Contains("InvalidCastException", report.Details);
    }

    [Fact]
    public void ToClipboardText_ContainsAllSections()
    {
        var report = DoorsErrorReport.Create("Title",
            new DoorsException(DoorsErrorKind.Busy, "busy"), occurredAt: When);

        var text = report.ToClipboardText();

        Assert.StartsWith("Title", text);
        Assert.Contains("Reason: " + report.Reason, text);
        Assert.Contains("What you can do: " + report.Suggestion, text);
        Assert.Contains(report.Details, text);
    }
}

public class DxlDoorsClientErrorTests
{
    private sealed class HangingExecutor : IDxlExecutor
    {
        public Task<string> ExecuteAsync(string dxl, CancellationToken cancellationToken = default) =>
            new TaskCompletionSource<string>().Task; // never completes, like a DOORS login window left open

        public void Dispose() { }
    }

    private sealed class ThrowingExecutor(Exception exception) : IDxlExecutor
    {
        public Task<string> ExecuteAsync(string dxl, CancellationToken cancellationToken = default) =>
            Task.FromException<string>(exception);

        public void Dispose() { }
    }

    [Fact]
    public async Task Connect_WhenDoorsDoesNotAnswer_FailsWithTimeout()
    {
        using var client = new DxlDoorsClient(new HangingExecutor(), TimeSpan.FromMilliseconds(50));

        var ex = await Assert.ThrowsAsync<DoorsException>(() => client.ConnectAsync());

        Assert.Equal(DoorsErrorKind.Timeout, ex.Kind);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Connect_CanBeCancelledWhileWaiting()
    {
        using var client = new DxlDoorsClient(new HangingExecutor());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(cts.Token));
    }

    [Fact]
    public async Task Connect_PassesExecutorErrorsThrough()
    {
        var failure = new DoorsException(DoorsErrorKind.AccessDenied, "denied");
        using var client = new DxlDoorsClient(new ThrowingExecutor(failure));

        var ex = await Assert.ThrowsAsync<DoorsException>(() => client.ConnectAsync());

        Assert.Same(failure, ex);
    }

    [Fact]
    public async Task Connect_WithEmptyResult_ReportsScriptFailure()
    {
        using var client = new DxlDoorsClient(new EmptyExecutor());

        var ex = await Assert.ThrowsAsync<DoorsException>(() => client.ConnectAsync());

        Assert.Equal(DoorsErrorKind.ScriptFailed, ex.Kind);
    }

    [Fact]
    public async Task List_WhenConnectionIsLost_MarksClientDisconnected()
    {
        var executor = new SequenceExecutor(
            () => Task.FromResult("OK\nS\tu\tdb\t9.7\n"),
            () => Task.FromException<string>(new DoorsException(DoorsErrorKind.ConnectionLost, "gone")));
        using var client = new DxlDoorsClient(executor);
        await client.ConnectAsync();

        var ex = await Assert.ThrowsAsync<DoorsException>(() => client.ListItemsAsync("/", true));

        Assert.Equal(DoorsErrorKind.ConnectionLost, ex.Kind);
        Assert.False(client.IsConnected);
    }

    private sealed class SequenceExecutor(params Func<Task<string>>[] steps) : IDxlExecutor
    {
        private readonly Queue<Func<Task<string>>> _steps = new(steps);

        public Task<string> ExecuteAsync(string dxl, CancellationToken cancellationToken = default) =>
            _steps.Dequeue()();

        public void Dispose() { }
    }

    private sealed class EmptyExecutor : IDxlExecutor
    {
        public Task<string> ExecuteAsync(string dxl, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty); // what runStr returns when DOORS rejected the DXL

        public void Dispose() { }
    }
}
