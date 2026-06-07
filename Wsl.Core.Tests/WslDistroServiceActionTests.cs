using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslDistroServiceActionTests
{
    private static (WslDistroService svc, FakeProcessRunner runner) Make()
    {
        var runner = new FakeProcessRunner();
        return (new WslDistroService(runner), runner);
    }

    [Fact]
    public async Task Start_runs_true_in_distro()
    {
        var (svc, runner) = Make();
        await svc.StartAsync("Ubuntu");
        Assert.Equal(new[] { "-d", "Ubuntu", "--", "true" }, runner.LastArgs);
    }

    [Fact]
    public async Task Terminate_passes_distro()
    {
        var (svc, runner) = Make();
        await svc.TerminateAsync("Ubuntu");
        Assert.Equal(new[] { "--terminate", "Ubuntu" }, runner.LastArgs);
    }

    [Fact]
    public async Task SetDefault_passes_distro()
    {
        var (svc, runner) = Make();
        await svc.SetDefaultAsync("Ubuntu");
        Assert.Equal(new[] { "--set-default", "Ubuntu" }, runner.LastArgs);
    }

    [Fact]
    public async Task SetVersion_passes_distro_and_version()
    {
        var (svc, runner) = Make();
        await svc.SetVersionAsync("Ubuntu", 2);
        Assert.Equal(new[] { "--set-version", "Ubuntu", "2" }, runner.LastArgs);
    }

    [Fact]
    public async Task Unregister_passes_distro()
    {
        var (svc, runner) = Make();
        await svc.UnregisterAsync("Ubuntu");
        Assert.Equal(new[] { "--unregister", "Ubuntu" }, runner.LastArgs);
    }

    [Fact]
    public async Task Failure_throws_WslException()
    {
        var (svc, runner) = Make();
        runner.Enqueue(1, "", "There is no distribution with the supplied name.");
        var ex = await Assert.ThrowsAsync<WslException>(() => svc.TerminateAsync("Ghost"));
        Assert.Equal(WslErrorKind.DistroNotFound, ex.Kind);
    }

    [Fact]
    public async Task SetDefaultUserAsync_invokes_manage_set_default_user()
    {
        var (svc, runner) = Make();
        runner.Enqueue(0, "");

        await svc.SetDefaultUserAsync("Ubuntu", "peter");

        Assert.Equal(new[] { "--manage", "Ubuntu", "--set-default-user", "peter" }, runner.LastArgs);
    }

    [Fact]
    public async Task ListUsersAsync_parses_passwd_users_with_login_shells()
    {
        var (svc, runner) = Make();
        runner.Enqueue(0,
            "root:x:0:0:root:/root:/bin/bash\n" +
            "daemon:x:1:1:daemon:/usr/sbin:/usr/sbin/nologin\n" +
            "peter:x:1000:1000::/home/peter:/bin/bash\n");

        var users = await svc.ListUsersAsync("Ubuntu");

        Assert.Equal(new[] { "root", "peter" }, users);
        Assert.Equal(new[] { "-d", "Ubuntu", "-u", "root", "--", "getent", "passwd" }, runner.LastArgs);
    }

    [Fact]
    public async Task ListUsersAsync_failure_throws_WslException()
    {
        var (svc, runner) = Make();
        runner.Enqueue(1, "", "There is no distribution with the supplied name.");
        await Assert.ThrowsAsync<WslException>(() => svc.ListUsersAsync("Ghost"));
    }
}
