﻿using System.Text.Json.Serialization;

namespace AniWorldReminder_API.Models
{
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class Result
    {
        [JsonPropertyName("adult")]
        public bool? Adult { get; set; }

        [JsonPropertyName("backdrop_path")]
        public string? BackdropPath { get; set; }

        [JsonPropertyName("genre_ids")]
        public List<int?>? GenreIds { get; set; }

        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("origin_country")]
        public List<string>? OriginCountry { get; set; }

        [JsonPropertyName("original_language")]
        public string? OriginalLanguage { get; set; }

        [JsonPropertyName("original_name")]
        public string? OriginalName { get; set; }

        [JsonPropertyName("overview")]
        public string? Overview { get; set; }

        [JsonPropertyName("popularity")]
        public decimal? Popularity { get; set; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("vote_average")]
        public decimal? VoteAverage { get; set; }

        [JsonPropertyName("vote_count")]
        public int? VoteCount { get; set; }
    }

    public class TMDBSearchTVModel
    {
        [JsonPropertyName("page")]
        public int? Page { get; set; }

        [JsonPropertyName("results")]
        public List<Result>? Results { get; set; }

        [JsonPropertyName("total_pages")]
        public int? TotalPages { get; set; }

        [JsonPropertyName("total_results")]
        public int? TotalResults { get; set; }
    }


}
