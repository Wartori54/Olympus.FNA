using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Olympus.Utils {

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
                }
            }
        }



        /// <summary>
        /// Saves a stream to a file, calling a callback in the meantime
        /// </summary>
        /// <param name="outputFile">The file to redirect the stream</param>
        /// <param name="stream">The data</param>
        /// <param name="length">The total bytes of data</param>
        /// <param name="progressCallback">The callback for the progress update</param>
        public static async Task<bool> Stream2FileWithProgress(string outputFile, Task<Stream> stream, int length, 
                Func<int, int, int, bool> progressCallback) {
            if (File.Exists(outputFile))
                File.Delete(outputFile);
            await using FileStream output = File.OpenWrite(outputFile);

            DateTime timeStart = DateTime.Now;
            await using Stream input = await stream;
            progressCallback(0, length, 0);
            
            byte[] buffer = new byte[4096];
            DateTime timeLastSpeed = timeStart;
            int read = 1;
            int readForSpeed = 0;
            int pos = 0;
            int speed = 0;
            while (read > 0) {
                int count = length > 0 ? (int) Math.Min(buffer.Length, length - pos) : buffer.Length;
                read = await input.ReadAsync(buffer, 0, count);
                output.Write(buffer, 0, read);
                pos += read;
                readForSpeed += read;

                TimeSpan td = DateTime.Now - timeLastSpeed;
                if (td.TotalMilliseconds > 100) {
                    speed = (int) ((readForSpeed / 1024D) / td.TotalSeconds);
                    readForSpeed = 0;
                    timeLastSpeed = DateTime.Now;
                }

                if (!progressCallback(pos, length, speed)) {
                    return false;
                }
            }

            progressCallback(pos, length, speed);

            return true;
        }

        private object TryHttpGetData(string tag, ICollection<string>? withFlags, Func<string, HttpClient, Func<Task<object>?>> task) {
            withFlags ??= new List<string>();
            
            using HttpClient wc = new();
            wc.Timeout = TimeSpan.FromMilliseconds(10000); // 10s timeout
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
        /// Fetches data from a url tag as string
        /// </summary>
        /// <returns>The data from the url as a string</returns>
        public string TryHttpGetDataString(string tag, ICollection<string>? withFlags = null) {
            return (string) TryHttpGetData(tag, withFlags, (urlString, wc) => async () => await wc.GetStringAsync(urlString));
        }
        
         /// <summary>
        /// Tries all urls from this list until success, throws HttpRequestException otherwise
        /// </summary>
        /// <returns>The data from the url as a async stream</returns>
        public Stream TryHttpGetDataStream(string tag, ICollection<string>? withFlags = null) {
            return (Stream) TryHttpGetData(tag, withFlags, (urlString, wc) => async () => await wc.GetStreamAsync(urlString));
        }

        private static string GetUrlFromUrl(DataBaseUrlEntry urlEntry) {
            using HttpClient wc = new();
            wc.Timeout = TimeSpan.FromMilliseconds(10000); // 10s timeout
            
            string urlString = urlEntry.Url;
            AppLogger.Log.Information($"Obtaining url from {urlString}");
            // The following wrapper makes it possible to call async method from a sync context
            // Note that calling the get accessor on Result forcibly waits until the task is done
            urlString = Task.Run(async () => await wc.GetStringAsync(urlString)).Result
                .TrimEnd(Environment.NewLine.ToCharArray()); // remove newline at the end

            return urlString;
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