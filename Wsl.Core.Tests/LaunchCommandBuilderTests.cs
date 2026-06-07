using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class LaunchCommandBuilderTests
{
    [Fact]
    public void Default_launch_only_selects_distro()
        => Assert.Equal(new[] { "-d", "Ubuntu" },
            LaunchCommandBuilder.Build("Ubuntu", new LaunchOptions()));

    [Fact]
    public void All_options_compose_in_canonical_order()
    {
        var opts = new LaunchOptions
        {
            User = "peter",
            WorkingDirectory = "~",
            ShellType = WslShellType.Login,
            Command = "htop",
        };
        Assert.Equal(
            new[] { "-d", "Ubuntu", "--user", "peter", "--cd", "~", "--shell-type", "login", "--", "htop" },
            LaunchCommandBuilder.Build("Ubuntu", opts));
    }

    [Fact]
    public void Exec_uses_exec_flag_instead_of_separator()
    {
        var opts = new LaunchOptions { Command = "htop", UseExec = true };
        Assert.Equal(new[] { "-d", "Ubuntu", "--exec", "htop" },
            LaunchCommandBuilder.Build("Ubuntu", opts));
    }

    [Fact]
    public void System_distro_replaces_distro_selection()
    {
        var opts = new LaunchOptions { SystemDistro = true };
        Assert.Equal(new[] { "--system" }, LaunchCommandBuilder.Build("Ubuntu", opts));
    }
}
