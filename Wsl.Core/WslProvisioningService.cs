namespace Wsl.Core;

/// <summary>One shell command run inside a distro as part of a template.</summary>
public record ProvisioningStep(string Description, string ShellCommand);

/// <summary>A named, ordered recipe of provisioning steps applied to a registered distro.</summary>
public record DistroTemplate(
    string Id, string DisplayName, string Description, IReadOnlyList<ProvisioningStep> Steps);

/// <summary>Outcome of a single provisioning step.</summary>
public record StepResult(string Description, bool Success, string Output);

/// <summary>
/// Post-install provisioning and cloning for registered WSL distros — the parts not already
/// covered by install/import/export. Provisioning runs template steps inside the distro as root;
/// cloning delegates export+import to <see cref="WslBackupService"/> rather than re-wrapping wsl.exe.
///
/// Idempotency is the template author's responsibility: re-applying a template re-runs every step,
/// so step commands should be safe to run twice (apt-get is). Provisioning is NOT transactional —
/// a failed step stops the run but leaves earlier steps applied.
/// </summary>
public class WslProvisioningService
{
    private readonly IProcessRunner _runner;
    private readonly WslBackupService _backup;

    public WslProvisioningService(IProcessRunner runner, WslBackupService backup)
    {
        _runner = runner;
        _backup = backup;
    }

    /// <summary>
    /// Runs each template step inside <paramref name="distro"/> as root. The shell command is passed
    /// as a single argv element to <c>bash -lc</c>, so the host shell never re-parses it (no Windows-side
    /// injection). Stops at the first non-zero exit and returns the results gathered so far.
    /// </summary>
    public async Task<IReadOnlyList<StepResult>> ApplyTemplateAsync(
        string distro, DistroTemplate template,
        IProgress<StepResult>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(distro)) throw new ArgumentException("Distro is required.", nameof(distro));
        ArgumentNullException.ThrowIfNull(template);

        var results = new List<StepResult>();
        foreach (var step in template.Steps)
        {
            ct.ThrowIfCancellationRequested();
            var r = await _runner.RunAsync("wsl.exe",
                new[] { "-d", distro, "-u", "root", "--", "bash", "-lc", step.ShellCommand }, null, ct);
            var output = string.Join('\n',
                new[] { r.StdOut, r.StdErr }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())).Trim();
            var sr = new StepResult(step.Description, r.ExitCode == 0, output);
            results.Add(sr);
            progress?.Report(sr);
            if (!sr.Success) break;
        }
        return results;
    }

    /// <summary>
    /// Clones <paramref name="source"/> into a new distro <paramref name="newName"/> registered at
    /// <paramref name="installDir"/>, by exporting to a temp tarball and importing it back. The temp
    /// tarball is always deleted; if the import fails part-way, the half-registered target is unregistered
    /// so the system is not left in an unknown state. Collision checking (newName already registered)
    /// is the caller's responsibility.
    /// </summary>
    public async Task CloneAsync(string source, string newName, string installDir, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Source distro is required.", nameof(source));
        if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("New name is required.", nameof(newName));
        if (string.IsNullOrWhiteSpace(installDir)) throw new ArgumentException("Install directory is required.", nameof(installDir));

        var temp = Path.Combine(Path.GetTempPath(), $"wslclone-{Guid.NewGuid():N}.tar");
        try
        {
            await _backup.ExportAsync(source, temp, ExportFormat.Tar, ct);
            try
            {
                await _backup.RestoreAsync(newName, installDir, temp, ExportFormat.Tar, 2, ct);
            }
            catch
            {
                // Import failed mid-way — drop any partial registration. Best-effort; swallow cleanup errors.
                try { await _runner.RunAsync("wsl.exe", new[] { "--unregister", newName }, null, ct); }
                catch { /* leave the original failure to surface */ }
                throw;
            }
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* temp left behind, not fatal */ }
        }
    }
}

/// <summary>
/// Built-in provisioning templates. Deliberately minimal for v1 — only safe, idempotent,
/// Debian/Ubuntu-family recipes. Anything heavier (language runtimes, Docker) is out of scope.
/// </summary>
public static class TemplateCatalog
{
    public static IReadOnlyList<DistroTemplate> BuiltIn { get; } = new[]
    {
        new DistroTemplate(
            "update-packages", "Update packages",
            "Refresh the apt index and upgrade all installed packages (Debian/Ubuntu).",
            new[]
            {
                new ProvisioningStep("Refresh package index", "apt-get update"),
                new ProvisioningStep("Upgrade installed packages",
                    "DEBIAN_FRONTEND=noninteractive apt-get upgrade -y"),
            }),
        new DistroTemplate(
            "build-essentials", "Build essentials",
            "Install common build tooling: build-essential, curl, git, ca-certificates (Debian/Ubuntu).",
            new[]
            {
                new ProvisioningStep("Refresh package index", "apt-get update"),
                new ProvisioningStep("Install build tooling",
                    "DEBIAN_FRONTEND=noninteractive apt-get install -y build-essential curl git ca-certificates"),
            }),
    };
}
