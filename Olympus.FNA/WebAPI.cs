using Newtonsoft.Json;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Olympus {
    public interface IWebAPI {

        Task<IEntry[]> GetFeaturedEntries();

        Task<IEntry[]> GetSearchEntries(string query);

        public interface IEntry {
            string Name { get; }
            string PageURL { get; }
            ICategory[] Categories { get; }
            string ShortDescription { get; }
            int Downloads { get; }
            string[] Images { get; }
            bool CanBeFeatured { get; }
        }

        public interface ICategory {
            string Name { get; }
            string ID { get; }
        }

        public record Category(string Name, string ID) : ICategory;

    }

    public abstract class WebAPI<TEntry> : IWebAPI where TEntry : IWebAPI.IEntry {

        public readonly App App;

        public WebAPI(App app) {
            App = app;
        }

        public abstract Task<TEntry[]> GetFeaturedEntries();

        public abstract Task<TEntry[]> GetSearchEntries(string query);

        async Task<IWebAPI.IEntry[]> IWebAPI.GetFeaturedEntries()
            => (await GetFeaturedEntries()).Cast<IWebAPI.IEntry>().ToArray();

        async Task<IWebAPI.IEntry[]> IWebAPI.GetSearchEntries(string query)
            => (await GetSearchEntries(query)).Cast<IWebAPI.IEntry>().ToArray();

    }

    public class CelesteWebAPI : WebAPI<CelesteWebAPI.MaxEntry> {

        public CelesteWebAPI(App app)
            : base(app) {
        }

        public override async Task<MaxEntry[]> GetFeaturedEntries()
            => await App.Web.GetJSON<MaxEntry[]>(@"https://max480-random-stuff.appspot.com/celeste/gamebanana-featured") ?? Array.Empty<MaxEntry>();

        public override async Task<MaxEntry[]> GetSearchEntries(string query)
            => await App.Web.GetJSON<MaxEntry[]>($@"https://max480-random-stuff.appspot.com/celeste/gamebanana-search?q={Uri.EscapeDataString(query)}&full=true") ?? Array.Empty<MaxEntry>();

        public async Task<MaxEntry[]> GetSortedEntries(int page, SortBy sort, string? itemtypeFilterType = null, string? itemtypeFilterValue = null)
            => await App.Web.GetJSON<MaxEntry[]>(
                $@"https://max480-random-stuff.appspot.com/celeste/gamebanana-list?{sort switch {
                    SortBy.Latest => "sort=latest&",
                    SortBy.Likes => "sort=likes&",
                    SortBy.Views => "sort=views&",
                    SortBy.Downloads => "sort=downloads&",
                    _ => "",
                }}{(
                    string.IsNullOrEmpty(itemtypeFilterValue) ? "" :
                    $"{itemtypeFilterType}={itemtypeFilterValue}&"
                )}page={page}&full=true") ?? Array.Empty<MaxEntry>();

        public class MaxEntry : IWebAPI.IEntry {
            public string? GameBananaType;
            public int GameBananaID;
            public string Author = "";
            public long CreatedDate;
            public string Name = "";
            public string PageURL = "";
            public int CategoryID;
            public string CategoryName = "";
            public string Description = "";
            public string Text = "";
            public int Views;
            public int Downloads;
            public int Likes;
            public string[] Screenshots = Array.Empty<string>();
            public string[] MirroredScreenshots = Array.Empty<string>();
            public File[] Files = Array.Empty<File>();

            string IWebAPI.IEntry.Name => Name;
            string IWebAPI.IEntry.PageURL => PageURL;
            IWebAPI.ICategory[] IWebAPI.IEntry.Categories => new IWebAPI.ICategory[] { new IWebAPI.Category(CategoryName, CategoryID.ToString()) };
            string IWebAPI.IEntry.ShortDescription => Description;
            int IWebAPI.IEntry.Downloads => Downloads;
            string[] IWebAPI.IEntry.Images => MirroredScreenshots;
            bool IWebAPI.IEntry.CanBeFeatured => GameBananaType != "Tool";

            public class File {
                public string Name = "";
                public string Description = "";
                public bool HasEverestYaml;
                public long Size;
                public long CreatedDate;
                public int Downloads;
                public string URL = "";
            }
        }

        public enum SortBy {
            None,
            Latest,
            Likes,
            Views,
            Downloads
        }

    }

    public class ThunderstoreWebAPI : WebAPI<ThunderstoreWebAPI.PackageCard> {

        public const string ApiExperimental = @"https://thunderstore.io/api/experimental";

        public readonly string CommunityID;

        public ThunderstoreWebAPI(App app, string communityID)
            : base(app) {
            CommunityID = communityID;
        }

        public override async Task<PackageCard[]> GetFeaturedEntries()
            => (await App.Web.GetJSON<CommunityPackageList>($@"{ApiExperimental}/frontend/c/{CommunityID}/packages/"))?.Packages ?? Array.Empty<PackageCard>();

        public override Task<PackageCard[]> GetSearchEntries(string query)
            => throw new NotImplementedException();

        public class CommunityPackageList {
            [JsonProperty("packages")]
            public PackageCard[] Packages = Array.Empty<PackageCard>();
        }

        public class PackageCard : IWebAPI.IEntry {
            [JsonProperty("cackages")]
            public PackageCategory[] Categories = Array.Empty<PackageCategory>();
            [JsonProperty("community_identifier")]
            public string CommunityIdentifier = "";
            [JsonProperty("description")]
            public string Description = "";
            [JsonProperty("download_count")]
            public int DownloadCount;
            [JsonProperty("image_src")]
            public string ImageSrc = "";
            [JsonProperty("namespace")]
            public string Namespace = "";
            [JsonProperty("package_name")]
            public string PackageName = "";

            string IWebAPI.IEntry.Name => PackageName.Replace('_', ' ');
            string IWebAPI.IEntry.PageURL => $@"https://{CommunityIdentifier}.thunderstore.io/package/{Namespace}/{PackageName}";
            IWebAPI.ICategory[] IWebAPI.IEntry.Categories => Categories;
            string IWebAPI.IEntry.ShortDescription => Description;
            int IWebAPI.IEntry.Downloads => DownloadCount;
            string[] IWebAPI.IEntry.Images => new string[] { ImageSrc };
            bool IWebAPI.IEntry.CanBeFeatured => true;
        }

        public class PackageCategory : IWebAPI.ICategory {
            public string Name = "";
            public string Slug = "";

            string IWebAPI.ICategory.Name => Name;
            string IWebAPI.ICategory.ID => Slug;
        }

    }
}
