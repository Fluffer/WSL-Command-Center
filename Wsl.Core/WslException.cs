namespace Wsl.Core;

public class WslException : Exception
{
    public int? ExitCode { get; }
    public string? StdErr { get; }
    public WslErrorKind Kind { get; }

    public WslException(WslErrorKind kind, string message, int? exitCode = null, string? stdErr = null)
        : base(message)
    {
        Kind = kind;
        ExitCode = exitCode;
        StdErr = stdErr;
    }
}
