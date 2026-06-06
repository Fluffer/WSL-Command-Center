namespace Wsl.Core;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string exe,
        string[] args,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}
