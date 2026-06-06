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
}
