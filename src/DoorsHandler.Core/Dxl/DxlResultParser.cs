using System.Text;

namespace DoorsHandler.Core.Dxl;

/// <summary>Parses the line protocol produced by the scripts in <see cref="DxlScripts"/>.</summary>
public static class DxlResultParser
{
    public static DoorsSessionInfo ParseSessionInfo(string? raw)
    {
        var lines = ReadLines(raw);
        foreach (var fields in lines)
        {
            if (fields[0] == "S")
            {
                return new DoorsSessionInfo(Field(fields, 1), Field(fields, 2), Field(fields, 3));
            }
        }

        throw new DoorsException(DoorsErrorKind.ScriptFailed, "DOORS oturum bilgisi döndürmedi.");
    }

    public static DoorsListing ParseListing(string? raw)
    {
        var items = new List<DoorsItem>();
        var warnings = new List<string>();

        foreach (var fields in ReadLines(raw))
        {
            switch (fields[0])
            {
                case "I":
                    items.Add(new DoorsItem(
                        DoorsItem.ParseType(Field(fields, 1)),
                        Field(fields, 2),
                        Field(fields, 3),
                        Field(fields, 4),
                        Field(fields, 5)));
                    break;
                case "W":
                    warnings.Add(DescribeWarning(Field(fields, 1), Field(fields, 2)));
                    break;
            }
        }

        return new DoorsListing(items, warnings);
    }

    /// <summary>
    /// Splits the raw result into lines of unescaped fields, throwing for <c>ERR</c> lines
    /// and when the <c>OK</c> marker is missing (e.g. the script aborted halfway).
    /// </summary>
    private static List<string[]> ReadLines(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            throw new DoorsException(DoorsErrorKind.ScriptFailed, "DOORS boş yanıt döndürdü. DXL betiği çalıştırılamamış olabilir.");
        }

        var result = new List<string[]>();
        var ok = false;
        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) continue;

            var fields = trimmed.Split('\t').Select(Unescape).ToArray();
            switch (fields[0])
            {
                case "OK":
                    ok = true;
                    break;
                case "ERR":
                    var code = Field(fields, 1);
                    throw new DoorsException(
                        code == "ROOT_NOT_FOUND" ? DoorsErrorKind.RootNotFound : DoorsErrorKind.ScriptFailed,
                        DescribeError(code, Field(fields, 2)));
                default:
                    result.Add(fields);
                    break;
            }
        }

        if (!ok)
        {
            throw new DoorsException(DoorsErrorKind.ScriptFailed, "DOORS yanıtı eksik; DXL betiği tamamlanmadan durdu.");
        }

        return result;
    }

    private static string Field(string[] fields, int index) => index < fields.Length ? fields[index] : string.Empty;

    private static string DescribeError(string code, string detail) => code switch
    {
        "ROOT_NOT_FOUND" => $"DOORS'ta '{detail}' klasörü/projesi bulunamadı ya da erişim izniniz yok.",
        _ => string.IsNullOrEmpty(detail) ? $"DOORS hatası: {code}" : $"DOORS hatası ({code}): {detail}",
    };

    private static string DescribeWarning(string code, string detail) => code switch
    {
        "FOLDER_INACCESSIBLE" => $"'{detail}' içeriği okunamadı (erişim izni olmayabilir).",
        "DXL" => $"DOORS uyarısı: {detail.Trim()}",
        _ => string.IsNullOrEmpty(detail) ? code : $"{code}: {detail}",
    };

    /// <summary>Reverses the escaping done by <c>dhEsc</c> in the DXL prelude.</summary>
    internal static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0) return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c != '\\' || i == value.Length - 1)
            {
                sb.Append(c);
                continue;
            }

            var next = value[++i];
            sb.Append(next switch
            {
                't' => '\t',
                'n' => '\n',
                'r' => '\r',
                '\\' => '\\',
                _ => next,
            });
        }

        return sb.ToString();
    }
}
