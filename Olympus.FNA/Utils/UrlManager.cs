using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
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
            public string TryHttpGetData() {
                if (UrlList.Count == 0)
                    throw new FormatException($"Couldn't read urls from {UrlsYamlPath}");
                // make sure the preferred url is on front, because yamldotnet doesn't ensure it
                if (!UrlList[0].Preferred) {
                    for (int i = 0; i < UrlList.Count; i++) {
                        if (!UrlList[i].Preferred) continue;
                        
                        DataBaseUrlEntry entry = UrlList[i];
                        UrlList.RemoveAt(i);
                        UrlList.Insert(0, entry);
                    }
                }
                using HttpClient wc = new();
                wc.Timeout = TimeSpan.FromMilliseconds(10000); // 10s timeout
                foreach (var urlEntry in UrlList) {
                    try {
                        string urlString = urlEntry.Url;
                        if (urlEntry.ProvidesUrl) {
                            Console.WriteLine($"Obtaining url from {urlString}");
                            // The following wrapper makes it possible to call async method from a sync context
                            // Note that calling the get accessor on Result forcibly waits until the task is done
                            urlString = Task.Run(async () => await wc.GetStringAsync(urlString)).Result
                                .TrimEnd(Environment.NewLine.ToCharArray()); // remove newline at the end
                        }

                        Console.WriteLine($"Downloading content from {urlString}");
                        string data = Task.Run(async () => await wc.GetStringAsync(urlString)).Result;

                        return data;
                    } catch (Exception e) when (e is HttpRequestException or TaskCanceledException) {
                        Console.WriteLine($"Url entry {urlEntry.Url} failed!");
                    }
                }

                throw new HttpRequestException("No url was able to successfully query the data");
            }
        }

        public class DataBaseUrlEntry {
            [YamlMember(Alias = "url", ApplyNamingConventions = false)]
            public string Url = "";
            [YamlMember(Alias = "provides_url", ApplyNamingConventions = false)]
            public bool ProvidesUrl = false;
            [YamlMember(Alias = "preferred", ApplyNamingConventions = false)]
            public bool Preferred = false;
        }

    }
}