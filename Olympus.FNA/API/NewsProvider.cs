using Newtonsoft.Json;
using Olympus.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Olympus.API { 

    #region Interfaces
    
    public interface INewsProvider {

        public IEnumerable<INewsEntry> PollLast(int n);
        public IEnumerable<INewsEntry> PollAll();
    }

    public interface INewsEntry {
        public string Title { get; }
        public string Text { get; }
        public IEnumerable<ILink> Links { get; }
        public IEnumerable<string> Images { get; }
        
        public interface ILink {
            public string Text { get; }
            public string Url { get; }
            
        }
    }
    
    #endregion

    #region Implementations
    
    // For INewsProvider implementations, translatable news must be implemented on the implementation side,
    // detecting the current language and sending the data as normal, and if this ever becomes a reality,
    // a property on INewsProvider may be added to distinguish which ones are translatable

    public class SimpleLink : INewsEntry.ILink {
        public string Text { get; }
        public string Url { get; }
        
        public SimpleLink(string text, string url) {
            Text = text;
            Url = url;
        }
    }

    public class MaddieNewsProvider : INewsProvider {
        public const string YamlPath = "metadata/urls/maddie_news.yaml";
        
        private readonly UrlManager manager;

        private readonly TimedCache<IEnumerable<INewsEntry>> newsCache;

        public MaddieNewsProvider() {
            manager = new UrlManager(YamlPath);
            newsCache = new(TimeSpan.FromMinutes(15), RefreshCache, null);
        }

        private IEnumerable<INewsEntry> RefreshCache(object? _) {
            AppLogger.Log.Information("Refreshing news");
            Stream jsonData = manager.TryHttpGetDataStream("olympus-news");
            using StreamReader sr = new(jsonData);
            using JsonTextReader jtr = new(sr);

            List<MaddieNewsEntry>? entries = JsonHelper.Serializer.Deserialize<List<MaddieNewsEntry>>(jtr);
            if (entries == null) {
                entries = new();
                AppLogger.Log.Error("Failed to obtain olympus-news");
            }

            return entries;
        }
        
        public IEnumerable<INewsEntry> PollLast(int n) {
            List<INewsEntry> ret = new(n);
            int i = 0;
            foreach (INewsEntry newsEntry in newsCache.Value) {
                if (i >= n) break;
                i++;
                ret.Add(newsEntry);
            }

            return ret;
        }

        public IEnumerable<INewsEntry> PollAll() {
            return newsCache.Value;
        }
        
        public class MaddieNewsEntry : INewsEntry {
            [JsonProperty("title")]
            private string title = "";
            [JsonIgnore]
            public string Title => title;

            [JsonProperty("shortDescription")]
            private string shortDescription = "";
            [JsonIgnore]
            public string Text => shortDescription;

            // This provider only provides a single link
            [JsonProperty("link")]
            private string link = "";
            [JsonIgnore]
            public IEnumerable<INewsEntry.ILink> Links => new [] {new SimpleLink("Open in browser", link)};

            // Only an image too
            [JsonProperty("image")]
            private string image = "";
            [JsonIgnore]
            public IEnumerable<string> Images => new []{ image };
        }
    }
    

    #endregion

    public class NewsProviderManager {
        private List<NewsRegister> apis;
        public NewsRegister Default; // Default to first one, prob going to get modified anyway
        
        public static NewsProviderManager? Instance;

        public NewsProviderManager() {
            if (Instance != null)
                throw new InvalidOperationException("RemoteAPIManager created multiple times!");
            Instance = this;
            apis = new List<NewsRegister>();
            apis = typeof(NewsRegister).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(NewsRegister))
                .Select(f => (NewsRegister) f.GetValue(null)!)
                .ToList();

            Default = apis[0];
        }

        public INewsProvider GetDefault() => Default.ApiInstance;
        
        
        public class NewsRegister {
            public static readonly NewsRegister MaddieNews = new("Olympus-news (maddie480.ovh)", new MaddieNewsProvider());
            
            public readonly string FriendlyName;
            public readonly INewsProvider ApiInstance;
            
            private NewsRegister(string friendlyName, INewsProvider apiInstance) {
                FriendlyName = friendlyName;
                ApiInstance = apiInstance;
            }
            
        }
        
        
    }
}