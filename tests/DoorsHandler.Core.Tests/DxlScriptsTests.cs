using DoorsHandler.Core.Dxl;

namespace DoorsHandler.Core.Tests;

public class DxlScriptsTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("  /  ", "/")]
    [InlineData("Proj", "/Proj")]
    [InlineData("/Proj/Reqs/", "/Proj/Reqs")]
    [InlineData(@"\Proj\Reqs", "/Proj/Reqs")]
    public void NormalizePath_ProducesDoorsStylePaths(string? input, string expected)
    {
        Assert.Equal(expected, DxlScripts.NormalizePath(input));
    }

    [Fact]
    public void ToDxlStringLiteral_EscapesSpecialCharacters()
    {
        Assert.Equal("\"a\\\"b\\\\c\\nd\"", DxlScripts.ToDxlStringLiteral("a\"b\\c\nd"));
    }

    [Fact]
    public void ListItems_EmbedsRootPathSafely()
    {
        var script = DxlScripts.ListItems("/Proj \"X\"", recursive: false);

        Assert.Contains("string dhRootPath = \"/Proj \\\"X\\\"\"", script);
        Assert.Contains("bool dhRecursive = false", script);
        Assert.Contains("oleSetResult", script);
    }

    [Fact]
    public void ReadModule_EmbedsModulePathSafely_AndClosesOnlyModulesItOpened()
    {
        var script = DxlScripts.ReadModule("Proj/\"Q\" Reqs/");

        Assert.Contains("string dhModPath = \"/Proj/\\\"Q\\\" Reqs\"", script);
        Assert.Contains("read(dhModPath, false)", script);
        Assert.Contains("if (!dhWasOpen)", script);
        Assert.Contains("for o in entire dhMod do", script);
        Assert.Contains("if (isDeleted(o)) continue", script);
    }

    [Fact]
    public void Scripts_HaveBalancedBracesAndParentheses()
    {
        foreach (var script in new[] { DxlScripts.SessionInfo(), DxlScripts.ListItems("/", true), DxlScripts.ReadModule("/P/SRS") })
        {
            var code = StripStringAndCharLiterals(script);
            Assert.Equal(code.Count(c => c == '{'), code.Count(c => c == '}'));
            Assert.Equal(code.Count(c => c == '('), code.Count(c => c == ')'));
        }
    }

    [Fact]
    public void Scripts_AreAscii_SoTheyWorkWithAnyDoorsCodePage()
    {
        foreach (var script in new[] { DxlScripts.SessionInfo(), DxlScripts.ListItems("/", true), DxlScripts.ReadModule("/P/SRS") })
        {
            Assert.All(script, c => Assert.True(c < 128, $"Non-ASCII character '{c}' in DXL script"));
        }
    }

    private static string StripStringAndCharLiterals(string code)
    {
        var sb = new System.Text.StringBuilder();
        char? quote = null;
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (quote is null)
            {
                if (c is '"' or '\'') quote = c;
                else sb.Append(c);
            }
            else if (c == '\\') i++;
            else if (c == quote) quote = null;
        }

        return sb.ToString();
    }
}
