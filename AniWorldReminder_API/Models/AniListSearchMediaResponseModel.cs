using System.Text.Json.Serialization;

namespace AniWorldReminder_API.Models
{
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class CoverImage
    {
        [JsonPropertyName("large")]
        public string? Large { get; set; }

        [JsonPropertyName("medium")]
        public string? Medium { get; set; }

        [JsonPropertyName("color")]
        public string? Color { get; set; }
    }

    public class Data
    {
        [JsonPropertyName("Page")]
        public Page? Page { get; set; }
    }

    public class EndDate
    {
        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("month")]
        public int? Month { get; set; }

        [JsonPropertyName("day")]
        public int? Day { get; set; }
    }

    public class Medium
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("title")]
        public Title? Title { get; set; }

        [JsonPropertyName("coverImage")]
        public CoverImage? CoverImage { get; set; }

        [JsonPropertyName("startDate")]
        public StartDate? StartDate { get; set; }

        [JsonPropertyName("endDate")]
        public EndDate? EndDate { get; set; }

        [JsonPropertyName("season")]
        public string? Season { get; set; }

        [JsonPropertyName("seasonYear")]
        public int? SeasonYear { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("format")]
        public string? Format { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("episodes")]
        public int? Episodes { get; set; }

        [JsonPropertyName("duration")]
        public int? Duration { get; set; }

        [JsonPropertyName("genres")]
        public List<string>? Genres { get; set; }

        [JsonPropertyName("isAdult")]
        public bool? IsAdult { get; set; }

        [JsonPropertyName("averageScore")]
        public decimal? AverageScore { get; set; }

        [JsonPropertyName("nextAiringEpisode")]
        public object? NextAiringEpisode { get; set; }
    }

    public class Page
    {
        [JsonPropertyName("pageInfo")]
        public PageInfo? PageInfo { get; set; }

        [JsonPropertyName("media")]
        public List<Medium>? Media { get; set; }
    }

    public class PageInfo
    {
        [JsonPropertyName("total")]
        public int? Total { get; set; }

        [JsonPropertyName("perPage")]
        public int? PerPage { get; set; }

        [JsonPropertyName("currentPage")]
        public int? CurrentPage { get; set; }

        [JsonPropertyName("lastPage")]
        public int? LastPage { get; set; }

        [JsonPropertyName("hasNextPage")]
        public bool? HasNextPage { get; set; }
    }

    public class AniListSearchMediaResponseModel
    {
        [JsonPropertyName("data")]
        public Data? Data { get; set; }
    }

    public class StartDate
    {
        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("month")]
        public int? Month { get; set; }

        [JsonPropertyName("day")]
        public int? Day { get; set; }
    }

    public class Title
    {
        [JsonPropertyName("english")]
        public string? English { get; set; }

        [JsonPropertyName("userPreferred")]
        public string? UserPreferred { get; set; }
    }

}
