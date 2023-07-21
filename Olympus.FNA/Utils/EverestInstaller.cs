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
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using JsonException = Newtonsoft.Json.JsonException;

namespace Olympus.Utils {
    public static class EverestInstaller {

        private static readonly TimedCache<ICollection<EverestVersion>> everestVersionCache =
            new TimedCache<ICollection<EverestVersion>>(new(0, 5, 0),
                o => {
                    string jsonData = UrlManager.Urls.EverestVersions.TryHttpGetDataString(new List<string>{"includeCore"});
                    List<EverestVersion>? versions = JsonConvert.DeserializeObject<List<EverestVersion>>(jsonData);
                    return versions ?? throw new JsonException("Couldn't parse json!");
                }, null);

        public static ICollection<EverestVersion> QueryEverestVersions() {
            return everestVersionCache.Value;
        }

        // Note: here the progress values will be: 50% of it for the download, and 50% for the install process
        public static async IAsyncEnumerable<Status> InstallVersion(EverestVersion version, Installation install) {
            // 1st part: the download
            (bool modifiable, string? full, Version? celesteVersion, string? framework, string? modLoaderName, Version? everestVersion) = install.ScanVersion(true);
            if (!modifiable) {
                yield return new Status("Installation is not modifiable", 1f, Status.Stage.Fail);
            }
            
            if (everestVersion != null) {
                bool canDelete = true;
                // Before everything, verify if a everest cache can be deleted
                foreach (Installation ins in
                         App.Instance.FinderManager.Found.Concat(App.Instance.FinderManager.Added)) {
                    (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName,
                            Version? ModVersion)
                        = ins.ScanVersion(false);
                    if (ModVersion == null) continue;
                    if (ModVersion.Minor != everestVersion.Minor) continue;
                    canDelete = false;
                    break;
                }

                string cacheFile = Path.Combine(Config.GetCacheDir(), "everestVersions",
                                                $"everest_{everestVersion.Minor}.zip");
                if (canDelete && File.Exists(cacheFile)) {
                    File.Delete(cacheFile);
                }
            }

            // MiniInstaller reads orig/Celeste.exe and copies Celeste.exe into it but only if missing.
            // We can help out and delete the orig folder if the existing Celeste.exe isn't modded.
            if (modLoaderName == null) { // Install is vanilla
                string orig = Path.Combine(install.Root, "orig");
                if (Directory.Exists(orig)) {
                    yield return new Status("Deleting previous backup", 0f, Status.Stage.InProgress);
                    Directory.Delete(orig, true);
                }
            }

            // Here "Native" is used as a synonym for "from the core branch"
            bool isNativeCurrentInstall = File.Exists(Path.Combine(install.Root, "Celeste.dll"));
            bool isNativeArtifact = !version.Branch.IsNonNative;

            // Uninstall only if caches are present for this install
            if (everestVersion != null && 
                File.Exists(Path.Combine(
                    Config.GetCacheDir(), "uninstallData",
                    ModList.ModDataBase.ValidateName(install.Root) + everestVersion.Minor + ".yaml"))) {
                yield return new Status("Uninstalling current version...", 0f, Status.Stage.InProgress);
                await foreach (Status status in UninstallEverest(install)) yield return status;
                
                (modifiable, full, celesteVersion, framework, modLoaderName, everestVersion) = install.ScanVersion(true);
            }

            using HttpClient wc = new HttpClient();
            wc.Timeout = TimeSpan.FromMilliseconds(10000); // 10 s timeout
            
            // The following uses a channel to be able to yield return data from the lambda to the main method
            // Note that it runs the job async, but the channel wont die until it finishes. 
            Channel<Status> statusChannel = Channel.CreateUnbounded<Status>();

            if (!Directory.Exists(Path.Combine(Config.GetCacheDir(), "everestVersions"))) {
                Directory.CreateDirectory(Path.Combine(Config.GetCacheDir(), "everestVersions"));
            }
            
            string outFile = Path.Combine(Config.GetCacheDir(), "everestVersions",
                                $"everest_{version.version}.zip");
            if (!File.Exists(outFile)) { // Download only if necessary
                Task<bool> job = UrlManager.Stream2FileWithProgress(outFile,
                    wc.GetStreamAsync(version.mainDownload), version.mainFileSize,
                    (progress, total, speed) => {
                        statusChannel.Writer.TryWrite(new Status(
                            $"Downloading files: {(float) progress / total * 100}%, {speed} Kb/s",
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
            }

            // Finally extract it
            foreach (Status status in UnpackTo(outFile, install.Root, "main/")) yield return status;
            
            // Right before miniinstaller, analyze the files
            HashSet<string>? initialFiles = null; 
            if (modLoaderName == null) // only if it was vanilla generate caches
                initialFiles = new(GetFilesInDirectory(install.Root));
                // That *can* and *will* be expensive on slow storage devices, but since installing is rarely done, we might as well not care

            // 2nd part: the installation
            yield return new Status("Running miniInstaller", 0.5f, Status.Stage.InProgress);
            await foreach (Status s in RunMiniInstaller(install, SetupAndGetMiniInstallerName(install, isNativeArtifact))) {
                yield return s;
            }

            if (modLoaderName == null) { // Only if vanilla make caches
                // List files again
                IEnumerable<string> finalFiles = GetFilesInDirectory(install.Root);

                // Find overlap
                LinkedList<string> newFiles = new();
                foreach (string file in finalFiles) {
                    if (!initialFiles!.Contains(file)) { // initialFiles cannot be null here
                        newFiles.AddLast(Path.GetRelativePath(install.Root, file));
                    }
                }

                string installCacheDir = Path.Combine(Config.GetCacheDir(), "uninstallData");
                if (!Directory.Exists(installCacheDir)) {
                    Directory.CreateDirectory(installCacheDir);
                }

                await using (FileStream file = File.Create(Path.Combine(
                                 installCacheDir,
                                 ModList.ModDataBase.ValidateName(install.Root) + version.version + ".yaml")))
                await using (TextWriter writer = new StreamWriter(file))
                    YamlHelper.Serializer.Serialize(writer, newFiles);
                yield return new Status("Caches done!", 1f, Status.Stage.Success);
            }
        }

        private static string SetupAndGetMiniInstallerName(Installation install, bool isNative) {
            string binaryName;
            if (PlatformHelper.Is(Platform.Windows)) {
                binaryName = isNative ? 
                                 PlatformHelper.Is(Platform.Bits64) ? 
                                 "MiniInstaller-win64.exe" 
                                 : "MiniInstaller-win.exe" 
                             : "MiniInstaller.exe";
                return binaryName;
            }

            if (PlatformHelper.Is(Platform.Linux)) {
                if (isNative) {
                    binaryName = "MiniInstaller-linux";
                    chmod(Path.Combine(install.Root, binaryName), Convert.ToInt32("0755", 8));
                    return binaryName;
                }

                string kickStartName = PlatformHelper.Is(Platform.Bits64)
                    ? "Celeste.bin.x86_64"
                    : "Celeste.bin.x86";
                
                binaryName = PlatformHelper.Is(Platform.Bits64) ? 
                    "MiniInstaller.bin.x86_64" 
                    : "MiniInstaller.bin.x86";
                // we'll use the monokickstart that celeste uses to run miniinstaller
                if (File.Exists(Path.Combine(install.Root, kickStartName))) {
                    if (!File.Exists(Path.Combine(install.Root, binaryName))) {
                        File.Copy(Path.Combine(install.Root, kickStartName),
                            Path.Combine(install.Root, binaryName));
                    }
                } else { // Assume that its core
                    string[] filesToCopy = { kickStartName, "monoconfig", "monomachineconfig" };
                    foreach (string file in filesToCopy) {
                        if (File.Exists(Path.Combine(install.Root, "orig/" + file))) { // Core moves the kickstart to the orig folder
                            if (!File.Exists(Path.Combine(install.Root, file))) {
                                File.Copy(Path.Combine(install.Root, "orig/" + file),
                                    Path.Combine(install.Root, file));
                            }
                        } else {
                            throw new FileNotFoundException(
                                $"File {file} does not exist on orig folder, is it non core?");
                        }
                    }
                }

                return binaryName; // Note: no chmod needed here since the celeste kickstart will already be executable and it'll be kept
            }

            if (PlatformHelper.Is(Platform.MacOS)) {
                if (isNative) {
                    binaryName = "MiniInstaller-osx";
                    chmod(Path.Combine(install.Root, binaryName), 0755);
                    return binaryName;
                }
                
                binaryName = "MiniInstaller.bin.osx";
                
                // we'll use the monokickstart that celeste uses to run miniinstaller
                if (File.Exists(Path.Combine(install.Root, "Celeste.bin.osx"))) {
                    if (!File.Exists(Path.Combine(install.Root, binaryName))) {
                        File.Copy(Path.Combine(install.Root, "Celeste.bin.osx"),
                            Path.Combine(install.Root, binaryName));
                    }
                } else {
                    throw new FileNotFoundException("Celeste monokickstart missing! (is it non native?)");
                }

                return binaryName; // Note: no chmod needed here since the celeste kickstart will already be executable and it'll be kept
            }

            throw new PlatformNotSupportedException("Unable to recognize platform! (manual install required)");
        }
        
        

        // Uninstall process... This is a huge method
        // Steps:
        // 1. Get a file listing for stock celeste
        // 2. Compare it to the currently installed everest version zip and delete the ones which aren't part of the celeste stock install
        // 3. Try using a cache to determinate those files that miniinstaller generated and delete them
        // Simple, right?
        public static async IAsyncEnumerable<Status> UninstallEverest(Installation install) {
            (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) 
                                = install.ScanVersion(true);
            if (ModVersion == null) {
                yield return new Status("Tried to uninstall from vanilla install", 1f, Status.Stage.Fail);
                yield break;
            }
            string file_listing = "";
            if (PlatformHelper.Is(Platform.Linux)) {
                file_listing = "celeste_files_linux.yaml";
            } else if (PlatformHelper.Is(Platform.MacOS)) {
                file_listing = "celeste_files_mac.yaml";
            } else if (PlatformHelper.Is(Platform.Windows)) {
                file_listing = "celeste_files_windows.yaml";
            } else {
                throw new PlatformNotSupportedException("Cannot deduce OS!"); // Note: intentional exception throw
            }

            Stream? file = OlympUI.Assets.OpenStream(Path.Combine("metadata", "celeste-files-listings", file_listing));
            if (file == null) {
                yield return new Status("Cannot open celeste file listings!", 1f, Status.Stage.Fail);
                yield break;
            }

            List<string> celesteFiles;
            using (StreamReader reader = new(file))
               celesteFiles = YamlHelper.Deserializer.Deserialize<List<string>>(reader);

            string everestCache = Path.Combine(Config.GetCacheDir(), "everestVersions", $"everest_{ModVersion.Minor}.zip");
            if (!File.Exists(everestCache)) { // Download it
                EverestVersion? version = null;
                foreach (EverestVersion ver in QueryEverestVersions()) {
                    if (ver.version == ModVersion.Minor) {
                        version = ver;
                    }
                }

                if (version == null) {
                    yield return new Status("Couldn't deduce current version, (is it too old?)", 1f, Status.Stage.Fail);
                    yield break;
                }

                using HttpClient wc = new HttpClient();
                wc.Timeout = TimeSpan.FromMilliseconds(10000); // 10 s timeout
                
                // The following uses a channel to be able to yield return data from the lambda to the main method
                // Note that it runs the job async, but the channel wont die until it finishes. 
                Channel<Status> statusChannel = Channel.CreateUnbounded<Status>();
    
                if (!Directory.Exists(Path.Combine(Config.GetCacheDir(), "everestVersions"))) {
                    Directory.CreateDirectory(Path.Combine(Config.GetCacheDir(), "everestVersions"));
                }
                
                Task<bool> job = UrlManager.Stream2FileWithProgress(everestCache,
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
            } else {
                yield return new Status("Cache found!", 0.5f, Status.Stage.InProgress);
            }

            yield return new Status("Restoring orig", 0.5f, Status.Stage.InProgress);

            void CopyFilesRecursively(string path, string orig, string target) {
                if (!File.Exists(path) && !Directory.Exists(path)) return;
                FileInfo info = new(path);
                if (info.LinkTarget != null) return; // Skip symbolic links
                string targetFile = Path.Combine(target, Path.GetRelativePath(orig, path));
                if (Directory.Exists(path)) {
                    Directory.CreateDirectory(targetFile);
                    foreach (string entry in Directory.EnumerateFileSystemEntries(path)) {
                        CopyFilesRecursively(entry, orig, target);
                    }
                } else {
                    File.Copy(path, targetFile, true);
                }
            }

            string[] origFiles = Directory.GetFileSystemEntries(Path.Combine(install.Root, "orig"));
            int i = 0;
            foreach (string origFile in origFiles) {
                yield return new Status($"Copying files... ({i}/{origFiles.Length})",
                     0.5f + 0.25f * ((float) i / origFiles.Length), Status.Stage.InProgress);
                CopyFilesRecursively(origFile, Path.Combine(install.Root, "orig"), install.Root);
                i++;
            }
            yield return new Status($"Copied files! ({i}/{origFiles.Length})",
                                 0.5f + 0.25f * ((float) i / origFiles.Length), Status.Stage.InProgress);

            void DirectoryCleaning(string path) {
                string? parentDir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(parentDir)) return;
                string targetDir = Path.Combine(install.Root, parentDir);
                if (!Directory.Exists(targetDir)
                    || Directory.EnumerateFileSystemEntries(targetDir).Any()) return;
                Directory.Delete(targetDir);
                DirectoryCleaning(parentDir);
            }

            ZipArchive zip = new ZipArchive(File.OpenRead(everestCache));

            i = 0;
            string zipPrefix = "main/";
            foreach (ZipArchiveEntry entry in zip.Entries) {
                string name = entry.FullName;
                if (string.IsNullOrEmpty(name) || name.EndsWith("/"))
                    continue;

                if (!string.IsNullOrEmpty(zipPrefix)) {
                    if (!name.StartsWith(zipPrefix))
                        continue;
                    name = name.Substring(zipPrefix.Length);
                }

                if (!celesteFiles.Contains(name) && File.Exists(Path.Combine(install.Root, name))) {
                    File.Delete(Path.Combine(install.Root, name));
                    yield return new Status($"Deleting file {name} ({i}/{zip.Entries.Count})",
                        0.75f + 0.125f * ((float) i / zip.Entries.Count), Status.Stage.InProgress);
                    DirectoryCleaning(name);
                }
                i++;
            }
            
            // Finally check for caches, and delete final files
            string cacheTarget = Path.Combine(Config.GetCacheDir(), "uninstallData",
                ModList.ModDataBase.ValidateName(install.Root) + ModVersion.Minor + ".yaml");

            if (File.Exists(cacheTarget)) {
                yield return new Status("Cleaning residual files...", 0.875f, Status.Stage.InProgress);
                List<string> residualFiles = YamlHelper.Deserializer.Deserialize<List<string>>(new StreamReader(File.OpenRead(cacheTarget)));

                i = 0;
                foreach (string residualFile in residualFiles) {
                    string absolutePathFile = Path.Combine(install.Root, residualFile);
                    if (!File.Exists(absolutePathFile)) continue;
                    File.Delete(absolutePathFile);
                    yield return new Status($"Deleting file {absolutePathFile} ({i}/{residualFiles.Count}",
                        0.875f + 0.125f * ((float) i / residualFiles.Count), Status.Stage.InProgress);
                    DirectoryCleaning(residualFile);
                    i++;
                }
            } else {
                yield return new Status("Nonexistent cache for this install, uninstall may be incomplete!", 1f,
                    Status.Stage.InProgress);
            }

            yield return new Status("Success!", 1f, Status.Stage.Success);

            bool canDelete = true;
            // After everything, verify if a everest cache can be deleted
            foreach (Installation ins in App.Instance.FinderManager.Found.Concat(App.Instance.FinderManager.Added)) {
                (bool Modifiable1, string Full1, Version? Version1, string? Framework1, string? ModName1, Version? ModVersion1) 
                    = ins.ScanVersion(false);
                if (ModVersion1 == null) continue;
                if (ModVersion1.Minor != ModVersion.Minor) continue;
                canDelete = false;
                break;
            }
            
            if (canDelete && File.Exists(everestCache)) {
                File.Delete(everestCache);
            }
            
            
            install.ScanVersion(true);
            Config.Instance.Installation = Config.Instance.Installation; // neat hack to update all ui stuff
            
        }


        private static async IAsyncEnumerable<Status> RunMiniInstaller(Installation install, string binaryName) {
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


            install.ScanVersion(true);
            Config.Instance.Installation = Config.Instance.Installation; // neat hack to update all ui stuff
        }
        
        private static IEnumerable<Status> UnpackTo(string targetFile, string outDir, string zipPrefix = "") {
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

                string to = Path.Combine(outDir, name);
                Console.Out.WriteLine($"{name} -> {to}");

                if (File.Exists(to))
                    File.Delete(to);
                else if (!Directory.Exists(Path.GetDirectoryName(to)))
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);

                using (FileStream fs = File.OpenWrite(to))
                using (Stream compressed = entry.Open())
                    compressed.CopyTo(fs);
            }

            yield return new Status($"Unzipped {count} files", -1, Status.Stage.InProgress);
        }

        public static EverestBranch? DeduceBranch(Installation install) {
            (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) 
                = install.ScanVersion(true);
            if (ModVersion == null) return null;
            ICollection<EverestVersion> everestVersions = QueryEverestVersions();
            foreach (EverestVersion everestVersion in everestVersions) {
                if (everestVersion.version == ModVersion.Minor) { // everest version gets stored in minor
                    return everestVersion.Branch;
                }
            }

            return null;
        }

        public static EverestVersion? GetLatestForBranch(EverestBranch branch) {
            ICollection<EverestVersion> everestVersions = QueryEverestVersions();
            EverestVersion? latestFound = null;
            foreach (EverestVersion everestVersion in everestVersions) {
                if (everestVersion.Branch == branch 
                    && (latestFound == null ||  latestFound.version < everestVersion.version)) {
                    latestFound = everestVersion;
                }
            }

            return latestFound;
        }
        
        public static IEnumerable<string> GetFilesInDirectory(string dir) {
            foreach (string f in Directory.GetFiles(dir)) {
                yield return f;
            }
            foreach (string d in Directory.GetDirectories(dir)) {
                
                if (new DirectoryInfo(d).LinkTarget != null) continue;

                foreach (string s in GetFilesInDirectory(d)) 
                    yield return s;
            }
        }


#if WINDOWS
        private static int chmod(string pathname, int mode) { return 0; }
#else
        [DllImport("libc", SetLastError = true)]
        private static extern int chmod(string pathname, int mode);
#endif

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

            public EverestBranch Branch => EverestBranch.FromString(branch, branch != "core");
        }

        public class EverestBranch {
            public readonly ReleaseType type;
            public readonly bool IsNonNative;

            public static EverestBranch FromString(string name, bool isNonNative) {
                switch (name) {
                    case "stable":
                        return new EverestBranch(ReleaseType.Stable, isNonNative);
                    case "beta":
                        return new EverestBranch(ReleaseType.Beta, isNonNative);
                    case "dev" or "core":
                        return new EverestBranch(ReleaseType.Dev, isNonNative);
                    default:
                        throw new Exception($"Unknown name for branch: {name}.");
                }
            }

            public EverestBranch(ReleaseType type, bool isNonNative) {
                this.type = type;
                IsNonNative = isNonNative;
            }

            public override string ToString() {
                return type.ToString();
            }

            public override bool Equals(object? obj) {
                if (obj is not EverestBranch branch) return false;
                if (type != branch.type) return false;
                return IsNonNative == branch.IsNonNative;
            }

            public enum ReleaseType {
                Stable,
                Beta,
                Dev
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
