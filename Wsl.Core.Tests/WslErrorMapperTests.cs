using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslErrorMapperTests
{
    [Theory]
    [InlineData("There is no distribution with the supplied name.", WslErrorKind.DistroNotFound)]
    [InlineData("A distribution with the supplied name already exists.", WslErrorKind.AlreadyExists)]
    [InlineData("Access is denied.", WslErrorKind.AccessDenied)]
    [InlineData("The Windows Subsystem for Linux is not installed.", WslErrorKind.NotInstalled)]
    [InlineData("The file or directory is corrupted and unreadable.", WslErrorKind.InvalidArchive)]
    [InlineData("some other unexpected failure", WslErrorKind.CommandFailed)]
    public void Maps_stderr_to_kind(string stderr, WslErrorKind expected)
    {
        Assert.Equal(expected, WslErrorMapper.Classify(exitCode: 1, stderr));
    }

    [Fact]
    public void Zero_exit_is_command_failed_when_forced()
    {
        // Mapper only classifies; callers decide when to throw.
        Assert.Equal(WslErrorKind.CommandFailed, WslErrorMapper.Classify(0, ""));
    }
}
