namespace Wsl.Core;

public static class WslErrorMapper
{
    public static WslErrorKind Classify(int exitCode, string stderr)
    {
        var s = stderr.ToLowerInvariant();
        if (s.Contains("no distribution with the supplied name")) return WslErrorKind.DistroNotFound;
        if (s.Contains("already exists")) return WslErrorKind.AlreadyExists;
        if (s.Contains("access is denied")) return WslErrorKind.AccessDenied;
        if (s.Contains("not installed")) return WslErrorKind.NotInstalled;
        if (s.Contains("corrupted") || s.Contains("not a valid")) return WslErrorKind.InvalidArchive;
        return WslErrorKind.CommandFailed;
    }

    /// <summary>Throws a WslException if exit code is non-zero.</summary>
    public static void ThrowIfFailed(ProcessResult result, string operation)
    {
        if (result.ExitCode == 0) return;
        var kind = Classify(result.ExitCode, result.StdErr);
        throw new WslException(kind, $"{operation} failed: {result.StdErr.Trim()}",
                               result.ExitCode, result.StdErr);
    }
}
