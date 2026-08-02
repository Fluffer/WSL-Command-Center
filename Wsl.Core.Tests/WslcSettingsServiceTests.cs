using System.IO;
using Wsl.Core.Containers;
using Xunit;

namespace Wsl.Core.Tests;

public class WslcSettingsServiceTests
{
    private const string DefaultContent =
        "# wslc user settings\r\n" +
        "# https://aka.ms/wslc-settings\r\n" +
        "# All settings support string value \"default\" which uses built-in defaults.\r\n" +
        "\r\n" +
        "session:\r\n" +
        "  # Number of virtual CPUs allocated to the session (e.g. 4 default: all available CPUs)\r\n" +
        "  # cpuCount: default\r\n" +
        "\r\n" +
        "  # Memory limit for the session (e.g. 2GB default: half of available memory)\r\n" +
        "  # memorySize: default\r\n" +
        "\r\n" +
        "  # Maximum disk image size (e.g. 500GB default: 1TB)\r\n" +
        "  # maxStorageSize: default\r\n" +
        "\r\n" +
        "  # Default host address that published ports bind to when 'container run -p' is\r\n" +
        "  # used without an explicit address (default: 127.0.0.1)\r\n" +
        "  # defaultBindingAddress: default\r\n" +
        "\r\n" +
        "# Credential storage backend: \"wincred\" or \"file\" (default: wincred)\r\n" +
        "# credentialStore: wincred\r\n";

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "wslc_settings_" + Guid.NewGuid().ToString("N"), "settings.yaml");

    private static WslcSettingsService MakeService(string path) => new(() => path);

    private static void DeleteTempDir(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // ── ReadAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_MissingFile_ReturnsAllUnset_NoError()
    {
        var path = TempPath();
        var svc = MakeService(path);

        var result = await svc.ReadAsync();

        Assert.False(result.FileExists);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.Settings.CpuCount);
        Assert.Null(result.Settings.MemorySize);
        Assert.Null(result.Settings.MaxStorageSize);
        Assert.Null(result.Settings.DefaultBindingAddress);
        Assert.Equal(CredentialStoreKind.Unset, result.Settings.CredentialStore);
    }

    [Fact]
    public async Task ReadAsync_DefaultTemplate_AllUnset()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, DefaultContent);
        try
        {
            var result = await MakeService(path).ReadAsync();

            Assert.True(result.FileExists);
            Assert.Null(result.Settings.CpuCount);
            Assert.Null(result.Settings.MemorySize);
            Assert.Null(result.Settings.MaxStorageSize);
            Assert.Null(result.Settings.DefaultBindingAddress);
            Assert.Equal(CredentialStoreKind.Unset, result.Settings.CredentialStore);
        }
        finally { DeleteTempDir(path); }
    }

    [Fact]
    public async Task ReadAsync_ValuesSet_ParsesThem()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = DefaultContent
            .Replace("  # cpuCount: default", "  cpuCount: 4")
            .Replace("  # memorySize: default", "  memorySize: 2GB")
            .Replace("  # maxStorageSize: default", "  maxStorageSize: 500GB")
            .Replace("  # defaultBindingAddress: default", "  defaultBindingAddress: 192.168.1.1")
            .Replace("# credentialStore: wincred", "credentialStore: file");
        await File.WriteAllTextAsync(path, content);
        try
        {
            var result = await MakeService(path).ReadAsync();

            Assert.Equal("4", result.Settings.CpuCount);
            Assert.Equal("2GB", result.Settings.MemorySize);
            Assert.Equal("500GB", result.Settings.MaxStorageSize);
            Assert.Equal("192.168.1.1", result.Settings.DefaultBindingAddress);
            Assert.Equal(CredentialStoreKind.File, result.Settings.CredentialStore);
        }
        finally { DeleteTempDir(path); }
    }

    [Fact]
    public async Task ReadAsync_ExplicitDefaultLiteral_IsDistinctFromUnset()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = DefaultContent.Replace("  # cpuCount: default", "  cpuCount: default");
        await File.WriteAllTextAsync(path, content);
        try
        {
            var result = await MakeService(path).ReadAsync();
            Assert.Equal("default", result.Settings.CpuCount);
        }
        finally { DeleteTempDir(path); }
    }

    // ── WriteAsync: missing file ────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_MissingFile_CreatesFromTemplate_ThenApplies()
    {
        var path = TempPath();
        try
        {
            var svc = MakeService(path);
            var result = await svc.WriteAsync(new WslcSettings { CpuCount = "4" });

            Assert.True(result.Success);
            var written = await File.ReadAllTextAsync(path);
            Assert.Contains("cpuCount: 4", written);
            Assert.DoesNotContain("# cpuCount:", written);
            // Untouched keys remain commented, from the template.
            Assert.Contains("# memorySize: default", written);
            Assert.Contains("# credentialStore: wincred", written);
        }
        finally { DeleteTempDir(path); }
    }

    // ── WriteAsync: uncomment in place ──────────────────────────────────────

    [Fact]
    public async Task WriteAsync_SetValue_UncommentsInPlace_PreservesSurroundingComments()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, DefaultContent);
        try
        {
            var svc = MakeService(path);
            await svc.WriteAsync(new WslcSettings { MemorySize = "2GB" });

            var written = await File.ReadAllTextAsync(path);
            Assert.Contains("  memorySize: 2GB\r\n", written);
            Assert.DoesNotContain("# memorySize:", written);
            // The explanatory comment directly above stays put.
            Assert.Contains("  # Memory limit for the session (e.g. 2GB default: half of available memory)\r\n  memorySize: 2GB", written);
            // Unrelated keys and their comments untouched.
            Assert.Contains("  # cpuCount: default", written);
            Assert.Contains("# credentialStore: wincred", written);
        }
        finally { DeleteTempDir(path); }
    }

    [Fact]
    public async Task WriteAsync_SetCredentialStore_UncommentsTopLevelKey()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, DefaultContent);
        try
        {
            await MakeService(path).WriteAsync(new WslcSettings { CredentialStore = CredentialStoreKind.File });

            var written = await File.ReadAllTextAsync(path);
            Assert.Contains("credentialStore: file\r\n", written);
            Assert.DoesNotContain("# credentialStore:", written);
        }
        finally { DeleteTempDir(path); }
    }

    // ── WriteAsync: clearing re-comments ─────────────────────────────────────

    [Fact]
    public async Task WriteAsync_ClearValue_ReCommentsRatherThanDeletes()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = DefaultContent.Replace("  # cpuCount: default", "  cpuCount: 8");
        await File.WriteAllTextAsync(path, content);
        try
        {
            await MakeService(path).WriteAsync(new WslcSettings { CpuCount = null });

            var written = await File.ReadAllTextAsync(path);
            Assert.Contains("  # cpuCount: 8\r\n", written); // re-commented, value preserved verbatim
            Assert.DoesNotContain("cpuCount: default", written); // not rewritten to a fresh template line
        }
        finally { DeleteTempDir(path); }
    }

    // ── Round trip ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RoundTrip_SetThenRead_ReflectsWrittenValues()
    {
        var path = TempPath();
        try
        {
            var svc = MakeService(path);
            await svc.WriteAsync(new WslcSettings
            {
                CpuCount = "4",
                MemorySize = "2GB",
                MaxStorageSize = "500GB",
                DefaultBindingAddress = "127.0.0.1",
                CredentialStore = CredentialStoreKind.Wincred,
            });

            var result = await svc.ReadAsync();

            Assert.Equal("4", result.Settings.CpuCount);
            Assert.Equal("2GB", result.Settings.MemorySize);
            Assert.Equal("500GB", result.Settings.MaxStorageSize);
            Assert.Equal("127.0.0.1", result.Settings.DefaultBindingAddress);
            Assert.Equal(CredentialStoreKind.Wincred, result.Settings.CredentialStore);
        }
        finally { DeleteTempDir(path); }
    }

    [Fact]
    public async Task RoundTrip_ReadModifyWrite_LeavesUntouchedKeysUnset()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, DefaultContent);
        try
        {
            var svc = MakeService(path);
            var read = await svc.ReadAsync();
            read.Settings.CpuCount = "6";
            await svc.WriteAsync(read.Settings);

            var reread = await svc.ReadAsync();
            Assert.Equal("6", reread.Settings.CpuCount);
            Assert.Null(reread.Settings.MemorySize);
            Assert.Equal(CredentialStoreKind.Unset, reread.Settings.CredentialStore);
        }
        finally { DeleteTempDir(path); }
    }

    // ── CRLF preservation ────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_PreservesCrlfLineEndings()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, DefaultContent); // already CRLF
        try
        {
            await MakeService(path).WriteAsync(new WslcSettings { CpuCount = "4" });
            var written = await File.ReadAllTextAsync(path);

            Assert.Contains("\r\n", written);
            Assert.DoesNotContain("\n\n", written.Replace("\r\n", "")); // no bare LF introduced
        }
        finally { DeleteTempDir(path); }
    }

    [Fact]
    public async Task WriteAsync_PreservesLfLineEndings()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lfContent = DefaultContent.Replace("\r\n", "\n");
        await File.WriteAllTextAsync(path, lfContent);
        try
        {
            await MakeService(path).WriteAsync(new WslcSettings { CpuCount = "4" });
            var written = await File.ReadAllTextAsync(path);

            Assert.DoesNotContain("\r\n", written);
            Assert.Contains("cpuCount: 4\n", written);
        }
        finally { DeleteTempDir(path); }
    }

    // ── Validation ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("4", true)]
    [InlineData("1", true)]
    [InlineData("default", true)]
    [InlineData("DEFAULT", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    [InlineData("4.5", false)]
    public void ValidateCpuCount_Cases(string value, bool expectedValid)
    {
        Assert.Equal(expectedValid, WslcSettingsService.ValidateCpuCount(value) is null);
    }

    [Theory]
    [InlineData("2GB", true)]
    [InlineData("512MB", true)]
    [InlineData("1TB", true)]
    [InlineData("500gb", true)]
    [InlineData("default", true)]
    [InlineData("2", false)]
    [InlineData("2GBs", false)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    public void ValidateMemorySize_Cases(string value, bool expectedValid)
    {
        Assert.Equal(expectedValid, WslcSettingsService.ValidateMemorySize(value) is null);
    }

    [Theory]
    [InlineData("500GB", true)]
    [InlineData("1TB", true)]
    [InlineData("default", true)]
    [InlineData("500", false)]
    [InlineData("", false)]
    public void ValidateMaxStorageSize_Cases(string value, bool expectedValid)
    {
        Assert.Equal(expectedValid, WslcSettingsService.ValidateMaxStorageSize(value) is null);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("::1", true)]
    [InlineData("default", true)]
    [InlineData("not-an-ip", false)]
    [InlineData("", false)]
    [InlineData("999.999.999.999", false)]
    public void ValidateDefaultBindingAddress_Cases(string value, bool expectedValid)
    {
        Assert.Equal(expectedValid, WslcSettingsService.ValidateDefaultBindingAddress(value) is null);
    }
}
