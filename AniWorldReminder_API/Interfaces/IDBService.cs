namespace AniWorldReminder_API.Interfaces
{
    public interface IDBService
    {
        Task<bool> InitAsync();
        Task<UserModel?> GetUserByTelegramIdAsync(string telegramChatId);
        Task<UserModel?> GetUserByUsernameAsync(string username);
        Task DeleteVerifyTokenAsync(string telegramChatId);
        Task UpdateVerificationStatusAsync(string telegramChatId, VerificationStatus verificationStatus);
        Task SetVerifyStatusAsync(UserModel user);
        Task<UserModel?> GetAuthUserAsync(string username);
        Task<SeriesModel?> GetSeriesAsync(string seriesPath);
        Task InsertSeries(string SeriesPath, IStreamingPortalService streamingPortalService);
        Task<UsersSeriesModel?> GetUsersSeriesAsync(string userId, string seriesPath);
        Task InsertUsersSeriesAsync(UsersSeriesModel usersSeries);
        Task DeleteUsersSeriesAsync(UsersSeriesModel usersSeries);
        Task<List<UsersSeriesModel>?> GetUsersSeriesAsync(string userId);
        Task<UserWebsiteSettings?> GetUserWebsiteSettings(string userId);
        Task UpdateUserWebsiteSettings(UserWebsiteSettings userWebsiteSettings);
        Task CreateUserWebsiteSettings(string userId);
        Task<IEnumerable<EpisodeDownloadModel>?> GetDownloads(string apiKey);
        Task RemoveFinishedDownload(string apiKey, EpisodeDownloadModel episode);
        Task<int> InsertDownloadAsync(string usersId, string seriesId, List<EpisodeModel> episodes);
        Task<string?> GetUserAPIKey(string userId);
        Task UpdateUserAPIKey(string userId, string apiKey);
        Task<string?> GetUserIdByAPIKey(string apiKey);
        Task<int> GetDownloadsCount(string apiKey);
        Task<UserModel?> GetUserByAPIKey(string apiKey);
        Task SetDownloaderPreferences(string apiKey, DownloaderPreferencesModel downloaderPreferences);
    }
}
