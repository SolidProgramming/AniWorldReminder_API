using System.Text.Json.Serialization;

namespace AniWorldReminder_API.Models
{
    public class SettingsModel
    {
        [JsonPropertyName("TelegramBot")]
        public TelegramBotSettingsModel TelegramBotSettings { get; set; } = default!;

        [JsonPropertyName("Database")]
        public DatabaseSettingsModel DatabaseSettings { get; set; } = default!;

        [JsonPropertyName("Proxy")]
        public ProxyAccountModel ProxySettings { get; set; } = default!;

        [JsonPropertyName(name: "AppSettings")]
        public AppSettingsModel AppSettings { get; set; } = default!;

        [JsonPropertyName(name: "Jwt")]
        public JwtSettingsModel JwtSettings { get; set; } = default!;

        [JsonPropertyName(name: "TMDB")]
        public TMDBSettingsModel TMDBSettings { get; set; } = default!;
    }
}
