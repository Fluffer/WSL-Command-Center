using Wsl.Core;
using Xunit;

namespace Wsl.Live.Tests;

[Trait("Category", "LiveWsl")]
public class RealProcessRunnerTests
{
    [Fact]
    public async Task Real_list_decodes_and_parses()
    {
        var svc = new WslDistroService(new RealProcessRunner());
        var distros = await svc.ListAsync();
        // On a machine with WSL installed this should not throw and should decode cleanly
        // (no NUL artifacts, real names). We assert the call succeeds and names are non-empty.
        Assert.All(distros, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
    }

    [Fact]
    public async Task Real_version_succeeds()
    {
        var result = await new RealProcessRunner().RunAsync("wsl.exe", new[] { "--version" });
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("WSL", result.StdOut);
    }
}
