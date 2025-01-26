using Dapper;
using MySql.Data.MySqlClient;

namespace AniWorldReminder_API.Services
{
    public class DBService(ILogger<DBService> logger) : IDBService
    {
        private string? DBConnectionString;

        public async Task<bool> InitAsync()
        {
            DatabaseSettingsModel? settings = SettingsHelper.ReadSettings<DatabaseSettingsModel>() ?? throw new Exception("");
            DBConnectionString = $"server={settings.Ip};port=3306;database={settings.Database};user={settings.Username};password={settings.Password};";

            if (!await TestDBConnectionAsync())
                return false;

            logger.LogInformation($"{DateTime.Now} | DB Service initialized");

            return true;
        }

        private async Task<bool> TestDBConnectionAsync()
        {
            try
            {
                using (MySqlConnection connection = new(DBConnectionString))
                {
                    await connection.OpenAsync();
                }

                logger.LogInformation($"{DateTime.Now} | Database reachablility ensured");

                return true;
            }
            catch (MySqlException ex)
            {
                logger.LogError($"{DateTime.Now} | DB connection could not be established. Error: " + ex.ToString());
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError($"{DateTime.Now} | {ex}");
                return false;
            }
        }

        public async Task<UserModel?> GetUserByTelegramIdAsync(string telegramChatId)
        {
            using MySqlConnection connection = new(DBConnectionString);

            Dictionary<string, object> dictionary = new()
            {
                { "@TelegramChatId", telegramChatId }
            };

            DynamicParameters parameters = new(dictionary);

            string query = "SELECT * FROM users WHERE TelegramChatId = @TelegramChatId";

            return await connection.QueryFirstOrDefaultAsync<UserModel>(query, parameters);
        }
        public async Task<UserModel?> GetUserByUsernameAsync(string username)
        {
            using MySqlConnection connection = new(DBConnectionString);

            Dictionary<string, object> dictionary = new()
            {
                { "@Username", username }
            };

            DynamicParameters parameters = new(dictionary);

            string query = "SELECT * FROM users WHERE Username = @Username";

            return await connection.QueryFirstOrDefaultAsync<UserModel>(query, parameters);
        }
        public async Task DeleteVerifyTokenAsync(string telegramChatId)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "UPDATE users " +
                           "SET users.VerifyToken = @VerifyToken " +
                           "WHERE users.TelegramChatId = @TelegramChatId";

            Dictionary<string, object> dictionary = new()
            {
                { "@VerifyToken", null },
                { "@TelegramChatId", telegramChatId }
            };

            DynamicParameters parameters = new(dictionary);

            await connection.ExecuteAsync(query, parameters);
        }
        public async Task UpdateVerificationStatusAsync(string telegramChatId, VerificationStatus verificationStatus)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "UPDATE users " +
                           "SET users.Verified = @Verified " +
                           "WHERE users.TelegramChatId = @TelegramChatId";

            Dictionary<string, object> dictionary = new()
            {
                { "@Verified", (int)verificationStatus },
                { "@TelegramChatId", telegramChatId }
            };

            DynamicParameters parameters = new(dictionary);

