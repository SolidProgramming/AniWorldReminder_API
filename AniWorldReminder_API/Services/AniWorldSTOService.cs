﻿using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using HtmlAgilityPack;

namespace AniWorldReminder_API.Services
{
    public class AniWorldSTOService(ILogger<AniWorldSTOService> logger, Interfaces.IHttpClientFactory httpClientFactory, string baseUrl, string name, StreamingPortal streamingPortal)
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

        public async Task<(bool success, List<SearchResultModel>? searchResults)> GetMediaAsync(string seriesName, bool strictSearch = false)
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
                    result.Description = result.Description.HtmlDecode().HtmlDecode();
                    result.StreamingPortal = StreamingPortal;
                    result.Path = result.Link.Replace("/anime/stream", "").Replace("/serie/stream", "");
                   
                    html = await HttpClient.GetStringAsync($"{BaseUrl}{result.Link}");
                    doc.LoadHtml(html);

                    result.CoverArtUrl = GetCoverArtUrl(doc)!;
                    result.CoverArtBase64 = await GetCoverArtBase64(result.CoverArtUrl);
                }

                return (true, filteredSearchResults);
            }
            catch (Exception)
            {
                return (false, null);
            }
        }

        public async Task<SeriesInfoModel?> GetMediaInfoAsync(string seriesPath, bool getMovieCoverArtUrl = false)
        {
            string seriesUrl;

            switch (StreamingPortal)
            {
                case StreamingPortal.STO:
                    seriesUrl = $"{BaseUrl}/serie/stream/{seriesPath}";
                    break;
                case StreamingPortal.AniWorld:
                    seriesUrl = $"{BaseUrl}/anime/stream/{seriesPath}";
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

            HtmlNode? titleNode = new HtmlNodeQueryBuilder()
                .Query(doc)
                    .GetNodesByQuery("//div[@class='series-title']/h1/span")
                        .FirstOrDefault();

            SeriesInfoModel seriesInfo = new()
            {
                Name = titleNode?.InnerHtml.HtmlDecode().HtmlDecode(),
                DirectLink = seriesUrl,
                Description = GetDescription(doc),
                SeasonCount = seasonCount,
                CoverArtUrl = GetCoverArtUrl(doc),
                StreamingPortal = StreamingPortal,
                Seasons = await GetSeasonsAsync(seriesPath, seasonCount),
                Path = $"/{seriesPath.TrimStart('/')}"
            };

            seriesInfo.CoverArtBase64 = await GetCoverArtBase64(seriesInfo.CoverArtUrl);

            foreach (SeasonModel season in seriesInfo.Seasons)
            {
                List<EpisodeModel>? episodes = await GetSeasonEpisodesAsync(seriesPath, season.Id);

                if (episodes is null || episodes.Count == 0)
                    continue;

                season.Episodes = episodes;
            }

            return seriesInfo;
        }

        private async Task<List<SeasonModel>> GetSeasonsAsync(string seriesPath, int seasonCount)
        {
            List<SeasonModel> seasons = [];

            for (int i = 0; i < seasonCount; i++)
            {
                string seasonUrl;

                switch (StreamingPortal)
                {
                    case StreamingPortal.STO:
                        seasonUrl = $"{BaseUrl}/serie/stream/{seriesPath}/staffel-{i + 1}";
                        break;
                    case StreamingPortal.AniWorld:
                        seasonUrl = $"{BaseUrl}/anime/stream/{seriesPath}/staffel-{i + 1}";
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

        private async Task<List<EpisodeModel>?> GetSeasonEpisodesAsync(string seriesPath, int season)
        {
            string seasonUrl, host;

            switch (StreamingPortal)
            {
                case StreamingPortal.STO:
                    seasonUrl = $"{BaseUrl}/serie/stream/{seriesPath}/staffel-{season}";
                    host = "s.to";
                    break;
                case StreamingPortal.AniWorld:
                    seasonUrl = $"{BaseUrl}/anime/stream/{seriesPath}/staffel-{season}";
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

        public async Task<SeasonModel?> GetSeasonEpisodesLinksAsync(string seriesPath, SeasonModel season)
        {
            string host;

            foreach (EpisodeModel episode in season.Episodes)
            {
                string episodeUrl;
                switch (StreamingPortal)
                {
                    case StreamingPortal.STO:
                        episodeUrl = $"{BaseUrl}/serie/stream/{seriesPath}" + "/staffel-{0}/episode-{1}";
                        host = "s.to";
                        break;
                    case StreamingPortal.AniWorld:
                        episodeUrl = $"{BaseUrl}/anime/stream/{seriesPath}" + "/staffel-{0}/episode-{1}";
                        host = "aniworld.to";
                        break;
                    default:
                        continue;
                }

                Uri uri = new(string.Format(episodeUrl, episode.Season, episode.Episode));
                string html = await HttpClient.GetStringAsync(uri);

                episode.DirectViewLinks = GetLanguageRedirectLinks(html, host);
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
                return node.InnerText.Remove(node.InnerText.Length - showMoreText.Length).HtmlDecode().HtmlDecode();
            }

            return node.InnerText.HtmlDecode().HtmlDecode();
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

        private static List<DirectViewLinkModel>? GetLanguageRedirectLinks(string html, string host)
        {
            HtmlDocument document = new();
            document.LoadHtml(html);

            List<HtmlNode> languageRedirectNodes = new HtmlNodeQueryBuilder()
                .Query(document)
                    .GetNodesByQuery("//div/a/i[@title='Hoster VOE']");

            if (languageRedirectNodes == null || languageRedirectNodes.Count == 0)
                return null;

            List<DirectViewLinkModel>? directViewLinks = [];
            string? redirectLink, directLink;

            redirectLink = GetLanguageRedirectLink(Language.GerDub);

            if (!string.IsNullOrEmpty(redirectLink))
            {
                directLink = $"https://" + host + redirectLink;
                directViewLinks.Add(new DirectViewLinkModel() { Language = Language.GerDub, DirectLink = directLink });
            }

            redirectLink = GetLanguageRedirectLink(Language.EngDub);

            if (!string.IsNullOrEmpty(redirectLink))
            {
                directLink = $"https://" + host + redirectLink;
                directViewLinks.Add(new DirectViewLinkModel() { Language = Language.EngDub, DirectLink = directLink });
            }

            redirectLink = GetLanguageRedirectLink(Language.EngSub);

            if (!string.IsNullOrEmpty(redirectLink))
            {
                directLink = $"https://" + host + redirectLink;
                directViewLinks.Add(new DirectViewLinkModel() { Language = Language.EngSub, DirectLink = directLink });
            }

            redirectLink = GetLanguageRedirectLink(Language.GerSub);

            if (!string.IsNullOrEmpty(redirectLink))
            {
                directLink = $"https://" + host + redirectLink;
                directViewLinks.Add(new DirectViewLinkModel() { Language = Language.GerSub, DirectLink = directLink });
            }

            return directViewLinks;

            string? GetLanguageRedirectLink(Language language)
            {
                List<HtmlNode> redirectNodes = languageRedirectNodes.Where(_ => _.ParentNode.ParentNode.ParentNode.Attributes["data-lang-key"].Value == language.ToVOELanguageKey())
                    .ToList();

                foreach (HtmlNode node in redirectNodes)
                {
                    if (node == null ||
                   node.ParentNode == null ||
                   node.ParentNode.ParentNode == null ||
                   node.ParentNode.ParentNode.ParentNode == null ||
                   !node.ParentNode.ParentNode.ParentNode.Attributes.Contains("data-link-target"))
                        continue;

                    return node.ParentNode.ParentNode.ParentNode.Attributes["data-link-target"].Value;
                }

                return null;
            }
        }

        public HttpClient? GetHttpClient()
        {
            return HttpClient;
        }
    }
}
