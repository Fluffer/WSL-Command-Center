using System.Text.Json;
using Wsl.Contracts;
using Xunit;

namespace Wsl.Core.Tests;

public class ContractsSerializationTests
{
    private static readonly JsonSerializerOptions Opts =
        new() { TypeInfoResolver = BrokerJsonContext.Default };

    [Fact]
    public void Roundtrips_polymorphic_request()
    {
        BrokerRequest req = new SetDefaultWslVersionRequest(2);
        var json = JsonSerializer.Serialize(req, Opts);
        var back = JsonSerializer.Deserialize<BrokerRequest>(json, Opts);
        var typed = Assert.IsType<SetDefaultWslVersionRequest>(back);
        Assert.Equal(2, typed.Version);
    }

    [Fact]
    public void Roundtrips_each_request_type()
    {
        BrokerRequest[] all =
        {
            new CheckWslInstalledRequest(),
            new EnableFeaturesRequest(),
            new InstallOrUpdateKernelRequest(),
            new SetDefaultWslVersionRequest(2),
        };
        foreach (var req in all)
        {
            var json = JsonSerializer.Serialize(req, Opts);
            var back = JsonSerializer.Deserialize<BrokerRequest>(json, Opts);
            Assert.Equal(req.GetType(), back!.GetType());
        }
    }

    [Fact]
    public void Roundtrips_response()
    {
        var resp = new BrokerResponse(true, null, RebootRequired: true, "done");
        var json = JsonSerializer.Serialize(resp, Opts);
        var back = JsonSerializer.Deserialize<BrokerResponse>(json, Opts);
        Assert.True(back!.Success);
        Assert.True(back.RebootRequired);
        Assert.Equal("done", back.Detail);
    }
}
