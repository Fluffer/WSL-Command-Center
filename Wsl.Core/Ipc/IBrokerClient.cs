using Wsl.Contracts;

namespace Wsl.Core.Ipc;

public interface IBrokerClient
{
    Task<BrokerResponse> SendAsync(BrokerRequest request, CancellationToken ct = default);
}
