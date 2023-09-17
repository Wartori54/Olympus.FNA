using MonoMod.Utils;
using Microsoft.Win32;
using OlympUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Mono.Cecil;
using MonoMod.Cil;
using System.Net.Http.Headers;

namespace Olympus {
    public abstract class Finder {

        public readonly FinderManager Manager;

        public string GameID { get; set; } = "Celeste";

        public virtual int Priority => 0;

        protected readonly string FinderTypeDefault;

        public Finder(FinderManager manager) {
            Manager = manager;
            FinderTypeDefault = GetType().Name;
            if (FinderTypeDefault.EndsWith("Finder")) {
                FinderTypeDefault = FinderTypeDefault[..^"Finder".Length];
            }
        }

        protected string? IsDir(string? path) {
            if (string.IsNullOrEmpty(path))
                return null;
            path = Path.TrimEndingDirectorySeparator(path);
            if (!Directory.Exists(path))
                return null;
            return path;
        }

        protected string? IsFile(string? path) {
            if (string.IsNullOrEmpty(path))
                return null;
            if (!File.Exists(path))
                return null;
            return path;
        }

        protected string? GetReg(string key, string value) {
            // Use RuntimeInformation.IsOSPlatform to satisfy the compiler (CA1416).
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return (string?) Registry.GetValue(key, value, null);
            return null;
        }

        protected string? GetEnv(string key) {
            return Environment.GetEnvironmentVariable(key);
        }

        protected string? Combine(params string?[] parts) {
            foreach (string? part in parts)
                if (string.IsNullOrEmpty(part))
                    return null;
            return Path.Combine(parts!);
        }

        public virtual bool Owns(Installation i) {
            return i.Type == FinderTypeDefault;
        }

        public abstract IAsyncEnumerable<Installation> FindCandidates();

    }

    public class FinderManager {

        public readonly App App;

        public List<Finder> Finders = new();

        public List<Installation> Found = new();

        public List<Installation> Added = new();

        public event Action<FinderUpdateState, List<Installation>, InstallManagerScene.InstallList>? Updated;

        public FinderManager(App app) {
            App = app;

            foreach (Type type in UIReflection.GetAllTypes(typeof(Finder))) {
                if (type.IsAbstract)
                    continue;

                Finders.Add((Finder) Activator.CreateInstance(type, this)!);
            }

            Finders.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        private readonly object RefreshingLock = new();
        public Task<List<Installation>> Refreshing = Task.FromResult(new List<Installation>());
        public Task<List<Installation>> Refresh() {
            Added = Config.Instance.ManualInstalls;

            if (!Refreshing.IsCompleted)
                return Refreshing;
            lock (RefreshingLock) {
                if (!Refreshing.IsCompleted)
                    return Refreshing;
                return Refreshing = Task.Run(async () => {
                    AppLogger.Log.Information("Refreshing install list");
                    HashSet<string> added = new();
                    List<Installation> installs = new();
                    Updated?.Invoke(FinderUpdateState.Start, installs, InstallManagerScene.InstallList.Found);
                    await foreach (Installation install in FindAll()) {
                        if (added.Add(install.Root)) {
                            AppLogger.Log.Information($"Found install: {install.Type} - {install.Root}");
                            installs.Add(install);
                            Updated?.Invoke(FinderUpdateState.Add, installs, InstallManagerScene.InstallList.Found);
                        }
                    }
                    Found = installs;
                    Updated?.Invoke(FinderUpdateState.End, installs, InstallManagerScene.InstallList.Found);
                    return installs;
                });
            }
        }

        public async IAsyncEnumerable<Installation> FindAll() {
            List<IAsyncEnumerator<Installation>> finding = Finders.Select(finder => FindAllIn(finder).GetAsyncEnumerator()).ToList();
            List<Task<bool>> pending = new();
            Dictionary<IAsyncEnumerator<Installation>, Task<bool>> map = new();

            try {
                for (int i = 0; i < finding.Count; i++) {
                    IAsyncEnumerator<Installation> f = finding[i];
                    pending.Add(map[f] = f.MoveNextAsync().AsTask());
                }

                do {
                    Task.WaitAny(pending.ToArray());

                    for (int i = 0; i < finding.Count; i++) {
                        IAsyncEnumerator<Installation> f = finding[i];
                        Task<bool> t = map[f];
                        if (t.IsCompleted) {
                            if (await t) {
                                yield return f.Current;
                                pending[i] = map[f] = f.MoveNextAsync().AsTask();
                            } else {
                                await f.DisposeAsync();
                                finding.RemoveAt(i);
                                pending.RemoveAt(i);
                                map.Remove(f);
                                i--;
                            }
                        }
                    }
                } while (pending.Count > 0);

            } finally {
                foreach (Task<bool> t in pending) {
                    if (!t.IsCompleted) {
                        try {
                            await t;
                        } catch { }
                    }
                }

                List<Exception> ex = new();
                foreach (IAsyncEnumerator<Installation> f in finding) {
                    try {
                        await f.DisposeAsync();
                    } catch (Exception e) {
                        ex.Add(e);
                    }
                }
            }
        }

        private async IAsyncEnumerable<Installation> FindAllIn(Finder finder) {
            await foreach (Installation install in finder.FindCandidates()) {
                install.Finder = finder;
                if (install.FixPath())
                    yield return install;
            }
        }

        // Helper to prevent duplicates
        public bool AddManualInstall(Installation install) {
            foreach (Installation item in Added) {
                if (item.Root == install.Root) {
                    return false;
                }
            }
            foreach (Installation item in Found) {
                if (item.Root == install.Root) {
                    return false;
                }
            }
            Added.Add(install);
            Config.Instance.ManualInstalls = Added;
            Config.Instance.Save();
            return true;
        }

        public void RemoveInstallation(Installation install) {
            Added.Remove(install);
            Config.Instance.ManualInstalls = Added;
            Config.Instance.Save();
        }

    }

