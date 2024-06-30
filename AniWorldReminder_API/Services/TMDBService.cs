
using Microsoft.AspNetCore.Components;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace AniWorldReminder_API.Services
{
    public class TMDBService : ITMDBService
    {
        private readonly ILogger<TMDBService>? Logger;
        private readonly Interfaces.IHttpClientFactory? HttpClientFactory;
        private HttpClient? HttpClient;
        private const string Version = "3";
        private readonly string Endpoint = $"https://api.themoviedb.org/{Version}/";
        private readonly TMDBSettingsModel? TMDBSettings;

        public TMDBService(ILogger<TMDBService> logger, Interfaces.IHttpClientFactory httpClientFactory)
        {
            Logger = logger;
            HttpClientFactory = httpClientFactory;

            HttpClient = HttpClientFactory.CreateHttpClient<TMDBService>();

            HttpClient.BaseAddress = new Uri(Endpoint);

            TMDBSettings = SettingsHelper.ReadSettings<TMDBSettingsModel>();
                       
            Logger.LogInformation($"{DateTime.Now} TMDB Service initialized");
        }

        public async Task<TMDBSearchTVModel?> SearchTVShow(string tvShowName)
        {
            const string searchEndpoint = "search/tv";

            Dictionary<string, string> queryData = new()
            {
                { "query", tvShowName },
                { "include_adult", "false" },
                { "language", "de-DE" },
                { "page", "1" }
            };

            return await GetAsync<TMDBSearchTVModel>(searchEndpoint, queryData);
        }
        public async Task<TMDBSearchTVByIdModel?> SearchTVShowById(int? tvShowId)
        {
            string tvEndpoint = $"tv/{tvShowId}";

            Dictionary<string, string> queryData = new()
            {
                { "language", "de-DE" },
            };

            return await GetAsync<TMDBSearchTVByIdModel>(tvEndpoint, queryData);
        }
        private async Task<T?> GetAsync<T>(string uri)
        {
            HttpRequestMessage request = new(HttpMethod.Get, uri);
            return await SendRequest<T>(request);
        }
        public async Task<T?> GetAsync<T>(string uri, Dictionary<string, string> queryData)
        {
            HttpRequestMessage request = new(HttpMethod.Get, new Uri(QueryHelpers.AddQueryString(HttpClient.BaseAddress + uri, queryData!)));
            return await SendRequest<T>(request);
        }
        public async Task<T?> GetAsync<T>(string uri, Dictionary<string, string> queryData, object body)
        {
            HttpRequestMessage request = new(HttpMethod.Get, new Uri(QueryHelpers.AddQueryString(HttpClient.BaseAddress + uri, queryData!)))
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            return await SendRequest<T>(request);
        }
        private async Task<bool> PostAsync(string uri, object value)
        {
            HttpRequestMessage request = new(HttpMethod.Post, uri)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            };
            return await SendRequest<bool>(request);
        }
        private async Task<T?> SendRequest<T>(HttpRequestMessage request)
        {
            if (HttpClient is null)
                return default;

            if (TMDBSettings is not null && !string.IsNullOrEmpty(TMDBSettings.AccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TMDBSettings.AccessToken);

            using HttpResponseMessage? response = await HttpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return default;

            if (!response.IsSuccessStatusCode)
                return default;

            if (typeof(T) == typeof(bool))
            {
                return (T)Convert.ChangeType(response.IsSuccessStatusCode, typeof(T));
            }

            if (response.Content.Headers.ContentLength == 0)
                return default;

            return await response.Content.ReadFromJsonAsync<T?>();
        }
    }
}
