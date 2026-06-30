using System.IO;
using System.Linq;
using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class VirtiofsTests
{
    private static string TempCfg() =>
        Path.Combine(Path.GetTempPath(), "viofs_" + System.Guid.NewGuid().ToString("N") + ".wslconfig");

    // ── WslGlobalConfig round-trip ──────────────────────────────────────────

    [Fact]
    public void Virtiofs_ParsedFromIni()
    {
        var cfg = WslGlobalConfig.FromIni(IniParser.Parse("[wsl2]\nvirtiofs=true\n"));
        Assert.True(cfg.Virtiofs);
    }

    [Fact]
    public void Virtiofs_WrittenToIni_WhenTrue_OmittedWhenNull()
    {
        var on = IniParser.Write(new WslGlobalConfig { Virtiofs = true }.ToIni());
        Assert.Contains("virtiofs=true", on.Replace(" ", ""));

        var off = IniParser.Write(new WslGlobalConfig { Virtiofs = null }.ToIni());
        Assert.DoesNotContain("virtiofs", off);
    }

    // ── ConfigViewModel.ApplyVirtiofsAsync ──────────────────────────────────

    [Fact]
    public async Task ApplyVirtiofs_Enable_WritesFlagAndShutsDown()
    {
        var path = TempCfg();
        try
        {
            var runner = new FakeProcessRunner();
            runner.Enqueue(0, "");  // wsl --shutdown
            var vm = new ConfigViewModel(new WslConfigService(runner, () => path), new WslDistroService(runner));

            await vm.ApplyVirtiofsCommand.ExecuteAsync(true);

            Assert.Contains("virtiofs=true", File.ReadAllText(path).Replace(" ", ""));
            Assert.Contains("--shutdown", runner.AllArgs.Last());
            Assert.True(vm.VirtiofsEnabled);
            Assert.Null(vm.ErrorMessage);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ApplyVirtiofs_ShutdownFails_FlagStillPersisted_WarnsNotErrors()
    {
        var path = TempCfg();
        try
        {
            var runner = new FakeProcessRunner();
            runner.Enqueue(1, "", "shutdown failed");  // wsl --shutdown returns non-zero
            var vm = new ConfigViewModel(new WslConfigService(runner, () => path), new WslDistroService(runner));

            await vm.ApplyVirtiofsCommand.ExecuteAsync(true);

            Assert.Contains("virtiofs=true", File.ReadAllText(path).Replace(" ", ""));  // flag durable
            Assert.True(vm.VirtiofsEnabled);                                            // toggle synced before shutdown
            Assert.Contains("failed", vm.StatusMessage!.ToLowerInvariant());            // warned, not hard error
            Assert.Null(vm.ErrorMessage);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ApplyVirtiofs_Disable_RemovesFlag_PreservesOtherKeys()
    {
        var path = TempCfg();
        File.WriteAllText(path, "[wsl2]\nmemory=4GB\nvirtiofs=true\n");
        try
        {
            var runner = new FakeProcessRunner();
            runner.Enqueue(0, "");  // shutdown
            var vm = new ConfigViewModel(new WslConfigService(runner, () => path), new WslDistroService(runner));

            await vm.ApplyVirtiofsCommand.ExecuteAsync(false);

            var written = File.ReadAllText(path);
            Assert.DoesNotContain("virtiofs", written);
            Assert.Contains("memory=4GB", written.Replace(" ", ""));   // unrelated key preserved
            Assert.False(vm.VirtiofsEnabled);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
