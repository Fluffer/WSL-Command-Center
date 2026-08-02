using System;
using Microsoft.UI.Xaml.Data;
using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Wsl.Core.Containers;
using Wsl.Core.Diagnostics;
using Wsl.Core.Monitoring;
using Wsl.Core.Snapshots;

namespace Wsl.App.Converters;

/// <summary>
/// Builds the accessible name for a row in one of the Containers page lists.
///
/// A ListViewItem falls back to the bound item's <c>ToString()</c> for its UIA Name when the
/// item template's root doesn't supply one. Every model bound in these lists is a positional
/// <c>record</c>, so that fallback announces the whole field dump — a screen reader would read
/// "WslcSessionRow { Session = WslcSession { Id = 1, CreatorPid = 6132, DisplayName …" instead
/// of anything useful. Binding <c>AutomationProperties.Name</c> through this converter replaces
/// that with a short human sentence.
///
/// It takes the whole row object (<c>{x:Bind Converter=...}</c> with no path) and switches on
/// type, rather than composing the string inline: compiled function-call bindings can't reach
/// the enclosing Page from inside a DataTemplate, the same toolchain limitation
/// <see cref="ContainerActionVisibilityConverter"/> works around.
/// </summary>
public sealed class ListItemAccessibleNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value switch
        {
            WslcContainer c => Describe(c),
            WslcImage i => $"Image {i.RepoTag}, {i.SizeHuman}",
            WslcVolume v => $"Volume {v.Name}, {v.Driver} driver",
            WslcNetwork n => $"Network {n.Name}, {n.Driver} driver",
            WslcSessionRow s => $"Session {s.DisplayName}, {s.TotalHuman} total",
            Distro d => $"Distro {d.Name}, {d.State}, version {d.Version}"
                        + (d.IsDefault ? ", default" : ""),
            StepResult r => $"Step {r.Description}: {(r.Success ? "succeeded" : "failed")}",
            DiagnosticRow g => $"{g.SeverityLabel}: {g.Title}. {g.Detail}",
            DistroMetrics m =>
                $"{m.Name}, CPU {m.CpuPercent:F0} percent, memory {Bytes(m.MemUsedBytes)} of "
                + $"{Bytes(m.MemTotalBytes)}, disk {Bytes(m.DiskUsedBytes)} of {Bytes(m.DiskTotalBytes)}",
            Snapshot s =>
                $"Snapshot {s.Label} of {s.Distro}, {Bytes(s.Bytes)}, taken {s.CreatedUtc.ToLocalTime():g}",
            PortForward p =>
                $"Port forward {p.ListenAddress} port {p.ListenPort} to {p.ConnectAddress} port {p.ConnectPort}",
            // Deliberately NOT `value.ToString()` — that fallback is the very record-dump this
            // converter exists to suppress.
            _ => string.Empty,
        };

    /// <summary>Short byte size for speech — "1.4 GB" reads better than "1503238553 bytes".</summary>
    private static string Bytes(long b)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = b;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }

    private static string Describe(WslcContainer c)
    {
        // State is Unknown on rows that came from the columnar fallback, where the raw STATUS
        // text is all we have — prefer that over announcing "Unknown".
        var state = c.State == WslcContainerState.Unknown && c.Status.Length > 0
            ? c.Status
            : c.State.ToString();
        var ports = c.Ports.Length > 0 ? $", ports {c.Ports}" : "";
        return $"Container {c.Name}, image {c.Image}, {state}{ports}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
