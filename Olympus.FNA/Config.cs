using MonoMod.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace Olympus {
    public class Config {

        [NonSerialized]
        public string? Path;

        [NonSerialized]
        private readonly JsonHelper.ExistingCreationConverter<Config> Converter;

        [NonSerialized]
        public static Config Instance = new();

        public Config() {
            Converter = new(this);
            Instance = this;
        }

        public string Updates = "stable";

        public Version VersionPrev = new();
        public Version Version = App.Version;

        public Installation? Install;
        public List<Installation> ManualInstalls = new();

        public bool? CSD;
        public bool? VSync;

        public float Overlay;

        public static string GetDefaultDir() {
            if (PlatformHelper.Is(Platform.MacOS)) {
                string? home = Environment.GetEnvironmentVariable("HOME");
                if (!string.IsNullOrEmpty(home)) {
                    return System.IO.Path.Combine(home, "Library", "Application Support", "Olympus.FNA");
                }
            }

            if (PlatformHelper.Is(Platform.Unix)) {
                string? config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (!string.IsNullOrEmpty(config)) {
                    return System.IO.Path.Combine(config, "Olympus.FNA");
                }
                string? home = Environment.GetEnvironmentVariable("HOME");
                if (!string.IsNullOrEmpty(home)) {
                    return System.IO.Path.Combine(home, ".config", "Olympus.FNA");
                }
            }

            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Olympus.FNA");
        }

        public static string GetDefaultPath() {
            return System.IO.Path.Combine(GetDefaultDir(), "config.json");
        }

        public void Load() {
            string path = Path ??= GetDefaultPath();

            if (!File.Exists(path))
                return;

            JsonHelper.Serializer.Converters.Add(Converter);

            try {
                using StreamReader sr = new(path);
                using JsonTextReader jtr = new(sr);

                object? other = JsonHelper.Serializer.Deserialize<Config>(jtr);

                if (other is null)
                    throw new Exception("Loading config returned null");
                if (other != this)
                    throw new Exception("Loading config created new instance");

            } finally {
                JsonHelper.Serializer.Converters.Remove(Converter);
            }
        }

        public void Save() {
            string path = Path ??= GetDefaultPath();

            string? dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(path))
                File.Delete(path);

            using StreamWriter sw = new(path);
            using JsonTextWriter jtw = new(sw);

            JsonHelper.Serializer.Serialize(jtw, this);
        }

    }
}
