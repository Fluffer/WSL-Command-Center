using Wsl.Core;

namespace Wsl.Core.Tests;

/// <summary>Records the last invocation and returns a queued result.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<ProcessResult> _results = new();

    public string? LastExe { get; private set; }
    public string[]? LastArgs { get; private set; }
    public List<string[]> AllArgs { get; } = new();

    public void Enqueue(ProcessResult result) => _results.Enqueue(result);

    public void Enqueue(int exitCode, string stdOut, string stdErr = "")
        => _results.Enqueue(new ProcessResult(exitCode, stdOut, stdErr));

    public Task<ProcessResult> RunAsync(
        string exe, string[] args, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        LastExe = exe;
        LastArgs = args;
        AllArgs.Add(args);
        var result = _results.Count > 0 ? _results.Dequeue() : new ProcessResult(0, "", "");
        return Task.FromResult(result);
    }
}
