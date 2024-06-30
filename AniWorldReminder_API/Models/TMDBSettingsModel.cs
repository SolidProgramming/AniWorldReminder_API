using System.Text.Json.Serialization;

namespace AniWorldReminder_API.Models
{
    public class TMDBSettingsModel
    {
        [JsonPropertyName("AccessToken")]
        public string AccessToken { get; set; } = default!;
    }
}
