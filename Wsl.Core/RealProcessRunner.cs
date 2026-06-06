using System.Diagnostics;
using System.Text;

namespace Wsl.Core;

/// <summary>
/// Real process runner. wsl.exe management output is UTF-16LE; we read raw bytes and decode,
/// sniffing a BOM and falling back to UTF-16LE. Linux command output (cat etc.) is UTF-8 —
/// callers requiring UTF-8 should use RunWithInputAsync/RunAsync and tolerate either; the BOM
/// sniff handles the common cases.
/// </summary>
public class RealProcessRunner : IProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public Task<ProcessResult> RunAsync(string exe, string[] args,
        TimeSpan? timeout = null, CancellationToken ct = default)
        => Exec(exe, args, stdin: null, timeout, ct);

    public Task<ProcessResult> RunWithInputAsync(string exe, string[] args, string stdin,
        TimeSpan? timeout = null, CancellationToken ct = default)
        => Exec(exe, args, stdin, timeout, ct);

    private static async Task<ProcessResult> Exec(string exe, string[] args, string? stdin,
        TimeSpan? timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        if (stdin is not null)
        {
            await proc.StandardInput.WriteAsync(stdin);
            proc.StandardInput.Close();
        }

        var outBytesTask = ReadAllBytesAsync(proc.StandardOutput.BaseStream, ct);
        var errBytesTask = ReadAllBytesAsync(proc.StandardError.BaseStream, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultTimeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new WslException(WslErrorKind.Timeout, $"{exe} timed out");
        }

        var stdout = Decode(await outBytesTask);
        var stderr = Decode(await errBytesTask);
        return new ProcessResult(proc.ExitCode, stdout, stderr);
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream s, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0) return "";
        // UTF-16LE BOM
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        // UTF-8 BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        // Heuristic: many NUL bytes in even positions => UTF-16LE (wsl management output).
        var nulEven = 0;
        var sample = Math.Min(bytes.Length, 64);
        for (var i = 1; i < sample; i += 2) if (bytes[i] == 0) nulEven++;
        if (nulEven > sample / 4) return Encoding.Unicode.GetString(bytes);
        return Encoding.UTF8.GetString(bytes);
    }
}
