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

    [Fact]
    public async Task TrimAsync_RunsFstrimAsRoot_ReturnsOutput()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "/: 2.5 GiB (2684354560 bytes) trimmed\n");
        var svc = new WslDiskService(runner);

        var result = await svc.TrimAsync("Ubuntu");

        Assert.Equal(new[] { "-d", "Ubuntu", "-u", "root", "--", "fstrim", "-v", "/" }, runner.AllArgs[0]);
        Assert.Contains("trimmed", result);
    }

    [Fact]
    public async Task TrimAsync_MissingFstrim_ReturnsInformationalNotThrow()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(127, "", "/bin/sh: 1: fstrim: not found");
        var svc = new WslDiskService(runner);

        var result = await svc.TrimAsync("Ubuntu");

        Assert.Contains("not installed", result);
    }

    [Fact]
    public async Task TrimAsync_DiscardUnsupported_ReturnsInformational()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "fstrim: /: the discard operation is not supported");
        var svc = new WslDiskService(runner);

        var result = await svc.TrimAsync("Ubuntu");

        Assert.Contains("does not support", result);
    }

    [Fact]
    public async Task TrimAsync_UnexpectedFailure_Throws()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "fstrim: /: FITRIM ioctl failed: Operation not permitted");
        var svc = new WslDiskService(runner);

        await Assert.ThrowsAsync<WslException>(() => svc.TrimAsync("Ubuntu"));
    }
}
