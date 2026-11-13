using System.Runtime.InteropServices;
using System.Security.Principal;

namespace DoorsHandler.Desktop.Doors;

/// <summary>Facts about this process that help diagnose COM connection problems.</summary>
internal static class EnvironmentInfo
{
    public static IEnumerable<KeyValuePair<string, string>> Describe()
    {
        yield return new("App version", AppInfo.Version);
        yield return new("App architecture", Environment.Is64BitProcess ? "64-bit" : "32-bit");
        // DOORS and this app must run with the same privileges, otherwise COM refuses access.
        yield return new("Running as administrator", IsElevated() switch
        {
            true => "Yes",
            false => "No",
            null => "Unknown",
        });
        yield return new("Operating system", RuntimeInformation.OSDescription);
    }

    private static bool? IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
