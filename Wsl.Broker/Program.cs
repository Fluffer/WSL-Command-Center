using Wsl.Broker;
using Wsl.Core;
using Wsl.Core.Ipc;

var ops = new PrivilegedOperations(new RealProcessRunner());
var verifier = new WindowsPeerVerifier();
var server = new BrokerServer(ops, verifier);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await server.RunAsync(cts.Token);
