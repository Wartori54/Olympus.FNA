using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Linq;
using System;

namespace Olympus {

    // Copied and refactored from https://github.com/EverestAPI/Olympus/blob/main/sharp/CmdModList.cs
    public class ModList {

        // public static HashAlgorithm Hasher = XXHash64.Create(); //TODO: hashing


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

            string[] allFiles = Directory.GetFiles(modsFolder);
            foreach (string file in allFiles) { // zips and bin(s)
                if (file.EndsWith(".zip")) {
                    // zip
                    if ((onlyUpdatable && updaterBlacklist.Contains(file)) || (excludeDisabled && blacklist.Contains(file)))
                        continue;
                    ModInfo info = parseZip(file, readYamls);
                    info.IsBlacklisted = blacklist.Contains(file);
                    info.IsUpdaterBlacklisted = updaterBlacklist.Contains(file);
                    mods.Add(info);
                } else if (file.EndsWith(".bin") && !onlyUpdatable) { // quick reminder that bins and dir cannot be updated
                    // bin
                    ModInfo info = parseBin(file);
                    info.IsBlacklisted = blacklist.Contains(file);
                    info.IsUpdaterBlacklisted = updaterBlacklist.Contains(file);
                    mods.Add(info);
                }
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
            public string DLL ="";
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
        }

        public class EverestModuleMetadata {
            public string Name = "";
            public string Version = "";
            public string DLL = "";
            public List<EverestModuleMetadata> Dependencies = new List<EverestModuleMetadata>();
        }
    }
}