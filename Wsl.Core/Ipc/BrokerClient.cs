using System.Diagnostics;
using System.IO.Pipes;
using Wsl.Contracts;

namespace Wsl.Core.Ipc;

public class BrokerClient : IBrokerClient
{
    private readonly string _brokerExePath;
    private readonly IPeerVerifier _verifier;

    public BrokerClient(string brokerExePath, IPeerVerifier verifier)
    {
        _brokerExePath = brokerExePath;
        _verifier = verifier;
    }

    public async Task<BrokerResponse> SendAsync(BrokerRequest request, CancellationToken ct = default)
    {
        // Try to connect to an already-running broker first (short timeout). Only one real
        // connection is ever opened — no separate "probe" that could consume the broker's
        // single FirstPipeInstance slot. If nothing is listening, launch elevated and retry.
        var client = await TryConnectAsync(TimeSpan.FromMilliseconds(300), ct);
        if (client is null)
        {
            if (!LaunchBrokerElevated())
                return new BrokerResponse(false, "Elevation was cancelled.");
            client = await TryConnectAsync(TimeSpan.FromSeconds(10), ct);
            if (client is null)
                return new BrokerResponse(false, "Broker did not start.");
        }

        using (client)
        {
            // Verify the SERVER before sending anything.
            var serverPid = Win32PipeClient.GetServerPid(client.SafePipeHandle);
            if (serverPid < 0 || !_verifier.IsTrustedPeer(serverPid, "Wsl.Broker.exe"))
                return new BrokerResponse(false, "Broker identity verification failed.");

            await PipeFraming.WriteAsync<BrokerRequest>(client, request, ct);
            var resp = await PipeFraming.ReadAsync<BrokerResponse>(client, ct);
            return resp ?? new BrokerResponse(false, "No response from broker.");
        }
    }

    private static async Task<NamedPipeClientStream?> TryConnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        var client = new NamedPipeClientStream(
            ".", PipeName.Broker, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(timeout, ct);
            return client;
        }
        catch (TimeoutException)
        {
            await client.DisposeAsync();
            return null;
        }
    }

    private bool LaunchBrokerElevated()
    {
        var psi = new ProcessStartInfo
        {
            FileName = _brokerExePath,
            UseShellExecute = true,
            Verb = "runas", // triggers UAC
        };
        try
        {
            Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // user declined UAC
        }
    }
}
