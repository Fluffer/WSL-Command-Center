namespace Wsl.Core;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string exe,
        string[] args,
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    /// <summary>Runs with text piped to stdin (used for `tee`-style writes).</summary>
    Task<ProcessResult> RunWithInputAsync(
        string exe,
        string[] args,
        string stdin,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}
