using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using System.Xml;
using HtmlAgilityPack;
using System.Web;
using AniWorldReminder_API.Models;
using Telegram.Bot.Types;
using Microsoft.Extensions.Hosting;

namespace AniWorldReminder_API.Services
{
    public class AniWorldSTOService(ILogger<AniWorldSTOService> logger, Interfaces.IHttpClientFactory httpClientFactory, string baseUrl, string name, StreamingPortal streamingPortal) : IStreamingPortalService
    {
        private HttpClient? HttpClient;

        public string BaseUrl { get; init; } = baseUrl;
        public string Name { get; init; } = name;
        public StreamingPortal StreamingPortal { get; init; } = streamingPortal;

        public async Task<bool> InitAsync(WebProxy? proxy = null)
        {
            if (proxy is null)
            {
                HttpClient = httpClientFactory.CreateHttpClient<AniWorldSTOService>();
            }
            else
            {
                HttpClient = httpClientFactory.CreateHttpClient<AniWorldSTOService>(proxy);
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

        public async Task<(bool success, List<SearchResultModel>? searchResults)> GetSeriesAsync(string seriesName, bool strictSearch = false)
        {
            (bool reachable, string? html) = await StreamingPortalHelper.GetHosterReachableAsync(this);

            if (!reachable)
                return (false, null);

            if (seriesName.Contains("'"))
            {
                seriesName = seriesName.Split('\'')[0];
            }

            using StringContent postData = new($"keyword={seriesName.SearchSanitize()}", Encoding.UTF8, "application/x-www-form-urlencoded");

            HttpResponseMessage? resp = await HttpClient.PostAsync(new Uri($"{BaseUrl}/ajax/search"), postData);

            if (!resp.IsSuccessStatusCode)
                return (false, null);

            string content = await resp.Content.ReadAsStringAsync();

            content = content.StripHtmlTags();
            try
            {
                List<SearchResultModel>? searchResults = System.Text.Json.JsonSerializer.Deserialize<List<SearchResultModel>>(content);

                if (searchResults is null)
                    return (false, null);

                if (!searchResults.Any(_ => _.Link.Contains("/stream")))
                    return (false, null);

                List<SearchResultModel>? filteredSearchResults = searchResults.Where(_ =>
                    _.Link.Contains("/stream") &&
                    _.Title.ToLower().Contains(seriesName.ToLower()) &&
                    !_.Link.Contains("staffel") &&
                    !_.Link.Contains("episode"))
                        .ToList();

                if (strictSearch)
                {
                    filteredSearchResults = filteredSearchResults.Where(_ => _.Title.HtmlDecode() == seriesName).ToList();
                }

                if (filteredSearchResults.Count == 0)
                    return (false, null);

                HtmlDocument doc = new();

                foreach (SearchResultModel result in filteredSearchResults)
                {
                    if (string.IsNullOrEmpty(result.Link))
                        continue;

                    result.Title = result.Title.HtmlDecode();
                    result.Description = result.Description.HtmlDecode();
                    result.StreamingPortal = StreamingPortal;

                    html = await HttpClient.GetStringAsync($"{BaseUrl}{result.Link}");
                    doc.LoadHtml(html);

                    result.CoverArtUrl = GetCoverArtUrl(doc)!;
                }

                return (true, filteredSearchResults);
            }
            catch (Exception)
            {
                return (false, null);
            }
        }

        public async Task<SeriesInfoModel?> GetSeriesInfoAsync(string seriesName)
        {
            string searchSeriesName = seriesName.UrlSanitize();

            string seriesUrl;

            switch (StreamingPortal)
            {
                case StreamingPortal.STO:
                    seriesUrl = $"{BaseUrl}/serie/stream/{searchSeriesName}";
                    break;
                case StreamingPortal.AniWorld:
                    seriesUrl = $"{BaseUrl}/anime/stream/{searchSeriesName}";
                    break;
                default:
                    return null;
            }

            HttpResponseMessage? resp = await HttpClient.GetAsync(new Uri(seriesUrl));

            if (!resp.IsSuccessStatusCode)
                return null;

            string content = await resp.Content.ReadAsStringAsync();

            HtmlDocument doc = new();
            doc.LoadHtml(content);

            List<HtmlNode>? seriesInfoNode = new HtmlNodeQueryBuilder()
                .Query(doc)
                    .GetNodesByQuery("//div[@class='hosterSiteDirectNav']/ul/li[last()]");

            if (seriesInfoNode is null || seriesInfoNode.Count == 0)
                return null;

            if (!int.TryParse(seriesInfoNode[0].InnerText, out int seasonCount))
                return null;


            SeriesInfoModel seriesInfo = new()
            {
                Name = seriesName,
                DirectLink = seriesUrl,
                Description = GetDescription(doc)?.HtmlDecode(),
                SeasonCount = seasonCount,
                CoverArtUrl = GetCoverArtUrl(doc),
                StreamingPortal = StreamingPortal,
                Seasons = await GetSeasonsAsync(searchSeriesName, seasonCount)
            };

            foreach (SeasonModel season in seriesInfo.Seasons)
            {
                List<EpisodeModel>? episodes = await GetSeasonEpisodesAsync(searchSeriesName, season.Id);

                if (episodes is null || episodes.Count == 0)
                    continue;

                season.Episodes = episodes;
            }

            return seriesInfo;
        }

        private async Task<List<SeasonModel>> GetSeasonsAsync(string searchSeriesName, int seasonCount)
        {
            List<SeasonModel> seasons = [];

            for (int i = 0; i < seasonCount; i++)
            {
                string seasonUrl;

                switch (StreamingPortal)
                {
                    case StreamingPortal.STO:
                        seasonUrl = $"{BaseUrl}/serie/stream/{searchSeriesName}/staffel-{i + 1}";
                        break;
                    case StreamingPortal.AniWorld:
                        seasonUrl = $"{BaseUrl}/anime/stream/{searchSeriesName}/staffel-{i + 1}";
                        break;
                    default:
                        return null;
                }

                HttpResponseMessage? resp = await HttpClient.GetAsync(new Uri(seasonUrl));

                if (!resp.IsSuccessStatusCode)
                    continue;

                string html = await resp.Content.ReadAsStringAsync();

                if (string.IsNullOrEmpty(html))
                    continue;

                HtmlDocument doc = new();
                doc.LoadHtml(html);

                List<HtmlNode>? episodeNodes = new HtmlNodeQueryBuilder()
                    .Query(doc)
                        .GetNodesByQuery($"//div[@class='hosterSiteDirectNav']/ul/li/a[@data-season-id=\"{i + 1}\"]");

                SeasonModel season = new()
                {
                    Id = i + 1,
                    EpisodeCount = episodeNodes.Count,
                };

                if (episodeNodes is null || episodeNodes.Count == 0)
                {
                    season.EpisodeCount = 0;
                    continue;
                }

                seasons.Add(season);
            }

            return seasons;
        }

        private async Task<List<EpisodeModel>?> GetSeasonEpisodesAsync(string seriesName, int season)
        {
            string seasonUrl, host;

            switch (StreamingPortal)
            {
                case StreamingPortal.STO:
                    seasonUrl = $"{BaseUrl}/serie/stream/{seriesName}/staffel-{season}";
                    host = "s.to";
                    break;
                case StreamingPortal.AniWorld:
                    seasonUrl = $"{BaseUrl}/anime/stream/{seriesName}/staffel-{season}";
                    host = "aniworld.to";
                    break;
                default:
                    return null;
            }

            HttpResponseMessage? resp = await HttpClient.GetAsync(new Uri(seasonUrl));

            if (!resp.IsSuccessStatusCode)
                return null;

            string html = await resp.Content.ReadAsStringAsync();

            if (string.IsNullOrEmpty(html))
                return null;

            HtmlDocument doc = new();
            doc.LoadHtml(html);

            List<HtmlNode>? episodeNodes = new HtmlNodeQueryBuilder()
                .Query(doc)
                    .GetNodesByQuery("//tbody/tr/td[@class=\"seasonEpisodeTitle\"]/a");

            if (episodeNodes is null || episodeNodes.Count == 0)
                return null;

            List<EpisodeModel> episodes = [];

            int i = 1;

            foreach (HtmlNode episodeNode in episodeNodes)
            {
                string episodeName = new Regex("<(strong|span)>(?'Name'.*?)</(strong|span)>")
                    .Matches(episodeNode.InnerHtml)
                        .First(_ => !string.IsNullOrEmpty(_.Groups["Name"].Value))
                            .Groups["Name"]
                                .Value;

                if (string.IsNullOrEmpty(episodeName))
                    continue;

                episodes.Add(new EpisodeModel()
                {
                    Name = episodeName,
                    Episode = i,
                    Season = season,
                    Languages = GetEpisodeLanguages(i, html)
                });

                i++;
            }

            return episodes;
        }

        public async Task<SeasonModel?> GetSeasonEpisodesLinks(string seriesName, SeasonModel season)
        {
            string host, episodeUrl = "";
            seriesName = seriesName.UrlSanitize();

            foreach (EpisodeModel episode in season.Episodes)
            {
                switch (StreamingPortal)
                {
                    case StreamingPortal.STO:
                        episodeUrl = $"{BaseUrl}/serie/stream/{seriesName}" + "/staffel-{0}/episode-{1}";
                        host = "s.to";
                        break;
                    case StreamingPortal.AniWorld:
                        episodeUrl = $"{BaseUrl}/anime/stream/{seriesName}" + "/staffel-{0}/episode-{1}";
                        host = "aniworld.to";
                        break;
                    default:
                        continue;
                }                

                Uri uri = new(string.Format(episodeUrl, episode.Season, episode.Episode));
                string res = await HttpClient.GetStringAsync(uri);

                Dictionary<Language, List<string>> languageRedirectLinks = GetLanguageRedirectLinks(res);

                if (languageRedirectLinks == null)
                    continue;

                string? browserUrl = null;

                foreach (KeyValuePair<Language, List<string>> kvp in languageRedirectLinks)
                {
                    browserUrl = "https://" + host + kvp.Value[0];

                    if (string.IsNullOrEmpty(browserUrl))
                        continue;

                    //m3u8Url = await GetEpisodeM3U8(browserUrl);
                }

                episode.DirectViewLink = browserUrl;
            }

            return season;
        }

        private Language GetEpisodeLanguages(int episode, string html)
        {
            HtmlDocument doc = new();
            doc.LoadHtml(html);

            List<HtmlNode>? languages = new HtmlNodeQueryBuilder()
                .Query(doc)
                    .GetNodesByQuery($"//tr[@data-episode-season-id=\"{episode}\"]/td/a/img");

            Language availableLanguages = Language.None;

            foreach (HtmlNode node in languages)
            {
                string language = node.Attributes["title"].Value;

                if (string.IsNullOrEmpty(language))
                    continue;

                switch (language)
                {
                    case "Deutsch/German":
                        availableLanguages |= Language.GerDub;
                        break;
                    case "Englisch":
                        availableLanguages |= Language.EngDub;
                        break;
                    case "Mit deutschem Untertitel":
                        availableLanguages |= Language.GerSub;
                        break;
                    default:
                        break;
                }
            }

            return availableLanguages;
        }

        private string? GetDescription(HtmlDocument document)
        {
            HtmlNode? node = new HtmlNodeQueryBuilder()
               .Query(document)
                   .ByClass("seri_des")
                   .Result;

            if (node is null)
                return null;

            string showMoreText = "mehr anzeigen";

            if (node.InnerText.EndsWith(showMoreText))
            {
                return node.InnerText.Remove(node.InnerText.Length - showMoreText.Length);
            }

            return node.InnerText;
        }

        private string? GetCoverArtUrl(HtmlDocument document)
        {
            HtmlNode? node = new HtmlNodeQueryBuilder()
                 .Query(document)
                     .GetNodesByQuery("//div[@class='seriesCoverBox']/img")
                        .FirstOrDefault();

            if (node is null)
                return null;

            return BaseUrl + node.Attributes["data-src"].Value;
        }

        private static Dictionary<Language, List<string>> GetLanguageRedirectLinks(string html)
        {
            Dictionary<Language, List<string>> languageRedirectLinks = [];

            HtmlDocument document = new();
            document.LoadHtml(html);

            List<HtmlNode> languageRedirectNodes = new HtmlNodeQueryBuilder()
                .Query(document)
                    .GetNodesByQuery("//div/a/i[@title='Hoster VOE']");

            if (languageRedirectNodes == null || languageRedirectNodes.Count == 0)
                return null;

            List<string> redirectLinks;


            redirectLinks = GetLanguageRedirectLinksNodes(Language.GerDub);

            if (redirectLinks.Count > 0)
            {
                languageRedirectLinks.Add(Language.GerDub, redirectLinks);
            }

            redirectLinks = GetLanguageRedirectLinksNodes(Language.EngDub);

            if (redirectLinks.Count > 0)
            {
                languageRedirectLinks.Add(Language.EngDub, redirectLinks);
            }

            redirectLinks = GetLanguageRedirectLinksNodes(Language.EngSub);

            if (redirectLinks.Count > 0)
            {
                languageRedirectLinks.Add(Language.EngSub, redirectLinks);
            }

            redirectLinks = GetLanguageRedirectLinksNodes(Language.GerSub);

            if (redirectLinks.Count > 0)
            {
                languageRedirectLinks.Add(Language.GerSub, redirectLinks);
            }

            return languageRedirectLinks;


            List<string> GetLanguageRedirectLinksNodes(Language language)
            {
                List<HtmlNode> redirectNodes = languageRedirectNodes.Where(_ => _.ParentNode.ParentNode.ParentNode.Attributes["data-lang-key"].Value == language.ToVOELanguageKey())
                    .ToList();

                List<string> filteredRedirectLinks = [];

                foreach (HtmlNode node in redirectNodes)
                {
                    if (node == null ||
                   node.ParentNode == null ||
                   node.ParentNode.ParentNode == null ||
                   node.ParentNode.ParentNode.ParentNode == null ||
                   !node.ParentNode.ParentNode.ParentNode.Attributes.Contains("data-link-target"))
                        continue;

                    filteredRedirectLinks.Add(node.ParentNode.ParentNode.ParentNode.Attributes["data-link-target"].Value);
                }

                return filteredRedirectLinks;
            }
        }

        public HttpClient GetHttpClient()
        {
            return HttpClient;
        }
    }
}
