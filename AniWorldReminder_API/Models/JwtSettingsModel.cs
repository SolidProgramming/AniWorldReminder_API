using System.Text.Json.Serialization;

namespace AniWorldReminder_API.Models
{
    public class JwtSettingsModel
    {
        [JsonPropertyName("Key")]
        public string Key { get; set; } = default!;

        [JsonPropertyName("Issuer")]
        public string? Issuer { get; set; } = default!;
    }
}
