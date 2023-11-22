using Olympus.API;
using Standart.Hash.xxHash;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Olympus.Utils {
    public static class ModUpdater {
        
        public static async void UpdateAllMods(Installation install) { //TODO: add update all button and auto update mods
            IEnumerable<ModAPI.IModFileInfo> mods = install.LocalInfoAPI.CreateAllModFileInfo();

            bool TickCallback(int progress, long lenght, int speed) {
                return true;
            }

            void FinishCallback(bool success, bool isDone) { }
            
            foreach (var mod in mods) {
                // Synchronous updates, for now
                await UpdateMod(mod, TickCallback, FinishCallback);
            }
        }

        /// <summary>
        /// Updates or installs a single mod
        /// </summary>
        /// <param name="mod">The populated mod info</param>
        /// <param name="tickCallback">Callback for download progress, parameters: (pos, lenght, speed) -> abort</param>
        /// <param name="finishCallback">Callback to determine outcome, parameters: (success, isDone)</param>
        public static async Task UpdateMod(ModAPI.IModFileInfo mod, Func<int, long, int, bool> tickCallback, Action<bool, bool> finishCallback) {
            // The following is copied and adapted from Everest (OuiModUpdateList.cs:257)
            string path;
            ModAPI.IModFileInfo remoteFileInfo;
            if (mod.IsLocal) { // update mod
                path = mod.Path!;
                remoteFileInfo = App.Instance.APIManager.TryAll<ModAPI.IModFileInfo>(api => api.GetModFileInfoFromId(mod.Name)) 
                                 ?? throw new ApplicationException($"Couldn't find update data for mod {mod.Name}");
            } else { // install it
                path = Path.Combine(Config.Instance.Installation?.Root ?? "", "Mods");
                if (path == "")
                    throw new InvalidOperationException("Tried to install mod without install selected");
                path = Path.Combine(path, mod.Name);
                remoteFileInfo = mod; // we can reuse the same modFileInfo, since the provided one is valid
            }
            
            // we will download the mod to Celeste_Directory/[update.GetHashCode()].zip at first.
            string zipPath = Path.Combine(Path.GetDirectoryName(path)!, $"modupdate-{mod.GetHashCode()}.zip");
            
            // download it...
            AppLogger.Log.Information($"Downloading to {zipPath}");
            bool success = false;
            bool finished = false;
            foreach (string url in remoteFileInfo.DownloadUrl!) {
                try {
                    AppLogger.Log.Information($"Trying {url}");
                    bool b = await Web.DownloadFileWithProgress(url, zipPath,
                        tickCallback);
                    finished = b;
                    success = true;
                    break;
                } catch (Exception e) {
                    const int retryDelaySeconds = 3;
                    AppLogger.Log.Error($"Download failed, attempting again in {retryDelaySeconds}s");
                    AppLogger.Log.Error(e, e.Message);
                    finishCallback.Invoke(false, false);
                    await Task.Delay(retryDelaySeconds*1000);
                } 
            }

            
            if (!success || !finished) {
                if (!success)
                    // update failed
                    AppLogger.Log.Error($"Installing/Updating {mod.Name} failed");
                else {
                    // update canceled
                    AppLogger.Log.Warning($"Installing/Updating {mod.Name} was canceled");
                }
                finishCallback.Invoke(success, finished);

                // try to delete mod-update.zip if it still exists.
                TryDelete(zipPath);
                return;
            }

            // verify its checksum
            VerifyChecksum(remoteFileInfo, zipPath);

            // install it
            InstallModUpdate(mod, zipPath);

            // done!
            finishCallback.Invoke(success, finished);
        }

        // The following 3 methods are copied from Everest (ModUpdaterHelper.cs:131)

        /// <summary>
        /// Verifies the downloaded mod's checksum, and throws an IOException if it doesn't match the database one.
        /// </summary>
        /// <param name="update">The mod info from the database</param>
        /// <param name="filePath">The path to the file to check</param>
        private static void VerifyChecksum(ModAPI.IModFileInfo update, string filePath) {
            string actualHash = CalculateChecksum(filePath);
            
            if (update.IsLocal) AppLogger.Log.Error("Useless verify, local mod info provided");
            string expectedHash = update.Hash;
            AppLogger.Log.Information($"Verifying checksum: actual hash is {actualHash}, expected hash is {expectedHash}");
            if (expectedHash != actualHash) {
                throw new IOException($"Checksum error: expected {expectedHash}, got {actualHash}");
            }
        }

        /// <summary>
        /// Installs a mod update in the Mods directory once it has been downloaded.
        /// This method will replace the installed mod zip with the one that was just downloaded.
        /// </summary>
        /// <param name="mod">The mod metadata from Everest for the installed mod</param>
        /// <param name="zipPath">The path to the zip the update has been downloaded to</param>
        private static void InstallModUpdate(ModAPI.IModFileInfo mod, string zipPath) {
            string destPath;
            if (!mod.IsLocal) {
                destPath = Path.Combine(Path.GetDirectoryName(zipPath) ?? throw new InvalidOperationException(), mod.Name + ".zip");
            } else {
                destPath = mod.Path!;
            }

            if (File.Exists(destPath)) {
                // delete the old zip, and move the new one.
                AppLogger.Log.Information($"Deleting mod .zip: {destPath}");
                File.Delete(destPath);
            }

            AppLogger.Log.Information($"Moving {zipPath} to {destPath}");
            File.Move(zipPath, destPath);
        }

        /// <summary>
        /// Tries deleting a file if it exists.
        /// If deletion fails, an error is written to the log.
        /// </summary>
        /// <param name="path">The path to the file to delete</param>
        private static void TryDelete(string path) {
            if (!File.Exists(path)) return;
            try {
                AppLogger.Log.Information($"Deleting file {path}");
                File.Delete(path);
            } catch (Exception) {
                AppLogger.Log.Warning($"Removing {path} failed");
            }
        }

        public static string CalculateChecksum(string filePath) {
            using (FileStream modFile = File.OpenRead(filePath))
                return BitConverter.ToString(BitConverter.GetBytes(xxHash64.ComputeHash(modFile))
                    .Reverse().ToArray()).Replace("-", "").ToLowerInvariant(); 
        }

        public static class Jobs {
            public static WorkingOnItScene.Job GetInstallModJob(ModAPI.RemoteModInfoAPI.RemoteModFileInfo mod) {
                return new WorkingOnItScene.Job(() => JobFunc(mod), "download_rot");

                static async IAsyncEnumerable<EverestInstaller.Status> JobFunc(ModAPI.RemoteModInfoAPI.RemoteModFileInfo mod) {
                    Channel<(string, float)> chan = Channel.CreateUnbounded<(string, float)>();
                    Task.Run<Task>(async () => {
                        try {
                            bool wasSuccessful = false;
                            DateTime lastUpdate = DateTime.Now;
                            await UpdateMod(mod, (pos, length, speed) => {
                                if (lastUpdate.Add(TimeSpan.FromSeconds(1)).CompareTo(DateTime.Now) < 0) {
                                    chan.Writer.TryWrite(($"Downloading... {pos*100F/length}% {speed} Kib/s {pos}", (float) pos / length));
                                    lastUpdate = DateTime.Now;
                                }

                                return true;
                            }, (success, finished) => {
                                wasSuccessful = success && finished;
                                if (success) {
                                    chan.Writer.TryWrite(("Downloading... 100% 0Kb/s", 1F)); // i love faking messages :)
                                } else {
                                    chan.Writer.TryWrite(("Download failed! Attempting next source", 0F));
                                }
                            });
                            if (wasSuccessful)
                                chan.Writer.TryWrite(("Mod downloaded successfully!", 1f));
                            else
                                chan.Writer.TryWrite(("Failed to update mod!", -1f));
                        } catch (Exception e) {
                            chan.Writer.TryWrite((e.ToString(), -1));
                            chan.Writer.TryWrite(("Failed to update mod!", -1f));
                            AppLogger.Log.Error(e, e.Message);
                        }
                        
                        chan.Writer.Complete();
                    });
                    
                    while (await chan.Reader.WaitToReadAsync())
                    while (chan.Reader.TryRead(out (string, float) item)) {
                        if (item.Item2 >= 0)
                            yield return new EverestInstaller.Status(item.Item1, item.Item2,
                                item.Item2 != 1f
                                    ? EverestInstaller.Status.Stage.InProgress
                                    : EverestInstaller.Status.Stage.Success);
                        else
                            yield return new EverestInstaller.Status(item.Item1, 1f,
                                EverestInstaller.Status.Stage.Fail);
                    }
                }
            }
        }
        
    }
}