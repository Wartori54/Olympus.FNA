using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using YYProject.XXHash;
using YamlDotNet.Serialization;

namespace Olympus {

    // Copied and refactored from https://github.com/EverestAPI/Olympus/blob/main/sharp/CmdModList.cs
    public class ModList {

        public static HashAlgorithm Hasher = XXHash64.Create(); //TODO: hashing

        public static ModDataBase dataBase = new ModDataBase();

        public static List<ModInfo> GatherModList(bool readYamls, bool computeHashes, bool onlyUpdatable, bool excludeDisabled) {
            if (Config.Instance == null || Config.Instance.Installation == null) return new List<ModInfo>();
            return GatherModList(Config.Instance.Installation, readYamls, computeHashes, onlyUpdatable, excludeDisabled);
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
                    // zip
                    if ((onlyUpdatable && updaterBlacklist.Contains(file)) || (excludeDisabled && blacklist.Contains(file)))
                        continue;
                    ModInfo info = parseZip(file, readYamls);
                    info.IsBlacklisted = blacklist.Contains(file);
                    info.IsUpdaterBlacklisted = updaterBlacklist.Contains(file);
                    zipMods.Add(info);
                } else if (file.EndsWith(".bin") && !onlyUpdatable) { // quick reminder that bins and dir cannot be updated
                    // bin
                    ModInfo info = parseBin(file);
                    info.IsBlacklisted = blacklist.Contains(file);
                    info.IsUpdaterBlacklisted = updaterBlacklist.Contains(file);
                    mods.Add(info);
                }
            }

            Dictionary<ModInfo, ModDBInfo> modZipDB = dataBase.QueryModDBInfoForMods(zipMods, true);

            foreach (ModInfo zipMod in zipMods) {
                ModDBInfo? tmp;
                if (modZipDB.TryGetValue(zipMod, out tmp)) {
                    zipMod.Description = tmp.Description;
                } else {
                    Console.WriteLine("ModDBInfo: Mod {0} did not appear on the mod data base", zipMod.Name);
                }
                mods.Add(zipMod);   
            }


            if (!onlyUpdatable) {
                string[] allDirs = Directory.GetDirectories(modsFolder);
                foreach (string dir in allDirs) {
                    if (Path.GetFileName(dir) == "Cache") continue;
                    // dir
                    ModInfo info = parseDir(dir, readYamls);
                    info.IsBlacklisted = blacklist.Contains(dir);
                    info.IsUpdaterBlacklisted = updaterBlacklist.Contains(dir);
                    mods.Add(info);
                }
            }
            

