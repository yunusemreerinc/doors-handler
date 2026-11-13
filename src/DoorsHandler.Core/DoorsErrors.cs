using System.Globalization;
using System.Text;

namespace DoorsHandler.Core;

/// <summary>Categories of DOORS failures, each with its own explanation for the user.</summary>
public enum DoorsErrorKind
{
    Unknown = 0,

    /// <summary>The DOORS COM server is not registered: the DOORS client is not installed on this machine.</summary>
    NotInstalled,

    /// <summary>Windows found DOORS but could not start it or attach to it.</summary>
    StartFailed,

    /// <summary>Windows refused access to the DOORS process (usually an administrator/normal user mismatch).</summary>
    AccessDenied,

    /// <summary>DOORS is running but is blocked by an open dialog or another script.</summary>
    Busy,

    /// <summary>DOORS did not answer in time (often because its login window is waiting).</summary>
    Timeout,

    /// <summary>DOORS was closed or crashed after we attached to it.</summary>
    ConnectionLost,

    /// <summary>The DXL script did not run or returned an unexpected result.</summary>
    ScriptFailed,

    /// <summary>The requested folder/project does not exist or is not visible to the user.</summary>
    RootNotFound,

    /// <summary>A module could not be opened (missing, no read access, or locked).</summary>
    ModuleNotAccessible,
}

/// <summary>Maps COM HRESULTs returned by the DOORS automation server to <see cref="DoorsErrorKind"/>.</summary>
public static class DoorsErrorClassifier
{
    public const int RegDbClassNotRegistered = unchecked((int)0x80040154); // REGDB_E_CLASSNOTREG
    public const int ServerExecFailure = unchecked((int)0x80080005);       // CO_E_SERVER_EXEC_FAILURE
    public const int FileNotFound = unchecked((int)0x80070002);            // ERROR_FILE_NOT_FOUND
    public const int AccessDenied = unchecked((int)0x80070005);            // E_ACCESSDENIED
    public const int CallRejected = unchecked((int)0x80010001);            // RPC_E_CALL_REJECTED
    public const int ServerCallRetryLater = unchecked((int)0x8001010A);    // RPC_E_SERVERCALL_RETRYLATER
    public const int ServerUnavailable = unchecked((int)0x800706BA);       // RPC_S_SERVER_UNAVAILABLE
    public const int CallFailed = unchecked((int)0x800706BE);              // RPC_S_CALL_FAILED
    public const int Disconnected = unchecked((int)0x80010108);            // RPC_E_DISCONNECTED
    public const int ServerDied = unchecked((int)0x80010114);              // RPC_E_SERVER_DIED_DNE

    /// <param name="hresult">The HRESULT of the COM exception.</param>
    /// <param name="duringStartup">True while creating the DOORS COM object, false for calls on an existing one.</param>
    public static DoorsErrorKind FromHResult(int hresult, bool duringStartup) => hresult switch
    {
        RegDbClassNotRegistered => DoorsErrorKind.NotInstalled,
        ServerExecFailure or FileNotFound => DoorsErrorKind.StartFailed,
        AccessDenied => DoorsErrorKind.AccessDenied,
        CallRejected or ServerCallRetryLater => DoorsErrorKind.Busy,
        ServerUnavailable or CallFailed or Disconnected or ServerDied =>
            duringStartup ? DoorsErrorKind.StartFailed : DoorsErrorKind.ConnectionLost,
        _ => duringStartup ? DoorsErrorKind.StartFailed : DoorsErrorKind.ScriptFailed,
    };
}

