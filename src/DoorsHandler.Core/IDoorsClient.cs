namespace DoorsHandler.Core;

/// <summary>High-level access to a DOORS database.</summary>
public interface IDoorsClient : IDisposable
{
    bool IsConnected { get; }

    /// <summary>Attaches to DOORS and returns information about the logged-in session.</summary>
    Task<DoorsSessionInfo> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists folders, projects and modules under <paramref name="rootPath"/>
    /// ("/" is the database root).
    /// </summary>
    Task<DoorsListing> ListItemsAsync(string rootPath, bool recursive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all objects of the formal module at <paramref name="moduleFullName"/>.
    /// The module is opened read-only and closed again unless the user already had it open.
    /// </summary>
    Task<DoorsModuleContent> ReadModuleAsync(string moduleFullName, CancellationToken cancellationToken = default);
}

/// <summary>Runs a DXL script inside DOORS and returns the string the script passed to <c>oleSetResult</c>.</summary>
public interface IDxlExecutor : IDisposable
{
    Task<string> ExecuteAsync(string dxl, CancellationToken cancellationToken = default);
}
