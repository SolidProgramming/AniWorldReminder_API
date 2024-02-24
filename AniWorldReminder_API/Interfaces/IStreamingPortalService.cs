using System.Net;

namespace AniWorldReminder_API.Interfaces
{
    public interface IStreamingPortalService
    {
        string BaseUrl { get; init; }
        string Name { get; init; }
        StreamingPortal StreamingPortal { get; init; }
        Task<bool> InitAsync(WebProxy? proxy = null);
        Task<(bool success, List<SearchResultModel>? searchResults)> GetSeriesAsync(string seriesName, bool strictSearch = false);
        Task<SeriesInfoModel?> GetSeriesInfoAsync(string seriesPath);
        Task<SeasonModel?> GetSeasonEpisodesLinksAsync(string seriesName, SeasonModel season);
        HttpClient GetHttpClient();
    }
}