/// <summary>What the user is told when an operation fails: what happened, why, and what to do.</summary>
public sealed record DoorsErrorReport(
    string Title,
    DoorsErrorKind Kind,
    string Reason,
    string Suggestion,
    string Details,
    DateTime OccurredAt)
{
    /// <summary>Plain-text version for the clipboard, e.g. to send to IT support.</summary>
    public string ToClipboardText() =>
        $"{Title}\r\nReason: {Reason}\r\nWhat you can do: {Suggestion}\r\n\r\nTechnical details:\r\n{Details}";

    /// <summary>Builds a report for an exception thrown while performing <paramref name="title"/>.</summary>
    /// <param name="title">What failed, e.g. "Could not connect to DOORS".</param>
    /// <param name="exception">The failure.</param>
    /// <param name="context">Extra "name: value" lines for the technical details (mode, root path, ...).</param>
    public static DoorsErrorReport Create(
        string title,
        Exception exception,
        IEnumerable<KeyValuePair<string, string>>? context = null,
        DateTime? occurredAt = null)
    {
        var kind = exception is DoorsException de ? de.Kind : DoorsErrorKind.Unknown;
        var (reason, suggestion) = Explain(kind);

        // These reasons are best explained by the specific message (which folder, which unexpected error).
        if (kind is DoorsErrorKind.RootNotFound or DoorsErrorKind.ModuleNotAccessible) reason = exception.Message;
        else if (kind == DoorsErrorKind.Unknown) reason = $"{reason} {exception.Message}";

        var when = occurredAt ?? DateTime.Now;
        return new DoorsErrorReport(title, kind, reason, suggestion, BuildDetails(exception, kind, context, when), when);
    }

    public static (string Reason, string Suggestion) Explain(DoorsErrorKind kind) => kind switch
    {
        DoorsErrorKind.NotInstalled => (
            "The IBM DOORS client was not found on this computer (the DOORS.Application COM server is not registered).",
            "Make sure the DOORS client is installed. If it is, repair the installation or ask your IT team to re-register its COM server. To try the app without DOORS, choose \"Demo data\" as the connection."),
        DoorsErrorKind.StartFailed => (
            "Windows could not start the DOORS client or attach to the running DOORS.",
            "Start DOORS yourself and log in, then click \"Retry\". If DOORS and this app run with different privileges (one of them \"as administrator\"), start both the same way."),
        DoorsErrorKind.AccessDenied => (
            "Windows denied access to the DOORS process. This usually happens when one of the two programs runs as administrator and the other does not.",
            "Run DOORS and this app with the same privileges (both normally, or both \"as administrator\"), then try again."),
        DoorsErrorKind.Busy => (
            "DOORS is busy: an open dialog (login, warning or settings window) or another running DXL script is blocking commands.",
            "Switch to the DOORS window, close any open dialogs or finish logging in, then click \"Retry\"."),
        DoorsErrorKind.Timeout => (
            "DOORS did not respond in time. Its login window may be waiting behind other windows.",
            "Find the DOORS window on the taskbar and log in, then click \"Retry\"."),
        DoorsErrorKind.ConnectionLost => (
            "The DOORS client was closed or crashed after the connection was made.",
            "Restart DOORS and log in, then click \"Retry\"."),
        DoorsErrorKind.ScriptFailed => (
            "DOORS could not run the command or returned an unexpected response. A DXL error window may have opened in DOORS.",
            "If DOORS shows an error message, note its text and close the window. If the problem persists, use \"Copy details\" to send the information to your support team."),
        DoorsErrorKind.RootNotFound => (
            "The requested folder or project was not found.",
            "Check the root folder path (e.g. /Project/Requirements), or enter / for the whole database. If the folder exists, you may not have permission to see it."),
        DoorsErrorKind.ModuleNotAccessible => (
            "The module could not be opened.",
            "Check that the module still exists and that you have read access. Someone may have locked it exclusively; refresh the list and try again."),
        _ => (
            "An unexpected error occurred.",
            "Click \"Retry\". If the problem persists, use \"Copy details\" to send the information to your support team."),
    };

    private static string BuildDetails(
        Exception exception,
        DoorsErrorKind kind,
        IEnumerable<KeyValuePair<string, string>>? context,
        DateTime when)
    {
        var sb = new StringBuilder();
        sb.Append("Time: ").AppendLine(when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        sb.Append("Error kind: ").AppendLine(kind.ToString());
        foreach (var (key, value) in context ?? [])
        {
            sb.Append(key).Append(": ").AppendLine(value);
        }

        var depth = 0;
        for (var e = exception; e is not null; e = e.InnerException, depth++)
        {
            sb.Append(depth == 0 ? "Error: " : "Inner error: ")
              .Append(e.GetType().Name)
              .Append(" (HRESULT 0x").Append(e.HResult.ToString("X8", CultureInfo.InvariantCulture)).Append("): ")
              .AppendLine(e.Message.Trim());
        }

        return sb.ToString().TrimEnd();
    }
}
