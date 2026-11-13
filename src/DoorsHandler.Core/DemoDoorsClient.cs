using DoorsHandler.Core.Dxl;

namespace DoorsHandler.Core;

/// <summary>
/// Offline client with a small sample database, for trying the UI on machines without DOORS.
/// </summary>
public sealed class DemoDoorsClient : IDoorsClient
{
    private static readonly DoorsItem[] Database =
    [
        Item(DoorsItemType.Project, "/Vehicle Project", "Sample project"),
        Item(DoorsItemType.Folder, "/Vehicle Project/01 Requirements", ""),
        Item(DoorsItemType.Formal, "/Vehicle Project/01 Requirements/System Requirements", "SyRS"),
        Item(DoorsItemType.Formal, "/Vehicle Project/01 Requirements/Software Requirements", "SRS"),
        Item(DoorsItemType.Formal, "/Vehicle Project/01 Requirements/Hardware Requirements", "HRS"),
        Item(DoorsItemType.Folder, "/Vehicle Project/02 Design", ""),
        Item(DoorsItemType.Formal, "/Vehicle Project/02 Design/Software Architecture", "SAD"),
        Item(DoorsItemType.Descriptive, "/Vehicle Project/02 Design/Raw Notes", "Imported document"),
        Item(DoorsItemType.Folder, "/Vehicle Project/03 Test", ""),
        Item(DoorsItemType.Formal, "/Vehicle Project/03 Test/System Test Cases", "STP"),
        Item(DoorsItemType.Link, "/Vehicle Project/03 Test/Verification Links", "Test → Requirement"),
        Item(DoorsItemType.Link, "/Vehicle Project/Derivation Links", "Satisfies"),
        Item(DoorsItemType.Project, "/Satellite Project", ""),
        Item(DoorsItemType.Formal, "/Satellite Project/Mission Requirements", "MRD"),
        Item(DoorsItemType.Formal, "/Satellite Project/Interface Control Document", "ICD"),
        Item(DoorsItemType.Folder, "/Templates", "Company templates"),
        Item(DoorsItemType.Formal, "/Templates/Requirement Template", ""),
    ];

    public bool IsConnected { get; private set; }

    public async Task<DoorsSessionInfo> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        IsConnected = true;
        return new DoorsSessionInfo("demo", "Demo Database", "9.7 (demo)");
    }

    public async Task<DoorsListing> ListItemsAsync(string rootPath, bool recursive, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) throw new InvalidOperationException("Connect to DOORS first.");
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);

        var root = DxlScripts.NormalizePath(rootPath);
        if (root != "/" && !Database.Any(i => i.FullName == root && !i.IsModule))
        {
            throw new DoorsException(DoorsErrorKind.RootNotFound, $"Folder or project '{root}' was not found in DOORS, or you do not have access to it.");
        }

        var prefix = root == "/" ? "/" : root + "/";
        var items = Database
            .Where(i => i.FullName.StartsWith(prefix, StringComparison.Ordinal))
            .Where(i => recursive || i.ParentPath == root)
            .ToList();
        return new DoorsListing(items, []);
    }

    public async Task<DoorsModuleContent> ReadModuleAsync(string moduleFullName, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) throw new InvalidOperationException("Connect to DOORS first.");
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);

        var path = DxlScripts.NormalizePath(moduleFullName);
        var module = Database.FirstOrDefault(i => i.FullName == path && i.Type == DoorsItemType.Formal)
            ?? throw new DoorsException(DoorsErrorKind.ModuleNotAccessible,
                $"Module '{path}' could not be opened (not found, or no read access).");

        DoorsAttributeDefinition[] definitions =
        [
            new("Priority", "Priority"),
            new("Status", "Status"),
            new("Verification Method", "Verification"),
        ];
        var prefix = string.IsNullOrEmpty(module.Description) ? "REQ" : module.Description;
        var objects = new List<DoorsObject>();
        var number = 0;
        for (var section = 1; section <= 3; section++)
        {
            var headingNo = ++number;
            objects.Add(DemoObject(prefix, headingNo, 1, null, $"{section} Chapter {section}", string.Empty, definitions, "", "", ""));
            for (var req = 1; req <= 4; req++)
            {
                var n = ++number;
                objects.Add(DemoObject(prefix, n, 2, headingNo, string.Empty,
                    $"The system shall perform function {section}.{req} within 100 ms.",
                    definitions,
                    req % 3 == 0 ? "Low" : "High",
                    req % 2 == 0 ? "Approved" : "Draft",
                    "Test"));
            }
        }

        return new DoorsModuleContent(module.Name, module.FullName, module.Description, definitions, objects, []);
    }

    private static DoorsObject DemoObject(
        string prefix, int number, int level, int? parent, string heading, string text,
        DoorsAttributeDefinition[] definitions, params string[] values) =>
        new(number, $"{prefix}-{number}", level, parent, heading, text,
            "demo", "01.10.2026", "demo", "05.11.2026",
            definitions.Select((d, i) => (d.Name, Value: values[i])).ToDictionary(x => x.Name, x => x.Value));

    public void Dispose() => IsConnected = false;

    private static DoorsItem Item(DoorsItemType type, string path, string description) =>
        new(type, path[(path.LastIndexOf('/') + 1)..], path, Id(path), description);

    private static string Id(string path) => ((uint)StableHash(path)).ToString("x8");

    private static int StableHash(string s)
    {
        unchecked
        {
            var h = 17;
            foreach (var c in s) h = h * 31 + c;
            return h;
        }
    }
}
