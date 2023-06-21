using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Olympus.Utils {
    public static class EverestInstaller {

        public static ICollection<EverestVersion>? QueryEverestVersions() {
            string jsonData = UrlManager.Urls.EverestVersions.TryHttpGetData(new List<string>{"includeCore"});
            List<EverestVersion>? versions = JsonConvert.DeserializeObject<List<EverestVersion>>(jsonData);
            return versions;
        }


        [Serializable]
        public class EverestVersion {
            // [JsonIgnore]
            // public DateTime versionDate {
            //     get {
            //         if (DateTime.TryParseExact(date, "yyyy-MM-ddTHH:mm:ss.fffffffZ", null, DateTimeStyles.None, out DateTime parsedDate))
            //             return parsedDate;
            //         return DateTime.MinValue;
            //     }
            // }

            public DateTime date = new();
            public int mainFileSize;
            public string mainDownload = "";
            // public string olympusMetaDownload = ""; // This is commented on purpose
            public string author = "";
            // public string olympusBuildDownload = ""; // Since we wont be using those
            public string description = "";
            public string branch = "";
            public int version;

            public EverestBranch Branch => EverestBranch.FromString(branch);
        }

        public class EverestBranch {
            public static EverestBranch Stable = new("Stable");
            public static EverestBranch Beta = new("Beta");
            public static EverestBranch Dev = new("Dev");
            public static EverestBranch Core = new("Core");

            private readonly string asString;

            private EverestBranch(string asString) {
                this.asString = asString;
            }

            public static EverestBranch FromString(string str) {
                IEnumerable<FieldInfo> fieldInfos = typeof(EverestBranch).GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(f => f.FieldType == typeof(EverestBranch)).ToList();
                if (!fieldInfos.Any()) throw new FieldAccessException("No fields found in EverestBranch, (something is very wrong D:)");
                foreach (var fieldInfo in fieldInfos) {
                    EverestBranch everestBranch = (EverestBranch?) fieldInfo.GetValue(null) ?? throw new MissingFieldException("Couldn't cast field");
                    if (string.Equals(everestBranch.asString, str, StringComparison.InvariantCultureIgnoreCase))
                        return everestBranch;
                }

                throw new MissingFieldException($"Branch {str} not found!");

            }

            public override string ToString() {
                return asString;
            }
        }
    }
}
