using System.Runtime.InteropServices;
using DoorsHandler.Core;

namespace DoorsHandler.Desktop.Doors;

/// <summary>
/// Runs DXL in the IBM DOORS (Classic) client through its COM automation server
/// (<c>DOORS.Application</c>): <c>runStr(dxl)</c> executes the script and the
/// <c>result</c> property returns whatever the script passed to <c>oleSetResult</c>.
/// </summary>
/// <remarks>
/// Late binding is used so no DOORS type library or interop assembly is needed at build time.
/// If the DOORS client is not running, Windows starts it and DOORS shows its login dialog;
/// the call returns once the user has logged in.
/// Every failure is rethrown as a <see cref="DoorsException"/> whose <see cref="DoorsException.Kind"/>
/// says why it failed, so the UI can explain it to the user.
/// </remarks>
internal sealed class ComDxlExecutor : IDxlExecutor
{
    public const string ProgId = "DOORS.Application";

    private readonly StaWorker _worker = new("DOORS COM");
    private object? _doors;
    private bool _disposed;

    public Task<string> ExecuteAsync(string dxl, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _worker.InvokeAsync(() => Run(dxl), cancellationToken);
    }

    private string Run(string dxl)
    {
        _doors ??= CreateDoors();
        dynamic doors = _doors;
        try
        {
            doors.runStr(dxl);
            object? result = doors.result;
            return result as string ?? string.Empty;
        }
        catch (Exception ex) when (ex is not DoorsException)
        {
            var kind = DoorsErrorClassifier.FromHResult(ex.HResult, duringStartup: false);
            if (kind == DoorsErrorKind.ConnectionLost)
            {
                Release();
            }

            throw new DoorsException(kind, $"DOORS komutu çalıştırılamadı: {ex.Message}", ex);
        }
    }

    private static object CreateDoors()
    {
        var type = Type.GetTypeFromProgID(ProgId, throwOnError: false)
            ?? throw new DoorsException(
                DoorsErrorKind.NotInstalled,
                $"'{ProgId}' COM sunucusu bu bilgisayarda kayıtlı değil.");

        try
        {
            return Activator.CreateInstance(type)
                ?? throw new DoorsException(DoorsErrorKind.StartFailed, "DOORS COM nesnesi oluşturulamadı.");
        }
        catch (Exception ex) when (ex is not DoorsException)
        {
            // Besides COMException, .NET maps some HRESULTs to other exception types
            // (e.g. E_ACCESSDENIED -> UnauthorizedAccessException), so classify by HResult.
            throw new DoorsException(
                DoorsErrorClassifier.FromHResult(ex.HResult, duringStartup: true),
                $"DOORS istemcisi başlatılamadı ya da bağlanılamadı: {ex.Message}",
                ex);
        }
    }

    private void Release()
    {
        if (_doors is not null && Marshal.IsComObject(_doors))
        {
            try
            {
                Marshal.FinalReleaseComObject(_doors);
            }
            catch (Exception)
            {
                // The DOORS process may already be gone; nothing left to release.
            }
        }

        _doors = null;
    }

    /// <summary>
    /// Does not block: if a DOORS call is still running (e.g. waiting for the login window),
    /// the release is queued behind it and the worker thread ends once that call returns.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = _worker.InvokeAsync(() => { Release(); return true; });
        _worker.Dispose();
    }
}
