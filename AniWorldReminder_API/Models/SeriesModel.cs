﻿namespace AniWorldReminder_API.Models
{
    public class SeriesModel
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? CoverArtUrl { get; set; }
        public string? Path { get; set; }
        public int SeasonCount { get; set; }
        public int EpisodeCount { get; set; }
        public StreamingPortal StreamingPortal { get; set; }
        public Language LanguageFlag { get; set; }
        public DateTime? Added { get; set; }
        public DateTime? Updated { get; set; }
    }
}
