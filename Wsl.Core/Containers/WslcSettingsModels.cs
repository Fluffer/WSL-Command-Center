namespace Wsl.Core.Containers;

/// <summary>
/// The <c>credentialStore</c> backend for <c>wslc registry login</c>. <see cref="Unset"/> means
/// the key is commented out (or absent) in <c>settings.yaml</c>; <see cref="Default"/> means the
/// key is present but explicitly set to the literal string <c>default</c> — distinct from Unset
/// even though both currently resolve to the same backend (wincred).
/// </summary>
public enum CredentialStoreKind
{
    Unset,
    Default,
    Wincred,
    File,
}

/// <summary>
/// The five keys wslc reads from <c>%LOCALAPPDATA%\wslc\settings.yaml</c>. Every string property
/// is null when the key is commented out (or missing) in the file — distinct from an explicit
/// value of the literal string <c>"default"</c>, which round-trips as-is. <see cref="CredentialStore"/>
/// uses <see cref="CredentialStoreKind.Unset"/> for the same "not set" case.
/// </summary>
public sealed class WslcSettings
{
    /// <summary><c>session.cpuCount</c> — a positive integer as a string, or the literal "default".</summary>
    public string? CpuCount { get; set; }

    /// <summary><c>session.memorySize</c> — a size like "2GB", or the literal "default".</summary>
    public string? MemorySize { get; set; }

    /// <summary><c>session.maxStorageSize</c> — a size like "500GB", or the literal "default".</summary>
    public string? MaxStorageSize { get; set; }

    /// <summary><c>session.defaultBindingAddress</c> — an IP address, or the literal "default".</summary>
    public string? DefaultBindingAddress { get; set; }

    /// <summary>Top-level <c>credentialStore</c>.</summary>
    public CredentialStoreKind CredentialStore { get; set; } = CredentialStoreKind.Unset;
}

/// <summary>Result of <see cref="WslcSettingsService.ReadAsync"/>.</summary>
public sealed class WslcSettingsReadResult
{
    public required WslcSettings Settings { get; init; }

    /// <summary>False when <c>settings.yaml</c> did not exist — <see cref="Settings"/> is then an
    /// all-unset object, not an error.</summary>
    public bool FileExists { get; init; }

    /// <summary>Set when the file exists but could not be read (I/O or permission failure).
    /// <see cref="Settings"/> is still a usable all-unset object in that case.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>Result of <see cref="WslcSettingsService.WriteAsync"/>.</summary>
public sealed class WslcSettingsWriteResult
{
    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }
}
