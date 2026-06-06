using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class IniParserTests
{
    private const string Sample =
        "[wsl2]\n" +
        "memory=8GB\n" +
        "processors=4\n" +
        "# a comment\n" +
        "customUnknownKey=keepme\n" +
        "\n" +
        "[experimental]\n" +
        "autoMemoryReclaim=gradual\n";

    [Fact]
    public void Parses_sections_and_keys()
    {
        var ini = IniParser.Parse(Sample);
        Assert.Equal("8GB", ini["wsl2"]["memory"]);
        Assert.Equal("4", ini["wsl2"]["processors"]);
        Assert.Equal("keepme", ini["wsl2"]["customUnknownKey"]);
        Assert.Equal("gradual", ini["experimental"]["autoMemoryReclaim"]);
    }

    [Fact]
    public void Roundtrips_to_text()
    {
        var ini = IniParser.Parse(Sample);
        var text = IniParser.Write(ini);
        var reparsed = IniParser.Parse(text);
        Assert.Equal("keepme", reparsed["wsl2"]["customUnknownKey"]);
        Assert.Equal("gradual", reparsed["experimental"]["autoMemoryReclaim"]);
    }
}
