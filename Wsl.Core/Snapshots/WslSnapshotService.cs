using System.Text.Json;

namespace Wsl.Core.Snapshots;

public class WslSnapshotService
{
    private readonly WslDistroService _distros;
    private readonly Func<string> _root;
    private readonly IProcessRunner _runner;
    private readonly StatePreservingExport _preserving;

    public WslSnapshotService(WslDistroService distros,
        Func<string> storeRootProvider, IProcessRunner runner,
        StatePreservingExport preserving)
    {
        _distros = distros;
        _root = storeRootProvider;
        _runner = runner;
        _preserving = preserving;
    }

    private string DistroDir(string distro)
    {
        var dir = Path.Combine(_root(), Sanitize(distro));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<Snapshot> CreateAsync(string distro, string label, int wslVersion,
        CancellationToken ct = default)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var dir = DistroDir(distro);
        var vhdx = Path.Combine(dir, stamp + ".vhdx");

        await _preserving.RunAsync(async c =>
        {
            var r = await _runner.RunAsync("wsl.exe",
                new[] { "--export", distro, vhdx, "--vhd" }, null, c);
            WslErrorMapper.ThrowIfFailed(r, $"Snapshot export {distro}");
        }, ct);

        long bytes = File.Exists(vhdx) ? new FileInfo(vhdx).Length : 0;
        var snap = new Snapshot(distro, label, DateTime.UtcNow, bytes, "vhd", wslVersion,
            vhdx, Path.ChangeExtension(vhdx, ".json"));
        File.WriteAllText(snap.SidecarPath, JsonSerializer.Serialize(snap));
        return snap;
    }

    public IReadOnlyList<Snapshot> List(string? distro = null)
    {
        var root = _root();
        if (!Directory.Exists(root)) return Array.Empty<Snapshot>();
        var dirs = distro is null
            ? Directory.GetDirectories(root)
            : new[] { Path.Combine(root, Sanitize(distro)) };
        var snaps = new List<Snapshot>();
        foreach (var d in dirs.Where(Directory.Exists))
            foreach (var json in Directory.GetFiles(d, "*.json"))
            {
                try
                {
                    var s = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(json));
                    if (s is not null) snaps.Add(s);
                }
                catch { /* skip corrupt sidecar */ }
            }
        return snaps.OrderByDescending(s => s.CreatedUtc).ToList();
    }

    public async Task RestoreCloneAsync(Snapshot snap, string newName, string installDir,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(installDir);
        var r = await _runner.RunAsync("wsl.exe",
            new[] { "--import", newName, installDir, snap.VhdxPath, "--vhd" }, null, ct);
        WslErrorMapper.ThrowIfFailed(r, $"Restore clone {newName}");
    }

    public async Task RestoreOverwriteAsync(Snapshot snap, string installDir,
        CancellationToken ct = default)
    {
        var running = (await _distros.ListAsync(ct))
            .Any(d => d.Name == snap.Distro && d.State == DistroState.Running);
        if (running)
            throw new WslException(WslErrorKind.CommandFailed,
                $"'{snap.Distro}' is running. Terminate it before overwrite-restore.");

        Directory.CreateDirectory(installDir);

        if (!File.Exists(snap.VhdxPath) || new FileInfo(snap.VhdxPath).Length == 0)
            throw new WslException(WslErrorKind.CommandFailed,
                $"Snapshot file missing or empty: {snap.VhdxPath}. Refusing to overwrite '{snap.Distro}'.");

        var unreg = await _runner.RunAsync("wsl.exe", new[] { "--unregister", snap.Distro }, null, ct);
        WslErrorMapper.ThrowIfFailed(unreg, $"Unregister {snap.Distro}");
        var imp = await _runner.RunAsync("wsl.exe",
            new[] { "--import", snap.Distro, installDir, snap.VhdxPath, "--vhd" }, null, ct);
        WslErrorMapper.ThrowIfFailed(imp, $"Restore overwrite {snap.Distro}");
    }

    public void Delete(Snapshot snap)
    {
        var root = Path.GetFullPath(_root()).TrimEnd(Path.DirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        foreach (var p in new[] { snap.VhdxPath, snap.SidecarPath })
        {
            if (string.IsNullOrEmpty(p)) continue;
            var full = Path.GetFullPath(p);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(full)) File.Delete(full);
        }
    }

    private static string Sanitize(string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (safe is "." or ".." || safe.Contains("..") || safe.StartsWith("-") ||
            safe.Contains('/') || safe.Contains('\\'))
            throw new ArgumentException($"Invalid distro name for snapshot store: '{name}'.");
        return safe;
    }
}
