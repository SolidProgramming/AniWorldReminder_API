﻿using System.Net;

namespace AniWorldReminder_API.Interfaces
{
    public interface IStreamingPortalService
    {
        string BaseUrl { get; init; }
        string Name { get; init; }
        StreamingPortal StreamingPortal { get; init; }
        Task<bool> InitAsync(WebProxy? proxy = null);
        Task<List<SearchResultModel>?> GetMediaAsync(string seriesName, bool strictSearch = false);
        Task<SeriesInfoModel?> GetMediaInfoAsync(string seriesPath, bool getMovieCoverArtUrl = false);
        Task<SeasonModel?> GetSeasonEpisodesLinksAsync(string seriesName, SeasonModel season);
        Task<List<SearchResultModel>?> GetPopularAsync();
        HttpClient? GetHttpClient();
    }
}
