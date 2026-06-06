using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Wsl.Contracts;
using Wsl.Core;
using Wsl.Core.Ipc;

namespace Wsl.Broker;

public class BrokerServer
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    private readonly PrivilegedOperations _ops;
    private readonly IPeerVerifier _verifier;

    public BrokerServer(PrivilegedOperations ops, IPeerVerifier verifier)
    {
        _ops = ops;
        _verifier = verifier;
    }

    /// <summary>Serves requests until the idle timeout elapses with no new connection.
    /// The idle timer is recreated each loop iteration (so it resets after every served
    /// request) and only bounds <c>WaitForConnectionAsync</c> — request handling receives the
    /// outer <paramref name="ct"/>, so a long privileged op is never killed mid-flight.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            using var server = CreatePipe();
            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idleCts.CancelAfter(IdleTimeout); // bounds the WaitForConnectionAsync below only
            try
            {
                await server.WaitForConnectionAsync(idleCts.Token);
            }
            catch (OperationCanceledException)
            {
                return; // idle => exit, re-elevate on next demand
            }

            var clientPid = Win32Pipe.GetClientPid(server.SafePipeHandle);
            if (clientPid < 0 || !_verifier.IsTrustedPeer(clientPid, "Wsl.App.exe"))
            {
                server.Disconnect();
                continue;
            }

            await HandleOneAsync(server, ct);
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var sid = WindowsIdentity.GetCurrent().User!;
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            sid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        // FirstPipeInstance: creation fails if the name is already taken (anti-squat).
        return NamedPipeServerStreamAcl.Create(
            PipeName.Broker, PipeDirection.InOut, maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            inBufferSize: 0, outBufferSize: 0, security);
    }

    private async Task HandleOneAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        var request = await PipeFraming.ReadAsync<BrokerRequest>(server, ct);
        BrokerResponse response;
        try
        {
            response = request is null
                ? new BrokerResponse(false, "Malformed request")
                : await _ops.HandleAsync(request, ct);
        }
        catch (Exception ex)
        {
            response = new BrokerResponse(false, ex.Message);
        }
        await PipeFraming.WriteAsync(server, response, ct);
        server.Disconnect();
    }
}
