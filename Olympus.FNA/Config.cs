using MonoMod.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Olympus {
    public class Config {

        [NonSerialized]
        public string? Path;

        [NonSerialized]
        private readonly JsonHelper.ExistingCreationConverter<Config> Converter;

        [NonSerialized]
        private List<Action<Installation?>> InstallUpdateEvents = new();
        [NonSerialized]
        private List<Action<Installation?, Installation?>> InstallUpdateEventsWithOld = new();
        
        [NonSerialized]
        public static Config Instance = new();

        public Config() {
            Converter = new(this);
            Instance = this;
        }

        public string Updates = "stable";

        public Version VersionPrev = new();
        public Version Version = App.Version;

        private Installation? Install;

        public Installation? Installation {
            get => Install;
            set {
                Installation? oldInstall = Install;
                if (Install != null) Install.WatcherEnabled = false;
                Install = value;
                if (Install != null) Install.WatcherEnabled = true;
                
                foreach (Action<Installation?> subscribed in InstallUpdateEvents) {
                    Task.Run(() =>
                        subscribed.Invoke(Install)
                    );
                }
                
                foreach (Action<Installation?, Installation?> subscribed in InstallUpdateEventsWithOld) {
                    Task.Run(() =>
                        subscribed.Invoke(Install, oldInstall)
                    );
                }
            }
        }
        
        public List<Installation> ManualInstalls = new();

        public bool? CSD;
        public bool? VSync;

        public float Overlay;

        public static string GetDefaultDir() {
            if (PlatformHelper.Is(Platform.MacOS)) {
                string? home = Environment.GetEnvironmentVariable("HOME");
                if (!string.IsNullOrEmpty(home)) {
                    return System.IO.Path.Combine(home, "Library", "Application Support", App.Name);
                }
            }

            if (PlatformHelper.Is(Platform.Unix)) {
                string? config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (!string.IsNullOrEmpty(config)) {
                    return System.IO.Path.Combine(config, App.Name);
                }
                string? home = Environment.GetEnvironmentVariable("HOME");
                if (!string.IsNullOrEmpty(home)) {
                    return System.IO.Path.Combine(home, ".config", App.Name);
                }
            }

            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), App.Name);
        }

        public static string GetDefaultConfigFilePath() {
            return System.IO.Path.Combine(GetDefaultDir(), "config.json");
        }

        public static string GetCacheDir() {
            if (PlatformHelper.Is(Platform.MacOS)) {
                string? home = Environment.GetEnvironmentVariable("HOME");
                if (!string.IsNullOrEmpty(home)) {
                    return System.IO.Path.Combine(home, "Library", "Caches", App.Name);
                }
            }

            if (PlatformHelper.Is(Platform.Unix)) {
                string? config = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                if (!string.IsNullOrEmpty(config)) {
                    return System.IO.Path.Combine(config, App.Name);
                }
                string? home = Environment.GetEnvironmentVariable("HOME");
                if (!string.IsNullOrEmpty(home)) {
                    return System.IO.Path.Combine(home, ".cache", App.Name);
                }
            }

            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.Name);
        }

        public void Load() {
            string path = Path ??= GetDefaultConfigFilePath();

            if (!File.Exists(path))
                return;

            JsonHelper.Serializer.Converters.Add(Converter);

            try {
                using StreamReader sr = new(path);
                using JsonTextReader jtr = new(sr);

                object? other = JsonHelper.Serializer.Deserialize<Config>(jtr);

                if (other is null) {
                    AppLogger.Log.Warning("Loading config returned null, discarding current file");
                    return;
                }
                if (other != this)
                    throw new Exception("Loading config created new instance");

            } finally {
                JsonHelper.Serializer.Converters.Remove(Converter);
            }
        }

        public void Save() {
            string path = Path ??= GetDefaultConfigFilePath();

            string? dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(path))
                File.Delete(path);

            using StreamWriter sw = new(path);
            using JsonTextWriter jtw = new(sw);

            JsonHelper.Serializer.Serialize(jtw, this);
        }

        
        // TODO: Please please please move this to an actual good event c# system
        // Subscribes an event for when the currently active install gets changed
        // Note: this call will be asyncronous
        public void SubscribeInstallUpdateNotify(Action<Installation?> action) {
            InstallUpdateEvents.Add(action);
        }

        public void SubscribeInstallUpdateNotify(Action<Installation?, Installation?> action) {
            InstallUpdateEventsWithOld.Add(action);
        }

    }
}
