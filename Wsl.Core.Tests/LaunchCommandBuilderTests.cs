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
    public void Shell_command_with_quotes_passes_as_single_argument()
    {
        var opts = new LaunchOptions { Command = "echo \"a b\"" };
        Assert.Equal(new[] { "-d", "Ubuntu", "--", "echo \"a b\"" },
            LaunchCommandBuilder.Build("Ubuntu", opts));
    }

    [Fact]
    public void Exec_command_tokenizes_respecting_quotes()
    {
        var opts = new LaunchOptions { Command = "python -c \"print('hi')\"", UseExec = true };
        Assert.Equal(new[] { "-d", "Ubuntu", "--exec", "python", "-c", "print('hi')" },
            LaunchCommandBuilder.Build("Ubuntu", opts));
    }

    [Fact]
    public void System_distro_replaces_distro_selection()
    {
        var opts = new LaunchOptions { SystemDistro = true };
        Assert.Equal(new[] { "--system" }, LaunchCommandBuilder.Build("Ubuntu", opts));
    }
}
