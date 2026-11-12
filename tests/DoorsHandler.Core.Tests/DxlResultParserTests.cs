using DoorsHandler.Core;
using DoorsHandler.Core.Dxl;

namespace DoorsHandler.Core.Tests;

public class DxlResultParserTests
{
    [Fact]
    public void ParseListing_ReadsItemsAndWarnings_InAnyOrder()
    {
        const string raw =
            "I\tProject\tProj\t/Proj\t00000001\t\n" +
            "I\tFormal\tSRS\t/Proj/SRS\t00000002\tSoftware reqs\n" +
            "I\tLink\tSatisfies\t/Proj/Satisfies\t00000003\t\n" +
            "W\tFOLDER_INACCESSIBLE\t/Proj/Secret\n" +
            "OK\n";

        var listing = DxlResultParser.ParseListing(raw);

        Assert.Equal(3, listing.Items.Count);
        var srs = listing.Items[1];
        Assert.Equal(DoorsItemType.Formal, srs.Type);
        Assert.Equal("SRS", srs.Name);
        Assert.Equal("/Proj/SRS", srs.FullName);
        Assert.Equal("/Proj", srs.ParentPath);
        Assert.Equal("00000002", srs.UniqueId);
        Assert.Equal("Software reqs", srs.Description);
        Assert.True(srs.IsModule);
        Assert.False(listing.Items[0].IsModule);

        var warning = Assert.Single(listing.Warnings);
        Assert.Contains("/Proj/Secret", warning);
    }

    [Fact]
    public void ParseListing_UnescapesFields()
    {
        const string raw = "OK\r\nI\tFormal\tA\\tB\t/X/A\\tB\t1\tline1\\nline2 \\\\ end\r\n";

        var item = Assert.Single(DxlResultParser.ParseListing(raw).Items);

        Assert.Equal("A\tB", item.Name);
        Assert.Equal("line1\nline2 \\ end", item.Description);
    }

    [Fact]
    public void ParseListing_EmptyFolder_ReturnsNoItems()
    {
        var listing = DxlResultParser.ParseListing("OK\n");

        Assert.Empty(listing.Items);
        Assert.Empty(listing.Warnings);
    }

    [Fact]
    public void ParseListing_ErrorLine_ThrowsWithReadableMessage()
    {
        var ex = Assert.Throws<DoorsException>(() =>
            DxlResultParser.ParseListing("ERR\tROOT_NOT_FOUND\t/Nope\n"));

        Assert.Contains("/Nope", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I\tFormal\tA\t/A\t1\t\n")] // script stopped before writing OK
    public void ParseListing_IncompleteResult_Throws(string? raw)
    {
        Assert.Throws<DoorsException>(() => DxlResultParser.ParseListing(raw));
    }

    [Fact]
    public void ParseListing_UnknownType_IsKeptAsUnknown()
    {
        var item = Assert.Single(DxlResultParser.ParseListing("OK\nI\tSomethingNew\tX\t/X\t9\t\n").Items);

        Assert.Equal(DoorsItemType.Unknown, item.Type);
        Assert.False(item.IsModule);
    }

    [Fact]
    public void ParseSessionInfo_ReadsUserDatabaseAndVersion()
    {
        var info = DxlResultParser.ParseSessionInfo("OK\nS\tjdoe\tProdDB\t9.7.2.3\n");

        Assert.Equal(new DoorsSessionInfo("jdoe", "ProdDB", "9.7.2.3"), info);
    }

    [Fact]
    public void ParseSessionInfo_WithoutSessionLine_Throws()
    {
        Assert.Throws<DoorsException>(() => DxlResultParser.ParseSessionInfo("OK\n"));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData(@"a\\b", @"a\b")]
    [InlineData(@"trailing\", @"trailing\")]
    [InlineData(@"\x", "x")]
    public void Unescape_HandlesEdgeCases(string input, string expected)
    {
        Assert.Equal(expected, DxlResultParser.Unescape(input));
    }
}
