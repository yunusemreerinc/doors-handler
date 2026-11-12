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

    /// <summary>
    /// Script reading every object of the formal module at <paramref name="moduleFullName"/>.
    /// </summary>
    /// <remarks>
    /// Output lines (besides <c>OK</c>/<c>ERR</c>/<c>W</c>):
    /// <list type="bullet">
    ///   <item><c>M&lt;TAB&gt;name&lt;TAB&gt;fullName&lt;TAB&gt;description</c></item>
    ///   <item><c>A&lt;TAB&gt;attributeName&lt;TAB&gt;typeName</c> for each user-defined object attribute, in order,</item>
    ///   <item><c>O&lt;TAB&gt;absNo&lt;TAB&gt;identifier&lt;TAB&gt;level&lt;TAB&gt;parentAbsNo&lt;TAB&gt;heading&lt;TAB&gt;text&lt;TAB&gt;
    ///   createdBy&lt;TAB&gt;createdOn&lt;TAB&gt;modifiedBy&lt;TAB&gt;modifiedOn</c> followed by one value per <c>A</c> line.</item>
    /// </list>
    /// The module is opened read-only and invisibly; it is closed afterwards only if it was not
    /// already open, so a module the user is working on is left alone. Soft-deleted objects are
    /// skipped; filters of the default view are ignored (<c>entire</c>).
    /// </remarks>
    public static string ReadModule(string moduleFullName)
    {
        var path = NormalizePath(moduleFullName);
        var sb = new StringBuilder(Prelude);
        sb.Append("string dhModPath = ").Append(ToDxlStringLiteral(path)).Append('\n');
        sb.Append("""

            Skip dhAttrNames = create
            int dhAttrCount = 0
            bool dhWasOpen = false
            Module dhMod = null

            void dhReadObjects() {
                AttrDef ad
                for ad in dhMod do {
                    if (!ad.object || ad.system) continue
                    string adName = ad.name
                    put(dhAttrNames, dhAttrCount, adName)
                    dhAttrCount++
                    dhOut += "A"
                    dhField(adName)
                    dhField(ad.typeName)
                    dhOut += "\n"
                }

                Object o
                for o in entire dhMod do {
                    if (isDeleted(o)) continue
                    int absNo = o."Absolute Number"
                    int parentNo = 0
                    Object p = parent(o)
                    if (!null p) {
                        parentNo = p."Absolute Number"
                    }
                    dhOut += "O"
                    dhField(absNo "")
                    dhField(identifier(o))
                    dhField(level(o) "")
                    dhField(parentNo "")
                    dhField(o."Object Heading" "")
                    dhField(o."Object Text" "")
                    dhField(o."Created By" "")
                    dhField(o."Created On" "")
                    dhField(o."Last Modified By" "")
                    dhField(o."Last Modified On" "")
                    int k
                    for (k = 0; k < dhAttrCount; k++) {
                        string an = ""
                        find(dhAttrNames, k, an)
                        dhField(o.an "")
                    }
                    dhOut += "\n"
                }
            }

            // Errors are suppressed so that one unreadable attribute does not abort the
            // export; they are reported as a warning instead.
            noError()
            ModName_ dhRef = module(dhModPath)
            if (!null dhRef) {
                dhWasOpen = open(dhRef)
                dhMod = read(dhModPath, false)
            }
            if (!null dhMod) {
                dhOut += "M"
                dhField(name(dhMod))
                dhField(fullName(dhMod))
                dhField(description(dhMod))
                dhOut += "\n"
                dhReadObjects()
            }
            string dhWarn = lastError()

            if (null dhMod) {
                dhOut += "ERR\tMODULE_NOT_ACCESSIBLE"
                dhField(dhModPath)
                dhField(dhWarn)
                dhOut += "\n"
            } else {
                if (!dhWasOpen) {
                    close(dhMod, false)
                }
                dhOut += "OK\n"
                if (!null dhWarn && dhWarn != "") {
                    dhOut += "W\tDXL"
                    dhField(dhWarn)
                    dhOut += "\n"
                }
            }
            delete dhAttrNames
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
