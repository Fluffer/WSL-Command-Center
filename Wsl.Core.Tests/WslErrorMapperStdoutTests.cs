using Wsl.Core;

namespace Wsl.Core.Tests;

/// <summary>
/// Regression guard for a defect found by running "Optimize disk (make sparse)" against a real
/// distro: wsl.exe reported the whole failure on STDOUT and left STDERR empty, so the UI showed
/// "Optimize WCC-SmokeTest failed: " with no reason. Verified live against WSL 2.9.3.0 —
/// `wsl --manage &lt;d&gt; --set-sparse true` exits -1, stdout carries the explanation, stderr is "".
/// </summary>
public class WslErrorMapperStdoutTests
{
    private const string SparseDisabledStdout =
        "Sparse VHD support is currently disabled due to potential data corruption.\n"
        + "To force a distribution to use a sparse VHD, please run:\n"
        + "wsl.exe --manage <DistributionName> --set-sparse true --allow-unsafe\n"
        + "Error code: Wsl/Service/E_INVALIDARG";

    [Fact]
    public void Failure_reported_only_on_stdout_still_reaches_the_message()
    {
        var result = new ProcessResult(-1, SparseDisabledStdout, "");

        var ex = Assert.Throws<WslException>(
            () => WslErrorMapper.ThrowIfFailed(result, "Optimize Ubuntu"));

        Assert.Contains("Sparse VHD support is currently disabled", ex.Message);
        Assert.Contains("--allow-unsafe", ex.Message);
        Assert.DoesNotContain("failed: \n", ex.Message);
        Assert.NotEqual("Optimize Ubuntu failed: ", ex.Message);
    }

    [Fact]
    public void Stderr_still_wins_when_both_streams_have_text()
    {
        var result = new ProcessResult(1, "noise on stdout", "the real error");

        var ex = Assert.Throws<WslException>(
            () => WslErrorMapper.ThrowIfFailed(result, "Export Ubuntu"));

        Assert.Contains("the real error", ex.Message);
        Assert.DoesNotContain("noise on stdout", ex.Message);
    }

    [Fact]
    public void Classification_works_off_stdout_when_stderr_is_empty()
    {
        var result = new ProcessResult(-1, "There is no distribution with the supplied name.", "");

        var ex = Assert.Throws<WslException>(
            () => WslErrorMapper.ThrowIfFailed(result, "Start Ghost"));

        Assert.Equal(WslErrorKind.DistroNotFound, ex.Kind);
    }

    [Fact]
    public void Both_streams_empty_still_yields_an_actionable_message()
    {
        var result = new ProcessResult(5, "", "");

        var ex = Assert.Throws<WslException>(
            () => WslErrorMapper.ThrowIfFailed(result, "Optimize Ubuntu"));

        Assert.Contains("exit code 5", ex.Message);
        Assert.False(ex.Message.TrimEnd().EndsWith(":"), "message must not trail off after a colon");
    }

    [Fact]
    public void Success_never_throws()
    {
        WslErrorMapper.ThrowIfFailed(new ProcessResult(0, "fine", ""), "Export Ubuntu");
    }
}
