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

    public static DoorsModuleContent ParseModuleContent(string? raw)
    {
        string name = string.Empty, fullName = string.Empty, description = string.Empty;
        var hasHeader = false;
        var definitions = new List<DoorsAttributeDefinition>();
        var objects = new List<DoorsObject>();
        var warnings = new List<string>();

        foreach (var fields in ReadLines(raw))
        {
            switch (fields[0])
            {
                case "M":
                    hasHeader = true;
                    name = Field(fields, 1);
                    fullName = Field(fields, 2);
                    description = Field(fields, 3);
                    break;
                case "A":
                    definitions.Add(new DoorsAttributeDefinition(Field(fields, 1), Field(fields, 2)));
                    break;
                case "O":
                    objects.Add(ParseObject(fields, definitions));
                    break;
                case "W":
                    warnings.Add(DescribeWarning(Field(fields, 1), Field(fields, 2)));
                    break;
            }
        }

        if (!hasHeader)
        {
            throw new DoorsException(DoorsErrorKind.ScriptFailed, "DOORS modül bilgisini döndürmedi.");
        }

        return new DoorsModuleContent(name, fullName, description, definitions, objects, warnings);
    }

    private const int FirstAttributeField = 11;

    private static DoorsObject ParseObject(string[] fields, List<DoorsAttributeDefinition> definitions)
    {
        var attributes = new Dictionary<string, string>(definitions.Count, StringComparer.Ordinal);
        for (var i = 0; i < definitions.Count; i++)
        {
            attributes[definitions[i].Name] = Field(fields, FirstAttributeField + i);
        }

        var parent = Int(Field(fields, 4));
        return new DoorsObject(
            AbsoluteNumber: Int(Field(fields, 1)),
            Identifier: Field(fields, 2),
            Level: Int(Field(fields, 3)),
            ParentAbsoluteNumber: parent > 0 ? parent : null,
            Heading: Field(fields, 5),
            Text: Field(fields, 6),
            CreatedBy: Field(fields, 7),
            CreatedOn: Field(fields, 8),
            LastModifiedBy: Field(fields, 9),
            LastModifiedOn: Field(fields, 10),
            Attributes: attributes);
    }

    private static int Int(string value) =>
        int.TryParse(value.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;

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
                        code switch
                        {
                            "ROOT_NOT_FOUND" => DoorsErrorKind.RootNotFound,
                            "MODULE_NOT_ACCESSIBLE" => DoorsErrorKind.ModuleNotAccessible,
                            _ => DoorsErrorKind.ScriptFailed,
                        },
                        DescribeError(code, Field(fields, 2), Field(fields, 3)));
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

    private static string DescribeError(string code, string detail, string doorsMessage) => code switch
    {
        "ROOT_NOT_FOUND" => $"DOORS'ta '{detail}' klasörü/projesi bulunamadı ya da erişim izniniz yok.",
        "MODULE_NOT_ACCESSIBLE" => string.IsNullOrWhiteSpace(doorsMessage)
            ? $"'{detail}' modülü açılamadı (bulunamadı ya da okuma izniniz yok)."
            : $"'{detail}' modülü açılamadı: {doorsMessage.Trim()}",
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
