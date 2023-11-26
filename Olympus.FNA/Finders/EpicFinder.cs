using MonoMod.Utils;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Olympus.Finders {
    public class EpicFinder : Finder {
        protected override Installation.InstallationType InstallationType => Installation.InstallationType.Epic;

        public EpicFinder(FinderManager manager)
            : base(manager) {
        }

        public string? FindRoot() {
            if (PlatformHelper.Is(Platform.Windows)) {
                return
                    IsDir(GetReg(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher", "AppDataPath")) ??
                    IsDir(GetReg(@"HKEY_LOCAL_MACHINE\SOFTWARE\Epic Games\EpicGamesLauncher", "AppDataPath"));
            }

            if (PlatformHelper.Is(Platform.MacOS)) {
                return IsDir(Combine(GetEnv("HOME"), "Library", "Application Support", "Epic", "EpicGamesLauncher", "Data"));
            }

            return null;
        }

        public override async IAsyncEnumerable<Installation> FindCandidates() {
            string? root = FindRoot();
            if (string.IsNullOrEmpty(root))
                yield break;

            root = IsDir(Combine(root, "Manifests"));
            if (string.IsNullOrEmpty(root))
                yield break;

            foreach (string dataPath in Directory.GetFiles(root, "*.item")) {
                Dictionary<string, object>? data = await Task.Run(() => {
                    using JsonTextReader jtr = new(new StreamReader(dataPath));
                    return JsonHelper.Serializer.Deserialize<Dictionary<string, object>>(jtr);
                });

                if (data is not null &&
                    data.TryGetValue("AppName", out object? nameRaw) && nameRaw as string == "Salt" &&
                    data.TryGetValue("InstallLocation", out object? pathRaw) && pathRaw is string path) {
                    yield return new(InstallationType, "Epic Games Store", path);
                    yield break;
                }
            }
        }

    }
}
