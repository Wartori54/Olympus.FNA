using MonoMod.Utils;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Olympus.Finders {
    public class LutrisDatabaseFinder : Finder {
        protected override Installation.InstallationType InstallationType => Installation.InstallationType.LutrisDatabase;

        public LutrisDatabaseFinder(FinderManager manager)
            : base(manager) {
        }

        public string? FindDatabase() {
            if (PlatformHelper.Is(Platform.Linux)) {
                return IsFile(Combine(GetEnv("HOME"), ".local", "share", "lutris", "pga.db"));
            }

            return null;
        }

        public override async IAsyncEnumerable<Installation> FindCandidates() {
            string? dbPath = FindDatabase();
            if (string.IsNullOrEmpty(dbPath))
                yield break;

            string? path;

            using (SqliteConnection con = new(new SqliteConnectionStringBuilder() {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString())) {
                await con.OpenAsync();

                using SqliteCommand cmd = con.CreateCommand();
                cmd.CommandText = @"
                    SELECT directory FROM games
                    WHERE name == ""Celeste""
                ";

                path = await cmd.ExecuteScalarAsync() as string;
            }

            if (!string.IsNullOrEmpty(path)) {
                yield return new(InstallationType, path);
                yield break;
            }
        }

    }
}