    public enum FinderUpdateState {
        Start,
        Add,
        End,
        Manual
    }

    public class Installation {

        public string Type; // TODO: Make this an enum
        public string Name;
        public string Root;

        public string? IconOverride;

        public string Icon => IconOverride ?? Type;

        [NonSerialized]
        public Finder? Finder;

        [NonSerialized]
        private (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) VersionLast;

        public Installation(string type, string name, string root) {
            Type = type;
            Name = name;
            Root = root;
        }

        public bool FixPath() {
            string root = Root;

            if (File.Exists(root)) { // root probably points to Celeste.exe
                string? newRoot = Path.GetDirectoryName(root);
                if (!string.IsNullOrEmpty(newRoot)) {
                    root = newRoot;
                }
            }

            if (root == "") {
                return false;
            }

            // Early exit check if possible.
            string path = root;
            if (File.Exists(Path.Combine(path, "Celeste.exe")) 
                || File.Exists(Path.Combine(path, "Celeste.dll"))) {
                Root = path;
                return true;
            }

            if (root.EndsWith('/') || root.EndsWith('\\')) {
                root = root[..^1];
            }

            // If dealing with macOS paths, get the root dir and find the new current dir.
            // We shouldn't need to check for \\ here.
            if (root.EndsWith("/Celeste.app/Contents/Resources")) {
                root = root[..^"/Celeste.app/Contents/Resources".Length];
            } else if (root.EndsWith("/Celeste.app/Contents/MacOS")) {
                root = root[..^"/Celeste.app/Contents/MacOS".Length];
            }

            path = root;
            if (File.Exists(Path.Combine(path, "Celeste.exe")) 
                || File.Exists(Path.Combine(path, "Celeste.dll"))) {
                Root = path;
                return true;
            }

            // Celeste 1.3.3.0 and newer
            path = Path.Combine(root, "Celeste.app", "Contents", "Resources");
            if (File.Exists(Path.Combine(path, "Celeste.exe"))) {
                Root = path;
                return true;
            }

            // Celeste pre 1.3.3.0
            path = Path.Combine(root, "Celeste.app", "Contents", "MacOS");
            if (File.Exists(Path.Combine(path, "Celeste.exe"))) {
                Root = path;
                return true;
            }

            return false;
        }
        
