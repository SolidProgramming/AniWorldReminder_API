using HtmlAgilityPack;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AniWorldReminder_API.Services
{
    public class AniWorldSTOService(ILogger<AniWorldSTOService> logger, Interfaces.IHttpClientFactory httpClientFactory, string baseUrl, string name, StreamingPortal streamingPortal, ITMDBService tmdbService)
        : IStreamingPortalService
    {
        private HttpClient? HttpClient;

        public string BaseUrl { get; init; } = baseUrl;
        public string Name { get; init; } = name;
        public StreamingPortal StreamingPortal { get; init; } = streamingPortal;
        private const string PopularSeriesUrl = "/beliebte-serien";
        private string PopularHtmlSearchQuery = "//div[@class='preview rows sevenCols']/div[@class='coverListItem']/a";

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

        public async Task<List<SearchResultModel>?> GetPopularAsync()
        {
            (bool reachable, string? html) = await StreamingPortalHelper.GetHosterReachableAsync(this);

            if (!reachable)
                return default;

            string popularUrl = BaseUrl;

            if (StreamingPortal == StreamingPortal.STO)
            {
                popularUrl += PopularSeriesUrl;
                PopularHtmlSearchQuery = "//div[@class='popular-page']/div[@class='mb-5'][2]/div[@class='row g-3']/div/a";
            }

            HttpResponseMessage? resp = await HttpClient.GetAsync(new Uri(popularUrl));

            if (!resp.IsSuccessStatusCode)
                return null;

            string content = await resp.Content.ReadAsStringAsync();

            HtmlDocument doc = new();
            doc.LoadHtml(content);

            List<HtmlNode>? popularSeriesNode = new HtmlNodeQueryBuilder()
               .Query(doc)
                   .GetNodesByQuery(PopularHtmlSearchQuery);

            if (popularSeriesNode is null || popularSeriesNode.Count == 0)
                return default;

            List<SearchResultModel>? popularSeries = [];

            Random rnd = new();

            foreach (HtmlNode node in popularSeriesNode.OrderBy(x => rnd.Next()).Take(5))
            {
                SearchResultModel searchResult = new();
                try
                {
                    searchResult.Link = node.Attributes["href"].Value;
                }
                catch (Exception)
                {
                    continue;
                }

                if (!searchResult.Link.StartsWith("/anime/stream") && !searchResult.Link.StartsWith("/serie"))
                    continue;

                switch (StreamingPortal)
                {
                    case StreamingPortal.AniWorld:
                        searchResult.Name = node.SelectSingleNode("h3").InnerText.HtmlDecode();
                        break;
                    case StreamingPortal.STO:
                        searchResult.Name = node.SelectSingleNode("picture/img").Attributes["alt"].Value.HtmlDecode();
                        break;
                    case StreamingPortal.MegaKino:
                    case StreamingPortal.Undefined:
                    default:
                        break;
                }

                searchResult.Path = searchResult.Link.Replace("/anime/stream", "");
                searchResult.StreamingPortal = StreamingPortal;

                html = await HttpClient.GetStringAsync($"{BaseUrl}{searchResult.Link}");

                HtmlDocument docSeries = new();
                docSeries.LoadHtml(html);

                if (StreamingPortal == StreamingPortal.STO)
                {
                    TMDBSearchTVByIdModel? tmdbSearchTV = await GetTMDBSearchTV(searchResult.Name);

                    if (tmdbSearchTV is not null && !string.IsNullOrEmpty(tmdbSearchTV.PosterPath))
                    {
                        searchResult.CoverArtUrl = $"https://image.tmdb.org/t/p/w300{tmdbSearchTV.PosterPath}";
                    }
                    else
                    {
                        string? coverArtUrl = GetCoverArtUrl(docSeries);

                        searchResult.CoverArtUrl = await GetCoverArtBase64(coverArtUrl);
                    }
                }
                else if (StreamingPortal == StreamingPortal.AniWorld)
                {
                    AniListSearchMediaResponseModel? aniListSearchMediaResponse = await GetAniListSearchMediaResponse(searchResult.Name);

                    Medium? medium = GetAniListSearchMedia(searchResult.Name, aniListSearchMediaResponse);

                    if (medium is not null)
                    {
                        searchResult.CoverArtUrl = medium.CoverImage?.Large;
                    }
                    else
                    {
                        string? coverArtUrl = GetCoverArtUrl(docSeries);

                        searchResult.CoverArtUrl = await GetCoverArtBase64(coverArtUrl);
                    }
                }

                popularSeries.Add(searchResult);
            }

            return popularSeries;
        }

        public async Task<List<SearchResultModel>?> GetMediaAsync(string seriesName, bool strictSearch = false)
        {
            (bool reachable, string? html) = await StreamingPortalHelper.GetHosterReachableAsync(this);

            if (!reachable)
                return default;


            if (seriesName.Contains("'"))
            {
                seriesName = seriesName.Split('\'')[0];

                if (string.IsNullOrWhiteSpace(seriesName))
                    return default;
            }

            HttpResponseMessage resp;

            switch (StreamingPortal)
            {
                case StreamingPortal.AniWorld:
                    StringContent postData = new($"keyword={seriesName.SearchSanitize()}", Encoding.UTF8, "application/x-www-form-urlencoded");
                    resp = await HttpClient.PostAsync(new Uri($"{BaseUrl}/ajax/search"), postData);
                    postData.Dispose();
                    break;
                case StreamingPortal.STO:
                    resp = await HttpClient.GetAsync(new Uri($"{BaseUrl}/suche?term={seriesName.SearchSanitize()}&tab=shows"));
                    break;
                case StreamingPortal.MegaKino:
                case StreamingPortal.Undefined:
                default:
                    return default;
            }

            string content = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(content))
                return default;

            List<SearchResultModel>? searchResults;

            switch (StreamingPortal)
            {
                case StreamingPortal.AniWorld:
                    content = content.StripHtmlTags();
                    searchResults = System.Text.Json.JsonSerializer.Deserialize<List<SearchResultModel>>(content);

                    if (!searchResults.Any(_ => _.Link.Contains("/stream")))
                        return default;

                    List<SearchResultModel>? filteredSearchResults = searchResults.Where(_ =>
                    !_.Link.StartsWith("/support") &&
                    _.Link.Contains("/stream") &&
                    !_.Link.Contains("staffel") &&
                    !_.Link.Contains("episode"))
                        .ToList();

                    if (strictSearch)
                    {
                        filteredSearchResults = filteredSearchResults.Where(_ => _.Name.HtmlDecode() == seriesName).ToList();
                    }

                    if (filteredSearchResults.Count == 0)
                        return default;

                    searchResults = filteredSearchResults;

                    break;
                case StreamingPortal.STO:
                    searchResults = ParseSTOSearchResults(content);
                    break;
                case StreamingPortal.MegaKino:
                case StreamingPortal.Undefined:
                default:
                    return default;
            }


            if (searchResults is null)
                return default;

            try
            {
                HtmlDocument doc = new();

                foreach (SearchResultModel result in searchResults)
                {
                    if (string.IsNullOrEmpty(result.Link))
                        continue;

                    result.Name = result.Name.HtmlDecode();
                    result.Description = result.Description.HtmlDecode().HtmlDecode();
                    result.StreamingPortal = StreamingPortal;
                    result.Path = result.Link.Replace("/anime/stream", "").Replace("/serie/stream", "");

                    html = await HttpClient.GetStringAsync($"{BaseUrl}{result.Link}");
                    doc.LoadHtml(html);

                    if (StreamingPortal == StreamingPortal.STO)
                    {
                        TMDBSearchTVByIdModel? tmdbSearchTV = await GetTMDBSearchTV(result.Name);

                        if (tmdbSearchTV is not null && !string.IsNullOrEmpty(tmdbSearchTV.PosterPath))
                        {
                            result.CoverArtUrl = $"https://image.tmdb.org/t/p/w300{tmdbSearchTV.PosterPath}";
                        }
                    }
                    else if (StreamingPortal == StreamingPortal.AniWorld)
                    {
                        AniListSearchMediaResponseModel? aniListSearchMediaResponse = await GetAniListSearchMediaResponse(result.Name);

                        Medium? medium = GetAniListSearchMedia(result.Name, aniListSearchMediaResponse);

                        if (medium is not null)
                        {
                            result.CoverArtUrl = medium.CoverImage?.Large;
                        }
                        else
                        {
                            string? coverArtUrl = GetCoverArtUrl(doc);

                            result.CoverArtUrl = await GetCoverArtBase64(coverArtUrl);
                        }
                    }
                }

                return searchResults;
            }
            catch (Exception)
            {
                return default;
            }
        }

        private List<SearchResultModel> ParseSTOSearchResults(string searchResultHtml)
        {
            HtmlDocument doc = new();
            doc.LoadHtml(searchResultHtml);

            List<HtmlNode>? searchResultNodes = new HtmlNodeQueryBuilder()
               .Query(doc)
                   .GetNodesByQuery("//a[@class='d-block show-cover']");

            List<SearchResultModel>? searchResults = [];

            foreach (var node in searchResultNodes.Distinct())
            {
                string link = node.Attributes["href"].Value;

                if (!link.StartsWith("/serie"))
                    continue;

                string name = node.SelectSingleNode("picture/img").Attributes["alt"].Value.HtmlDecode();
                string coverUrl = BaseUrl + node.SelectSingleNode("picture/img").Attributes["src"].Value;

                SearchResultModel searchResult = new()
                {
                    Name = name,
                    Link = link,
                    Description = string.Empty,
                    StreamingPortal = StreamingPortal,
                    Path = link.Replace("/serie/stream", ""),
                    CoverArtUrl = coverUrl
                };

                searchResults.Add(searchResult);
            }

            return searchResults;
        }

        public async Task<SeriesInfoModel?> GetMediaInfoAsync(string seriesPath, bool getMovieCoverArtUrl = false)
        {
            string seriesUrl, seasonNodeQuery, titleNodeQuery;

            switch (StreamingPortal)
            {
                case StreamingPortal.STO:
                    seriesUrl = $"{BaseUrl}/serie/{seriesPath}";
                    seasonNodeQuery = "//li[@class='nav-item me-1 mb-2'][last()]/a";
                    titleNodeQuery = "//li[@class='breadcrumb-item show-name']/a";
                    break;
                case StreamingPortal.AniWorld:
                    seriesUrl = $"{BaseUrl}/anime/stream/{seriesPath}";
                    seasonNodeQuery = "//div[@class='hosterSiteDirectNav']/ul/li[last()]";
                    titleNodeQuery = "//div[@class='series-title']/h1/span";
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

            List<HtmlNode>? seasonNode = new HtmlNodeQueryBuilder()
                .Query(doc)
                    .GetNodesByQuery(seasonNodeQuery);

            if (seasonNode is null || seasonNode.Count == 0)
                return default;

            int seasonCount = 0;

            switch (StreamingPortal)
            {
                case StreamingPortal.AniWorld:
                    if (!int.TryParse(seasonNode[0].InnerText, out seasonCount))
                        return default;
                    break;
                case StreamingPortal.STO:
                    if (!int.TryParse(seasonNode[0].Attributes["data-season-pill"].Value, out seasonCount))
                        return default;
                    break;
                case StreamingPortal.Undefined:
                case StreamingPortal.MegaKino:
                default:
                    break;
            }

            HtmlNode? titleNode = new HtmlNodeQueryBuilder()
                .Query(doc)
                    .GetNodesByQuery(titleNodeQuery)
                        .FirstOrDefault();

            if (titleNode is null)
                return default;

            string? seriesName = titleNode.InnerHtml.Trim().HtmlDecode().HtmlDecode();

            if (string.IsNullOrEmpty(seriesName))
                return default;

            SeriesInfoModel seriesInfo = new()
            {
                Name = seriesName,
                DirectLink = seriesUrl,
                Description = GetDescription(doc, StreamingPortal),
                SeasonCount = seasonCount,
                StreamingPortal = StreamingPortal,
                Seasons = await GetSeasonsAsync(seriesPath, seasonCount),
                Path = $"/{seriesPath.TrimStart('/')}"
            };

            if (StreamingPortal == StreamingPortal.STO)
            {
                seriesInfo.TMDBSearchTVById = await GetTMDBSearchTV(seriesName);

                if (seriesInfo.TMDBSearchTVById is not null && !string.IsNullOrEmpty(seriesInfo.TMDBSearchTVById.PosterPath))
                {
                    seriesInfo.CoverArtUrl = $"https://image.tmdb.org/t/p/w300{seriesInfo.TMDBSearchTVById.PosterPath}";
                }
                else
                {
                    string? coverArtUrl = GetCoverArtUrl(doc);

                    seriesInfo.CoverArtUrl = await GetCoverArtBase64(coverArtUrl);
                }
            }
            else if (StreamingPortal == StreamingPortal.AniWorld)
            {
                AniListSearchMediaResponseModel? aniListSearchMediaResponse = await GetAniListSearchMediaResponse(seriesName);

                seriesInfo.AniListSearchMedia = GetAniListSearchMedia(seriesName, aniListSearchMediaResponse);

                if (seriesInfo.AniListSearchMedia is not null)
                {
                    seriesInfo.CoverArtUrl = seriesInfo.AniListSearchMedia.CoverImage?.Large;
                }
                else
                {
                    string? coverArtUrl = GetCoverArtUrl(doc);

                    seriesInfo.CoverArtUrl = await GetCoverArtBase64(coverArtUrl);
                }
            }

            foreach (SeasonModel season in seriesInfo.Seasons)
            {
                List<EpisodeModel>? episodes = await GetSeasonEpisodesAsync(seriesPath, season.Id);

                if (episodes is null || episodes.Count == 0)
                    continue;

                season.Episodes = episodes;
            }

            return seriesInfo;
        }

        private async Task<AniListSearchMediaResponseModel?> GetAniListSearchMediaResponse(string seriesName)
        {
            string? query = AniListAPIQuery.GetQuery(AniListAPIQueryType.SearchMedia, seriesName);

            if (string.IsNullOrEmpty(query))
                return null;

            using StringContent postData = new(query, Encoding.UTF8, "application/json");
            HttpResponseMessage? respAniList = await HttpClient.PostAsync(new Uri(AniListAPIQuery.Uri), postData);

            if (!respAniList.IsSuccessStatusCode)
                return default;

            return await respAniList.Content.ReadFromJsonAsync<AniListSearchMediaResponseModel>();
        }

        private async Task<TMDBSearchTVByIdModel?> GetTMDBSearchTV(string seriesName)
        {
            TMDBSearchTVModel? searchTV = await tmdbService.SearchTVShow(seriesName.SearchSanitize());

            if (searchTV is not null && searchTV.Results is not null)
            {
                int? tmdbSeriesId = searchTV.Results.FirstOrDefault(_ => _.Name!.Contains(seriesName) || seriesName.Contains(_.Name))?.Id;

                if (tmdbSeriesId is not null && tmdbSeriesId > 0)
                {
                    return await tmdbService.SearchTVShowById(tmdbSeriesId);
                }
                else if (tmdbSeriesId is null && tmdbSeriesId is null && searchTV.Results.Count > 0)
                {
                    tmdbSeriesId = searchTV.Results.First()?.Id;
                    return await tmdbService.SearchTVShowById(tmdbSeriesId);
                }
            }

            return default;
        }

        private async Task<List<SeasonModel>?> GetSeasonsAsync(string seriesPath, int seasonCount)
        {
            List<SeasonModel> seasons = [];
            string episodesNodesQuery;

            for (int i = 0; i < seasonCount; i++)
            {
                string seasonUrl;

                switch (StreamingPortal)
                {
                    case StreamingPortal.STO:
                        seasonUrl = $"{BaseUrl}/serie/{seriesPath}/staffel-{i + 1}";
                        episodesNodesQuery = "//nav[@id='episode-nav']/ul/li[@class='nav-item me-1 mb-2']/a[@class=' alphabet-link nav-link  ']";
                        break;
                    case StreamingPortal.AniWorld:
                        seasonUrl = $"{BaseUrl}/anime/stream/{seriesPath}/staffel-{i + 1}";
                        episodesNodesQuery = $"//div[@class='hosterSiteDirectNav']/ul/li/a[@data-season-id=\"{i + 1}\"]";
                        break;
                    default:
                        return default;
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
                        .GetNodesByQuery(episodesNodesQuery);

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
            string seasonUrl, episodesNodesQuery, episodesNamesRegex;

            switch (StreamingPortal)
            {
                case StreamingPortal.STO:
                    seasonUrl = $"{BaseUrl}/serie/{seriesPath}/staffel-{season}";
                    episodesNodesQuery = "//tbody/tr[@class='episode-row  ']/td[@class='fw-medium episode-title-cell']";
                    episodesNamesRegex = "<(strong|span) class=\".*?episode-title-.*?\" title=\"(?'Name'.*?)\">.*?<\\/(strong|span)>";
                    break;
                case StreamingPortal.AniWorld:
                    seasonUrl = $"{BaseUrl}/anime/stream/{seriesPath}/staffel-{season}";
                    episodesNodesQuery = "//tbody/tr/td[@class=\"seasonEpisodeTitle\"]/a";
                    episodesNamesRegex = "<(strong|span)>(?'Name'.*?)</(strong|span)>";
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
                    .GetNodesByQuery(episodesNodesQuery);

            if (episodeNodes is null || episodeNodes.Count == 0)
                return null;

            List<EpisodeModel> episodes = [];

            int i = 1;

            foreach (HtmlNode episodeNode in episodeNodes)
            {
                try
                {
                    string episodeName = new Regex(episodesNamesRegex)
                    .Matches(episodeNode.InnerHtml)
                        .First(_ => !string.IsNullOrEmpty(_.Groups["Name"].Value))
                            .Groups["Name"]
                                .Value;

                    if (string.IsNullOrEmpty(episodeName))
                        continue;

                    episodes.Add(new EpisodeModel()
                    {
                        Name = episodeName.HtmlDecode(),
                        Episode = i,
                        Season = season,
                        Languages = GetEpisodeLanguages(i, html)
                    });
                }
                catch (Exception){ }

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
                        episodeUrl = $"{BaseUrl}/serie/{seriesPath}" + "/staffel-{0}/episode-{1}";
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
                    case "Englisch mit deutschem Untertitel":
                        availableLanguages |= Language.EngDubGerSub;
                        break;
                    default:
                        break;
                }
            }

            return availableLanguages;
        }

        private string? GetDescription(HtmlDocument document, StreamingPortal streamingPortal)
        {
            string descriptionNodeQuery;

            switch (streamingPortal)
            {
                case StreamingPortal.AniWorld:
                    descriptionNodeQuery = "seri_des";
                    break;
                case StreamingPortal.STO:
                    descriptionNodeQuery = "description-text";
                    break;
                case StreamingPortal.Undefined:
                case StreamingPortal.MegaKino:
                default:
                    return null;
            }

            HtmlNode? node = new HtmlNodeQueryBuilder()
               .Query(document)
                   .ByClass(descriptionNodeQuery)
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

        private async Task<string?> GetCoverArtBase64(string? url)
        {
            if (string.IsNullOrEmpty(url))
                return default;

            byte[]? imageBytes = await HttpClient.GetByteArrayAsync(url);

            if (imageBytes.Length > 0)
            {
                return "data:image/png;base64, " + Convert.ToBase64String(imageBytes);
            }

            return default;
        }

        private Medium? GetAniListSearchMedia(string mediaName, AniListSearchMediaResponseModel? aniListSearchMedia)
        {
            if (aniListSearchMedia is null ||
                aniListSearchMedia.Data is null ||
                aniListSearchMedia.Data.Page is null ||
                aniListSearchMedia.Data.Page.Media is null ||
                aniListSearchMedia.Data.Page.Media.Count == 0)
                return default;

            Medium? medium = aniListSearchMedia.Data.Page.Media.FirstOrDefault(_ => _.Title is not null &&
                    ((!string.IsNullOrEmpty(_.Title.UserPreferred) && _.Title.UserPreferred.Contains(mediaName)) ||
                    (!string.IsNullOrEmpty(_.Title.English) && _.Title.English.Contains(mediaName))));

            if (medium is null)
            {
                medium = aniListSearchMedia.Data.Page.Media.FirstOrDefault();
            }

            if (medium is not null)
            {
                medium.AverageScore = medium.AverageScore / 10;
            }

            return medium;
        }

        private static List<DirectViewLinkModel>? GetLanguageRedirectLinks(string html, string host)
        {
            HtmlDocument document = new();
            document.LoadHtml(html);

            List<HtmlNode> languageRedirectNodes = new HtmlNodeQueryBuilder()
                .Query(document)
                    .GetNodesByQuery("//div/a/i[contains(@title, 'Hoster')]");

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
