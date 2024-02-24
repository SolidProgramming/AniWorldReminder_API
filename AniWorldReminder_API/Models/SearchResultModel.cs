using System.Text.Json.Serialization;

namespace AniWorldReminder_API.Models
{
    public class SearchResultModel
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("link")]
        public string? Link { get; set; }

        public string? Path { get; set; }

        public string? CoverArtUrl { get; set; }
        public StreamingPortal StreamingPortal { get; set; }
    }
}
