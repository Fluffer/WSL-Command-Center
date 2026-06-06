using System.Threading.Tasks;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslDiskServiceTests
{
    [Fact]
    public async Task OptimizeAsync_TerminatesThenSetsSparse()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");   // terminate
        runner.Enqueue(0, "");   // set-sparse
        var svc = new WslDiskService(runner);

        await svc.OptimizeAsync("Ubuntu");

        Assert.Equal(2, runner.AllArgs.Count);
        Assert.Equal(new[] { "--terminate", "Ubuntu" }, runner.AllArgs[0]);
        Assert.Equal(new[] { "--manage", "Ubuntu", "--set-sparse", "true" }, runner.AllArgs[1]);
    }

    [Fact]
    public async Task OptimizeAsync_ThrowsWhenManageFails()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");                       // terminate ok
        runner.Enqueue(1, "", "sparse not supported"); // set-sparse fails
        var svc = new WslDiskService(runner);

        await Assert.ThrowsAsync<WslException>(() => svc.OptimizeAsync("Ubuntu"));
    }
}
