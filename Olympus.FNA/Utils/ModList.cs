using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace Olympus.Utils {

    // Copied, refactored and heavily extended from https://github.com/EverestAPI/Olympus/blob/main/sharp/CmdModList.cs
    public static class ModList {

        public static readonly ModDataBase DataBase = new ModDataBase();

        public static List<ModInfo> GatherModList(bool readYamls, bool computeHashes, bool onlyUpdatable, bool excludeDisabled) {
            return Config.Instance.Installation == null 
                ? new List<ModInfo>() : GatherModList(Config.Instance.Installation, 
                    readYamls, computeHashes, onlyUpdatable, excludeDisabled);
        }

        public static List<ModInfo> GatherModList(Installation install, bool readYamls, bool computeHashes, bool onlyUpdatable, bool excludeDisabled) {
            string modsFolder = Path.Combine(install.Root, "Mods");
            if (!Directory.Exists(modsFolder)) 
                return new List<ModInfo>();
            
            List<string> blacklist;
            string blacklistPath = Path.Combine(modsFolder, "blacklist.txt");
            if (File.Exists(blacklistPath)) {
                blacklist = File.ReadAllLines(blacklistPath).Select(l => (l.StartsWith("#") ? "" : l).Trim()).ToList();
            } else {
                blacklist = new List<string>();
            }

            List<string> updaterBlacklist;
            string updaterBlacklistPath = Path.Combine(modsFolder, "updaterblacklist.txt");
            if (File.Exists(updaterBlacklistPath)) {
                updaterBlacklist = File.ReadAllLines(updaterBlacklistPath).Select(l => (l.StartsWith("#") ? "" : l).Trim()).ToList();
            } else {
                updaterBlacklist = new List<string>();
            }

            List<ModInfo> mods = new List<ModInfo>();
            List<ModInfo> zipMods = new List<ModInfo>();

            string[] allFiles = Directory.GetFiles(modsFolder);
            foreach (string file in allFiles) { // zips and bin(s)
                if (file.EndsWith(".zip")) {
                    if (file.Contains("modupdate")) continue;
                    // zip
                    if ((onlyUpdatable && updaterBlacklist.Contains(file)) || (excludeDisabled && blacklist.Contains(file)))
                        continue;
                    try {
                        ModInfo info = ParseZip(file, readYamls);
                        info.IsBlacklisted = blacklist.Contains(Path.GetFileName(file));
                        info.IsUpdaterBlacklisted = updaterBlacklist.Contains(Path.GetFileName(file));
                        zipMods.Add(info);
                    } catch (InvalidDataException e) {
                        AppLogger.Log.Error($"Zip: {file} is corrupted or unreadable");
                        AppLogger.Log.Error(e, e.Message);
                    }
                } else if (file.EndsWith(".bin") && !onlyUpdatable) { // quick reminder that bins and dir cannot be updated
                    // bin
                    ModInfo info = ParseBin(file);
                    info.IsBlacklisted = blacklist.Contains(Path.GetFileName(file));
                    info.IsUpdaterBlacklisted = updaterBlacklist.Contains(Path.GetFileName(file));
                    mods.Add(info);
                }
            }

            Dictionary<ModInfo, ModDBInfo> modZipDB = DataBase.QueryModDBInfoForMods(zipMods, true);

            foreach (ModInfo zipMod in zipMods) {
                if (modZipDB.TryGetValue(zipMod, out ModDBInfo? dbInfo)) {
                    zipMod.DbInfo = dbInfo;
                    zipMod.Description = dbInfo.Description;
                    if (DataBase.RawUpdateDataBase.TryGetValue(zipMod.Name, out ModDBUpdateInfo? updateInfo))
                        zipMod.NewVersion = updateInfo._VersionString;
                } else {
                    // Don't log since it has been already warned
                    // AppLogger.Log.Warning("ModDBInfo: Mod {0} did not appear on the mod data base", zipMod.Name);
                }
                mods.Add(zipMod);   
            }


            if (!onlyUpdatable) {
                string[] allDirs = Directory.GetDirectories(modsFolder);
                foreach (string dir in allDirs) {
                    if (Path.GetFileName(dir) == "Cache") continue;
                    // dir
                    ModInfo info = ParseDir(dir, readYamls);
                    info.IsBlacklisted = blacklist.Contains(Path.GetFileName(dir));
                    info.IsUpdaterBlacklisted = updaterBlacklist.Contains(Path.GetFileName(dir));
                    mods.Add(info);
                }
            }
            

            return mods;
        }

        private static ModInfo ParseZip(string file, bool readYamls) {
            ModInfo info = new() {
                Path = file,
                IsFile = true,
            };

            if (!readYamls) return info;
            using (FileStream zipStream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                zipStream.Seek(0, SeekOrigin.Begin);

                using (ZipArchive zip = new ZipArchive(zipStream, ZipArchiveMode.Read))
                using (Stream? stream = (zip.GetEntry("everest.yaml") ?? zip.GetEntry("everest.yml"))?.Open())
                using (StreamReader? reader = stream == null ? null : new StreamReader(stream))
                    info.Parse(reader);
            }

            // if (computeHashes && info.Name != null) {
            //     using (FileStream stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            //         info.Hash = BitConverter.ToString(Hasher.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            // }
            return info;
        }

        private static ModInfo ParseBin(string file) {
            ModInfo info = new ModInfo() {
                Path = file,
                IsFile = true,
            };
            // No info from bins
            return info;
        }

        private static ModInfo ParseDir(string dir, bool readYamls) {
            ModInfo info = new ModInfo() {
                Path = dir,
                IsFile = false,

            };

            if (!readYamls) return info;
            try {
                string yamlPath = Path.Combine(dir, "everest.yaml");
                if (!File.Exists(yamlPath))
                    yamlPath = Path.Combine(dir, "everest.yml");

                if (File.Exists(yamlPath)) {
                    using (FileStream stream = File.Open(yamlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader reader = new StreamReader(stream))
                        info.Parse(reader);
                }
            } catch (UnauthorizedAccessException) { /* skip errors */ }
            return info;
        }

        public static void BlackListUpdate(ModInfo mod) {
            if (Config.Instance.Installation == null) {
                AppLogger.Log.Warning("BlackListUpdate called before setting an install");
                return;
            }

            string modsFolder = Path.Combine(Config.Instance.Installation.Root, "Mods");
            string blacklistPath = Path.Combine(modsFolder, "blacklist.txt");
            List<string> blacklistString = File.ReadAllLines(blacklistPath).ToList();
            bool found = false;
            for (int i = 0; i < blacklistString.Count; i++) {
                if (!blacklistString[i].Contains(Path.GetFileName(mod.Path))) continue;
                if (!mod.IsBlacklisted)
                    blacklistString[i] = blacklistString[i].Insert(0, "# ");
                else
                    blacklistString[i] = blacklistString[i].Replace("#", "").Trim();
                found = true;
                break;
            }

            if (!found)
                blacklistString.Add((mod.IsBlacklisted ? "" : "# ") + Path.GetFileName(mod.Path));
            File.WriteAllLines(blacklistPath, blacklistString);
        }

        public class ModInfo {
            public string Path = "";
            public string Hash = "";
            public bool IsFile;
            public bool IsBlacklisted;
            public bool IsUpdaterBlacklisted;

            [YamlIgnore]
            public string Name {
                get {
                    if (name == "") {
                        return System.IO.Path.GetFileName(Path);
                    }

                    return name;
                }
            }

            [YamlMember(Alias = "Name")]
            public string name = "";
            public string Version = "";
            [YamlIgnore] // For updates
            public string? NewVersion = null;
            public string Description = "";
            public string DLL = "";
            public string[] Dependencies = {};
            public bool IsValid;
            [YamlIgnore]
            public ModDBInfo? DbInfo;

            public ModDBUpdateInfo? DbUpdateInfo;

            public void Parse(TextReader? reader) {
                try {
                    if (reader == null) return;
                    
                    List<EverestModuleMetadata> yaml = YamlHelper.Deserializer.Deserialize<List<EverestModuleMetadata>>(reader);
                    if (yaml != null && yaml.Count <= 0) return;
                    
                    name = yaml[0].Name;
                    Version = yaml[0].Version;
                    DLL = yaml[0].DLL;
                    if (yaml[0].Dependencies.Capacity != 0)
                        Dependencies = yaml[0].Dependencies.Select(dep => dep.Name).ToArray();

                    IsValid = Name != null && Version != null;
                } catch {
                    // ignore parse errors
                }
            }

            public override int GetHashCode() {
                // ReSharper disable twice NonReadonlyMemberInGetHashCode
                return Name.GetHashCode() ^ Hash.GetHashCode() ^ Version.GetHashCode();
                // hash alone would be enough, but name and version is also hashed for bug proofing
            }

            public override bool Equals(object? obj) {
                return obj != null && this.GetHashCode() == obj.GetHashCode();
            }
        }

        public class EverestModuleMetadata {
            public string Name = "";
            public string Version = "";
            public string DLL = "";
            public List<EverestModuleMetadata> Dependencies = new List<EverestModuleMetadata>();
        }

        public class ModDataBase {

            private static readonly string DBPath = Path.Join(Config.GetCacheDir(), "mod_search_database.yaml");

            private static readonly string ModCachePath = Path.Join(Config.GetCacheDir(), "ModCaches");

            // Performance related, may contain non existent hashes because of updating mods, its fine
            private static readonly Dictionary<ModInfo, string> CachedHashes = new();

            private readonly Dictionary<int, ModDBInfo> rawDataBase = new();
            // Maps gamebanana id to info
            public Dictionary<int, ModDBInfo> RawDataBase {
                get {
                    if (rawDataBase.Count == 0 || invalidateModDataBase)
                        this.DownloadModDataBase();
                    return rawDataBase;
                }
            }

            private Dictionary<string, ModDBUpdateInfo> rawUpdateDataBase = new();

            // Maps name to info
            public Dictionary<string, ModDBUpdateInfo> RawUpdateDataBase { 
                get {
                    if (rawUpdateDataBase.Count == 0) 
                        this.DownloadUpdateDataBase();
                    return rawUpdateDataBase;
                }
            }

            private bool invalidateModDataBase;

            private DateTime lastSearchDBRefresh = DateTime.MinValue;

            // minimum time between redownloading the DB
            private static readonly TimeSpan NoRedownloadInterval = new TimeSpan(0, 5, 0); // 5 min

            private void InvalidateModDatabase() {
                if (DateTime.Now - lastSearchDBRefresh <= NoRedownloadInterval) return;
                
                invalidateModDataBase = true;
                lastSearchDBRefresh = DateTime.Now;
            }
            
            // Downloads and loads the yaml containing everything in gamebanana
            private void DownloadModDataBase() {
                AppLogger.Log.Information("Populating DB, redownload: {0}", invalidateModDataBase);
                string yamlData = "";
                if (!invalidateModDataBase) {
                    if (File.Exists(DBPath))
                        yamlData = File.ReadAllText(DBPath);
                    else
                        InvalidateModDatabase();
                }
                if (invalidateModDataBase) {
                    AppLogger.Log.Information("Redownloading DB");
                    yamlData = UrlManager.Urls.ModDataBase.TryHttpGetDataString();
                    
                    File.WriteAllText(DBPath, yamlData);
                    AppLogger.Log.Information("Saved DB");
                }
                List<ModDBInfo> listDB = YamlHelper.Deserializer.Deserialize<List<ModDBInfo>>(yamlData);
                AppLogger.Log.Information("Deserialized db");
                rawDataBase.Clear();
                
                // We are forced to copy (i think)
                foreach (ModDBInfo entry in listDB) {
                    rawDataBase.Add(entry.GameBananaId, entry);
                }

                invalidateModDataBase = false;
            }

            // Download the everest_update.yaml, downloaded on each boot
            private void DownloadUpdateDataBase() {
                AppLogger.Log.Information("Downloading UpdateDB");
                string yamlData = UrlManager.Urls.ModUpdateDataBase.TryHttpGetDataString();
                
                rawUpdateDataBase = YamlHelper.Deserializer.Deserialize<Dictionary<string, ModDBUpdateInfo>>(yamlData);
                foreach (string name in rawUpdateDataBase.Keys) {
                    rawUpdateDataBase[name].Name = name;
                }
                AppLogger.Log.Information("Deserialized updaterDB");
            }

            // Returns the corresponding ModDBInfo for every ModInfo
            public Dictionary<ModInfo, ModDBInfo> QueryModDBInfoForMods(List<ModInfo> targetMods, bool cacheMods) {
                AppLogger.Log.Information("Querying Db");
                Dictionary<ModInfo, ModDBInfo> filteredMods = new(targetMods.Count);
                
                // Method explanation: We need to map ModInfo to ModDBInfo but unluckily theres no way to do that directly (as of now)
                // so we must rely on the everest_update.yaml, because it contains the names from everest.yaml from each mod and the 
                // gamebanana id for each mod, which ModDBInfo has as well
                // so in the end the connection is ModInfo -> ModDBUpdateInfo -> ModDBInfo

                // Check cache
                // Dict for performance
                Dictionary<string, ModInfo> mappedMods = new(targetMods.Count); // .Count possible performance optimization?
                foreach (ModInfo mod in targetMods) {
                    if (Path.GetFileName(mod.Path).Contains("modupdate")) continue; // skip leftover mod update files
                        
                    ModDBInfo? cachedInfo = QueryFromCache(mod);
                    if (cachedInfo != null) {
                        filteredMods.Add(mod, cachedInfo);
                        continue;
                    }
                    if (!mappedMods.TryAdd(mod.Name, mod)) // TODO: check hashes to determinate if they're the same version
                        AppLogger.Log.Warning($"Mod {mod.Name} from {mod.Path} is duplicate, skipping");
                }

                foreach (KeyValuePair<string, ModInfo> entry in mappedMods) {
                    if (RawUpdateDataBase.TryGetValue(entry.Key, out ModDBUpdateInfo? updateDBEntry)) {
                        int GBId = updateDBEntry.GameBananaId;
                        if (RawDataBase.TryGetValue(GBId, out ModDBInfo? dBInfo)) {
                            if (cacheMods) {
                                string validName = ValidateName(entry.Key);
                                AppLogger.Log.Information("Caching mod: {0}", entry.Key);
                                if (!Directory.Exists(ModCachePath))
                                    Directory.CreateDirectory(ModCachePath);
                                
                                if (CachedHashes.TryGetValue(entry.Value, out string? hash)) 
                                    dBInfo.Hash = hash;
                                else {
                                    dBInfo.Hash = ModUpdater.CalculateChecksum(entry.Value.Path);
                                    CachedHashes.Add(entry.Value, dBInfo.Hash);
                                }
                                
                                using (FileStream file = File.OpenWrite(Path.Join(ModCachePath, validName + ".yaml")))
                                using (StreamWriter writer = new StreamWriter(file))
                                    YamlHelper.Serializer.Serialize(writer, dBInfo);
                            }
                            filteredMods.Add(entry.Value, dBInfo);
                        } else {
                            AppLogger.Log.Warning("Mod {0} has match in updateDB but not in searchDB", entry.Key);
                        }
                    } else {
                        AppLogger.Log.Warning("Mod {0} not found in DB", entry.Key);
                    }
                }

                return filteredMods;
            }

            private ModDBInfo? QueryFromCache(ModInfo mod) {
                string filePath = Path.Join(ModCachePath, ValidateName(mod.Name) + ".yaml");
                if (!File.Exists(filePath)) return null;
                AppLogger.Log.Information("Queriyng from cache {0}... ", mod.Name);
                // Read cache
                ModDBInfo readData;
                using (StreamReader file = File.OpenText(filePath))
                    readData = YamlHelper.Deserializer.Deserialize<ModDBInfo>(file);
                    
                // Hash installed mod to detect updates
                string realHash;
                if (CachedHashes.TryGetValue(mod, out string? hash)) {
                    realHash = hash;
                } else {
                    realHash = ModUpdater.CalculateChecksum(mod.Path);
                    CachedHashes.Add(mod, realHash);
                }
                if (!readData.Hash.Equals(realHash)) { // It has been updated
                    AppLogger.Log.Warning("Cache outdated!");
                    this.InvalidateModDatabase(); // The mod was modified (thus probably updated)
                    // so we need to update the DB
                    File.Delete(filePath); // Delete it so it can be re-cached from new DB
                    return null;
                }
                return readData;
            }

            public ModDBUpdateInfo? QueryUpdateInfo(ModInfo mod) {
                RawUpdateDataBase.TryGetValue(mod.Name, out ModDBUpdateInfo? updateDBEntry);
                return updateDBEntry;
            }

            private static readonly Dictionary<char, char> ForbiddenChars = new() {
                    {'<', '\0'},
                    {'>', '\0'},
                    {':', '\0'},
                    {'\"', '\0'},
                    {'\\', '_'},
                    {'|', '\0'},
                    {'?', '\0'},
                    {'*', '\0'},
                    {'/', '_'},
                    {' ', '\0'}
                };

            static ModDataBase() { // Populate forbiddenChars
                // add [0-32] ascii chars
                for (int i = 0; i < 32; i++) {
                    ForbiddenChars.Add((char)i, '\0');
                }
            }

            // Modifies a name to be valid on all file systems
            public static string ValidateName(string name) {
                string res = "";
                name = name.Trim();

                foreach (char c in name) {
                    char newC = ForbiddenChars.GetValueOrDefault(c, c);
                    if (newC != '\0')
                        res += newC;
                }

                return res.ToLower();
            }

            
            
            
        }

        public class ModDBInfo {
            public string Name = "";
            public string GameBananaType = "";
            public int GameBananaId = 0;
            public string Author = "";
            public string Description = "";
            public string[] Screenshots = {};
            public string[] MirroredScreenshots = {};
            public string CategoryName = "";
            public string Hash = ""; // Note: this is added, just for validity checking
        }

        public class ModDBUpdateInfo {
            public string Name = "";
            public string MirrorURL = "";
            public string GameBananaType = "";
            [YamlIgnore]
            public Version ModVer = new Version();
            [YamlIgnore]
            public string _VersionString = "";
            [YamlMember(Alias = "Version")]
            public string VersionString  {
                get => _VersionString;
                set {
                    _VersionString = value;
                    Version? nullableVer = null;
                    int versionSplitIndex = value.IndexOf('-');
                    if (versionSplitIndex == -1)
                        Version.TryParse(value, out nullableVer);
                    else
                        Version.TryParse(value.Substring(0, versionSplitIndex), out nullableVer);
                    ModVer = nullableVer ?? new Version();
                    
                }
            }
            public int LastUpdate;
            public int Size;
            public int GameBananaId;
            public int GameBananaFileId;
            public string[] xxHash = {};
            public string URL = "";
        }
    }
}