using System.Text.Json.Serialization;

namespace Wsl.Core.Containers;

/// <summary>An image row parsed from `wslc image list --format json`. All fields are best-effort —
/// the preview CLI's format may churn.</summary>
public record WslcImage(
    string FullId,
    string ShortId,
    string Repository,
    string Tag,
    DateTimeOffset Created,
    long SizeBytes,
    string SizeHuman)
{
    public string RepoTag => $"{Repository}:{Tag}";
}

/// <summary>A volume row parsed from `wslc volume list --format json`.</summary>
public record WslcVolume(string Driver, string Name);

/// <summary>A network row parsed from `wslc network list --format json`.</summary>
public record WslcNetwork(string Driver, string Id, string Name);

/// <summary>Outcome of a mutating wslc resource command (remove/prune/pull/push/tag/create/
/// login/logout). Never throws to the caller — non-zero exit, a missing exe, or a timeout all
/// degrade to a failed result carrying stderr.</summary>
public record WslcActionResult(bool Ok, int ExitCode, string StdErr)
{
    public static WslcActionResult Failed(string stdErr) => new(false, -1, stdErr);
}

/// <summary>Raw JSON shape of one `wslc image list --format json` element. Mapped to
/// <see cref="WslcImage"/> for callers.</summary>
internal sealed class WslcImageJson
{
    [JsonPropertyName("Created")] public long Created { get; set; }
    [JsonPropertyName("Id")] public string Id { get; set; } = "";
    [JsonPropertyName("Repository")] public string Repository { get; set; } = "";
    [JsonPropertyName("Size")] public long Size { get; set; }
    [JsonPropertyName("Tag")] public string Tag { get; set; } = "";
}

/// <summary>Raw JSON shape of one `wslc volume list --format json` element.</summary>
internal sealed class WslcVolumeJson
{
    [JsonPropertyName("Driver")] public string Driver { get; set; } = "";
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
}

/// <summary>Raw JSON shape of one `wslc network list --format json` element.</summary>
internal sealed class WslcNetworkJson
{
    [JsonPropertyName("Driver")] public string Driver { get; set; } = "";
    [JsonPropertyName("Id")] public string Id { get; set; } = "";
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
}
