using Standart.Hash.xxHash;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Olympus.Utils {
    public static class ModUpdater {
        
        public static void UpdateAllMods(Installation install) { //TODO: add update all button and auto update mods
            List<ModList.ModInfo> mods = ModList.GatherModList(install, true, true, true, false);

            bool TickCallback(int progress, long lenght, int speed) {
                return true;
            }

            void FinishCallback(bool success, bool isDone) {
                
            }
            
            foreach (var mod in mods) {
                UpdateMod(mod, TickCallback, FinishCallback);
            }
        }

        /// <summary>
        /// Updates a single mod
        /// </summary>
        /// <param name="mod">The populated mod info</param>
        /// <param name="tickCallback">Callback for download progress, parameters: (pos, lenght, speed) -> abort</param>
        /// <param name="finishCallback">Callback to determine outcome, parameters: (success, isDone)</param>
        public static void UpdateMod(ModList.ModInfo mod, Func<int, long, int, bool> tickCallback, Action<bool, bool> finishCallback) {
            
            // The following is copied and adapted from Everest (OuiModUpdateList.cs:257)
            Task<Task> job = new(async () => {
                if (mod.DbUpdateInfo == null) {
                    mod.DbUpdateInfo = ModList.DataBase.QueryUpdateInfo(mod);
                    if (mod.DbUpdateInfo == null) {
                        Console.WriteLine($"Cannot obtain update info for mod {mod.Name}({mod.Path})");
                        return;
                    }
                }
                // we will download the mod to Celeste_Directory/[update.GetHashCode()].zip at first.
                string zipPath = Path.Combine(Path.GetDirectoryName(mod.Path)!, $"modupdate-{mod.GetHashCode()}.zip");
                
                // download it...
                Console.WriteLine($"Downloading to {zipPath}");
                bool success = false;
                bool finished = false;
                List<string> sources = new() { mod.DbUpdateInfo.URL, mod.DbUpdateInfo.MirrorURL };
                foreach (string url in sources) {
                    try {
                        Console.WriteLine($"Trying {url}");
                        bool b = await DownloadFileWithProgress(url, zipPath,
                            tickCallback);
                        finished = b;
                        success = true;
                        break;
                    } catch (Exception e) {
                        Console.WriteLine("Download failed");
                        Console.WriteLine(e);
                        finishCallback.Invoke(false, false);
                        await Task.Delay(3000);
                    } 
                }

                
                finishCallback.Invoke(success, finished);
                if (!success || !finished) {
                    if (!success)
                        // update failed
                        Console.WriteLine($"Updating {mod.Name} failed");
                    else {
                        // update canceled
                        Console.WriteLine($"Updating {mod.Name} was canceled");
                    }

                    // try to delete mod-update.zip if it still exists.
                    TryDelete(zipPath);
                    return;
                }

                // verify its checksum
                VerifyChecksum(mod.DbUpdateInfo, zipPath);

                // install it
                InstallModUpdate(mod, zipPath);

                // done!
            });

            job.Start();
        }


        // The following is copied and adapted from Everest (Everest.Updater.cs:510)

        /// <summary>
        /// Downloads a file and calls the progressCallback parameter periodically with progress information.
        /// This can be used to display the download progress on screen.
        /// </summary>
        /// <param name="url">The URL to download the file from</param>
        /// <param name="destPath">The path the file should be downloaded to</param>
        /// <param name="progressCallback">A method called periodically as the download progresses. Parameters are progress, length and speed in KiB/s.
        /// Should return true for the download to continue, false for it to be cancelled.</param>
        private static async Task<bool> DownloadFileWithProgress(string url, string destPath, Func<int, long, int, bool> progressCallback) {
            DateTime timeStart = DateTime.Now;

            if (File.Exists(destPath))
                File.Delete(destPath);

            HttpClient request =  new();
            request.Timeout = TimeSpan.FromMilliseconds(10000);
//             // disable IPv6 for this request, as it is known to cause "the request has timed out" issues for some users
            // request.ServicePoint.BindIPEndPointDelegate = delegate (ServicePoint servicePoint, IPEndPoint remoteEndPoint, int retryCount) {
            //     if (remoteEndPoint.AddressFamily != AddressFamily.InterNetwork) {
            //         throw new InvalidOperationException("no IPv4 address");
            //     }
            //     return new IPEndPoint(IPAddress.Any, 0);
            // };

            // Manual buffered copy from web input to file output.
            // Allows us to measure speed and progress.
            using HttpResponseMessage response = await request.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            long length = response.Content.Headers.ContentLength ?? 0;
            await using Stream input = await response.Content.ReadAsStreamAsync();
            await using FileStream output = File.OpenWrite(destPath);
            if (length == 0) 
                Console.WriteLine("Cannot determine file length!");

            progressCallback(0, length, 0);

            byte[] buffer = new byte[4096];
            DateTime timeLastSpeed = timeStart;
            int read = 1;
            int readForSpeed = 0;
            int pos = 0;
            int speed = 0;
            while (read > 0) {
                int count = length > 0 ? (int) Math.Min(buffer.Length, length - pos) : buffer.Length;
                read = await input.ReadAsync(buffer, 0, count);
                output.Write(buffer, 0, read);
                pos += read;
                readForSpeed += read;

                TimeSpan td = DateTime.Now - timeLastSpeed;
                if (td.TotalMilliseconds > 100) {
                    speed = (int) ((readForSpeed / 1024D) / td.TotalSeconds);
                    readForSpeed = 0;
                    timeLastSpeed = DateTime.Now;
                }

                if (!progressCallback(pos, length, speed)) {
                    return false;
                }
            }

            return true;
        }

        // The following 3 methods are copied from Everest (ModUpdaterHelper.cs:131)

        /// <summary>
        /// Verifies the downloaded mod's checksum, and throws an IOException if it doesn't match the database one.
        /// </summary>
        /// <param name="update">The mod info from the database</param>
        /// <param name="filePath">The path to the file to check</param>
        private static void VerifyChecksum(ModList.ModDBUpdateInfo update, string filePath) {
            string actualHash = CalculateChecksum(filePath);
            
            string expectedHash = update.xxHash[0];
            Console.WriteLine($"Verifying checksum: actual hash is {actualHash}, expected hash is {expectedHash}");
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
        private static void InstallModUpdate(ModList.ModInfo mod, string zipPath) {
            // delete the old zip, and move the new one.
            Console.WriteLine($"Deleting mod .zip: {mod.Path}");
            File.Delete(mod.Path);

            Console.WriteLine($"Moving {zipPath} to {mod.Path}");
            File.Move(zipPath, mod.Path);
        }

        /// <summary>
        /// Tries deleting a file if it exists.
        /// If deletion fails, an error is written to the log.
        /// </summary>
        /// <param name="path">The path to the file to delete</param>
        private static void TryDelete(string path) {
            if (!File.Exists(path)) return;
            try {
                Console.WriteLine($"Deleting file {path}");
                File.Delete(path);
            } catch (Exception) {
                Console.WriteLine($"Removing {path} failed");
            }
        }

        public static string CalculateChecksum(string filePath) {
            using (FileStream modFile = File.OpenRead(filePath))
                return BitConverter.ToString(BitConverter.GetBytes(xxHash64.ComputeHash(modFile))
                    .Reverse().ToArray()).Replace("-", "").ToLowerInvariant(); 
        }
        
    }
}