using System.Text.Json;
using DoorsHandler.Core;
using DoorsHandler.Core.Dxl;

namespace DoorsHandler.Core.Tests;

public class ModuleContentParserTests
{
    private const string Raw =
        "M\tSRS\t/P/SRS\tSoftware reqs\n" +
        "A\tPriority\tPriorityType\n" +
        "A\tNotes\tText\n" +
        "O\t1\tSRS-1\t1\t0\t1 Intro\t\tjdoe\t01.10.2026\tjdoe\t02.10.2026\t\t\n" +
        "O\t2\tSRS-2\t2\t1\t\tThe system shall\\tstart.\\nQuickly.\tjdoe\t01.10.2026\tamy\t03.10.2026\tHigh\tsee \\\\ref\n" +
        "W\tDXL\t-E- no read access\n" +
        "OK\n";

    [Fact]
    public void ParseModuleContent_ReadsHeaderAttributesAndObjects()
    {
        var content = DxlResultParser.ParseModuleContent(Raw);

        Assert.Equal("SRS", content.Name);
        Assert.Equal("/P/SRS", content.FullName);
        Assert.Equal("Software reqs", content.Description);
        Assert.Equal([new("Priority", "PriorityType"), new("Notes", "Text")], content.AttributeDefinitions);
        Assert.Equal(2, content.Objects.Count);

        var heading = content.Objects[0];
        Assert.Equal(1, heading.AbsoluteNumber);
        Assert.Equal(1, heading.Level);
        Assert.Null(heading.ParentAbsoluteNumber);
        Assert.Equal("1 Intro", heading.Heading);

        var req = content.Objects[1];
        Assert.Equal("SRS-2", req.Identifier);
        Assert.Equal(2, req.Level);
        Assert.Equal(1, req.ParentAbsoluteNumber);
        Assert.Equal("The system shall\tstart.\nQuickly.", req.Text);
        Assert.Equal("amy", req.LastModifiedBy);
        Assert.Equal("03.10.2026", req.LastModifiedOn);
        Assert.Equal("High", req.Attributes["Priority"]);
        Assert.Equal("see \\ref", req.Attributes["Notes"]);

        Assert.Contains("no read access", Assert.Single(content.Warnings));
    }

    [Fact]
    public void ParseModuleContent_MissingAttributeValues_BecomeEmpty()
    {
        var content = DxlResultParser.ParseModuleContent(
            "M\tX\t/X\t\nA\tPriority\tT\nO\t5\tX-5\t1\t0\tH\n" + "OK\n");

        var obj = Assert.Single(content.Objects);
        Assert.Equal(string.Empty, obj.Text);
        Assert.Equal(string.Empty, obj.Attributes["Priority"]);
    }

    [Fact]
    public void ParseModuleContent_ModuleNotAccessible_ThrowsWithDoorsMessage()
    {
        var ex = Assert.Throws<DoorsException>(() => DxlResultParser.ParseModuleContent(
            "ERR\tMODULE_NOT_ACCESSIBLE\t/P/Locked\t-E- module is locked\n"));

        Assert.Equal(DoorsErrorKind.ModuleNotAccessible, ex.Kind);
        Assert.Contains("/P/Locked", ex.Message);
        Assert.Contains("module is locked", ex.Message);
    }

    [Fact]
    public void ParseModuleContent_WithoutHeader_IsAScriptFailure()
    {
        var ex = Assert.Throws<DoorsException>(() => DxlResultParser.ParseModuleContent("OK\n"));

        Assert.Equal(DoorsErrorKind.ScriptFailed, ex.Kind);
    }
}

public class ModuleJsonExporterTests
{
    private static DoorsItem Formal(string path) =>
        new(DoorsItemType.Formal, path[(path.LastIndexOf('/') + 1)..], path, "id-" + path.Length, "");

    private sealed class FakeClient(Func<string, DoorsModuleContent> read) : IDoorsClient
    {
        public List<string> Read { get; } = [];
        public bool IsConnected => true;

        public Task<DoorsSessionInfo> ConnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DoorsSessionInfo("u", "db", "9.7"));

        public Task<DoorsListing> ListItemsAsync(string rootPath, bool recursive, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DoorsModuleContent> ReadModuleAsync(string moduleFullName, CancellationToken cancellationToken = default)
        {
            Read.Add(moduleFullName);
            return Task.FromResult(read(moduleFullName));
        }

        public void Dispose() { }
    }

    // Sample text with non-ASCII characters (German umlauts, ß, µ, °) to check they survive the export.
    private static DoorsModuleContent Content(string path) => new(
        path[(path.LastIndexOf('/') + 1)..], path, "desc",
        [new("Priorität", "Priority")],
        [new(1, "R-1", 1, null, "Einführung", "", "a", "1", "b", "2", new Dictionary<string, string> { ["Priorität"] = "Höchste" }),
         new(2, "R-2", 2, 1, "", "Größe \"5 µm\" bei 20 °C", "a", "1", "b", "2", new Dictionary<string, string> { ["Priorität"] = "" })],
        []);

