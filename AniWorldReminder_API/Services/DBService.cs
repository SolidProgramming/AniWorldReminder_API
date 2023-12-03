using AniWorldReminder_API.Enums;
using Dapper;
using MySql.Data.MySqlClient;

namespace AniWorldReminder_API.Services
{
    public class DBService : IDBService
    {
        private readonly ILogger<DBService> Logger;
        private string? DBConnectionString;

        public DBService(ILogger<DBService> logger)
        {
            Logger = logger;
        }
        public async Task<bool> InitAsync()
        {
            DatabaseSettingsModel? settings = SettingsHelper.ReadSettings<DatabaseSettingsModel>() ?? throw new Exception("");
            DBConnectionString = $"server={settings.Ip};port=3306;database={settings.Database};user={settings.Username};password={settings.Password};";

            if (!await TestDBConnectionAsync())
                return false;

            Logger.LogInformation($"{DateTime.Now} | DB Service initialized");

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

                Logger.LogInformation($"{DateTime.Now} | Database reachablility ensured");

                return true;
            }
            catch (MySqlException ex)
            {
                Logger.LogError($"{DateTime.Now} | DB connection could not be established. Error: " + ex.ToString());
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"{DateTime.Now} | {ex}");
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
        }        public async Task<UserModel?> GetUserByUsernameAsync(string username)
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

            string query = "SELECT * FROM users WHERE Username = @Username";

            return await connection.QuerySingleOrDefaultAsync<UserModel>(query, parameters);
        }
        public async Task<SeriesModel?> GetSeriesAsync(string seriesName)
        {
            using MySqlConnection connection = new(DBConnectionString);

            Dictionary<string, object> dictionary = new()
            {
                { "@Name", seriesName }
            };

            DynamicParameters parameters = new(dictionary);

            string query = "SELECT * FROM series WHERE series.Name = @Name";

            return await connection.QueryFirstOrDefaultAsync<SeriesModel>(query, parameters);
        }
        public async Task InsertSeries(string seriesName, IStreamingPortalService streamingPortalService)
        { 
            SeriesInfoModel? seriesInfo = await streamingPortalService.GetSeriesInfoAsync(seriesName);

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

            string query = "INSERT INTO series (StreamingPortalId, Name, SeasonCount, EpisodeCount, CoverArtUrl) VALUES (@StreamingPortalId, @Name, @SeasonCount, @EpisodeCount, @CoverArtUrl); " +
                "select LAST_INSERT_ID()";

            Dictionary<string, object> dictionary = new()
            {
                { "@StreamingPortalId", streamingPortalId },
                { "@Name", seriesInfo.Name },
                { "@SeasonCount", seriesInfo.SeasonCount },
                { "@EpisodeCount", seriesInfo.Seasons.Last().EpisodeCount },
                { "@CoverArtUrl", seriesInfo.CoverArtUrl },
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
        public async Task<UsersSeriesModel?> GetUsersSeriesAsync(string username, string seriesName)
        {
            using MySqlConnection connection = new(DBConnectionString);

            Dictionary<string, object> dictionary = new()
            {
                { "@Username", username },
                { "@seriesName", seriesName }
            };

            DynamicParameters parameters = new(dictionary);

            string query = "SELECT users.*, series.*, users_series.* FROM users " +
                           "JOIN users_series ON users.id = users_series.UserId " +
                           "JOIN series ON users_series.SeriesId = series.id " +
                           "WHERE Username = @Username AND series.Name = @seriesName";

            IEnumerable<UsersSeriesModel> users_series =
                await connection.QueryAsync<UserModel, SeriesModel, UsersSeriesModel, UsersSeriesModel>
                (query, (users, series, users_series) =>
                {                   
                    return new UsersSeriesModel()
                    {
                        Id = users_series.Id,
                        Users = users,
                        Series = series
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
        public async Task<List<UsersSeriesModel>?> GetUsersSeriesAsync(string username)
        {
            using MySqlConnection connection = new(DBConnectionString);

            Dictionary<string, object> dictionary = new()
            {
                { "@Username", username }
            };

            DynamicParameters parameters = new(dictionary);

            string query = "SELECT users.*, series.*, users_series.* FROM users " +
                           "JOIN users_series ON users.id = users_series.UserId " +
                           "JOIN series ON users_series.SeriesId = series.id " +
                           "WHERE Username = @Username";

            IEnumerable<UsersSeriesModel> users_series =
                await connection.QueryAsync<UserModel, SeriesModel, UsersSeriesModel, UsersSeriesModel>
                (query, (users, series, users_series) =>
                {
                    return new UsersSeriesModel()
                    {
                        Id = users_series.Id,
                        Users = users,
                        Series = series
                    };
                }, parameters);

            return users_series.ToList();
        }
    }
}
