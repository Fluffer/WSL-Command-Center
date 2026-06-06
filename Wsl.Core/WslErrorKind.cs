namespace Wsl.Core;

public enum WslErrorKind
{
    NotInstalled,
    DistroNotFound,
    AccessDenied,
    AlreadyExists,
    InvalidArchive,
    CommandFailed,
    Timeout
}