            return mods;
        }

        private static ModInfo parseZip(string file, bool readYamls) {
            ModInfo info = new ModInfo() {
                Path = file,
                IsFile = true,
            };

            if (readYamls) {
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
            }
            return info;
        }

        private static ModInfo parseBin(string file) {
            ModInfo info = new ModInfo() {
                Path = file,
                IsFile = true,
            };

            return info;
        }

        private static ModInfo parseDir(string dir, bool readYamls) {
            ModInfo info = new ModInfo() {
                Path = dir,
                IsFile = false,

            };

            if (readYamls) {
                try {
                    string yamlPath = Path.Combine(dir, "everest.yaml");
                    if (!File.Exists(yamlPath))
                        yamlPath = Path.Combine(dir, "everest.yml");

                    if (File.Exists(yamlPath)) {
                        using (FileStream stream = File.Open(yamlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (StreamReader reader = new StreamReader(stream))
                            info.Parse(reader);
                    }
                } catch (UnauthorizedAccessException) { }
            }
            return info;
        }

        public class ModInfo {
            public string Path = "";
            public string Hash = "";
            public bool IsFile;
            public bool IsBlacklisted;
            public bool IsUpdaterBlacklisted;

            public string Name = "";
            public string Version = "";
            public string Description = "";
            public string DLL = "";
            public string[] Dependencies = {};
            public bool IsValid;

            public void Parse(TextReader? reader) {
                try {
                    if (reader != null) {
                        List<EverestModuleMetadata> yaml = YamlHelper.Deserializer.Deserialize<List<EverestModuleMetadata>>(reader);
                        if (yaml != null && yaml.Count > 0) {
                            Name = yaml[0].Name;
                            Version = yaml[0].Version;
                            DLL = yaml[0].DLL;
                            if (yaml[0].Dependencies.Capacity != 0)
                                Dependencies = yaml[0].Dependencies.Select(dep => dep.Name).ToArray();

                            IsValid = Name != null && Version != null;
                        }
                    }
                } catch {
                    // ignore parse errors
                }
            }

            public override int GetHashCode() {
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
            public static string UrlsYamlPath = "metadata/urls.yaml";

            public static string DBName = "mod_search_database.yaml";

            public static string modCachePath = Path.Join(Config.GetDefaultDir(), "modCaches");

            private static DataBaseUrls? _urls = null;

            public static DataBaseUrls Urls {
                get {
                    if (_urls == null) {
                        // retrieve the url
                        using (Stream? stream = OlympUI.Assets.OpenStream(UrlsYamlPath)) {
                            if (stream == null) {
                                throw new FileNotFoundException("Couldn't query DB urls, {0} file not found", UrlsYamlPath);
                            }
                            using (StreamReader reader = new StreamReader(stream))
                                _urls = YamlHelper.Deserializer.Deserialize<DataBaseUrls>(reader);
                        }
                    }
                    return _urls;
                }
            }
            
            // Performance related, may contain non existent hashes because of updating mods, its fine
            private static Dictionary<ModInfo, string> CachedHashes = new Dictionary<ModInfo, string>();

            private Dictionary<int, ModDBInfo> _rawDataBase = new Dictionary<int, ModDBInfo>();
            // Maps gamebanana id to info
            public Dictionary<int, ModDBInfo> RawDataBase {
                get {
                    if (_rawDataBase.Count == 0 || invalidateModDataBase)
                        this.DownloadModDataBase();
                    return _rawDataBase;
                }
            }

            private Dictionary<string, ModDBUpdateInfo> _rawUpdateDataBase = new Dictionary<string, ModDBUpdateInfo>();

            // Maps name to info
            public Dictionary<string, ModDBUpdateInfo> RawUpdateDataBase { 
                get {
                    if (_rawUpdateDataBase.Count == 0) 
                        this.DownloadUpdateDataBase();
                    return _rawUpdateDataBase;
                }
            }

            private bool invalidateModDataBase = false;

            public void InvalidateModDatabase() => invalidateModDataBase = true;
            
            // Downloads and loads the yaml containing everything in gamebanana
            private void DownloadModDataBase() {
                Console.WriteLine("Populating DB, redownload: {0}", invalidateModDataBase);
                string yamlData = "";
                if (!invalidateModDataBase) {
                    if (File.Exists(Path.Join(Config.GetDefaultDir(), DBName)))
                        yamlData = File.ReadAllText(Path.Join(Config.GetDefaultDir(), DBName));
                    else
                        InvalidateModDatabase();
                }
                if (invalidateModDataBase) {
                    Console.WriteLine("Redownloading DB");
                    

                    using (HttpClient wc = new HttpClient()) {
                        Console.WriteLine("Downloading...");
                        string dataBaseUrl = Urls.ModDataBase;

                        yamlData = Task.Run<string>(async() => await wc.GetStringAsync(dataBaseUrl)).Result;

                        File.WriteAllText(Path.Join(Config.GetDefaultDir(), DBName), yamlData);
                        Console.WriteLine("Saved DB");
                    }
                }
                List<ModDBInfo> listDB = YamlHelper.Deserializer.Deserialize<List<ModDBInfo>>(yamlData);
                Console.WriteLine("Deserialized db");
                _rawDataBase.Clear();
                // We are foced to copy aaaaa (i think)
                foreach (ModDBInfo entry in listDB) {
                    _rawDataBase.Add(entry.GameBananaId, entry);
                }

                invalidateModDataBase = false;
            }

            // Download the everest_update.yaml, downloaded on each boot
            private void DownloadUpdateDataBase() {
                Console.WriteLine("Downloading UpdateDB");
                string yamlData = "";
                string dbUrl = "";
                using (HttpClient wc = new HttpClient()) {
                    Console.WriteLine("Obtaining updaterDB url");
                    dbUrl = Task.Run<string>(async() => await wc.GetStringAsync(Urls.ModUpdateDataBase)).Result;

                    Console.WriteLine("Downloading everest-update.yaml");
                    yamlData = Task.Run<string>(async() => await wc.GetStringAsync(dbUrl)).Result;

                }
                _rawUpdateDataBase = YamlHelper.Deserializer.Deserialize<Dictionary<string, ModDBUpdateInfo>>(yamlData);
                foreach (string name in _rawUpdateDataBase.Keys) {
                    _rawUpdateDataBase[name].Name = name;
                }
                Console.WriteLine("Deserialized updaterDB");
            }

            // Returns the corresponding ModDBInfo for every ModInfo
            public Dictionary<ModInfo, ModDBInfo> QueryModDBInfoForMods(List<ModInfo> targetMods, bool CacheMods) {
                Console.WriteLine("Querying Db");
                Dictionary<ModInfo, ModDBInfo> filteredMods = new Dictionary<ModInfo, ModDBInfo>(targetMods.Count);
                // Method explanation: We need to map ModInfo to ModDBInfo but unluckily theres no way to do that directly (as of now)
                // so we must rely on the everest_update.yaml, because it contains the names from everest.yaml from each mod and the 
                // gamebanana id for each mod, which ModDBInfo also has
                // so in the end the connection is ModInfo -> ModDBUpdateInfo -> ModDBInfo

                // Check cache
                // Dict for performance
                Dictionary<string, ModInfo> mappedMods = new Dictionary<string, ModInfo>(targetMods.Count); // .Count possible performance optimization?
                foreach (ModInfo mod in targetMods) {
                    ModDBInfo? cachedInfo = QueryFromCache(mod);
                    if (cachedInfo != null) {
                        filteredMods.Add(mod, cachedInfo);
                        continue;
                    }
                    mappedMods.Add(mod.Name, mod);
                }

                foreach (KeyValuePair<string, ModInfo> entry in mappedMods) {
                    if (RawUpdateDataBase.ContainsKey(entry.Key)) {
                        int GBId = RawUpdateDataBase[entry.Key].GameBananaId;
                        if (RawDataBase.ContainsKey(GBId)) {
                            ModDBInfo dBInfo = RawDataBase[GBId];
                            if (CacheMods) {
                                string validName = ValidateName(entry.Key);
                                Console.WriteLine("Cacheing mod: {0}", entry.Key);
                                if (!Directory.Exists(modCachePath))
                                    Directory.CreateDirectory(modCachePath);
                                
                                if (CachedHashes.ContainsKey(entry.Value)) 
                                    dBInfo.Hash = CachedHashes[entry.Value];
                                else {
                                    using (FileStream modFile = File.OpenRead(entry.Value.Path))
                                        dBInfo.Hash = BitConverter.ToString(Hasher.ComputeHash(modFile)).Replace("-", "").ToLowerInvariant();
                                    CachedHashes.Add(entry.Value, dBInfo.Hash);
                                }
                                Console.WriteLine("Hashed mod");
                                
                                using (FileStream file = File.OpenWrite(Path.Join(modCachePath, validName + ".yaml")))
                                using (StreamWriter writer = new StreamWriter(file))
                                    YamlHelper.Serializer.Serialize(writer, dBInfo);
                                
                                Console.WriteLine("Cached mod");
                            }
                            filteredMods.Add(entry.Value, dBInfo);
                        } else {
                            Console.WriteLine("Mod {0} has match in updateDB but not in searchDB", entry.Key);
                        }
                    } else {
                        Console.WriteLine("Mod {0} not found in DB", entry.Key);
                    }
                }

                return filteredMods;
            }

            private ModDBInfo? QueryFromCache(ModInfo mod) {
                string filePath = Path.Join(modCachePath, ValidateName(mod.Name) + ".yaml");
                if (File.Exists(filePath)) {
                    Console.Write("Queriyng from cache {0}... ", mod.Name);
                    // Read cache
                    ModDBInfo readData;
                    using (StreamReader file = File.OpenText(filePath))
                        readData = YamlHelper.Deserializer.Deserialize<ModDBInfo>(file);
                    
                    // Hash installed mod to detect updates
                    string realHash;
                    if (CachedHashes.ContainsKey(mod)) {
                        realHash = CachedHashes[mod];
                    } else {
                        using (FileStream modFile = File.OpenRead(mod.Path))
                            realHash = BitConverter.ToString(Hasher.ComputeHash(modFile)).Replace("-", "").ToLowerInvariant();
                        CachedHashes.Add(mod, realHash);
                    }
                    if (!readData.Hash.Equals(realHash)) { // It has been updated
                        Console.WriteLine("cache oudated");
                        this.InvalidateModDatabase(); // The mod was modified (thus probably updated)
                        // so we need to update the DB
                        File.Delete(filePath); // Delete it so it can be re-cached from new DB
                        return null;
                    }
                    Console.WriteLine("success!");
                    return readData;
                }
                return null;
            }

            private static Dictionary<char, bool> forbidenChars = new Dictionary<char, bool>() {
                    {'<', true},
                    {'>', true},
                    {':', true},
                    {'\"', true},
                    {'\\', true},
                    {'|', true},
                    {'?', true},
                    {'*', true},
                    {'/', true},
                    {' ', true}
                };

            static ModDataBase() { // Populate forbidenChars
                // add [0-32] ascii chars
                for (int i = 0; i < 32; i++) {
                    forbidenChars.Add((char)i, true);
                }
            }

            // Modifies a name to be valid on all file systems
            public static string ValidateName(string name) {
                string res = "";
                name = name.Trim();

                foreach (char c in name) {
                    if (!forbidenChars.GetValueOrDefault(c, false)) {
                        res += c;
                    }
                }

                return res.ToLower();
            }

            public class DataBaseUrls {
                public string ModDataBase = "";
                public string ModUpdateDataBase = "";
            }
        }

        public class ModDBInfo {
            public string Name = "";
            public string GameBananaType = "";
            public int GameBananaId;
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
                get {
                    return _VersionString;
                }
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