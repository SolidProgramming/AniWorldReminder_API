using HtmlAgilityPack;
using System.Net;
using System.Text;

namespace AniWorldReminder_API.Services
{
    public abstract class StreamingPortalServiceBase<TService> : IStreamingPortalService
    {
        private HttpClient? httpClient;

        protected StreamingPortalServiceBase(
            ILogger logger,
            Interfaces.IHttpClientFactory httpClientFactory,
            string baseUrl,
            string name,
            StreamingPortal streamingPortal,
            ITMDBService tmdbService)
        {
            Logger = logger;
            HttpClientFactory = httpClientFactory;
            TmdbService = tmdbService;
            BaseUrl = baseUrl;
            Name = name;
            StreamingPortal = streamingPortal;
        }

        protected ILogger Logger { get; }
        protected Interfaces.IHttpClientFactory HttpClientFactory { get; }
        protected ITMDBService TmdbService { get; }
        protected HttpClient HttpClient => httpClient ?? throw new InvalidOperationException($"{Name} service is not initialized.");

        public string BaseUrl { get; init; }
        public string Name { get; init; }
        public StreamingPortal StreamingPortal { get; init; }

        public async Task<bool> InitAsync(WebProxy? proxy = null)
        {
            httpClient = proxy is null
                ? HttpClientFactory.CreateHttpClient<TService>()
                : HttpClientFactory.CreateHttpClient<TService>(proxy);

            (bool success, string? ipv4) = await HttpClient.GetIPv4();

            if (!success)
            {
                Logger.LogError($"{DateTime.Now} | {Name} Service unable to retrieve WAN IP");
            }
            else
            {
                Logger.LogInformation($"{DateTime.Now} | {Name} Service initialized with WAN IP {ipv4}");
            }

            return success;
        }

        public HttpClient? GetHttpClient()
        {
            return httpClient;
        }

        public abstract Task<List<SearchResultModel>?> GetMediaAsync(string seriesName, bool strictSearch = false);
        public abstract Task<SeriesInfoModel?> GetMediaInfoAsync(string seriesPath, bool getMovieCoverArtUrl = false);
        public abstract Task<SeasonModel?> GetSeasonEpisodesLinksAsync(string seriesPath, SeasonModel season);
        public abstract Task<List<SearchResultModel>?> GetPopularAsync();

        protected async Task<AniListSearchMediaResponseModel?> GetAniListSearchMediaResponseAsync(string seriesName)
        {
            string? query = AniListAPIQuery.GetQuery(AniListAPIQueryType.SearchMedia, seriesName);

            if (string.IsNullOrEmpty(query))
                return null;

            using StringContent postData = new(query, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await HttpClient.PostAsync(new Uri(AniListAPIQuery.Uri), postData);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<AniListSearchMediaResponseModel>();
        }

        protected async Task<TMDBSearchTVByIdModel?> GetTMDBSearchTVAsync(string seriesName)
        {
            TMDBSearchTVModel? searchTV = await TmdbService.SearchTVShow(seriesName.SearchSanitize());

            if (searchTV?.Results is not { Count: > 0 })
                return null;

            int? tmdbSeriesId = searchTV.Results
                .FirstOrDefault(_ => _.Name!.Contains(seriesName) || seriesName.Contains(_.Name))?
                .Id;

            if (tmdbSeriesId is > 0)
                return await TmdbService.SearchTVShowById(tmdbSeriesId);

            tmdbSeriesId = searchTV.Results.FirstOrDefault()?.Id;

            return tmdbSeriesId is > 0
                ? await TmdbService.SearchTVShowById(tmdbSeriesId)
                : null;
        }

        protected Medium? GetAniListSearchMedia(string mediaName, AniListSearchMediaResponseModel? aniListSearchMedia)
        {
            if (aniListSearchMedia?.Data?.Page?.Media is not { Count: > 0 })
                return null;

            Medium? medium = aniListSearchMedia.Data.Page.Media.FirstOrDefault(_ => _.Title is not null &&
                    ((!string.IsNullOrEmpty(_.Title.UserPreferred) && _.Title.UserPreferred.Contains(mediaName)) ||
                    (!string.IsNullOrEmpty(_.Title.English) && _.Title.English.Contains(mediaName))));

            medium ??= aniListSearchMedia.Data.Page.Media.FirstOrDefault();

            if (medium is not null)
            {
                medium.AverageScore /= 10;
            }

            return medium;
        }

        protected string? GetCoverArtUrl(HtmlDocument document)
        {
            HtmlNode? node = new HtmlNodeQueryBuilder()
                .Query(document)
                    .GetNodesByQuery("//div[@class='seriesCoverBox']/img")
                    .FirstOrDefault();

            if (node is null)
                return null;

            return BaseUrl + node.Attributes["data-src"].Value;
        }

        protected async Task<string?> GetCoverArtBase64Async(string? url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            byte[] imageBytes = await HttpClient.GetByteArrayAsync(url);

            return imageBytes.Length > 0
                ? "data:image/png;base64, " + Convert.ToBase64String(imageBytes)
                : null;
        }

        protected async Task<string?> GetCoverArtBase64Async(HtmlDocument document)
        {
            return await GetCoverArtBase64Async(GetCoverArtUrl(document));
        }

        protected string GetAbsoluteUrl(string redirectUrl)
        {
            if (Uri.TryCreate(redirectUrl, UriKind.Absolute, out Uri? absoluteUri))
                return absoluteUri.ToString();

            return new Uri(new Uri(BaseUrl), redirectUrl).ToString();
        }

        protected abstract List<DirectViewLinkModel>? GetLanguageRedirectLinks(string html);
    }
}
