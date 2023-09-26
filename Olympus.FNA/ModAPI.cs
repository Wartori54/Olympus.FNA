using Newtonsoft.Json;
using Olympus.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace Olympus {

    public class ModAPI {
        #region Interfaces
        public interface IModInfoAPI { }

        public interface IModInfo {
            public string Name { get; }
            public string Author { get; }
            public string Description { get; }
            public DateTime CreationDate { get; }
            public DateTime LastModified { get; }
            public string[] Screenshots { get; }
            public string ModType { get; }
            public string PageURL { get; }
            public IEnumerable<IModFileInfo> Files { get; }
        }


        public interface IModFileInfo {
            public string? Path { get; }
            public abstract string Name { get; }
            public string Hash { get; }
            public bool IsLocal { get; }
            public DateTime? LastUpdate { get; }
            public string[]? DownloadUrl { get; }
            public bool? IsBlacklisted { get; }
            public bool? IsUpdaterBlacklisted { get; }
            public Version? Version { get; }
            public string[]? DependencyIds { get; }
        }
        
        #endregion
        
        #region LocalAPI

        /// <summary>
        /// Represents a local file api that points to an install
        /// It's not capable of creating ModInfo on its own
        /// </summary>
        public class LocalInfoAPI : IModInfoAPI {
            private readonly Installation install;

            public readonly ManualCache<IEnumerable<IModFileInfo>> modFileInfoCache;

            public LocalInfoAPI(Installation install) {
                this.install = install;
                modFileInfoCache = new ManualCache<IEnumerable<IModFileInfo>>(INTERNAL_CreateAllModFileInfo, this);
            }

            public IEnumerable<IModFileInfo> CreateAllModFileInfo() {
                return modFileInfoCache.Value;
            }

            private IEnumerable<IModFileInfo> INTERNAL_CreateAllModFileInfo(object? _) {
                List<IModFileInfo> generated = new();
                string modsDir = Path.Combine(install.Root, "Mods");
                foreach (string fsEntry in Directory.EnumerateFileSystemEntries(modsDir)) {
                    IModFileInfo? res = CreateModFileInfo(fsEntry);
                    
                    if (res != null) {
                        generated.Add(res);
                    }
                }

                return generated;
            }

            public IModFileInfo? CreateModFileInfo(string fsEntry) {
                IModFileInfo? res = null;
                if (File.Exists(fsEntry)) {
                    if (fsEntry.EndsWith(".zip")) {
                        res = ParseZip(fsEntry);
                    } else if (fsEntry.EndsWith(".bin")) {
                        res = CreateModFileInfo(fsEntry, null); // No everest yaml for .bin s
                    } else { // Maybe warn if user has other files in Mods folder?
                        res = null;
                    }
                } else { // Assume directory
                    try {
                        string yamlPath = Path.Combine(fsEntry, "everest.yaml");
                        if (!File.Exists(yamlPath))
                            yamlPath = Path.Combine(fsEntry, "everest.yml");
        
                        if (File.Exists(yamlPath)) {
                            using (FileStream stream = File.Open(yamlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            using (StreamReader reader = new(stream))
                                res = CreateModFileInfo(fsEntry, reader);
                        } // Notice: don't consider folders without everest.yaml
                    } catch (UnauthorizedAccessException) { /* skip errors */ }
                }

                return res;
            }
            
            public IModFileInfo CreateModFileInfo(string path, TextReader? readYaml) {
                return new LocalModFileInfo(path, readYaml, install);
            }

            private IModFileInfo? ParseZip(string fsEntry) {
                using (FileStream zipStream = File.Open(fsEntry, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                    zipStream.Seek(0, SeekOrigin.Begin);
                    using (ZipArchive zip = new ZipArchive(zipStream, ZipArchiveMode.Read))
                    using (Stream? stream =
                           (zip.GetEntry("everest.yaml") ?? zip.GetEntry("everest.yml"))?.Open())
                    using (StreamReader? reader = stream == null ? null : new StreamReader(stream)) {
                        if (reader == null) { // Warn but create it anyways
                            AppLogger.Log.Warning($"Failed reading everest.yaml for mod {fsEntry}");
                        }

                        return CreateModFileInfo(fsEntry, reader);

                    }
                }
            }

            

            public class LocalModFileInfo : IModFileInfo {
                // Refers to the full path to the file
                public string? Path { get; }

                private string? hash;

                public string Hash {
                    get {
                        if (Path == null) return "";
                        hash ??= ModUpdater.CalculateChecksum(Path);
                        return hash;
                    }
                }

                public bool IsLocal => true;
                public DateTime? LastUpdate => null;
                public string[]? DownloadUrl => null;
                public bool? IsBlacklisted => install.MainBlacklist.items.Contains(System.IO.Path.GetFileName(Path) ?? "");
                public bool? IsUpdaterBlacklisted => install.UpdateBlacklist.items.Contains(System.IO.Path.GetFileName(Path) ?? "");
                public Version? Version { get; }
                public string[]? DependencyIds { get; }

                public string Name { get; }

                private Installation install;

                public LocalModFileInfo(string path, TextReader? everestYaml, Installation install) {
                    this.install = install;
                    Path = path;
                    Name = System.IO.Path.GetFileName(path);
                    
                    if (everestYaml == null) return;
                    
                    using FileStream zipStream = File.Open(Path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    zipStream.Seek(0, SeekOrigin.Begin);

                    using (ZipArchive zip = new ZipArchive(zipStream, ZipArchiveMode.Read))
                    using (Stream? stream = (zip.GetEntry("everest.yaml") ?? zip.GetEntry("everest.yml"))?.Open())
                    using (StreamReader? reader = stream == null ? null : new StreamReader(stream)) {
                        if (reader == null) return;

                        List<EverestModuleMetadata>? yaml =
                            YamlHelper.Deserializer.Deserialize<List<EverestModuleMetadata>>(reader);
                        // ReSharper disable twice ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                        if (yaml == null || yaml.Count <= 0 || yaml[0] == null) return;

                        Name = yaml[0].Name;

                        try {
                            Version = new Version(yaml[0].Version);
                        } catch {
                            AppLogger.Log.Warning($"Mod {Name} contains an invalid version: {yaml[0].Version}");
                        }

                        if (yaml[0].Dependencies.Count != 0)
                            DependencyIds = yaml[0].Dependencies.Select(dep => dep.Name).ToArray();

                    }

                    
                }

                public class EverestModuleMetadata {
                    public string Name = "";
                    public string Version = "";
                    public string DLL = "";
                    public List<EverestModuleMetadata> Dependencies = new();
                }
            }
        }
        
        #endregion

        #region RemoteAPIs
        public abstract class RemoteModInfoAPI : IModInfoAPI {

            public UrlManager UrlManager;

            protected RemoteModInfoAPI(string urlsYamlPath) {
                UrlManager = new UrlManager(urlsYamlPath);
            }
            
            public abstract IEnumerable<RemoteModInfo> GetFeaturedEntries();
            public abstract IEnumerable<RemoteModInfo> GetSearchEntries(string query);
            // Here ID is an alias for the Name key present on the everest.yaml
            public abstract RemoteModFileInfo? GetModFileInfoFromId(string ID);

            public abstract RemoteModInfo? GetModInfoFromFileInfo(IModFileInfo file);

            public abstract class RemoteModFileInfo : IModFileInfo {
                public string? Path => null;
                public abstract string Name { get; }
                public abstract string Hash { get; }
                public bool IsLocal => false;
                public abstract DateTime? LastUpdate { get; }
                public abstract string[]? DownloadUrl { get; }
                public bool? IsBlacklisted => null;
                public bool? IsUpdaterBlacklisted => null;
                public abstract Version Version { get; }
                public string[]? DependencyIds => null;
            }
            
            public abstract class RemoteModInfo : IModInfo {
                public abstract string Name { get; }
                public abstract string Author { get; }
                public abstract string Description { get; }
                public abstract DateTime CreationDate { get; }
                public abstract DateTime LastModified { get; }
                public abstract string[] Screenshots { get; }
                public abstract string ModType { get; }
                public abstract string PageURL { get; }
                public abstract IEnumerable<IModFileInfo> Files { get; }
            }
        }

        public class MaddieModInfoAPI : RemoteModInfoAPI {

            private const string YamlPath = "metadata/urls/maddie_api.yaml";

            // Maps file id-name to file info
            private TimedCache<Dictionary<string, MaddieModFileInfo>> rawUpdateDataBase; 
            // Maps gamebanana id file to id-name
            private TimedCache<Dictionary<int, string>> mmdlUpdateDataBase;
            // Contains the featured entries
            private TimedCache<List<RemoteModInfo>> featuredEntries;
            // Maps file id to mod info
            private TimedCache<Dictionary<int, MaddieModInfo>> searchDataBase;

            public MaddieModInfoAPI() : base(YamlPath) {
                rawUpdateDataBase = new(TimeSpan.FromMinutes(15), 
                    DownloadUpdateDataBase, null);
                mmdlUpdateDataBase = new(TimeSpan.FromMinutes(15), _ => {
                    AppLogger.Log.Information("Re-indexing UpdateDB");
                    Dictionary<int, string> ret = new();
                    foreach (KeyValuePair<string, MaddieModFileInfo> kvp in rawUpdateDataBase.Value) {
                        if (!ret.TryAdd(kvp.Value.GameBananaFileId, kvp.Key)) {
                            throw new InvalidDataException("Found multiple GameBananaFileId!");
                        }
                    }

                    return ret;
                }, null);
                featuredEntries = new(TimeSpan.FromMinutes(15), _ => {
                    AppLogger.Log.Information("Getting featured entries");
                    
                    Stream jsonData = UrlManager.TryHttpGetDataStream("gamebanana-featured");
                    using StreamReader sr = new(jsonData);
                    using JsonTextReader jtr = new(sr);

                    List<MaddieModInfo>? entries = JsonHelper.Serializer.Deserialize<List<MaddieModInfo>>(jtr);
                    if (entries == null) {
                        entries = new();
                        AppLogger.Log.Error("Failed obtaining featured entries");
                    }

                    List<RemoteModInfo> ret = new();
                    foreach (MaddieModInfo entry in entries) {
                        // Tools cannot be featured
                        if (entry.ModType == "Tool") continue;
                        List<MaddieModFileInfo> newFiles = new();
                        foreach (IModFileInfo file in entry.Files) {
                            string mmdl = file.DownloadUrl![0].Split('/')[^1];
                            if (!mmdlUpdateDataBase.Value.TryGetValue(int.Parse(mmdl), out string? ID)) {
                                AppLogger.Log.Warning($"Can't file for gamebanan ID: {mmdl}");
                                continue;
                            }
                            
                            newFiles.Add(rawUpdateDataBase.Value[ID]); // rawUpdateDataBase must contain the ID if the above succeded
                        }

                        entry.DummyFiles = null;
                        entry.MaddieFiles = newFiles;
                        ret.Add(entry);
                    }

                    return ret;
                }, null);
                searchDataBase = new(TimeSpan.FromMinutes(15), _ => {
                    AppLogger.Log.Information("Re-indexing mod search database");
                    
                    string yamlData = UrlManager.TryHttpGetDataString("search_database");

                    List<MaddieModInfo> data =
                        YamlHelper.Deserializer.Deserialize<List<MaddieModInfo>>(yamlData);

                    Dictionary<int, MaddieModInfo> ret = new();

                    foreach (MaddieModInfo modInfo in data) {
                        List<MaddieModFileInfo> newFiles = new();
                        foreach (MaddieModFileInfoDummy dummyFile in modInfo.DummyFiles ?? new List<MaddieModFileInfoDummy>()) {
                            int mmdl = int.Parse(dummyFile.DownloadUrl![0].Split('/')[^1]);
                            if (!mmdlUpdateDataBase.Value.TryGetValue(mmdl, out string? ID)) {
                                // This means its probably not the latest file, discard it
                                continue;
                            }

                            newFiles.Add(rawUpdateDataBase.Value[ID]);
                        }

                        modInfo.DummyFiles = null;
                        modInfo.MaddieFiles = newFiles;
                        foreach (MaddieModFileInfo file in modInfo.MaddieFiles) { // must contain dummy files
                            if (!ret.TryAdd(file.GameBananaFileId, modInfo)) {
                                AppLogger.Log.Error($"ID: {file.GameBananaFileId} appeared multiple times!");
                            }
                        }
                    }

                    return ret;
                }, null);
            }

            private Dictionary<string, MaddieModFileInfo> DownloadUpdateDataBase(object? _) {
                AppLogger.Log.Information("Downloading UpdateDB");
                
                string yamlData = UrlManager.TryHttpGetDataString("everest_update");
                
                Dictionary<string, MaddieModFileInfo> ret = 
                    YamlHelper.Deserializer.Deserialize<Dictionary<string, MaddieModFileInfo>>(yamlData);

                foreach (KeyValuePair<string, MaddieModFileInfo> entry in ret)
                    entry.Value.name = entry.Key;
                
                AppLogger.Log.Information("Deserialized updaterDB");
                return ret;
            }

            public override IEnumerable<RemoteModInfo> GetFeaturedEntries() {
                return featuredEntries.Value;
            }

            public override IEnumerable<RemoteModInfo> GetSearchEntries(string query) {
                throw new NotImplementedException();
            }

            public override RemoteModFileInfo? GetModFileInfoFromId(string ID) {
                if (!rawUpdateDataBase.Value.TryGetValue(ID, out MaddieModFileInfo? info)) {
                    AppLogger.Log.Warning($"Tried to obtain non existent ID: {ID}");
                    return null;
                }

                return info;
            }

            // Note to future maintainers: The edge case where the localFileInfo is outdated is covered here,
            // by the fact that it uses the mod id, it'll pick up the latest remoteFileInfo, and pairing with the 
            // remoteInfo will be successful. :peaceline:
            public override RemoteModInfo? GetModInfoFromFileInfo(IModFileInfo file) {
                MaddieModFileInfo? maddieFile;
                if (file.IsLocal && file is LocalInfoAPI.LocalModFileInfo localFile) {
                    if (localFile.Name == null) return null;
                    if (!rawUpdateDataBase.Value.TryGetValue(localFile.Name, out maddieFile)) {
                        return null;
                    }
                } else {
                    // Note: IModFileInfo may implement another api, return null if that's the case
                    maddieFile = file as MaddieModFileInfo;
                }

                if (maddieFile == null || !searchDataBase.Value.TryGetValue(maddieFile.GameBananaFileId, out MaddieModInfo? info)) {
                     return null;
                }

                return info;
            }

            public class MaddieModFileInfo : RemoteModFileInfo {
                // FOR SOME REASON THIS IS A LIST, WHAT???? 
                [YamlMember(Alias = "xxHash")] 
                public List<string> hash = new();
                [YamlIgnore]
                public override string Hash => hash[0];
                
                [YamlMember(Alias = "LastUpdate")] 
                public int lastUpdateI;
                [YamlIgnore]
                public override DateTime? LastUpdate => DateTimeOffset.FromUnixTimeSeconds(lastUpdateI).UtcDateTime;

                public string URL = "";
                public string MirrorURL = "";

                [YamlIgnore]
                public override string[]? DownloadUrl => new[] { URL, MirrorURL };

                [YamlMember(Alias = "Version")]
                public string version = "";
                [YamlIgnore]
                public override Version Version => Version.Parse(version.Trim('\''));

                public int GameBananaFileId;

                [YamlMember(Alias = "Name")]
                public string name = "";
                [YamlIgnore]
                public override string Name => name;
            }

            // I HECKING LOVE WHEN APIS PROVIDE THE SAME DATA IN TWO DIFFERENT FORMATS :))))))))))))))))
            public class MaddieModInfo : RemoteModInfo {
                [JsonProperty("Name")]
                [YamlMember(Alias = "Name")]
                public string name = "";
                [Newtonsoft.Json.JsonIgnore]
                [YamlIgnore]
                public override string Name => name;
                
                [JsonProperty("Author")]
                [YamlMember(Alias = "Author")]
                public string author = "";
                [Newtonsoft.Json.JsonIgnore]
                [YamlIgnore]
                public override string Author => author;
                
                [JsonProperty("Description")]
                [YamlMember(Alias = "Description")]
                public string description = "";
                [Newtonsoft.Json.JsonIgnore]
                [YamlIgnore]
                public override string Description => description;
                
                [JsonProperty("CreatedDate")]
                [YamlMember(Alias = "CreatedDate")]
                public int creationDateI;
                [Newtonsoft.Json.JsonIgnore]
                [YamlIgnore]
                public override DateTime CreationDate => DateTimeOffset.FromUnixTimeSeconds(creationDateI).UtcDateTime;
                
                [JsonProperty("UpdatedDate")]
                [YamlMember(Alias = "UpdatedDate")]
                public int updatedDateI;
                [Newtonsoft.Json.JsonIgnore]
                [YamlIgnore]
                public override DateTime LastModified => DateTimeOffset.FromUnixTimeSeconds(updatedDateI).UtcDateTime;
                
                [JsonProperty("Screenshots")]
                [YamlMember(Alias = "Screenshots")]
                public string[] screenshots = Array.Empty<string>();
                [Newtonsoft.Json.JsonIgnore]
                [YamlIgnore]
                public override string[] Screenshots => screenshots;
                
                [JsonProperty("GameBananaType")]
                [YamlMember(Alias = "GameBananaType")]
                public string modType = "";
                [YamlIgnore]
                [Newtonsoft.Json.JsonIgnore]
                public override string ModType => modType;
                
                [JsonProperty("PageURL")]
                [YamlMember(Alias = "PageURL")]
                public string pageURL = "";
                [YamlIgnore]
                [Newtonsoft.Json.JsonIgnore]
                public override string PageURL => pageURL;
                
                public int GameBananaId;
                
                [JsonProperty("Files")]
                [YamlMember(Alias = "Files")] // Notice using dummy, it'll get fixed later
                public List<MaddieModFileInfoDummy>? DummyFiles = new();
                [YamlIgnore]
                [Newtonsoft.Json.JsonIgnore]
                public List<MaddieModFileInfo> MaddieFiles = new();
                [Newtonsoft.Json.JsonIgnore]
                [YamlIgnore] // This is proxied so that yamldotnet picks up the right IModFileInfo impl
                public override IEnumerable<IModFileInfo> Files => DummyFiles != null ? DummyFiles : MaddieFiles;
            }

            // Dummy ModFileInfo, this will get swapped by a good one right after its creation
            public class MaddieModFileInfoDummy : RemoteModFileInfo {
                public override string Name => "";
                public override string Hash => "";
                [Newtonsoft.Json.JsonIgnore]
                [YamlIgnore]
                public override DateTime? LastUpdate { get; } = DateTime.MinValue;
                public string URL = "";
                [Newtonsoft.Json.JsonIgnore]
                [YamlIgnore]
                public override string[]? DownloadUrl => new[] { URL };

                public override Version Version { get; } = new Version();
            }
        }
        
        #endregion
        
        #region RemoteAPIManager

        public class RemoteAPIManager {
            private Dictionary<APIs, RemoteModInfoAPI> apis;
            public static RemoteAPIManager? Instance;
            public static APIs Default = 0; // Default to first one, prob going to get modified anyway

            public RemoteAPIManager() {
                if (Instance != null)
                    throw new InvalidOperationException("RemoteAPIManager created multiple times!");
                Instance = this;
                apis = new Dictionary<APIs, RemoteModInfoAPI>();
                // Add all the apis here
                apis.Add(APIs.MaddieAPI, new MaddieModInfoAPI());
                
            }


            public RemoteModInfoAPI GetAPI(APIs api) {
                return apis[api];
            }

            public RemoteModInfoAPI DefaultAPI() {
                return apis[Default];
            }
            
            /// <summary>
            /// Attempts to call a method from RemoteModInfoAPI on all apis, until one succeeds
            /// </summary>
            /// <param name="func">The method to call on the api</param>
            /// <typeparam name="T">The expected return type</typeparam>
            /// <returns>null if failure, an object otherwise</returns>
            public T? TryAll<T>(Func<RemoteModInfoAPI, T?> func) where T : class {
                foreach (RemoteModInfoAPI api in apis.Values) {
                    T? res = func(api);
                    if (res != null) return res;
                }

                return null;
            }

            public enum APIs {
                MaddieAPI,
            }
        }
        
        #endregion
    }
}
