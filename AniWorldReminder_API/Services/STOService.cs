using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace AniWorldReminder_API.Services
{
    public class STOService : StreamingPortalServiceBase<STOService>
    {
        private const string PopularSeriesUrl = "/beliebte-serien";
        private const string PopularHtmlSearchQuery = "//div[@class='popular-page']/div[@class='mb-5'][2]/div[@class='row g-3']/div/a";
        private const string DescriptionNodeQuery = "description-text";
        private const string TitleNodeQuery = "//li[@class='breadcrumb-item show-name']/a";
        private const string SeasonNodeQuery = "//li[@class='nav-item me-1 mb-2'][last()]/a";

        public STOService(
            ILogger<STOService> logger,
            Interfaces.IHttpClientFactory httpClientFactory,
            ITMDBService tmdbService)
            : base(logger, httpClientFactory, "https://s.to", "S.TO", StreamingPortal.STO, tmdbService)
        {
        }

        public override async Task<List<SearchResultModel>?> GetPopularAsync()
        {
            (bool reachable, string? _) = await StreamingPortalHelper.GetHosterReachableAsync(this);

            if (!reachable)
                return null;

            HttpResponseMessage response = await HttpClient.GetAsync(new Uri($"{BaseUrl}{PopularSeriesUrl}"));

            if (!response.IsSuccessStatusCode)
                return null;

            string content = await response.Content.ReadAsStringAsync();

            HtmlDocument document = new();
            document.LoadHtml(content);

            List<HtmlNode>? popularSeriesNodes = new HtmlNodeQueryBuilder()
                .Query(document)
                .GetNodesByQuery(PopularHtmlSearchQuery);

            if (popularSeriesNodes is not { Count: > 0 })
                return null;

            List<SearchResultModel> popularSeries = [];
            Random random = new();

            foreach (HtmlNode node in popularSeriesNodes.OrderBy(_ => random.Next()).Take(5))
            {
                string? link = node.Attributes["href"]?.Value;
                HtmlNode? imageNode = node.SelectSingleNode("picture/img");

                if (string.IsNullOrEmpty(link) || !link.StartsWith("/serie") || imageNode is null)
                    continue;

                SearchResultModel searchResult = new()
                {
                    Link = link,
                    Name = imageNode.Attributes["alt"]?.Value.HtmlDecode(),
                    Path = link.Replace("/serie/stream", string.Empty).Replace("/serie", string.Empty),
                    StreamingPortal = StreamingPortal
                };

                if (string.IsNullOrEmpty(searchResult.Name))
                    continue;

                await PopulateCoverArtAsync(searchResult);
                popularSeries.Add(searchResult);
            }

            return popularSeries;
        }

        public override async Task<List<SearchResultModel>?> GetMediaAsync(string seriesName, bool strictSearch = false)
        {
            (bool reachable, string? _) = await StreamingPortalHelper.GetHosterReachableAsync(this);

            if (!reachable)
                return null;

            if (seriesName.Contains("'"))
            {
                seriesName = seriesName.Split('\'')[0];

                if (string.IsNullOrWhiteSpace(seriesName))
                    return null;
            }

            HttpResponseMessage response = await HttpClient.GetAsync(new Uri($"{BaseUrl}/suche?term={seriesName.SearchSanitize()}&tab=shows"));
            string content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(content))
                return null;

            List<SearchResultModel> searchResults = ParseSearchResults(content);

            if (strictSearch)
            {
                searchResults = searchResults.Where(_ => _.Name == seriesName).ToList();
            }

            if (searchResults.Count == 0)
                return null;

            foreach (SearchResultModel result in searchResults)
            {
                if (string.IsNullOrEmpty(result.Link))
                    continue;

                result.Name = result.Name.HtmlDecode();
                result.Description = result.Description.HtmlDecode().HtmlDecode();
                result.StreamingPortal = StreamingPortal;
                result.Path = result.Link.Replace("/serie/stream", string.Empty).Replace("/serie", string.Empty);

                await PopulateCoverArtAsync(result);
            }

            return searchResults;
        }

        public override async Task<SeriesInfoModel?> GetMediaInfoAsync(string seriesPath, bool getMovieCoverArtUrl = false)
        {
            string normalizedSeriesPath = seriesPath.Trim('/');
            string seriesUrl = $"{BaseUrl}/serie/{normalizedSeriesPath}";
            HttpResponseMessage response = await HttpClient.GetAsync(new Uri(seriesUrl));

            if (!response.IsSuccessStatusCode)
                return null;

            string content = await response.Content.ReadAsStringAsync();

            HtmlDocument document = new();
            document.LoadHtml(content);

            List<HtmlNode>? seasonNodes = new HtmlNodeQueryBuilder()
                .Query(document)
                .GetNodesByQuery(SeasonNodeQuery);

            if (seasonNodes is not { Count: > 0 } || !int.TryParse(seasonNodes[0].Attributes["data-season-pill"]?.Value, out int seasonCount))
                return null;

            HtmlNode? titleNode = new HtmlNodeQueryBuilder()
                .Query(document)
                .GetNodesByQuery(TitleNodeQuery)
                .FirstOrDefault();

            string? seriesName = titleNode?.InnerHtml.Trim().HtmlDecode().HtmlDecode();

            if (string.IsNullOrEmpty(seriesName))
                return null;

            List<SeasonModel>? seasons = await GetSeasonsAsync(normalizedSeriesPath, seasonCount);

            if (seasons is null)
                return null;

            SeriesInfoModel seriesInfo = new()
            {
                Name = seriesName,
                DirectLink = seriesUrl,
                Description = GetDescription(document),
                SeasonCount = seasonCount,
                StreamingPortal = StreamingPortal,
                Seasons = seasons,
                Path = $"/{normalizedSeriesPath}"
            };

            seriesInfo.TMDBSearchTVById = await GetTMDBSearchTVAsync(seriesName);
            seriesInfo.CoverArtUrl = !string.IsNullOrEmpty(seriesInfo.TMDBSearchTVById?.PosterPath)
                ? $"https://image.tmdb.org/t/p/w300{seriesInfo.TMDBSearchTVById.PosterPath}"
                : await GetCoverArtBase64Async(document);

            foreach (SeasonModel season in seriesInfo.Seasons)
            {
                List<EpisodeModel>? episodes = await GetSeasonEpisodesAsync(normalizedSeriesPath, season.Id);

                if (episodes is { Count: > 0 })
                {
                    season.Episodes = episodes;
                }
            }

            return seriesInfo;
        }

        public override async Task<SeasonModel?> GetSeasonEpisodesLinksAsync(string seriesPath, SeasonModel season)
        {
            string normalizedSeriesPath = seriesPath.Trim('/');

            foreach (EpisodeModel episode in season.Episodes)
            {
                Uri uri = new($"{BaseUrl}/serie/{normalizedSeriesPath}/staffel-{episode.Season}/episode-{episode.Episode}");
                string html = await HttpClient.GetStringAsync(uri);
                episode.DirectViewLinks = GetLanguageRedirectLinks(html);
            }

            return season;
        }

        private List<SearchResultModel> ParseSearchResults(string searchResultHtml)
        {
            HtmlDocument document = new();
            document.LoadHtml(searchResultHtml);

            List<HtmlNode>? searchResultNodes = new HtmlNodeQueryBuilder()
                .Query(document)
                .GetNodesByQuery("//a[@class='d-block show-cover']");

            if (searchResultNodes is null || searchResultNodes.Count == 0)
                return [];

            List<SearchResultModel> searchResults = [];

            foreach (HtmlNode node in searchResultNodes.Distinct())
            {
                string? link = node.Attributes["href"]?.Value;
                HtmlNode? imageNode = node.SelectSingleNode("picture/img");

                if (string.IsNullOrEmpty(link) || !link.StartsWith("/serie") || imageNode is null)
                    continue;

                string? name = imageNode.Attributes["alt"]?.Value.HtmlDecode();
                string? coverPath = imageNode.Attributes["src"]?.Value;

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(coverPath))
                    continue;

                searchResults.Add(new SearchResultModel
                {
                    Name = name,
                    Link = link,
                    Description = string.Empty,
                    StreamingPortal = StreamingPortal,
                    Path = link.Replace("/serie/stream", string.Empty).Replace("/serie", string.Empty),
                    CoverArtUrl = BaseUrl + coverPath
                });
            }

            return searchResults;
        }

        private async Task PopulateCoverArtAsync(SearchResultModel searchResult)
        {
            string html = await HttpClient.GetStringAsync($"{BaseUrl}{searchResult.Link}");

            HtmlDocument document = new();
            document.LoadHtml(html);

            TMDBSearchTVByIdModel? tmdbSearchTV = await GetTMDBSearchTVAsync(searchResult.Name);
            searchResult.CoverArtUrl = !string.IsNullOrEmpty(tmdbSearchTV?.PosterPath)
                ? $"https://image.tmdb.org/t/p/w300{tmdbSearchTV.PosterPath}"
                : await GetCoverArtBase64Async(document);
        }

        private async Task<List<SeasonModel>?> GetSeasonsAsync(string seriesPath, int seasonCount)
        {
            List<SeasonModel> seasons = [];

            for (int i = 1; i <= seasonCount; i++)
            {
                string seasonUrl = $"{BaseUrl}/serie/{seriesPath}/staffel-{i}";
                HttpResponseMessage response = await HttpClient.GetAsync(new Uri(seasonUrl));

                if (!response.IsSuccessStatusCode)
                    continue;

                string html = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrEmpty(html))
                    continue;

                HtmlDocument document = new();
                document.LoadHtml(html);

                List<HtmlNode>? episodeNodes = new HtmlNodeQueryBuilder()
                    .Query(document)
                    .GetNodesByQuery("//nav[@id='episode-nav']/ul/li[@class='nav-item me-1 mb-2']/a[contains(@class,'alphabet-link nav-link')]");

                if (episodeNodes is null || episodeNodes.Count == 0)
                    continue;

                seasons.Add(new SeasonModel
                {
                    Id = i,
                    EpisodeCount = episodeNodes.Count
                });
            }

            return seasons;
        }

        private async Task<List<EpisodeModel>?> GetSeasonEpisodesAsync(string seriesPath, int season)
        {
            string seasonUrl = $"{BaseUrl}/serie/{seriesPath}/staffel-{season}";
            HttpResponseMessage response = await HttpClient.GetAsync(new Uri(seasonUrl));

            if (!response.IsSuccessStatusCode)
                return null;

            string html = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrEmpty(html))
                return null;

            HtmlDocument document = new();
            document.LoadHtml(html);

            List<HtmlNode>? episodeNodes = new HtmlNodeQueryBuilder()
                .Query(document)
                .GetNodesByQuery("//tbody/tr[@class='episode-row  ']/td[@class='fw-medium episode-title-cell']");

            if (episodeNodes is null || episodeNodes.Count == 0)
                return null;

            List<EpisodeModel> episodes = [];
            Regex episodeNameRegex = new("<(strong|span) class=\".*?episode-title-.*?\" title=\"(?'Name'.*?)\">.*?<\\/(strong|span)>");
            int episodeNumber = 1;

            foreach (HtmlNode episodeNode in episodeNodes)
            {
                Match? match = episodeNameRegex.Matches(episodeNode.InnerHtml)
                    .FirstOrDefault(_ => !string.IsNullOrEmpty(_.Groups["Name"].Value));

                string? episodeName = match?.Groups["Name"].Value;

                if (!string.IsNullOrEmpty(episodeName))
                {
                    episodes.Add(new EpisodeModel
                    {
                        Name = episodeName.HtmlDecode(),
                        Episode = episodeNumber,
                        Season = season,
                        Languages = GetEpisodeLanguages(episodeNumber, html)
                    });
                }

                episodeNumber++;
            }

            return episodes;
        }

        private Language GetEpisodeLanguages(int episode, string html)
        {
            HtmlDocument document = new();
            document.LoadHtml(html);

            List<HtmlNode>? languages = new HtmlNodeQueryBuilder()
                .Query(document)
                .GetNodesByQuery($"//tr[th[contains(@class,'episode-number-cell') and normalize-space()='{episode}']]/td[contains(@class,'episode-language-cell')]/*[contains(@class,'svg-flag-')]");

            if (languages is null || languages.Count == 0)
                return Language.None;

            Language availableLanguages = Language.None;

            foreach (HtmlNode node in languages)
            {
                string languageClass = node.Attributes["class"]?.Value ?? string.Empty;

                if (string.IsNullOrEmpty(languageClass))
                    continue;

                if (languageClass.Contains("svg-flag-german"))
                {
                    availableLanguages |= Language.GerDub;
                    continue;
                }

                if (languageClass.Contains("svg-flag-english"))
                {
                    availableLanguages |= Language.EngDub;
                }
            }

            return availableLanguages;
        }

        protected override List<DirectViewLinkModel>? GetLanguageRedirectLinks(string html)
        {
            HtmlDocument document = new();
            document.LoadHtml(html);

            List<HtmlNode>? redirectNodes = new HtmlNodeQueryBuilder()
                .Query(document)
                .GetNodesByQuery("//div[@id='episode-links']//button[@data-play-url]");

            if (redirectNodes is null || redirectNodes.Count == 0)
                return null;

            List<DirectViewLinkModel> directViewLinks = [];

            foreach (HtmlNode node in redirectNodes)
            {
                string redirectUrl = node.GetAttributeValue("data-play-url", string.Empty);
                string languageLabel = node.GetAttributeValue("data-language-label", string.Empty);

                if (string.IsNullOrEmpty(redirectUrl) || string.IsNullOrEmpty(languageLabel))
                    continue;

                Language? language = languageLabel switch
                {
                    "Deutsch" => Language.GerDub,
                    "Englisch" => Language.EngDub,
                    "Ger-Sub" => Language.GerSub,
                    _ => null
                };

                if (language is null || directViewLinks.Any(_ => _.Language == language))
                    continue;

                directViewLinks.Add(new DirectViewLinkModel
                {
                    Language = language.Value,
                    DirectLink = new Uri(new Uri(BaseUrl), redirectUrl).ToString()
                });
            }

            return directViewLinks.Count > 0 ? directViewLinks : null;
        }

        private static string? GetDescription(HtmlDocument document)
        {
            HtmlNode? node = new HtmlNodeQueryBuilder()
                .Query(document)
                .ByClass(DescriptionNodeQuery)
                .Result;

            if (node is null)
                return null;

            const string showMoreText = "mehr anzeigen";

            return node.InnerText.EndsWith(showMoreText)
                ? node.InnerText.Remove(node.InnerText.Length - showMoreText.Length).HtmlDecode().HtmlDecode()
                : node.InnerText.HtmlDecode().HtmlDecode();
        }
    }
}
