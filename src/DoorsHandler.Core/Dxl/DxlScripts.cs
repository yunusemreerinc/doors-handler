using System.Text;

namespace DoorsHandler.Core.Dxl;

/// <summary>
/// Builds the DXL scripts sent to DOORS.
/// </summary>
/// <remarks>
/// Every script returns its output through <c>oleSetResult</c> using a simple line protocol
/// that <see cref="DxlResultParser"/> understands:
/// <list type="bullet">
///   <item><c>OK</c> once the script has finished successfully,</item>
///   <item><c>ERR&lt;TAB&gt;code&lt;TAB&gt;detail</c> when it failed,</item>
///   <item><c>S&lt;TAB&gt;user&lt;TAB&gt;database&lt;TAB&gt;version</c> for session info,</item>
///   <item><c>I&lt;TAB&gt;type&lt;TAB&gt;name&lt;TAB&gt;fullName&lt;TAB&gt;uniqueId&lt;TAB&gt;description</c> per item,</item>
///   <item><c>W&lt;TAB&gt;code&lt;TAB&gt;detail</c> for non-fatal warnings.</item>
/// </list>
/// Lines may come in any order. Messages are sent as ASCII codes and translated on the .NET
/// side, so the scripts stay independent of the DOORS client's code page.
/// Field values are escaped (<c>\t</c>, <c>\n</c>, <c>\r</c>, <c>\\</c>) so they never break the protocol.
/// </remarks>
public static class DxlScripts
{
    // Helpers shared by all scripts. Names are prefixed with "dh" to avoid clashing
    // with DXL built-ins or user-defined globals in the DOORS session.
    private const string Prelude = """
        Buffer dhOut = create

        string dhEsc(string s) {
            if (null s) return ""
            Buffer b = create
            int n = length(s)
            int k
            for (k = 0; k < n; k++) {
                char c = s[k]
                if (c == '\\') { b += "\\\\" }
                else if (c == '\t') { b += "\\t" }
                else if (c == '\n') { b += "\\n" }
                else if (c == '\r') { b += "\\r" }
                else { b += c }
            }
            string r = stringOf(b)
            delete b
            return r
        }

        void dhField(string s) {
            dhOut += "\t"
            dhOut += dhEsc(s)
        }

        void dhFinish() {
            oleSetResult(stringOf(dhOut))
            delete dhOut
        }

        """;

    /// <summary>Script returning the current user, database name and DOORS version.</summary>
    public static string SessionInfo() => Prelude + """
        dhOut += "OK\n"
        dhOut += "S"
        dhField(doorsname())
        dhField(getDatabaseName())
        dhField(doorsInfo(infoVersion))
        dhOut += "\n"
        dhFinish()
        """;

    /// <summary>Script listing folders, projects and modules under <paramref name="rootPath"/>.</summary>
    public static string ListItems(string rootPath, bool recursive)
    {
        var root = NormalizePath(rootPath);
        var sb = new StringBuilder(Prelude);
        sb.Append("string dhRootPath = ").Append(ToDxlStringLiteral(root)).Append('\n');
        sb.Append("bool dhRecursive = ").Append(recursive ? "true" : "false").Append('\n');
        sb.Append("""

            void dhEmit(Item i) {
                dhOut += "I"
                dhField(type(i))
                dhField(name(i))
                dhField(fullName(i))
                dhField(uniqueID(i))
                dhField(description(i))
                dhOut += "\n"
            }

            void dhScan(Folder f) {
                Item i
                for i in f do {
                    string t = type(i)
                    dhEmit(i)
                    if (dhRecursive && (t == "Folder" || t == "Project")) {
                        Folder sub = folder(fullName(i))
                        if (null sub) {
                            dhOut += "W\tFOLDER_INACCESSIBLE"
                            dhField(fullName(i))
                            dhOut += "\n"
                        } else {
                            dhScan(sub)
                        }
                    }
                }
            }

            // Errors are suppressed while scanning so that a single unreadable
            // folder does not abort the whole listing; they are reported as warnings.
            noError()
            Folder dhRoot = folder(dhRootPath)
            if (!null dhRoot) {
                dhScan(dhRoot)
            }
            string dhWarn = lastError()

            if (null dhRoot) {
                dhOut += "ERR\tROOT_NOT_FOUND"
                dhField(dhRootPath)
                dhOut += "\n"
            } else {
                dhOut += "OK\n"
                if (!null dhWarn && dhWarn != "") {
                    dhOut += "W\tDXL"
                    dhField(dhWarn)
                    dhOut += "\n"
                }
            }
            dhFinish()

            """);
        return sb.ToString();
    }

    /// <summary>Trims the path, ensures a leading slash and removes a trailing one (except for the root).</summary>
    public static string NormalizePath(string? path)
    {
        var p = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (p.Length == 0) return "/";
        if (!p.StartsWith('/')) p = "/" + p;
        while (p.Length > 1 && p.EndsWith('/')) p = p[..^1];
        return p;
    }

    /// <summary>Encodes a .NET string as a DXL string literal.</summary>
    public static string ToDxlStringLiteral(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
