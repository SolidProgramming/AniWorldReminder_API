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

        public async Task<UserModel?> GetUserAsync(string telegramChatId)
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
    }
}
