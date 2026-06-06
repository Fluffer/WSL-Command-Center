using System.IO;
using Wsl.Core.Settings;
using Xunit;

namespace Wsl.Core.Tests;

public class ThemeServiceTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"wsl-cc-{Path.GetRandomFileName()}.json");

    [Fact]
    public void Load_WhenFileMissing_ReturnsSystemDefault()
    {
        var path = TempFile();
        var svc = new ThemeService(path);

        Assert.Equal("System", svc.Load().Theme);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsTheme()
    {
        var path = TempFile();
        try
        {
            var svc = new ThemeService(path);
            svc.Save(new AppSettings { Theme = "Dark" });

            Assert.Equal("Dark", new ThemeService(path).Load().Theme);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_WhenFileCorrupt_ReturnsSystemDefault()
    {
        var path = TempFile();
        try
        {
            File.WriteAllText(path, "{ not valid json");
            Assert.Equal("System", new ThemeService(path).Load().Theme);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsAccentAndFont()
    {
        var path = TempFile();
        try
        {
            new ThemeService(path).Save(new AppSettings { Accent = "Green", Font = "Cascadia Mono" });
            var loaded = new ThemeService(path).Load();
            Assert.Equal("Green", loaded.Accent);
            Assert.Equal("Cascadia Mono", loaded.Font);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_DefaultsAccentAndFont()
    {
        var svc = new ThemeService(TempFile());
        var s = svc.Load();
        Assert.Equal("Default", s.Accent);
        Assert.Equal("Segoe UI Variable", s.Font);
    }

    [Fact]
    public void Save_CreatesParentDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var path = Path.Combine(dir, "settings.json");
        try
        {
            new ThemeService(path).Save(new AppSettings { Theme = "Light" });
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
