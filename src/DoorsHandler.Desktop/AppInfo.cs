using System.Reflection;

namespace DoorsHandler.Desktop;

/// <summary>Name and version of the app, as shown to the user.</summary>
internal static class AppInfo
{
    public const string Name = "DOORS Handler";

    /// <summary>
    /// The version in X.YY format (e.g. "0.01"), from the AppVersion property in the project file.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // Defensive: drop any "+<build metadata>" suffix in case a build adds one.
        return informational?.Split('+')[0] is { Length: > 0 } version ? version : "?";
    }
}
