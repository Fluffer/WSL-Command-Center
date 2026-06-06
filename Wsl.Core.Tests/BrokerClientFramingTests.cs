using Wsl.Contracts;
using Wsl.Core.Ipc;
using Xunit;

namespace Wsl.Core.Tests;

public class BrokerClientFramingTests
{
    [Fact]
    public async Task Request_then_response_roundtrip_over_stream()
    {
        using var stream = new MemoryStream();

        // Write a request, rewind, read it back as the server would.
        await PipeFraming.WriteAsync<BrokerRequest>(
            stream, new SetDefaultWslVersionRequest(2), default);
        stream.Position = 0;
        var req = await PipeFraming.ReadAsync<BrokerRequest>(stream, default);
        Assert.IsType<SetDefaultWslVersionRequest>(req);

        // Now a response in a fresh stream.
        using var s2 = new MemoryStream();
        await PipeFraming.WriteAsync(s2, new BrokerResponse(true, Detail: "ok"), default);
        s2.Position = 0;
        var resp = await PipeFraming.ReadAsync<BrokerResponse>(s2, default);
        Assert.True(resp!.Success);
        Assert.Equal("ok", resp.Detail);
    }

    [Fact]
    public async Task Truncated_length_returns_null()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2 }); // < 4 length bytes
        var resp = await PipeFraming.ReadAsync<BrokerResponse>(stream, default);
        Assert.Null(resp);
    }
}
