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
        Task<SeriesModel?> GetSeriesAsync(string seriesName);
        Task InsertSeries(string seriesName, IStreamingPortalService streamingPortalService);
        Task<UsersSeriesModel?> GetUsersSeriesAsync(string userId, string seriesName);
        Task InsertUsersSeriesAsync(UsersSeriesModel usersSeries);
        Task DeleteUsersSeriesAsync(UsersSeriesModel usersSeries);
        Task<List<UsersSeriesModel>?> GetUsersSeriesAsync(string userId);
        Task<UserWebsiteSettings?> GetUserWebsiteSettings(string userId);
        Task UpdateUserWebsiteSettings(UserWebsiteSettings userWebsiteSettings);
        Task CreateUserWebsiteSettings(string userId);
        Task<IEnumerable<EpisodeDownloadModel>?> GetDownloadEpisodes(string userId);
        Task RemoveFinishedDownload(string userId, string downloadId);
        Task InsertDownloadAsync(string usersId, string seriesId, List<EpisodeModel> episodes);
    }
}
