﻿namespace AniWorldReminder_API.Interfaces
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
        Task<UserModel?> GetAuthUserByIdAsync(string userId);
        Task<SeriesModel?> GetSeriesAsync(string seriesPath);
        Task InsertSeries(string SeriesPath, IStreamingPortalService streamingPortalService);
        Task<UsersSeriesModel?> GetUsersSeriesAsync(string userId, string seriesPath);
        Task InsertUsersSeriesAsync(UsersSeriesModel usersSeries);
        Task DeleteUsersSeriesAsync(UsersSeriesModel usersSeries);
        Task<List<UsersSeriesModel>?> GetUsersSeriesAsync(string userId);
        Task<UserWebsiteSettings?> GetUserWebsiteSettings(string userId);
        Task UpdateUserWebsiteSettings(string userId, UserWebsiteSettings userWebsiteSettings);
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
        Task<DownloaderPreferencesModel?> GetDownloaderPreferences(string apiKey);
        Task InsertMovieDownloadAsync(AddMovieDownloadRequestModel download);
        Task<string?> CreateWatchlist(string watchlistName, string userId, List<SeriesModel> watchlist);
        Task<List<WatchlistModel>?> GetUserWatchlists(string userId);
    }
}