        /// <summary>
        /// Retrieves info about an install
        /// </summary>
        /// <param name="force">Whether to re scan the install for changes</param>
        /// <returns>A Tuple:
        /// - Modifiable: Whether the install can be modded or not
        /// - Full: A summary of the install as a string
        /// - Version: Represents the Celeste version
        /// - Framework: Represents the rendering framework as a string, either "FNA" or "XNA"
        /// - ModName: The name of the installed mod loader.
        /// - ModVersion: The version of the installed mod loader.
        /// </returns>
        public (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) ScanVersion(bool force) {
            if (!force && VersionLast != default)
                return VersionLast;

            string root = Root;

            if (!File.Exists(Path.Combine(root, "Celeste.exe")) && !File.Exists(Path.Combine(root, "Celeste.dll"))) {
                return VersionLast = (false, "Celeste.exe or Celeste.dll missing", null, null, null, null);
            }

            // Check if we're dealing with the UWP version.
            if (File.Exists(Path.Combine(root, "AppxManifest.xml")) &&
                File.Exists(Path.Combine(root, "xboxservices.config"))) {
                try {
                    using (Stream s = File.Open(Path.Combine(root, "Celeste.exe"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (ModuleDefinition.ReadModule(s)) {
                        // no-op, just try to see if the file can be opened at all.
                    }
                } catch {
                    return VersionLast = (false, "UWP unsupported", null, null, null, null);
                }
            }

            string fileName = "";
            Retry:
            try {
                if (fileName == "") {
                    if (File.Exists(Path.Combine(root,
                            "Celeste.dll"))) // always default to Celeste.dll because its likely-er
                        fileName = "Celeste.dll";
                    else
                        fileName = "Celeste.exe";
                }


                using ModuleDefinition game = ModuleDefinition.ReadModule(Path.Combine(root, fileName));
                
                // Celeste's version is set by - and thus stored in - the Celeste .ctor
                if (game.GetType("Celeste.Celeste") is not TypeDefinition t_Celeste)
                    return VersionLast = (false, "Malformed Celeste.exe: Can't find main type", null, null, null,
                        null);

                MethodDefinition? c_Celeste =
                    t_Celeste.FindMethod("System.Void orig_ctor_Celeste()") ??
                    t_Celeste.FindMethod("System.Void .ctor()");
                if (c_Celeste is null)
                    return VersionLast = (false, "Malformed Celeste.exe: Can't find constructor", null, null, null,
                        null);
                if (!c_Celeste.HasBody)
                    return VersionLast = (false, "Malformed Celeste.exe: Constructor without code", null, null,
                        null, null);

                // Grab the version from the .ctor, in hopes that any mod loader 
                Version? version = null;
                using (ILContext il = new(c_Celeste)) {
                    il.Invoke(il => {
                        ILCursor c = new(il);

                        MethodReference? c_Version = null;
                        if (!c.TryGotoNext(i =>
                                i.MatchNewobj(out c_Version) &&
                                c_Version?.DeclaringType?.FullName == "System.Version") || c_Version is null)
                            return;

                        if (c_Version.Parameters.All(p => p.ParameterType.MetadataType == MetadataType.Int32)) {
                            int[] args = new int[c_Version.Parameters.Count];
                            for (int i = args.Length - 1; i >= 0; --i) {
                                c.Index--;
                                args[i] = c.Next.GetInt();
                            }

                            switch (args.Length) {
                                case 2:
                                    version = new(args[0], args[1]);
                                    break;

                                case 3:
                                    version = new(args[0], args[1], args[2]);
                                    break;

                                case 4:
                                    version = new(args[0], args[1], args[2], args[3]);
                                    break;
                            }

                        } else if (c_Version.Parameters.Count == 1 &&
                                   c_Version.Parameters[0].ParameterType.MetadataType == MetadataType.String &&
                                   c.Prev.Operand is string arg) {
                            version = new(arg);
                        }
                    });
                }

                if (version is null)
                    return VersionLast = (false, "Malformed Celeste.exe: Can't parse version", null, null, null,
                        null);

                string framework = game.AssemblyReferences.Any(r => r.Name == "FNA") ? "FNA" : "XNA";

                // TODO: Find Matterhorn and grab its version.

                if (game.GetType("Celeste.Mod.Everest") is TypeDefinition t_Everest &&
                    t_Everest.FindMethod("System.Void .cctor()") is MethodDefinition c_Everest) {
                    // Note: The very old Everest.Installer GUI and old Olympus assume that the first operation in .cctor is ldstr with the version string.
                    // The string might move in the future, but at least the format should be the same.
                    // It should thus be safe to assume that the first string with a matching format is the Everest version.
                    string? versionEverestFull = null;
                    Version? versionEverest = null;
                    bool versionEverestValid = false;
                    using (ILContext il = new(c_Everest)) {
                        il.Invoke(il => {
                            ILCursor c = new(il);
                            while (!versionEverestValid &&
                                   c.TryGotoNext(i => i.MatchLdstr(out versionEverestFull)) &&
                                   versionEverestFull is not null) {
                                int split = versionEverestFull.IndexOf('-');
                                versionEverestValid = split != -1
                                    ? Version.TryParse(versionEverestFull.AsSpan(0, split), out versionEverest)
                                    : Version.TryParse(versionEverestFull, out versionEverest);
                            }
                        });
                    }

                    return !string.IsNullOrEmpty(versionEverestFull) && versionEverest is not null &&
                           versionEverestValid
                        ? VersionLast = (true, $"Celeste {version}-{framework} + Everest {versionEverestFull}",
                            version, framework, "Everest", versionEverest)
                        : VersionLast = (true, $"Celeste {version}-{framework} + Everest ?", version, framework,
                            "Everest", null);
                }

                return VersionLast = (true, $"Celeste {version}-{framework}", version, framework, null, null);

            } catch (Exception e) {
                if (fileName == "Celeste.dll") { // the dll was borked, may be vanilla with residual files
                    fileName = "Celeste.exe";
                    goto Retry;
                }
                AppLogger.Log.Warning($"Failed to scan installation of type \"{Type}\" at \"{root}\":\n{e}");
                return VersionLast = (false, "?", null, null, null, null);
            }
        }

        public override bool Equals(object? obj) {
            if (base.Equals(obj)) return true;
            if (!(obj is Installation)) return false;

            Installation other = (Installation) obj;
            return (
                this.Type.Equals(other.Type) &&
                this.Name.Equals(other.Name) &&
                this.Root.Equals(other.Root)
            );
        }

        public override int GetHashCode() {
            return base.GetHashCode(); // Just to avoid warnings
        }

    }
}
