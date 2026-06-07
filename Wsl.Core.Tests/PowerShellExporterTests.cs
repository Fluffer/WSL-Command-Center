using Wsl.Core;
using Wsl.Core.Scripting;
using Xunit;

namespace Wsl.Core.Tests;

public class PowerShellExporterTests
{
    private readonly PowerShellExporter _x = new();

    [Fact]
    public void Export_Tar_NoSpaces_NoQuotes()
        => Assert.Equal(@"wsl.exe --export Ubuntu C:\b.tar --format tar",
            _x.Export("Ubuntu", @"C:\b.tar", ExportFormat.Tar));

    [Fact]
    public void Export_TarGz_QuotesArgsWithSpaces()
        => Assert.Equal("wsl.exe --export \"My Distro\" \"C:\\my backups\\d.tar\" --format tar.gz",
            _x.Export("My Distro", @"C:\my backups\d.tar", ExportFormat.TarGz));

    [Fact]
    public void Restore_Vhd_IncludesVhdAndVersion()
        => Assert.Equal(@"wsl.exe --import Ubuntu C:\WSL\Ubuntu C:\b.vhdx --vhd --version 2",
            _x.Restore("Ubuntu", @"C:\WSL\Ubuntu", @"C:\b.vhdx", ExportFormat.Vhd, 2));

    [Fact]
    public void Restore_Tar_NoVhdFlag()
        => Assert.Equal(@"wsl.exe --import Ubuntu C:\WSL\Ubuntu C:\b.tar --version 2",
            _x.Restore("Ubuntu", @"C:\WSL\Ubuntu", @"C:\b.tar", ExportFormat.Tar, 2));

    [Fact]
    public void Start_MirrorsServiceBootCommand()
        => Assert.Equal("wsl.exe -d Ubuntu -- true", _x.Start("Ubuntu"));

    [Fact]
    public void Terminate_Command()
        => Assert.Equal("wsl.exe --terminate Ubuntu", _x.Terminate("Ubuntu"));

    [Fact]
    public void SetDefault_Command()
        => Assert.Equal("wsl.exe --set-default Ubuntu", _x.SetDefault("Ubuntu"));

    [Fact]
    public void Unregister_Command()
        => Assert.Equal("wsl.exe --unregister Ubuntu", _x.Unregister("Ubuntu"));

    [Fact]
    public void List_Command()
        => Assert.Equal("wsl.exe --list --verbose", _x.List());

    [Fact]
    public void Optimize_TerminateThenSetSparse()
        => Assert.Equal("wsl.exe --terminate Ubuntu\r\nwsl.exe --manage Ubuntu --set-sparse true",
            _x.Optimize("Ubuntu"));

    [Fact]
    public void Install_NoLaunch_Command()
        => Assert.Equal("wsl.exe --install -d Ubuntu --no-launch", _x.Install("Ubuntu"));

    [Fact]
    public void Install_QuotesNameWithSpaces()
        => Assert.Equal("wsl.exe --install -d \"My Distro\" --no-launch", _x.Install("My Distro"));

    [Fact]
    public void Shutdown_Command()
        => Assert.Equal("wsl.exe --shutdown", _x.Shutdown());

    [Fact]
    public void Launch_DefaultOptions_SelectsDistroOnly()
        => Assert.Equal("wsl.exe -d Ubuntu", _x.Launch("Ubuntu", new LaunchOptions()));

    [Fact]
    public void Launch_QuotesArgsWithSpaces()
        => Assert.Equal("wsl.exe -d \"My Distro\" --cd \"/mnt/c/my dir\"",
            _x.Launch("My Distro", new LaunchOptions { WorkingDirectory = "/mnt/c/my dir" }));

    [Fact]
    public void Launch_AllOptions_MirrorsBuilderOrder()
        => Assert.Equal("wsl.exe -d Ubuntu --user peter --cd ~ --shell-type login -- htop",
            _x.Launch("Ubuntu", new LaunchOptions
            {
                User = "peter",
                WorkingDirectory = "~",
                ShellType = WslShellType.Login,
                Command = "htop",
            }));

    [Fact]
    public void EnableFeatures_MirrorsBrokerSequence()
        => Assert.Equal(
            "dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart\r\n" +
            "dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart\r\n" +
            "wsl.exe --update\r\n" +
            "wsl.exe --set-default-version 2",
            _x.EnableFeatures());

    [Fact]
    public void EnableFeatures_WithPreRelease_MirrorsBrokerSequence()
        => Assert.Equal(
            "dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart\r\n" +
            "dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart\r\n" +
            "wsl.exe --update --pre-release\r\n" +
            "wsl.exe --set-default-version 2",
            _x.EnableFeatures(preRelease: true));
}
