using System.Text;

namespace DoorsHandler.Core;

/// <summary>Writes a module listing as CSV (UTF-8 with BOM so Excel shows Turkish characters correctly).</summary>
public static class CsvExporter
{
    public static string ToCsv(IEnumerable<DoorsItem> items, char separator = ';')
    {
        var sb = new StringBuilder();
        AppendRow(sb, separator, "Tür", "Ad", "Tam Yol", "Üst Klasör", "Unique ID", "Açıklama");
        foreach (var item in items)
        {
            AppendRow(sb, separator,
                item.Type.ToString(), item.Name, item.FullName, item.ParentPath, item.UniqueId, item.Description);
        }

        return sb.ToString();
    }

    public static void Write(string path, IEnumerable<DoorsItem> items, char separator = ';') =>
        File.WriteAllText(path, ToCsv(items, separator), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    private static void AppendRow(StringBuilder sb, char separator, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(separator);
            sb.Append(Quote(values[i], separator));
        }

        sb.Append("\r\n");
    }

    private static string Quote(string value, char separator)
    {
        if (value.IndexOfAny([separator, '"', '\n', '\r']) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
