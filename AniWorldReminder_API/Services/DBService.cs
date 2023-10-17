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

        public async Task<bool> Init()
        {
            DatabaseSettingsModel? settings = SettingsHelper.ReadSettings<DatabaseSettingsModel>();

            if (settings is null)
            {
                throw new Exception("");
            }

            DBConnectionString = $"server={settings.Ip};port=3306;database={settings.Database};user={settings.Username};password={settings.Password};";

            if (!await TestDBConnection())
                return false;

            Logger.LogInformation($"{DateTime.Now} | DB Service initialized");

            return true;
        }

        private async Task<bool> TestDBConnection()
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
    }
}
