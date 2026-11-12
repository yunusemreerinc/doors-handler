using DoorsHandler.Core.Dxl;

namespace DoorsHandler.Core;

/// <summary><see cref="IDoorsClient"/> that talks to DOORS by running DXL through an <see cref="IDxlExecutor"/>.</summary>
public sealed class DxlDoorsClient : IDoorsClient
{
    /// <summary>Default time to wait for DOORS to start and for the user to log in.</summary>
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromMinutes(2);

    private readonly IDxlExecutor _executor;
    private bool _disposed;

    public DxlDoorsClient(IDxlExecutor executor, TimeSpan? connectTimeout = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        ConnectTimeout = connectTimeout ?? DefaultConnectTimeout;
    }

    public TimeSpan ConnectTimeout { get; }

    public bool IsConnected { get; private set; }

    public async Task<DoorsSessionInfo> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string raw;
        try
        {
            // A COM call cannot be aborted, so on timeout/cancel we stop waiting for it;
            // the caller disposes this client and a new one is created for the next attempt.
            raw = await _executor.ExecuteAsync(DxlScripts.SessionInfo(), cancellationToken)
                .WaitAsync(ConnectTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new DoorsException(
                DoorsErrorKind.Timeout,
                $"DOORS {ConnectTimeout.TotalSeconds:0} saniye içinde yanıt vermedi.",
                ex);
        }

        var info = DxlResultParser.ParseSessionInfo(raw);
        IsConnected = true;
        return info;
    }

    public async Task<DoorsListing> ListItemsAsync(string rootPath, bool recursive, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsConnected)
        {
            throw new InvalidOperationException("Önce DOORS'a bağlanın.");
        }

        string raw;
        try
        {
            // No timeout: scanning a large database legitimately takes long. The user can cancel.
            raw = await _executor.ExecuteAsync(DxlScripts.ListItems(rootPath, recursive), cancellationToken)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DoorsException ex) when (ex.Kind == DoorsErrorKind.ConnectionLost)
        {
            IsConnected = false;
            throw;
        }

        return DxlResultParser.ParseListing(raw);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsConnected = false;
        _executor.Dispose();
    }
}
