using System.Net;

namespace AniWorldReminder_API.Interfaces
{
    public interface IStreamingPortalService
    {
        string BaseUrl { get; init; }
        string Name { get; init; }
        Task<bool> Init(WebProxy? proxy = null);
        Task<(bool success, List<SearchResultModel>? searchResults)> GetSeriesAsync(string seriesName, bool strictSearch = false);
        Task<SeriesInfoModel?> GetSeriesInfoAsync(string seriesName, StreamingPortal streamingPortal);
        HttpClient GetHttpClient();
    }
}
