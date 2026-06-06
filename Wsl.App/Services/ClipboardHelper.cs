using Windows.ApplicationModel.DataTransfer;

namespace Wsl.App;

/// <summary>Thin wrapper over the WinRT clipboard for copying plain text.</summary>
internal static class ClipboardHelper
{
    public static void CopyText(string text)
    {
        var dp = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        dp.SetText(text ?? "");
        Clipboard.SetContent(dp);
    }
}
