using HtmlAgilityPack;
using MySqlX.XDevAPI.Common;
using System.Net;
using System.Text;

namespace AniWorldReminder_API.Services
{
    public class MegaKinoService(ILogger<MegaKinoService> logger, Interfaces.IHttpClientFactory httpClientFactory, string baseUrl, string name, StreamingPortal streamingPortal)
        : IStreamingPortalService
    {

        private HttpClient? HttpClient;

        public string BaseUrl { get; init; } = baseUrl;
        public string Name { get; init; } = name;
        public StreamingPortal StreamingPortal { get; init; } = streamingPortal;

        public async Task<bool> InitAsync(WebProxy? proxy = null)
        {
            if (proxy is null)
            {
                HttpClient = httpClientFactory.CreateHttpClient<MegaKinoService>();
            }
            else
            {
                HttpClient = httpClientFactory.CreateHttpClient<MegaKinoService>(proxy);
            }

            (bool success, string? ipv4) = await HttpClient.GetIPv4();

            if (!success)
            {
                logger.LogError($"{DateTime.Now} | {Name} Service unable to retrieve WAN IP");
            }
            else
            {
                logger.LogInformation($"{DateTime.Now} | {Name} Service initialized with WAN IP {ipv4}");
            }

            return success;
        }

        public HttpClient? GetHttpClient()
        {
            return HttpClient;
        }
        public async Task<(bool success, List<SearchResultModel>? searchResults)> GetMediaAsync(string seriesName, bool strictSearch = false)
        {
            (bool reachable, string? html) = await StreamingPortalHelper.GetHosterReachableAsync(this);

            if (!reachable)
                return (false, null);

            using StringContent postData = new($"keyword=do=search&subaction=search&search_start=0&full_search=0&result_from=1&story={seriesName.SearchSanitize()}", Encoding.UTF8, "application/x-www-form-urlencoded");

            HttpResponseMessage? resp = await HttpClient.PostAsync(new Uri($"{BaseUrl}/index.php?do=search"), postData);

            if (!resp.IsSuccessStatusCode)
                return (false, null);

            string content = await resp.Content.ReadAsStringAsync();

            HtmlDocument doc = new();
            doc.LoadHtml(content);

            List<HtmlNode>? mediaSearchResultsNodes = new HtmlNodeQueryBuilder()
                .Query(doc)
                    .GetNodesByQuery("//div[@id='dle-content']/a");

            List<SearchResultModel> searchResults = [];

            for (int i = 0; i < mediaSearchResultsNodes.Count - 1; i++)
            {
                string title = mediaSearchResultsNodes[0].SelectNodes("//h3[@class='poster__title ws-nowrap']")[i].InnerText;

                SearchResultModel searchResult = new()
                {
                    Title = title,
                    StreamingPortal = StreamingPortal,
                    CoverArtUrl = GetCoverArtUrl(doc, i)
                };

                searchResult.CoverArtBase64 = await GetCoverArtBase64(searchResult.CoverArtUrl);

                searchResults.Add(searchResult);
            }


            return (true, searchResults);
        }

        public async Task<SeasonModel?> GetSeasonEpisodesLinksAsync(string seriesName, SeasonModel season)
        {
            throw new NotImplementedException();
        }

        public Task<SeriesInfoModel?> GetMediaInfoAsync(string seriesPath)
        {
            throw new NotImplementedException();
        }

        private string? GetCoverArtUrl(HtmlDocument document, int index)
        {
            HtmlNode? node = new HtmlNodeQueryBuilder()
                 .Query(document)
                     .GetNodesByQuery("//div[@class='poster__img img-responsive img-responsive--portrait img-fit-cover']/img")[index];

            if (node is null)
                return null;

            return BaseUrl + node.Attributes["data-src"].Value;
        }

        private async Task<string?> GetCoverArtBase64(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                byte[]? imageBytes = await HttpClient.GetByteArrayAsync(url);

                if (imageBytes.Length > 0)
                {
                    return "data:image/png;base64, " + Convert.ToBase64String(imageBytes);
                }
            }
            return default;
        }
    }
}
