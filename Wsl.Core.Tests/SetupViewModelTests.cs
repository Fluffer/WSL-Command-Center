using Wsl.App.Logic.ViewModels;
using Wsl.Contracts;
using Wsl.Core;
using Wsl.Core.Ipc;
using Xunit;

namespace Wsl.Core.Tests;

public sealed class FakeBrokerClient : IBrokerClient
{
    private readonly Queue<BrokerResponse> _responses = new();
    public List<BrokerRequest> Sent { get; } = new();
    public void Enqueue(BrokerResponse r) => _responses.Enqueue(r);

    public Task<BrokerResponse> SendAsync(BrokerRequest request, CancellationToken ct = default)
    {
        Sent.Add(request);
        return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : new BrokerResponse(true));
    }
}

public class SetupViewModelTests : IDisposable
{
    private readonly string _statePath =
        Path.Combine(Path.GetTempPath(), $"wslcc-setup-{Guid.NewGuid():N}.json");

    private SetupViewModel Make(FakeBrokerClient client)
        => new(client, new BootstrapStateStore(_statePath));

    [Fact]
    public async Task EnableFeatures_with_reboot_sets_reboot_pending_state()
    {
        var client = new FakeBrokerClient();
        client.Enqueue(new BrokerResponse(true, RebootRequired: true));
        var vm = Make(client);

        await vm.EnableFeaturesAsync();

        Assert.IsType<EnableFeaturesRequest>(client.Sent[0]);
        Assert.True(vm.RebootRequired);
        var store = new BootstrapStateStore(_statePath);
        Assert.Equal(BootstrapStep.RebootPending, await store.ReadAsync());
    }

    [Fact]
    public async Task ResumeAfterReboot_installs_kernel_then_sets_default_then_done()
    {
        // Pre-seed: reboot is pending.
        await new BootstrapStateStore(_statePath).WriteAsync(BootstrapStep.RebootPending);
        var client = new FakeBrokerClient();
        client.Enqueue(new BrokerResponse(true)); // install kernel
        client.Enqueue(new BrokerResponse(true)); // set default version
        var vm = Make(client);

        await vm.ResumeAsync();

        Assert.IsType<InstallOrUpdateKernelRequest>(client.Sent[0]);
        Assert.IsType<SetDefaultWslVersionRequest>(client.Sent[1]);
        Assert.Equal(BootstrapStep.Done, await new BootstrapStateStore(_statePath).ReadAsync());
        Assert.True(vm.IsComplete);
    }

    [Fact]
    public async Task Failure_surfaces_error_and_does_not_advance()
    {
        var client = new FakeBrokerClient();
        client.Enqueue(new BrokerResponse(false, "Access is denied."));
        var vm = Make(client);

        await vm.EnableFeaturesAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.RebootRequired);
    }

    public void Dispose() { if (File.Exists(_statePath)) File.Delete(_statePath); }
}
