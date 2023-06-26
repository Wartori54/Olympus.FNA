using MonoMod.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Olympus.Utils {
    public static class EverestInstaller {

        public static ICollection<EverestVersion>? QueryEverestVersions() {
            string jsonData = UrlManager.Urls.EverestVersions.TryHttpGetDataString(new List<string>{"includeCore"});
            List<EverestVersion>? versions = JsonConvert.DeserializeObject<List<EverestVersion>>(jsonData);
            return versions;
        }

        // Note: here the progress values will be: 50% of it for the download, and 50% for the install process
        public static async IAsyncEnumerable<Status> InstallVersion(EverestVersion version, Installation install) {
            
            // 1st part: the download
            (bool modifiable, string? full, Version? celesteVersion, string? framework, string? modLoaderName, Version? everestVersion) = install.ScanVersion(true);
            if (!modifiable) {
                yield return new Status("Installation is not mod-able", 1f, Status.Stage.Fail);
            }

            // MiniInstaller reads orig/Celeste.exe and copies Celeste.exe into it but only if missing.
            // Olympus can help out and delete the orig folder if the existing Celeste.exe isn't modded.
            if (modLoaderName == null) { // Install is vanilla
                string orig = Path.Combine(install.Root, "orig");
                if (Directory.Exists(orig)) {
                    yield return new Status("Deleting previous backup", 0f, Status.Stage.InProgress);
                    Directory.Delete(orig, true);
                }
            }

            // Here "Native" is used as a synonym for "from the core branch"
            bool isNativeCurrentInstall = File.Exists(Path.Combine(install.Root, "Celeste.dll"));
            bool isNativeArtifact = version.Branch == EverestBranch.Core;

            using HttpClient wc = new HttpClient();
            wc.Timeout = TimeSpan.FromMilliseconds(10000); // 10 s timeout
            
            // The following uses a channel to be able to yield return data from the lamba to the main method
            // Note that it runs the job async, but the channel wont die until it finishes. 
            Channel<Status> statusChannel = Channel.CreateUnbounded<Status>();
            
            string outFile = Path.Combine(install.Root, "everest_update.zip");
            Task<bool> job = UrlManager.Stream2FileWithProgress(outFile,
                wc.GetStreamAsync(version.mainDownload), version.mainFileSize,
                (progress, total, speed) => {
                    statusChannel.Writer.TryWrite(new Status($"Downloading files: {(float) progress/total*100}%, {speed} Kb/s", 
                        (float) progress / total, Status.Stage.InProgress));
                    if (progress == total) {
                        statusChannel.Writer.TryComplete();
                    }
                    
                    return true; // no cancellation (yet)
                });

            while (await statusChannel.Reader.WaitToReadAsync())
                while (statusChannel.Reader.TryRead(out Status? s)) {
                     Status temp = new(s.Text, s.Progress / 2, s.CurrentStage);
                     yield return temp;
                }

            job.Wait(); // Wait anyways to prevent jank
            
            // Finally extract it
            foreach (Status status in UnpackThere(outFile, "main/")) yield return status;

            // 2nd part: the installation
            yield return new Status("Running miniInstaller", 0.5f, Status.Stage.InProgress);
            if (isNativeArtifact)
                foreach (Status s in RunMiniInstallerNative(install))
                    yield return s;
            else
                await foreach (Status s in RunMiniInstaller(install)) {
                    yield return s;
                }
            
            // Delete the zip file
            File.Delete(outFile);
        }


        private static async IAsyncEnumerable<Status> RunMiniInstaller(Installation install) {
            string binaryName = "";
            if (PlatformHelper.Is(Platform.Windows)) {
                binaryName = "MiniInstaller.exe";
            } else if (PlatformHelper.Is(Platform.Linux)) {
                binaryName = "MiniInstaller.bin.x86_64";
                // we'll use the monokickstart that celeste uses to run miniinstaller
                if (File.Exists(Path.Combine(install.Root, "Celeste.bin.x86_64"))) {
                    if (!File.Exists(Path.Combine(install.Root, binaryName))) {
                        File.Copy(Path.Combine(install.Root, "Celeste.bin.x86_64"),
                            Path.Combine(install.Root, binaryName));
                        
                    }
                } else {
                    yield return new Status("Celeste monokickstart missing! (is it non native?)", 1f,
                        Status.Stage.Fail);
                }
            } else if (PlatformHelper.Is(Platform.MacOS)) {
                binaryName = "MiniInstaller.bin.osx";
                 // we'll use the monokickstart that celeste uses to run miniinstaller
                 if (File.Exists(Path.Combine(install.Root, "Celeste.bin.osx"))) {
                     if (!File.Exists(Path.Combine(install.Root, binaryName))) {
                         File.Copy(Path.Combine(install.Root, "Celeste.bin.osx"),
                             Path.Combine(install.Root, binaryName));
                     }
                 } else {
                     yield return new Status("Celeste monokickstart missing! (is it non native?)", 1f,
                         Status.Stage.Fail);
                 }
            } else {
                yield return new Status("Unable to recognize platform! (manual install required)", 1f, Status.Stage.Fail);
                yield break;
            }
                
            ProcessStartInfo config = new() {
                WorkingDirectory = install.Root,
                CreateNoWindow = true,
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                FileName = Path.Combine(install.Root, binaryName),
            };
            
            Channel<(string, bool)> processStdData = Channel.CreateUnbounded<(string, bool)>();
            Process process = new();
            process.StartInfo = config;
            bool stdError = false;
            // The following code has the goal to pipe the stdout and stderr to the status ienumerable this method returns
            // It uses a channel that will get killed once the process finishes in the async task,
            // and meanwhile the process is alive, it'll pipe the data.
            process.OutputDataReceived += (sender, e) => {
                if (e.Data == null) return;
                processStdData.Writer.TryWrite((e.Data, false));
            };
            process.ErrorDataReceived += (sender, e) => {
                if (e.Data == null) return;
                processStdData.Writer.TryWrite((e.Data ?? "", true));
                stdError = true; // Assume fail if there's any stdError data
            };



            (bool, Exception?) startSuccess = (true, null);
            try { // Those are the only statements that can throw
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            } catch (Exception e) {
                startSuccess = (false, e);
            }

            if (!startSuccess.Item1) { // yield return on catch block is not allowed, so this is a workaround
                 yield return new Status("Starting miniinstaller failed!", 1f, Status.Stage.Fail);
                 yield return new Status(startSuccess.Item2!.ToString(), 1f, Status.Stage.Fail);
                 yield break; 
            }
            
            
            // The following is intentional, we want to wait and kill the channel asynchronously.
#pragma warning disable CS4014 
            Task.Run(() => {
#pragma warning restore CS4014
                process.WaitForExit();
                processStdData.Writer.TryComplete();
            });

            int i = 0;
            // Lets come up with a progress value since we miniinstaller doesn't give it to us
            // MiniInstaller outputs exactly 42 lines, as of writing this, before ending, so lets return almost 100% at 42 lines
            // The used function is -1.06^(-x+0.5)+1
            while (await processStdData.Reader.WaitToReadAsync())
            while (processStdData.Reader.TryRead(out (string, bool) entry)) {
                 yield return new Status(entry.Item1, (float)(-Math.Pow(1.1, -i+0.5)+1), entry.Item2 ? Status.Stage.Fail : Status.Stage.InProgress);
                 i++;               
            }

            if (stdError) {
                yield return new Status("Miniinstaller error, see above!", 1f, Status.Stage.Fail);
                yield break;
            }

            // Reaching here means install has finished (and its successful)
            yield return new Status("Everest install finished", 1f, Status.Stage.Success);


            Config.Instance.Installation?.ScanVersion(true);
            Config.Instance.Installation = Config.Instance.Installation; // neat hack to update all ui stuff
        }
        
        private static IEnumerable<Status> RunMiniInstallerNative(Installation install) {
             yield break;
        }

        private static IEnumerable<Status> UnpackThere(string targetFile, string zipPrefix = "") {
            ZipArchive zip = new(File.OpenRead(targetFile));
            
            int count = zip.Entries.Count(entry => entry.FullName.StartsWith(zipPrefix));
            int i = 0;

            yield return new Status($"Unzipping {count} files", -1f, Status.Stage.InProgress);

            foreach (ZipArchiveEntry entry in zip.Entries) {
                string name = entry.FullName;
                if (string.IsNullOrEmpty(name) || name.EndsWith("/"))
                    continue;

                if (!string.IsNullOrEmpty(zipPrefix)) {
                    if (!name.StartsWith(zipPrefix))
                        continue;
                    name = name.Substring(zipPrefix.Length);
                }

                yield return new Status($"Unzipping #{i} / {count}: {name}", -1f, Status.Stage.InProgress);
                i++;

                string to = Path.Combine(Path.GetDirectoryName(targetFile)!, name);
                Console.Out.WriteLine($"{name} -> {to}");

                if (File.Exists(to))
                    File.Delete(to);

                using (FileStream fs = File.OpenWrite(to))
                using (Stream compressed = entry.Open())
                    compressed.CopyTo(fs);
            }

            yield return new Status($"Unzipped {count} files", -1, Status.Stage.InProgress);
        }



        [Serializable]
        public class EverestVersion {
            // [JsonIgnore]
            // public DateTime versionDate {
            //     get {
            //         if (DateTime.TryParseExact(date, "yyyy-MM-ddTHH:mm:ss.fffffffZ", null, DateTimeStyles.None, out DateTime parsedDate))
            //             return parsedDate;
            //         return DateTime.MinValue;
            //     }
            // }

            public DateTime date = new();
            public int mainFileSize;
            public string mainDownload = "";
            // public string olympusMetaDownload = ""; // This is commented on purpose
            public string author = "";
            // public string olympusBuildDownload = ""; // Since we wont be using those
            public string description = "";
            public string branch = "";
            public int version;

            public EverestBranch Branch => EverestBranch.FromString(branch);
        }

        public class EverestBranch {
            public static EverestBranch Stable = new("Stable");
            public static EverestBranch Beta = new("Beta");
            public static EverestBranch Dev = new("Dev");
            public static EverestBranch Core = new("Core");

            private readonly string asString;

            private EverestBranch(string asString) {
                this.asString = asString;
            }

            public static EverestBranch FromString(string str) {
                IEnumerable<FieldInfo> fieldInfos = typeof(EverestBranch).GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(f => f.FieldType == typeof(EverestBranch)).ToList();
                if (!fieldInfos.Any()) throw new FieldAccessException("No fields found in EverestBranch, (something is very wrong D:)");
                foreach (var fieldInfo in fieldInfos) {
                    EverestBranch everestBranch = (EverestBranch?) fieldInfo.GetValue(null) ?? throw new MissingFieldException("Couldn't cast field");
                    if (string.Equals(everestBranch.asString, str, StringComparison.InvariantCultureIgnoreCase))
                        return everestBranch;
                }

                throw new MissingFieldException($"Branch {str} not found!");

            }

            public override string ToString() {
                return asString;
            }
        }
        
        public class Status { // A simple class so hold status data
            public readonly string Text;
            public readonly float Progress;
            public readonly Stage CurrentStage;

            public Status(string text, float progress, Stage stage) {
                Text = text;
                Progress = progress;
                CurrentStage = stage;
            }
            
            public enum Stage {
                InProgress,
                Success,
                Fail,
                
            }
            
        }
    }
}
