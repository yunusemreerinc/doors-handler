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
/// </remarks>
internal sealed class ComDxlExecutor : IDxlExecutor
{
    public const string ProgId = "DOORS.Application";

    // HRESULTs meaning the DOORS process went away (closed or crashed) since we attached.
    private static readonly int[] DisconnectedHResults =
    [
        unchecked((int)0x800706BA), // RPC_S_SERVER_UNAVAILABLE
        unchecked((int)0x800706BE), // RPC_S_CALL_FAILED
        unchecked((int)0x80010108), // RPC_E_DISCONNECTED
        unchecked((int)0x80010114), // RPC_E_SERVER_DIED_DNE
    ];

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
        catch (COMException ex) when (DisconnectedHResults.Contains(ex.HResult))
        {
            Release();
            throw new DoorsConnectionLostException("DOORS ile bağlantı koptu. DOORS istemcisi kapatılmış olabilir; yeniden bağlanın.", ex);
        }
        catch (COMException ex)
        {
            throw new DoorsException($"DOORS DXL betiğini çalıştıramadı: {ex.Message}", ex);
        }
    }

    private static object CreateDoors()
    {
        var type = Type.GetTypeFromProgID(ProgId, throwOnError: false)
            ?? throw new DoorsException(
                $"'{ProgId}' COM sunucusu bulunamadı. Bu bilgisayarda IBM DOORS istemcisinin kurulu olduğundan emin olun.");

        try
        {
            return Activator.CreateInstance(type)
                ?? throw new DoorsException("DOORS COM nesnesi oluşturulamadı.");
        }
        catch (COMException ex)
        {
            throw new DoorsException($"DOORS istemcisine bağlanılamadı: {ex.Message}", ex);
        }
    }

    private void Release()
    {
        if (_doors is not null && Marshal.IsComObject(_doors))
        {
            Marshal.FinalReleaseComObject(_doors);
        }

        _doors = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _worker.InvokeAsync(() => { Release(); return true; }).Wait(TimeSpan.FromSeconds(5));
        _worker.Dispose();
    }
}
