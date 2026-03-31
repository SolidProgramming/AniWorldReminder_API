using HtmlAgilityPack;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AniWorldReminder_API.Services
{
    public class AniWorldService : StreamingPortalServiceBase<AniWorldService>
    {        
        private const string PopularHtmlSearchQuery = "//div[@class='preview rows sevenCols']/div[@class='coverListItem']/a";
        private const string DescriptionNodeQuery = "seri_des";
        private const string TitleNodeQuery = "//div[@class='series-title']/h1/span";
        private const string SeasonNodeQuery = "//div[@class='hosterSiteDirectNav']/ul/li[last()]";

        public AniWorldService(
            ILogger<AniWorldService> logger,
            Interfaces.IHttpClientFactory httpClientFactory,
            ITMDBService tmdbService)
            : base(logger, httpClientFactory, "https://aniworld.to", "AniWorld", StreamingPortal.AniWorld, tmdbService)
        {
        }

        public override async Task<List<SearchResultModel>?> GetPopularAsync()
        {
            (bool reachable, string? _) = await StreamingPortalHelper.GetHosterReachableAsync(this);

            if (!reachable)
                return null;

            HttpResponseMessage response = await HttpClient.GetAsync(new Uri(BaseUrl));

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

                if (string.IsNullOrEmpty(link) || !link.StartsWith("/anime/stream"))
                    continue;

                HtmlNode? titleNode = node.SelectSingleNode("h3");

                if (titleNode is null)
                    continue;

                SearchResultModel searchResult = new()
                {
                    Link = link,
                    Name = titleNode.InnerText.HtmlDecode(),
                    Path = link.Replace("/anime/stream", ""),
                    StreamingPortal = StreamingPortal
                };

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

            using StringContent postData = new($"keyword={seriesName.SearchSanitize()}", Encoding.UTF8, "application/x-www-form-urlencoded");
            HttpResponseMessage response = await HttpClient.PostAsync(new Uri($"{BaseUrl}/ajax/search"), postData);
            string content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(content))
                return null;

            List<SearchResultModel>? searchResults = JsonSerializer.Deserialize<List<SearchResultModel>>(content.StripHtmlTags());

            if (searchResults?.Any(_ => _.Link?.Contains("/stream") == true) != true)
                return null;

            List<SearchResultModel> filteredSearchResults = searchResults.Where(_ =>
                    !string.IsNullOrEmpty(_.Link) &&
                    !_.Link.StartsWith("/support") &&
                    _.Link.Contains("/stream") &&
                    !_.Link.Contains("staffel") &&
                    !_.Link.Contains("episode"))
                .ToList();

            if (strictSearch)
            {
                filteredSearchResults = filteredSearchResults
                    .Where(_ => !string.IsNullOrEmpty(_.Name) && _.Name.HtmlDecode() == seriesName)
                    .ToList();
            }

            if (filteredSearchResults.Count == 0)
                return null;

            try
            {
                foreach (SearchResultModel result in filteredSearchResults)
                {
                    if (string.IsNullOrEmpty(result.Link))
                        continue;

                    result.Name = result.Name?.HtmlDecode();
                    result.Description = result.Description?.HtmlDecode().HtmlDecode();
                    result.StreamingPortal = StreamingPortal;
                    result.Path = result.Link.Replace("/anime/stream", string.Empty);

                    await PopulateCoverArtAsync(result);
                }

                return filteredSearchResults;
            }
            catch (InvalidOperationException ex)
            {
                Logger.LogError(ex, "{Name} search result processing failed for {SeriesName}", Name, seriesName);
                return null;
            }
        }

        public override async Task<SeriesInfoModel?> GetMediaInfoAsync(string seriesPath, bool getMovieCoverArtUrl = false)
        {
            string seriesUrl = $"{BaseUrl}/anime/stream/{seriesPath}";
            HttpResponseMessage response = await HttpClient.GetAsync(new Uri(seriesUrl));

            if (!response.IsSuccessStatusCode)
                return null;

            string content = await response.Content.ReadAsStringAsync();

            HtmlDocument document = new();
            document.LoadHtml(content);

            List<HtmlNode>? seasonNodes = new HtmlNodeQueryBuilder()
                .Query(document)
                .GetNodesByQuery(SeasonNodeQuery);

            if (seasonNodes is not { Count: > 0 } || !int.TryParse(seasonNodes[0].InnerText, out int seasonCount))
                return null;

            HtmlNode? titleNode = new HtmlNodeQueryBuilder()
                .Query(document)
                .GetNodesByQuery(TitleNodeQuery)
                .FirstOrDefault();

            string? seriesName = titleNode?.InnerHtml.Trim().HtmlDecode().HtmlDecode();

            if (string.IsNullOrEmpty(seriesName))
                return null;

            List<SeasonModel>? seasons = await GetSeasonsAsync(seriesPath, seasonCount);

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
                Path = $"/{seriesPath.TrimStart('/')}"
            };

            AniListSearchMediaResponseModel? aniListSearchMediaResponse = await GetAniListSearchMediaResponseAsync(seriesName);
            seriesInfo.AniListSearchMedia = GetAniListSearchMedia(seriesName, aniListSearchMediaResponse);
            seriesInfo.CoverArtUrl = seriesInfo.AniListSearchMedia?.CoverImage?.Large ?? await GetCoverArtBase64Async(document);

            foreach (SeasonModel season in seriesInfo.Seasons)
            {
                List<EpisodeModel>? episodes = await GetSeasonEpisodesAsync(seriesPath, season.Id);

                if (episodes is { Count: > 0 })
                {
                    season.Episodes = episodes;
                }
            }

            return seriesInfo;
        }

        public override async Task<SeasonModel?> GetSeasonEpisodesLinksAsync(string seriesPath, SeasonModel season)
        {
            foreach (EpisodeModel episode in season.Episodes)
            {
                Uri uri = new($"{BaseUrl}/anime/stream/{seriesPath}/staffel-{episode.Season}/episode-{episode.Episode}");
                string html = await HttpClient.GetStringAsync(uri);
                episode.DirectViewLinks = GetLanguageRedirectLinks(html);
            }

            return season;
        }

        private async Task PopulateCoverArtAsync(SearchResultModel searchResult)
        {
            string html = await HttpClient.GetStringAsync($"{BaseUrl}{searchResult.Link}");

            HtmlDocument document = new();
            document.LoadHtml(html);

            if (string.IsNullOrEmpty(searchResult.Name))
                return;

            AniListSearchMediaResponseModel? aniListSearchMediaResponse = await GetAniListSearchMediaResponseAsync(searchResult.Name);
            Medium? medium = GetAniListSearchMedia(searchResult.Name, aniListSearchMediaResponse);

            searchResult.CoverArtUrl = medium?.CoverImage?.Large ?? await GetCoverArtBase64Async(document);
        }

        private async Task<List<SeasonModel>?> GetSeasonsAsync(string seriesPath, int seasonCount)
        {
            List<SeasonModel> seasons = [];

            for (int i = 1; i <= seasonCount; i++)
            {
                string seasonUrl = $"{BaseUrl}/anime/stream/{seriesPath}/staffel-{i}";
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
                    .GetNodesByQuery($"//div[@class='hosterSiteDirectNav']/ul/li/a[@data-season-id=\"{i}\"]");

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
            string seasonUrl = $"{BaseUrl}/anime/stream/{seriesPath}/staffel-{season}";
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
                .GetNodesByQuery("//tbody/tr/td[@class=\"seasonEpisodeTitle\"]/a");

            if (episodeNodes is null || episodeNodes.Count == 0)
                return null;

            List<EpisodeModel> episodes = [];
            Regex episodeNameRegex = new("<(strong|span)>(?'Name'.*?)</(strong|span)>");
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
                .GetNodesByQuery($"//tr[@data-episode-season-id=\"{episode}\"]/td/a/img");

            if (languages is null || languages.Count == 0)
                return Language.None;

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
                }
            }

            return availableLanguages;
        }

        protected override List<DirectViewLinkModel>? GetLanguageRedirectLinks(string html)
        {
            HtmlDocument document = new();
            document.LoadHtml(html);

            List<HtmlNode> languageRedirectNodes = new HtmlNodeQueryBuilder()
                .Query(document)
                .GetNodesByQuery("//div/a/i[contains(@title, 'Hoster')]");

            if (languageRedirectNodes.Count == 0)
                return null;

            List<DirectViewLinkModel> directViewLinks = [];

            AddRedirectLink(Language.GerDub);
            AddRedirectLink(Language.EngDub);
            AddRedirectLink(Language.EngSub);
            AddRedirectLink(Language.GerSub);

            return directViewLinks;

            void AddRedirectLink(Language language)
            {
                string? redirectLink = GetLanguageRedirectLink(language);

                if (string.IsNullOrEmpty(redirectLink))
                    return;

                directViewLinks.Add(new DirectViewLinkModel
                {
                    Language = language,
                    DirectLink = new Uri(new Uri(BaseUrl), redirectLink).ToString()
                });
            }

            string? GetLanguageRedirectLink(Language language)
            {
                List<HtmlNode> redirectNodes = languageRedirectNodes
                    .Where(_ => _.ParentNode.ParentNode.ParentNode.Attributes["data-lang-key"].Value == language.ToVOELanguageKey())
                    .ToList();

                foreach (HtmlNode node in redirectNodes)
                {
                    if (node.ParentNode?.ParentNode?.ParentNode is not HtmlNode parentNode ||
                        !parentNode.Attributes.Contains("data-link-target"))
                        continue;

                    return parentNode.Attributes["data-link-target"].Value;
                }

                return null;
            }
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
