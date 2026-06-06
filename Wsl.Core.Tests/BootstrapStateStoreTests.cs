using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class BootstrapStateStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"wslcc-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Default_is_done_when_no_file()
    {
        var store = new BootstrapStateStore(_path);
        Assert.Equal(BootstrapStep.Done, await store.ReadAsync());
    }

    [Fact]
    public async Task Roundtrips_step()
    {
        var store = new BootstrapStateStore(_path);
        await store.WriteAsync(BootstrapStep.RebootPending);
        Assert.Equal(BootstrapStep.RebootPending, await store.ReadAsync());
    }

    [Fact]
    public async Task Clear_resets_to_done()
    {
        var store = new BootstrapStateStore(_path);
        await store.WriteAsync(BootstrapStep.InstallKernel);
        await store.ClearAsync();
        Assert.Equal(BootstrapStep.Done, await store.ReadAsync());
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
}
