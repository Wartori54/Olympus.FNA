using MonoMod.Utils;
using System;
using System.Diagnostics;
using System.IO;

namespace Olympus.Utils {
    public class GameLauncher {
        public static LaunchResult LaunchCurrent(bool vanilla) {
            if (Config.Instance.Installation == null) return LaunchResult.MissingInstall;
            return Launch(Config.Instance.Installation, vanilla);
        }

        public static LaunchResult Launch(Installation install, bool vanilla) {
            if (vanilla) {
                MetaNotificationScene.PushNotification(new Notification {
                    Message = "Launching Vanilla..."
                });
            } else {
                MetaNotificationScene.PushNotification(new Notification {
                    Message = "Launching Everest..."
                });
            }
            
            Process game = new Process();

            if (PlatformHelper.Is(Platform.Unix)) { // Linux and Mac are different
                game.StartInfo.FileName = Path.Combine(install.Root, "Celeste");

                // The following is stolen from Old Olympus sharp/CmdLaunch.cs#L37
                // 1.3.3.0 splits Celeste into two, so to speak.
                if (!File.Exists(game.StartInfo.FileName) && Path.GetFileName(install.Root) == "Resources") {
                    string? parentDir = Path.GetDirectoryName(install.Root);
                    if (parentDir == null) return LaunchResult.InstallInvalid;
                    game.StartInfo.FileName = Path.Combine(parentDir, "MacOS", "Celeste");
                }
            } else { // Assume windows
                game.StartInfo.FileName = Path.Combine(install.Root, "Celeste.exe");
            }

            if (!File.Exists(game.StartInfo.FileName)) {
                return LaunchResult.InstallInvalid;
            }

            Environment.CurrentDirectory = game.StartInfo.WorkingDirectory = install.Root;

            if (vanilla) {
                (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) = install.ScanVersion(false);
                if (ModVersion == null || ModVersion.Minor == 0 || ModVersion.Minor >= (1550 + 700)) {
                    try {
                        File.WriteAllText(Path.Combine(install.Root, "nextLaunchIsVanilla.txt"), "This file was created by Olympus and will be deleted automatically.");
                        AppLogger.Log.Information("nextLaunchIsVanilla.txt created");
                    } catch (Exception e) {
                        AppLogger.Log.Error($"Failed to create nextLaunchIsVanilla.txt: {e}");
                        return LaunchResult.IOError;
                    }
                } else {
                    game.StartInfo.Arguments = "--vanilla";
                    AppLogger.Log.Information("Old version: " + ModVersion.Minor + ", loading using '--vanilla' argument");
                }
            }

            game.Start();

            return LaunchResult.Success;
        }

        public enum LaunchResult {
            Success,        // Everything went ok
            MissingInstall, // No installs found or none selected
            InstallInvalid, // Somethings wrong with the install
            IOError         // IOError (probably permission related)
        }
    }
}