            await connection.ExecuteAsync(query, parameters);
        }
        public async Task SetVerifyStatusAsync(UserModel user)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "UPDATE users " +
                           "SET users.Verified = @Verified " +
                           ", users.Username = @Username " +
                           ", users.Password = @Password " +
                           "WHERE users.TelegramChatId = @TelegramChatId";

            Dictionary<string, object> dictionary = new()
            {
                { "@Verified", (int)VerificationStatus.Verified },
                { "@TelegramChatId", user.TelegramChatId! },
                { "@Username", user.Username! },
                { "@Password", user.Password! }
            };

            DynamicParameters parameters = new(dictionary);

            await connection.ExecuteAsync(query, parameters);
        }
        public async Task<UserModel?> GetAuthUserAsync(string username)
        {
            using MySqlConnection connection = new(DBConnectionString);

            Dictionary<string, object> dictionary = new()
            {
                { "@Username", username },
            };

            DynamicParameters parameters = new(dictionary);

            string query = "SELECT * FROM users WHERE users.Username = @Username";

            return await connection.QuerySingleOrDefaultAsync<UserModel>(query, parameters);
        }
        public async Task<UserModel?> GetAuthUserByIdAsync(string userId)
        {
            using MySqlConnection connection = new(DBConnectionString);

            Dictionary<string, object> dictionary = new()
            {
                { "@UserId", userId },
            };

            DynamicParameters parameters = new(dictionary);

            string query = "SELECT * FROM users WHERE users.id = @UserId";

            return await connection.QuerySingleOrDefaultAsync<UserModel>(query, parameters);
        }
        public async Task<SeriesModel?> GetSeriesAsync(string seriesPath)
        {
            using MySqlConnection connection = new(DBConnectionString);

            Dictionary<string, object> dictionary = new()
            {
                { "@SeriesPath", seriesPath }
            };

            DynamicParameters parameters = new(dictionary);

            string query = "SELECT * FROM series WHERE series.Path = @SeriesPath";

            return await connection.QueryFirstOrDefaultAsync<SeriesModel>(query, parameters);
        }
        public async Task InsertSeries(string SeriesPath, IStreamingPortalService streamingPortalService)
        {
            SeriesInfoModel? seriesInfo = await streamingPortalService.GetMediaInfoAsync(SeriesPath);

            if (seriesInfo is null)
                return;

            int seriesId = await InsertSeriesAsync(seriesInfo, streamingPortalService.StreamingPortal);

            if (seriesId == -1)
                return;

            foreach (SeasonModel season in seriesInfo.Seasons)
            {
                await InsertEpisodesAsync(seriesId, season.Episodes);
            }
        }
        private async Task<int> InsertSeriesAsync(SeriesInfoModel seriesInfo, StreamingPortal streamingPortal)
        {
            if (string.IsNullOrEmpty(seriesInfo.Name))
                return -1;

            using MySqlConnection connection = new(DBConnectionString);

            string streamingPortalIdQuery = "SELECT id FROM streamingportals WHERE streamingportals.Name = @Name";

            string? streamingPortalName = StreamingPortalHelper.GetStreamingPortalName(streamingPortal);

            if (string.IsNullOrEmpty(streamingPortalName))
                return -1;

            Dictionary<string, object> dictionaryPortalId = new()
            {
                { "@Name", streamingPortalName },
            };

            DynamicParameters parametersPortalId = new(dictionaryPortalId);

            int streamingPortalId = await connection.QueryFirstOrDefaultAsync<int>(streamingPortalIdQuery, parametersPortalId);

            if (streamingPortalId < 1)
                return -1;

            string query = "INSERT INTO series (StreamingPortalId, Name, SeasonCount, EpisodeCount, Path, CoverArtUrl) VALUES (@StreamingPortalId, @Name, @SeasonCount, @EpisodeCount, @SeriesPath, @CoverArtUrl); " +
                "select LAST_INSERT_ID()";

            Dictionary<string, object> dictionary = new()
            {
                { "@StreamingPortalId", streamingPortalId },
                { "@Name", seriesInfo.Name },
                { "@SeasonCount", seriesInfo.SeasonCount },
                { "@EpisodeCount", seriesInfo.Seasons.Last().EpisodeCount },
                { "@SeriesPath", seriesInfo.Path },
                { "@CoverArtUrl", seriesInfo.CoverArtUrl }
            };

            DynamicParameters parameters = new(dictionary);

            return await connection.ExecuteScalarAsync<int>(query, parameters);
        }
        private async Task InsertEpisodesAsync(int seriesId, List<EpisodeModel> episodes)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "INSERT INTO episodes (SeriesId, Season, Episode, Name, LanguageFlag) VALUES (@SeriesId, @Season, @Episode, @Name, @LanguageFlag)";

            Dictionary<string, object> dictionary;

            foreach (EpisodeModel episode in episodes)
            {
                if (string.IsNullOrEmpty(episode.Name))
                    continue;

                dictionary = new()
                {
                    { "@SeriesId",  seriesId},
                    { "@Season",  episode.Season},
                    { "@Episode",  episode.Episode},
                    { "@Name",  episode.Name},
                    { "LanguageFlag", episode.Languages }
                };

                DynamicParameters parameters = new(dictionary);

                await connection.ExecuteAsync(query, parameters);
            }
        }
        public async Task<UsersSeriesModel?> GetUsersSeriesAsync(string userId, string seriesPath)
        {
            using MySqlConnection connection = new(DBConnectionString);

            Dictionary<string, object> dictionary = new()
            {
                { "@UserId", userId },
                { "@SeriesPath", $"/{seriesPath.TrimStart('/')}" }
            };

            DynamicParameters parameters = new(dictionary);

            string query = "SELECT users.*, series.*, users_series.* FROM users " +
                           "JOIN users_series ON users.id = users_series.UserId " +
                           "JOIN series ON users_series.SeriesId = series.id " +
                           "WHERE UserId = @UserId AND series.Path = @SeriesPath";

            IEnumerable<UsersSeriesModel> users_series =
                await connection.QueryAsync<UserModel, SeriesModel, UsersSeriesModel, UsersSeriesModel>
                (query, (users, series, users_series) =>
                {
                    return new UsersSeriesModel()
                    {
                        Id = users_series.Id,
                        Users = users,
                        Series = series,
                        LanguageFlag = users_series.LanguageFlag
                    };
                }, parameters);

            return users_series.FirstOrDefault();
        }
        public async Task InsertUsersSeriesAsync(UsersSeriesModel usersSeries)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "INSERT INTO users_series (UserId, SeriesId, LanguageFlag) VALUES (@UserId, @SeriesId, @LanguageFlag)";

            if (usersSeries.Users is null || usersSeries.Series is null)
                return;

            Dictionary<string, object> dictionary = new()
            {
                { "@UserId", usersSeries.Users.Id },
                { "@SeriesId", usersSeries.Series.Id },
                { "@LanguageFlag", usersSeries.LanguageFlag },
            };

            DynamicParameters parameters = new(dictionary);

            await connection.ExecuteAsync(query, parameters);
        }
        public async Task DeleteUsersSeriesAsync(UsersSeriesModel usersSeries)
        {
            using MySqlConnection connection = new(DBConnectionString);

            Dictionary<string, object> dictionary = new()
            {
                { "@id", usersSeries.Id }
            };

            DynamicParameters parameters = new(dictionary);

            string query = "DELETE FROM users_series WHERE id = @id";

            await connection.ExecuteAsync(query, parameters);
        }
        public async Task<List<UsersSeriesModel>?> GetUsersSeriesAsync(string userId)
        {
            using MySqlConnection connection = new(DBConnectionString);

            Dictionary<string, object> dictionary = new()
            {
                { "@UserId", userId }
            };

            DynamicParameters parameters = new(dictionary);

            string query = "SELECT * FROM users_series " +
                           "JOIN users ON users_series.UserId = users.id " +
                           "JOIN series ON users_series.SeriesId = series.id " +
                           "JOIN streamingportals ON series.StreamingPortalId = streamingportals.id " +
                           "WHERE UserId = @UserId";

            IEnumerable<UsersSeriesModel> users_series =
                await connection.QueryAsync<UsersSeriesModel, UserModel, SeriesModel, StreamingPortalModel, UsersSeriesModel>
                (query, (users_series, users, series, streamingportals) =>
                {
                    series.StreamingPortal = streamingportals.Name!.ToStreamingPortal();
                    series.Added = users_series.Added;

                    return new UsersSeriesModel()
                    {
                        Id = users_series.Id,
                        Series = series,
                        Users = users
                    };
                }, parameters);

            return users_series.ToList();
        }
        public async Task<UserWebsiteSettings?> GetUserWebsiteSettings(string userId)
        {
            using MySqlConnection connection = new(DBConnectionString);

            Dictionary<string, object> dictionary = new()
            {
                { "@UserId", userId }
            };

            DynamicParameters parameters = new(dictionary);

            string query = "SELECT users_settings.* FROM users_settings " +
                "LEFT JOIN users ON users_settings.UserId = users.id " +
                "WHERE users.id = @UserId";

            return await connection.QuerySingleOrDefaultAsync<UserWebsiteSettings>(query, parameters);
        }
        public async Task UpdateUserWebsiteSettings(string userId, UserWebsiteSettings userWebsiteSettings)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "UPDATE users_settings " +
                           "SET users_settings.TelegramDisableNotifications = @TelegramDisableNotifications, " +
                           "users_settings.TelegramNoCoverArtNotifications = @TelegramNoCoverArtNotifications " +
                           "WHERE users_settings.UserId = @UserId";

            Dictionary<string, object> dictionary = new()
            {
                { "@TelegramDisableNotifications", userWebsiteSettings.TelegramDisableNotifications },
                { "@TelegramNoCoverArtNotifications", userWebsiteSettings.TelegramNoCoverArtNotifications },
                { "@UserId", userId }
            };

            DynamicParameters parameters = new(dictionary);

            await connection.ExecuteAsync(query, parameters);
        }
        public async Task CreateUserWebsiteSettings(string userId)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "INSERT INTO users_settings (UserId) VALUES (@UserId)";

            Dictionary<string, object> dictionary = new()
            {
                { "@UserId", userId }
            };

            DynamicParameters parameters = new(dictionary);

            await connection.ExecuteAsync(query, parameters);
        }
        public async Task<IEnumerable<EpisodeDownloadModel>?> GetDownloads(string apiKey)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "SELECT download.*, series.*, streamingportals.*, users_series.* FROM download " +
                           "JOIN series ON download.SeriesId = series.id " +
                           "JOIN streamingportals ON series.StreamingPortalId = streamingportals.id " +
                           "JOIN users ON download.UsersId = users.id " +
                           "JOIN users_series ON series.id = users_series.SeriesId " +
                           "WHERE users.APIKey = @APIKey " +
                           "AND users.id = users_series.UserId " +
                           "ORDER BY download.id";

            Dictionary<string, object> dictionary = new()
            {
                { "@APIKey", apiKey }
            };

            DynamicParameters parameters = new(dictionary);

            return await connection.QueryAsync<DownloadModel, StreamingPortalModel, UsersSeriesModel, EpisodeDownloadModel>
               (query, (download, streamingportal, usersSeries) =>
               {
                   download.LanguageFlag = usersSeries.LanguageFlag;
                   return new EpisodeDownloadModel()
                   {
                       Download = download,
                       StreamingPortal = streamingportal
                   };
               }, parameters);
        }
        public async Task RemoveFinishedDownload(string apiKey, EpisodeDownloadModel episode)
        {
            string? userId = await GetUserIdByAPIKey(apiKey);

            if (string.IsNullOrEmpty(userId))
                return;

            using MySqlConnection connection = new(DBConnectionString);

            string query = "DELETE FROM download " +
                "WHERE download.UsersId = @UsersId AND download.SeriesId = @SeriesId AND download.Season = @Season AND download.Episode = @Episode";

            Dictionary<string, object> dictionary = new()
            {
                { "@UsersId", userId },
                { "@SeriesId", episode.Download.SeriesId },
                { "@Season", episode.Download.Season },
                { "@Episode", episode.Download.Episode }
            };

            DynamicParameters parameters = new(dictionary);

            await connection.ExecuteAsync(query, parameters);
        }
        public async Task<int> InsertDownloadAsync(string usersId, string seriesId, List<EpisodeModel> episodes)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string selectQuery = "SELECT EXISTS(" +
                   "SELECT * FROM download " +
                   "WHERE SeriesId = @SeriesId AND UsersId = @UsersId AND Season = @Season AND Episode = @Episode AND LanguageFlag = @LanguageFlag)";

            string query = "INSERT INTO download (SeriesId, UsersId ,Season, Episode, LanguageFlag) VALUES (@SeriesId, @UsersId , @Season, @Episode, @LanguageFlag)";

            Dictionary<string, object> dictionary;

            int rowsAdded = 0;

            foreach (EpisodeModel episode in episodes)
            {
                dictionary = new()
                {
                    { "@SeriesId",  seriesId},
                    { "@UsersId",  usersId},
                    { "@Season",  episode.Season},
                    { "@Episode",  episode.Episode},
                    { "@LanguageFlag",  episode.Languages}
                };

                int rows = await connection.ExecuteScalarAsync<int>(selectQuery, dictionary);

                if (rows > 0)
                    continue;

                DynamicParameters parameters = new(dictionary);

                rowsAdded += await connection.ExecuteAsync(query, parameters);
            }

            return rowsAdded;
        }
        public async Task<string?> GetUserAPIKey(string userId)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "SELECT users.APIKey FROM users WHERE users.id = @UserId";

            Dictionary<string, object> dictionary = new()
            {
                { "@UserId", userId }
            };

            DynamicParameters parameters = new(dictionary);

            return await connection.QuerySingleOrDefaultAsync<string?>(query, parameters);
        }
        public async Task UpdateUserAPIKey(string userId, string apiKey)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "UPDATE users " +
                           "SET users.APIKey = @APIKey " +
                           "WHERE users.id = @UserId";

            Dictionary<string, object> dictionary = new()
            {
                { "@APIKey", apiKey },
                { "@UserId", userId }
            };

            DynamicParameters parameters = new(dictionary);

            await connection.ExecuteAsync(query, parameters);
        }
        public async Task<string?> GetUserIdByAPIKey(string apiKey)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "SELECT users.id FROM users WHERE users.APIKey = @APIKey";

            Dictionary<string, object> dictionary = new()
            {
                { "@APIKey", apiKey }
            };

            DynamicParameters parameters = new(dictionary);

            return await connection.QuerySingleOrDefaultAsync<string?>(query, parameters);
        }
        public async Task<int> GetDownloadsCount(string apiKey)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "SELECT COUNT(1) FROM download " +
                "JOIN users ON download.UsersId = users.id " +
                "WHERE users.APIKey = @APIKey";

            Dictionary<string, object> dictionary = new()
            {
                { "@APIKey", apiKey }
            };

            DynamicParameters parameters = new(dictionary);

            return await connection.ExecuteScalarAsync<int>(query, dictionary);
        }
        public async Task<UserModel?> GetUserByAPIKey(string apiKey)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "SELECT users.* FROM users WHERE users.APIKey = @APIKey";

            Dictionary<string, object> dictionary = new()
            {
                { "@APIKey", apiKey }
            };

            DynamicParameters parameters = new(dictionary);

            return await connection.QuerySingleOrDefaultAsync<UserModel>(query, parameters);
        }
        public async Task SetDownloaderPreferences(string apiKey, DownloaderPreferencesModel downloaderPreferences)
        {
            UserModel? user = await GetUserByAPIKey(apiKey);

            if (user is null)
                return;

            using MySqlConnection connection = new(DBConnectionString);

            string selectQuery = "SELECT EXISTS(SELECT * FROM users_downloader_preferences WHERE users_downloader_preferences.UserId = @UserId)";

            Dictionary<string, object> dictionary = new()
            {
                { "@UserId", user.Id }
            };

            int rows = await connection.ExecuteScalarAsync<int>(selectQuery, dictionary);

            if (rows > 0)
            {
                await UpdateDownloaderPreferences(user, downloaderPreferences);
            }
            else
            {
                await InsertDownloaderPreferences(user, downloaderPreferences);
            }
        }
        private async Task UpdateDownloaderPreferences(UserModel user, DownloaderPreferencesModel downloaderPreferences)
        {
            using MySqlConnection connection = new(DBConnectionString);

            Dictionary<string, object> dictionary = new()
            {
                { "@UserId", user.Id },
                { "@Interval", downloaderPreferences.Interval },
                { "@AutoStart", downloaderPreferences.AutoStart },
                { "@TelegramCaptchaNotification", downloaderPreferences.TelegramCaptchaNotification },
                { "@UseProxy", downloaderPreferences.UseProxy },
                { "@ProxyUri", downloaderPreferences.ProxyUri },
                { "@ProxyUsername", downloaderPreferences.ProxyUsername },
                { "@ProxyPassword", downloaderPreferences.ProxyPassword },
            };

            string query = "UPDATE users_downloader_preferences " +
                "SET users_downloader_preferences.Interval = @Interval, " +
                "users_downloader_preferences.AutoStart = @AutoStart, " +
                "users_downloader_preferences.TelegramCaptchaNotification = @TelegramCaptchaNotification, " +
                "users_downloader_preferences.UseProxy = @UseProxy, " +
                "users_downloader_preferences.ProxyUri = @ProxyUri, " +
                "users_downloader_preferences.ProxyUsername = @ProxyUsername, " +
                "users_downloader_preferences.ProxyPassword = @ProxyPassword " +
                "WHERE users_downloader_preferences.UserId = @UserId";

            DynamicParameters parameters = new(dictionary);

            await connection.ExecuteAsync(query, parameters);
        }
        private async Task InsertDownloaderPreferences(UserModel user, DownloaderPreferencesModel downloaderPreferences)
        {
            using MySqlConnection connection = new(DBConnectionString);

            Dictionary<string, object> dictionary = new()
            {
                { "@UserId", user.Id },
                { "@Interval", downloaderPreferences.Interval },
                { "@AutoStart", downloaderPreferences.AutoStart },
                { "@TelegramCaptchaNotification", downloaderPreferences.TelegramCaptchaNotification },
                { "@UseProxy", downloaderPreferences.UseProxy },
                { "@ProxyUri", downloaderPreferences.ProxyUri },
                { "@ProxyUsername", downloaderPreferences?.ProxyUsername },
                { "@ProxyPassword", downloaderPreferences?.ProxyPassword },
            };

            string query = "INSERT INTO users_downloader_preferences " +
               "(UserId, users_downloader_preferences.Interval, AutoStart, TelegramCaptchaNotification, UseProxy, ProxyUri, ProxyUsername, ProxyPassword) " +
               "VALUES (@UserId, @Interval, @AutoStart, @TelegramCaptchaNotification, @UseProxy, @ProxyUri, @ProxyUsername, @ProxyPassword)";

            DynamicParameters parameters = new(dictionary);

            await connection.ExecuteAsync(query, parameters);
        }
        public async Task<DownloaderPreferencesModel?> GetDownloaderPreferences(string apiKey)
        {
            UserModel? user = await GetUserByAPIKey(apiKey);

            if (user is null)
                return default;

            using MySqlConnection connection = new(DBConnectionString);

            string selectQuery = "SELECT * FROM users_downloader_preferences WHERE users_downloader_preferences.UserId = @UserId";

            Dictionary<string, object> dictionary = new()
            {
                { "@UserId", user.Id }
            };

            DynamicParameters parameters = new(dictionary);

            DownloaderPreferencesModel? preferences = await connection.QuerySingleOrDefaultAsync<DownloaderPreferencesModel>(selectQuery, parameters);

            if (preferences is null)
            {
                preferences = new();
                await InsertDownloaderPreferences(user, preferences);
            }

            return preferences;
        }
        public async Task InsertMovieDownloadAsync(AddMovieDownloadRequestModel download)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string selectQuery = "SELECT EXISTS(" +
                   "SELECT * FROM movie_download " +
                   "WHERE DirectUrl = @DirectUrl AND UsersId = @UsersId)";

            string query = "INSERT INTO movie_download (DirectUrl, UsersId) VALUES (@DirectUrl, @UsersId)";

            Dictionary<string, object> dictionary = new()
            {
                { "@DirectUrl",  download.DirectUrl},
                { "@UsersId",  download.UserId},
            };

            int rows = await connection.ExecuteScalarAsync<int>(selectQuery, dictionary);

            if (rows > 0)
                return;

            DynamicParameters parameters = new(dictionary);

            await connection.ExecuteAsync(query, parameters);
        }
        public async Task<string?> CreateWatchlist(string watchlistName, string userId, List<SeriesModel> watchlist)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "INSERT INTO watchlists (Name, Ident) VALUES (@Name, @Ident);";

            string watchlistIdent = Guid.NewGuid().ToString();

            Dictionary<string, object> dictionary = new()
            {
                { "@Name", watchlistName },
                { "@Ident", watchlistIdent }
            };

            DynamicParameters parameters = new(dictionary);

            try
            {
                await connection.ExecuteAsync(query, parameters);
            }
            catch (Exception)
            {
                return default;
            }

            query = "SELECT id FROM watchlists WHERE Ident = @Ident;";

            string? watchlistId;

            try
            {
                watchlistId = await connection.ExecuteScalarAsync<string>(query, parameters);

                if (string.IsNullOrEmpty(watchlistId))
                    return default;
            }
            catch (Exception)
            {
                return default;
            }

            foreach (SeriesModel series in watchlist)
            {
                dictionary.Clear();

                dictionary.Add("@WatchlistId", watchlistId);
                dictionary.Add("@SeriesId", series.Id);

                parameters = new(dictionary);

                query = "INSERT INTO watchlists_series (WatchlistId, SeriesId) VALUES (@WatchlistId, @SeriesId);";

                await connection.ExecuteAsync(query, parameters);
            }

            query = "INSERT INTO users_watchlists (UserId, WatchlistId) VALUES (@UserId, @WatchlistId);";

            dictionary.Clear();

            dictionary.Add("@UserId", userId);
            dictionary.Add("@WatchlistId", watchlistId);

            parameters = new(dictionary);

            await connection.ExecuteAsync(query, parameters);

            return watchlistIdent;
        }

        public async Task<List<WatchlistModel>?> GetUserWatchlists(string userId)
        {
            using MySqlConnection connection = new(DBConnectionString);

            string query = "SELECT series.*, watchlists.* FROM users_watchlists " +
                "JOIN watchlists ON users_watchlists.WatchlistId = watchlists.id " +
                "JOIN watchlists_series ON users_watchlists.WatchlistId = watchlists_series.WatchlistId " +
                "JOIN series ON watchlists_series.SeriesId = series.id " +
                "JOIN users ON users_watchlists.UserId = users.id " +
                "WHERE users.id = @UserId";

            Dictionary<string, object> dictionary = new()
            {
                { "@UserId", userId }
            };

            DynamicParameters parameters = new(dictionary);

            List<WatchlistModel> watchlists = [];

            await connection.QueryAsync<SeriesModel, WatchlistModel, WatchlistModel>
              (query, (series, watchlist) =>
              {
                  watchlist.Series = series;
                  watchlists.Add(watchlist);

                  return watchlist;
              }, parameters);


            return watchlists;
        }
    }
}
