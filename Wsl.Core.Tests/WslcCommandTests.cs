using Wsl.Core.Containers;
using Xunit;

namespace Wsl.Core.Tests;

public class WslcCommandTests
{
    [Theory]
    [InlineData("ps", true)]
    [InlineData("inspect", true)]
    [InlineData("logs", true)]
    [InlineData("--version", true)]
    [InlineData("rm", false)]
    [InlineData("kill", false)]
    [InlineData("stop", false)]
    [InlineData("create", false)]
    public void IsReadOnly_classifies_leading_verb(string input, bool expected)
        => Assert.Equal(expected, WslcCommand.IsReadOnly(input));

    [Fact]
    public void IsReadOnly_is_case_insensitive()
        => Assert.True(WslcCommand.IsReadOnly("PS"));

    [Fact]
    public void IsReadOnly_false_for_blank()
        => Assert.False(WslcCommand.IsReadOnly("   "));

    [Fact]
    public void Tokenize_splits_on_whitespace()
        => Assert.Equal(new[] { "ps", "-a" }, WslcCommand.Tokenize("ps  -a"));

    [Fact]
    public void Tokenize_keeps_quoted_groups_together()
        => Assert.Equal(new[] { "inspect", "my container" }, WslcCommand.Tokenize("inspect \"my container\""));

    [Fact]
    public void Tokenize_blank_is_empty()
        => Assert.Empty(WslcCommand.Tokenize("   "));

    [Fact]
    public void FirstVerb_returns_leading_token()
        => Assert.Equal("logs", WslcCommand.FirstVerb("logs web --follow"));

    [Fact]
    public void FirstVerb_blank_is_empty_string()
        => Assert.Equal("", WslcCommand.FirstVerb(""));
}
