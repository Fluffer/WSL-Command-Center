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

    /// <summary>
    /// The text wsl.exe actually used to explain a failure. It does NOT reliably use stderr:
    /// `wsl --manage --set-sparse` writes its whole explanation to stdout and leaves stderr empty,
    /// which surfaced in the UI as a bare "Optimize &lt;distro&gt; failed: " with no reason at all.
    /// Prefer stderr, fall back to stdout, so the diagnosis reaches the user either way.
    /// </summary>
    internal static string FailureText(ProcessResult result)
    {
        var err = (result.StdErr ?? "").Trim();
        return err.Length > 0 ? err : (result.StdOut ?? "").Trim();
    }

    /// <summary>Throws a WslException if exit code is non-zero.</summary>
    public static void ThrowIfFailed(ProcessResult result, string operation)
    {
        if (result.ExitCode == 0) return;
        var detail = FailureText(result);
        // Classify on the same text the user is shown, so a stdout-only failure is still
        // categorised (e.g. "no distribution with the supplied name" on stdout).
        var kind = Classify(result.ExitCode, detail);
        var message = detail.Length > 0
            ? $"{operation} failed: {detail}"
            : $"{operation} failed with exit code {result.ExitCode}.";
        throw new WslException(kind, message, result.ExitCode, detail);
    }
}
