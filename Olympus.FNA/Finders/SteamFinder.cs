using MonoMod.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Olympus.Finders {
    public class SteamFinder : Finder {
        protected override Installation.InstallationType InstallationType => Installation.InstallationType.Steam;

        public SteamFinder(FinderManager manager)
            : base(manager) {
        }

        public override bool Owns(Installation i) {
            return
                i.Type == Installation.InstallationType.Steam ||
                i.Type == Installation.InstallationType.SteamShortcut ||
                base.Owns(i);
        }

        public string? FindRoot() {
            if (PlatformHelper.Is(Platform.Windows)) {
                return
                    IsDir(GetReg(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")) ??
                    IsDir(GetReg(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath"));
            }

            if (PlatformHelper.Is(Platform.MacOS)) {
                return IsDir(Combine(GetEnv("HOME"), "Library", "Application Support", "Steam"));
            }

            if (PlatformHelper.Is(Platform.Linux)) {
                string? home = GetEnv("HOME");
                return
                    IsDir(Combine(home, ".local", "share", "Steam")) ??
                    IsDir(Combine(home, ".steam", "steam")) ??
                    IsDir(Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")) ??
                    IsDir(Combine(home, ".var", "app", "com.valvesoftware.Steam", ".steam", "steam"));
            }

            return null;
        }

        public string? FindCommon(string? root) {
            return
                IsDir(Combine(root, "SteamApps", "common")) ??
                IsDir(Combine(root, "steamapps", "common"));
        }

        public IEnumerable<string> FindLibraries() {
            string? steam = FindRoot();
            if (string.IsNullOrEmpty(steam))
                yield break;

            string? common = FindCommon(steam);
            if (!string.IsNullOrEmpty(common))
                yield return common;

            string? config = IsFile(Combine(steam, "config", "config.vdf"));
            if (!string.IsNullOrEmpty(config)) {
                // Reading the entire file is not elegant, but this is also what old Olympus did, thus it's fine.
                foreach (Match match in Regex.Matches(File.ReadAllText(config), @"BaseInstallFolder[^""]*""\s*(""[^""]*"")")) {
                    string? path = JsonConvert.DeserializeObject<string>(match.Groups[1].Value);
                    path = FindCommon(path);
                    if (!string.IsNullOrEmpty(path))
                        yield return path;
                }
            }

            // Newer versions of Steam switched to a separate libraryfolders.vdf
            config = IsFile(Combine(steam, "config", "libraryfolders.vdf"));
            if (!string.IsNullOrEmpty(config)) {
                foreach (Match match in Regex.Matches(File.ReadAllText(config), @"path[^""]*""\s*(""[^""]*"")")) {
                    string? path = JsonConvert.DeserializeObject<string>(match.Groups[1].Value);
                    path = FindCommon(path);
                    if (!string.IsNullOrEmpty(path))
                        yield return path;
                }
            }
        }

        public async IAsyncEnumerable<Dictionary<string, object>> FindShortcuts() {
            string? steam = FindRoot();
            if (string.IsNullOrEmpty(steam))
                yield break;

            // TODO (old): At least one Windows user reported not having a Steam userdata folder..?
            string? userdata = IsDir(Combine(steam, "userdata"));
            if (string.IsNullOrEmpty(userdata))
                yield break;

            List<Task<Dictionary<string, object>[]>> tasks = Directory.EnumerateDirectories(userdata).Select(userdataSub => Task.Run(() => {
                // This should be a nice to have check, but who knows what Valve plans to do in the future.
#if false
                if (!int.TryParse(Path.GetFileName(userdataSub), out _))
                    return Array.Empty<Dictionary<string, object>>();
#endif

                string? path = IsFile(Combine(userdataSub, "config", "shortcuts.vdf"));
                if (string.IsNullOrEmpty(path))
                    return Array.Empty<Dictionary<string, object>>();

                using FileStream stream = new(path, FileMode.Open, FileAccess.Read);
                using BinaryReader reader = new(stream);

                Dictionary<string, object> root = new();
                Dictionary<string, object>? current = root;
                Stack<Dictionary<string, object>> stack = new();

                while (stream.Position < stream.Length) {
                    byte type = reader.ReadByte();
                    if (type == 0x08) {
                        if (!stack.TryPop(out current))
                            goto EOF;
                        continue;
                    }

                    // Field names can have different casings across different objs in the same file!
                    string key = reader.ReadNullTerminatedString().ToLowerInvariant();
                    switch (type) {
                        case 0x00:
                            stack.Push(current);
                            Dictionary<string, object> child = new();
                            current[key] = child;
                            current = child;
                            break;

                        case 0x01:
                            current[key] = reader.ReadNullTerminatedString();
                            break;

                        case 0x02:
                            current[key] = reader.ReadUInt32();
                            break;

                        default:
#if DEBUG
                            throw new Exception($"Encountered unknown VDF type {type}");
#else
                            AppLogger.Log.Warning($"Encountered unknown VDF type {type}");
                            goto EOF;
#endif
                    }
                }

                EOF:;

                if (root.TryGetValue("shortcuts", out object? shortcutsRaw) && shortcutsRaw is Dictionary<string, object> shortcuts) {
                    return shortcuts.Values
                        .Select(shortcutRaw => shortcutRaw as Dictionary<string, object>)
                        .Where(shortcut => shortcut is not null)
                        .Cast<Dictionary<string, object>>()
                        .ToArray();
                }

                return Array.Empty<Dictionary<string, object>>();
            })).ToList();

            do {
                Task.WaitAny(tasks.ToArray());

                for (int i = 0; i < tasks.Count; i++) {
                    Task<Dictionary<string, object>[]> t = tasks[i];
                    if (!t.IsCompleted)
                        continue;

                    foreach (Dictionary<string, object>? shortcut in await t)
                        yield return shortcut;

                    tasks.RemoveAt(i);
                    i--;
                }

            } while (tasks.Count > 0);
        }

        public override async IAsyncEnumerable<Installation> FindCandidates() {
            foreach (string library in FindLibraries()) {
                string? path = IsDir(Combine(library, GameID));
                if (!string.IsNullOrEmpty(path))
                    yield return new Installation(Installation.InstallationType.Steam, "Steam", path);
            }

            // This will add *all* shortcutted games and their startup dirs, but eh.
            await foreach (Dictionary<string, object> shortcut in FindShortcuts()) {
                string? path = null;

                if (string.IsNullOrEmpty(path) &&
                    shortcut.TryGetValue("exe", out object? exeRaw) && exeRaw is string exe &&
                    Regex.Match(exe, @"^""?([^"" ]*)") is Match exeMatch && exeMatch.Success &&
                    exeMatch.Groups[1].Success) {
                    path = IsDir(Path.GetDirectoryName(exeMatch.Groups[1].Value));
                }

                if (string.IsNullOrEmpty(path) &&
                    shortcut.TryGetValue("startdir", out object? startdirRaw) && startdirRaw is string startdir &&
                    Regex.Match(startdir, @"^""?([^"" ]*)") is Match startdirMatch && startdirMatch.Success &&
                    startdirMatch.Groups[1].Success) {
                    path = IsDir(startdirMatch.Groups[1].Value);
                }

                if (!string.IsNullOrEmpty(path)) {
                    if (shortcut.TryGetValue("appname", out object? appnameRaw) && appnameRaw is string appname) {
                        yield return new Installation(Installation.InstallationType.SteamShortcut, $"Steam Shortcut: {appname}", path);

                    } else {
                        yield return new Installation(Installation.InstallationType.SteamShortcut, "Steam Shortcut", path);
                    }
                }
            }
        }

    }
}
