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
        Task<UsersSeriesModel?> GetUsersSeriesAsync(string telegramChatId, string seriesName);
        Task InsertUsersSeriesAsync(UsersSeriesModel usersSeries);
        Task DeleteUsersSeriesAsync(UsersSeriesModel usersSeries);
    }
}
