using HarmonyLib;
using MediaBrowser.Controller.Entities;
using SQLitePCL.pretty;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.ModOptions;

namespace StrmAssistant.Mod
{
    public static class EnhanceChineseSearch
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();

        private static Type raw;
        private static MethodInfo sqlite3_enable_load_extension;
        private static FieldInfo sqlite3_db;
        private static MethodInfo _createConnection;
        private static MethodInfo _getJoinCommandText;
        private static MethodInfo _createSearchTerm;
        private static MethodInfo _cacheIdsFromTextParams;

        private static string _tokenizerPath;
        private static bool _patchPhase2Initialized;
        private static string[] _includeItemTypes = Array.Empty<string>();

        public static void Initialize()
        {
            _tokenizerPath = Path.Combine(Plugin.Instance.ApplicationPaths.PluginsPath, "libsimple.so");

            try
            {
                var sqlitePCLEx = Assembly.Load("SQLitePCLRawEx.core");
                raw = sqlitePCLEx.GetType("SQLitePCLEx.raw");
                sqlite3_enable_load_extension = raw.GetMethod("sqlite3_enable_load_extension",
                    BindingFlags.Static | BindingFlags.Public);

                var sqlitePCLPretty = Assembly.Load("SQLitePCL.pretty");
                var sQLiteDatabaseConnection = sqlitePCLPretty.GetType("SQLitePCL.pretty.SQLiteDatabaseConnection");
                sqlite3_db = sQLiteDatabaseConnection.GetField("db", BindingFlags.NonPublic | BindingFlags.Instance);

                var embySqlite = Assembly.Load("Emby.Sqlite");
                var baseSqliteRepository = embySqlite.GetType("Emby.Sqlite.BaseSqliteRepository");
                _createConnection = baseSqliteRepository.GetMethod("CreateConnection",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var embyServerImplementationsAssembly = Assembly.Load("Emby.Server.Implementations");
                var sqliteItemRepository =
                    embyServerImplementationsAssembly.GetType("Emby.Server.Implementations.Data.SqliteItemRepository");
                _getJoinCommandText = sqliteItemRepository.GetMethod("GetJoinCommandText",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _createSearchTerm =
                    sqliteItemRepository.GetMethod("CreateSearchTerm", BindingFlags.NonPublic | BindingFlags.Static);
                _cacheIdsFromTextParams = sqliteItemRepository.GetMethod("CacheIdsFromTextParams",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("EnhanceChineseSearch - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                (Plugin.Instance.GetPluginOptions().ModOptions.EnhanceChineseSearch ||
                 Plugin.Instance.GetPluginOptions().ModOptions.EnhanceChineseSearchRestore))
            {
                UpdateSearchScope();
                PatchPhase1();
            }
        }

        private static void PatchPhase1()
        {
            var ensureTokenizerResult = EnsureTokenizerExists();

            if (ensureTokenizerResult)
            {
                if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
                {
                    try
                    {
                        if (!IsPatched(_createConnection, typeof(EnhanceChineseSearch)))
                        {
                            HarmonyMod.Patch(_createConnection,
                                postfix: new HarmonyMethod(typeof(EnhanceChineseSearch).GetMethod("CreateConnectionPostfix",
                                    BindingFlags.Static | BindingFlags.NonPublic)));
                            Plugin.Instance.logger.Debug("Patch CreateConnection Success by Harmony");
                        }
                        return;
                    }
                    catch (Exception he)
                    {
                        Plugin.Instance.logger.Debug("Patch CreateConnection Failed by Harmony");
                        Plugin.Instance.logger.Debug(he.Message);
                        Plugin.Instance.logger.Debug(he.StackTrace);
                        PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                    }
                }
            }

            Plugin.Instance.logger.Debug("EnhanceChineseSearch - PatchPhase1 Failed");
            ResetOptions();
        }
        
        private static void PatchPhase2(IDatabaseConnection connection)
        {
            string ftsTableName;

            if (Plugin.Instance.ApplicationHost.ApplicationVersion >= new Version("4.8.3.0"))
            {
                ftsTableName = "fts_search9";
            }
            else
            {
                ftsTableName = "fts_search8";
            }

            var tokenizerCheckQuery = $@"
                SELECT 
                    CASE 
                        WHEN instr(sql, 'tokenize=""simple""') > 0 THEN 'simple'
                        WHEN instr(sql, 'tokenize=""unicode61 remove_diacritics 2""') > 0 THEN 'unicode61 remove_diacritics 2'
                        ELSE 'unknown'
                    END AS tokenizer_name
                FROM 
                    sqlite_master 
                WHERE 
                    type = 'table' AND 
                    name = '{ftsTableName}';";

            var tokenizerName = "unknown";
            var rebuildFtsResult = true;
            var patchSearchFunctionsResult = false;

            try
            {
                using (var statement = connection.PrepareStatement(tokenizerCheckQuery))
                {
                    if (statement.MoveNext())
                    {
                        tokenizerName = statement.Current?.GetString(0) ?? "unknown";
                    }
                }

                Plugin.Instance.logger.Info("EnhanceChineseSearch - Current tokenizer is " + tokenizerName);

                if (!string.Equals(tokenizerName, "unknown", StringComparison.Ordinal))
                {
                    if (Plugin.Instance.GetPluginOptions().ModOptions.EnhanceChineseSearchRestore)
                    {
                        if (string.Equals(tokenizerName, "simple", StringComparison.Ordinal))
                        {
                            rebuildFtsResult = RebuildFts(connection, ftsTableName, "unicode61 remove_diacritics 2");
                        }

                        if (rebuildFtsResult)
                        {
                            Plugin.Instance.logger.Info("EnhanceChineseSearch - Restore Success");
                            ResetOptions();
                        }
                    }
                    else if (Plugin.Instance.GetPluginOptions().ModOptions.EnhanceChineseSearch)
                    {
                        patchSearchFunctionsResult = PatchSearchFunctions();

                        if (patchSearchFunctionsResult)
                        {
                            if (string.Equals(tokenizerName, "unicode61 remove_diacritics 2", StringComparison.Ordinal))
                            {
                                rebuildFtsResult = RebuildFts(connection, ftsTableName, "simple");
                            }

                            if (rebuildFtsResult)
                            {
                                Plugin.Instance.logger.Info("EnhanceChineseSearch - Load Success");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("EnhanceChineseSearch - PatchPhase2 Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
            }

            if (!patchSearchFunctionsResult || !rebuildFtsResult ||
                string.Equals(tokenizerName, "unknown", StringComparison.Ordinal))
            {
                ResetOptions();
            }
        }

        private static bool RebuildFts(IDatabaseConnection connection, string ftsTableName, string tokenizerName)
        {
            string populateQuery;

            if (Plugin.Instance.ApplicationHost.ApplicationVersion < new Version("4.9.0.0"))
            {
                populateQuery =
                    $"insert into {ftsTableName}(RowId, Name, OriginalTitle, SeriesName, Album) select id, " +
                    GetSearchColumnNormalization("Name") + ", " +
                    GetSearchColumnNormalization("OriginalTitle") + ", " +
                    GetSearchColumnNormalization("SeriesName") + ", " +
                    GetSearchColumnNormalization("Album") +
                    " from MediaItems";
            }
            else
            {
                populateQuery =
                    $"insert into {ftsTableName}(RowId, Name, OriginalTitle, SeriesName, Album) select id, " +
                    GetSearchColumnNormalization("Name") + ", " +
                    GetSearchColumnNormalization("OriginalTitle") + ", " +
                    GetSearchColumnNormalization("SeriesName") + ", " +
                    GetSearchColumnNormalization(
                        "(select case when AlbumId is null then null else (select name from MediaItems where Id = AlbumId limit 1) end)") +
                    " from MediaItems";
            }

            connection.BeginTransaction(TransactionMode.Deferred);
            try
            {
                var dropFtsTableQuery = $"DROP TABLE IF EXISTS {ftsTableName}";
                connection.Execute(dropFtsTableQuery);

                var createFtsTableQuery =
                    $"CREATE VIRTUAL TABLE IF NOT EXISTS {ftsTableName} USING FTS5 (Name, OriginalTitle, SeriesName, Album, tokenize=\"{tokenizerName}\", prefix='1 2 3 4')";
                connection.Execute(createFtsTableQuery);

                Plugin.Instance.logger.Info($"EnhanceChineseSearch - Filling {ftsTableName} Start");

                connection.Execute(populateQuery);
                connection.CommitTransaction();

                Plugin.Instance.logger.Info($"EnhanceChineseSearch - Filling {ftsTableName} Complete");

                return true;
            }
            catch (Exception e)
            {
                connection.RollbackTransaction();
                Plugin.Instance.logger.Warn("EnhanceChineseSearch - RebuildFts Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
            }

            return false;
        }

        private static bool EnsureTokenizerExists()
        {
            var resourceName = GetTokenizerResourceName();
            var expectedSha1 = GetExpectedSha1();

            if (resourceName == null || expectedSha1 == null) return false;

            try
            {
                if (File.Exists(_tokenizerPath))
                {
                    var existingSha1 = ComputeSha1(_tokenizerPath);
                    Plugin.Instance.logger.Debug(existingSha1 == expectedSha1
                        ? "Tokenizer exists with matching SHA-1."
                        : "Tokenizer exists but SHA-1 does not match.");

                    return true;
                }

                Plugin.Instance.logger.Debug("Tokenizer does not exist. Exporting...");
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    using (var fileStream = new FileStream(_tokenizerPath, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(fileStream);
                    }
                }

                Plugin.Instance.logger.Info($"Exported {resourceName} to {_tokenizerPath}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("EnhanceChineseSearch - EnsureTokenizerExists Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
            }

            return false;
        }

        private static string GetTokenizerResourceName()
        {
            var tokenizerNamespace = Assembly.GetExecutingAssembly().GetName().Name + ".Tokenizer";
            var winSimpleTokenizer = $"{tokenizerNamespace}.win.libsimple.so";
            var linuxSimpleTokenizer = $"{tokenizerNamespace}.linux.libsimple.so";

            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT when Environment.Is64BitOperatingSystem:
                    return winSimpleTokenizer;
                case PlatformID.Unix when Environment.Is64BitOperatingSystem:
                    return linuxSimpleTokenizer;
                default:
                    return null;
            }
        }

        private static string GetExpectedSha1()
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    return "a83d90af9fb88e75a1ddf2436c8b67954c761c83";
                case PlatformID.Unix:
                    return "f7fb8ba0b98e358dfaa87570dc3426ee7f00e1b6";
                default:
                    return null;
            }
        }

        private static string ComputeSha1(string filePath)
        {
            using (var sha1 = SHA1.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = sha1.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
        
        private static void ResetOptions()
        {
            Plugin.Instance.GetPluginOptions().ModOptions.EnhanceChineseSearch = false;
            Plugin.Instance.GetPluginOptions().ModOptions.EnhanceChineseSearchRestore = false;
            Plugin.Instance.SavePluginOptionsSuppress();
        }

        private static bool PatchSearchFunctions()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (!IsPatched(_getJoinCommandText, typeof(EnhanceChineseSearch)))
                    {
                        HarmonyMod.Patch(_getJoinCommandText,
                            postfix: new HarmonyMethod(typeof(EnhanceChineseSearch).GetMethod(
                                "GetJoinCommandTextPostfix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug("Patch GetJoinCommandText Success by Harmony");
                    }
                    if (!IsPatched(_createSearchTerm, typeof(EnhanceChineseSearch)))
                    {
                        HarmonyMod.Patch(_createSearchTerm,
                            prefix: new HarmonyMethod(typeof(EnhanceChineseSearch).GetMethod(
                                "CreateSearchTermPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug("Patch CreateSearchTerm Success by Harmony");
                    }
                    if (!IsPatched(_cacheIdsFromTextParams, typeof(EnhanceChineseSearch)))
                    {
                        HarmonyMod.Patch(_cacheIdsFromTextParams,
                            prefix: new HarmonyMethod(typeof(EnhanceChineseSearch).GetMethod("CacheIdsFromTextParamsPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug("Patch CacheIdsFromTextParams Success by Harmony");
                    }
                    return true;
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch SearchFunctions Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                    PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                }
            }

            return false;
        }

        private static bool LoadTokenizerExtension(IDatabaseConnection connection)
        {
            try
            {
                connection.Execute("SELECT load_extension('" + _tokenizerPath + "')");

                return true;
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("EnhanceChineseSearch - Load tokenizer failed.");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
            }
            
            return false;
        }

        [HarmonyPostfix]
        private static void CreateConnectionPostfix(bool isReadOnly, ref IDatabaseConnection __result)
        {
            var db = sqlite3_db.GetValue(__result);
            sqlite3_enable_load_extension.Invoke(raw, new[] { db, 1 });
            var tokenizerLoaded = LoadTokenizerExtension(__result);

            if (!isReadOnly && !_patchPhase2Initialized && tokenizerLoaded)
            {
                PatchPhase2(__result);
                _patchPhase2Initialized = true;
            }
        }
        
        public static void UpdateSearchScope()
        {
            var searchScope = Plugin.Instance.GetPluginOptions().ModOptions.SearchScope?
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

            var includeItemTypes = new List<string>();
            
            foreach (var scope in searchScope)
            {
                if (Enum.TryParse(scope, true, out SearchItemType type))
                {
                    switch (type)
                    {
                        case SearchItemType.Book:
                            includeItemTypes.AddRange(new[] { "Book" });
                            break;
                        case SearchItemType.Collection:
                            includeItemTypes.AddRange(new[] { "BoxSet" });
                            break;
                        case SearchItemType.Episode:
                            includeItemTypes.AddRange(new[] { "Episode" });
                            break;
                        case SearchItemType.Game:
                            includeItemTypes.AddRange(new[] { "Game", "GameSystem" });
                            break;
                        case SearchItemType.Genre:
                            includeItemTypes.AddRange(new[] { "MusicGenre", "GameGenre", "Genre" });
                            break;
                        case SearchItemType.LiveTv:
                            includeItemTypes.AddRange(new[] { "LiveTvChannel", "LiveTvProgram", "LiveTvSeries" });
                            break;
                        case SearchItemType.Movie:
                            includeItemTypes.AddRange(new[] { "Movie" });
                            break;
                        case SearchItemType.Music:
                            includeItemTypes.AddRange(new[] { "Audio","MusicVideo" });
                            break;
                        case SearchItemType.MusicAlbum:
                            includeItemTypes.AddRange(new[] { "MusicAlbum" });
                            break;
                        case SearchItemType.Person:
                            includeItemTypes.AddRange(new[] { "MusicArtist", "Person" });
                            break;
                        case SearchItemType.Photo:
                            includeItemTypes.AddRange(new[] { "Photo" });
                            break;
                        case SearchItemType.PhotoAlbum:
                            includeItemTypes.AddRange(new[] { "PhotoAlbum" });
                            break;
                        case SearchItemType.Playlist:
                            includeItemTypes.AddRange(new[] { "Playlist" });
                            break;
                        case SearchItemType.Series:
                            includeItemTypes.AddRange(new[] { "Series" });
                            break;
                        case SearchItemType.Studio:
                            includeItemTypes.AddRange(new[] { "Studio" });
                            break;
                        case SearchItemType.Tag:
                            includeItemTypes.AddRange(new[] { "Tag" });
                            break;
                        case SearchItemType.Trailer:
                            includeItemTypes.AddRange(new[] { "Trailer" });
                            break;
                    }
                }
            }

            _includeItemTypes = includeItemTypes.ToArray();
        }

        private static string GetSearchColumnNormalization(string columnName)
        {
            return "replace(replace(" + columnName + ",'''',''),'.','')";
        }

        private static bool EnableJoinFtsSearch(InternalItemsQuery query)
        {
            var result = (query.Limit == 30 || query.Limit == 50) &&
                         (query.PersonTypes == null || query.PersonTypes.Length == 0) &&
                         !string.IsNullOrEmpty(query.SearchTerm);

            return result;
        }

        [HarmonyPostfix]
        private static void GetJoinCommandTextPostfix(InternalItemsQuery query,
            List<KeyValuePair<string, string>> bindParams,
            string mediaItemsTableQualifier, ref string __result)
        {
            if (EnableJoinFtsSearch(query))
            {
                if (__result.Contains("match @SearchTerm"))
                {
                    __result = __result.Replace("match @SearchTerm", "match simple_query(@SearchTerm)");
                }
            }
        }

        [HarmonyPrefix]
        private static bool CreateSearchTermPrefix(string searchTerm, ref string __result)
        {
            var newSearchTerm = searchTerm;
            if (Regex.Matches(searchTerm, "'").Count >= 3)
            {
                newSearchTerm = searchTerm.Replace("'", "");
            }

            __result = newSearchTerm;
            return false;
        }
        
        [HarmonyPrefix]
        private static bool CacheIdsFromTextParamsPrefix(InternalItemsQuery query, IDatabaseConnection db)
        {
            if ((query.Limit == 30 || query.Limit == 50) &&
                (query.PersonTypes == null || query.PersonTypes.Length == 0))
            {
                if (!string.IsNullOrEmpty(query.NameStartsWith))
                {
                    query.SearchTerm = query.NameStartsWith;
                    query.NameStartsWith = null;
                }

                if (query.IncludeItemTypes.Length == 0 && !string.IsNullOrEmpty(query.SearchTerm))
                {
                    query.IncludeItemTypes = _includeItemTypes;
                }
            }

            return true;
        }
    }
}
