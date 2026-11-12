using DoorsHandler.Core;

namespace DoorsHandler.Core.Tests;

public class DxlDoorsClientTests
{
    private sealed class FakeExecutor(params string[] responses) : IDxlExecutor
    {
        private readonly Queue<string> _responses = new(responses);
        public List<string> Scripts { get; } = [];
        public bool Disposed { get; private set; }

        public Task<string> ExecuteAsync(string dxl, CancellationToken cancellationToken = default)
        {
            Scripts.Add(dxl);
            return Task.FromResult(_responses.Dequeue());
        }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task ConnectThenList_RunsBothScripts()
    {
        var executor = new FakeExecutor(
            "OK\nS\tuser\tdb\t9.7\n",
            "OK\nI\tFormal\tSRS\t/P/SRS\t1\t\n");
        using var client = new DxlDoorsClient(executor);

        var session = await client.ConnectAsync();
        var listing = await client.ListItemsAsync("/P", recursive: true);

        Assert.True(client.IsConnected);
        Assert.Equal("user", session.UserName);
        Assert.Equal("SRS", Assert.Single(listing.Items).Name);
        Assert.Contains("\"/P\"", executor.Scripts[1]);
    }

    [Fact]
    public async Task List_BeforeConnect_Throws()
    {
        using var client = new DxlDoorsClient(new FakeExecutor());

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListItemsAsync("/", true));
    }

    [Fact]
    public void Dispose_DisposesExecutor()
    {
        var executor = new FakeExecutor();
        new DxlDoorsClient(executor).Dispose();

        Assert.True(executor.Disposed);
    }
}

public class DemoDoorsClientTests
{
    [Fact]
    public async Task Recursive_ListsEverythingUnderRoot()
    {
        using var client = new DemoDoorsClient();
        await client.ConnectAsync();

        var all = await client.ListItemsAsync("/", recursive: true);
        var project = await client.ListItemsAsync("/Uydu Projesi/", recursive: true);

        Assert.True(all.Items.Count(i => i.IsModule) > 5);
        Assert.All(project.Items, i => Assert.StartsWith("/Uydu Projesi/", i.FullName));
        Assert.Equal(2, project.Items.Count);
    }

    [Fact]
    public async Task NonRecursive_ListsDirectChildrenOnly()
    {
        using var client = new DemoDoorsClient();
        await client.ConnectAsync();

        var listing = await client.ListItemsAsync("/", recursive: false);

        Assert.All(listing.Items, i => Assert.Equal("/", i.ParentPath));
    }

    [Fact]
    public async Task UnknownRoot_Throws()
    {
        using var client = new DemoDoorsClient();
        await client.ConnectAsync();

        await Assert.ThrowsAsync<DoorsException>(() => client.ListItemsAsync("/Yok", recursive: true));
    }
}

public class CsvExporterTests
{
    [Fact]
    public void ToCsv_QuotesValuesThatNeedIt()
    {
        var items = new[]
        {
            new DoorsItem(DoorsItemType.Formal, "SRS; v2", "/P/SRS; v2", "1", "say \"hi\""),
        };

        var lines = CsvExporter.ToCsv(items).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Tür;Ad;Tam Yol;Üst Klasör;Unique ID;Açıklama", lines[0]);
        Assert.Equal("Formal;\"SRS; v2\";\"/P/SRS; v2\";/P;1;\"say \"\"hi\"\"\"", lines[1]);
    }
}
