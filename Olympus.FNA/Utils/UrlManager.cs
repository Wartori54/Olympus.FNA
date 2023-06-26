using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Olympus.Utils {

    public static class UrlManager {
        
        private const string UrlsYamlPath = "metadata/urls.yaml";
        
        
        private static DataBaseUrls? urls;
        
        public static DataBaseUrls Urls { 
            get {
                if (urls != null) return urls;
                
                // retrieve the url
                using (Stream? stream = OlympUI.Assets.OpenStream(UrlsYamlPath)) {
                    if (stream == null) {
                        throw new FileNotFoundException("Couldn't query DB urls, {0} file not found", UrlsYamlPath);
                    }
                    using (StreamReader reader = new(stream))
                        urls = YamlHelper.Deserializer.Deserialize<DataBaseUrls>(reader);
                }
                return urls;
            }
        }

        /// <summary>
        /// Saves a stream to a file, calling a callback in the meantime
        /// </summary>
        /// <param name="outputFile">The file to redirect the stream</param>
        /// <param name="stream">The data</param>
        /// <param name="length">The total bytes of data</param>
        /// <param name="progressCallback">The callback for the progress update</param>
        public static async Task<bool> Stream2FileWithProgress(string outputFile, Task<Stream> stream, int length, Func<int, int, int, bool> progressCallback) {
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
        
        
        public class DataBaseUrls {
            public DataBaseUrlList ModDataBase = new();
            public DataBaseUrlList ModUpdateDataBase = new();
            public DataBaseUrlList EverestVersions = new();
        }
        
        public class DataBaseUrlList {
            public List<DataBaseUrlEntry> UrlList = new();
            
            /// <summary>
            /// Tries all urls from this list until success, throws HttpRequestException otherwise
            /// </summary>
            /// <returns>The data from the url as a string</returns>
            public string TryHttpGetDataString(ICollection<string>? withFlags = null) {
                withFlags ??= new List<string>();

                ValidateData();
                
                using HttpClient wc = new();
                wc.Timeout = TimeSpan.FromMilliseconds(10000); // 10s timeout
                foreach (DataBaseUrlEntry urlEntry in UrlList) {
                    try {
                        string urlString = urlEntry.Url;
                        if (urlEntry.ProvidesUrl)
                            urlString = GetUrlFromUrl(urlEntry);

                        urlString = AddFlags(urlString, urlEntry.Flags, withFlags);

                        Console.WriteLine($"Downloading content from {urlString}");
                        return Task.Run(async () => await wc.GetStringAsync(urlString)).Result;
                    } catch (Exception e) when (e is HttpRequestException or TaskCanceledException) {
                        Console.WriteLine($"Url entry {urlEntry.Url} failed!");
                    }
                }

                throw new HttpRequestException("No url was able to successfully query the data");
            }
            
             /// <summary>
            /// Tries all urls from this list until success, throws HttpRequestException otherwise
            /// </summary>
            /// <returns>The data from the url as a async stream</returns>
            public Task<Stream> TryHttpGetDataStream(ICollection<string>? withFlags = null) {
                withFlags ??= new List<string>();

                ValidateData();

                using HttpClient wc = new();
                wc.Timeout = TimeSpan.FromMilliseconds(10000); // 10s timeout
                foreach (DataBaseUrlEntry urlEntry in UrlList) {
                    try {
                        string urlString = urlEntry.Url;
                        if (urlEntry.ProvidesUrl)
                            urlString = GetUrlFromUrl(urlEntry);

                        urlString = AddFlags(urlString, urlEntry.Flags, withFlags);

                        Console.WriteLine($"Downloading content from {urlString}");
                        return wc.GetStreamAsync(urlString);
                    } catch (Exception e) when (e is HttpRequestException or TaskCanceledException) {
                        Console.WriteLine($"Url entry {urlEntry.Url} failed!");
                    }
                }

                throw new HttpRequestException("No url was able to successfully query the data");
            }
            
            private void ValidateData() {
                if (UrlList.Count == 0)
                    throw new FormatException($"Couldn't read urls from {UrlsYamlPath}");
                // make sure the preferred url is on front, because yamldotnet doesn't ensure it
                if (UrlList[0].Preferred) return;
                for (int i = 0; i < UrlList.Count; i++) {
                    if (!UrlList[i].Preferred) continue;
                        
                    DataBaseUrlEntry entry = UrlList[i];
                    UrlList.RemoveAt(i);
                    UrlList.Insert(0, entry);
                }
            }

            private static string GetUrlFromUrl(DataBaseUrlEntry urlEntry) {
                using HttpClient wc = new();
                wc.Timeout = TimeSpan.FromMilliseconds(10000); // 10s timeout
                
                string urlString = urlEntry.Url;
                Console.WriteLine($"Obtaining url from {urlString}");
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
                        Console.WriteLine($"Unknown flag ({flag}) for url: {originalUrl}, skipping...");
                        continue;
                    }
                    urlString += temp;
                }

                return urlString;
            }
        }

        public class DataBaseUrlEntry {
            [YamlMember(Alias = "url", ApplyNamingConventions = false)]
            public string Url = "";
            [YamlMember(Alias = "provides_url", ApplyNamingConventions = false)]
            public bool ProvidesUrl = false;
            [YamlMember(Alias = "preferred", ApplyNamingConventions = false)]
            public bool Preferred = false;
            [YamlMember(Alias = "flags", ApplyNamingConventions = false)]
            public Dictionary<string, string?> Flags = new ();
        }

    }
}