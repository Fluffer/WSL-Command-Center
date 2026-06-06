using System.Text;
using System.Text.Json;
using Wsl.Contracts;

namespace Wsl.Core.Ipc;

/// <summary>Length-prefixed (4-byte LE) UTF-8 JSON framing, shared by client and server.</summary>
public static class PipeFraming
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { TypeInfoResolver = BrokerJsonContext.Default };

    public static async Task WriteAsync<T>(Stream s, T message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message, JsonOpts);
        var payload = Encoding.UTF8.GetBytes(json);
        await s.WriteAsync(BitConverter.GetBytes(payload.Length), ct);
        await s.WriteAsync(payload, ct);
        await s.FlushAsync(ct);
    }

    public static async Task<T?> ReadAsync<T>(Stream s, CancellationToken ct) where T : class
    {
        var lenBuf = new byte[4];
        if (!await ReadExactAsync(s, lenBuf, ct)) return null;
        var len = BitConverter.ToInt32(lenBuf, 0);
        if (len <= 0 || len > 1_000_000) return null;
        var payload = new byte[len];
        if (!await ReadExactAsync(s, payload, ct)) return null;
        return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(payload), JsonOpts);
    }

    private static async Task<bool> ReadExactAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        var off = 0;
        while (off < buf.Length)
        {
            var n = await s.ReadAsync(buf.AsMemory(off), ct);
            if (n == 0) return false;
            off += n;
        }
        return true;
    }
}
