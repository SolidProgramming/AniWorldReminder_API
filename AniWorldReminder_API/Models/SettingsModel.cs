using System.Text.Json.Serialization;

namespace AniWorldReminder_API.Models
{
    public class SettingsModel
    {
        [JsonPropertyName("Database")]
        public DatabaseSettingsModel DatabaseSettings { get; set; } = default!;

        [JsonPropertyName("Proxy")]
        public ProxyAccountModel ProxySettings { get; set; } = default!;
    }
}
