using MonoMod.Utils;
using System.Diagnostics;

namespace Olympus.Utils; 

public static class URIHelper {

    public static void OpenInBrowser(string url) {
        if (PlatformHelper.Is(Platform.Windows)) {
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}"));
        } else if (PlatformHelper.Is(Platform.Linux)) {
            Process.Start("xdg-open", url);
        } else if (PlatformHelper.Is(Platform.MacOS)) {
            Process.Start("open", url);
        } else {
            AppLogger.Log.Error("Cannot open url since platform is not recognized");
        }
    }
    
}