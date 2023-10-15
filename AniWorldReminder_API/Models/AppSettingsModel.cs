using System.Text.Json.Serialization;

namespace AniWorldReminder_API.Models
{
    public class AppSettingsModel
    {
        [JsonPropertyName("AddSwagger")]
        public bool AddSwagger { get; set; } = default!;
    }
}
