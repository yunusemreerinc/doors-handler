using DoorsHandler.Core.Dxl;

namespace DoorsHandler.Core;

/// <summary><see cref="IDoorsClient"/> that talks to DOORS by running DXL through an <see cref="IDxlExecutor"/>.</summary>
public sealed class DxlDoorsClient : IDoorsClient
{
    private readonly IDxlExecutor _executor;
    private bool _disposed;

    public DxlDoorsClient(IDxlExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public bool IsConnected { get; private set; }

    public async Task<DoorsSessionInfo> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var raw = await _executor.ExecuteAsync(DxlScripts.SessionInfo(), cancellationToken).ConfigureAwait(false);
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

        var raw = await _executor.ExecuteAsync(DxlScripts.ListItems(rootPath, recursive), cancellationToken).ConfigureAwait(false);
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
