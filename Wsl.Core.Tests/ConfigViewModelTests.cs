using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class ConfigViewModelTests
{
    private static string GlobalIni =
        "[wsl2]\nmemory=8GB\nprocessors=4\ncustomKey=keep\n";

    private static WslConfigService MakeConfigService(FakeProcessRunner runner, string globalFile)
        => new(runner, globalPathProvider: () => globalFile);

    [Fact]
    public async Task LoadGlobal_reads_typed_fields(/* uses temp file */)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"wslcfg-{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(tmp, GlobalIni);
        try
        {
            var runner = new FakeProcessRunner();
            var vm = new ConfigViewModel(MakeConfigService(runner, tmp), new WslDistroService(runner));

            await vm.LoadGlobalAsync();

            Assert.Equal("8GB", vm.Memory);
            Assert.Equal("4", vm.Processors);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task SaveGlobal_preserves_unknown_key()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"wslcfg-{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(tmp, GlobalIni);
        try
        {
            var runner = new FakeProcessRunner();
            var vm = new ConfigViewModel(MakeConfigService(runner, tmp), new WslDistroService(runner));
            await vm.LoadGlobalAsync();
            vm.Memory = "16GB";

            await vm.SaveGlobalAsync();

            var written = await File.ReadAllTextAsync(tmp);
            Assert.Contains("memory=16GB", written);
            Assert.Contains("customKey=keep", written);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task LoadDistro_reads_via_cat()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "[user]\ndefault=peter\n[boot]\nsystemd=true\n");
        var vm = new ConfigViewModel(MakeConfigService(runner, "unused"), new WslDistroService(runner)) { SelectedDistro = "Ubuntu" };

        await vm.LoadDistroAsync();

        Assert.Equal("peter", vm.DefaultUser);
        Assert.True(vm.Systemd);
        Assert.Equal(new[] { "-d", "Ubuntu", "-u", "root", "cat", "/etc/wsl.conf" }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task LoadDistros_populates_from_list()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0,
            "  NAME      STATE     VERSION\r\n" +
            "* Ubuntu    Stopped   2\r\n" +
            "  Debian    Running   2\r\n");
        var vm = new ConfigViewModel(MakeConfigService(runner, "unused"), new WslDistroService(runner));

        await vm.LoadDistrosAsync();

        Assert.Equal(new[] { "Ubuntu", "Debian" }, vm.Distros);
    }

    [Fact]
    public async Task SaveGlobal_PersistsGuiApplicationsAndAutoMemoryReclaim()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"wslcfg-{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(tmp, "[wsl2]\nmemory=4GB\n");
        try
        {
            var runner = new FakeProcessRunner();
            var cfgService = MakeConfigService(runner, tmp);
            var vm = new ConfigViewModel(cfgService, new WslDistroService(runner));
            await vm.LoadGlobalAsync();
            vm.GuiApplications = false;
            vm.AutoMemoryReclaim = "gradual";
            await vm.SaveGlobalAsync();

            var saved = await cfgService.ReadGlobalAsync();
            Assert.Equal(false, saved.GuiApplications);
            Assert.Equal("gradual", saved.AutoMemoryReclaim);
        }
        finally { File.Delete(tmp); }
    }
}
