using HtmlAgilityPack;
using System;
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
                HtmlNode? titleNode = mediaSearchResultsNodes[0].SelectNodes("//h3[@class='poster__title ws-nowrap']")[i];
                string title = titleNode.InnerText;

                HtmlNode? baseNode = mediaSearchResultsNodes[i];

                SearchResultModel searchResult = new()
                {
                    Title = title.HtmlDecode(),
                    StreamingPortal = StreamingPortal,
                    CoverArtUrl = GetCoverArtUrl(doc, i),
                    Link = baseNode.Attributes["href"].Value,
                    Path = GetLinkPath(baseNode.Attributes["href"].Value)
                };

                searchResult.CoverArtBase64 = await GetCoverArtBase64(searchResult.CoverArtUrl);

                searchResults.Add(searchResult);
            }


            return (true, searchResults);
        }

        private string GetLinkPath(string href)
        {
            return href.Split(new string[] { BaseUrl }, StringSplitOptions.None)[1].Replace("films", "").Replace("serials", "").Replace(".html", "");
        }

        public async Task<SeasonModel?> GetSeasonEpisodesLinksAsync(string seriesName, SeasonModel season)
        {
            throw new NotImplementedException();
        }

        public async Task<SeriesInfoModel?> GetMediaInfoAsync(string seriesPath, bool getMovieCoverArtUrl = false)
        {
            string seriesUrl = $"{BaseUrl}/films/{seriesPath}.html";
         
            HttpResponseMessage? resp = await HttpClient.GetAsync(new Uri(seriesUrl));

            if (!resp.IsSuccessStatusCode)
                return null;

            string content = await resp.Content.ReadAsStringAsync();

            HtmlDocument doc = new();
            doc.LoadHtml(content);

            HtmlNode? titleNode = new HtmlNodeQueryBuilder()
               .Query(doc)
                   .GetNodesByQuery("//h1[@itemprop='name']")
                       .FirstOrDefault();

            SeriesInfoModel seriesInfo = new()
            {
                Name = titleNode?.InnerHtml,
                DirectLink = seriesUrl,
                Description = GetDescription(doc),
                StreamingPortal = StreamingPortal,
                Path = $"/{seriesPath.TrimStart('/')}"
            };

            if (getMovieCoverArtUrl)
            {
                seriesInfo.CoverArtUrl = GetMovieCoverArtUrl(doc);
            }
            else
            {
                seriesInfo.CoverArtUrl = GetCoverArtUrl(doc, 0);
            }

            seriesInfo.CoverArtBase64 = await GetCoverArtBase64(seriesInfo.CoverArtUrl);

            return seriesInfo;
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

        private string? GetMovieCoverArtUrl(HtmlDocument document)
        {
            HtmlNode? node = new HtmlNodeQueryBuilder()
                .Query(document)
                    .GetNodesByQuery("//div[@class='pmovie__poster img-fit-cover']/img")
                        .FirstOrDefault();

            if (node is null)
                return null;

            return BaseUrl + node.Attributes["data-src"].Value;
        }

        private string? GetDescription(HtmlDocument document)
        {
            HtmlNode? node = new HtmlNodeQueryBuilder()
               .Query(document)
                   .GetNodesByQuery("//div[@itemprop='description']/p")
                    .FirstOrDefault();

            if (node is null)
                return null;
                       
            return node.InnerText;
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
