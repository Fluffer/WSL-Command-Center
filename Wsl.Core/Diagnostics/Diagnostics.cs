namespace Wsl.Core.Diagnostics;

public enum DiagnosticSeverity { Ok, Info, Warning, Error }

/// <summary>A repair the user can trigger for a finding. Execution lives in the app layer
/// (some fixes need the elevated broker); Core only declares the intent.</summary>
public sealed record DiagnosticFix(string Id, string Label, bool Destructive);

public sealed record DiagnosticResult(
    string Id, string Title, DiagnosticSeverity Severity, string Detail, DiagnosticFix? Fix = null);

/// <summary>One health check. Implementations must be side-effect-free where possible and never
/// throw out of <see cref="RunAsync"/> for an expected failure — return an Error/Warning result instead.</summary>
public interface IDiagnosticCheck
{
    string Id { get; }
    Task<DiagnosticResult> RunAsync(CancellationToken ct = default);
}

/// <summary>Runs the registered checks sequentially (sequential on purpose: bounds concurrency and
/// avoids ordering hazards where one check could start the WSL VM and skew a later one).</summary>
public sealed class WslDiagnosticsService
{
    private readonly IReadOnlyList<IDiagnosticCheck> _checks;

    public WslDiagnosticsService(IEnumerable<IDiagnosticCheck> checks) => _checks = checks.ToList();

    public async Task<IReadOnlyList<DiagnosticResult>> RunAllAsync(CancellationToken ct = default)
    {
        var results = new List<DiagnosticResult>(_checks.Count);
        foreach (var check in _checks)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await SafeRunAsync(check, ct));
        }
        return results;
    }

    /// <summary>A throwing check degrades to a Warning row instead of failing the whole run.</summary>
    private static async Task<DiagnosticResult> SafeRunAsync(IDiagnosticCheck check, CancellationToken ct)
    {
        try { return await check.RunAsync(ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new DiagnosticResult(check.Id, check.Id, DiagnosticSeverity.Warning,
                $"This check could not complete: {ex.Message}");
        }
    }

    /// <summary>Fix ids shared between checks and the app-layer executor.</summary>
    public static class Fixes
    {
        public const string RestartWsl = "restart-wsl";
        public const string UpdateWsl = "update-wsl";
    }
}
