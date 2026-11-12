using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoorsHandler.Core;

/// <summary>Progress of a multi-module export: module <see cref="Index"/> (1-based) of <see cref="Total"/>.</summary>
public sealed record ModuleExportProgress(int Index, int Total, string ModuleFullName);

/// <summary>Outcome for one module: either its content or the error that prevented reading it.</summary>
public sealed record ModuleExportResult(DoorsItem Module, DoorsModuleContent? Content, Exception? Error)
{
    public bool Succeeded => Content is not null;
}

/// <summary>Reads the contents of formal modules from DOORS and writes them as JSON.</summary>
public static class ModuleJsonExporter
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keep Turkish and other non-ASCII text readable in the file instead of \uXXXX escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Reads each formal module in <paramref name="modules"/>. A module that cannot be read is
    /// recorded with its error and the export continues; cancellation and a lost DOORS
    /// connection stop the whole export.
    /// </summary>
    public static async Task<IReadOnlyList<ModuleExportResult>> ReadModulesAsync(
        IDoorsClient client,
        IReadOnlyList<DoorsItem> modules,
        IProgress<ModuleExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ModuleExportResult>(modules.Count);
        for (var i = 0; i < modules.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var module = modules[i];
            progress?.Report(new ModuleExportProgress(i + 1, modules.Count, module.FullName));
            try
            {
                var content = await client.ReadModuleAsync(module.FullName, cancellationToken).ConfigureAwait(false);
                results.Add(new ModuleExportResult(module, content, null));
            }
            catch (DoorsException ex) when (ex.Kind != DoorsErrorKind.ConnectionLost)
            {
                results.Add(new ModuleExportResult(module, null, ex));
            }
        }

        return results;
    }

    public static string ToJson(
        IEnumerable<ModuleExportResult> results,
        DoorsSessionInfo? session,
        DateTimeOffset exportedAt)
    {
        var document = new ExportDocument(
            FormatVersion,
            exportedAt,
            session?.DatabaseName,
            session?.UserName,
            session?.Version,
            results.Select(ToModule).ToList());
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static async Task WriteAsync(
        string path,
        IEnumerable<ModuleExportResult> results,
        DoorsSessionInfo? session,
        DateTimeOffset exportedAt,
        CancellationToken cancellationToken = default)
    {
        var json = ToJson(results, session, exportedAt);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
            .ConfigureAwait(false);
    }

    private static ExportModule ToModule(ModuleExportResult result)
    {
        var item = result.Module;
        if (result.Content is not { } content)
        {
            return new ExportModule(item.Name, item.FullName, item.UniqueId, NullIfEmpty(item.Description),
                Error: result.Error?.Message ?? "Bilinmeyen hata", null, null, null, null);
        }

        return new ExportModule(
            content.Name.Length > 0 ? content.Name : item.Name,
            content.FullName.Length > 0 ? content.FullName : item.FullName,
            item.UniqueId,
            NullIfEmpty(content.Description),
            Error: null,
            content.Warnings.Count > 0 ? content.Warnings : null,
            content.AttributeDefinitions,
            content.Objects.Count,
            content.Objects.Select(o => new ExportObject(
                o.AbsoluteNumber,
                o.Identifier,
                o.Level,
                o.ParentAbsoluteNumber,
                o.Heading,
                o.Text,
                o.CreatedBy,
                o.CreatedOn,
                o.LastModifiedBy,
                o.LastModifiedOn,
                o.Attributes)).ToList());
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    // JSON shape. Property names are camelCased by JsonOptions; attribute names inside
    // "attributes" are kept exactly as they are in DOORS.
    private sealed record ExportDocument(
        int FormatVersion,
        DateTimeOffset ExportedAt,
        string? Database,
        string? ExportedBy,
        string? DoorsVersion,
        IReadOnlyList<ExportModule> Modules);

    private sealed record ExportModule(
        string Name,
        string FullName,
        string UniqueId,
        string? Description,
        string? Error,
        IReadOnlyList<string>? Warnings,
        IReadOnlyList<DoorsAttributeDefinition>? AttributeDefinitions,
        int? ObjectCount,
        IReadOnlyList<ExportObject>? Objects);

    private sealed record ExportObject(
        int AbsoluteNumber,
        string Identifier,
        int Level,
        int? ParentAbsoluteNumber,
        string Heading,
        string Text,
        string CreatedBy,
        string CreatedOn,
        string LastModifiedBy,
        string LastModifiedOn,
        IReadOnlyDictionary<string, string> Attributes);
}