    [Fact]
    public async Task ReadModules_ContinuesAfterAModuleFails_AndReportsProgress()
    {
        var client = new FakeClient(path => path == "/P/Locked"
            ? throw new DoorsException(DoorsErrorKind.ModuleNotAccessible, "locked")
            : Content(path));
        var reported = new List<ModuleExportProgress>();

        var results = await ModuleJsonExporter.ReadModulesAsync(client,
            [Formal("/P/A"), Formal("/P/Locked"), Formal("/P/B")],
            new SynchronousProgress(reported.Add));

        Assert.Equal(["/P/A", "/P/Locked", "/P/B"], client.Read);
        Assert.Equal([true, false, true], results.Select(r => r.Succeeded));
        Assert.Equal("locked", results[1].Error!.Message);
        Assert.Equal([1, 2, 3], reported.Select(p => p.Index));
        Assert.All(reported, p => Assert.Equal(3, p.Total));
    }

    [Fact]
    public async Task ReadModules_StopsWhenConnectionIsLost()
    {
        var client = new FakeClient(path => path == "/P/A"
            ? throw new DoorsException(DoorsErrorKind.ConnectionLost, "gone")
            : Content(path));

        await Assert.ThrowsAsync<DoorsException>(() =>
            ModuleJsonExporter.ReadModulesAsync(client, [Formal("/P/A"), Formal("/P/B")]));
        Assert.Equal(["/P/A"], client.Read);
    }

    [Fact]
    public async Task ReadModules_HonoursCancellation()
    {
        var client = new FakeClient(Content);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ModuleJsonExporter.ReadModulesAsync(client, [Formal("/P/A")], cancellationToken: cts.Token));
        Assert.Empty(client.Read);
    }

    [Fact]
    public void ToJson_ProducesExpectedShape()
    {
        var results = new[]
        {
            new ModuleExportResult(Formal("/P/SRS"), Content("/P/SRS"), null),
            new ModuleExportResult(Formal("/P/Locked"), null,
                new DoorsException(DoorsErrorKind.ModuleNotAccessible, "Module '/P/Locked' could not be opened.")),
        };

        var json = ModuleJsonExporter.ToJson(results, new DoorsSessionInfo("jdoe", "ProdDB", "9.7"),
            new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.FromHours(3)));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("2026-11-12T10:00:00+03:00", root.GetProperty("exportedAt").GetString());
        Assert.Equal("ProdDB", root.GetProperty("database").GetString());
        Assert.Equal("jdoe", root.GetProperty("exportedBy").GetString());

        var modules = root.GetProperty("modules");
        Assert.Equal(2, modules.GetArrayLength());

        var srs = modules[0];
        Assert.Equal("/P/SRS", srs.GetProperty("fullName").GetString());
        Assert.Equal(2, srs.GetProperty("objectCount").GetInt32());
        Assert.False(srs.TryGetProperty("error", out _));
        Assert.Equal("Priorität", srs.GetProperty("attributeDefinitions")[0].GetProperty("name").GetString());

        var objects = srs.GetProperty("objects");
        Assert.False(objects[0].TryGetProperty("parentAbsoluteNumber", out _));
        Assert.Equal(1, objects[1].GetProperty("parentAbsoluteNumber").GetInt32());
        Assert.Equal("Größe \"5 µm\" bei 20 °C", objects[1].GetProperty("text").GetString());
        // Attribute names are kept exactly as in DOORS (not camelCased).
        Assert.Equal("Höchste", objects[0].GetProperty("attributes").GetProperty("Priorität").GetString());

        var locked = modules[1];
        Assert.Contains("could not be opened", locked.GetProperty("error").GetString());
        Assert.False(locked.TryGetProperty("objects", out _));

        // Non-ASCII text is written as-is, not as \u escapes.
        Assert.Contains("Höchste", json);
        Assert.Contains("5 µm", json);
    }

    [Fact]
    public async Task DemoClient_ReadsFormalModules_AndRejectsOthers()
    {
        using var client = new DemoDoorsClient();
        await client.ConnectAsync();

        var content = await client.ReadModuleAsync("/Vehicle Project/01 Requirements/Software Requirements");
        var ex = await Assert.ThrowsAsync<DoorsException>(() =>
            client.ReadModuleAsync("/Vehicle Project/Derivation Links"));

        Assert.Equal(15, content.Objects.Count);
        Assert.Equal("SRS-2", content.Objects[1].Identifier);
        Assert.Equal(1, content.Objects[1].ParentAbsoluteNumber);
        Assert.Equal(DoorsErrorKind.ModuleNotAccessible, ex.Kind);
    }

    /// <summary>Progress&lt;T&gt; posts asynchronously; tests need the callbacks to run inline.</summary>
    private sealed class SynchronousProgress(Action<ModuleExportProgress> report) : IProgress<ModuleExportProgress>
    {
        public void Report(ModuleExportProgress value) => report(value);
    }
}
