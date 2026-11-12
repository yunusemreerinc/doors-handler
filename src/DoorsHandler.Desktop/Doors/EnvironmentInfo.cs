using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace DoorsHandler.Desktop.Doors;

/// <summary>Facts about this process that help diagnose COM connection problems.</summary>
internal static class EnvironmentInfo
{
    public static IEnumerable<KeyValuePair<string, string>> Describe()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        yield return new("Uygulama sürümü", version);
        yield return new("Uygulama mimarisi", Environment.Is64BitProcess ? "64-bit" : "32-bit");
        // DOORS and this app must run with the same privileges, otherwise COM refuses access.
        yield return new("Yönetici olarak çalışıyor", IsElevated() switch
        {
            true => "Evet",
            false => "Hayır",
            null => "Bilinmiyor",
        });
        yield return new("İşletim sistemi", RuntimeInformation.OSDescription);
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
