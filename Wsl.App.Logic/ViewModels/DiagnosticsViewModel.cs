using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;
using Wsl.Core.Diagnostics;

namespace Wsl.App.Logic.ViewModels;

/// <summary>UI-friendly projection of a <see cref="DiagnosticResult"/> (keeps WinUI types out of Core).</summary>
public sealed class DiagnosticRow
{
    public DiagnosticRow(DiagnosticResult result) => Result = result;
    public DiagnosticResult Result { get; }
    public string Title => Result.Title;
    public string Detail => Result.Detail;
    public string SeverityLabel => Result.Severity.ToString();

    /// <summary>Segoe Fluent Icons glyph for the severity. Numeric code points keep the source ASCII.</summary>
    public string Glyph => char.ConvertFromUtf32(Result.Severity switch
    {
        DiagnosticSeverity.Ok => 0xE73E,       // CheckMark
        DiagnosticSeverity.Info => 0xE946,     // Info
        DiagnosticSeverity.Warning => 0xE7BA,  // Warning
        _ => 0xEA39,                            // ErrorBadge
    });

    public bool HasFix => Result.Fix is not null;
    public string FixLabel => Result.Fix?.Label ?? "";
    public DiagnosticFix? Fix => Result.Fix;
}

public partial class DiagnosticsViewModel : ObservableObject
{
    private readonly WslDiagnosticsService _diag;
    private readonly WslDistroService _distros;
    private readonly WslSystemService _system;

    public DiagnosticsViewModel(WslDiagnosticsService diag, WslDistroService distros, WslSystemService system)
    { _diag = diag; _distros = distros; _system = system; }

    public ObservableCollection<DiagnosticRow> Rows { get; } = new();
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    [RelayCommand]
    public Task RunAsync() => Guarded(async () =>
    {
        await ReloadAsync();
        var problems = Rows.Count(r => r.Result.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error);
        StatusMessage = problems == 0 ? "All checks passed." : $"{problems} issue(s) found.";
    });

    [RelayCommand]
    public Task ApplyFixAsync(DiagnosticFix fix) => fix is null ? Task.CompletedTask : Guarded(async () =>
    {
        switch (fix.Id)
        {
            case WslDiagnosticsService.Fixes.RestartWsl: await _distros.ShutdownAsync(); break;
            case WslDiagnosticsService.Fixes.UpdateWsl: await _system.UpdateAsync(); break;
            default: ErrorMessage = $"Unknown fix '{fix.Id}'."; return;
        }
        await ReloadAsync();
        StatusMessage = $"Applied: {fix.Label}. Re-ran checks.";
    });

    private async Task ReloadAsync()
    {
        var results = await _diag.RunAllAsync();
        Rows.Clear();
        foreach (var r in results) Rows.Add(new DiagnosticRow(r));
    }

    private async Task Guarded(Func<Task> work)
    {
        if (IsBusy) return;   // re-entry guard: a second run/fix while one is in flight would interleave Rows mutations
        IsBusy = true; ErrorMessage = null; StatusMessage = null;
        try { await work(); }
        catch (System.Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
