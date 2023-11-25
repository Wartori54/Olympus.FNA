using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace Olympus.API {
    
    // For anyone about to yell at me because Web.cs and UrlManager.cs are the same thing:
    // UrlManager is to work with url.yaml files, thus all its methods work with those files
    // Web is solely for context/url agnostic methods, thus all its methods take in urls as strings
    // if and only if you have that in mind, you are now allowed to yell at me
    public class UrlManager {
        private readonly Dictionary<string, DataBaseUrlEntry> urls = new();
        private readonly string urlsYamlPath;
        
        public UrlManager(string urlsYamlPath) {
            this.urlsYamlPath = urlsYamlPath;
            List<DataBaseUrlEntry> readUrls;
            using (Stream? stream = OlympUI.Assets.OpenStream(urlsYamlPath)) {
                if (stream == null) {
                    throw new FileNotFoundException("Couldn't query urls, {0} file not found", urlsYamlPath);
                }
                using (StreamReader reader = new(stream))
                    readUrls = YamlHelper.Deserializer.Deserialize<List<DataBaseUrlEntry>>(reader);
            }

            foreach (DataBaseUrlEntry url in readUrls) {
                if (!urls.TryAdd(url.Tag, url)) {
                    AppLogger.Log.Error($"File {urlsYamlPath} contains multiple urls with same tag: {url.Tag}");
                    MetaNotificationScene.PushNotification(new Notification{ Message = $"File {urlsYamlPath} contains multiple urls with same tag: {url.Tag}", Level = Notification.SeverityLevel.Warning });
                }
            }
        }

        private object TryHttpGetData(string tag, ICollection<string>? withFlags, Func<string, HttpClient, Func<Task<object>?>> task) {
            withFlags ??= new List<string>();
            
            using HttpClient wc = new();
            wc.Timeout = TimeSpan.FromMilliseconds(10000); // 10s timeout
            wc.DefaultRequestHeaders.UserAgent.ParseAdd("amongus");
            if (!urls.TryGetValue(tag, out DataBaseUrlEntry? urlEntry)) {
                throw new InvalidOperationException(
                    $"Tried to obtain tag non-existent tag: {tag} from file {urlsYamlPath}");
            }
            
            string urlString = urlEntry.Url;

            urlString = AddFlags(urlString, urlEntry.Flags, withFlags);

            AppLogger.Log.Information($"Downloading content from {urlString}");
            return Task.Run(task(urlString, wc)).Result;
        }

        /// <summary>
        /// Get the database entry of a url tag
        /// </summary>
        /// <returns>The database entry</returns>
        public DataBaseUrlEntry GetEntry(string tag) {
            if (!urls.TryGetValue(tag, out DataBaseUrlEntry? urlEntry)) {
                throw new InvalidOperationException($"Tried to obtain tag non-existent tag: {tag} from file {urlsYamlPath}");
            }

            return urlEntry;
        }
        
        /// <summary>
        /// Fetches data from a url tag as string
        /// </summary>
        /// <returns>The data from the url as a string</returns>
        public string TryHttpGetDataString(string tag, ICollection<string>? withFlags = null) {
            // The following wrapper (the lambda) makes it possible to call async method from a sync context
            // Note that calling the get accessor on Result forcibly waits until the task is done
            return (string) TryHttpGetData(tag, withFlags, (urlString, wc) => async () => await wc.GetStringAsync(urlString));
        }
        
         /// <summary>
        /// Tries all urls from this list until success, throws HttpRequestException otherwise
        /// </summary>
        /// <returns>The data from the url as a async stream</returns>
        public Stream TryHttpGetDataStream(string tag, ICollection<string>? withFlags = null) {
            return (Stream) TryHttpGetData(tag, withFlags, (urlString, wc) => async () => await wc.GetStreamAsync(urlString));
        }

        private static string AddFlags(string urlString, IReadOnlyDictionary<string, string?> flagDict, ICollection<string> flags) {
            string originalUrl = urlString;
            foreach (string flag in flags) {
                if (!flagDict.TryGetValue(flag, out string? temp)) {
                    AppLogger.Log.Warning($"Unknown flag ({flag}) for url: {originalUrl}, skipping...");
                    continue;
                }
                urlString += temp;
            }

            return urlString;
        }

        public class DataBaseUrlEntry {
            [YamlMember(Alias = "tag", ApplyNamingConventions = false)]
            public string Tag = "";
            [YamlMember(Alias = "url", ApplyNamingConventions = false)]
            public string Url = "";
            [YamlMember(Alias = "flags", ApplyNamingConventions = false)]
            public Dictionary<string, string?> Flags = new ();
        }

    }
}